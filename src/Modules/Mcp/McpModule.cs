using System.Security.Claims;
using HomeShowing.BuildingBlocks.Tenancy;
using HomeShowing.Identity.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace HomeShowing.Mcp;

/// <summary>
/// The scheduler as an MCP server (Streamable HTTP at /mcp), a second interface over the same application
/// services as the REST API. Personal access tokens authenticate; tools are filtered by the caller's role on both
/// tools/list and tools/call, so a buyer never sees or runs agent tools.
/// </summary>
public static class McpModule
{
    public static readonly IReadOnlySet<string> BuyerTools = new HashSet<string> { "search_listings", "find_open_slots", "book_showing", "list_my_showings", "cancel_showing" };
    public static readonly IReadOnlySet<string> AgentTools = new HashSet<string> { "list_showing_requests", "respond_to_request", "get_listing_schedule" };

    public static IReadOnlySet<string> ToolsFor(ClaimsPrincipal? user) => user?.FindFirst(AppClaims.Role)?.Value switch
    {
        Roles.Buyer => BuyerTools,
        Roles.Agent => AgentTools,
        _ => new HashSet<string>(),
    };

    public static IServiceCollection AddMcpModule(this IServiceCollection services)
    {
        services.AddMcpServer(o =>
            {
                o.ServerInfo = new Implementation { Name = "home-showing-scheduler", Version = "1.0.0" };
                o.ServerInstructions = "Find homes for sale and book showings for the signed in buyer, or manage showing requests for the signed in agent. " +
                                       "Times are shown in the user's time zone; pass slotStart values exactly as returned by find_open_slots.";
            })
            .WithHttpTransport(o => o.Stateless = true)
            .WithTools<BuyerTools>()
            .WithTools<AgentTools>()
            .WithRequestFilters(f =>
            {
                f.AddListToolsFilter(next => async (ctx, ct) =>
                {
                    var result = await next(ctx, ct);
                    var allowed = ToolsFor(ctx.User);
                    result.Tools = result.Tools.Where(t => allowed.Contains(t.Name)).ToList();
                    return result;
                });
                f.AddCallToolFilter(next => async (ctx, ct) =>
                {
                    if (!ToolsFor(ctx.User).Contains(ctx.Params?.Name ?? ""))
                        throw new McpProtocolException($"Unknown tool: '{ctx.Params?.Name}'", McpErrorCode.InvalidParams);
                    // Same tenant context as REST: tenant, user and role come from the token, never from tool arguments.
                    var u = ctx.User!;
                    ctx.Services!.GetRequiredService<TenantContext>().Set(
                        u.FindFirst(AppClaims.TenantId)!.Value, u.FindFirst(AppClaims.Sub)!.Value, u.FindFirst(AppClaims.Role)!.Value,
                        u.FindFirst(AppClaims.Name)?.Value, u.FindFirst(AppClaims.TimeZone)?.Value);
                    return await next(ctx, ct);
                });
            });
        return services;
    }

    public static IEndpointRouteBuilder MapMcpEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMcp("/mcp").RequireAuthorization("BuyerOrAgent");
        return app;
    }
}
