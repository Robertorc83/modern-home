using HomeShowing.BuildingBlocks.Events;
using NodaTime;

namespace HomeShowing.Scheduling.Contracts;

public sealed record ShowingSnapshot(string ShowingId, string ListingId, string ListingAddress, string ListingTimeZone,
    string BuyerId, string BuyerName, string AgentId, string AgentName, Instant SlotStartUtc, string Status);

public sealed record ShowingRequested(string TenantId, ShowingSnapshot Showing, bool AutoConfirmed) : IDomainEvent;
public sealed record ShowingConfirmed(string TenantId, ShowingSnapshot Showing) : IDomainEvent;
public sealed record ShowingDeclined(string TenantId, ShowingSnapshot Showing) : IDomainEvent;
public sealed record ShowingCancelled(string TenantId, ShowingSnapshot Showing, string CancelledByRole) : IDomainEvent;
