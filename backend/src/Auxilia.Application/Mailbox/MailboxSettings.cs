using System.Globalization;

using Auxilia.Application.Abstractions.Settings;

namespace Auxilia.Application.Mailbox;

/// <summary>Settings of the mailbox module (N05).</summary>
public static class MailboxSettings
{
    public const string Module = "Mailbox";

    /// <summary>Ports a user-entered IMAP server may use, comma separated (993 = TLS, 143 = STARTTLS).</summary>
    public static readonly SettingDefinition<string> ImapAllowedPorts = new(
        "mailbox.imap.allowedPorts", Module, "993,143", isValid: value => ParsePorts(value) is { Count: > 0 });

    public static IReadOnlyList<SettingDefinition> All { get; } = [ImapAllowedPorts];

    /// <returns>Null when a part is not a port.</returns>
    public static IReadOnlySet<int>? ParsePorts(string? value)
    {
        var ports = new HashSet<int>();
        foreach (var part in (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
            {
                return null;
            }

            ports.Add(port);
        }

        return ports;
    }
}
