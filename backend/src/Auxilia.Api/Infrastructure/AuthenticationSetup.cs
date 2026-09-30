using Auxilia.Api.Tenancy;
using Auxilia.Application.Abstractions.Identity;
using Auxilia.Diagnostics;
using Auxilia.Infrastructure.Realtime;
using Auxilia.Infrastructure.Security.Tokens;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Auxilia.Api.Infrastructure;

/// <summary>
/// Bearer authentication with Auxilia access tokens (skill auxilia-security): ES256 only, issuer and audience from
/// section <c>Auth</c>, keys from the <see cref="SigningKeyRing"/> (reloaded when a token carries an unknown <c>kid</c>),
/// claims kept as issued (<c>sub</c>, <c>tenant</c>, <c>sid</c>, <c>role</c>). A token whose <c>jti</c> or session is
/// on the deny-list is rejected. Failures answer 401 ProblemDetails: <c>AUX-10022</c>, or <c>AUX-12023</c> when revoked.
/// </summary>
internal static class AuthenticationSetup
{
    private const string RevokedItem = "Auxilia.AccessTokenRevoked";

    public static IServiceCollection AddAuxiliaAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(TokenOptions.SectionName);
        services.Configure<TokenOptions>(section);
        var tokenOptions = section.Get<TokenOptions>() ?? new TokenOptions();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = tokenOptions.Issuer,
                    ValidAudience = tokenOptions.Audience,
                    ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
                    NameClaimType = TokenClaims.Subject,
                    RoleClaimType = TokenClaims.Role,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                };
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = LoadKeysAsync,
                    OnTokenValidated = CheckDenyListAsync,
                    OnChallenge = WriteChallengeAsync,
                };
            });

        // The key ring is a singleton: the resolver reads its current snapshot (loaded in OnMessageReceived).
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<SigningKeyRing>((options, ring) =>
                options.TokenValidationParameters.IssuerSigningKeyResolver = (_, _, _, _) => ring.ValidationKeys);

        services.AddAuthorization();
        return services;
    }

    private static async Task LoadKeysAsync(MessageReceivedContext context)
    {
        string token;
        var header = context.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            token = header["Bearer ".Length..].Trim();
        }
        else if (context.Request.Path.StartsWithSegments(RealtimeRegistration.HubPath, StringComparison.OrdinalIgnoreCase)
            && context.Request.Query["access_token"].FirstOrDefault() is { Length: > 0 } queryToken)
        {
            // Browsers cannot set headers on WebSocket/SSE requests: SignalR sends the token in the query string, only
            // accepted on the hub path.
            token = queryToken;
            context.Token = token;
        }
        else
        {
            return;
        }

        string? kid;
        try
        {
            kid = new JsonWebToken(token).Kid;
        }
        catch (ArgumentException)
        {
            // Not a JWT: validation rejects it (401) without touching the ring.
            return;
        }

        await context.HttpContext.RequestServices.GetRequiredService<SigningKeyRing>()
            .EnsureLoadedAsync(kid, context.HttpContext.RequestAborted);
    }

    private static async Task CheckDenyListAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        var tenant = principal?.FindFirst(TokenClaims.Tenant)?.Value;
        var tokenId = principal?.FindFirst(TokenClaims.TokenId)?.Value;
        if (tenant is null || tokenId is null)
        {
            context.Fail("The access token has no tenant or token id.");
            return;
        }

        Guid? sessionId = Guid.TryParse(principal!.FindFirst(TokenClaims.Session)?.Value, out var sid) ? sid : null;
        var denyList = context.HttpContext.RequestServices.GetRequiredService<IAccessTokenDenyList>();
        if (await denyList.IsDeniedAsync(tenant, tokenId, sessionId, context.HttpContext.RequestAborted))
        {
            context.HttpContext.Items[RevokedItem] = true;
            context.Fail("The access token was revoked.");
        }
    }

    private static async Task WriteChallengeAsync(JwtBearerChallengeContext context)
    {
        context.HandleResponse();
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Headers.WWWAuthenticate = "Bearer";
        var revoked = context.HttpContext.Items.ContainsKey(RevokedItem);
        await TenantResolutionMiddleware.WriteProblemAsync(
            context.HttpContext,
            revoked ? EventCodes.Identity.AccessTokenRevoked : EventCodes.Host.AuthenticationRequired,
            StatusCodes.Status401Unauthorized,
            revoked ? "The access token was revoked" : "A valid access token is required");
    }
}
