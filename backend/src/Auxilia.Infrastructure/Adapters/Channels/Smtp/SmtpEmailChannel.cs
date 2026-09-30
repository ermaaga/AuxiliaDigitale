using System.Text.Json;
using System.Text.Json.Serialization;

using Auxilia.Application.Abstractions.Channels;
using Auxilia.Diagnostics;
using Auxilia.Domain.Messaging;

using MailKit.Net.Smtp;
using MailKit.Security;

using MimeKit;

namespace Auxilia.Infrastructure.Adapters.Channels.Smtp;

/// <summary>How the SMTP connection is secured (N03).</summary>
public enum SmtpSecurity
{
    None,
    StartTls,
    SslOnConnect,
}

/// <summary>Non-secret settings of an <c>smtp</c> account; the password is the account secret.</summary>
public sealed record SmtpSettings(string Host, int Port, SmtpSecurity Security, string? Username, string FromAddress, string? FromName)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static SmtpSettings? Parse(string settingsJson)
    {
        try
        {
            return JsonSerializer.Deserialize<SmtpSettings>(settingsJson, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>E-mail through SMTP with MailKit (provider <c>smtp</c>). One connection per message.</summary>
internal sealed class SmtpEmailChannel : IMessageChannel
{
    public const string ProviderKey = "smtp";

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public string Provider => ProviderKey;

    public MessageChannel Channel => MessageChannel.Email;

    public bool AreSettingsValid(string settingsJson) =>
        SmtpSettings.Parse(settingsJson) is { } settings
        && !string.IsNullOrWhiteSpace(settings.Host)
        && settings.Port is > 0 and <= 65535
        && Enum.IsDefined(settings.Security)
        && IsValidRecipient(settings.FromAddress);

    public bool IsValidRecipient(string recipient) =>
        !string.IsNullOrWhiteSpace(recipient) && MailboxAddress.TryParse(recipient, out var address) && address.Address.Contains('@', StringComparison.Ordinal);

    public async Task SendAsync(ChannelMessage message, ChannelAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(account);

        var settings = SmtpSettings.Parse(account.SettingsJson) ?? throw new ChannelPermanentException(Errors.Messaging.AccountSettingsInvalid("settings"));
        if (!IsValidRecipient(message.Recipient))
        {
            throw new ChannelPermanentException(Errors.Messaging.RecipientInvalid());
        }

        using var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(settings.FromName ?? string.Empty, settings.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.Recipient));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.Body }.ToMessageBody();

        using var client = new SmtpClient { Timeout = (int)Timeout.TotalMilliseconds };
        try
        {
            await client.ConnectAsync(settings.Host, settings.Port, Options(settings.Security), cancellationToken);
            if (!string.IsNullOrEmpty(settings.Username))
            {
                await client.AuthenticateAsync(settings.Username, account.Secret ?? string.Empty, cancellationToken);
            }

            await client.SendAsync(mime, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);
        }
        catch (AuthenticationException exception)
        {
            throw new ChannelPermanentException(Errors.Messaging.AccountAuthenticationFailed(), exception);
        }
        catch (SmtpCommandException exception) when (IsPermanent(exception))
        {
            throw new ChannelPermanentException(
                exception.ErrorCode == SmtpErrorCode.RecipientNotAccepted ? Errors.Messaging.RecipientInvalid() : Errors.Messaging.ChannelNotAvailable(ProviderKey),
                exception);
        }
    }

    /// <summary>5xx replies are permanent (RFC 5321); 4xx and connection problems are retried.</summary>
    private static bool IsPermanent(SmtpCommandException exception) => (int)exception.StatusCode >= 500;

    private static SecureSocketOptions Options(SmtpSecurity security) => security switch
    {
        SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
        SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        _ => SecureSocketOptions.None,
    };
}
