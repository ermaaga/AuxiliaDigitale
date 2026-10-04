using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Identity;
using Auxilia.Contracts.Identity;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Tests.Identity;

public sealed class ActiveSessionQueryTests
{
    private readonly FakeReader reader = new();
    private readonly ManualTimeProvider clock = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task List_MarksTheCallersSession_SortsRolesAndPassesTheFilter()
    {
        var current = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var now = clock.GetUtcNow();
        reader.Rows.AddRange([
            new ActiveSessionRow(current, Guid.CreateVersion7(), "anna", "Anna Bianchi", [TenantRole.Employee, TenantRole.Administrator], "web", "10.0.0.1", "Firefox", now, now, now.AddHours(2)),
            new ActiveSessionRow(other, Guid.CreateVersion7(), "luca", "Luca Blu", [TenantRole.Client], "web", null, null, now, now, now.AddHours(2)),
        ]);
        var query = new ActiveSessionQueryService(reader, clock);

        var page = (await query.ListAsync(new ActiveSessionQuery(" anna ", "userName", 1, 25), current, Ct)).Value;

        page.Items.Select(item => (item.Id, item.IsCurrent)).ShouldBe([(current, true), (other, false)]);
        page.Items[0].Roles.ShouldBe(["Administrator", "Employee"]);
        reader.LastFilter.ShouldBe(new ActiveSessionFilter("anna", ActiveSessionSort.UserName, false, 0, 25, now));

        await query.ListAsync(new ActiveSessionQuery(null, null, 2, 10), null, Ct);
        (reader.LastFilter!.Sort, reader.LastFilter.Descending, reader.LastFilter.Skip).ShouldBe((ActiveSessionSort.LastUsedAt, true, 10));
    }

    [Fact]
    public async Task List_ValidatesTheQuery()
    {
        var query = new ActiveSessionQueryService(reader, clock);

        var invalid = await query.ListAsync(new ActiveSessionQuery(new string('x', 201), "ipAddress", 0, 101), null, Ct);

        invalid.Error!.ValidationErrors.Keys.ShouldBe(["page", "pageSize", "sort", "search"], ignoreOrder: true);
    }

    [Fact]
    public async Task Summary_CountsActiveNowInTheLastFiveMinutes()
    {
        reader.Counts = new ActiveSessionCounts(7, 4, 2);
        var query = new ActiveSessionQueryService(reader, clock);

        (await query.SummaryAsync(Ct)).ShouldBe(new ActiveSessionSummaryResponse(7, 4, 2));
        reader.ActiveSince.ShouldBe(clock.GetUtcNow().AddMinutes(-5));
    }

    private sealed class FakeReader : IActiveSessionReader
    {
        public List<ActiveSessionRow> Rows { get; } = [];

        public ActiveSessionFilter? LastFilter { get; private set; }

        public ActiveSessionCounts Counts { get; set; } = new(0, 0, 0);

        public DateTimeOffset? ActiveSince { get; private set; }

        public Task<(IReadOnlyList<ActiveSessionRow> Items, int Total)> PageAsync(ActiveSessionFilter filter, CancellationToken cancellationToken)
        {
            LastFilter = filter;
            return Task.FromResult<(IReadOnlyList<ActiveSessionRow>, int)>((Rows, Rows.Count));
        }

        public Task<ActiveSessionCounts> CountAsync(DateTimeOffset now, DateTimeOffset activeSince, CancellationToken cancellationToken)
        {
            ActiveSince = activeSince;
            return Task.FromResult(Counts);
        }
    }
}
