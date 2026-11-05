using HomeShowing.BuildingBlocks.Web;
using HomeShowing.Listings.Application;
using HomeShowing.Listings.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace HomeShowing.Listings.Endpoints;

public static class ListingEndpoints
{
    public static IEndpointRouteBuilder MapListingEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/listings").WithTags("Listings").RequireAuthorization();

        g.MapGet("/", async ([AsParameters] ListingQueryParams p, ListingService svc, CancellationToken ct) =>
            (await svc.ListAsync(p.ToQuery(), ct)).ToHttp(hits => TypedResults.Ok(hits.Select(h => h.Listing))));

        g.MapGet("/{id}", async (string id, ListingService svc, CancellationToken ct) => (await svc.GetAsync(id, ct)).ToHttp());

        g.MapPost("/", async (ListingInput input, ListingService svc, CancellationToken ct) =>
            (await svc.CreateAsync(input, ct)).ToHttp(l => TypedResults.Created($"/listings/{l.Id}", l))).RequireAuthorization("Agent");

        g.MapPut("/{id}", async (string id, ListingInput input, ListingService svc, CancellationToken ct) =>
            (await svc.UpdateAsync(id, input, ct)).ToHttp()).RequireAuthorization("AgentOrAdmin");

        g.MapPut("/{id}/showing-rules", async (string id, ShowingRules rules, ListingService svc, CancellationToken ct) =>
            (await svc.UpdateRulesAsync(id, rules, ct)).ToHttp()).RequireAuthorization("AgentOrAdmin");

        return app;
    }

    public sealed class ListingQueryParams
    {
        public bool? Mine { get; set; }
        public string? City { get; set; }
        public decimal? MaxPrice { get; set; }
        public int? MinBeds { get; set; }
        public PropertyType? PropertyType { get; set; }
        public string? Near { get; set; }
        public double? RadiusKm { get; set; }

        public ListingQuery ToQuery() => new()
        {
            Mine = Mine ?? false, City = City, MaxPrice = MaxPrice, MinBeds = MinBeds, PropertyType = PropertyType, Near = Near, RadiusKm = RadiusKm,
        };
    }
}
