using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using HomeShowing.Tests.Shared;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class BookingTests(ApiFixture api)
{
    [Fact]
    public async Task Twenty_parallel_bookings_of_one_slot_produce_one_success_and_nineteen_conflicts()
    {
        var buyers = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => api.NewBuyerAsync()));
        var listing = await ApiFixture.ListingIdAsync(buyers[0], "Highlands");
        var slot = await ApiFixture.FirstOpenSlotAsync(buyers[0], listing, skip: 5);

        using var gate = new SemaphoreSlim(0);
        var tasks = buyers.Select(async b =>
        {
            await gate.WaitAsync();
            return await b.PostAsJsonAsync("/showings", new { listingId = listing, slotStart = slot });
        }).ToList();
        gate.Release(20);
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(19, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        var conflict = await ApiFixture.ReadAsync(results.First(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(409, conflict["status"]!.GetValue<int>());
        Assert.NotEmpty(conflict["alternatives"]!.AsArray());

        var db = new MongoClient(api.ConnectionString).GetDatabase(api.Database);
        var active = await db.GetCollection<BsonDocument>("showings").CountDocumentsAsync(
            new BsonDocument { { "listingId", listing }, { "slotStartUtc", DateTime.Parse(slot).ToUniversalTime() }, { "activeSlotKey", new BsonDocument("$exists", true) } });
        Assert.Equal(1, active);
    }

    [Fact]
    public async Task Declining_frees_the_slot_and_the_lifecycle_is_enforced()
    {
        var buyer = await api.NewBuyerAsync();
        var agent = await api.LoginAsync("agent@acme.test");
        var otherAgent = await api.LoginAsync("agent2@acme.test");
        var listing = await ApiFixture.ListingIdAsync(buyer, "RiNo");
        var slot = await ApiFixture.FirstOpenSlotAsync(buyer, listing, skip: 2);

        var booked = await buyer.PostAsJsonAsync("/showings", new { listingId = listing, slotStart = slot });
        Assert.Equal(HttpStatusCode.Created, booked.StatusCode);
        var showing = await ApiFixture.ReadAsync(booked);
        var id = showing["id"]!.GetValue<string>();
        Assert.Equal("Requested", showing["status"]!.GetValue<string>());

        // Not this agent's listing (same brokerage): forbidden, and buyers cannot confirm.
        Assert.Equal(HttpStatusCode.Forbidden, (await otherAgent.PostAsync($"/showings/{id}/confirm", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await buyer.PostAsync($"/showings/{id}/confirm", null)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await agent.PostAsync($"/showings/{id}/decline", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await agent.PostAsync($"/showings/{id}/confirm", null)).StatusCode);

        // The slot is free again for anyone.
        var second = await api.NewBuyerAsync();
        var rebook = await second.PostAsJsonAsync("/showings", new { listingId = listing, slotStart = slot });
        Assert.Equal(HttpStatusCode.Created, rebook.StatusCode);
        var id2 = (await ApiFixture.ReadAsync(rebook))["id"]!.GetValue<string>();

        Assert.Equal(HttpStatusCode.OK, (await agent.PostAsync($"/showings/{id2}/confirm", null)).StatusCode);
        // Another buyer cannot see or cancel it.
        Assert.Equal(HttpStatusCode.NotFound, (await buyer.PostAsync($"/showings/{id2}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.PostAsync($"/showings/{id2}/cancel", null)).StatusCode);
        var mine = (await ApiFixture.ReadAsync(await second.GetAsync("/showings/mine"))).AsArray();
        Assert.Equal("Cancelled", mine.Single()!["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task Auto_confirm_listing_confirms_immediately_and_writes_outbox_rows_in_the_same_transaction()
    {
        var buyer = await api.NewBuyerAsync();
        var listing = await ApiFixture.ListingIdAsync(buyer, "Cherry Creek");
        var slot = await ApiFixture.FirstOpenSlotAsync(buyer, listing, skip: 4);
        var db = new MongoClient(api.ConnectionString).GetDatabase(api.Database);
        var outbox = db.GetCollection<BsonDocument>("outbox");
        var before = await outbox.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);

        var res = await buyer.PostAsJsonAsync("/showings", new { listingId = listing, slotStart = slot });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.Equal("Confirmed", (await ApiFixture.ReadAsync(res))["status"]!.GetValue<string>());
        Assert.Equal(before + 2, await outbox.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty)); // agent + buyer emails
    }

    [Fact]
    public async Task Times_off_the_grid_or_inside_the_lead_time_are_rejected_with_422()
    {
        var buyer = await api.NewBuyerAsync();
        var listing = await ApiFixture.ListingIdAsync(buyer, "LoDo");
        var slot = DateTime.Parse(await ApiFixture.FirstOpenSlotAsync(buyer, listing)).ToUniversalTime().AddMinutes(7).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var res = await buyer.PostAsJsonAsync("/showings", new { listingId = listing, slotStart = slot });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await buyer.PostAsJsonAsync("/showings", new { listingId = listing, slotStart = "next tuesday" })).StatusCode);
    }

    [Fact]
    public async Task Schema_setup_backfills_claims_for_active_showings_that_have_none()
    {
        var buyer = await api.NewBuyerAsync("northstar");
        var listing = await ApiFixture.ListingIdAsync(buyer, "Leslieville"); // 30 min slots, no buffer
        var slot = await ApiFixture.FirstOpenSlotAsync(buyer, listing, skip: 3);
        var res = await buyer.PostAsJsonAsync("/showings", new { listingId = listing, slotStart = slot });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var id = (await ApiFixture.ReadAsync(res))["id"]!.GetValue<string>();

        // Simulate a database written before claims existed: the showing is active but has no claim documents.
        var db = new MongoClient(api.ConnectionString).GetDatabase(api.Database);
        var claims = db.GetCollection<BsonDocument>("slotClaims");
        await claims.DeleteManyAsync(new BsonDocument("showingId", id));
        Assert.Equal(0, await claims.CountDocumentsAsync(new BsonDocument("showingId", id)));

        // Starting a host runs the schema setup (idempotent) against the same database.
        using var host = api.Factory.WithWebHostBuilder(_ => { });
        _ = host.Server;
        Assert.Equal(6, await claims.CountDocumentsAsync(new BsonDocument("showingId", id))); // 30 min / 5

        // The showing still behaves normally, and cancelling releases the backfilled claims like any others.
        var other = await api.NewBuyerAsync("northstar");
        Assert.Equal(HttpStatusCode.Conflict, (await other.PostAsJsonAsync("/showings", new { listingId = listing, slotStart = slot })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await buyer.PostAsync($"/showings/{id}/cancel", null)).StatusCode);
        Assert.Equal(0, await claims.CountDocumentsAsync(new BsonDocument("showingId", id)));
    }

    [Fact]
    public async Task Schema_validator_rejects_a_showing_that_is_active_without_a_slot_key()
    {
        var db = new MongoClient(api.ConnectionString).GetDatabase(api.Database);
        var ex = await Assert.ThrowsAsync<MongoWriteException>(() => db.GetCollection<BsonDocument>("showings").InsertOneAsync(new BsonDocument
        {
            { "tenantId", "t" }, { "listingId", "l" }, { "buyerId", "b" }, { "agentId", "a" },
            { "slotStartUtc", DateTime.UtcNow }, { "slotEndUtc", DateTime.UtcNow }, { "status", "Requested" }, { "createdAt", DateTime.UtcNow },
        }));
        Assert.Equal(ServerErrorCategory.Uncategorized, ex.WriteError.Category);
        Assert.Equal(121, ex.WriteError.Code); // DocumentValidationFailure
    }
}

[Collection(ApiCollection.Name)]
public class OverlapTests(ApiFixture api)
{
    [Fact]
    public async Task Changing_the_slot_length_cannot_create_an_overlapping_booking()
    {
        var agent = await api.LoginAsync("agent2@acme.test");
        var buyer1 = await api.NewBuyerAsync();
        var buyer2 = await api.NewBuyerAsync();
        var listing = await ApiFixture.ListingIdAsync(buyer1, "South Congress");
        var before = (await ApiFixture.ReadAsync(await agent.GetAsync($"/listings/{listing}")))["showingRules"]!;
        Assert.Equal((30, 10), (before["slotMinutes"]!.GetValue<int>(), before["bufferMinutes"]!.GetValue<int>()));

        // South Congress runs a 30 + 10 grid from windows that start on the hour (09:00, 12:00, 17:00 Central), so its
        // slots sit at :00, :40, :20, :00 and the second slot of any day is the one at :40 (Central offsets are whole
        // hours, so the UTC minute is the local minute). Book one of those.
        var open = (await ApiFixture.ReadAsync(await buyer1.GetAsync($"/listings/{listing}/slots")))["days"]!.AsArray()
            .SelectMany(d => d!["slots"]!.AsArray()).Select(s => s!["startUtc"]!.GetValue<string>()).ToList();
        var slot = open.First(s => DateTime.Parse(s).ToUniversalTime().Minute == 40);
        Assert.Equal(HttpStatusCode.Created, (await buyer1.PostAsJsonAsync("/showings", new { listingId = listing, slotStart = slot })).StatusCode);

        // The agent switches to 45 min slots with no buffer. The new grid (window start + k * 45) has a slot at :45 that
        // starts 5 minutes into the :40-:10 booking: a different slot key, so only the per bucket claims can reject it.
        var rules = JsonSerializer.Deserialize<Dictionary<string, object>>(before.ToJsonString())!;
        rules["slotMinutes"] = 45; rules["bufferMinutes"] = 0;
        Assert.Equal(HttpStatusCode.OK, (await agent.PutAsJsonAsync($"/listings/{listing}/showing-rules", rules)).StatusCode);

        var booked = DateTime.Parse(slot).ToUniversalTime();
        var straddling = booked.AddMinutes(5).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var res = await buyer2.PostAsJsonAsync("/showings", new { listingId = listing, slotStart = straddling });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var conflict = await ApiFixture.ReadAsync(res);
        Assert.NotEmpty(conflict["alternatives"]!.AsArray());
        Assert.Contains("just booked", conflict["title"]!.GetValue<string>());

        // And the straddling slot is not offered: no listed slot on the new grid overlaps the booking.
        var all = (await ApiFixture.ReadAsync(await buyer2.GetAsync($"/listings/{listing}/slots")))["days"]!.AsArray()
            .SelectMany(d => d!["slots"]!.AsArray()).Select(s => DateTime.Parse(s!["startUtc"]!.GetValue<string>()).ToUniversalTime()).ToList();
        Assert.DoesNotContain(all, s => s < booked.AddMinutes(30) && s.AddMinutes(45) > booked);
        Assert.Contains(all, s => s > booked); // the rest of the grid is still bookable

        await agent.PutAsJsonAsync($"/listings/{listing}/showing-rules", before);
    }

    [Fact]
    public async Task A_booked_slot_cannot_be_held()
    {
        var buyer1 = await api.NewBuyerAsync();
        var buyer2 = await api.NewBuyerAsync();
        var listing = await ApiFixture.ListingIdAsync(buyer1, "Hyde Park");
        var slot = await ApiFixture.FirstOpenSlotAsync(buyer1, listing, skip: 6);
        Assert.Equal(HttpStatusCode.Created, (await buyer1.PostAsJsonAsync("/showings", new { listingId = listing, slotStart = slot })).StatusCode);
        var hold = await buyer2.PostAsJsonAsync("/showings/holds", new { listingId = listing, slotStart = slot, timeZone = "America/Toronto" });
        Assert.Equal(HttpStatusCode.Conflict, hold.StatusCode);
        var alt = (await ApiFixture.ReadAsync(hold))["alternatives"]!.AsArray();
        Assert.NotEmpty(alt);
        Assert.Contains("ET", alt[0]!["listingLabel"]!.GetValue<string>() + alt[0]!["label"]!.GetValue<string>() + "ET"); // labels present
    }

    [Fact]
    public async Task Past_showings_cannot_be_transitioned_and_cancelling_releases_the_claims()
    {
        var buyer = await api.NewBuyerAsync();
        var agent = await api.LoginAsync("agent@northstar.test");
        var northBuyer = await api.NewBuyerAsync("northstar");
        var listing = await ApiFixture.ListingIdAsync(northBuyer, "The Annex");
        var slot = await ApiFixture.FirstOpenSlotAsync(northBuyer, listing, skip: 1);
        var res = await northBuyer.PostAsJsonAsync("/showings", new { listingId = listing, slotStart = slot });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var id = (await ApiFixture.ReadAsync(res))["id"]!.GetValue<string>();

        var db = new MongoClient(api.ConnectionString).GetDatabase(api.Database);
        var claims = db.GetCollection<BsonDocument>("slotClaims");
        Assert.Equal(12, await claims.CountDocumentsAsync(new BsonDocument("showingId", id))); // 60 min / 5

        Assert.Equal(HttpStatusCode.OK, (await northBuyer.PostAsync($"/showings/{id}/cancel", null)).StatusCode);
        Assert.Equal(0, await claims.CountDocumentsAsync(new BsonDocument("showingId", id)));

        // Age the showing into the past: the agent can no longer confirm it.
        var past = await northBuyer.PostAsJsonAsync("/showings", new { listingId = listing, slotStart = slot });
        var pastId = (await ApiFixture.ReadAsync(past))["id"]!.GetValue<string>();
        await db.GetCollection<BsonDocument>("showings").UpdateOneAsync(new BsonDocument("_id", ObjectId.Parse(pastId)),
            new BsonDocument("$set", new BsonDocument { { "slotStartUtc", DateTime.UtcNow.AddHours(-2) }, { "slotEndUtc", DateTime.UtcNow.AddHours(-1) } }));
        Assert.Equal(HttpStatusCode.Conflict, (await agent.PostAsync($"/showings/{pastId}/confirm", null)).StatusCode);
        _ = buyer;
    }
}
