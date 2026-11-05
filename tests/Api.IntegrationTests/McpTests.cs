using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HomeShowing.Tests.Shared;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Api.IntegrationTests;

/// <summary>Drives the MCP server with the official SDK client over Streamable HTTP, as Claude Code would.</summary>
[Collection(ApiCollection.Name)]
public class McpTests(ApiFixture api)
{
    private async Task<string> CreateTokenAsync(HttpClient user)
    {
        var res = await user.PostAsJsonAsync("/auth/tokens", new { name = "test" });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await ApiFixture.ReadAsync(res))["token"]!.GetValue<string>();
    }

    private async Task<McpClient> ConnectAsync(string token)
    {
        var http = api.Factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(http.BaseAddress!, "/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        }, http, ownsHttpClient: true);
        return await McpClient.CreateAsync(transport);
    }

    private static string Text(CallToolResult r) => string.Concat(r.Content.OfType<TextContentBlock>().Select(t => t.Text));

    [Fact]
    public async Task Buyer_token_sees_only_buyer_tools_and_can_book_a_slot()
    {
        var buyer = await api.NewBuyerAsync();
        await using var client = await ConnectAsync(await CreateTokenAsync(buyer));

        var tools = await client.ListToolsAsync();
        var names = tools.Select(t => t.Name).ToHashSet();
        Assert.Equal(new HashSet<string> { "search_listings", "find_open_slots", "book_showing", "list_my_showings", "cancel_showing" }, names);
        Assert.DoesNotContain("respond_to_request", names);
        Assert.True(tools.Single(t => t.Name == "search_listings").ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.False(tools.Single(t => t.Name == "book_showing").ProtocolTool.Annotations?.ReadOnlyHint);

        var listing = await ApiFixture.ListingIdAsync(buyer, "Hyde Park");
        var slot = await ApiFixture.FirstOpenSlotAsync(buyer, listing, skip: 3);
        var found = await client.CallToolAsync("find_open_slots", new Dictionary<string, object?> { ["listingId"] = listing, ["availabilityText"] = "weekdays and weekends" });
        Assert.Contains("slotStart=", Text(found));

        var booked = await client.CallToolAsync("book_showing", new Dictionary<string, object?> { ["listingId"] = listing, ["slotStart"] = slot });
        Assert.NotEqual(true, booked.IsError);
        Assert.Contains("4407 Avenue C", Text(booked));

        // The same slot again is a clean tool error with alternatives, not a protocol failure.
        var again = await client.CallToolAsync("book_showing", new Dictionary<string, object?> { ["listingId"] = listing, ["slotStart"] = slot });
        Assert.True(again.IsError);
        Assert.Contains("just booked", Text(again));
        Assert.Contains("Nearest open slots", Text(again));

        var mine = (await ApiFixture.ReadAsync(await buyer.GetAsync("/showings/mine"))).AsArray();
        Assert.Equal("mcp", mine.Single()!["source"]!.GetValue<string>());
    }

    [Fact]
    public async Task Brokerage_b_token_cannot_see_brokerage_a_listings()
    {
        var acmeBuyer = await api.LoginAsync("buyer@acme.test");
        var acmeListing = await ApiFixture.ListingIdAsync(acmeBuyer, "LoDo");
        var acmeSlot = await ApiFixture.FirstOpenSlotAsync(acmeBuyer, acmeListing);

        var northBuyer = await api.NewBuyerAsync("northstar");
        await using var client = await ConnectAsync(await CreateTokenAsync(northBuyer));

        var search = Text(await client.CallToolAsync("search_listings", new Dictionary<string, object?> { ["city"] = "Denver" }));
        Assert.DoesNotContain("Larimer", search);
        Assert.DoesNotContain(acmeListing, search);
        var all = Text(await client.CallToolAsync("search_listings", new Dictionary<string, object?>()));
        Assert.DoesNotContain(acmeListing, all);
        Assert.Contains("Toronto", all);

        var book = await client.CallToolAsync("book_showing", new Dictionary<string, object?> { ["listingId"] = acmeListing, ["slotStart"] = acmeSlot });
        Assert.True(book.IsError);
        Assert.Contains("not found", Text(book), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Agent_token_sees_agent_tools_and_can_confirm_a_request()
    {
        var agent = await api.LoginAsync("agent@northstar.test");
        await using var client = await ConnectAsync(await CreateTokenAsync(agent));
        var names = (await client.ListToolsAsync()).Select(t => t.Name).ToHashSet();
        Assert.Equal(new HashSet<string> { "list_showing_requests", "respond_to_request", "get_listing_schedule" }, names);

        var requests = Text(await client.CallToolAsync("list_showing_requests", new Dictionary<string, object?>()));
        var id = System.Text.RegularExpressions.Regex.Match(requests, @"showing id (\w+)").Groups[1].Value;
        Assert.NotEmpty(id);
        var confirmed = await client.CallToolAsync("respond_to_request", new Dictionary<string, object?> { ["showingId"] = id, ["decision"] = "confirm" });
        Assert.StartsWith("Confirmed", Text(confirmed));
    }

    [Fact]
    public async Task Missing_or_revoked_tokens_get_401()
    {
        var anon = api.Anonymous();
        var body = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsync("/mcp", body)).StatusCode);

        var buyer = await api.NewBuyerAsync();
        var created = await ApiFixture.ReadAsync(await buyer.PostAsJsonAsync("/auth/tokens", new { name = "to revoke" }));
        Assert.Equal(HttpStatusCode.NoContent, (await buyer.DeleteAsync($"/auth/tokens/{created["id"]}")).StatusCode);
        var revoked = api.Anonymous();
        revoked.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created["token"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Unauthorized, (await revoked.PostAsync("/mcp", new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", System.Text.Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await revoked.GetAsync("/listings")).StatusCode);

        // Tokens are stored as hashes only.
        var tokens = (await ApiFixture.ReadAsync(await buyer.GetAsync("/auth/tokens"))).AsArray();
        Assert.All(tokens, t => Assert.Null(t!["token"]));
    }
}
