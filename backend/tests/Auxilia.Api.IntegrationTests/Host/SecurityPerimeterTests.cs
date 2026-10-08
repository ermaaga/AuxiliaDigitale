using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

using Auxilia.Api.Authorization;
using Auxilia.Api.IntegrationTests.Identity;
using Auxilia.Api.IntegrationTests.Platform;
using Auxilia.Api.IntegrationTests.Realtime;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Auxilia.Api.IntegrationTests.Host;

/// <summary>
/// Internal pen-test of the access perimeter (H-01, ASVS V4/V8): every endpoint of the real host declares how it is
/// protected, and every protected endpoint refuses the wrong kind of caller — anonymous, a platform token on tenant
/// endpoints (D-21), a tenant user on console endpoints (N02), a user of another tenant (cross-tenant). Route values
/// are random, bodies empty: what is checked is that the guard answers before any handler logic.
/// </summary>
public sealed partial class SecurityPerimeterTests(SecurityPerimeterTests.Factory factory) : IClassFixture<SecurityPerimeterTests.Factory>
{
    private const string Password = "a long enough password";

    /// <summary>Anonymous on purpose (skill auxilia-security), each with the reason.</summary>
    private static readonly Dictionary<string, string> Anonymous = new(StringComparer.Ordinal)
    {
        ["POST /api/v1/auth/token"] = "sign-in and refresh (client application, rate limit; the refresh token is the credential)",
        ["GET /api/v1/auth/methods"] = "sign-in methods of the login page",
        ["GET /api/v1/auth/password-policy"] = "password rules shown on the activation and reset pages",
        ["POST /api/v1/auth/activate"] = "account activation link (single-use token, rate limit)",
        ["POST /api/v1/auth/password/forgot"] = "forgot password (rate limit, same answer for unknown users)",
        ["POST /api/v1/auth/password/reset"] = "password reset link (single-use token, rate limit)",
        ["POST /api/v1/auth/password/change"] = "expired password (the current password is the credential, rate limit)",
        ["POST /api/v1/auth/otp"] = "e-mailed sign-in code (rate limit, same answer for unknown users)",
        ["POST /api/v1/auth/two-factor/setup"] = "required authenticator setup before the first sign-in (password is the credential, rate limit)",
        ["POST /api/v1/auth/two-factor/setup/confirm"] = "confirms the required authenticator setup and signs in (password + code, rate limit)",
        ["POST /api/v1/platform/auth/enrollment"] = "System user TOTP enrolment (activation token)",
        ["POST /api/v1/platform/auth/activate"] = "System user activation (activation token + TOTP)",
        ["POST /api/v1/platform/auth/token"] = "console sign-in (console client + password + TOTP)",
        ["GET /api/v1/registrations/captcha"] = "external registration captcha (D-14)",
        ["POST /api/v1/registrations"] = "external registration (client application + captcha + rate limit, D-14)",
        ["GET /api/v1/i18n/{language}"] = "translations of the login pages",
        ["GET /api/v1/i18n/languages"] = "languages of the login pages",
        ["GET /api/v1/branding"] = "tenant branding of the login pages",
        ["GET /api/v1/branding/{asset}"] = "tenant branding images of the login pages",
    };

    /// <summary>Required query values of some endpoints: without them binding fails (400) before any guard runs.</summary>
    private static readonly Dictionary<string, string> SampleQueries = new(StringComparer.Ordinal)
    {
        ["GET /api/v1/appointments/calendar"] = "?from=2026-01-01&to=2026-01-31",
        ["GET /api/v1/appointments/conflicts"] = $"?employeeUserId={Guid.Empty}&date=2026-01-01&time=10:00&durationMinutes=30",
        ["GET /api/v1/documents/zip"] = $"?caseId={Guid.Empty}",
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void EveryEndpoint_DeclaresHowItIsProtected()
    {
        var endpoints = Endpoints();
        var problems = endpoints
            .Select(endpoint => endpoint.Kind switch
            {
                Protection.Anonymous when !Anonymous.ContainsKey(endpoint.Key) => $"{endpoint.Key}: anonymous without a reason in {nameof(Anonymous)}",
                Protection.AuthenticatedOnly => $"{endpoint.Key}: authenticated only: use RequirePermission, RequireTenantUser or a platform requirement",
                Protection.None => $"{endpoint.Key}: no authorization at all",
                _ => null,
            })
            .OfType<string>()
            .Concat(Anonymous.Keys
                .Except(endpoints.Where(endpoint => endpoint.Kind == Protection.Anonymous).Select(endpoint => endpoint.Key))
                .Select(key => $"{key}: listed as anonymous but not an anonymous endpoint (stale entry)"))
            .ToList();

        problems.ShouldBeEmpty(string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public async Task ProtectedEndpoints_WithoutAToken_Answer401()
    {
        var violations = await SweepAsync(
            Endpoints().Where(endpoint => endpoint.Kind is not Protection.Anonymous),
            token: null,
            tenantHeader: ApiDatabase.TenantA,
            allowed: [HttpStatusCode.Unauthorized]);

        violations.ShouldBeEmpty(string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public async Task TenantScopedPlatformToken_NeverReachesTenantUserEndpoints()
    {
        var token = await PlatformTenantTokens.IssueAsync(factory, ApiDatabase.TenantA, Ct);

        var violations = await SweepAsync(
            Endpoints().Where(endpoint => endpoint.Kind is Protection.Permission or Protection.TenantUser),
            token,
            tenantHeader: null,
            allowed: [HttpStatusCode.Forbidden, HttpStatusCode.NotFound]);

        violations.ShouldBeEmpty(string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public async Task ConsoleToken_NeverReachesTenantEndpoints_EvenWithATenantHeader()
    {
        var token = await PlatformTenantTokens.SignInConsoleAsync(factory, Ct);

        var violations = await SweepAsync(
            Endpoints().Where(endpoint => endpoint.Kind is Protection.Permission or Protection.TenantUser or Protection.PlatformTenant),
            token,
            tenantHeader: ApiDatabase.TenantA,
            allowed: [HttpStatusCode.BadRequest, HttpStatusCode.Forbidden, HttpStatusCode.NotFound]);

        violations.ShouldBeEmpty(string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public async Task TenantAdministrator_NeverReachesPlatformEndpoints()
    {
        var token = await AdministratorTokenAsync();

        var violations = await SweepAsync(
            Endpoints().Where(endpoint => endpoint.Kind is Protection.PlatformConsole or Protection.PlatformTenant),
            token,
            tenantHeader: null,
            allowed: [HttpStatusCode.Forbidden]);

        violations.ShouldBeEmpty(string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public async Task TenantAdministrator_NeverReachesAnotherTenant()
    {
        var token = await AdministratorTokenAsync();

        var violations = await SweepAsync(
            Endpoints().Where(endpoint => endpoint.Kind is Protection.Permission or Protection.TenantUser or Protection.PlatformTenant),
            token,
            tenantHeader: ApiDatabase.TenantB,
            allowed: [HttpStatusCode.Forbidden]);

        violations.ShouldBeEmpty(string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public async Task RealtimeHub_RefusesTenantScopedPlatformTokens()
    {
        var token = await PlatformTenantTokens.IssueAsync(factory, ApiDatabase.TenantA, Ct);

        await Should.ThrowAsync<InvalidOperationException>(() => HubTestClient.ConnectAsync(factory.Server, "/hubs/notifications", token, Ct));
    }

    private async Task<string> AdministratorTokenAsync()
    {
        var (_, userName) = await factory.AddUserAsync([TenantRole.Administrator], Password);
        return await factory.SignInAsync(userName, Password, Ct);
    }

    /// <returns>One line per endpoint that answered outside <paramref name="allowed"/>.</returns>
    private async Task<List<string>> SweepAsync(
        IEnumerable<ApiEndpoint> endpoints, string? token, string? tenantHeader, IReadOnlyCollection<HttpStatusCode> allowed)
    {
        using var client = factory.CreateClient();
        var violations = new List<string>();
        foreach (var endpoint in endpoints)
        {
            using var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), endpoint.SamplePath() + SampleQueries.GetValueOrDefault(endpoint.Key));
            if (token is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            if (tenantHeader is not null)
            {
                request.Headers.Add("X-Tenant", tenantHeader);
            }

            if (endpoint.Multipart)
            {
                request.Content = new MultipartFormDataContent { { new ByteArrayContent([0x25, 0x50, 0x44, 0x46]), "file", "sample.pdf" } };
            }
            else if (endpoint.Method is "POST" or "PUT" or "PATCH")
            {
                request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            }

            using var response = await client.SendAsync(request, Ct);
            if (!allowed.Contains(response.StatusCode))
            {
                var body = await response.Content.ReadAsStringAsync(Ct);
                violations.Add($"{endpoint.Key} ({endpoint.Kind}): {(int)response.StatusCode} {body[..Math.Min(body.Length, 200)]}");
            }
        }

        return violations;
    }

    private List<ApiEndpoint> Endpoints()
    {
        // The host builds its endpoints on first use.
        using var _ = factory.CreateClient();
        var endpoints = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => (Endpoint: endpoint, Pattern: "/" + (endpoint.RoutePattern.RawText ?? string.Empty).Trim('/').Replace("{version:apiVersion}", "1", StringComparison.Ordinal)))
            .Where(item => item.Pattern.StartsWith("/api/v1/", StringComparison.Ordinal) && !item.Pattern.Contains("/test-", StringComparison.Ordinal))
            .SelectMany(item => (item.Endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(method => new ApiEndpoint(method, item.Pattern, Classify(item.Endpoint), IsMultipart(item.Endpoint))))
            .DistinctBy(endpoint => endpoint.Key)
            .OrderBy(endpoint => endpoint.Key, StringComparer.Ordinal)
            .ToList();

        // A sweep over nothing would pass: the host maps a few hundred endpoints.
        endpoints.Count.ShouldBeGreaterThan(100);
        return endpoints;
    }

    private static bool IsMultipart(Endpoint endpoint) =>
        endpoint.Metadata.GetMetadata<IAcceptsMetadata>()?.ContentTypes.Contains("multipart/form-data", StringComparer.OrdinalIgnoreCase) == true;

    private static Protection Classify(Endpoint endpoint)
    {
        var metadata = endpoint.Metadata;
        if (metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            return Protection.Anonymous;
        }

        if (metadata.GetMetadata<PlatformAccessMetadata>() is { } platform)
        {
            return platform.TenantScoped ? Protection.PlatformTenant : Protection.PlatformConsole;
        }

        if (metadata.GetMetadata<PermissionMetadata>() is not null)
        {
            return Protection.Permission;
        }

        if (metadata.GetMetadata<TenantUserMetadata>() is not null)
        {
            return Protection.TenantUser;
        }

        return metadata.GetMetadata<IAuthorizeData>() is not null ? Protection.AuthenticatedOnly : Protection.None;
    }

    private enum Protection
    {
        None,
        Anonymous,
        AuthenticatedOnly,
        TenantUser,
        Permission,
        PlatformConsole,
        PlatformTenant,
    }

    private sealed partial record ApiEndpoint(string Method, string Pattern, Protection Kind, bool Multipart)
    {
        public string Key => $"{Method} {Pattern}";

        /// <summary>The pattern with sample route values: a slug for <c>{slug}</c>, a language for <c>{lang}</c>, otherwise a new id.</summary>
        public string SamplePath() => RouteParameter().Replace(Pattern, match => match.Groups["name"].Value switch
        {
            "slug" => ApiDatabase.TenantA,
            "lang" => "it",
            _ => Guid.CreateVersion7().ToString(),
        });

        [GeneratedRegex(@"\{\**(?<name>[A-Za-z]+)[^}]*\}")]
        private static partial Regex RouteParameter();
    }

    public sealed class Factory : AuthEndpointsTests.Factory
    {
        public override async ValueTask InitializeAsync()
        {
            await base.InitializeAsync();
            await PlatformTenantTokens.EnsureConsoleClientAsync(Services);
        }
    }
}
