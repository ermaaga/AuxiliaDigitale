using Auxilia.Api.IntegrationTests.Identity;
using Auxilia.Application.Abstractions.Realtime;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Contracts.Realtime;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.DependencyInjection;

using Testcontainers.Redis;

namespace Auxilia.Api.IntegrationTests.Realtime;

/// <summary>Two API nodes on one Valkey: a push made on one node reaches a connection held by the other (backplane).</summary>
public sealed class RedisBackplaneTests : IClassFixture<RedisBackplaneTests.Nodes>
{
    private const string Password = "a long enough password";

    private readonly Nodes nodes;

    public RedisBackplaneTests(Nodes nodes) => this.nodes = nodes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PushOnOneNode_ReachesAConnectionOnTheOther()
    {
        var (userId, userName) = await nodes.First.AddUserAsync([TenantRole.Employee], Password);
        await using var connection = await HubTestClient.ConnectAsync(
            nodes.First.Server, "/hubs/notifications", await nodes.First.SignInAsync(userName, Password, Ct), Ct);

        await using var scope = nodes.Second.Services.CreateAsyncScope();
        var tenant = await scope.ServiceProvider.GetRequiredService<ITenantDirectory>().FindBySlugAsync(ApiDatabase.TenantA, Ct);
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().Set(tenant!);
        await scope.ServiceProvider.GetRequiredService<IRealtimeNotifier>()
            .ToUserAsync(userId, RealtimeEvents.NotificationReceived, new { title = "from node 2" }, Ct);

        (await connection.NextInvocationAsync(RealtimeEvents.NotificationReceived, Ct)).GetProperty("title").GetString().ShouldBe("from node 2");
    }

    public sealed class Nodes : IAsyncLifetime
    {
        private readonly RedisContainer valkey = new RedisBuilder("valkey/valkey:8-alpine").Build();

        public Node First { get; private set; } = null!;

        public Node Second { get; private set; } = null!;

        public async ValueTask InitializeAsync()
        {
            await valkey.StartAsync();
            First = new Node(valkey.GetConnectionString());
            Second = new Node(valkey.GetConnectionString());
            await First.InitializeAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await First.DisposeAsync();
            await Second.DisposeAsync();
            await valkey.DisposeAsync();
        }
    }

    public sealed class Node(string redis) : AuthEndpointsTests.Factory
    {
        protected override string RedisConnectionString => redis;
    }
}
