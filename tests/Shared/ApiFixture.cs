using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.MongoDb;

namespace HomeShowing.Tests.Shared;

/// <summary>
/// Boots the real API (all modules, real auth, real seed) against a throwaway MongoDB 8 single node replica set
/// in Docker, so transactions, the unique partial index, TTL and change streams behave exactly as in production.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string Password = "demo1234";
    private readonly MongoDbContainer _mongo = new MongoDbBuilder("mongo:8.0").WithReplicaSet("rs0").Build();
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public string ConnectionString { get; private set; } = "";
    public string Database { get; } = "it_" + Guid.NewGuid().ToString("N")[..8];

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task InitializeAsync()
    {
        await _mongo.StartAsync();
        var cs = _mongo.GetConnectionString();
        ConnectionString = cs.Contains("directConnection") ? cs : cs + (cs.Contains('?') ? "&" : "?") + "directConnection=true";
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("Mongo:ConnectionString", ConnectionString)
            .UseSetting("Mongo:Database", Database)
            .UseSetting("Seed:Enabled", "true")
            .UseSetting("RateLimits:AuthPerMinute", "10000")
            .UseSetting("ANTHROPIC_API_KEY", ""));
        _ = Factory.Server; // start the host: applies schema and seeds
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _mongo.DisposeAsync();
    }

    public HttpClient Anonymous() => Factory.CreateClient();

    public async Task<HttpClient> LoginAsync(string email)
    {
        var client = Factory.CreateClient();
        var res = await client.PostAsJsonAsync("/auth/login", new { email, password = Password });
        res.EnsureSuccessStatusCode();
        var token = (await res.Content.ReadFromJsonAsync<JsonObject>())!["token"]!.GetValue<string>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task<HttpClient> NewBuyerAsync(string tenantSlug = "acme")
    {
        var client = Factory.CreateClient();
        var email = $"buyer-{Guid.NewGuid():N}@test.local";
        var res = await client.PostAsJsonAsync("/auth/register-buyer", new { tenantSlug, email, password = Password, name = "Test Buyer" });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var token = (await res.Content.ReadFromJsonAsync<JsonObject>())!["token"]!.GetValue<string>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static async Task<JsonNode> ReadAsync(HttpResponseMessage res) =>
        JsonNode.Parse(await res.Content.ReadAsStringAsync()) ?? throw new InvalidOperationException("empty body");

    /// <summary>A listing id in the caller's tenant by neighborhood.</summary>
    public static async Task<string> ListingIdAsync(HttpClient client, string neighborhood)
    {
        var list = (await ReadAsync(await client.GetAsync("/listings"))).AsArray();
        return list.First(l => l!["neighborhood"]?.GetValue<string>() == neighborhood)!["id"]!.GetValue<string>();
    }

    /// <summary>The first open slot of a listing, as the client sees it.</summary>
    public static async Task<string> FirstOpenSlotAsync(HttpClient client, string listingId, int skip = 0)
    {
        var slots = await ReadAsync(await client.GetAsync($"/listings/{listingId}/slots"));
        return slots["days"]!.AsArray().SelectMany(d => d!["slots"]!.AsArray()).Skip(skip).First()!["startUtc"]!.GetValue<string>();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
