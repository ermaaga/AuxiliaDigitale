# Performance e accessibilità — misure di H-02

Stato al 2026-10-06. Obiettivo: liste veloci con i volumi di uno studio grande dopo anni di uso, Lighthouse ≥ 90,
WCAG 2.2 AA verificata in tema chiaro e scuro.

## 1. Database: liste con volume realistico

**Metodo.** Un database tenant usa e getta (PostgreSQL 18, tutte le migrazioni) con dati generati in SQL:
50.052 persone (2 Administrator, 50 operatori, 50.000 clienti), 5 specializzazioni (2 private), 40 servizi,
200.000 pratiche, 500.000 documenti, 150.000 appuntamenti, 500.000 notifiche, 300.000 accessi, 30.000 richieste,
30.000 task (561 MB). Le porte dati delle liste (`ClientData`, `CaseData`, `DocumentData`, `AppointmentData`) sono
state chiamate direttamente con i filtri reali, registrando l'SQL generato da EF Core e il tempo medio di tre
esecuzioni a cache calda; i casi lenti analizzati con `EXPLAIN ANALYZE`.

| Lista (pagina di 25 + totale) | Prima | Dopo | Intervento |
|---|---|---|---|
| Documenti, operatore (F10) | **4.512 ms** | ~470 ms (pagina 36 ms + totale ~430 ms) | regola "cliente in specializzazione privata altrui" non più correlata per documento; indice `documents(uploaded_at, id)` |
| Documenti, Administrator | 94 ms | ~45 ms (pagina 1,6 ms) | indice `documents(uploaded_at, id)` |
| Pratiche, operatore (F10) | 447 ms | ~210 ms (pagina 0,6 ms) | indice `cases(started_on, number, id)` |
| Pratiche, Administrator | 82 ms | ~28 ms (pagina 0,8 ms) | indice `cases(started_on, number, id)` |
| Dashboard pratiche, operatore | 596 ms | invariato | 5 conteggi sulle ~100k pratiche visibili, pagina aggiornata ogni minuto: accettato |
| Pratiche ordinate per importo pagato | 364 ms | invariato | ordinamento raro su somma calcolata: accettato |
| Clienti (tutti, miei, nome, e-mail, telefono, contatori) | 4–60 ms | invariato | — |
| Documenti per nome file (`ILIKE '%…%'`) | 133 ms | invariato | — |
| Appuntamenti (tutti, operatore, mese) | 3–29 ms | invariato | — |

**Decisioni.**
- Il totale delle liste resta esatto: un operatore che vede quasi tutti i 500.000 documenti paga ~0,4 s per contarli.
  Un totale stimato cambierebbe paginazione ed export: da rivalutare solo se i volumi reali lo richiedono.
- Nessun indice trigram (`pg_trgm`) per le ricerche `ILIKE '%…%'`: con questi volumi restano sotto 140 ms, mentre gli
  indici GIN rallentano ogni scrittura (import legacy compreso).
- Gli indici sono creati normalmente (non `CONCURRENTLY`): i database dei tenant nascono al cutover, prima dell'uso.
- La regola F10 dei documenti senza pratica ha ora un test SQL (`DocumentPersistenceTests`), verificato anche
  rompendo volutamente la regola.

## 2. Web: Lighthouse

`e2e/lighthouse.spec.ts` (ADR 0017) sulla build di produzione, preset desktop, sessione reale di un Administrator. Per non allungare la CI gira su richiesta, insieme allo sweep di accessibilità: `pnpm --filter web e2e:quality`.

| Pagina | Performance | Accessibilità | Best practice |
|---|---|---|---|
| Login tenant, login console | 100 | 100 | 100 |
| Dashboard, clienti, pratiche, documenti, appuntamenti, task | 100 | 100 | 100 |

Correzioni emerse (prima: best practice 92–96 e accessibilità 95 su alcune pagine):
- **CSP e `eval`**: zod 4 prova `new Function` al primo parsing; la CSP (senza `unsafe-eval`) lo bloccava con una
  violazione su ogni pagina con form → `z.config({ jitless: true })` nel browser (`lib/zod-config.ts`).
- **CSP e font**: FullCalendar incorpora il font delle icone come `data:` → `font-src 'self' data:` (prima le frecce del
  calendario non comparivano e la console registrava un errore).
- **Schede senza pannello** (`/tasks`, casella delle richieste): `aria-controls` puntava a un pannello inesistente →
  la tabella è il `TabsContent` della scheda attiva.
- **Menu dell'account** (WCAG 2.5.3, *label in name*): il nome accessibile ora contiene le iniziali visibili.

Restano suggerimenti informativi interni a Next.js (JavaScript per browser datati, CSS che blocca il rendering),
senza effetto sul punteggio.

## 3. Accessibilità

`e2e/accessibility.spec.ts`: axe (WCAG 2.0/2.1/2.2 A e AA, nessuna violazione seria o critica) su 26 pagine del tenant
di un Administrator e 4 pagine pubbliche, **in tema chiaro e scuro** (il test verifica che il tema sia davvero
applicato), più il layout a 360 px delle pagine senza una spec funzionale (task, sessioni, segmenti, esclusioni,
template). Le altre spec continuano a controllare con axe ogni pagina che visitano.
