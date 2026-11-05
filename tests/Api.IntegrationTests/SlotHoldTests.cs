using System.Net;
using System.Net.Http.Json;
using HomeShowing.Tests.Shared;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class SlotHoldTests(ApiFixture api)
{
    [Fact]
    public async Task A_second_buyer_cannot_hold_or_book_a_held_slot()
    {
        var first = await api.NewBuyerAsync();
        var second = await api.NewBuyerAsync();
        var listing = await ApiFixture.ListingIdAsync(first, "Wash Park");
        var slot = await ApiFixture.FirstOpenSlotAsync(first, listing, skip: 1);

        var hold = await first.PostAsJsonAsync("/showings/holds", new { listingId = listing, slotStart = slot });
        Assert.Equal(HttpStatusCode.Created, hold.StatusCode);
        var expires = DateTime.Parse((await ApiFixture.ReadAsync(hold))["expiresAt"]!.GetValue<string>()).ToUniversalTime();
        Assert.InRange(expires - DateTime.UtcNow, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5.1));

        Assert.Equal(HttpStatusCode.Conflict, (await second.PostAsJsonAsync("/showings/holds", new { listingId = listing, slotStart = slot })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await second.PostAsJsonAsync("/showings", new { listingId = listing, slotStart = slot })).StatusCode);

        // The held slot is hidden from the other buyer but still offered to the holder.
        Assert.DoesNotContain(slot, await AllSlotsAsync(second, listing));
        Assert.Contains(slot, await AllSlotsAsync(first, listing));

        // Booking converts the hold.
        Assert.Equal(HttpStatusCode.Created, (await first.PostAsJsonAsync("/showings", new { listingId = listing, slotStart = slot })).StatusCode);
        var db = new MongoClient(api.ConnectionString).GetDatabase(api.Database);
        Assert.Equal(0, await db.GetCollection<BsonDocument>("slotHolds").CountDocumentsAsync(new BsonDocument("listingId", listing)));
    }

    [Fact]
    public async Task Releasing_a_hold_frees_the_slot_and_holds_expire_through_a_ttl_index()
    {
        var first = await api.NewBuyerAsync();
        var second = await api.NewBuyerAsync();
        var listing = await ApiFixture.ListingIdAsync(first, "LoDo");
        var slot = await ApiFixture.FirstOpenSlotAsync(first, listing, skip: 2);

        var hold = await ApiFixture.ReadAsync(await first.PostAsJsonAsync("/showings/holds", new { listingId = listing, slotStart = slot }));
        Assert.Equal(HttpStatusCode.NoContent, (await first.DeleteAsync($"/showings/holds/{hold["id"]}")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await second.PostAsJsonAsync("/showings/holds", new { listingId = listing, slotStart = slot })).StatusCode);

        var db = new MongoClient(api.ConnectionString).GetDatabase(api.Database);
        var indexes = await (await db.GetCollection<BsonDocument>("slotHolds").Indexes.ListAsync()).ToListAsync();
        var ttl = indexes.Single(i => i["name"] == "ttl_expiresAt");
        Assert.Equal(0, ttl["expireAfterSeconds"].ToInt32());

        // An expired hold that the TTL monitor has not removed yet (it runs every 60 s) no longer blocks anyone.
        await db.GetCollection<BsonDocument>("slotHolds").UpdateManyAsync(new BsonDocument("listingId", listing),
            new BsonDocument("$set", new BsonDocument("expiresAt", DateTime.UtcNow.AddMinutes(-1))));
        Assert.Equal(HttpStatusCode.Created, (await first.PostAsJsonAsync("/showings/holds", new { listingId = listing, slotStart = slot })).StatusCode);
    }

    private static async Task<List<string>> AllSlotsAsync(HttpClient c, string listing) =>
        (await ApiFixture.ReadAsync(await c.GetAsync($"/listings/{listing}/slots")))["days"]!.AsArray()
            .SelectMany(d => d!["slots"]!.AsArray()).Select(s => s!["startUtc"]!.GetValue<string>()).ToList();
}
