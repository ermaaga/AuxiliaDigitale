namespace Auxilia.MigrationRunner.LegacyImport;

/// <summary>
/// The legacy document files, copied to a local directory (<c>--files</c>): the legacy <c>UploadedDocuments</c> folder
/// as it is, or the files downloaded from the legacy FTP or Azure container. A legacy <c>FilePath</c> (relative path,
/// absolute path or URL, each ending with <c>{guid}_{name}</c>) is found by its relative path, then by its last segment;
/// nothing outside the directory is ever read.
/// </summary>
internal sealed class LegacyFiles
{
    private readonly string? root;

    public LegacyFiles(string? root)
    {
        this.root = string.IsNullOrWhiteSpace(root) ? null : Path.GetFullPath(root) + Path.DirectorySeparatorChar;
    }

    /// <summary>Whether a directory was given: without it documents cannot be copied.</summary>
    public bool IsAvailable => root is not null;

    /// <summary>The full path of the legacy file in the directory, or <c>null</c>.</summary>
    public string? Locate(string filePath)
    {
        if (root is null || string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var path = Uri.TryCreate(filePath, UriKind.Absolute, out var uri) && !uri.IsFile ? Uri.UnescapeDataString(uri.AbsolutePath) : filePath;
        path = path.Replace('\\', '/');
        var candidates = new List<string>();
        if (!Path.IsPathRooted(path) && uri is null)
        {
            candidates.Add(path);
        }

        candidates.Add(path[(path.LastIndexOf('/') + 1)..]);
        foreach (var candidate in candidates.Where(candidate => candidate.Length > 0))
        {
            var full = Path.GetFullPath(Path.Combine(root, candidate));
            if (full.StartsWith(root, StringComparison.Ordinal) && File.Exists(full))
            {
                return full;
            }
        }

        return null;
    }

    /// <summary>The file name the user gave: the legacy storage prefixed it with <c>{guid}_</c>.</summary>
    public static string OriginalName(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        var underscore = fileName.IndexOf('_', StringComparison.Ordinal);
        return underscore == 36 && Guid.TryParse(fileName[..36], out _) ? fileName[37..] : fileName;
    }
}
