using System.Net;

using Microsoft.AspNetCore.HttpOverrides;

namespace Auxilia.Api.Infrastructure;

/// <summary>
/// The caller's address behind the Next.js BFF or a reverse proxy (P3-03): <c>X-Forwarded-For</c> is honoured only
/// from trusted peers (loopback by default, plus section <c>ForwardedHeaders</c>: <c>KnownProxies</c> addresses and
/// <c>KnownNetworks</c> CIDRs) and only its last hop, so sign-in rate limits and the login audit see the user, never a
/// forged address.
/// </summary>
internal static class ForwardedHeadersSetup
{
    public const string SectionName = "ForwardedHeaders";

    public static IServiceCollection AddAuxiliaForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        var proxies = section.GetSection("KnownProxies").Get<string[]>() ?? [];
        var networks = section.GetSection("KnownNetworks").Get<string[]>() ?? [];

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
            options.ForwardLimit = 1;
            foreach (var proxy in proxies)
            {
                options.KnownProxies.Add(IPAddress.Parse(proxy));
            }

            foreach (var network in networks)
            {
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }
        });

        return services;
    }
}
