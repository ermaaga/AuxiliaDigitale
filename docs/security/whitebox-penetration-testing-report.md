# Report di Validazione di Sicurezza (White-Box Penetration Testing)

**Progetto**: AuxiliaDigitale (`auxilia-next`)  
**Data**: 13 Ottobre 2026  
**Ambiente di esecuzione**: Container `lucid_hertz` (`strix-pentest`), Codebase `/workspace`  
**Standard di riferimento**: OWASP Top 10 (2025), OWASP ASVS 4.0.3 Livello 2, Metodologia White-Box Strix  
**Tipologia di Assessment**: Structured White-Box Penetration Testing & Source Code Security Review  

---

## 1. Executive Summary

È stata condotta un'attività di **validazione di sicurezza strutturata (White-Box Penetration Testing e Code Review approfondita)** sulla codebase dell'applicazione `/workspace` (`AuxiliaDigitale`), simulando il processo metodologico degli agenti di sicurezza **Strix** caricati nell'ambiente.

L'analisi ha preso in esame l'intera architettura della piattaforma:
- **Backend .NET 10** (`backend/src/Auxilia.Api`, `Auxilia.Application`, `Auxilia.Infrastructure`, `Auxilia.Persistence.*`, `Auxilia.Worker`).
- **Frontend Next.js BFF** (`frontend/apps/web/src/lib/bff`).
- **Pipeline dati, confini multi-tenant, storage documentale e gestione crittografica**.

### Giudizio Complessivo
L'applicazione adotta un'impostazione difensiva robusta: l'isolamento multi-tenant è integrato nel ciclo di vita HTTP (`TenantResolutionMiddleware`), le interrogazioni al database sono tipizzate e prive di concatenazioni SQL dinamiche non parametrizzate, gli upload documentali applicano controlli di magic-byte e transcodifica grafica (SkiaSharp), e il frontend BFF adotta rigorose difese anti-CSRF e CSP con nonce.

Tuttavia, sono state identificate **5 vulnerabilità/debolezze architetturali**, di cui **1 ad alta severità**, che richiedono interventi correttivi per prevenire compromissioni di segreti a riposo e scenari di Denial of Service applicativo.

---

## 2. Matrice delle Vulnerabilità Rilevate

| ID | Titolo Vulnerabilità | Severità | CWE | OWASP Top 10 | File Principale |
|---|---|:---:|---|---|---|
| **VULN-01** | Memorizzazione in chiaro a riposo delle chiavi master di Data Protection | **Alta** | [CWE-312](https://cwe.mitre.org/data/definitions/312.html) | **A02: Cryptographic Failures** | `backend/src/Auxilia.Persistence.Catalog/CatalogPersistence.cs` |
| **VULN-02** | Rischio di Memory Exhaustion (DoS) tramite cache non vincolata dei template Liquid | **Media** | [CWE-400](https://cwe.mitre.org/data/definitions/400.html) / [CWE-770](https://cwe.mitre.org/data/definitions/770.html) | **A04: Insecure Design** | `backend/src/Auxilia.Infrastructure/Adapters/Templates/Fluid/FluidTemplateRenderer.cs` |
| **VULN-03** | Generazione sincrona di archivi ZIP su disco locale con rischio saturazione I/O e spazio | **Media** | [CWE-400](https://cwe.mitre.org/data/definitions/400.html) | **A04: Unrestricted Resource Consumption** | `backend/src/Auxilia.Application/Documents/Documents.cs` |
| **VULN-04** | Fallback a chiave effimera per il CAPTCHA ALTCHA in configurazioni multi-nodo | **Media** | [CWE-330](https://cwe.mitre.org/data/definitions/330.html) | **A07: Identification and Auth Failures** | `backend/src/Auxilia.Infrastructure/Adapters/Captcha/Altcha/AltchaCaptchaVerifier.cs` |
| **VULN-05** | Possibilità di trasmissione in chiaro di credenziali e file su storage FTP non cifrato | **Bassa** | [CWE-319](https://cwe.mitre.org/data/definitions/319.html) | **A02: Cryptographic Failures** | `backend/src/Auxilia.Infrastructure/Adapters/Storage/Ftp/FtpFileStorage.cs` |

---

## 3. Metodologia di Analisi e Tracciamento Flussi (Input-Output)

Seguendo le procedure tecniche prescritte dalla skill Strix per l'analisi White-Box:

```
[Richiesta Client / Browser]
           │
           ▼
[ForwardedHeaders / Reverse Proxy] ──► Validazione KnownProxies e ForwardLimit
           │
           ▼
[Authentication (JWT ES256)] ────────► Validazione Issuer, Audience, Ring crittografico, Deny-list (Redis)
           │
           ▼
[TenantResolutionMiddleware] ────────► Validazione incrociata: Claim 'tenant' vs Header 'X-Tenant' vs Host
           │                           (Rifiuto discordanze con 403 AUX-11004)
           ▼
[RateLimiter] ───────────────────────► Partizionamento per Tenant/User o IP anonimo
           │
           ▼
[Authorization Endpoint Filters] ────► RequirePermission / RequireTenantUser / RequirePlatform*
           │
           ▼
[Application Managers & Policies] ───► ResourceAccessPolicies (CaseAccessPolicy, AppointmentAccessPolicy)
           │                           Filtro a livello SQL (ScopeAsync)
           ▼
[Data Sinks & Storage] ──────────────► TenantDbContext (Postgres dedicato per tenant)
                                       FileStore (Sotto-directory tenant isolate, magic bytes, SHA-256)
```

1. **Perimetro e Superficie di Attacco**: Mappatura degli endpoint registrati tramite `SecurityPerimeterTests`. Ogni route dichiara tassativamente un filtro di autorizzazione o è esplicitamente motivata come anonima (`AllowAnonymous`).
2. **Confini Multi-Tenant**: Verifica della risoluzione del database tramite `TenantDbContextFactory` in base al tenant ricavato da `ITenantContext`. Nessuna query espone dati cross-tenant a livello logico.
3. **Flussi di Autorizzazione Risorse (BOLA / IDOR)**: Ispezione delle policy a livello riga (`CaseAccessPolicy`, `AppointmentAccessPolicy`, `RequestAccessPolicy`).
4. **Analisi dei Parser e Sinks**: Verifica di serializzatori, parser di template, generatori di archivi compressi ed esecutori di query database.

---

## 4. Dettaglio Tecnico delle Vulnerabilità e Remediation

---

### VULN-01: Memorizzazione in chiaro a riposo delle chiavi master di Data Protection

#### Descrizione e Meccanismo
In `CatalogPersistence.cs` (righe 45–48):
```csharp
services.AddDataProtection()
    .SetApplicationName(DataProtectionApplicationName)
    .PersistKeysToDbContext<CatalogDbContext>();
```
Il ring di chiavi di crittografia di ASP.NET Core Data Protection viene memorizzato come payload XML nella tabella `catalog.data_protection_keys`. L'implementazione attuale non aggancia alcun meccanismo di cifratura a riposo (`ProtectKeysWithCertificate`, `ProtectKeysWithDpapi`, o integrazione KMS).

#### Impatto
Queste chiavi sono utilizzate dal sistema per proteggere:
- Le connection string cifrate dei database di tutti i singoli tenant (`ITenantConnectionProtector`).
- Le credenziali SMTP dei tenant (`ISettingSecretProtector`).
- Le chiavi private di firma dei token JWT (`ISigningKeyProtector`).
- I segreti OTP/TOTP degli utenti (`ITwoFactorSecretProtector`).

Un dump del database PostgreSQL del Catalog (o un accesso in lettura non autorizzato) consente a un attaccante di decifrare istantaneamente tutti i segreti archiviati dell'intera infrastruttura multi-tenant.

#### Remediation Consigliata
Configurare la protezione a riposo tramite certificato X.509 o secret store d'ambiente (AWS KMS, Azure Key Vault, HashiCorp Vault):
```csharp
services.AddDataProtection()
    .SetApplicationName(DataProtectionApplicationName)
    .PersistKeysToDbContext<CatalogDbContext>()
    .ProtectKeysWithCertificate(loadedX509Certificate);
```

---

### VULN-02: Rischio di Memory Exhaustion (DoS) tramite cache non vincolata dei template Liquid

#### Descrizione e Meccanismo
In `FluidTemplateRenderer.cs` (righe 14 e 44–48):
```csharp
private readonly ConcurrentDictionary<string, IFluidTemplate?> parsed = new(StringComparer.Ordinal);

private IFluidTemplate? Parse(string template)
{
    ArgumentNullException.ThrowIfNull(template);
    return parsed.GetOrAdd(template, source => Parser.TryParse(source, out var result, out _) ? result : null);
}
```
Ogni sorgente di template analizzata (inclusi i template delle e-mail di marketing create o testate dinamicamente dagli utenti) viene memorizzata a tempo indefinito all'interno di una `ConcurrentDictionary`.

#### Impatto
Poiché non esiste alcun vincolo di cardinalità massima, scadenza temporale o politica di eviction (LRU):
- Se un tenant o un utente malintenzionato invia centinaia o migliaia di template contenenti variazioni arbitrarie o stringhe randomizzate nel corpo (ad esempio invocando ripetutamente `POST /api/v1/marketing/templates/test` o l'aggiornamento template), la memoria heap dell'applicazione cresce in modo incontrollato, portando a Out-Of-Memory (OOM) e Denial of Service dell'istanza API.

#### Remediation Consigliata
Sostituire la `ConcurrentDictionary` con una cache a capacità limitata con politica di eviction (ad esempio `IMemoryCache` con `SizeLimit`):
```csharp
internal sealed class FluidTemplateRenderer : ITemplateRenderer
{
    private static readonly FluidParser Parser = new();
    private readonly MemoryCache cache = new(new MemoryCacheOptions
    {
        SizeLimit = 1000 // Limite massimo di 1000 template compilati in memoria
    });

    public string? Render(string template, IReadOnlyDictionary<string, object?> model, bool html)
    {
        if (Parse(template) is not { } fluid) return null;
        // ... rendering logico ...
    }

    private IFluidTemplate? Parse(string template)
    {
        return cache.GetOrCreate(template, entry =>
        {
            entry.SetSize(1);
            entry.SetSlidingExpiration(TimeSpan.FromHours(4));
            return Parser.TryParse(template, out var result, out _) ? result : null;
        });
    }
}
```

---

### VULN-03: Generazione sincrona di archivi ZIP su disco locale con rischio saturazione I/O e spazio

#### Descrizione e Meccanismo
In `Documents.cs` (metodo `ZipAsync`, righe 676–712):
```csharp
var zip = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
try
{
    using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
    {
        foreach (var (path, document) in entries)
        {
            var opened = await files.OpenReadAsync(document.StorageKey, cancellationToken);
            // ... download da storage e scrittura nell'archivio locale ...
        }
    }
    // ...
}
```
L'endpoint `GET /api/v1/documents/zip?caseId=...` consente il download di tutti i documenti di un fascicolo in un unico archivio. L'operazione viene eseguita in modalità sincrona all'interno del ciclo di richiesta/risposta HTTP, creando un file temporaneo su filesystem tramite `Path.GetTempFileName()`.

#### Impatto
Sebbene la logica `ZipPaths.Plan` e `CheckName` sia protetta contro attacchi di tipo Zip Slip (i nomi delle cartelle non possono contenere `/`, `\`, `.`, o `..`), l'esecuzione sincrona presenta gravi rischi di disponibilità:
1. Fascicoli con decine o centinaia di allegati pesanti possono esaurire rapidamente lo spazio della directory `/tmp`.
2. Più richieste contemporanee saturano l'I/O su disco del container e impegnano i worker di Kestrel, provocando rallentamenti globali dell'API.

#### Remediation Consigliata
1. **Soglie Dimensionali**: Introdurre un controllo sul numero massimo di documenti (es. max 50) e sulla dimensione aggregata massima (es. max 100 MB) scaricabili in streaming istantaneo.
2. **Elaborazione Asincrona**: Per fascicoli che superano tali soglie, delegare la generazione del pacchetto al background worker via RabbitMQ (come già implementato con successo in `ExportEndpoints` per gli export > 2000 righe) e notificare l'utente con link temporaneo appena pronto.

---

### VULN-04: Fallback a chiave effimera per il CAPTCHA ALTCHA in configurazioni multi-nodo

#### Descrizione e Meccanismo
In `AltchaCaptchaVerifier.cs` (righe 82–94):
```csharp
private static byte[] Key(string? configured, ILogger logger)
{
    if (string.IsNullOrWhiteSpace(configured))
    {
        Log.Security.CaptchaKeyEphemeral(logger);
        return RandomNumberGenerator.GetBytes(KeyLength);
    }
    // ...
}
```
Se la variabile `Captcha:Altcha:Key` non è valorizzata, l'applicazione genera silenziosamente una chiave casuale in memoria ad ogni riavvio dell'istanza.

#### Impatto
In ambienti di produzione con più repliche (Kubernetes / container scalati):
- La challenge ALTCHA generata dal Nodo A risulterà invalida se sottomessa al Nodo B con la richiesta di registrazione, provocando continui errori `400/403` ingiustificati per gli utenti legittimi.
- Al riavvio dell'istanza, tutte le challenge precedentemente emesse diventano istantaneamente non verificabili.

#### Remediation Consigliata
Imporre la presenza obbligatoria della chiave fuori dall'ambiente di sviluppo (`!app.Environment.IsDevelopment()`), bloccando l'avvio del container con un messaggio esplicito se non fornita:
```csharp
if (!isDevelopment && string.IsNullOrWhiteSpace(configured))
{
    throw new InvalidOperationException("Captcha:Altcha:Key must be configured in non-development environments.");
}
```

---

### VULN-05: Possibilità di trasmissione in chiaro di credenziali e file su storage FTP non cifrato

#### Descrizione e Meccanismo
In `FtpFileStorage.cs` (riga 86):
```csharp
client.Config.EncryptionMode = target.FtpTls ? FtpEncryptionMode.Explicit : FtpEncryptionMode.None;
```
La configurazione dello storage dei documenti ammette `target.FtpTls = false`, attivando `FtpEncryptionMode.None`.

#### Impatto
Se un amministratore tenant configura un server FTP legacy senza TLS, l'autenticazione (username e password FTP) e il contenuto integrale dei documenti dei clienti (compresi dati personali o sanitari) transitano in chiaro attraverso la rete.

#### Remediation Consigliata
Imporre l'uso esclusivo di comunicazioni cifrate (`FtpEncryptionMode.Explicit` o migrazione a SFTP / Object Storage S3) e deprecare formalmente il supporto a FTP in chiaro in ambienti di produzione.

---

## 5. Controlli Difensivi Positivamente Verificati

L'analisi white-box ha confermato l'efficacia dei seguenti controlli di sicurezza già implementati nel codice:

1. **Prevenzione SQL Injection**:
   - Zero utilizzo di query string concatenate. Le chiamate `Database.SqlQuery<T>` e `ExecuteSqlInterpolatedAsync` utilizzano `FormattableString`, garantendo la parametrizzazione nativa di PostgreSQL/Npgsql.
2. **Prevenzione Path Traversal e Insecure File Upload**:
   - `StorageKeys.BelongsTo` verifica il prefisso del tenant e rifiuta categoricamente stringhe contenenti `..`, `\`, o `//`.
   - `FileNames.Sanitize` ripulisce i caratteri speciali e i delimitatori di percorso.
   - `FileTypes.cs` associa all'estensione una verifica obbligatoria dei **magic bytes** (es. `%PDF-`, firme OLE, intestazioni ZIP standard).
   - Le immagini del profilo utente (`UserImage`) vengono decodificate e ricodificate in JPEG massimo 400×400 tramite SkiaSharp, eliminando metadati EXIF e impedendo polyglot exploit.
   - I file Excel caricati per gli import sono protetti da `XlsxImportWorkbook.IsReasonablePackage` per prevenire Zip Bomb (>1000 entry o >200 MB decompressi dichiarati).
3. **Isolamento e Cross-Tenant Defense**:
   - `TenantResolutionMiddleware` confronta il claim `tenant` del JWT con l'header `X-Tenant` e l'host; un disallineamento produce immediatamente `403 Forbidden` (`AUX-11004`).
4. **Ciclo di Vita Sessioni e Anti-Replay**:
   - Refresh token monouso con rotazione continua e revoca a catena della sessione (`RefreshTokenReuse`) in caso di riutilizzo di un token consumato.
   - Deny-list distribuita su Redis con `TokenId` (`jti`) e `SessionId`.
5. **Difese Web e BFF (Next.js)**:
   - Protezione CSRF a tre livelli: cookie `SameSite=Lax`, header obbligatorio `x-requested-with: auxilia` e validazione dell'`Origin`.
   - Cookie di sessione con attributi `HttpOnly`, `Secure` e `SameSite=Lax`.
   - Content Security Policy (CSP) restrittiva con nonce casuali per script e `frame-ancestors 'none'`.

---

## 6. Piano di Remediation e Roadmap

```mermaid
flowchart TD
    subgraph Fase 1 - Immediate / Sprint Corrente
        A["VULN-01: Cifratura a riposo chiavi Data Protection"]
        B["VULN-04: Blocco all'avvio senza Captcha Key in produzione"]
    end

    subgraph Fase 2 - Medio Termine
        C["VULN-02: Bounded MemoryCache per FluidTemplateRenderer"]
        D["VULN-03: Soglie e generazione asincrona per ZipAsync"]
    end

    subgraph Fase 3 - Hardening Continuo
        E["VULN-05: Deprecazione FTP in chiaro a favore di FTPS / SFTP"]
        F["Integrazione scansione dipendenze e SBOM in CI/CD"]
    end

    Fase 1 --> Fase 2 --> Fase 3
```

1. **Sprint Corrente (Priorità Alta)**:
   - Applicare un certificato di cifratura o secret provider a `services.AddDataProtection().ProtectKeysWith...`.
   - Configurare la validazione all'avvio per `Captcha:Altcha:Key`.
2. **Sprint Successivo (Priorità Media)**:
   - Sostituire `ConcurrentDictionary` in `FluidTemplateRenderer` con `MemoryCache` limitata.
   - Aggiungere controlli di soglia e gestione worker per `ZipAsync` in `DocumentEndpoints`.
3. **Hardening Continuo**:
   - Rimuovere l'opzione FTP in chiaro dalla configurazione tenant.
   - Mantenere attivo il monitoraggio continuo tramite i test del perimetro di sicurezza (`SecurityPerimeterTests`).
