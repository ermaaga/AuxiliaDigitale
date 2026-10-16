# Nota privacy — account collegati, posta e calendari esterni (N05)

Stato al 2026-10-16 (C-08). Destinatari: il titolare del trattamento (lo studio cliente, cioè il tenant) e chi ne cura
la documentazione privacy. Descrive cosa fa il modulo `mailbox` (ADR 0021) con i dati personali, per il registro dei
trattamenti e per decidere se serve una DPIA. Non è un parere legale.

## 1. Cosa succede

Un utente dello studio (Administrator o Employee, mai i clienti) collega dal proprio profilo una o più caselle di posta
(Google, Microsoft, PEC/IMAP) e i propri calendari (Google, Microsoft). All'apertura delle pagine Posta e Calendario,
o con "Sincronizza ora", Auxilia copia dal fornitore:

| Dato copiato | Dove | Di chi sono i dati |
|---|---|---|
| Intestazioni dei messaggi: mittente (nome e indirizzo), destinatari e Cc, oggetto, date, letto/non letto, dimensione, nomi degli allegati | `mailbox.mail_messages` (lunghezze limitate) | dell'utente e di **terzi** (clienti, fornitori, chiunque scriva) |
| Cartelle (solo Posta in arrivo e Inviata) | `mailbox.mail_folders` | dell'utente |
| Eventi del calendario nella finestra −30 / +180 giorni: titolo, luogo, inizio/fine, provvisorio, link | `mailbox.calendar_events` | dell'utente e di terzi nominati nel titolo o nel luogo |
| Indirizzo dell'account, colore, server IMAP, stato, ultima sincronizzazione | `mailbox.connected_accounts` | dell'utente |
| Token OAuth (refresh) o password IMAP | `mailbox.connected_accounts.protected_secret`, cifrati con Data Protection per tenant | dell'utente |
| Esito di ogni sincronizzazione (conteggi, codice d'errore, trace id) | `mailbox.sync_runs` | nessun contenuto |
| Messaggi inseriti in una pratica: solo hash dell'id del messaggio e dell'indirizzo dell'account | `mailbox.filed_messages` | nessun contenuto in chiaro |

**Non vengono copiati** i corpi dei messaggi né gli allegati: vengono letti dal fornitore solo quando l'utente apre un
messaggio, mostrati e non salvati (il file temporaneo dell'originale è cancellato a fine richiesta). Fa eccezione ciò
che l'utente **inserisce in una pratica** ("Inserisci in pratica"): l'originale `.eml` e gli allegati scelti diventano
documenti della pratica e seguono da lì le regole dei documenti (F14, conservazione della pratica).

## 2. Finalità, accesso, conservazione

- **Finalità**: permettere all'utente di vedere la propria posta e i propri impegni dentro Auxilia e di archiviare nelle
  pratiche i messaggi che le riguardano. Nessun altro uso (niente profilazione, niente marketing, niente invio).
- **Accesso**: solo il proprietario dell'account. Gli Administrator dello studio non vedono la posta degli altri utenti,
  il personale della piattaforma (System) non vede nulla (D-21). Verificato da `MailboxIsolationTests` (altro utente
  e altro tenant) e da `SecurityPerimeterTests` (token di piattaforma).
- **Conservazione**: finché l'account resta collegato. Scollegandolo si cancellano subito account, segreti, intestazioni,
  eventi e cronologia delle sincronizzazioni, e si revoca il consenso presso il fornitore quando lo prevede (Google);
  restano i documenti già inseriti nelle pratiche. **Eliminando l'operatore** (Directory) i suoi account collegati e le
  copie si cancellano nella stessa operazione, con la revoca dopo il commit (`Mailbox.Public.IConnectedAccountCleanup`,
  test `DeletingTheEmployee_RemovesTheirConnectedAccountsAndCopies`). La sola **disattivazione dell'accesso** invece
  li lascia (l'operatore può essere riattivato): chi esce dallo studio va eliminato, oppure scollega prima i propri
  account.
- **Minimizzazione**: solo intestazioni con lunghezze massime, prima sincronizzazione limitata (`mailbox.sync.initialDays`
  90, `initialMaxMessages` 2000), calendario nella finestra configurata, ambiti OAuth in sola lettura e chiesti
  separatamente per posta e calendario.
- **Log**: solo codici evento e id; mai indirizzi, oggetti o contenuti (eventi di sicurezza `AUX-29033…29036`).

## 3. Ruoli e fornitori

- Il **titolare** è lo studio (tenant); Auxilia, come gestore della piattaforma, tratta i dati per suo conto
  (responsabile, art. 28 GDPR) come per il resto dei dati del tenant.
- Google e Microsoft (e il gestore PEC) sono i fornitori **scelti dall'utente** per la propria casella: Auxilia vi accede
  con il consenso dell'utente e nei limiti degli ambiti concessi. L'app OAuth registrata dal gestore della piattaforma
  (`docs/runbooks/connected-accounts-oauth.md`) è soggetta alle condizioni dei fornitori: per Gmail gli ambiti sono
  *restricted* (verifica di Google e valutazione di sicurezza annuale prima di uscire dalla modalità di test).
- Le copie stanno nel database del tenant, nello stesso hosting dei dati dello studio (ADR 0022, regione UE).

## 4. Valutazione preliminare della DPIA

Criteri delle linee guida WP248 (EDPB) e dell'elenco del Garante (provvedimento 467/2018), applicati a questo
trattamento:

| Criterio | Presente? | Note |
|---|---|---|
| Valutazione, scoring, profilazione | no | nessuna elaborazione dei contenuti |
| Decisioni automatizzate con effetti giuridici | no | — |
| Monitoraggio sistematico | **in parte** | il trattamento riguarda la corrispondenza dei dipendenti, ma l'accesso è riservato al dipendente stesso; nessun accesso del datore di lavoro |
| Dati sensibili o di natura altamente personale | **possibile** | la corrispondenza di uno studio (CAF, patronato, commercialista) può contenere dati sanitari o giudiziari nell'oggetto o nei nomi degli allegati |
| Trattamento su larga scala | dipende dal tenant | per uno studio con pochi operatori, no |
| Combinazione di insiemi di dati | no | le intestazioni restano separate dall'anagrafica; il collegamento a un cliente avviene solo quando l'utente inserisce un messaggio in una pratica |
| Interessati vulnerabili | **possibile** | dipendenti (rapporto di lavoro) e clienti dei patronati |
| Nuove tecnologie | no | accesso standard OAuth/IMAP |
| Impedimento all'esercizio di diritti | no | — |

Due o più criteri possono ricorrere (corrispondenza dei dipendenti, dati di natura personale, interessati vulnerabili):
**la DPIA è raccomandata** per gli studi che trattano dati sanitari o di patronato, e comunque prudente prima di
abilitare il modulo `mailbox` in produzione. Il modulo è opzionale ma, come ogni modulo nuovo, `auxctl migrate catalog`
lo aggiunge al piano predefinito per tutti i ruoli (i permessi lo limitano ad Administrator ed Employee): finché la DPIA
non è fatta il System lo esclude per tenant (override del modulo dalla console), così la valutazione può essere fatta
studio per studio prima dell'attivazione.

Decisione aperta per l'utente (titolare/gestore): fare una DPIA unica per la piattaforma, da allegare al contratto con
gli studi, oppure lasciarla a ogni studio con questa nota come base.

## 5. Da fare prima dell'attivazione in produzione

- Aggiornare l'informativa ai dipendenti dello studio (uso facoltativo, cosa viene copiato, chi vede cosa) e, se
  serve, l'informativa ai clienti (messaggi archiviati nelle pratiche).
- Inserire il trattamento nel registro dello studio e del gestore della piattaforma.
- Procedura di uscita del dipendente: eliminarlo (gli account collegati si cancellano) invece di disattivarne solo
  l'accesso, oppure fargli scollegare prima gli account dal profilo.
- Escludere il modulo `mailbox` dai tenant che non hanno ancora fatto la valutazione (override dalla console).
- Per Google: completare la verifica dell'app e la valutazione di sicurezza per gli ambiti Gmail.
