namespace Auxilia.MigrationRunner.Cli;

/// <summary>
/// Minimal parser for auxctl: leading words are the command path and positional values, <c>--name value</c> are
/// options, <c>--name</c> followed by another option (or nothing) is a flag. Written in-house (&lt; 100 lines) instead
/// of adding a CLI package (skill auxilia-dependency-policy).
/// </summary>
internal sealed class CommandLine
{
    private readonly Dictionary<string, string> options = new(StringComparer.Ordinal);
    private readonly HashSet<string> flags = new(StringComparer.Ordinal);

    private CommandLine(IReadOnlyList<string> words) => Words = words;

    public IReadOnlyList<string> Words { get; }

    public static CommandLine Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var words = new List<string>();
        var parsed = new CommandLine(words);
        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                words.Add(arg);
                continue;
            }

            var name = arg[2..];
            if (index + 1 < args.Count && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                parsed.options[name] = args[++index];
            }
            else
            {
                parsed.flags.Add(name);
            }
        }

        return parsed;
    }

    public bool Is(params string[] path) =>
        Words.Count >= path.Length && path.Select((word, index) => Words[index] == word).All(match => match);

    public string? Word(int index) => index < Words.Count ? Words[index] : null;

    public string? Option(string name) => options.GetValueOrDefault(name);

    public bool Flag(string name) => flags.Contains(name);
}
