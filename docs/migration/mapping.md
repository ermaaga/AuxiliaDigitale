# Mappatura della migrazione dati dal legacy (Fase 7)

> Documento vivo dei task E-01…E-06 (`docs/PLAN.md`). Sorgente: database PostgreSQL del legacy alla baseline D-30
> (`develop` @ `8fa6622` + `Security_Update` @ `e314e9a`). Destinazione: il database di **un** tenant (slug e nome da
> D-11, passati a `--tenant` al momento dell'esecuzione). Il modello di lettura è in
> `backend/src/Auxilia.MigrationRunner/LegacyImport/`; lo schema legacy di riferimento è generato dalle migrazioni EF del
> legacy (`backend/tests/Auxilia.LegacyImport.Tests/Fixtures/legacy-*.sql`). Un test fallisce se una tabella legacy
> non ha una riga nella tabella del §3, o se il catalogo `LegacyTables` non coincide con gli script.

## 1. Esecuzione

| Comando | Task | Cosa fa |
|---|---|---|
| `auxctl legacy inspect [--tenant <slug>]` | E-01 | Legge il database legacy: variante dello schema (con o senza `Security_Update`, tabella `ImportJobs`), ultima migrazione EF applicata, righe per tabella con destino e task, utenti esclusi (solo SystemConfigurator) e senza ruolo, tabelle sconosciute. Con `--tenant` aggiunge le righe già mappate in `ops.legacy_id_map` del tenant. Non scrive nulla. |
| `auxctl legacy import --tenant <slug> [--files <dir>] [--dry-run] [--since <istante>]` | E-02…E-06 | Import per moduli nell'ordine del §4 in **una** transazione del tenant (annullata con `--dry-run`, stesso report). Ripetibile: le righe già importate si aggiornano. Report per tabella (creati, aggiornati, esclusi, scartati) e avvisi per riga raggruppati per motivo, con gli id legacy. Termina sempre con la riconciliazione (§7): con differenze il codice d'uscita è 2 (`AUX-28010`) e il cutover è bloccato. |

- La stringa di connessione del legacy si legge dalla variabile d'ambiente **`AUXILIA_LEGACY_CONNECTION`**, mai dalla riga di comando e mai stampata (come `AUXILIA_TENANT_CONNECTION`). Conviene un utente PostgreSQL con sola lettura; in ogni caso la sessione è aperta con `default_transaction_read_only=on`, quindi ogni scrittura sul legacy fallisce.
- Prima di leggere, lo schema viene confrontato con il modello di lettura: tabelle della baseline o colonne mancanti → `AUX-28007` e nessuna lettura; server non raggiungibile → `AUX-28006`; ispezione riuscita → log `AUX-28008` (solo conteggi, nessun dato personale).
- Tabelle presenti nel database ma sconosciute al catalogo (un legacy più recente della baseline) vengono elencate da `inspect`: vanno mappate qui prima dell'import.

## 2. Regole generali

- **Id**: ogni riga legacy migrata riceve un Guid v7 registrato in `ops.legacy_id_map (entity, legacy_id, new_id, imported_at)`, con `entity` = nome della tabella legacy (`Users`, `Subscriptions`, …). Una riga già mappata conserva il suo Guid: un import ripetuto o con `--since` aggiorna gli stessi record invece di duplicarli. Ogni chiave esterna legacy si risolve **solo** attraverso la mappa (`LegacyIdMap`); un riferimento non risolvibile è un errore di riga nel report, non un record orfano. La mappa e le righe a cui appartiene si salvano nella stessa transazione.
- **Scrittura**: l'import scrive con il modello di persistenza del tenant (`TenantDbContext`) e i costruttori del dominio, non con i Manager: niente notifiche, e-mail, outbox o push durante la migrazione. Le regole del dominio (codice fiscale, stati, transizioni) restano valide: una riga che le viola finisce nel report con il motivo (ADR 0016).
- **Date e ore**: le colonne legacy sono `timestamp with time zone` (UTC). Gli istanti restano istanti. Le colonne che nel legacy sono **date di calendario** salvate come istante (`Users.DateOfBirth`, `RegistrationRequests.DateOfBirth`, `Subscriptions.StartDate/EndDate`) si convertono in data locale del fuso del tenant (`Europe/Rome` di default): la data di nascita inserita a mezzanotte locale risulta salvata il giorno prima alle 22:00/23:00 UTC. `DateOfBirth = -infinity` (default della colonna aggiunta in seguito) → nessuna data di nascita.
- **Audit**: le colonne di audit (`created_at`/`created_by`…) dei record migrati hanno l'istante dell'import e l'attore `System` (`auxctl`); le date storiche del legacy restano nei campi di dominio (`assigned_at` delle assegnazioni e dei membri, `password_changed_at`, `recorded_at` dei consensi, `attempted_at` degli accessi, date delle pratiche e dei documenti).
- **Segreti**: nessun segreto legacy viene copiato in chiaro. Gli hash BCrypt si copiano così come sono (`password_format = LegacyBcrypt`, rehash al primo accesso, ADR/P2-02); la password SMTP viene cifrata con Data Protection; token di reset e sessioni non si migrano.
- **Password note del legacy**: il legacy creava account con password fisse (`DataSeeder`: `admin`, `password`; approvazione delle registrazioni: `password`; impostazione `DefaultPassword`). Gli utenti il cui hash verifica una di queste password ricevono `must_change_password = true` e nessun accesso con quella password resta possibile senza cambiarla (E-02; elenco nel report, senza le password).
- **Testi personalizzati e JSON**: `CustomFields` (jsonb) passa invariato in `custom_fields` dopo la validazione contro le definizioni importate da `EntityConfigurations` (F20); chiavi sconosciute → avviso nel report.
- **Dati personali nei log e nei report**: solo conteggi, id legacy e codici d'errore; mai nomi, e-mail o codici fiscali.

## 3. Tabelle

| Tabella legacy | Destino | Task | Note |
|---|---|---|---|
| `Users` | `directory.people` + `identity.users` + `directory.client_profiles` / `employee_profiles` + `directory.client_assignments` | E-02 | §5.1. L'utente seedato `system` (solo ruolo SystemConfigurator) non si migra (D-18). |
| `Roles` | `identity.roles` (per nome) | E-02 | `Administrator`, `Employee`, `Client`; `SystemConfigurator` non esiste più (D-18). |
| `UserRoles` | `identity.user_roles` | E-02 | Per nome del ruolo; il ruolo SystemConfigurator si scarta. |
| `RoleSpecializations` | `directory.specializations` | E-02 | `PrivateSubscriptions` → `is_private`, `RoleId` → `role` (Client/Employee), `WorkNumber` → `work_phone`. |
| `UserRoleSpecializations` | `directory.specialization_members` | E-02 | `AssignedAt` → `assigned_at`. |
| `PasswordHistories` | `identity.password_history` (`format = LegacyBcrypt`) | E-02 | Solo con `Security_Update` (F35). |
| `LoginAuditLogs` | `identity.login_attempts` | E-02 | Solo con `Security_Update`; `LoginType` → `method` (§6), `Success`/`FailureReason` → `succeeded`/`failure_reason`. |
| `MembershipTypes` | `cases.service_categories` | E-03 | |
| `Memberships` | `cases.services` | E-03 | `MembershipTypeId` → `category_id`, `RoleSpecializationId` → `specialization_id`, `DurationDays` mantenuto, `currency = EUR`. |
| `MembershipFolderTemplates` | `cases.service_folders` | E-03 | Albero con `ParentId` → `parent_id`, `SortOrder` → `sort_order` (F33). |
| `Subscriptions` | `cases.cases` + `case_payments` + `case_status_history` + `case_numbers` | E-03 | §5.2. |
| `UserDocuments` | `documents.documents` + `documents.document_areas` + file | E-04 | §5.3. |
| `Appointments` | `scheduling.appointments` + `appointment_status_history` | E-05 | `ScheduledDate` → `starts_at`, `ends_at = starts_at + DurationMinutes` (durata ricondotta a 5…1440 minuti con avviso), `ShowInGlobalCalendare` → `show_in_global_calendar`, `EmployeeId` → `employee_user_id`, note tagliate a 1000 caratteri; anche gli appuntamenti passati; una riga di storico con lo stato legacy (§6, sconosciuto → scartato); `requested_by_client` = stato `Pending` (il legacy non lo registra); cliente non migrato → scartato. |
| `Requests` | `engagement.requests` + `request_messages` | E-05 | `Message` → messaggio #1 (autore `SenderId`, `CreatedAt`); `Response` non vuota → messaggio #2 (autore il destinatario, o il primo Administrator migrato se `ReceiverId` è null, `RespondedAt`) e stato `Responded`; `ReceiverId = null` o destinatario non migrato → ufficio; tipo §6 (sconosciuto → `General` con avviso); oggetto e testo tagliati ai limiti; mittente non migrato → scartata. A un'esecuzione successiva una risposta arrivata nel frattempo diventa il messaggio #2. |
| `Notifications` | `engagement.notifications` | E-05 | §5.4. Utente non migrato → esclusa (contata). |
| `RegistrationRequests` | `directory.registration_requests` | E-05 | `IsProcessed = false` → `Pending`; processata con un utente legacy con la stessa e-mail (l'approvazione lo creava) migrato come cliente → `Approved` (`client_id` dalla mappa); altrimenti `Rejected`. `client_application` e versione privacy `legacy`, lingua predefinita del tenant, data di nascita nel fuso del tenant; una sola richiesta in attesa per e-mail (le altre scartate). A un'esecuzione successiva una richiesta processata nel frattempo viene approvata o rifiutata. |
| `ImportTypes` | `imports.import_types` | E-05 | Solo storico; `TargetEntity` §6; nome unico (doppione con ` (id legacy)`). |
| `Imports` | `imports.import_jobs` | E-05 | Solo storico, senza righe né file (`ImportedData` scartato); stato §6 (sempre concluso); nome vuoto → nome del file. |
| `ImportJobs` | `imports.import_jobs` | E-05 | Solo storico. Spesso **assente**: è nel modello EF legacy ma nessuna migrazione la crea (Q61); si legge quando c'è. |
| `Languages` | `localization.languages` | E-05 | Lingue attive del legacy attivate nel tenant; lingue nuove create. |
| `ResourceKeys` | `localization.resource_keys` | E-05 | Le chiavi legacy hanno gli stessi nomi del seed `D_20260930_003`. |
| `ResourceTranslations` | `localization.resource_translations` (`is_customized`) | E-05 | Valore diverso da quello del tenant → impostato come personalizzato (`is_customized = true`); chiavi solo legacy → nuove chiavi non di sistema (categoria legacy in minuscolo, o `legacy`). Chiavi delle schede allenamento (`Workout`, `Exercise`) escluse (D-09). |
| `SystemConfigurations` | `configuration.settings` + `configuration.branding_assets` | E-05 | §5.5. |
| `EmailConfigurations` | `configuration.messaging_accounts` | E-05 | Account `Email`/`smtp` (`SmtpServer`, `SmtpPort`, sicurezza da `EnableSsl` + porta: 465 → SSL alla connessione, SSL → STARTTLS, altrimenti nessuna; `Username`, `FromEmail`, `FromName`), password cifrata con Data Protection (`IAccountSecretProtector`), `IsActive` → `is_active`; l'account attivo diventa il predefinito per l'e-mail se il tenant non ne ha uno. Nessuna regola di invio: l'account predefinito serve ogni scopo e ruolo (N03). |
| `ModuleConfigurations` | `identity.role_permissions` | E-05 | §5.6 (come le pagine; nessun override nel Catalog). |
| `PageConfigurations` | `identity.role_permissions` + `configuration.grid_layouts` | E-05 | §5.6. |
| `EntityConfigurations` | `configuration.custom_field_definitions` | E-05 | Ogni `PropertyName` diventa un campo con la stessa chiave (i valori già copiati in `custom_fields` combaciano), etichetta = nome, tipo da `PropertyType` (§6), `VisibleOnGrid`; entità `User` → `client`, `Subscription` → `case`, `Appointment` → `appointment`, `UserDocument` → `document`, `Request` → `request`; booleani CAF/PATRONATO con `dashboard_counter` (F20, F27, Q41). Nome non valido come chiave → avviso. |
| `WorkoutPlans` | non migrata | – | Schede allenamento rimosse (D-09); il conteggio è nel report di `inspect`. |
| `UserSessions` | non migrata | – | Le sessioni terminano al cutover. |
| `AppLogs` | non migrata | – | Archiviata con il backup del legacy (R-03). |
| `PasswordResetTokens` | non migrata | – | Solo con `Security_Update`; link di reset di breve durata. |

## 4. Ordine

1. **E-02** lingue (servono a `language_code`), ruoli, specializzazioni, utenti → persone, account, profili, assegnazioni, membri delle specializzazioni, storico password, accessi.
2. **E-03** categorie, servizi, cartelle, pratiche (+ pagamenti, storico, numeri).
3. **E-04** aree, documenti e file.
4. **E-05** appuntamenti, richieste, notifiche, registrazioni, storico import, configurazione (impostazioni, account di invio, moduli e pagine, griglie, campi personalizzati, traduzioni, branding), consenso marketing.
5. **E-06** riconciliazione, `--dry-run` (tutto in una transazione annullata alla fine, con il report), `--since` (righe legacy create o modificate dopo l'istante, per il delta del cutover).

## 5. Dettagli

### 5.1 Utenti

| Legacy | Nuovo |
|---|---|
| `FullName` / `Surname` | `people.first_name` / `last_name` (il legacy usa `FullName` come nome; `Surname` vuoto → l'ultima parola di `FullName`, o `FullName` ripetuto se è una sola parola) |
| `Email`, `Phone`, `FiscalCode` | `people.email`, `phone`, `fiscal_code` (regole del dominio; un valore non valido, o un codice fiscale già usato da un'altra persona, si lascia vuoto con un avviso: la persona si migra comunque) |
| `DateOfBirth` | `people.birth_date` (data locale, §2) |
| `Username` | `users.user_name` (spazi → punti; unico senza distinzione di maiuscole, Q52: duplicato → `.{id legacy}` aggiunto, con avviso) |
| `PasswordHash` | `users.password_hash`, `password_format = LegacyBcrypt` |
| `PasswordChangedAt` (`Security_Update`) | `users.password_changed_at`; senza la colonna → `CreatedAt` |
| `IsActive` | `users.is_active` (accesso); lo stato del cliente (D-05) si ricalcola dalle pratiche (§5.2) |
| `LanguageId` | `users.language_code` (codice della lingua legacy) |
| `ProfileImage` (data URL) | `identity.user_images` decodificata e ricodificata con `IImageProcessor` (JPEG ≤ 400 × 400); non decodificabile → scartata con avviso |
| `PrivacyConsent = true` | consenso `Privacy` / `Email` concesso, fonte `LegacyMigration`, istante `CreatedAt` |
| ruolo Client | `client_profiles` (+ consenso `Marketing` / `Email` = **true**, fonte `LegacyMigration`, D-23) |
| `AssignedEmployeeId` (cliente) | `client_profiles.employee_user_id` + una riga aperta in `client_assignments` dal `CreatedAt` |
| ruolo Employee | `employee_profiles` con `IsDefaultEmployee` → `is_default` (al più uno, Q31) e `AssignedAdministratorId` → `administrator_user_id` (Q32) |
| `EnableConfigurationSettings` | non migrato (ruolo SystemConfigurator rimosso, D-18) |
| `CustomFields` | `people.custom_fields` (§2) |

Utenti senza alcun ruolo: non migrati, elencati nel report (`inspect` ne dà il numero). Utenti con più ruoli: tutti i ruoli tranne SystemConfigurator.

### 5.2 Pratiche (`Subscriptions`)

| Legacy | Nuovo |
|---|---|
| `UserId` | `cases.client_id` (persona del cliente; utente non migrato o senza profilo cliente → pratica scartata) |
| `MembershipId` | `service_id`; `price` = prezzo del servizio legacy (`Memberships.Price`), `currency = EUR` |
| `RoleSpecializationId` | `specialization_id`, altrimenti quella del servizio (regole D-04 sulle pratiche private); solo specializzazioni Employee, le altre lasciate vuote con avviso |
| `StartDate` / `EndDate` | `started_on` / `expires_on` (date locali, §2; `EndDate` impostata dal legacy al completamento, Q03); `completed_at` = `EndDate` (istante) per le concluse |
| `Status` (int) | `status`: 0 `Inserted`, 1 `InProgress`, 2 `Sent`, 3 `Completed` |
| `IsRejected`, `IsActive` | `is_rejected`, `is_active` |
| `AmountPaid > 0` | un pagamento in `case_payments` (`amount` arrotondato a due decimali, `paid_on = StartDate`, nota `legacy`); a un'esecuzione successiva segue l'importo legacy |
| — | una riga in `case_status_history` con lo stato corrente (`from_status` null); a un'esecuzione successiva uno stato diverso aggiunge una riga |
| — | `number` `{anno}-{sequenza a 5 cifre}` da `case_numbers` per anno di `started_on`, in ordine di id legacy (stessa sequenza del sistema nuovo) |
| `CustomFields` | `custom_fields` (solo oggetti JSON; altrimenti `{}` con avviso) |

Stato fuori da 0…3 o data di inizio mancante → pratica scartata. Dopo le pratiche si ricalcola lo stato di ogni cliente con la regola del sistema nuovo (Q03, `Case.CountsAsOpen`: attivo ⇔ almeno una pratica attiva, non conclusa e senza scadenza o con scadenza futura; il legacy contava anche le concluse il giorno stesso).

Catalogo: nomi di categorie attive e di servizi unici (un doppione riceve ` ({id legacy})` con avviso); durata fuori da 1…3650 giorni ricondotta all'intervallo con avviso; prezzo arrotondato a due decimali; specializzazione del servizio solo se Employee. Cartelle: genitori prima dei figli, profondità oltre 10 o genitore non migrato → scartata.

### 5.3 Documenti (`UserDocuments`)

- **File**: i file legacy si copiano prima in una cartella locale passata con `--files <dir>`. Può essere la cartella `DocumentStorage:Path` del legacy (`uploads/documents`, volume Docker) così com'è, oppure il contenuto del container Azure o della cartella FTP del legacy scaricato con i loro strumenti (per esempio `azcopy`, `lftp`), perché le credenziali legacy non entrano nell'import. Un `FilePath` legacy (percorso relativo, assoluto o URL, sempre terminato da `{guid}_{nome}`) si cerca per percorso relativo e poi per ultimo segmento; nulla fuori dalla cartella viene letto. Senza `--files` i documenti sono scartati con un motivo.
- **Copia**: ogni file passa da `Documents.Public.IFileStore` con gli stessi controlli di un upload (tipo ammesso, contenuto coerente, dimensione massima `documents.maxUploadMb`, SHA-256) e finisce in `tenants/{slug}/documents/{yyyy}/{MM}/{id}.{ext}` dello storage del tenant (`documents.storage.provider`; con `local` auxctl usa la stessa `Storage:Local:RootPath` di Api e Worker). File rifiutato, mancante o `FilePath = queued` → documento scartato con il motivo (bloccante in riconciliazione). Con `--dry-run` i file vengono messi in staging per controllarli e poi cancellati. Un documento già importato non si copia di nuovo. Se la transazione fallisce, i file già copiati restano orfani nello storage (nessuna riga li usa) e un nuovo import li copia di nuovo.
- `Area` (testo) → `document_areas`: una per valore distinto, spazi compressi, uguale per nome (senza distinzione di maiuscole) a un'area attiva esistente; vuota → nessuna area.
- `UserId` → `client_id` (deve essere un cliente migrato); `SubscriptionId` → `case_id` se la pratica è stata migrata ed è dello stesso cliente, altrimenti il documento resta del solo cliente (avviso); `FolderTemplateId` → `folder_id` solo se la cartella è del servizio della pratica (F33), altrimenti radice della pratica (avviso); `UploadedByUserId` → `uploaded_by_user_id` (vuoto se l'utente non è migrato).
- `FileName` senza il prefisso `{guid}_` → `file_name`; `FileType`/`FileSize` → tipo e dimensione verificati dal file store (dimensione diversa dal record legacy → avviso); `ReferenceYear` (0 = colonna aggiunta dopo) → anno del caricamento nel fuso del tenant con avviso; `Description`, `UploadedAt`, `CustomFields` (solo oggetti JSON) → `description`, `uploaded_at`, `custom_fields`; `status = Available`.

### 5.4 Notifiche

Il legacy salva titolo e testo; il nuovo modello salva solo `kind` + parametri. Si migrano **tutte** (le lette con `read_at` = creazione, così lo storico resta) con kind `legacy.message` (`NotificationKinds.LegacyMessage`, testi EN + IT `{title}` / `{message}` dalla data-migration `D_20261006_002`; non è tra i tipi con preferenze perché nessuno la invia più). Il collegamento viene dal `Type` legacy con `RelatedEntityId` risolto nella mappa: `Appointment` → `/appointments?open={id}`, `Request` → `/requests?open={id}`, `Subscription`/`SubscriptionExpiring` → `/cases/{id}`; altri tipi o record non migrati → nessun collegamento.

### 5.5 Impostazioni (`SystemConfigurations`)

Ogni valore passa dalla sua `SettingDefinition` (tipo e regola di validità); un valore non valido lascia il predefinito con un avviso, una chiave sconosciuta è riportata.

| Legacy | Nuovo |
|---|---|
| `RegistrationEnabled`, `SendRegistrationConfirmationEmail` | `registration.enabled`, `registration.sendConfirmationEmail` |
| `RegistrationLanguage` | `registration.defaultLanguage` |
| `AutoSubscriptionExpiry`, `SubscriptionExpiringDays` | `cases.expiry.enabled`, `cases.expiry.expiringDays` (1…365) |
| `UseAppName` (solo il testo `False` lo spegne, come l'header legacy) | `branding.useAppName` |
| `ThemeType` `gradient` (+ `ThemePrimary`, `ThemeSecondary`) / altro (+ `ThemeSolid`) | `branding.theme.fill` `Gradient` (+ colore primario e di accento) / `Solid` (+ colore primario) |
| `BackgroundType` `image` / `color` / `gradient` | `branding.background.kind` `Image` / `Solid` / `Gradient` |
| `BackgroundColor` | `branding.background.color` |
| `BackgroundGradient` (CSS `linear-gradient(…)`) | i primi due colori → `branding.background.startColor` / `endColor` |
| `BackgroundImage` (base64) | `configuration.branding_assets` di tipo `Background` (JPEG, PNG o WebP ≤ 2 MB) |
| `DefaultPassword` (usato solo per marcare gli account del §2), `ReCaptcha`, `UseLocalCache`, `UseQueueForDocuments`, `EnableSubscriptionExpiryService`, `InitDatabase`, `SaveLog`, `AuditLog`, `LogBlobStorage`, `SessionTimeout` | non migrati (ARCHITECTURE §7.2) |

`AppName` e la cartella dei documenti erano in `appsettings` del legacy, non nel database: il nome dell'applicazione si imposta nella console (`branding.appName`) e lo storage con `documents.storage.*`.

### 5.5b Opzioni di `appsettings` del legacy (F32)

Le opzioni del file `Auxilia.Client.Web/appsettings.json` non si migrano come dati: ognuna diventa configurazione
dell'applicazione (host, segreti dell'hosting) o impostazione del tenant (console, `/platform/tenants/{slug}/settings`).

| Legacy | Nuovo |
|---|---|
| `AppName` | `branding.appName` (impostazione del tenant) |
| `DefaultPassword` | eliminato: account con link di attivazione (D-06) o password temporanea monouso (`auxctl users reset-password`) |
| `DetailedErrors` | solo in Development; gli errori mostrano codice `AUX-NNNNN` e traceId (F25) |
| `AllowedHosts` | `AllowedHosts` dell'API e `Tenancy:BaseDomains` (tenant dal sottodominio) |
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings:Catalog` + connection string protetta per tenant nel Catalog (D-02) |
| `InitDatabase` | `auxctl migrate catalog` / `migrate tenants` e data-migration incrementali (F30) |
| `SessionTimeout` | `auth.session.idleMinutes`, `auth.session.absoluteDays`, `auth.session.rememberMeDays` (impostazioni del tenant) |
| `ReCaptcha:SiteKey`, `ReCaptcha:SecretKey` | `client_applications.captcha_provider` + `Captcha:Altcha:Key` (B-06, D-14) |
| `UseLocalCache`, `CacheConnection` | `ConnectionStrings:Redis` facoltativa (senza: cache solo in memoria) |
| `Logging`, `Serilog`, `SaveLog`, `AuditLog:FilePath` | sezione `AuxiliaLogging` (console JSON + file giornalieri per tenant), livello di debug temporaneo per tenant (D-28); audit sempre in `audit.entity_changes` |
| `LogBlobStorage:*` | `AuxiliaLogging:Storage` = `azure-blob` (H-03) |
| `EnableSubscriptionExpiryService` | `cases.expiry.enabled` + job ricorrente `cases.expiry` (D-15) |
| `DocumentStorage:Path` | `documents.storage.provider` = `local` + `Storage:Local:RootPath` |
| `FtpStorage:*` | `documents.storage.ftp.*` (password come segreto protetto) |
| `AzureStorage:*` | `documents.storage.azure.*` |
| `UseQueueForDocuments` | sempre: controllo dei file nel Worker (`ProcessDocumentCommand`, coda `auxilia.documents`) |
| `RabbitMQ:*` | `ConnectionStrings:RabbitMq`; `MaxMessageSizeMB` non serve (i file non viaggiano nei messaggi) |

### 5.6 Moduli, pagine, griglie (F21, F22, Q40)

- Il legacy nega una pagina a un ruolo con una riga `IsEnabled = false` (`ModuleConfigurations` per il modulo, `PageConfigurations` per la pagina). Per ogni riga disabilitata di un ruolo del tenant si tolgono a quel ruolo i permessi della pagina in `identity.role_permissions`. La sincronizzazione dei permessi a ogni migrazione non li restituisce, perché tocca solo i permessi nuovi. Le pagine abilitate o senza riga mantengono i permessi predefiniti dei moduli.
- Pagine → permessi: `Dashboard` → `reporting.dashboard.view`; `Clients` → `directory.clients.*`; `Employees` → `directory.employees.*`; `Requests`/`UserRequests` → `engagement.requests.*`; `RegistrationRequests` → `directory.registrations.review`; `Subscriptions`/`Subscription` → `cases.cases.*`; `Memberships` → `cases.services.*`; `Appointments` → `scheduling.appointments.*`; `Documents` → `documents.files.*`; `Sessions` → `identity.sessions.*`. Senza equivalente (escluse): `Logs` (log della piattaforma, D-17), `WorkoutPlans` (D-09), `AllClients` (filtro "tutti i clienti" della lista). Ruolo SystemConfigurator: escluso (D-18). Altre pagine: avviso.
- `ConfigurationGrid` di una pagina abilitata → layout del ruolo della griglia corrispondente (`Clients` → `directory.clients`, `Employees` → `directory.employees`, `Subscriptions` → `cases.cases`, `Memberships` → `cases.services`, `Appointments` → `scheduling.appointments`, `Requests` → `engagement.requests`). Le colonne legacy sono visibili nell'ordine legacy; quelle che il legacy poteva mostrare ma non mostrava sono nascoste; le colonne nuove (per esempio il numero della pratica) mantengono il loro valore predefinito; quelle non nascondibili restano visibili.
- Se l'import gira su un tenant già in uso, permessi, griglie e impostazioni sono in cache: va svuotata la cache del tenant (runbook).

## 6. Valori

| Legacy | Valori legacy | Nuovo |
|---|---|---|
| `Subscriptions.Status` | 0, 1, 2, 3 | `Inserted`, `InProgress`, `Sent`, `Completed` |
| `Appointments.Status` | `Pending`, `Approved`, `Rejected`, `Completed` (o `Concluded`), `Cancelled` | stesso nome (`AppointmentStatus`) |
| `Requests.Status` | `Pending`, `Responded` | `Pending`, `Responded` |
| `Requests.Type` | `General`, `Information`, `Support` (select del form) | stesso nome (`RequestType`) |
| `LoginAuditLogs.LoginType` | `Password`, `Otp` | `password`, `email-otp` |
| `EntityConfigurations` `PropertyType` | `boolean`, `number`/`int`/`decimal`, `date`/`datetime`, altro | `Boolean`, `Number`, `Date`, `Text` |
| `Imports.Status` / `ImportJobs.Status` | `Concluded`, `Completed`, `Failed`, altro (`Pending`, `Running`, `Processing`) | `Completed`, `Completed`, `Failed`, `Cancelled` (mai finito nel legacy) |
| `ImportTypes.TargetEntity` | `Client`, `Employee`, `Subscription`, `Membership` | `Client`, `Employee`, `Case`, `Service` |
| `Roles.Name` | `Administrator`, `Employee`, `Client`, `SystemConfigurator` | i primi tre; l'ultimo scartato |
| `RoleSpecializations.RoleId` | id del ruolo | `role` per nome (solo Client/Employee) |

Un valore non elencato è un errore di riga nel report (mai un default silenzioso).

## 7. Riconciliazione (E-06, bloccante per il cutover)

Ogni esecuzione termina con la riconciliazione (`LegacyReconciliation`), dentro la stessa transazione: anche un `--dry-run` è riconciliato con quello che avrebbe scritto. Il report elenca ogni controllo con `ok` o `DIFF`, il valore del legacy e quello del tenant; una differenza dà il codice d'uscita 2 e il log `AUX-28010`.

| Controllo | Legacy | Tenant |
|---|---|---|
| Righe per tabella migrata riga per riga (`Users`, `RoleSpecializations`, `MembershipTypes`, `Memberships`, `MembershipFolderTemplates`, `Subscriptions`, `UserDocuments`, `Appointments`, `Requests`, `Notifications`, `RegistrationRequests`, `ImportTypes`, `Imports`, `ImportJobs`, `LoginAuditLogs`, `EmailConfigurations`) | righe della tabella | righe importate (`ops.legacy_id_map`) + escluse per decisione + scartate con un motivo |
| Utenti per ruolo | ruoli degli utenti importati (senza SystemConfigurator) | ruoli di quegli account |
| Clienti assegnati a un operatore importato | `AssignedEmployeeId` | operatore in carico |
| Pratiche per stato, rifiutate | `Status`, `IsRejected` delle pratiche importate | stato ed esito |
| Importo pagato | somma di `AmountPaid` (a due decimali) | somma dei pagamenti con nota `legacy` |
| Appuntamenti per stato | stato legacy | stato |
| Richieste con risposta | `Response` non vuota | messaggio #2 presente |
| Registrazioni processate | `IsProcessed` | Approved + Rejected |
| Documenti | documenti importati | file riletti dallo storage del tenant con la stessa dimensione e lo stesso SHA-256 (non in `--dry-run`, che non scrive file) |

Le traduzioni, le impostazioni, i campi personalizzati, i permessi e le griglie non hanno un conteggio da confrontare: ogni valore scartato o lasciato al predefinito è un avviso del report, da leggere prima del cutover.

**Delta al cutover (`--since <istante>`).** Il legacy non registra quando una riga cambia (solo alcune tabelle hanno una data di creazione), quindi le righe si confrontano sempre tutte: è questo che rende l'esecuzione ripetibile e la riconciliazione completa. `--since` limita il lavoro costoso: i file importati prima dell'istante non vengono riletti dalla riconciliazione. In ogni esecuzione, inoltre, la verifica BCrypt delle password assegnate dal legacy (§2) riguarda solo gli hash nuovi o cambiati. Procedura: un import completo prima della finestra di manutenzione, poi, con il legacy in sola lettura, un import con `--since` uguale all'istante del precedente, senza differenze.

## 8. Punti aperti

- **D-11**: slug e nome del tenant del cliente attuale (servono solo per eseguire, non per sviluppare: `--tenant`).
- Variante del database di produzione: `auxctl legacy inspect` sul dump dirà se `Security_Update` è applicato e se `ImportJobs` esiste.
- Storage dei documenti di produzione (provider e percorsi reali di `FilePath`), da verificare sul dump (E-04).
