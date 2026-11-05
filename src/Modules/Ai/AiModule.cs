using Anthropic;
using HomeShowing.Ai.Application;
using HomeShowing.Ai.Infrastructure;
using HomeShowing.BuildingBlocks.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HomeShowing.Ai;

public static class AiModule
{
    /// <summary>With ANTHROPIC_API_KEY set the Claude parser is registered; without it, the rule based one.
    /// The app runs end to end either way.</summary>
    public static IServiceCollection AddAiModule(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<RuleBasedAvailabilityParser>();
        var key = config["ANTHROPIC_API_KEY"];
        if (!string.IsNullOrWhiteSpace(key))
        {
            services.AddSingleton(_ => new AnthropicClient { ApiKey = key });
            services.AddSingleton<IAvailabilityParser, ClaudeAvailabilityParser>();
        }
        else
        {
            services.AddSingleton<IAvailabilityParser>(sp => sp.GetRequiredService<RuleBasedAvailabilityParser>());
        }
        services.AddScoped<AvailabilityParsingService>();
        return services;
    }

    public static IEndpointRouteBuilder MapAiEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/ai").WithTags("AI").RequireAuthorization();
        g.MapPost("/parse-availability", async (ParseRequest req, AvailabilityParsingService svc, CancellationToken ct) => (await svc.ParseAsync(req, ct)).ToHttp())
            .RequireRateLimiting("ai");
        g.MapGet("/provider", (IAvailabilityParser p) => TypedResults.Ok(new { provider = p.Provider }));
        return app;
    }
}
