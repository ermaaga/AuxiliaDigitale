namespace Auxilia.Api.Infrastructure;

/// <summary>
/// Response headers for a JSON API (skill auxilia-security): nothing may be framed, sniffed or cached by default.
/// The Scalar UI (Development only) needs scripts and styles, so it keeps only the non-CSP headers.
/// </summary>
internal static class SecurityHeaders
{
    public const string ApiContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";

    public static IApplicationBuilder UseAuxiliaSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "no-referrer";
                headers["Cross-Origin-Opener-Policy"] = "same-origin";

                if (!context.Request.Path.StartsWithSegments(OpenApiSetup.ScalarPath, StringComparison.OrdinalIgnoreCase))
                {
                    headers.ContentSecurityPolicy = ApiContentSecurityPolicy;
                }

                if (string.IsNullOrEmpty(headers.CacheControl))
                {
                    headers.CacheControl = "no-store";
                }

                return Task.CompletedTask;
            });

            await next(context);
        });
}
