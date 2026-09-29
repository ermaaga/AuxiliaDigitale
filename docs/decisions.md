# Registro decisioni del progetto

Decisioni prese con l'utente. Estende `decisions.md` del marketplace `auxilia-claude-skills` (#1–#12); in caso di conflitto vale **questo file** (più recente). Ogni cambio di decisione va riportato qui e nei documenti che la citano.

| ID | Tema | Decisione | Data | Stato |
|---|---|---|---|---|
| D-01 | Remote GitHub | Da definire | – | Aperta (P0-01) |
| D-02 | Creazione DB tenant da `auxctl` (utente con `CREATEDB`) | Default: sì, con `--existing-database` | – | Aperta (fine Fase 1) |
| D-03 | "Token di sicurezza" | Default: refresh token rotante + credenziale client app (`X-Client-Id`) | – | Aperta (inizio Fase 2) |
| D-04 | Visibilità pratiche private | **Come il codice legacy**: pratica visibile se senza specializzazione, o con specializzazione non privata, o se l'operatore possiede la specializzazione. Administrator vede tutto | 2026-09-29 | Confermata |
| D-05 | Accesso vs stato cliente | **Separati**: `users.is_active` (può fare login) ≠ `client_profiles.status` (Attivo/Inattivo in base alle pratiche) | 2026-09-29 | Confermata |
| D-06 | Password iniziale | **Link di attivazione** monouso via email; lo staff può impostare una password a mano e reinviare il link | 2026-09-29 | Confermata |
| D-07 | Scadenza pratica alla creazione | Default: `expires_on` nullo (come legacy), `due_on` facoltativo | – | Aperta (prima di B-08) |
| D-08 | Sessione singola per utente | **Impostazione tenant `auth.singleSession`, default OFF** | 2026-09-29 | Confermata |
| D-09 | Schede allenamento / codice palestra | **Rimosso** dal prodotto (F18) | 2026-09-29 | Confermata |
| D-10 | Email automatica di scadenza | Superata da D-15 (nessun job schedulato); l'azione manuale "invia avviso scadenza" resta | 2026-09-29 | Chiusa |
| D-11 | Slug e nome del tenant del cliente attuale | Da chiedere | – | Aperta (Fase 7 ETL) |
| D-12 | Hosting prod, osservabilità (tracce/metriche), mobile | Aperte; i log vanno su Azure Storage (D-17) | – | Aperta (Fase 8) |
| D-13 | Perimetro prodotto | Gestione **clienti, pratiche, appuntamenti, campagne marketing** | 2026-09-29 | Confermata |
| D-14 | Registrazione esterna dei clienti | **Solo API** (invio richiesta da un client esterno registrato + elenco/approva/rifiuta). **Nessuna pagina**, né pubblica né di approvazione | 2026-09-29 | Confermata |
| D-15 | Job schedulati | **Nessun job schedulato** in tutto il sistema. Il Worker elabora solo messaggi in coda. Le logiche periodiche (es. scadenze pratiche) sono comandi eseguibili a mano (API di piattaforma / `auxctl`) e registrate in un registry pronto per uno scheduler futuro, oggi disattivato | 2026-09-29 | Confermata |
| D-16 | Account di invio (SMTP, in futuro WhatsApp) | **N account per canale**; scelta con regole **scopo + ruolo di chi invia** (es. Transazionale/Notifica/Marketing × Administrator/Employee), con account di default per canale | 2026-09-29 | Confermata |
| D-17 | Log | **Tutti i log Information+** su file giornalieri **per tenant** nello storage account (`tenants/{slug}/…`, + file `platform/…` per eventi senza tenant). Nessuna copia nel DB. La cancellazione/retention è fatta **fuori dal sistema** sullo storage account. La pagina Log legge i file | 2026-09-29 | Confermata |
| D-18 | Ruolo System | **Ruolo di piattaforma** (sopra i tenant): crea/modifica/sospende/elimina tenant, abilita/disabilita moduli per tenant e per ruolo (base per i piani di prezzo futuri), gestisce la parte tecnica di ogni tenant (griglie, etichette/traduzioni, campi custom, account SMTP/WhatsApp per ruolo, branding, impostazioni, permessi, specializzazioni, import). **Unico** a gestire i moduli. Il ruolo `SystemConfigurator` del tenant non esiste più | 2026-09-29 | Confermata (dettagli in ARCHITECTURE §4) |
| D-19 | Login Google | Rimandato: servirà per far accedere i **clienti dall'app mobile**. Ora si predispone solo l'astrazione dei metodi di autenticazione | 2026-09-29 | Confermata |
| D-20 | Marketing v1 | Solo **email**; **segmenti dinamici**, **liste statiche**, **disiscrizione + consensi**. Niente tracciamento aperture/click, niente invii programmati (solo "invia ora" tramite coda). WhatsApp in futuro tramite un endpoint esterno (adapter HTTP) | 2026-09-29 | Confermata |
| D-21 | System e dati di business (Q-A) | Il System **non** vede né modifica i dati di business dei tenant | 2026-09-29 | Confermata |
| D-22 | 2FA System (Q-B) | **Obbligatoria** (TOTP) per gli utenti System | 2026-09-29 | Confermata |
| D-23 | Consenso marketing clienti migrati (Q-C) | Alla migrazione il consenso marketing email viene impostato a **true** (fonte `LegacyMigration`) | 2026-09-29 | Confermata |
| D-24 | Pagina di disiscrizione (Q-D) | **Rimandata**: nessuna pagina né link di disiscrizione in v1; la revoca del consenso la fa lo staff dalla scheda cliente | 2026-09-29 | Confermata |
| D-25 | Eliminazione tenant (Q-F) | Solo **archiviazione** (nessuna eliminazione fisica del DB dal sistema) | 2026-09-29 | Confermata |
| D-26 | Organizzazione della logica applicativa | **Manager** per area (es. `ICaseManager`) iniettati con dependency injection, al posto di un handler per ogni caso d'uso; dettagli in ARCHITECTURE §4bis | 2026-09-29 | Preferenza confermata, dettagli da approvare |
| A-01 | Layout del codice | Progetti per layer con cartelle per modulo + descrittori di modulo + test di confine | 2026-09-29 | Proposta (in ARCHITECTURE, da approvare) |
