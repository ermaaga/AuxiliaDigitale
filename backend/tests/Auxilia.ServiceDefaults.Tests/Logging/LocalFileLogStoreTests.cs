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

    [Fact]
    public async Task OpenReadAsync_ReadsWhileAnotherWriterAppends()
    {
        var store = new LocalFileLogStore(root);
        await store.AppendAsync("tenants/acme/2026/09/29.jsonl", Encoding.UTF8.GetBytes("a\n"), TestContext.Current.CancellationToken);

        await using var stream = await store.OpenReadAsync("tenants/acme/2026/09/29.jsonl", TestContext.Current.CancellationToken);
        await store.AppendAsync("tenants/acme/2026/09/29.jsonl", Encoding.UTF8.GetBytes("b\n"), TestContext.Current.CancellationToken);

        stream.ShouldNotBeNull();
        using var reader = new StreamReader(stream);
        (await reader.ReadToEndAsync(TestContext.Current.CancellationToken)).ShouldBe("a\nb\n");
    }

    [Fact]
    public async Task OpenReadAsync_MissingFile_ReturnsNull()
    {
        var store = new LocalFileLogStore(root);

        (await store.OpenReadAsync("tenants/acme/2026/09/29.jsonl", TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task OpenReadAsync_PathOutsideRoot_Throws()
    {
        var store = new LocalFileLogStore(root);

        await Should.ThrowAsync<ArgumentException>(() => store.OpenReadAsync("../escape.jsonl", TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
