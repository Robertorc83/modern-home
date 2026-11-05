using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HomeShowing.Tests.Shared;

namespace Tenancy.Tests;

[Collection(ApiCollection.Name)]
public class TenantIsolationTests(ApiFixture api)
{
    [Fact]
    public async Task A_user_in_another_brokerage_gets_404_not_403_on_a_listing()
    {
        var acmeAgent = await api.LoginAsync("agent@acme.test");
        var acmeListing = await ApiFixture.ListingIdAsync(acmeAgent, "LoDo");

        foreach (var email in new[] { "buyer@northstar.test", "agent@northstar.test", "admin@northstar.test" })
        {
            var other = await api.LoginAsync(email);
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/listings/{acmeListing}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/listings/{acmeListing}/slots")).StatusCode);
        }

        var northAgent = await api.LoginAsync("agent@northstar.test");
        var update = await northAgent.PutAsJsonAsync($"/listings/{acmeListing}/showing-rules", new { slotMinutes = 30, weekly = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);

        var northBuyer = await api.LoginAsync("buyer@northstar.test");
        var slot = await ApiFixture.FirstOpenSlotAsync(acmeAgent, acmeListing);
        Assert.Equal(HttpStatusCode.NotFound, (await northBuyer.PostAsJsonAsync("/showings", new { listingId = acmeListing, slotStart = slot })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await northBuyer.PostAsJsonAsync("/showings/holds", new { listingId = acmeListing, slotStart = slot })).StatusCode);
    }

    [Fact]
    public async Task List_endpoints_never_return_another_tenants_documents()
    {
        var acmeAdmin = await api.LoginAsync("admin@acme.test");
        var acmeIds = (await ApiFixture.ReadAsync(await acmeAdmin.GetAsync("/listings"))).AsArray().Select(l => l!["id"]!.GetValue<string>()).ToHashSet();
        var acmeShowingIds = (await ApiFixture.ReadAsync(await acmeAdmin.GetAsync("/showings/inbox"))).AsArray().Select(s => s!["id"]!.GetValue<string>()).ToHashSet();
        Assert.NotEmpty(acmeIds);
        Assert.NotEmpty(acmeShowingIds);

        var northAdmin = await api.LoginAsync("admin@northstar.test");
        var northListings = (await ApiFixture.ReadAsync(await northAdmin.GetAsync("/listings"))).AsArray();
        Assert.NotEmpty(northListings);
        Assert.All(northListings, l => Assert.DoesNotContain(l!["id"]!.GetValue<string>(), acmeIds));
        Assert.All(northListings, l => Assert.Equal("Toronto", l!["city"]!.GetValue<string>()));

        var northInbox = (await ApiFixture.ReadAsync(await northAdmin.GetAsync("/showings/inbox"))).AsArray();
        Assert.All(northInbox, s => Assert.DoesNotContain(s!["id"]!.GetValue<string>(), acmeShowingIds));

        // Geo search and matching are tenant scoped too: the Acme Denver listings sit inside this radius (an Acme buyer gets
        // them with the same query), so it is the tenant filter inside $geoNear, not the distance, that hides them.
        var northBuyer = await api.LoginAsync("buyer@northstar.test");
        var acmeBuyer = await api.LoginAsync("buyer@acme.test");
        Assert.NotEmpty((await ApiFixture.ReadAsync(await acmeBuyer.GetAsync("/listings?near=Denver&radiusKm=500"))).AsArray());
        var near = (await ApiFixture.ReadAsync(await northBuyer.GetAsync("/listings?near=Denver&radiusKm=500"))).AsArray();
        Assert.All(near, l => Assert.DoesNotContain(l!["id"]!.GetValue<string>(), acmeIds));
        var match = await ApiFixture.ReadAsync(await northBuyer.PostAsJsonAsync("/match", new
        {
            timeZone = "America/Denver",
            ranges = new[] { new { start = DateTime.UtcNow.ToString("yyyy-MM-ddT00:00"), end = DateTime.UtcNow.AddDays(14).ToString("yyyy-MM-ddT23:00") } },
        }));
        Assert.All(match["results"]!.AsArray(), r => Assert.DoesNotContain(r!["listing"]!["id"]!.GetValue<string>(), acmeIds));

        var northAgents = (await ApiFixture.ReadAsync(await northAdmin.GetAsync("/admin/agents"))).AsArray();
        Assert.All(northAgents, a => Assert.EndsWith("@northstar.test", a!["email"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Showing_actions_across_tenants_look_like_not_found()
    {
        var acmeAgent = await api.LoginAsync("agent@acme.test");
        var acmeShowing = (await ApiFixture.ReadAsync(await acmeAgent.GetAsync("/showings/inbox"))).AsArray().First()!["id"]!.GetValue<string>();
        var northAgent = await api.LoginAsync("agent@northstar.test");
        Assert.Equal(HttpStatusCode.NotFound, (await northAgent.PostAsync($"/showings/{acmeShowing}/confirm", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await northAgent.PostAsync($"/showings/{acmeShowing}/cancel", null)).StatusCode);
    }

    [Fact]
    public async Task A_token_with_a_forged_tenant_claim_is_rejected()
    {
        var buyer = await api.LoginAsync("buyer@northstar.test");
        var jwt = buyer.DefaultRequestHeaders.Authorization!.Parameter!;
        var parts = jwt.Split('.');
        var payload = System.Text.Encoding.UTF8.GetString(Microsoft.IdentityModel.Tokens.Base64UrlEncoder.DecodeBytes(parts[1]));
        var forged = payload.Replace("\"role\":\"Buyer\"", "\"role\":\"BrokerageAdmin\"");
        var token = $"{parts[0]}.{Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode(forged)}.{parts[2]}";
        var client = api.Anonymous();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/admin/agents")).StatusCode);
    }

    [Fact]
    public async Task Tenant_comes_from_the_token_not_the_body()
    {
        var northAgent = await api.LoginAsync("agent@northstar.test");
        var acmeAdmin = await api.LoginAsync("admin@acme.test");
        var acmeTenantId = (await ApiFixture.ReadAsync(await acmeAdmin.GetAsync("/auth/me")))["tenantId"]!.GetValue<string>();

        var res = await northAgent.PostAsJsonAsync("/listings", new
        {
            tenantId = acmeTenantId, ownerUserId = "someone-else", address = "1 Injection Way", city = "Toronto", price = 500000, beds = 2, baths = 1,
            propertyType = "Condo", timeZone = "America/Toronto", location = new { lat = 43.65, lng = -79.38 },
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var id = (await ApiFixture.ReadAsync(res))["id"]!.GetValue<string>();
        Assert.Equal(HttpStatusCode.NotFound, (await acmeAdmin.GetAsync($"/listings/{id}")).StatusCode);
        var north = (await ApiFixture.ReadAsync(await northAgent.GetAsync($"/listings/{id}")));
        Assert.NotEqual("someone-else", north["ownerUserId"]!.GetValue<string>());
    }
}
