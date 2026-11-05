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
