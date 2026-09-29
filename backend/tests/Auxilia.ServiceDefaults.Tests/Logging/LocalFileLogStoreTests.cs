using System.Text;

using Auxilia.ServiceDefaults.Logging.Storage;

namespace Auxilia.ServiceDefaults.Tests.Logging;

public sealed class LocalFileLogStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "auxilia-logs-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AppendAsync_CreatesFoldersAndAppends()
    {
        var store = new LocalFileLogStore(root);

        await store.AppendAsync("tenants/acme/2026/09/29.jsonl", Encoding.UTF8.GetBytes("a\n"), TestContext.Current.CancellationToken);
        await store.AppendAsync("tenants/acme/2026/09/29.jsonl", Encoding.UTF8.GetBytes("b\n"), TestContext.Current.CancellationToken);

        (await File.ReadAllTextAsync(Path.Combine(root, "tenants", "acme", "2026", "09", "29.jsonl"), TestContext.Current.CancellationToken)).ShouldBe("a\nb\n");
    }

    [Fact]
    public async Task AppendAsync_PathOutsideRoot_Throws()
    {
        var store = new LocalFileLogStore(root);

        await Should.ThrowAsync<ArgumentException>(
            () => store.AppendAsync("../escape.jsonl", Encoding.UTF8.GetBytes("x"), TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
