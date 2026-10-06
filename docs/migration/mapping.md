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
| `auxctl legacy import --tenant <slug> [--dry-run]` | E-02…E-05 | Import per moduli nell'ordine del §4 in **una** transazione del tenant (annullata con `--dry-run`, stesso report). Ripetibile: le righe già importate si aggiornano. Report per tabella (creati, aggiornati, esclusi, scartati) e avvisi per riga raggruppati per motivo, con gli id legacy. Riconciliazione (§7) e `--since` con E-06. |

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
| `Appointments` | `scheduling.appointments` + `appointment_status_history` | E-05 | `ScheduledDate` → `starts_at`, `ends_at = starts_at + DurationMinutes`, `ShowInGlobalCalendare` → `show_in_global_calendar`, `EmployeeId` → `employee_user_id`; stato §6. |
| `Requests` | `engagement.requests` + `request_messages` | E-05 | `Message` → messaggio #1 (autore `SenderId`, `CreatedAt`); `Response` non vuota → messaggio #2 (autore il destinatario, o il primo Administrator se `ReceiverId` è null, `RespondedAt`); `ReceiverId = null` → ufficio; stato e tipo §6. |
| `Notifications` | `engagement.notifications` | E-05 | §5.4. |
| `RegistrationRequests` | `directory.registration_requests` | E-05 | `IsProcessed = false` → `Pending`; processata con un utente legacy con la stessa e-mail → `Approved` (`client_id` dalla mappa); altrimenti `Rejected`. `client_application = legacy`. |
| `ImportTypes` | `imports.import_types` | E-05 | Solo storico. |
| `Imports` | `imports.import_jobs` | E-05 | Solo storico, senza righe né file (`ImportedData` scartato); stato §6. |
| `ImportJobs` | `imports.import_jobs` | E-05 | Solo storico. Spesso **assente**: è nel modello EF legacy ma nessuna migrazione la crea (Q61); si legge quando c'è. |
| `Languages` | `localization.languages` | E-05 | Lingue attive del legacy attivate nel tenant. |
| `ResourceKeys` | `localization.resource_keys` | E-05 | Le chiavi legacy hanno gli stessi nomi del seed `D_20260930_003`. |
| `ResourceTranslations` | `localization.resource_translations` | E-05 | Valore diverso dal seed → aggiornato con `is_customized = true`; chiavi solo legacy → nuove chiavi non di sistema. Chiavi delle schede allenamento escluse (D-09). |
| `SystemConfigurations` | `configuration.settings` + `configuration.branding_assets` | E-05 | Tabella §5.5 (ARCHITECTURE §7.2). |
| `EmailConfigurations` | `configuration.messaging_accounts` + `configuration.sender_rules` | E-05 | Account `Email`/`smtp` di default, password cifrata con Data Protection, regola di default; `IsActive` → `is_active`. |
| `ModuleConfigurations` | `catalog.tenant_module_overrides` + `identity.role_permissions` | E-05 | `ModulePath` → codice del modulo; disabilitato per un ruolo → override del modulo per quel ruolo (F22). |
| `PageConfigurations` | `identity.role_permissions` + `configuration.grid_layouts` | E-05 | Pagina disabilitata per ruolo → permessi tolti al ruolo; `ConfigurationGrid` → layout della griglia per ruolo (F21). |
| `EntityConfigurations` | `configuration.custom_field_definitions` | E-05 | Definizioni CAF/PATRONATO incluse, con `dashboard_counter` (F20, F27, Q41). |
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

- `Area` (testo) → `document_areas` (una per valore distinto, nomi normalizzati negli spazi) e `area_id`.
- `UserId` → `client_id`; `SubscriptionId` → `case_id`; `FolderTemplateId` → `folder_id` (F33); `UploadedByUserId` → `uploaded_by_user_id`.
- `FileName`, `FileType`, `FileSize`, `ReferenceYear`, `Description`, `UploadedAt` → `file_name`, `content_type`, `size`, `reference_year`, `description`, `uploaded_at`.
- File: letto dallo storage legacy (`FilePath`, provider locale/FTP/Azure delle impostazioni legacy), copiato con `IFileStorage` in `tenants/{slug}/documents/…`, `sha256` calcolato, `status = Available`. `FilePath = queued` o file mancante → documento non migrato e riga nel report (bloccante in riconciliazione).

### 5.4 Notifiche

Il legacy salva titolo e testo; il nuovo modello salva solo `kind` + parametri. Proposta per E-05: solo le notifiche **non lette**, con kind `legacy.message` (testi EN + IT `{title}` / `{message}`) e collegamento dal `Type` (`Appointment` → appuntamento, `Request` → richiesta, `Subscription` → pratica, `RegistrationRequest` → registrazioni) risolto con la mappa; le lette si contano nel report. Da confermare all'avvio di E-05.

### 5.5 Impostazioni (`SystemConfigurations`)

Corrispondenze in ARCHITECTURE §7.2: `RegistrationEnabled` → `registration.enabled`, `SendRegistrationConfirmationEmail` → `registration.sendConfirmationEmail`, `RegistrationLanguage` → `registration.defaultLanguage`, `AutoSubscriptionExpiry`/`SubscriptionExpiringDays` → `cases.expiry.enabled`/`expiringDays`, `AppName` → `branding.app_name`, tema e sfondo → branding, `SessionTimeout` → `auth.session.idleMinutes`, storage documenti → `documents.storage.*` (credenziali cifrate). Eliminati: `DefaultPassword` (D-06; usato solo per marcare gli account del §2), `ReCaptcha`, `UseLocalCache`, `UseQueueForDocuments`, `EnableSubscriptionExpiryService`, `InitDatabase`, `SaveLog`, `AuditLog`. Chiavi sconosciute → avviso nel report.

## 6. Valori

| Legacy | Valori legacy | Nuovo |
|---|---|---|
| `Subscriptions.Status` | 0, 1, 2, 3 | `Inserted`, `InProgress`, `Sent`, `Completed` |
| `Appointments.Status` | `Pending`, `Approved`, `Rejected`, `Completed`, `Cancelled` | stesso nome (`AppointmentStatus`) |
| `Requests.Status` | `Pending`, `Responded` | `Pending`, `Responded` |
| `Requests.Type` | `General`, `Information`, `Support` (select del form) | stesso nome (`RequestType`) |
| `LoginAuditLogs.LoginType` | `Password`, `Otp` | `password`, `email-otp` |
| `Imports.Status` | `Pending`, `Running`, `Concluded`, `Completed`, `Failed` | `Pending`, `Processing`, `Completed`, `Completed`, `Failed` |
| `ImportJobs.Status` | `Processing`, `Completed`, `Failed` | stesso nome |
| `Roles.Name` | `Administrator`, `Employee`, `Client`, `SystemConfigurator` | i primi tre; l'ultimo scartato |
| `RoleSpecializations.RoleId` | id del ruolo | `role` per nome (solo Client/Employee) |

Un valore non elencato è un errore di riga nel report (mai un default silenzioso).

## 7. Riconciliazione (E-06, bloccante per il cutover)

- Righe per tabella legacy vs record creati, al netto delle esclusioni dichiarate (§3) e degli scarti elencati nel report.
- Utenti per ruolo; assegnazioni cliente → operatore; membri delle specializzazioni.
- Pratiche per stato e rifiutate; somma di `AmountPaid` = somma di `case_payments.amount`.
- Documenti: conteggio, file copiati con SHA-256 verificato rileggendo lo storage di destinazione.
- Appuntamenti per stato, richieste e messaggi, registrazioni per stato.
- Traduzioni personalizzate, impostazioni, account di invio, override di moduli e permessi.

## 8. Punti aperti

- **D-11**: slug e nome del tenant del cliente attuale (servono solo per eseguire, non per sviluppare: `--tenant`).
- Variante del database di produzione: `auxctl legacy inspect` sul dump dirà se `Security_Update` è applicato e se `ImportJobs` esiste.
- Storage dei documenti di produzione (provider e percorsi reali di `FilePath`), da verificare sul dump (E-04).
- Notifiche legacy (§5.4), da confermare in E-05.
