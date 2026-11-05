using System.Text.Json;
using HomeShowing.BuildingBlocks.Tenancy;
using HomeShowing.Identity.Application;
using HomeShowing.Identity.Infrastructure;
using HomeShowing.Listings.Application;
using HomeShowing.Listings.Contracts;
using HomeShowing.Scheduling.Application;
using HomeShowing.Scheduling.Contracts;
using HomeShowing.Tenancy;
using NodaTime.Text;

namespace HomeShowing.Api.Seed;

/// <summary>
/// Two brokerages, demo users for every role, ten listings in three time zones with varied showing rules, and a
/// few showings so the inbox is not empty. Runs once on an empty database. Everything is created through the
/// same application services the API uses, so the seed also exercises the real rules.
/// </summary>
public sealed class DemoSeed(IServiceProvider root, ITenantDirectory tenants, CredentialStore credentials, ILogger<DemoSeed> log)
{
    public const string Password = "demo1234";

    public async Task RunAsync(CancellationToken ct)
    {
        if ((await tenants.ListAsync(ct)).Count > 0) return;
        log.LogInformation("Seeding demo data");

        var acme = await tenants.CreateAsync("acme", "Acme Realty", "America/Denver", ct);
        var north = await tenants.CreateAsync("northstar", "Northstar Homes", "America/Toronto", ct);

        var users = new Dictionary<string, (string Id, string TenantId, string Role, string Name)>();
        async Task User(TenantInfo t, string email, string role, string name, string tz)
        {
            using var scope = root.CreateScope();
            var u = scope.ServiceProvider.GetRequiredService<AuthService>().NewUser(t.Id, email, Password, role, name, tz);
            await credentials.InsertUserAsync(u, ct);
            users[email] = (u.Id, t.Id, role, name);
        }
        await User(acme, "admin@acme.test", Roles.BrokerageAdmin, "Morgan Shaw", "America/Denver");
        await User(acme, "agent@acme.test", Roles.Agent, "Alex Rivera", "America/Denver");
        await User(acme, "agent2@acme.test", Roles.Agent, "Jordan Lee", "America/Chicago");
        await User(acme, "buyer@acme.test", Roles.Buyer, "Bea Martin", "America/Denver");
        await User(acme, "buyer2@acme.test", Roles.Buyer, "Sam Patel", "America/Toronto");
        await User(north, "admin@northstar.test", Roles.BrokerageAdmin, "Priya Nair", "America/Toronto");
        await User(north, "agent@northstar.test", Roles.Agent, "Chris Tremblay", "America/Toronto");
        await User(north, "buyer@northstar.test", Roles.Buyer, "Noah Wilson", "America/Toronto");

        var listings = new Dictionary<string, string>();
        foreach (var (key, owner, input) in Listings())
        {
            using var scope = As(scope: root.CreateScope(), users[owner]);
            var r = await scope.ServiceProvider.GetRequiredService<ListingService>().CreateAsync(input, ct);
            if (!r.IsSuccess) throw new InvalidOperationException($"Seed listing {key} failed: {r.Error!.Detail}");
            listings[key] = r.Value.Id;
        }

        // A few showings so the agent inbox has something in it. Saturday morning in LoDo stays mostly open for the demo.
        await Book(users["buyer2@acme.test"], listings["highlands"], 1, ct);
        await Book(users["buyer2@acme.test"], listings["lodo"], 3, ct);
        await Book(users["buyer@acme.test"], listings["cherry-creek"], 2, ct);
        await Book(users["buyer@northstar.test"], listings["liberty"], 1, ct);
        // One confirmed showing for Alex (agent@acme.test) so the Confirmed tab is not empty: Sam books a later
        // Highlands slot and Alex confirms it, through the same service the inbox uses.
        if (await Book(users["buyer2@acme.test"], listings["highlands"], 4, ct) is { } toConfirm)
            await Confirm(users["agent@acme.test"], toConfirm, ct);
        log.LogInformation("Seeded {Tenants} brokerages, {Users} users, {Listings} listings", 2, users.Count, listings.Count);
    }

    /// <returns>The new showing's id, or null when the booking was skipped or refused.</returns>
    private async Task<string?> Book((string Id, string TenantId, string Role, string Name) buyer, string listingId, int nth, CancellationToken ct)
    {
        using var scope = As(root.CreateScope(), buyer);
        var sp = scope.ServiceProvider;
        var listing = await sp.GetRequiredService<IListingCatalog>().FindAsync(listingId, ct);
        var open = await sp.GetRequiredService<AvailabilityService>().OpenSlotsAsync(listing!, ct);
        if (open.Count <= nth) return null;
        var r = await sp.GetRequiredService<BookingService>().BookAsync(new BookRequest(listingId, InstantPattern.ExtendedIso.Format(open[nth].Start), "seed"), ct);
        if (!r.IsSuccess) { log.LogWarning("Seed booking failed: {Title}", r.Error!.Title); return null; }
        return r.Value.Id;
    }

    private async Task Confirm((string Id, string TenantId, string Role, string Name) agent, string showingId, CancellationToken ct)
    {
        using var scope = As(root.CreateScope(), agent);
        var r = await scope.ServiceProvider.GetRequiredService<BookingService>().ConfirmAsync(showingId, ct);
        if (!r.IsSuccess) log.LogWarning("Seed confirmation failed: {Title}", r.Error!.Title);
    }

    private static IServiceScope As(IServiceScope scope, (string Id, string TenantId, string Role, string Name) user)
    {
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(user.TenantId, user.Id, user.Role, user.Name);
        return scope;
    }

    private static ShowingRules Rules(int slot, int buffer, int lead, bool auto, params (string Days, string Start, string End)[] w) => new()
    {
        SlotMinutes = slot, BufferMinutes = buffer, LeadTimeHours = lead, HorizonDays = 21, AutoConfirm = auto,
        Weekly = w.SelectMany(x => x.Days.Split(',').Select(d => new WeeklyWindow { DayOfWeek = d.Trim(), Start = x.Start, End = x.End })).ToList(),
    };

    private const string Weekdays = "Monday,Tuesday,Wednesday,Thursday,Friday";

    private static Dictionary<string, JsonElement> Attrs(object o) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(o))!;

    private static readonly Dictionary<string, string[]> PhotoIds = new()
    {
        ["lodo"] = ["1545324418-cc1a3fa10c00", "1502672260266-1c1ef2d93688", "1484154218962-a197022b5858"],
        ["highlands"] = ["1568605114967-8130f3a36994", "1493809842364-78817add7ffb", "1600607687939-ce8a6c25118c"],
        ["rino"] = ["1600596542815-ffad4c1539a9", "1522708323590-d24dbb6b0267"],
        ["cherry-creek"] = ["1560448204-e02f11c3d0e2", "1554995207-c18c203602cb"],
        ["wash-park"] = ["1512917774080-9991f1c4c750", "1583608205776-bfd35f0d9f83", "1600047509807-ba8f99d2cdde"],
        ["soco"] = ["1600585154340-be6161a56a0c", "1502672260266-1c1ef2d93688"],
        ["hyde-park"] = ["1570129477492-45c003edd2be", "1493809842364-78817add7ffb"],
        ["liberty"] = ["1522708323590-d24dbb6b0267", "1484154218962-a197022b5858"],
        ["annex"] = ["1564013799919-ab600027ffc6", "1600607687939-ce8a6c25118c", "1583608205776-bfd35f0d9f83"],
        ["leslieville"] = ["1580587771525-78b9dba3b914", "1554995207-c18c203602cb"],
    };

    /// <summary>Demo photos (Unsplash). Real uploads go to S3 through presigned URLs; see docs/aws-deployment.md.</summary>
    private static List<string> Photos(string key) =>
        PhotoIds[key].Select(id => $"https://images.unsplash.com/photo-{id}?w=1200&q=70&auto=format&fit=crop").ToList();

    private static IEnumerable<(string Key, string Owner, ListingInput Input)> Listings() =>
        RawListings().Select(x => { x.Input.Photos = Photos(x.Key); return x; });

    private static IEnumerable<(string Key, string Owner, ListingInput Input)> RawListings()
    {
        ListingInput L(string address, string city, string hood, decimal price, int beds, decimal baths, PropertyType type, object attrs,
            string tz, double lat, double lng, ShowingRules rules, string description) => new()
        {
            Address = address, City = city, Neighborhood = hood, Price = price, Beds = beds, Baths = baths, PropertyType = type,
            Attributes = Attrs(attrs), TimeZone = tz, Location = new GeoPoint(lat, lng), ShowingRules = rules, Description = description,
            Status = ListingStatus.Active,
        };

        yield return ("lodo", "agent@acme.test", L("1420 Larimer St #5B", "Denver", "LoDo", 585_000, 2, 2, PropertyType.Condo,
            new { hoaMonthly = 450, floor = 5, parking = "1 garage space" }, "America/Denver", 39.7486, -104.9990,
            Rules(30, 15, 12, false, ("Saturday", "09:00", "12:00"), ("Sunday", "13:00", "16:00"), ("Wednesday", "17:30", "19:30")),
            "Corner loft above Larimer Square with 14 ft ceilings and a south facing balcony."));
        yield return ("highlands", "agent@acme.test", L("2815 W 32nd Ave", "Denver", "Highlands", 849_000, 4, 3, PropertyType.House,
            new { lotSizeSqft = 6250, yearBuilt = 1912 }, "America/Denver", 39.7620, -105.0240,
            Rules(45, 15, 24, false, (Weekdays, "17:30", "19:30"), ("Saturday", "10:00", "14:00")),
            "Restored Victorian with a detached garage and a big backyard, two blocks from the 32nd Ave shops."));
        yield return ("rino", "agent@acme.test", L("3301 Walnut St Unit 4", "Denver", "RiNo", 615_000, 3, 2.5m, PropertyType.Townhouse,
            new { hoaMonthly = 210, rooftopDeck = true }, "America/Denver", 39.7672, -104.9787,
            Rules(30, 0, 4, false, ("Tuesday,Thursday", "18:00", "20:00"), ("Saturday", "09:00", "12:00")),
            "New build townhome with a rooftop deck and mountain views."));
        yield return ("cherry-creek", "agent2@acme.test", L("250 Columbine St #310", "Denver", "Cherry Creek", 499_000, 1, 1, PropertyType.Condo,
            new { hoaMonthly = 380, floor = 3 }, "America/Denver", 39.7186, -104.9547,
            Rules(20, 10, 2, true, ("Saturday", "08:00", "11:00"), ("Sunday", "09:00", "12:00"), (Weekdays, "12:00", "13:00")),
            "Walk to Cherry Creek North. Showings are confirmed automatically."));
        var washPark = Rules(60, 0, 24, false, ("Saturday", "11:00", "15:00"), ("Sunday", "11:00", "15:00"));
        washPark.BlackoutDates = [NextSaturday().ToString("yyyy-MM-dd", null)];
        yield return ("wash-park", "agent2@acme.test", L("1155 S Gaylord St", "Denver", "Wash Park", 1_150_000, 5, 4, PropertyType.House,
            new { lotSizeSqft = 7800, yearBuilt = 1926 }, "America/Denver", 39.6950, -104.9617, washPark,
            "Tudor two blocks from the park. Sellers are away next Saturday, so no showings that day."));
        yield return ("soco", "agent2@acme.test", L("1805 S Congress Ave #12", "Austin", "South Congress", 575_000, 3, 2.5m, PropertyType.Townhouse,
            new { hoaMonthly = 260 }, "America/Chicago", 30.2470, -97.7505,
            Rules(30, 10, 12, false, ("Saturday", "09:00", "12:00"), ("Sunday", "12:00", "15:00"), ("Thursday", "17:00", "19:00")),
            "SoCo townhome, walk to everything. Listing is in Central time."));
        yield return ("hyde-park", "agent2@acme.test", L("4407 Avenue C", "Austin", "Hyde Park", 725_000, 3, 2, PropertyType.House,
            new { lotSizeSqft = 5400, yearBuilt = 1938 }, "America/Chicago", 30.3071, -97.7285,
            Rules(45, 15, 24, true, (Weekdays, "16:00", "19:00"), ("Saturday", "10:00", "13:00")),
            "Bungalow with original hardwood floors near UT."));
        yield return ("liberty", "agent@northstar.test", L("60 Atlantic Ave #1204", "Toronto", "Liberty Village", 689_000, 2, 1, PropertyType.Condo,
            new { hoaMonthly = 610, floor = 12 }, "America/Toronto", 43.6386, -79.4189,
            Rules(30, 15, 12, false, ("Saturday", "10:00", "13:00"), (Weekdays, "18:00", "20:00")),
            "Two bed corner unit with a lake view."));
        yield return ("annex", "agent@northstar.test", L("112 Brunswick Ave", "Toronto", "The Annex", 1_640_000, 4, 3, PropertyType.House,
            new { lotSizeSqft = 2900, yearBuilt = 1895 }, "America/Toronto", 43.6620, -79.4060,
            Rules(60, 0, 24, false, ("Saturday,Sunday", "13:00", "17:00")),
            "Bay and gable semi on a tree lined street."));
        yield return ("leslieville", "agent@northstar.test", L("23 Curzon St", "Toronto", "Leslieville", 1_049_000, 3, 2, PropertyType.Townhouse,
            new { hoaMonthly = 0, parking = "laneway" }, "America/Toronto", 43.6636, -79.3277,
            Rules(30, 0, 6, true, ("Saturday", "09:00", "12:00"), ("Tuesday,Thursday", "17:30", "19:30")),
            "Freehold townhouse with laneway parking."));
    }

    private static NodaTime.LocalDate NextSaturday()
    {
        var today = NodaTime.SystemClock.Instance.GetCurrentInstant().InZone(NodaTime.DateTimeZoneProviders.Tzdb["America/Denver"]).Date;
        return today.Next(NodaTime.IsoDayOfWeek.Saturday);
    }
}
