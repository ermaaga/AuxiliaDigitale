using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Documents.Public;

/// <summary>
/// A file checked and written to staging (<c>tenants/{slug}/staging/…</c>): name sanitised as the legacy app did (Q51),
/// type accepted and matching its content, size within <c>documents.maxUploadMb</c>, SHA-256 of the content.
/// </summary>
public sealed record StagedFile(string StagingKey, string FileName, string Extension, string ContentType, long Size, string Sha256);

/// <summary>
/// The files of the current tenant on its storage (F14, ARCHITECTURE §6): uploads go to staging first and are
/// committed after the database transaction (so a failed operation leaves no committed file), keys are always under
/// <c>tenants/{slug}/</c> (other keys are refused). Used by Documents (B-12) and Imports (S-08).
/// </summary>
public interface IFileStore
{
    /// <summary>Checks and writes the upload to staging, streaming it (never fully in memory, Q49).</summary>
    Task<Result<StagedFile>> StageAsync(Stream content, string? fileName, CancellationToken cancellationToken);

    /// <summary>Moves a staged file to <c>tenants/{slug}/{area}/{yyyy}/{MM}/{id}.{ext}</c> and returns that key.</summary>
    Task<Result<string>> CommitAsync(StagedFile file, string area, CancellationToken cancellationToken);

    /// <summary>The content of a key of the tenant; <c>null</c> when the file is missing (legacy "FileNotFound").</summary>
    Task<Result<Stream?>> OpenReadAsync(string key, CancellationToken cancellationToken);

    /// <summary>Deletes a key of the tenant (staged or committed); missing files are fine.</summary>
    Task<Result> DeleteAsync(string key, CancellationToken cancellationToken);
}
