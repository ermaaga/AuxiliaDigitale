using System.Security.Cryptography;
using System.Text;

using Auxilia.Application.Abstractions.Storage;
using Auxilia.Infrastructure.Adapters.Storage.AzureBlob;
using Auxilia.Infrastructure.Adapters.Storage.Ftp;
using Auxilia.Infrastructure.Adapters.Storage.Local;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

using Microsoft.Extensions.Options;

namespace Auxilia.Infrastructure.Tests.Adapters.Storage;

/// <summary>The same contract for every adapter: write, read, move (staging → commit), delete, missing files.</summary>
public abstract class FileStorageContractTests
{
    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected abstract IFileStorage Storage { get; }

    protected abstract StorageTarget Target { get; }

    private static async Task<string?> ReadAsync(Stream? stream)
    {
        if (stream is null)
        {
            return null;
        }

        await using (stream)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadToEndAsync(Ct);
        }
    }

    [Fact]
    public async Task WriteReadMoveDelete_RoundTrip()
    {
        var staging = $"tenants/demo/staging/{Guid.NewGuid():N}";
        var final = $"tenants/demo/documents/2026/10/{Guid.NewGuid():N}.txt";
        Storage.IsConfigured(Target).ShouldBeTrue();

        await Storage.WriteAsync(Target, staging, new MemoryStream("first"u8.ToArray()), Ct);
        await Storage.WriteAsync(Target, staging, new MemoryStream("hello"u8.ToArray()), Ct);
        (await ReadAsync(await Storage.OpenReadAsync(Target, staging, Ct))).ShouldBe("hello");

        await Storage.MoveAsync(Target, staging, final, Ct);
        (await Storage.OpenReadAsync(Target, staging, Ct)).ShouldBeNull();
        (await ReadAsync(await Storage.OpenReadAsync(Target, final, Ct))).ShouldBe("hello");

        await Storage.DeleteAsync(Target, final, Ct);
        await Storage.DeleteAsync(Target, final, Ct);
        (await Storage.OpenReadAsync(Target, final, Ct)).ShouldBeNull();
    }
}

public sealed class LocalFileStorageTests : FileStorageContractTests, IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "auxilia-storage-" + Guid.NewGuid().ToString("N"));

    public LocalFileStorageTests()
    {
        Storage = new LocalFileStorage(Options.Create(new LocalStorageOptions { RootPath = root }));
    }

    protected override IFileStorage Storage { get; }

    protected override StorageTarget Target { get; } = new();

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public async Task KeysCannotLeaveTheRoot_AndNoHalfWrittenFileStays()
    {
        await Should.ThrowAsync<ArgumentException>(() => Storage.WriteAsync(Target, "../outside.txt", new MemoryStream([1]), Ct));

        await Should.ThrowAsync<IOException>(() => Storage.WriteAsync(Target, "tenants/demo/staging/broken", new FailingStream(), Ct));
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    private sealed class FailingStream : MemoryStream
    {
        public FailingStream()
            : base([1, 2, 3])
        {
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new IOException("Connection reset");
    }
}

/// <summary>Azure Blob on Azurite, with an account created for the run (no key in the repository).</summary>
public sealed class AzureBlobFileStorageTests : FileStorageContractTests, IAsyncLifetime
{
    private const string Account = "auxtests";

    private readonly string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    private readonly IContainer azurite;

    public AzureBlobFileStorageTests()
    {
        azurite = new ContainerBuilder("mcr.microsoft.com/azure-storage/azurite:3.35.0")
            .WithCommand("azurite-blob", "--blobHost", "0.0.0.0", "--skipApiVersionCheck")
            .WithEnvironment("AZURITE_ACCOUNTS", $"{Account}:{key}")
            .WithPortBinding(10000, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("successfully listens"))
            .Build();
    }

    protected override IFileStorage Storage { get; } = new AzureBlobFileStorage();

    protected override StorageTarget Target => new(
        AzureConnectionString: $"DefaultEndpointsProtocol=http;AccountName={Account};AccountKey={key};BlobEndpoint=http://{azurite.Hostname}:{azurite.GetMappedPublicPort(10000)}/{Account};",
        AzureContainer: "documents");

    public async ValueTask InitializeAsync() => await azurite.StartAsync();

    public async ValueTask DisposeAsync() => await azurite.DisposeAsync();

    [Fact]
    public async Task DeletingFromAMissingContainer_IsFine()
    {
        await Storage.DeleteAsync(Target with { AzureContainer = "never-created" }, "tenants/demo/x", Ct);
        (await Storage.OpenReadAsync(Target with { AzureContainer = "never-created" }, "tenants/demo/x", Ct)).ShouldBeNull();
    }
}

public sealed class FtpFileStorageTests
{
    [Theory]
    [InlineData("/documents", "tenants/demo/a.pdf", "/documents/tenants/demo/a.pdf")]
    [InlineData("/documents/", "/tenants/demo/a.pdf", "/documents/tenants/demo/a.pdf")]
    [InlineData(null, "tenants/demo/a.pdf", "/tenants/demo/a.pdf")]
    public void Keys_GoUnderTheConfiguredFolder(string? path, string key, string expected) =>
        FtpFileStorage.PathOf(new StorageTarget(FtpPath: path), key).ShouldBe(expected);

    [Fact]
    public void IsConfigured_NeedsAHost()
    {
        var storage = new FtpFileStorage();

        storage.IsConfigured(new StorageTarget()).ShouldBeFalse();
        storage.IsConfigured(new StorageTarget(FtpHost: "ftp.example.test")).ShouldBeTrue();
    }
}
