# AuxiliaDigitale — Architettura (v0.2)

> Stato: **approvata** (2026-09-29). Nessun codice scritto.
> Baseline legacy (D-30): `develop` @ `8fa6622` + branch `Security_Update` + `fix/zip-download-folder`.
> Fonti: blueprint, `data-model.md`, `decisions.md` del marketplace `auxilia-claude-skills`; analisi del legacy (`docs/parity/`); decisioni del progetto in [`docs/decisions.md`](../decisions.md) (D-01…D-20), che **prevalgono** sul marketplace.
> Le voci ancora da chiarire sono in §16.

---

## 1. Perimetro del prodotto (D-13)

Sistema multi-tenant per studi/uffici che gestiscono **clienti, pratiche, appuntamenti e campagne marketing**.

| Area | Modulo | Note |
|---|---|---|
| Piattaforma | `Platform` (tenant, piani, moduli, console System), `Tenancy` | nuovo, ruolo System (D-18) |
| Accesso | `Identity` | password oggi; metodi esterni pluggable (Google per clienti mobile, D-19) |
| Anagrafiche | `Directory` | persone, clienti, operatori, assegnazioni, specializzazioni, tag, consensi, richieste di registrazione (**solo API**, D-14) |
| Pratiche | `Cases` | catalogo servizi e categorie, pratiche, pagamenti, storico stati, checklist, cartelle per servizio |
| Documenti | `Documents` | upload, cartelle, ZIP |
| Agenda | `Scheduling` | appuntamenti |
| Relazione | `Engagement` | richieste a thread, notifiche, timeline attività, task |
| Comunicazioni | `Messaging` | **nuovo**: canali (email oggi, WhatsApp domani), N account per canale, regole di scelta, template, registro invii (D-16) |
| Marketing | `Marketing` | **nuovo**: segmenti, liste, campagne email, disiscrizioni (D-20) |
| Amministrazione tecnica | `Configuration`, `Localization`, `Imports` | gestite dal System (D-18) |
| Trasversali | `Reporting`, `Audit` | export, storico modifiche |
| ~~Training~~ | – | **rimosso** (D-09) |

---

## 2. Principi
1. Il codice legacy è la fonte di verità funzionale; nessuna funzionalità persa se non per decisione esplicita (registro in `docs/decisions.md`).
2. Modular monolith + Clean Architecture; moduli con confini verificati da test.
3. Tutto è tenant-aware (dati, cache, file, code, log, realtime, lock, rate limit).
4. Integrazioni esterne come **plug-in** (porta + adapter scelti da configurazione).
5. Configurazione **nel DB**, tipizzata, servita da memoria + Redis; `appsettings` solo per l'infrastruttura.
6. Ogni errore ha un codice `AUX-NNNNN` visibile in log, API e UI.
7. Minimo privilegio: ogni endpoint ha un permesso; il System gestisce la tecnica, non legge i dati di business (§4.3).
8. Nessun job schedulato (D-15): il lavoro asincrono passa solo da code.
9. API-first: ogni funzionalità è un'API documentata (OpenAPI) utilizzabile da web, futura app mobile e client esterni; le pagine web sono un client come gli altri.

---

## 3. Deployable e repository

| Deployable | Ruolo |
|---|---|
| `Auxilia.Api` | API REST `/api/v1` (tenant) + `/api/v1/platform` (System), hub SignalR |
| `Auxilia.Worker` | consumer delle code Rebus/RabbitMQ (email, documenti, import, export, campagne) — **nessun timer** |
| `Auxilia.MigrationRunner` (`auxctl`) | migrazioni, provisioning tenant, import legacy, esecuzione manuale dei job, utility |
| `frontend/apps/web` | Next.js: app del tenant (`/{tenant}/…`) + console di piattaforma (`/platform/…`), ciascuna col suo BFF/sessione |

```
AuxiliaDigitale/
├── backend/src/
│   ├── Auxilia.ServiceDefaults (niente AppHost Aspire: ADR 0013, D-32)
│   ├── Auxilia.SharedKernel · Auxilia.Diagnostics
│   ├── Auxilia.Domain/<Module>/
│   ├── Auxilia.Application/Abstractions/{Operations,Caching,Settings,Modules,Channels,Storage,Auth,Jobs,…}
│   │                     /<Module>/{I<Area>Manager, <Area>Manager, I<Area>QueryService, Validators, Dtos}  ·  /<Module>/Public/I<Module>Api.cs
│   ├── Auxilia.Contracts/<Module>/  ·  Messages/v1/
│   ├── Auxilia.Infrastructure/Adapters/<Capability>/<Provider>/  ·  Logging/  ·  Caching/  ·  Settings/
│   ├── Auxilia.Persistence.Catalog/  ·  Auxilia.Persistence.Tenant/Configurations/<Module>/
│   ├── Auxilia.Api/Endpoints/<Module>/  ·  Endpoints/Platform/  ·  Hubs/  ·  Modules/
│   ├── Auxilia.Worker/Handlers/<Module>/
│   └── Auxilia.MigrationRunner/Commands/
├── backend/tests/ (Domain, Application, Api.Integration, Worker.Integration, Persistence, Architecture, LegacyImport, Tests.Common)
├── frontend/apps/web/src/
│   ├── app/[tenant]/(public)/  login · forgot-password · reset-password · activate
│   ├── app/[tenant]/(app)/     dashboard · clients · cases · services · appointments · documents · requests · marketing · sessions · profile
│   ├── app/platform/(public)/login  ·  app/platform/(console)/ tenants/[slug]/{overview,modules,settings,branding,messaging,grids,custom-fields,localization,permissions,specializations,imports,logs,jobs} · plans
│   ├── app/api/auth/* · app/api/bff/[...path] · app/api/platform-auth/* · app/api/platform-bff/[...path]
│   ├── features/<module>/ · components/ · lib/
│   └── packages/ api-client · ui · config
├── deploy/ · docs/ (PLAN, decisions, parity, requirements, architecture, adr, migration, log-event-registry.md) · .github/
```

**A-01 (confermata)**: progetti per **layer** con cartelle per **modulo** (come prescrivono le skill), più descrittori di modulo (§5.1) e test di confine: un modulo non usa entità/Manager interni di un altro; comunica tramite `I<Module>Api` pubbliche o eventi/messaggi. FK tra schemi ammesse, navigation property tra aggregati di moduli diversi no.

---

## 4. Ruoli e modello di accesso

### 4.1 Ruoli
| Ruolo | Livello | Dove vive | Cosa fa |
|---|---|---|---|
| **System** | piattaforma | `catalog.platform_users` | tenant, piani, moduli, configurazione tecnica di ogni tenant (D-18) |
| Administrator | tenant | `identity.user_roles` | gestione funzionale dello studio: operatori, clienti, pratiche, servizi, richieste, sessioni, campagne |
| Employee (Operatore) | tenant | idem | lavoro quotidiano su clienti assegnati, pratiche, documenti, appuntamenti, richieste |
| Client | tenant | idem | propri dati, pratiche, appuntamenti, richieste (web oggi, app mobile domani) |

Il legacy `SystemConfigurator` (ruolo del tenant) **sparisce**: tutto ciò che faceva passa al System. L'utente `system` seedato nel legacy non viene migrato.

### 4.2 Come il System opera su un tenant
1. Login sulla console `/platform` (credenziali di piattaforma, 2FA TOTP consigliata — §16).
2. Sceglie un tenant → l'API emette un token con `scope=platform`, `role=System`, `tenant={slug}`, `act={platformUserId}` (breve durata).
3. Gli endpoint tecnici del tenant accettano solo questo tipo di token; ogni modifica finisce nello storico (`audit.entity_changes` con `actor_type=Platform`) e nei log con codice `29xxx`.

Implementazione (P2-06): utenti in `catalog.platform_users` con password + TOTP obbligatorio (RFC 6238 implementato in `Infrastructure/Security/Totp.cs`, segreto protetto con Data Protection, codice non riutilizzabile), creati con `auxctl platform users add` (token di attivazione monouso) e attivati dalla console (enrollment + attivazione). Login `POST /api/v1/platform/auth/token` con client `PlatformConsole` → sessione di console (`catalog.platform_sessions`, refresh rotante, 30 min inattività / 12 h). `POST /api/v1/platform/tenants/{slug}/token` → token di 10 minuti con `scope=platform`, `actor_type=platform`, `role=System`, `tenant`, `act`, `sid` della sessione di console (il logout revoca anche questi, deny-list nello spazio `platform`). Filtri `RequirePlatformUser()` (console) e `RequirePlatformTenant()` (tecnici del tenant, qualsiasi stato); i token di piattaforma non hanno permessi né moduli visibili (D-21).

### 4.3 Perimetro del System (minimo privilegio)
| Può | Non può (proposta, §16) |
|---|---|
| Creare, modificare, sospendere, riattivare, archiviare, eliminare tenant (eliminazione con doppia conferma + backup) | leggere/modificare clienti, pratiche, documenti, appuntamenti, richieste di un tenant |
| Assegnare piano e abilitare/disabilitare moduli per tenant e per ruolo | fare login come un utente del tenant |
| Impostazioni, branding, account di invio + regole per ruolo, griglie, etichette/traduzioni, campi custom, permessi dei ruoli, specializzazioni | |
| Tipi di import ed esecuzione import (crea dati, ma è attività tecnica come nel legacy) | |
| Consultare i log del tenant, alzarne temporaneamente il livello (D-28), eseguire a mano i job (es. scadenze pratiche) | |

---

## 4bis. Logica applicativa: Manager + OperationRunner (D-26)

### Struttura per area
| Tipo | Responsabilità | Esempio |
|---|---|---|
| `I<Area>Manager` / `<Area>Manager` | operazioni che cambiano stato: validazione, permessi, regole di dominio, salvataggio, eventi | `ICaseManager.CreateAsync`, `AdvanceAsync`, `CompleteAsync` |
| `I<Area>QueryService` | letture: liste paginate, filtri, dettaglio, export (solo `AsNoTracking` + proiezione) | `ICaseQueryService.ListAsync(CaseFilter, Paging)` |
| `I<Module>Api` (Public) | unico ingresso per gli altri moduli | `IDirectoryApi.GetClientSummaryAsync` |
| Validator (FluentValidation) | regole sugli input | `CreateCaseRequestValidator` |
| Policy di accesso | regole resource-based | `ICaseAccessPolicy` (pratiche private D-04) |
| Endpoint / handler Worker | sottili: leggono la richiesta, chiamano il Manager, mappano il `Result` | `CasesEndpoints.MapCases` |

Tutto registrato con dependency injection (interfacce nell'Application, implementazioni `internal`), una classe = una responsabilità, dipendenze esplicite nel costruttore, testabile sostituendo le dipendenze.

### `IOperationRunner` — ogni operazione è osservabile allo stesso modo
```csharp
public Task<Result<CaseDto>> CreateAsync(CreateCaseRequest request, CancellationToken ct) =>
    operations.RunAsync(Operations.Cases.Create, new { request.ClientId, request.ServiceId }, async scope =>
    {
        var validation = await validator.ValidateAsync(request, ct);           // → Errors.Validation (AUX-…)
        if (!validation.IsValid) return validation.ToResult<CaseDto>();
        if (!await access.CanCreateAsync(request.ServiceId, ct)) return Errors.Cases.Forbidden();
        // regole di dominio sull'aggregato, salvataggio, eventi (outbox)
        scope.SetEntity("Case", created.Id);                                   // compare in log e traccia
        return created.ToDto();
    }, ct);
```
`RunAsync` fa sempre, nello stesso ordine:
1. apre una **traccia** OpenTelemetry con nome dell'operazione (`Cases.Create`) e tag tenant, utente, entità;
2. apre uno **scope di log** con `Operation`, `TenantSlug`, `UserId`, `CorrelationId`;
3. per le operazioni di scrittura apre la **transazione** (execution strategy Postgres) e registra gli eventi nell'outbox;
4. misura **durata ed esito** (metriche `auxilia.operation.duration` e `auxilia.operation.count{operation,outcome}`);
5. scrive **un log di esito** con codice evento: Information per l'evento di business riuscito, Warning per errori attesi (validazione, permessi, regole), Error per eccezioni inattese (`AUX-10001`, con `TraceId`);
6. converte le eccezioni note (concorrenza, timeout, annullamento) nei loro codici.

`Operations.<Area>.<Nome>` è un catalogo di costanti (nome operazione + codice evento di successo) in `Auxilia.Diagnostics`, così operazioni, log e metriche hanno nomi univoci verificati dai test.

### Percorso di troubleshooting
Codice e riferimento mostrati all'utente (`AUX-14004 · rif. 4f2a…`) → file del tenant di quel giorno filtrato per `TraceId` → tutte le righe della richiesta, incluse quelle del Worker e degli invii (il `traceparent` viaggia negli header dei messaggi) → eventualmente livello Debug temporaneo per quel tenant (D-28) e riproduzione.

---

## 5. Moduli pluggable e piani (pricing futuro)

### 5.1 Descrittore di modulo
```csharp
public interface IModuleDescriptor
{
    string Code { get; }                                   // "cases", "marketing"
    ModuleKind Kind { get; }                               // Core (sempre attivo) | Optional
    int EventCodeRangeStart { get; }
    IReadOnlyList<PermissionDefinition> Permissions { get; }
    IReadOnlyList<SettingDefinition> Settings { get; }
    IReadOnlyList<NavigationEntry> Navigation { get; }     // per ruolo, con permesso richiesto
    IReadOnlyList<RecurringJobDefinition> Jobs { get; }    // §13, solo registrati
    void AddServices(IServiceCollection services, IConfiguration configuration);
    void MapEndpoints(IEndpointRouteBuilder app);          // gruppo con filtro "modulo attivo per tenant e ruolo"
}
```
Aggiungere un modulo = un descrittore + le sue cartelle; il registry popola il catalogo moduli, i permessi, le impostazioni, la navigazione e i filtri degli endpoint.

Implementazione (P1-11): `IModuleDescriptor` è in `Application/Abstractions/Modules` **senza** `MapEndpoints` e senza `IConfiguration` (l'Application non conosce HTTP; le impostazioni sono dati): gli endpoint del modulo sono un `IModuleEndpoints` nell'Api con lo stesso `ModuleCode`, mappati da `MapModules` in un gruppo con filtro tenant attivo → filtro visibilità del modulo (404 `AUX-10017`, identico a una route inesistente). I job si registrano in `AddServices` come `IRecurringJob`. I descrittori sono elencati in `DependencyInjection.Modules` (`Application/<Modulo>/<Modulo>Module.cs`); Core: identity, configuration, localization, messaging; Optional: directory, cases, scheduling, documents, engagement, marketing, reporting (imports con S-08). Un test di architettura verifica registrazione, range dei codici evento, namespace, navigazione e impostazioni. La sincronizzazione di `catalog.modules` (`IModuleCatalogManager`, `Tenancy.SyncModules`, `AUX-11019`) gira dopo `auxctl migrate catalog`: inserisce i moduli nuovi e li aggiunge al piano di default per tutti i ruoli, aggiorna tipo e range, segna non disponibili i moduli non più presenti, non tocca le scelte del System sui moduli già noti.

### 5.2 Modello di abilitazione (Catalog)
```
modules (catalogo generato dai descrittori)
plans ─< plan_modules (piano, modulo, ruoli ammessi)
tenants ─< tenant_plans (piano, valido dal/al)
tenants ─< tenant_module_overrides (modulo, abilitato?, ruoli ammessi)   ← gestito solo dal System
```
**Modulo visibile al ruolo R nel tenant T** ⇔ modulo `Core` **oppure** (override del tenant se presente, altrimenti piano del tenant) include il modulo **per R** — **e** il ruolo ha i permessi del modulo.
- Oggi: un solo piano `standard` con tutti i moduli; il System usa gli override. Il pricing si aggiunge creando piani, senza toccare il codice.
- Modulo non visibile → endpoint `404` (non esiste per quel ruolo/tenant), assente da `/me/navigation`.
- Il risultato è calcolato una volta e messo in cache (`t:{slug}:platform:modules`), invalidato a ogni modifica del System.
- La tabella tenant `configuration.modules` del data-model v1 **non serve più** (vive nel Catalog).
- Il piano `standard` include ogni modulo **per tutti i ruoli** (N02): la visibilità per ruolo del legacy (menu e pagine di F22) si riproduce con la navigazione per ruolo dei descrittori e con i permessi di ruolo (`role_permissions`, P2-03), non togliendo moduli dal piano.
- Implementazione (P1-11): `IModuleAccess` (moduli effettivi del tenant corrente in `ReferenceDataCache`, chiave `t:{slug}:platform:modules:current`, tag `t:{slug}:platform` e `platform:platform`; piano valido in quel momento, override prima del piano), ruoli dell'utente da tutte le claim `role` (non solo la prima, Q39); `INavigationQueryService` costruisce il menu (unione dei ruoli, ordinato) per `/me/navigation` (P2-03).
- Permessi (P2-03): ogni descrittore dichiara i suoi permessi (`<modulo>.<area>.<azione>`, ruoli di default) e ogni voce di navigazione ne richiede uno; nel tenant `identity.permissions` + `identity.role_permissions`, sincronizzati a ogni migrazione del tenant (permesso nuovo → ruoli di default una volta sola; permessi non più dichiarati eliminati; concessioni esistenti mai toccate). Permessi effettivi = unione su tutti i ruoli dell'utente delle concessioni del ruolo il cui modulo è visibile a quel ruolo (`IPermissionAccess`, concessioni in cache `t:{slug}:identity:role-permissions:current`). Endpoint: `RequirePermission(...)` dopo il filtro del modulo (404 se il modulo è nascosto, 403 `AUX-12028` se manca il permesso); Manager: `IAccessGuard` (permesso, poi ogni `IResourceAccessPolicy<T>` della risorsa). `GET /me` e `GET /me/navigation`.

---

## 6. Plug-in: porte e adapter
```
Application/Abstractions/<Capability>/I<Capability>.cs          ← porta
Infrastructure/Adapters/<Capability>/<Provider>/…               ← adapter, uno per cartella, chiave univoca
```
Adapter registrati come *keyed services*; quale usare è un'impostazione (§7). Credenziali cifrate nel DB (Data Protection), mai in chiaro in cache o log.

| Capability | Porta | Adapter ora | Predisposti / futuri |
|---|---|---|---|
| Metodi di login | `IAuthenticationMethod` | `password`, `email-otp` (F35, attivabile per tenant) | `google` (clienti mobile, D-19), `microsoft`, `oidc` |
| Canali di invio | `IMessageChannel` | `email` → provider `smtp` (MailKit) | `whatsapp` → provider `http-gateway` (endpoint esterno, D-20), `sms` |
| Storage file | `IFileStorage` | `local`, `ftp`, `azure-blob` | `s3` |
| Storage log | (sink Serilog) | `local-file` (sviluppo), `azure-blob` | – |
| Captcha (API pubbliche) | `ICaptchaVerifier` | `none`, `altcha` | – |
| Antivirus | `IMalwareScanner` | `none` | `clamav` |
| PDF / export | `IPdfRenderer`, `ITabularExporter` | `migradoc`, `csv`, `xlsx` | – |
| Job periodici | `IRecurringJob` | esecuzione manuale | scheduler (disattivo, D-15) |

---

## 7. Configurazione: livelli, archiviazione, cache

### 7.1 Livelli
| Livello | Dove | Contenuto | Chi |
|---|---|---|---|
| 0 Infrastruttura | `appsettings` + env/secret store | connection string Catalog, Redis, RabbitMQ, storage dei log, issuer/audience JWT (`Auth`), Data Protection (le chiavi di firma JWT stanno in `catalog.signing_keys`, cifrate), URL pubblici | DevOps |
| 1 Default codice | `SettingDefinition` | default sicuri | sviluppo |
| 2 Piattaforma | `catalog.platform_settings` | default per tutti i tenant | System |
| 3 Tenant | `configuration.*` nel tenant DB | impostazioni, branding, account di invio + regole, griglie, campi custom | System |
| 4 Utente | `configuration.user_settings` | valori delle impostazioni con scope utente (es. tema); i dati di profilo restano in `identity` (B-03) | utente |

Valore effettivo: utente → tenant → piattaforma → default (secondo gli scope dichiarati dalla definizione). Nessuna chiave libera: ogni impostazione è una `SettingDefinition<T>` (chiave, modulo, scope, default, segreta?, validazione, chiave di descrizione). L'editor generico del System elenca tutte le definizioni.

Implementazione (P1-10): `SettingDefinition<T>` / `SecretSettingDefinition` (`Application/Abstractions/Settings`), registrate dai moduli (`AddSettingDefinitions`, dal descrittore di modulo con P1-11); lettura con `ISettingsProvider` (un valore salvato non più valido per la definizione è ignorato con `AUX-20004` e vale il livello successivo); scrittura con `ISettingsManager` (`Configuration.SetSetting` / `ResetSetting`, codici `AUX-20001…20006`). Valori in JSON: `catalog.platform_settings`, `configuration.settings`, `configuration.user_settings`. I segreti sono cifrati con Data Protection (purpose `Auxilia.Configuration.SettingSecret.v1`) prima del salvataggio e decifrati solo da `GetSecretAsync`; i valori utente non vanno in cache.

### 7.2 Impostazioni legacy → nuove
| Legacy | Nuovo |
|---|---|
| `RegistrationEnabled`, `SendRegistrationConfirmationEmail` | `registration.enabled` (default **false**, D-14), `registration.sendConfirmationEmail`, `registration.notifyAdmins` (default false: non c'è ancora una pagina di approvazione) |
| `RegistrationLanguage` | `registration.defaultLanguage` (lingua delle email di conferma) |
| `AutoSubscriptionExpiry`, `SubscriptionExpiringDays` | `cases.expiry.enabled`, `cases.expiry.expiringDays` (usati dal comando manuale) |
| `UseAppName`, tema, sfondo | tabella `configuration.branding` |
| `EmailConfiguration` | `configuration.messaging_accounts` (account `smtp` di default) |
| `AppName` | `branding.app_name` |
| `SessionTimeout` | `auth.session.idleMinutes` (120) + `auth.singleSession` (false, D-08) |
| `DefaultPassword` | eliminato (D-06) |
| storage documenti | `documents.storage.provider` + credenziali; `documents.maxUploadMb` (60) |
| `ReCaptcha`, `UseLocalCache`, `UseQueueForDocuments`, `EnableSubscriptionExpiryService`, `InitDatabase`, `SaveLog`, `AuditLog` | eliminati |
| `LogBlobStorage` | infrastruttura (livello 0) |

### 7.3 Memoria + Redis
```
lettura  ─► L1 memoria (60 s) ─miss─► L2 Redis t:{slug}:configuration:snapshot (30 min) ─miss─► DB (tenant + catalog)
scrittura─► commit ─► RemoveByTag t:{slug}:configuration ─► PUBLISH auxilia:invalidate {tag} ─► ogni nodo svuota il suo L1
```
Snapshot unico per tenant; stesso meccanismo (`ReferenceDataCache<T>`) per traduzioni, moduli effettivi, permessi, navigazione, branding, account di invio, lookup, definizioni campi custom. Segreti cifrati anche in cache. Redis giù → L1 + DB, warning `AUX-24xxx`. Invalidazione sempre dopo il commit.

Implementazione (P1-10): `IReferenceDataCache` su `HybridCache`; L2 Redis/Valkey solo se `ConnectionStrings:Redis` è configurata, dietro un circuit breaker (primo errore → 30 s di sole L1 + DB, `AUX-24001` una volta, `AUX-24002` al ripristino); canale `auxilia:invalidate` per svuotare la L1 degli altri nodi; health check `redis` Degraded (non Unhealthy) se Redis è giù. Le entry dello snapshot hanno i tag `t:{slug}:{modulo}` e `platform:{modulo}`: una modifica di piattaforma invalida tutti i tenant. Le operazioni registrano l'invalidazione con `IOperationScope.OnCommitted`, eseguita dall'`IOperationRunner` dopo il commit dell'operazione più esterna (se fallisce: `AUX-10021`, l'esito resta positivo). Anche la risoluzione dei tenant (`catalog:tenant:…`, tag `catalog:tenants`, L1 30 s / L2 5 min) usa questa cache ed è invalidata dal ciclo di vita dei tenant.

---

## 8. Comunicazioni in uscita (`Messaging`, D-16)

### 8.1 Account e regole
- `configuration.messaging_accounts`: `id`, `channel` (`Email`, `WhatsApp`, `Sms`), `provider` (`smtp`, `http-gateway`…), `name`, `settings jsonb` (es. host, porta, sicurezza, mittente; per WhatsApp: URL endpoint, numero mittente), `secret_protected`, `is_default` (uno per canale), `is_active`.
- `configuration.sender_rules`: `channel`, `purpose` (`Transactional`, `Notification`, `Marketing`), `role` (null = qualsiasi), `account_id`, `priority`.
- **Risoluzione**: (canale, scopo, ruolo di chi causa l'invio) → (canale, scopo, qualsiasi ruolo) → default del canale. Invii senza utente (es. comando di sistema) usano ruolo nullo.
- Il System gestisce account e regole dalla console, con **invio di prova** per account.

### 8.2 Pipeline di invio
`IMessageDispatcher.SendAsync(message)` → risolve account → scrive `messaging.outbound_messages` (stato `Queued`) → messaggio Rebus su `auxilia.messaging` (outbox) → Worker: adapter del canale → stato `Sent`/`Failed` (codice errore) → retry/second-level → `error`.
- Template: `messaging.message_templates` (canale, codice, lingua, oggetto, corpo Liquid) per email transazionali (attivazione account, reset password, conferma richiesta di registrazione, avviso scadenza, risposta a richiesta…) e marketing.
- Registro invii consultabile (chi, cosa, quando, esito) — risponde a "la mail è partita?".
- **WhatsApp (predisposto)**: canale e tipo account esistono nel modello; l'adapter `http-gateway` (POST verso un endpoint esterno con token segreto, callback di stato su un endpoint webhook firmato) si implementa quando servirà.

### 8.3 Implementazione (P1-13)
- **Modulo `Messaging`**: `Domain/Messaging` contiene `MessagingAccount`, `SenderRule`, `MessageTemplate` e `OutboundMessage` (Queued → Sent | Failed). Tabelle: `configuration.messaging_accounts` (un solo default per canale, indice univoco filtrato), `configuration.sender_rules`, `messaging.message_templates`, `messaging.outbound_messages`. Migrazione `Messaging_Initial`.
- **`IMessagingAccountManager`** (System, S-03): crea/aggiorna gli account; il segreto è protetto con Data Protection (purpose `Auxilia.Messaging.AccountSecret.v1`) e mai restituito. Il primo account di un canale diventa il default; il default deve restare attivo (`AUX-25017`). Le regole di un canale si sostituiscono in blocco e vengono validate. L'**invio di prova** è sincrono e registrato nell'outbound log. Ogni modifica svuota lo snapshot `t:{slug}:messaging:accounts:current`, che non contiene segreti.
- **`IMessageDispatcher`** (`Messaging/Public`, API pubblica per gli altri moduli):
  - risolve l'account (§8.1) con i ruoli di chi invia;
  - rende il template Liquid (Fluid, valori codificati HTML nel corpo) nella lingua del destinatario, con fallback su lingua del tenant e poi inglese;
  - salva `OutboundMessage` Queued e accoda `DeliverOutboundMessageCommand` nell'outbox.
- **Consegna**: `IOutboundMessageManager.DeliverAsync` (Worker, coda `auxilia.messaging`, handler non transazionale, idempotente sullo stato). Errore permanente (`ChannelPermanentException`: destinatario o credenziali rifiutati, 5xx SMTP) → Failed. Errore transitorio → tentativo registrato (`AUX-25018`) e retry del bus. Consegna at-least-once.
- **Adapter**: `IMessageChannel` in `Infrastructure/Adapters/Channels/Smtp` (MailKit, sicurezza None/StartTls/SslOnConnect). Un account con provider senza adapter (es. `http-gateway` per WhatsApp) si può salvare, ma l'invio dà `AUX-25013`.
- **Template di sistema** EN + IT: `account-activation`, `password-reset`, `registration-received`, `case-expiry-reminder`, `request-reply`, `account-test`. Seed con la data-migration `D_20260930_001`: idempotente, non sovrascrive i template personalizzati.

---

## 9. Autenticazione pluggable

- Identità: ASP.NET Identity nel tenant DB; l'API emette **sempre** i propri token (JWT ES256 10–15 min + refresh rotante; client app con `X-Client-Id`). Chiavi ES256 in `catalog.signing_keys` (privata cifrata con Data Protection), JWKS su `/.well-known/jwks.json`, rotazione manuale `auxctl keys rotate`.
- Metodi come plug-in `IAuthenticationMethod`; oggi `password` e `email-otp` (codice a 6 cifre via email, 10 minuti, monouso — F35, default off).
- Sicurezza account (F35, D-30): policy password da impostazioni, storico delle ultime N password, scadenza con cambio obbligatorio, reset via token monouso, ogni tentativo di accesso in `identity.login_attempts` + log `29xxx`; pagina "Audit accessi" per l'Administrator. `GET /api/v1/auth/methods` restituisce i metodi attivi del tenant (la pagina di login e l'app mobile li leggono).
- Già pronti perché standard: tabella `identity.user_logins`; grant `external_code` progettato (flusso OAuth con PKCE, `state` cifrato con tenant e client, codice monouso scambiato dal BFF/app).
- **Google (D-19)**: si attiva in futuro per i **clienti dall'app mobile** — aggiunta di `catalog.identity_providers` (credenziali di piattaforma), `configuration.auth_methods` (abilitazione per tenant, domini ammessi, politica di collegamento/auto-registrazione) e dell'adapter. Nessuna modifica ai moduli.
- Attivazione account (D-06): token monouso via email → pagina `/{tenant}/activate` per scegliere la password. Reset password analogo.
- Console System: autenticazione separata (utenti Catalog), token di piattaforma (§4.2).
- Sessioni: `identity.refresh_sessions`; revoca + deny-list `jti` + push `ForceLogout`; sessione singola opzionale (D-08, default off).
- Realtime (P2-05): hub SignalR `/hubs/notifications` (tipo condiviso in `Infrastructure/Realtime`, perché anche il Worker invia tramite il backplane Redis, canali `auxilia:signalr`); gruppi `t:{slug}`, `t:{slug}:u:{userId}`, `t:{slug}:role:{ruolo}`, `t:{slug}:s:{sessionId}`; token in query string accettato solo sul path dell'hub; connessione chiusa alla scadenza del token; `IRealtimeNotifier` (Application) best effort. Ogni fine sessione invia `ForceLogout` al gruppo della sessione. Senza Redis il Worker non può inviare.
- Rate limiting (P2-04): limiter di ASP.NET Core, in memoria per nodo, partizioni per tenant; globale = per chiamante (utente per tenant, altrimenti IP) in catena con un limite per client app; `POST /auth/token` e i link account (attivazione, password dimenticata, reset) hanno policy più strette per tenant e IP. Oltre il limite 429 `AUX-10024` con `Retry-After` ed evento `AUX-29016`; health esclusi. Limiti nella sezione `RateLimiting` (livello 0). Con più nodi il limite effettivo si moltiplica per il numero di nodi; dietro reverse proxy servono i forwarded headers (H-01).

---

## 10. Database

### 10.1 Topologia
Catalog DB (uno) + un DB per tenant, uno schema Postgres per modulo. Estensioni `citext`, `pg_trgm`, `unaccent`.

### 10.2 Catalog (`catalog`)
| Tabella | Contenuto |
|---|---|
| `tenants` | id, slug, nome, stato (`Provisioning, Active, Suspended, MigrationFailed, Archived, Deleting`), connection string cifrata, lingua e fuso di default, versioni schema/dati, [audit] |
| `tenant_domains` | host → tenant |
| `modules` | codice, tipo (Core/Optional), nome (chiave traduzione) — sincronizzata dai descrittori |
| `plans`, `plan_modules` | piani e moduli inclusi **per ruolo** |
| `tenant_plans` | piano del tenant con validità |
| `tenant_module_overrides` | abilitazioni/disabilitazioni del System per tenant e ruolo |
| `platform_users` (+ `platform_user_roles`) | utenti System, 2FA, lockout |
| `platform_settings` | default di piattaforma |
| `client_applications` | web BFF, console, mobile, integrazioni esterne (es. il futuro client di registrazione) |
| `migration_runs` | migrazioni, provisioning, import, esecuzioni manuali dei job |
| `data_protection_keys` | chiavi Data Protection |
| *(futuro)* `identity_providers` | credenziali OAuth di piattaforma (Google…) |

### 10.3 Tenant DB — schemi
Base `data-model.md` v1 con queste modifiche:

| Schema | Tabelle | Modifiche rispetto a v1 |
|---|---|---|
| `identity` | users (+ `password_changed_at`), roles, user_roles, role_permissions, user_logins, user_claims, user_tokens, refresh_sessions, devices, **user_preferences**, **password_history**, **login_attempts** | ruoli: Administrator, Employee, Client (niente SystemConfigurator); storico password e tentativi di accesso da F35 |
| `directory` | people, client_profiles, employee_profiles, assignments, specializations, person_specializations, registration_requests, **tags**, **person_tags**, **consents** | tag e consensi con storico (per canale e finalità) per il marketing |
| `cases` | service_categories, services, **service_folders**, service_required_documents, cases, case_status_history, case_payments, case_document_requirements | `service_folders` per F33 |
| `documents` | document_areas, document_types, documents | `documents.folder_id` → `service_folders` (SET NULL) |
| `scheduling` | appointments, appointment_status_history | stati Pending/Approved/Rejected/Completed/Cancelled |
| `engagement` | requests, request_messages, request_message_attachments, notifications, notification_preferences, activities, tasks | task senza promemoria automatici (D-15) |
| **`messaging`** | **message_templates**, **outbound_messages** | nuovo (§8) |
| **`marketing`** | **segments**, **static_lists**, **static_list_members**, **campaigns**, **campaign_recipients**, **suppressions** | nuovo (§11) |
| `imports` | import_types, import_jobs, import_job_rows | invariato |
| `configuration` | settings, **user_settings**, branding, **messaging_accounts**, **sender_rules**, grid_layouts, user_saved_views, custom_field_definitions | − `modules` (nel Catalog), − `email_settings` (→ messaging_accounts); `custom_field_definitions` + `group_name`, `badge_color`, `visible_on_grid`, `dashboard_counter` |
| `localization` | languages, resource_keys, resource_translations | invariato |
| `audit` | **entity_changes** | − `app_logs` (log su file, D-17); `entity_changes` con `actor_type` (User/Platform/System) |
| `ops` | data_migrations_history, outbox_messages, processed_messages, legacy_id_map, number_sequences, **job_runs** | `job_runs`: esecuzioni manuali dei job (chi, quando, esito) |
| ~~`training`~~ | – | rimosso |

Implementazione di `identity` e `directory.people` (P2-01):
- Tabelle: `identity.users`, `identity.roles` (dati di riferimento fissi: Administrator, Employee, Client) e `identity.user_roles`; `directory.people` è minimale, B-01 la completa. `users.user_name` è `citext` univoco (login case-insensitive, F01); un solo account per persona (FK `person_id`).
- Non si usano gli store EF né lo `UserManager` di ASP.NET Identity, che non si adattano al database per tenant aperto da `ITenantDbContextFactory`. Il dominio `User` gestisce stato, lockout progressivo e security stamp. L'hashing usa il `PasswordHasher` di ASP.NET Identity (`Microsoft.Extensions.Identity.Core`, PBKDF2 V3) dentro un hasher composito che verifica anche i BCrypt del legacy (`password_format = LegacyBcrypt`) e li rehasha al primo login.
- `TenantRole` è in `SharedKernel.Tenancy`: lo usano Identity, Platform (piani e override) e l'autorizzazione.

### 10.4 Convenzioni
uuid v7, `snake_case`, tabelle plurali, FK indicizzate; audit `created_*/updated_*`; `xmin` sugli aggregati; soft delete con indici univoci parziali `WHERE NOT is_deleted`; enum `varchar` + `CHECK`; `numeric(12,2)` + valuta; `date` vs `timestamptz` UTC; `citext`; `custom_fields jsonb` + GIN; trigram per ricerca; numerazioni da `ops.number_sequences`; modifiche di schema con migrazioni EF (expand/contract), dati di riferimento con `IDataMigration` idempotenti.

### 10.5 GDPR e ciclo di vita dei dati
Consensi con storico (obbligatori per il marketing); export dati di una persona e anonimizzazione (comando + azione Admin); nessuna retention automatica nel DB (D-15): gli storici crescono in modo contenuto; log e file temporanei gestiti con regole dello storage account (lifecycle policy), fuori dal sistema come da D-17.

### 10.6 Backup
Per tenant DB + Catalog + storage file/log con prefisso tenant; test di ripristino documentato (dettagli con D-12).

---

## 11. Marketing v1 (D-20)
| Oggetto | Descrizione |
|---|---|
| Segmento dinamico | regola `jsonb` validata (stato cliente, tag, specializzazioni, servizi/stato pratiche, campi custom, età, città, date) tradotta in query sicura; anteprima con conteggio |
| Lista statica | clienti scelti a mano (anche da selezione multipla nella tabella clienti) o importati |
| Template | email (oggetto, corpo Liquid con segnaposto, lingua), anteprima e **invio di prova** |
| Campagna | segmento o lista + template + account (da regole: scopo Marketing × ruolo di chi invia); stati `Draft → Sending → Sent` (+ `Cancelled`, `Failed`); `scheduled_at` presente ma non usato (D-15) |
| Invio | "Invia ora" → messaggio in coda → Worker: snapshot destinatari (`campaign_recipients`), esclusione di chi non ha consenso email marketing valido o è in `suppressions`, invio a lotti via `Messaging`, stato per destinatario |
| Disiscrizione | **rimandata (D-24)**; in v1 la revoca del consenso la fa lo staff; la tabella `suppressions` resta per esclusioni manuali |
| Statistiche | destinatari, inviati, falliti, esclusi (niente aperture/click) |

---

## 12. Logging, audit, osservabilità (D-17)

| Cosa | Dove |
|---|---|
| **Tutti i log Information+** (Debug in sviluppo per configurazione; in produzione per singolo tenant e a tempo, D-28) | file **JSON-lines** giornalieri sullo storage account: `logs/tenants/{slug}/{yyyy}/{MM}/{dd}.jsonl` e `logs/platform/{yyyy}/{MM}/{dd}.jsonl` per eventi senza tenant; append blob scritti da Api, Worker e Runner |
| Campi di ogni riga | timestamp UTC, livello, `EventCode AUX-NNNNN`, messaggio, eccezione, `TenantSlug`, `UserId` / `PlatformUserId`, `ClientId`, `TraceId`, `CorrelationId`, host, versione |
| Console | JSON compatto (container / sviluppo) |
| Livello per tenant (D-28) | impostazione di piattaforma per tenant `logging.minimumLevel` + `logging.overrideUntil`: il filtro di Serilog legge il valore dalla cache del tenant (invalidata subito alla modifica) e torna al livello normale alla scadenza, senza job né riavvio; ogni modifica è auditata |
| Pagina Log | console System → tenant → Log: legge i file per intervallo di date con filtri (livello, codice, traceId, utente, testo); nessuna copia nel DB |
| Retention | fuori dal sistema, sullo storage account (D-17) |
| Eventi di sicurezza | codici `29xxx` nei log (`Log.Security`): accesso fallito, blocco, aggiornamento hash legacy, cambio ruoli/password/attivazione, riuso refresh token, fine sessione, reset password richiesto/completato, attivazione account, client app rifiutata, rotazione chiave di firma, permesso negato, tentativo cross-tenant, rate limit superato; elenco completo in `docs/log-event-registry.md` |
| Storico modifiche dati | `audit.entity_changes` (DB), mostrato come "cronologia" dei record |
| Storici di business | stati pratiche/appuntamenti, timeline cliente, registro invii |
| Tracce e metriche | OpenTelemetry pronto nel codice; esportazione verso un backend quando si decide D-12 |

Implementazione: Serilog con instradamento per `TenantSlug` verso un writer per (tenant, giorno) sullo storage configurato (`local-file` in sviluppo, `azure-blob` in produzione), scrittura asincrona a lotti; codici evento da `Auxilia.Diagnostics` (`[LoggerMessage]` obbligatorio), range 19000–19999 → **Marketing**, 25000–25999 → **Messaging**; mascheramento di password, token, connection string, CF, contenuto documenti. Pacchetti da verificare con la dependency policy: `Serilog.Sinks.Map`, `Serilog.Sinks.AzureBlobStorage` (o sink custom sull'SDK `Azure.Storage.Blobs`).

---

## 13. Lavoro asincrono (D-15)

| Coda | Uso |
|---|---|
| `auxilia.messaging` | email (e in futuro WhatsApp) |
| `auxilia.documents` | post-elaborazione upload, ZIP grandi |
| `auxilia.notifications` | notifiche in-app + push SignalR |
| `auxilia.imports` | validazione e importazione |
| `auxilia.reporting` | export grandi |
| `auxilia.marketing` | espansione destinatari e invio campagne |
| `auxilia.platform` | provisioning/eliminazione tenant, esecuzione manuale job |
| `error` | messaggi falliti dopo i retry |

**Job periodici predisposti ma non schedulati**: ogni modulo dichiara i suoi `RecurringJobDefinition` (codice, descrizione, frequenza suggerita, comando per tenant). Oggi si eseguono **a mano** dal System (console → tenant → Job, o `auxctl jobs run <job> --tenant <slug>`), con lock distribuito, idempotenza e registro in `ops.job_runs`. Uno scheduler futuro userà lo stesso registry senza modifiche ai moduli.
Job registrati: `cases.expiry` (parità F11), `engagement.task-reminders` (futuro), `documents.staging-cleanup` (in alternativa lifecycle policy dello storage).

Implementazione (P1-12):
- **Rebus su RabbitMQ**. Routing per convenzione: un messaggio in `Contracts/Messages/V<n>/<Modulo>/` va nella coda `auxilia.<modulo>` (`MessageRouting`). I messaggi per tenant implementano `ITenantMessage`. Api e auxctl hanno un bus solo invio (`AddMessageBusClient`); il Worker crea un bus per ogni coda che ha handler nel suo assembly (`AddMessageBusWorker`). Connessione in `ConnectionStrings:RabbitMq`: senza, i messaggi restano nell'outbox.
- **Header** di ogni messaggio: `rbs2-msg-id` (chiave di idempotenza), `x-tenant-slug`, `x-correlation-id`, `x-user-id`, `x-actor-type`, `traceparent`. Vengono catturati quando il messaggio è prodotto.
- **Outbox**: `IMessageOutbox.EnqueueAsync(scope, message)` scrive in `ops.outbox_messages` nella transazione dell'operazione e invia dopo il commit (`OnCommitted`). Nessun dispatcher in polling (D-15): se l'invio fallisce la riga resta pendente (`AUX-23008`) e la invia il job manuale `bus.outbox`. I messaggi di piattaforma senza DB tenant si inviano direttamente dopo il commit.
- **Ricezione**: `IIncomingMessageProcessor` (Application), sotto l'operazione `Bus.HandleMessage` (`AUX-23001`). Tenant da `x-tenant-slug`: se manca → `23003`, se sconosciuto o non attivo → `23004`, in entrambi i casi coda `error` senza retry. Chiamante e correlazione dagli header; traccia unita al `traceparent`. Gli handler del Worker ereditano `MessageHandler<T>` e restano sottili.
- **Idempotenza**: `ops.processed_messages (message_id, handler)`. Per gli handler transazionali il record è scritto nella stessa transazione delle modifiche; gli handler di job (non transazionali) lo scrivono dopo il successo e sono idempotenti per costruzione.
- **Retry**: 5 tentativi immediati, poi retry di secondo livello con ritardi 10 s / 1 min / 5 min / 30 min (`AUX-23006`; deferral in `public.rebus_timeouts` del database Catalog, tabella creata da Rebus), poi coda `error` (`23007`). Un `Result` fallito dell'handler è permanente (`MessageRejectedException`, `23005`) e va subito in `error`.
- **Job**: `RunRecurringJobCommand` → `auxilia.platform` → `IJobRunner`. Lock per tenant e job con advisory lock PostgreSQL nel database del tenant (`IJobLock`): funziona anche senza Redis; una seconda esecuzione concorrente è rifiutata con `AUX-26004`. `ops.job_runs` è scritto fuori dalla transazione, quindi sopravvive al rollback del messaggio.

---

## 14. Frontend
- Due aree con sessioni separate: **app del tenant** (`/{tenant}/…`, Admin/Employee/Client) e **console di piattaforma** (`/platform/…`, System).
- BFF: il browser non vede mai i token.
- Navigazione dall'API (`/me/navigation` rispetta piani, override e permessi); ogni `features/<module>` dichiara le sue route.
- Pagine pubbliche del tenant: login, forgot/reset password, attivazione, disiscrizione. **Nessuna pagina di registrazione** (D-14).
- Branding come design token, dark mode, WCAG 2.2 AA, tabelle server-side con viste salvate, traduzioni dall'API.

---

## 15. Checklist "nessun buco"

| Aspetto | Dove |
|---|---|
| Isolamento tenant | risoluzione tenant, factory DbContext, prefissi cache/file/log/lock/code/SignalR, test obbligatori |
| Ruoli e perimetri | §4; token di piattaforma distinti; test di autorizzazione per ogni endpoint |
| Moduli e piani | §5; filtro endpoint + navigazione + cache |
| Autenticazione | §9 |
| Autorizzazione dati | permessi + policy resource-based (pratiche private, D-04) nelle query |
| Configurazione | §7 |
| Segreti | Data Protection nel DB, env/secret store per l'infrastruttura, gitleaks |
| Comunicazioni | §8: account per ruolo, template localizzati, registro invii, retry |
| Errori | `Result` + codici `AUX-`, ProblemDetails, UI con codice e riferimento traccia |
| Log e audit | §12 |
| Concorrenza / idempotenza | `xmin` + ETag/If-Match; `Idempotency-Key`; consumer delle code idempotenti |
| Tempo | UTC nel DB, fuso del tenant nella presentazione e nelle regole di calendario |
| Localizzazione | chiavi DB EN+IT, anche per email e PDF |
| File | staging → commit, hash, magic bytes, antivirus pluggable, download autorizzato |
| Privacy | consensi, disiscrizione, export/anonimizzazione |
| Asincrono | §13, nessun timer |
| Migrazioni | `auxctl`, expand/contract, data-migration idempotenti |
| Backup | §10.6 |
| API-first / client esterni | OpenAPI versionato, client app registrate, rate limit, captcha pluggable per le API pubbliche |
| Migrazione dal legacy | `auxctl legacy import` con dry-run e riconciliazione |

---

## 16. Punti chiariti

| ID | Domanda | Proposta |
|---|---|---|
| A-01 | ✅ confermata | layer + cartelle per modulo + descrittori + test di confine |
| Q-A | ✅ D-21 | Il System non vede i dati di business |
| Q-B | ✅ D-22 | 2FA obbligatoria |
| Q-C | ✅ D-23 | Consenso impostato a true in migrazione (fonte `LegacyMigration`) |
| Q-D | ✅ D-24 | Disiscrizione rimandata |
| Q-E | ✅ D-29 | System gestisce le specializzazioni; Admin/Operatore le assegnano |
| Q-F | ✅ D-25 | Solo archiviazione |
