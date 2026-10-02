using System.Text;

using Auxilia.ServiceDefaults.Logging;
using Auxilia.ServiceDefaults.Logging.Storage;

using Serilog.Formatting.Compact;

namespace Auxilia.ServiceDefaults.Tests.Logging;

public sealed class TenantFileSinkTests : IDisposable
{
    private readonly RecordingStore store = new();
    private readonly RecordingStore buffer = new();
    private readonly StringWriter console = new();

    [Fact]
    public async Task EmitBatchAsync_WritesOneJsonLinePerEventInItsTenantFile()
    {
        var sink = CreateSink();

        await sink.EmitBatchAsync([LogEvents.Create(tenant: "acme"), LogEvents.Create(tenant: "acme"), LogEvents.Create()]);

        store.Lines("tenants/acme/2026/09/29.jsonl").Length.ShouldBe(2);
        store.Lines("platform/2026/09/29.jsonl").Length.ShouldBe(1);
        buffer.Files.ShouldBeEmpty();
        console.ToString().ShouldBeEmpty();
    }

    [Fact]
    public async Task EmitBatchAsync_StorageFails_BuffersLocallyAndReportsOnce()
    {
        store.Fail = true;
        var sink = CreateSink();

        await sink.EmitBatchAsync([LogEvents.Create(tenant: "acme")]);
        await sink.EmitBatchAsync([LogEvents.Create(tenant: "acme")]);

        buffer.Lines("tenants/acme/2026/09/29.jsonl").Length.ShouldBe(2);
        var reports = console.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        reports.Length.ShouldBe(1);
        reports[0].ShouldContain("\"EventCode\":\"AUX-10015\"");
    }

    [Fact]
    public async Task EmitBatchAsync_StorageRecovers_ReportsRecovery()
    {
        store.Fail = true;
        var sink = CreateSink();
        await sink.EmitBatchAsync([LogEvents.Create()]);

        store.Fail = false;
        await sink.EmitBatchAsync([LogEvents.Create()]);

        store.Lines("platform/2026/09/29.jsonl").Length.ShouldBe(1);
        console.ToString().ShouldContain("\"EventCode\":\"AUX-10016\"");
    }

    [Fact]
    public async Task EmitBatchAsync_StorageAndBufferFail_Throws()
    {
        // Serilog keeps the batch and retries it.
        store.Fail = true;
        buffer.Fail = true;
        var sink = CreateSink();

        await Should.ThrowAsync<IOException>(() => sink.EmitBatchAsync([LogEvents.Create()]));
    }

    public void Dispose() => console.Dispose();

    private TenantFileSink CreateSink() =>
        new(store, buffer, new RenderedCompactJsonFormatter(), console, new ManualTimeProvider(LogEvents.Noon));

    private sealed class RecordingStore : ILogFileStore
    {
        public Dictionary<string, StringBuilder> Files { get; } = new(StringComparer.Ordinal);

        public bool Fail { get; set; }

        public Task AppendAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new IOException("storage down");
            }

            if (!Files.TryGetValue(path, out var file))
            {
                Files[path] = file = new StringBuilder();
            }

            file.Append(Encoding.UTF8.GetString(content.Span));
            return Task.CompletedTask;
        }

        public Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult<Stream?>(Files.TryGetValue(path, out var file) ? new MemoryStream(Encoding.UTF8.GetBytes(file.ToString())) : null);

        public string[] Lines(string path) =>
            Files[path].ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }
}
