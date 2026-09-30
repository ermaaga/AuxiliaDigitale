using Auxilia.SharedKernel.Domain;

namespace Auxilia.Domain.Messaging;

/// <summary>
/// A message template per channel, code and language (<c>messaging.message_templates</c>): subject and body in Liquid.
/// System templates are seeded in EN and IT by data-migrations, which never overwrite a customised template.
/// </summary>
public sealed class MessageTemplate : AggregateRoot<Guid>, IAuditable
{
    public const int CodeMaxLength = 100;
    public const int LanguageMaxLength = 10;
    public const int SubjectMaxLength = 300;

    public MessageTemplate(Guid id, MessageChannel channel, string code, string language, string subject, string body, bool isSystem)
        : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(code.Length, CodeMaxLength);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(language.Length, LanguageMaxLength);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(subject.Length, SubjectMaxLength);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        Channel = channel;
        Code = code;
        Language = language;
        Subject = subject;
        Body = body;
        IsSystem = isSystem;
    }

    private MessageTemplate()
    {
        Code = Language = Subject = Body = string.Empty;
    }

    public MessageChannel Channel { get; private set; }

    public string Code { get; private set; }

    public string Language { get; private set; }

    public string Subject { get; private set; }

    public string Body { get; private set; }

    /// <summary>Shipped with the product (seeded); a tenant may customise it.</summary>
    public bool IsSystem { get; private set; }

    /// <summary>Edited by the tenant: data-migrations leave it alone.</summary>
    public bool IsCustomized { get; private set; }

    /// <summary>A tenant edit (console).</summary>
    public void Customize(string subject, string body)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(subject.Length, SubjectMaxLength);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        Subject = subject;
        Body = body;
        IsCustomized = true;
    }

    /// <summary>A new shipped version (data-migration); ignored when the tenant customised the template.</summary>
    public bool UpgradeSystemContent(string subject, string body)
    {
        if (IsCustomized)
        {
            return false;
        }

        Subject = subject;
        Body = body;
        return true;
    }
}
