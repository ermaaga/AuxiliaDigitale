using Auxilia.Application.Abstractions.Settings;

namespace Auxilia.Application.Documents;

/// <summary>Document storage settings (ARCHITECTURE §7.2; credentials of the providers arrive with B-11).</summary>
public static class DocumentsSettings
{
    public const string Module = "Documents";

    /// <summary>Keys of the <c>IFileStorage</c> adapters (ARCHITECTURE §6).</summary>
    public static readonly IReadOnlyList<string> StorageProviders = ["local", "ftp", "azure-blob"];

    public static readonly SettingDefinition<string> StorageProvider = new(
        "documents.storage.provider", Module, "local", isValid: StorageProviders.Contains);

    /// <summary>Maximum size of one uploaded file in MB (60, as the legacy upload limit).</summary>
    public static readonly SettingDefinition<int> MaxUploadMb = new(
        "documents.maxUploadMb", Module, 60, isValid: megabytes => megabytes is >= 1 and <= 1024);

    public static IReadOnlyList<SettingDefinition> All { get; } = [StorageProvider, MaxUploadMb];
}
