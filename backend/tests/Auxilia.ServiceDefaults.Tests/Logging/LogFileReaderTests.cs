using System.Text;

using Auxilia.ServiceDefaults.Logging;
using Auxilia.ServiceDefaults.Logging.Storage;

namespace Auxilia.ServiceDefaults.Tests.Logging;

public sealed class LogFileReaderTests : IDisposable
{
    private static readonly DateOnly Day = new(2026, 9, 29);

    private readonly string root = Path.Combine(Path.GetTempPath(), "auxilia-logs-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ReadTenantDayAsync_ReturnsTheLinesOfThatTenantAndDay()
    {
        var store = new LocalFileLogStore(root);
        await store.AppendAsync("tenants/acme/2026/09/29.jsonl", Encoding.UTF8.GetBytes("{\"a\":1}\n\n{\"b\":2}\n"), TestContext.Current.CancellationToken);
        await store.AppendAsync("tenants/other/2026/09/29.jsonl", Encoding.UTF8.GetBytes("{\"c\":3}\n"), TestContext.Current.CancellationToken);

        var lines = await new LogFileReader(store).ReadTenantDayAsync("acme", Day, TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);

        lines.ShouldBe(["{\"a\":1}", "{\"b\":2}"]);
    }

    [Fact]
    public async Task ReadTenantDayAsync_NoFile_ReturnsNothing()
    {
        var lines = await new LogFileReader(new LocalFileLogStore(root))
            .ReadTenantDayAsync("acme", Day, TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);

        lines.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("../platform")]
    [InlineData("ACME")]
    public async Task ReadTenantDayAsync_InvalidSlug_NeverReadsAnotherFile(string slug)
    {
        var store = new LocalFileLogStore(root);
        await store.AppendAsync("platform/2026/09/29.jsonl", Encoding.UTF8.GetBytes("{\"p\":1}\n"), TestContext.Current.CancellationToken);

        var lines = await new LogFileReader(store).ReadTenantDayAsync(slug, Day, TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);

        lines.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReadTenantDayAsync_NoStorage_ReturnsNothing()
    {
        var lines = await new LogFileReader(null).ReadTenantDayAsync("acme", Day, TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);

        lines.ShouldBeEmpty();
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
