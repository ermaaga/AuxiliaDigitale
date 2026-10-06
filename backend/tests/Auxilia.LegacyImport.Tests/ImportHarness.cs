using System.Security.Cryptography;

using Auxilia.Application;
using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Channels;
using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Images;
using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Documents.Public;
using Auxilia.Diagnostics;
using Auxilia.Domain.Identity;
using Auxilia.MigrationRunner.LegacyImport;
using Auxilia.Persistence.Tenant;
using Auxilia.Persistence.Tenant.Interceptors;
using Auxilia.SharedKernel.Results;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using NSubstitute;

namespace Auxilia.LegacyImport.Tests;

/// <summary>What the import tests share: the tenant context as auxctl opens it and the importer with test services.</summary>
internal static class ImportHarness
{
    public static readonly TimeZoneInfo Rome = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");

    /// <summary>The module registry as the hosts build it (grids and permissions of every module).</summary>
    public static readonly Lazy<IModuleRegistry> Modules = new(() =>
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        return services.BuildServiceProvider().GetRequiredService<IModuleRegistry>();
    });

    public static TenantDbContext Context(NpgsqlDataSource tenant)
    {
        var system = Substitute.For<ICurrentUser>();
        system.ActorType.Returns(ActorType.System);
        return new TenantDbContext(TenantDbContextOptions.Create(tenant, new TenantAuditInterceptor(system, TimeProvider.System)));
    }

    public static async Task<LegacyImportReport> ImportAsync(
        LegacySource source, NpgsqlDataSource tenant, bool dryRun = false, IPasswordHasher? hasher = null, IFileStore? files = null, string? filesRoot = null)
    {
        if (hasher is null)
        {
            hasher = Substitute.For<IPasswordHasher>();
            hasher.Verify(Arg.Any<string>(), Arg.Any<PasswordFormat>(), Arg.Any<string>()).Returns(PasswordVerification.Failed);
        }

        var importer = new LegacyImporter(new LegacyImportServices(
            hasher, Substitute.For<IImageProcessor>(), files ?? new MemoryFileStore(), new LegacyFiles(filesRoot), new PlainSecrets(), Modules.Value));
        await using var db = Context(tenant);
        return await importer.RunAsync(source, db, TimeProvider.System, Rome, "it", dryRun, TestContext.Current.CancellationToken);
    }
}

/// <summary>Secrets marked instead of encrypted, so the tests can see what was protected.</summary>
internal sealed class PlainSecrets : IAccountSecretProtector
{
    public string Protect(string secret) => $"protected:{secret}";

    public string Unprotect(string protectedSecret) => protectedSecret["protected:".Length..];
}

/// <summary>A file store in memory: accepts files with an extension, as the tenant's store would after its checks.</summary>
internal sealed class MemoryFileStore : IFileStore
{
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

    public async Task<Result<StagedFile>> StageAsync(Stream content, string? fileName, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty).TrimStart('.').ToLowerInvariant();
        if (extension is not ("pdf" or "jpg" or "png"))
        {
            return Errors.Documents.FileTypeNotAllowed();
        }

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var key = $"tenants/t/staging/{Guid.CreateVersion7():N}";
        Files[key] = buffer.ToArray();
        return new StagedFile(key, fileName!, extension, extension == "pdf" ? "application/pdf" : $"image/{extension}", buffer.Length,
            Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray())));
    }

    public Task<Result<string>> CommitAsync(StagedFile file, string area, CancellationToken cancellationToken)
    {
        var key = $"tenants/t/{area}/2026/10/{Guid.CreateVersion7():N}.{file.Extension}";
        Files[key] = Files[file.StagingKey];
        Files.Remove(file.StagingKey);
        return Task.FromResult(Result.Success(key));
    }

    public Task<Result<Stream?>> OpenReadAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success<Stream?>(Files.TryGetValue(key, out var bytes) ? new MemoryStream(bytes) : null));

    public Task<Result> DeleteAsync(string key, CancellationToken cancellationToken)
    {
        Files.Remove(key);
        return Task.FromResult(Result.Success());
    }
}
