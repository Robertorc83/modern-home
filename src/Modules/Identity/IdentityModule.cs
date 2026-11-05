using HomeShowing.BuildingBlocks.Persistence;
using HomeShowing.BuildingBlocks.Tenancy;
using HomeShowing.Identity.Application;
using HomeShowing.Identity.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace HomeShowing.Identity;

public static class IdentityModule
{
    /// <param name="environment">Host environment name (e.g. <c>Development</c>); outside Development the public dev
    /// signing key is refused unless <c>Auth:AllowDevKey=true</c> says this is a demo.</param>
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration config, string? environment = null)
    {
        var jwt = new JwtOptions();
        config.GetSection("Auth").Bind(jwt);
        if (jwt.SigningKey.Length < 32) throw new InvalidOperationException("Auth:SigningKey must be at least 32 characters.");
        if (jwt.IsDevKey && !string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(config["Auth:AllowDevKey"], "true", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Auth:SigningKey is the publicly known development key. Set Auth__SigningKey (JWT_SIGNING_KEY in docker-compose) to a secret of " +
                "at least 32 characters, or set Auth__AllowDevKey=true for a throwaway demo.");
        services.AddSingleton(jwt);
        services.AddSingleton<JwtIssuer>();
        services.AddSingleton<CredentialStore>();
        services.AddSingleton<IUserDirectory, UserDirectory>();
        services.AddScoped<UserRepository>();
        services.AddScoped<TokenRepository>();
        services.AddScoped<AuthService>();
        services.AddSingleton<IMongoSchemaSetup, IdentitySchema>();

        services.AddAuthentication(AuthSchemes.Smart)
            // One entry point: personal access tokens go to the PAT handler, everything else is a JWT.
            .AddPolicyScheme(AuthSchemes.Smart, "JWT or personal access token", o =>
                o.ForwardDefaultSelector = ctx =>
                    ctx.Request.Headers.Authorization.ToString().StartsWith("Bearer " + PatFormat.Prefix, StringComparison.Ordinal)
                        ? AuthSchemes.Pat : JwtBearerDefaults.AuthenticationScheme)
            .AddScheme<AuthenticationSchemeOptions, PatAuthenticationHandler>(AuthSchemes.Pat, null)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer, ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    NameClaimType = AppClaims.Name, RoleClaimType = AppClaims.Role, ClockSkew = TimeSpan.FromMinutes(1),
                };
            });

        services.AddSingleton<IAuthorizationHandler, NotPersonalAccessTokenHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, PatAwareAuthorizationResultHandler>();
        services.AddAuthorizationBuilder()
            .AddPolicy("Buyer", p => p.RequireAuthenticatedUser().RequireClaim(AppClaims.Role, Roles.Buyer))
            .AddPolicy("Agent", p => p.RequireAuthenticatedUser().RequireClaim(AppClaims.Role, Roles.Agent))
            .AddPolicy("Admin", p => p.RequireAuthenticatedUser().RequireClaim(AppClaims.Role, Roles.BrokerageAdmin))
            .AddPolicy("AgentOrAdmin", p => p.RequireAuthenticatedUser().RequireClaim(AppClaims.Role, Roles.Agent, Roles.BrokerageAdmin))
            .AddPolicy("BuyerOrAgent", p => p.RequireAuthenticatedUser().RequireClaim(AppClaims.Role, Roles.Buyer, Roles.Agent))
            // Web session only: token management and admin screens (see NotPersonalAccessTokenRequirement).
            .AddPolicy(NotPersonalAccessTokenRequirement.Policy, p => p.RequireAuthenticatedUser().AddRequirements(new NotPersonalAccessTokenRequirement()));
        return services;
    }
}
