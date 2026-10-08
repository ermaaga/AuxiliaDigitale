using Auxilia.Api.Endpoints.Identity;
using Auxilia.Infrastructure.Security.Tokens;

namespace Auxilia.Api.Infrastructure;

/// <summary>
/// Every log line of a request carries the client application (F25): the <c>client_id</c> claim of the access token, or
/// the <c>X-Client-Id</c> header of an anonymous call (sign-in, registration). Never the client secret.
/// </summary>
internal static class ClientAppLogScope
{
    public const string Property = "ClientId";

    public static IApplicationBuilder UseClientAppLogScope(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var clientId = context.User.FindFirst(TokenClaims.Client)?.Value
                ?? context.Request.Headers[AuthEndpoints.ClientIdHeader].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(clientId))
            {
                await next(context);
                return;
            }

            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ClientAppLogScope));
            using (logger.BeginScope(new Dictionary<string, object?> { [Property] = clientId.Length > 100 ? clientId[..100] : clientId }))
            {
                await next(context);
            }
        });
}
