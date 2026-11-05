using HomeShowing.BuildingBlocks.Audit;
using HomeShowing.BuildingBlocks.Events;
using HomeShowing.BuildingBlocks.Persistence;
using HomeShowing.BuildingBlocks.Results;
using HomeShowing.BuildingBlocks.Tenancy;
using HomeShowing.Listings.Application;
using HomeShowing.Listings.Contracts;
using HomeShowing.Scheduling.Contracts;
using HomeShowing.Scheduling.Domain;
using HomeShowing.Scheduling.Infrastructure;
using NodaTime;
using NodaTime.Text;

namespace HomeShowing.Scheduling.Application;

/// <summary>Holds, bookings and the showing lifecycle. Shared by REST and MCP, so both get the same rules.</summary>
public sealed class BookingService(
    IListingCatalog listings, AvailabilityService availability, ShowingRepository showings, SlotHoldRepository holds,
    IUnitOfWork uow, IDomainEventDispatcher events, IAuditLog audit, IClock clock, ITenantContext tenant)
{
    public static readonly Duration HoldDuration = Duration.FromMinutes(5);

    public static bool TryParseInstant(string? s, out Instant instant)
    {
        var r = InstantPattern.ExtendedIso.Parse(s ?? "");
        if (!r.Success)
        {
            var o = OffsetDateTimePattern.ExtendedIso.Parse(s ?? "");
            instant = o.Success ? o.Value.ToInstant() : default;
            return o.Success;
        }
        instant = r.Value;
        return true;
    }

    public async Task<Result<HoldView>> HoldAsync(string listingId, string slotStart, CancellationToken ct)
    {
        if (!tenant.IsInRole(Roles.Buyer)) return Error.Forbidden("Only buyers hold slots.");
        var (listing, slot, err) = await ResolveSlotAsync(listingId, slotStart, ct);
        if (err is not null) return err;
        var key = Showing.SlotKey(listing!.Id, slot.Start);
        var now = clock.GetCurrentInstant();
        var hold = new SlotHold { ListingId = listing.Id, ActiveSlotKey = key, BuyerId = tenant.UserId, SlotStartUtc = slot.Start, SlotEndUtc = slot.End, ExpiresAt = now + HoldDuration };

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                await holds.AddAsync(hold, ct);
                await holds.ReleaseOthersAsync(tenant.UserId, key, ct);
                return new HoldView(hold.Id, hold.ListingId, hold.SlotStartUtc, hold.ExpiresAt);
            }
            catch (Exception ex) when (MongoErrors.IsDuplicateKey(ex))
            {
                var existing = await holds.ByKeyAsync(key, ct);
                if (existing is null) continue;                                    // it just expired; try again
                if (existing.BuyerId == tenant.UserId)
                {
                    var extended = await holds.ExtendAsync(existing.Id, now + HoldDuration, ct);
                    return new HoldView(extended.Id, extended.ListingId, extended.SlotStartUtc, extended.ExpiresAt);
                }
                if (existing.ExpiresAt <= now) { await holds.DeleteExpiredAsync(key, now, ct); continue; } // TTL monitor lags ~60 s
                return await ConflictAsync(listing, slot, "Another buyer is confirming this slot right now.", null, ct);
            }
        }
        return await ConflictAsync(listing, slot, "That slot is being held by another buyer.", null, ct);
    }

    public async Task<Result<bool>> ReleaseHoldAsync(string holdId, CancellationToken ct) =>
        await holds.DeleteAsync(holdId, tenant.UserId, ct) ? true : Error.NotFound("Hold");

    public async Task<Result<ShowingView>> BookAsync(BookRequest req, CancellationToken ct)
    {
        if (!tenant.IsInRole(Roles.Buyer)) return Error.Forbidden("Only buyers book showings.");
        var (listing, slot, err) = await ResolveSlotAsync(req.ListingId, req.SlotStart, ct);
        if (err is not null) return err;
        var key = Showing.SlotKey(listing!.Id, slot.Start);
        var now = clock.GetCurrentInstant();

        try
        {
            var showing = await uow.ExecuteAsync(async token =>
            {
                var hold = await holds.ByKeyAsync(key, token);
                if (hold is not null && hold.BuyerId != tenant.UserId && hold.ExpiresAt > now)
                    throw new AbortTransactionException("held");
                if (hold is not null) await holds.DeleteByKeyForBuyerAsync(key, hold.BuyerId, token); // booking converts the hold

                var s = new Showing
                {
                    ListingId = listing.Id, ListingAddress = listing.Address, ListingCity = listing.City, ListingTimeZone = listing.TimeZone,
                    BuyerId = tenant.UserId, BuyerName = tenant.UserName ?? "", AgentId = listing.OwnerUserId, AgentName = listing.OwnerName,
                    SlotStartUtc = slot.Start, SlotEndUtc = slot.End,
                    Status = listing.ShowingRules.AutoConfirm ? ShowingStatus.Confirmed : ShowingStatus.Requested,
                    ActiveSlotKey = key, CreatedAt = now, UpdatedAt = now, Source = req.Source ?? "web",
                };
                await showings.AddAsync(s, token); // the unique partial index decides who wins
                await events.DispatchAsync(new ShowingRequested(s.TenantId, Snapshot(s), listing.ShowingRules.AutoConfirm), token);
                await audit.RecordAsync("showing.requested", s.Id, token);
                return s;
            }, ct);
            return SlotFormat.View(showing);
        }
        catch (Exception ex) when (MongoErrors.IsDuplicateKey(ex) || ex is AbortTransactionException)
        {
            var msg = ex is AbortTransactionException ? "Another buyer is confirming this slot right now." : "That slot was just booked.";
            return await ConflictAsync(listing, slot, msg, req.TimeZone, ct);
        }
    }

    public Task<Result<ShowingView>> ConfirmAsync(string id, CancellationToken ct) =>
        TransitionAsync(id, [ShowingStatus.Requested], ShowingStatus.Confirmed, AgentCan, s => new ShowingConfirmed(s.TenantId, Snapshot(s)), ct);

    public Task<Result<ShowingView>> DeclineAsync(string id, CancellationToken ct) =>
        TransitionAsync(id, [ShowingStatus.Requested], ShowingStatus.Declined, AgentCan, s => new ShowingDeclined(s.TenantId, Snapshot(s)), ct);

    public Task<Result<ShowingView>> CancelAsync(string id, CancellationToken ct) =>
        TransitionAsync(id, [ShowingStatus.Requested, ShowingStatus.Confirmed], ShowingStatus.Cancelled,
            s => s.BuyerId == tenant.UserId || AgentCan(s), s => new ShowingCancelled(s.TenantId, Snapshot(s), tenant.Role), ct);

    public async Task<IReadOnlyList<ShowingView>> MineAsync(CancellationToken ct) =>
        (await showings.ForBuyerAsync(tenant.UserId, ct)).Select(SlotFormat.View).ToList();

    public async Task<Result<IReadOnlyList<ShowingView>>> InboxAsync(ShowingStatus? status, CancellationToken ct)
    {
        string? agentId = tenant.IsInRole(Roles.Agent) ? tenant.UserId : tenant.IsInRole(Roles.BrokerageAdmin) ? null : "";
        if (agentId == "") return Error.Forbidden("Only agents and admins have an inbox.");
        return (await showings.ForAgentAsync(agentId, status, ct)).Select(SlotFormat.View).ToList();
    }

    public async Task<Result<IReadOnlyList<ShowingView>>> ForListingAsync(string listingId, CancellationToken ct)
    {
        var listing = await listings.FindAsync(listingId, ct);
        if (listing is null) return Error.NotFound("Listing");
        if (!tenant.IsInRole(Roles.BrokerageAdmin) && listing.OwnerUserId != tenant.UserId) return Error.Forbidden("Not your listing.");
        return (await showings.ForListingAsync(listingId, clock.GetCurrentInstant() - Duration.FromDays(1), ct)).Select(SlotFormat.View).ToList();
    }

    private bool AgentCan(Showing s) => tenant.IsInRole(Roles.BrokerageAdmin) || (tenant.IsInRole(Roles.Agent) && s.AgentId == tenant.UserId);

    private async Task<Result<ShowingView>> TransitionAsync(string id, ShowingStatus[] from, ShowingStatus to,
        Func<Showing, bool> allowed, Func<Showing, IDomainEvent> evt, CancellationToken ct)
    {
        var current = await showings.GetByIdAsync(id, ct);
        if (current is null) return Error.NotFound("Showing");
        // A buyer must not learn about other buyers' showings: not-yours looks like not-found.
        if (!allowed(current)) return tenant.IsInRole(Roles.Buyer) ? Error.NotFound("Showing") : Error.Forbidden("This showing is not on your listing.");
        if (!from.Contains(current.Status)) return Error.Conflict($"Showing is already {current.Status}.");

        var updated = await uow.ExecuteAsync(async token =>
        {
            var s = await showings.TransitionAsync(id, from, to, clock.GetCurrentInstant(), token);
            if (s is null) return null;
            await DispatchAsync(evt(s), token);
            await audit.RecordAsync($"showing.{to.ToString().ToLowerInvariant()}", s.Id, token);
            return s;
        }, ct);
        return updated is null ? Error.Conflict("Showing changed while you were looking at it. Refresh and try again.") : SlotFormat.View(updated);
    }

    private Task DispatchAsync(IDomainEvent e, CancellationToken ct) => e switch
    {
        ShowingConfirmed c => events.DispatchAsync(c, ct),
        ShowingDeclined d => events.DispatchAsync(d, ct),
        ShowingCancelled x => events.DispatchAsync(x, ct),
        _ => Task.CompletedTask,
    };

    private async Task<(ListingDto? Listing, Slot Slot, Error? Error)> ResolveSlotAsync(string listingId, string slotStart, CancellationToken ct)
    {
        var listing = await listings.FindAsync(listingId ?? "", ct);
        if (listing is null || listing.Status != ListingStatus.Active) return (null, default, Error.NotFound("Listing"));
        if (!TryParseInstant(slotStart, out var start))
            return (null, default, Error.Validation("slotStart must be an ISO 8601 instant, e.g. 2026-11-07T16:00:00Z."));
        var zone = AvailabilityService.Zone(listing.TimeZone) ?? DateTimeZone.Utc;
        var grid = SlotGenerator.Generate(listing.ShowingRules, zone, clock.GetCurrentInstant());
        var slot = grid.FirstOrDefault(s => s.Start == start);
        if (slot == default)
            return (listing, default, Error.Unprocessable("Not an open slot",
                "That time is not one of this listing's showing slots (outside its windows, inside the lead time, or a blackout date)."));
        return (listing, slot, null);
    }

    private async Task<Error> ConflictAsync(ListingDto listing, Slot slot, string title, string? viewerZone, CancellationToken ct)
    {
        var open = await availability.OpenSlotsAsync(listing, ct);
        var zone = AvailabilityService.Zone(listing.TimeZone) ?? DateTimeZone.Utc;
        var viewer = AvailabilityService.Zone(viewerZone) ?? zone;
        var alternatives = Matcher.Nearest(open.Where(s => s.Start != slot.Start), [slot.ToInterval()], 3)
            .Select(s => SlotFormat.View(s, viewer, zone)).ToList();
        return Error.Conflict(title, alternatives.Count > 0 ? "Here are the nearest open slots." : "There are no other open slots right now.",
            new Dictionary<string, object?> { ["alternatives"] = alternatives });
    }

    private static ShowingSnapshot Snapshot(Showing s) =>
        new(s.Id, s.ListingId, s.ListingAddress, s.ListingTimeZone, s.BuyerId, s.BuyerName, s.AgentId, s.AgentName, s.SlotStartUtc, s.Status.ToString());
}
