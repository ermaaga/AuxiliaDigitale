namespace Auxilia.Infrastructure.Security.Tokens;

/// <summary>Infrastructure settings of access tokens (level 0, section <c>Auth</c>): who issues them and for whom.</summary>
public sealed class TokenOptions
{
    public const string SectionName = "Auth";

    public string Issuer { get; set; } = "https://auxilia.app";

    public string Audience { get; set; } = "auxilia-api";
}

/// <summary>Claims of Auxilia access tokens (skill auxilia-security).</summary>
public static class TokenClaims
{
    public const string Subject = "sub";
    public const string Tenant = "tenant";
    public const string Session = "sid";
    public const string Role = "role";
    public const string Client = "client_id";
    public const string TokenId = "jti";
}
