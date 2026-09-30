using System.Net;
using System.Net.Http.Json;

using Auxilia.Api.Endpoints;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Auxilia.Api.IntegrationTests.Host;

/// <summary>The caller's address behind the BFF: X-Forwarded-For only from trusted peers, last hop only.</summary>
public sealed class ForwardedHeadersTests : IClassFixture<ForwardedHeadersTests.Factory>
{
    private const string PeerHeader = "X-Test-Peer";

    private readonly Factory factory;

    public ForwardedHeadersTests(Factory factory) => this.factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("127.0.0.1", "203.0.113.9", "203.0.113.9")]
    [InlineData("10.1.2.3", "203.0.113.9", "203.0.113.9")]
    [InlineData("127.0.0.1", "198.51.100.7, 203.0.113.9", "203.0.113.9")]
    [InlineData("198.51.100.1", "203.0.113.9", "198.51.100.1")]
    [InlineData("127.0.0.1", null, "127.0.0.1")]
    public async Task ForwardedFor_IsTrustedOnlyFromKnownPeers(string peer, string? forwardedFor, string expected)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/test-ip");
        request.Headers.Add(PeerHeader, peer);
        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        using var response = await client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<string>(Ct)).ShouldBe(expected);
    }

    public sealed class Factory : ApiFactory
    {
        protected override IApiEndpoints? Endpoints { get; } = new IpEndpoints();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("ForwardedHeaders:KnownNetworks:0", "10.0.0.0/8");
            builder.ConfigureServices(services => services.AddTransient<IStartupFilter, PeerStartupFilter>());
        }
    }

    /// <summary>The test server has no peer address: set it from a header, before the API pipeline runs.</summary>
    private sealed class PeerStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (IPAddress.TryParse(context.Request.Headers[PeerHeader].ToString(), out var peer))
                {
                    context.Connection.RemoteIpAddress = peer;
                }

                return nextMiddleware(context);
            });
            next(app);
        };
    }

    private sealed class IpEndpoints : IApiEndpoints
    {
        public void Map(RouteGroupBuilder api) =>
            api.MapGet("/test-ip", (HttpContext context) => TypedResults.Ok(context.Connection.RemoteIpAddress?.ToString()))
                .AllowAnonymous()
                .ExcludeFromDescription();
    }
}
