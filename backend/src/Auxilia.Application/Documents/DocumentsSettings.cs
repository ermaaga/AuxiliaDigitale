using Auxilia.Application.Abstractions.Settings;

namespace Auxilia.Application.Documents;

/// <summary>
/// Document storage settings (ARCHITECTURE §7.2): the provider per tenant and its connection (FTP server, Azure
/// container); secrets encrypted. The local provider writes under the infrastructure path <c>Storage:Local:RootPath</c>.
/// </summary>
public static class DocumentsSettings
{
    public const string Module = "Documents";

    /// <summary>Keys of the <c>IFileStorage</c> adapters (ARCHITECTURE §6).</summary>
    public static readonly IReadOnlyList<string> StorageProviders = ["local", "ftp", "azure-blob"];

    public static readonly SettingDefinition<string> StorageProvider = new(
        "documents.storage.provider", Module, "local", choices: StorageProviders);

    /// <summary>Maximum size of one uploaded file in MB (60, as the legacy upload limit).</summary>
    public static readonly SettingDefinition<int> MaxUploadMb = new(
        "documents.maxUploadMb", Module, 60, isValid: megabytes => megabytes is >= 1 and <= 1024);

    /// <summary>FTP server (legacy <c>FtpStorage:Host</c>), e.g. <c>ftp.example.com</c>.</summary>
    public static readonly SettingDefinition<string> FtpHost = new(
        "documents.storage.ftp.host", Module, string.Empty, isValid: host => host.Length <= 255 && !host.Contains('/', StringComparison.Ordinal));

    public static readonly SettingDefinition<int> FtpPort = new("documents.storage.ftp.port", Module, 21, isValid: port => port is >= 1 and <= 65535);

    public static readonly SettingDefinition<string> FtpUser = new("documents.storage.ftp.user", Module, string.Empty, isValid: user => user.Length <= 255);

    public static readonly SecretSettingDefinition FtpPassword = new("documents.storage.ftp.password", Module);

    /// <summary>Folder on the server under which the keys are written (legacy <c>FtpStorage:Path</c>).</summary>
    public static readonly SettingDefinition<string> FtpPath = new(
        "documents.storage.ftp.path", Module, "/documents", isValid: path => path.StartsWith('/') && path.Length <= 255 && !path.Contains("..", StringComparison.Ordinal));

    /// <summary>Explicit FTPS (on by default; the legacy used plain FTP).</summary>
    public static readonly SettingDefinition<bool> FtpTls = new("documents.storage.ftp.tls", Module, true);

    public static readonly SecretSettingDefinition AzureConnectionString = new("documents.storage.azure.connectionString", Module);

    /// <summary>Blob container (3–63 lower-case letters, digits, hyphens).</summary>
    public static readonly SettingDefinition<string> AzureContainer = new(
        "documents.storage.azure.container", Module, "documents",
        isValid: name => name.Length is >= 3 and <= 63 && name.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-'));

    public static IReadOnlyList<SettingDefinition> All { get; } =
        [StorageProvider, MaxUploadMb, FtpHost, FtpPort, FtpUser, FtpPassword, FtpPath, FtpTls, AzureConnectionString, AzureContainer];
}
