using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using HomeShowing.Tests.Shared;

namespace Api.IntegrationTests;

/// <summary>Regression tests for the security / robustness review findings.</summary>
[Collection(ApiCollection.Name)]
public class ReviewFixesTests(ApiFixture api)
{
    private static object NewListing(string status, string address) => new
    {
        status, address, city = "Denver", price = 500_000, beds = 2, baths = 1, propertyType = "Condo", timeZone = "America/Denver",
        location = new { lat = 39.74, lng = -104.99 },
    };

    [Fact]
    public async Task A_personal_access_token_cannot_mint_or_manage_tokens()
    {
        var buyer = await api.NewBuyerAsync();
        var created = await ApiFixture.ReadAsync(await buyer.PostAsJsonAsync("/auth/tokens", new { name = "first" }));

        var pat = api.Anonymous();
        pat.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created["token"]!.GetValue<string>());

        var mint = await pat.PostAsJsonAsync("/auth/tokens", new { name = "second" });
        Assert.Equal(HttpStatusCode.Forbidden, mint.StatusCode);
        var problem = await ApiFixture.ReadAsync(mint);
        Assert.Equal(403, problem["status"]!.GetValue<int>());
        Assert.Contains("Personal access tokens cannot manage tokens", problem["detail"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Forbidden, (await pat.GetAsync("/auth/tokens")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await pat.DeleteAsync($"/auth/tokens/{created["id"]}")).StatusCode);

        // The same token still works for ordinary REST reads.
        Assert.Equal(HttpStatusCode.OK, (await pat.GetAsync("/listings")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await pat.GetAsync("/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Bad_geo_input_is_a_400_not_a_500()
    {
        var buyer = await api.LoginAsync("buyer@acme.test");
        Assert.Equal(HttpStatusCode.BadRequest, (await buyer.GetAsync("/listings?near=Denver&radiusKm=-1")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await buyer.GetAsync("/listings?near=Denver&radiusKm=10000")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await buyer.GetAsync("/listings?nearLat=500&nearLng=-104.99")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await buyer.GetAsync("/listings?nearLat=39.74&nearLng=-181")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await buyer.GetAsync("/listings?near=Atlantis")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await buyer.GetAsync("/listings?nearLat=39.74&nearLng=-104.99&radiusKm=5")).StatusCode);
    }

    [Fact]
    public async Task Put_without_status_keeps_the_existing_status()
    {
        var agent = await api.LoginAsync("agent@acme.test");
        var created = await agent.PostAsJsonAsync("/listings", NewListing("Draft", "1 Draft St"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var listing = await ApiFixture.ReadAsync(created);
        Assert.Equal("Draft", listing["status"]!.GetValue<string>());
        var id = listing["id"]!.GetValue<string>();

        var update = new
        {
            address = "1 Draft St, renamed", city = "Denver", price = 510_000, beds = 2, baths = 1, propertyType = "Condo",
            timeZone = "America/Denver", location = new { lat = 39.74, lng = -104.99 },
        };
        var put = await agent.PutAsJsonAsync($"/listings/{id}", update);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var updated = await ApiFixture.ReadAsync(put);
        Assert.Equal("Draft", updated["status"]!.GetValue<string>());
        Assert.Equal("1 Draft St, renamed", updated["address"]!.GetValue<string>());
        Assert.Equal(listing["version"]!.GetValue<int>() + 1, updated["version"]!.GetValue<int>());

        // A new listing without a status is Active.
        var active = await ApiFixture.ReadAsync(await agent.PostAsJsonAsync("/listings", update));
        Assert.Equal("Active", active["status"]!.GetValue<string>());

        // Sending a stale version is a conflict.
        var stale = await agent.PutAsJsonAsync($"/listings/{id}", new { update.address, update.city, update.price, update.beds, update.baths, update.propertyType, update.timeZone, update.location, version = 0 });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task Agents_cannot_see_other_agents_draft_listings()
    {
        var agent2 = await api.LoginAsync("agent2@acme.test");
        var created = await ApiFixture.ReadAsync(await agent2.PostAsJsonAsync("/listings", NewListing("Draft", "2 Hidden Ave")));
        var id = created["id"]!.GetValue<string>();

        var agent = await api.LoginAsync("agent@acme.test");
        Assert.Equal(HttpStatusCode.NotFound, (await agent.GetAsync($"/listings/{id}")).StatusCode);
        var visible = (await ApiFixture.ReadAsync(await agent.GetAsync("/listings"))).AsArray();
        Assert.DoesNotContain(visible, l => l!["id"]!.GetValue<string>() == id);
        Assert.Contains(visible, l => l!["status"]!.GetValue<string>() == "Active"); // the brokerage's active listings still show

        Assert.Equal(HttpStatusCode.OK, (await agent2.GetAsync($"/listings/{id}")).StatusCode);
        var admin = await api.LoginAsync("admin@acme.test");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/listings/{id}")).StatusCode);
        var buyer = await api.LoginAsync("buyer@acme.test");
        Assert.Equal(HttpStatusCode.NotFound, (await buyer.GetAsync($"/listings/{id}")).StatusCode);
    }

    [Fact]
    public async Task Admin_agent_list_includes_listing_counts()
    {
        var admin = await api.LoginAsync("admin@acme.test");
        var agents = (await ApiFixture.ReadAsync(await admin.GetAsync("/admin/agents"))).AsArray();
        var alex = agents.Single(a => a!["email"]!.GetValue<string>() == "agent@acme.test")!;
        Assert.True(alex["listingCount"]!.GetValue<int>() >= 3);
    }

    [Fact]
    public async Task Same_email_can_exist_in_two_brokerages_and_login_picks_the_matching_password()
    {
        var email = $"twin-{Guid.NewGuid():N}@test.local";
        var anon = api.Anonymous();
        Assert.Equal(HttpStatusCode.Created, (await anon.PostAsJsonAsync("/auth/register-buyer", new { tenantSlug = "acme", email, password = "acme-pass-1", name = "Twin A" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await anon.PostAsJsonAsync("/auth/register-buyer", new { tenantSlug = "northstar", email, password = "north-pass-2", name = "Twin N" })).StatusCode);
        // Same brokerage again: still a conflict.
        Assert.Equal(HttpStatusCode.Conflict, (await anon.PostAsJsonAsync("/auth/register-buyer", new { tenantSlug = "acme", email, password = "acme-pass-1", name = "Twin A" })).StatusCode);

        var north = await ApiFixture.ReadAsync(await anon.PostAsJsonAsync("/auth/login", new { email, password = "north-pass-2" }));
        Assert.Equal("northstar", north["user"]!["tenantSlug"]!.GetValue<string>());
        var acme = await ApiFixture.ReadAsync(await anon.PostAsJsonAsync("/auth/login", new { email, password = "acme-pass-1" }));
        Assert.Equal("acme", acme["user"]!["tenantSlug"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/auth/login", new { email, password = "wrong" })).StatusCode);
    }
}
