using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;

using Auxilia.Api.Tenancy;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Diagnostics;
using Auxilia.Infrastructure.Security.Tokens;

using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Auxilia.Api.RateLimiting;

/// <summary>A sliding-window limit: <see cref="PermitLimit"/> requests per <see cref="Window"/>.</summary>
public sealed class RateLimitRule
{
    public int PermitLimit { get; set; }

    public TimeSpan Window { get; set; }
}

/// <summary>
/// Rate limits (section <c>RateLimiting</c>, infrastructure level): per node, in memory. Partitions are tenant-aware
/// (<c>t:{slug}:…</c>) so one tenant cannot exhaust another's budget.
/// </summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; set; } = true;

    /// <summary><c>POST /auth/token</c> per tenant and IP (brute force of one account is also stopped by the lockout).</summary>
    public RateLimitRule SignIn { get; set; } = new() { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) };

    /// <summary>Activation and password reset links per tenant and IP (they send e-mails).</summary>
    public RateLimitRule AccountLinks { get; set; } = new() { PermitLimit = 10, Window = TimeSpan.FromMinutes(15) };

    /// <summary>Registration requests (F02, external client applications) per tenant and IP.</summary>
    public RateLimitRule Registration { get; set; } = new() { PermitLimit = 10, Window = TimeSpan.FromHours(1) };

    /// <summary>Every request of an authenticated user, per tenant and user.</summary>
    public RateLimitRule User { get; set; } = new() { PermitLimit = 600, Window = TimeSpan.FromMinutes(1) };

    /// <summary>Every anonymous request, per IP.</summary>
    public RateLimitRule Anonymous { get; set; } = new() { PermitLimit = 300, Window = TimeSpan.FromMinutes(1) };

    /// <summary>Every request of a client application (<c>client_id</c> claim or <c>X-Client-Id</c>), all users together.</summary>
    public RateLimitRule Client { get; set; } = new() { PermitLimit = 5000, Window = TimeSpan.FromMinutes(1) };
}

/// <summary>
/// Rate limiting (skill auxilia-security): a global limiter chains a per-caller limit (authenticated user per tenant,
/// otherwise IP) with a per-client-application limit; the sign-in, account-link and registration endpoints add
/// stricter per-IP policies. Rejections answer 429 ProblemDetails <c>AUX-10024</c> with <c>Retry-After</c> and log the security event
/// <c>AUX-29016</c>. Health endpoints are not limited.
/// </summary>
internal static class RateLimitingSetup
{
    public const string SignInPolicy = "auth-token";

    public const string AccountLinksPolicy = "auth-links";

    public const string RegistrationPolicy = "registrations";

    private const int SegmentsPerWindow = 6;

    public static IServiceCollection AddAuxiliaRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RateLimitingOptions>(configuration.GetSection(RateLimitingOptions.SectionName));
        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(CallerPartition),
                PartitionedRateLimiter.Create<HttpContext, string>(ClientPartition));
            options.AddPolicy(SignInPolicy, context => IpPartition(context, SignInPolicy, rules => rules.SignIn));
            options.AddPolicy(AccountLinksPolicy, context => IpPartition(context, AccountLinksPolicy, rules => rules.AccountLinks));
            options.AddPolicy(RegistrationPolicy, context => IpPartition(context, RegistrationPolicy, rules => rules.Registration));
            options.OnRejected = OnRejectedAsync;
        });
        return services;
    }

    private static RateLimitPartition<string> CallerPartition(HttpContext context)
    {
        var rules = Rules(context);
        if (rules is null || IsExempt(context))
        {
            return RateLimitPartition.GetNoLimiter("none");
        }

        var user = context.User;
        if (user.Identity?.IsAuthenticated == true && user.FindFirstValue(TokenClaims.Subject) is { } subject)
        {
            return Sliding($"t:{Tenant(context)}:user:{subject}", rules.User);
        }

        return Sliding($"ip:{Ip(context)}", rules.Anonymous);
    }

    private static RateLimitPartition<string> ClientPartition(HttpContext context)
    {
        var rules = Rules(context);
        var clientId = context.User.FindFirstValue(TokenClaims.Client) ?? context.Request.Headers["X-Client-Id"].FirstOrDefault();
        return rules is null || IsExempt(context) || string.IsNullOrWhiteSpace(clientId)
            ? RateLimitPartition.GetNoLimiter("none")
            : Sliding($"client:{clientId.Trim()}", rules.Client);
    }

    private static RateLimitPartition<string> IpPartition(HttpContext context, string policy, Func<RateLimitingOptions, RateLimitRule> rule)
    {
        var rules = Rules(context);
        return rules is null
            ? RateLimitPartition.GetNoLimiter("none")
            : Sliding($"t:{Tenant(context)}:{policy}:ip:{Ip(context)}", rule(rules));
    }

    private static RateLimitPartition<string> Sliding(string key, RateLimitRule rule) =>
        RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = rule.PermitLimit,
            Window = rule.Window,
            SegmentsPerWindow = SegmentsPerWindow,
            QueueLimit = 0,
            AutoReplenishment = true,
        });

    private static RateLimitingOptions? Rules(HttpContext context)
    {
        var options = context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
        return options.Enabled ? options : null;
    }

    private static bool IsExempt(HttpContext context) =>
        context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);

    private static string Tenant(HttpContext context) =>
        context.RequestServices.GetRequiredService<ITenantContext>().Current?.Slug ?? "-";

    // The caller set by the forwarded headers middleware: KnownProxies/KnownNetworks of the production proxy are set by H-03.
    private static string Ip(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static async ValueTask OnRejectedAsync(OnRejectedContext rejected, CancellationToken cancellationToken)
    {
        var context = rejected.HttpContext;
        var policy = context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName ?? "global";
        var partitionKind = context.User.Identity?.IsAuthenticated == true ? "user" : "ip";
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Auxilia.Api.RateLimiting");
        Log.Security.RateLimitExceeded(logger, policy, partitionKind, context.Request.Method, context.Request.Path.Value ?? "/");

        // Sliding windows free permits segment by segment: without lease metadata, one segment of the rule is the wait.
        if (!rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            var rules = context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
            var rule = policy switch
            {
                SignInPolicy => rules.SignIn,
                AccountLinksPolicy => rules.AccountLinks,
                RegistrationPolicy => rules.Registration,
                _ => partitionKind == "user" ? rules.User : rules.Anonymous,
            };
            retryAfter = rule.Window / SegmentsPerWindow;
        }

        context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        await TenantResolutionMiddleware.WriteProblemAsync(
            context, EventCodes.Host.TooManyRequests, StatusCodes.Status429TooManyRequests, "Too many requests, retry later");
    }
}
