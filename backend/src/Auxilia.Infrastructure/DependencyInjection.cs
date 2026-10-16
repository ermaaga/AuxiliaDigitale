using Auxilia.Application.Abstractions.Captcha;
using Auxilia.Application.Abstractions.Channels;
using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Logging;
using Auxilia.Application.Abstractions.Messaging;
using Auxilia.Application.Abstractions.Realtime;
using Auxilia.Application.Abstractions.Storage;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Infrastructure.Adapters.Captcha.Altcha;
using Auxilia.Infrastructure.Adapters.Captcha.None;
using Auxilia.Infrastructure.Adapters.Channels.Smtp;
using Auxilia.Infrastructure.Adapters.Storage.AzureBlob;
using Auxilia.Infrastructure.Adapters.Storage.Ftp;
using Auxilia.Infrastructure.Adapters.Storage.Local;
using Auxilia.Infrastructure.Adapters.Templates.Fluid;
using Auxilia.Infrastructure.Caching;
using Auxilia.Infrastructure.Logging;
using Auxilia.Infrastructure.Messaging;
using Auxilia.Infrastructure.Realtime;
using Auxilia.Infrastructure.Security;
using Auxilia.Infrastructure.Security.Tokens;
using Auxilia.Infrastructure.Tenancy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Auxilia.Application.Abstractions.Images;
using Auxilia.Infrastructure.Adapters.Images.Skia;

namespace Auxilia.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Technical services shared by every host (adapters are added by later tasks); the reference-data cache is in memory until <see cref="CachingRegistration.AddRedisCache"/>.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(provider => provider.GetRequiredService<TenantContext>());
        services.AddScoped<ITenantContextSetter>(provider => provider.GetRequiredService<TenantContext>());

        services.AddReferenceDataCache();

        // Per-tenant log levels (D-28): replaced by the Redis channel in AddRedisCache.
        services.TryAddSingleton<ITenantLogLevelBroadcast, NullTenantLogLevelBroadcast>();

        // Replaced by AddMessageBusClient / AddMessageBusWorker when RabbitMQ is configured.
        services.TryAddSingleton<IMessageSender, UnavailableMessageSender>();

        // Outbound channel adapters (keyed by provider on the account) and the template engine (ARCHITECTURE §6, §8).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMessageChannel, SmtpEmailChannel>());
        services.TryAddSingleton<ITemplateRenderer, FluidTemplateRenderer>();

        // Captcha of the public endpoints, chosen by the client application (F02); ALTCHA options from Captcha:Altcha.
        services.AddOptions<AltchaOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICaptchaVerifier, NoCaptchaVerifier>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICaptchaVerifier, AltchaCaptchaVerifier>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Application.Abstractions.Exports.IExportWriter, Adapters.Exports.CsvExportWriter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Application.Abstractions.Exports.IExportWriter, Adapters.Exports.XlsxExportWriter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Application.Abstractions.Exports.IExportWriter, Adapters.Exports.PdfExportWriter>());
        services.TryAddSingleton<Application.Abstractions.Imports.IImportWorkbook, Adapters.Imports.XlsxImportWorkbook>();

        // File storage of the documents (ARCHITECTURE §6): the provider is a tenant setting; local root from Storage:Local.
        services.AddOptions<LocalStorageOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFileStorage, LocalFileStorage>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFileStorage, FtpFileStorage>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFileStorage, AzureBlobFileStorage>());

        // Uploaded pictures are decoded and re-encoded by the server (F04).
        services.TryAddSingleton<IImageProcessor, SkiaImageProcessor>();

        // Connected accounts (N05, ADR 0021): OAuth applications from ConnectedAccounts, IMAP test, DNS for the SSRF check.
        services.AddOptions<Adapters.OAuth.ConnectedAccountsOptions>();
        services.AddHttpClient(Adapters.OAuth.OAuthProviderBase.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(20));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Application.Abstractions.Mailbox.IOAuthProvider, Adapters.OAuth.GoogleOAuthProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Application.Abstractions.Mailbox.IOAuthProvider, Adapters.OAuth.MicrosoftOAuthProvider>());
        services.TryAddSingleton<Application.Abstractions.Mailbox.IImapConnectionTester, Adapters.Mail.Imap.ImapConnectionTester>();
        services.TryAddSingleton<Application.Abstractions.Mailbox.IHostAddressResolver, Adapters.Network.DnsHostAddressResolver>();

        // Calendars of connected accounts (C-04): Google Calendar and Microsoft Graph over HTTP, the sync's access token.
        services.AddHttpClient(Adapters.Calendar.CalendarProviderBase.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(30));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Application.Abstractions.Mailbox.ICalendarProvider, Adapters.Calendar.GoogleCalendarProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Application.Abstractions.Mailbox.ICalendarProvider, Adapters.Calendar.MicrosoftCalendarProvider>());

        // Mail headers of connected accounts (C-06): Gmail and Graph over the same HTTP client, PEC / IMAP with MailKit.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Application.Abstractions.Mailbox.IMailProvider, Adapters.Mail.GmailMailProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Application.Abstractions.Mailbox.IMailProvider, Adapters.Mail.GraphMailProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Application.Abstractions.Mailbox.IMailProvider, Adapters.Mail.Imap.ImapMailProvider>());

        services.TryAddSingleton<IPasswordHasher, CompositePasswordHasher>();

        // Tokens (skill auxilia-security): ES256 ring in the Catalog, JWT issuer, deny-list on the distributed cache
        // (in memory until AddRedisCache replaces IDistributedCache).
        services.AddOptions<TokenOptions>();
        services.AddDistributedMemoryCache();
        services.TryAddSingleton<SigningKeyRing>();
        services.TryAddScoped<ISigningKeyFactory, SigningKeyFactory>();
        services.TryAddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.TryAddSingleton<IAccessTokenDenyList, DistributedAccessTokenDenyList>();
        services.TryAddSingleton<ITotpService, TotpService>();

        // Replaced by AddRealtime in the Api and the Worker.
        services.TryAddScoped<IRealtimeNotifier, NullRealtimeNotifier>();

        return services;
    }
}
