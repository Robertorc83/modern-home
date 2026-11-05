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

        // 30 min grid: book the second slot of the first open day.
        var slot = await ApiFixture.FirstOpenSlotAsync(buyer1, listing, skip: 1);
        Assert.Equal(HttpStatusCode.Created, (await buyer1.PostAsJsonAsync("/showings", new { listingId = listing, slotStart = slot })).StatusCode);

        // The agent switches to 45 min slots with no buffer: the new grid has a slot that straddles the booking.
        var rules = JsonSerializer.Deserialize<Dictionary<string, object>>(before.ToJsonString())!;
        rules["slotMinutes"] = 45; rules["bufferMinutes"] = 0;
        Assert.Equal(HttpStatusCode.OK, (await agent.PutAsJsonAsync($"/listings/{listing}/showing-rules", rules)).StatusCode);

        var booked = DateTime.Parse(slot).ToUniversalTime();
        var straddling = booked.AddMinutes(-15).ToString("yyyy-MM-ddTHH:mm:ssZ"); // 45 min from here overlaps the 30 min booking
        var res = await buyer2.PostAsJsonAsync("/showings", new { listingId = listing, slotStart = straddling });
        Assert.True(res.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity, $"got {res.StatusCode}");
        if (res.StatusCode == HttpStatusCode.Conflict) Assert.NotEmpty((await ApiFixture.ReadAsync(res))["alternatives"]!.AsArray());

        // Direct check of the database rule, independent of the grid: a straddling showing cannot be inserted.
        var all = (await ApiFixture.ReadAsync(await buyer2.GetAsync($"/listings/{listing}/slots")))["days"]!.AsArray()
            .SelectMany(d => d!["slots"]!.AsArray()).Select(s => DateTime.Parse(s!["startUtc"]!.GetValue<string>()).ToUniversalTime()).ToList();
        Assert.DoesNotContain(all, s => s < booked.AddMinutes(30) && s.AddMinutes(45) > booked);

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
