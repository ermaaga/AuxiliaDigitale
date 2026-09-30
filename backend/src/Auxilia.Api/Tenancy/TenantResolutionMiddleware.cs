using System.Diagnostics;
using System.Security.Claims;

using Auxilia.Api.Infrastructure;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Diagnostics;

using Microsoft.Extensions.Options;

namespace Auxilia.Api.Tenancy;

/// <summary>
/// Resolves the tenant of the request (skill auxilia-multitenancy): 1. token claim <c>tenant</c> (authoritative),
/// 2. header <c>X-Tenant</c>, 3. host (<c>{slug}.{base domain}</c> or a custom domain). A claim that disagrees with
/// the header or host is a cross-tenant attempt: 403 <c>AUX-11004</c>. An unknown tenant is 404 <c>AUX-11009</c>.
/// The tenant goes into <see cref="ITenantContext"/>, the log scope (<c>TenantSlug</c>, so logs reach the tenant's
/// daily file) and the trace. Whether the endpoint needs an active tenant is decided by <see cref="TenantEndpointFilter"/>.
/// </summary>
internal sealed class TenantResolutionMiddleware
{
    public const string TenantClaim = "tenant";

    public const string TenantHeader = "X-Tenant";

    private readonly RequestDelegate next;
    private readonly TenancyOptions options;
    private readonly ILogger<TenantResolutionMiddleware> logger;

    public TenantResolutionMiddleware(RequestDelegate next, IOptions<TenancyOptions> options, ILogger<TenantResolutionMiddleware> logger)
    {
        this.next = next;
        this.options = options.Value;
        this.logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ITenantDirectory directory, ITenantContextSetter tenantContext)
    {
        var cancellationToken = context.RequestAborted;
        var claim = Normalize(context.User.Identity?.IsAuthenticated == true ? context.User.FindFirstValue(TenantClaim) : null);
        var header = Normalize(context.Request.Headers[TenantHeader].FirstOrDefault());

        TenantInfo? tenant = null;
        string? requested = header;
        if (requested is null)
        {
            (requested, tenant) = await FromHostAsync(context.Request.Host.Host, directory, cancellationToken);
        }

        if (claim is not null && requested is not null && !string.Equals(claim, requested, StringComparison.Ordinal))
        {
            Log.Security.CrossTenantAttempt(logger, claim, requested);
            await WriteProblemAsync(context, Errors.Tenancy.CrossTenantAttempt().Code, StatusCodes.Status403Forbidden,
                "The token does not belong to the requested tenant");
            return;
        }

        var slug = claim ?? requested;
        if (slug is null)
        {
            await next(context);
            return;
        }

        if (tenant is null || tenant.Slug != slug)
        {
            tenant = await directory.FindBySlugAsync(slug, cancellationToken);
        }

        if (tenant is null)
        {
            var notFound = Errors.Tenancy.TenantNotFound();
            await WriteProblemAsync(context, notFound.Code, StatusCodes.Status404NotFound, notFound.Description);
            return;
        }

        tenantContext.Set(tenant);
        Activity.Current?.SetTag("auxilia.tenant", tenant.Slug);

        using (logger.BeginScope(new Dictionary<string, object?> { ["TenantSlug"] = tenant.Slug }))
        {
            await next(context);
        }
    }

    private async Task<(string? Slug, TenantInfo? Tenant)> FromHostAsync(string host, ITenantDirectory directory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(host) || options.IgnoredHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
        {
            return (null, null);
        }

        foreach (var baseDomain in options.BaseDomains)
        {
            var suffix = "." + baseDomain;
            if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                var label = host[..^suffix.Length];
                return label.Contains('.', StringComparison.Ordinal) ? (null, null) : (Normalize(label), null);
            }
        }

        var tenant = await directory.FindByHostAsync(host, cancellationToken);
        return (tenant?.Slug, tenant);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    internal static Task WriteProblemAsync(HttpContext context, int code, int status, string title)
    {
        context.Response.StatusCode = status;
        return context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails =
            {
                Status = status,
                Title = title,
                Type = ProblemDetailsSetup.ErrorTypeUri(code),
                Extensions = { [ProblemDetailsSetup.ErrorCodeKey] = ProblemDetailsSetup.ErrorCode(code) },
            },
        }).AsTask();
    }
}
