# Report di Sicurezza Applicativa: API & Autenticazione

**Progetto**: AuxiliaDigitale (`auxilia-next`)  
**Data**: 13 Ottobre 2026  
**Ambito**: Backend Minimal APIs (.NET 10), Autenticazione, Session Management, Tenancy, Frontend BFF (Next.js)  
**Standard di riferimento**: OWASP API Security Top 10 (2023), OWASP ASVS 4.0.3 Livello 2, Metodologia Strix  

---

## 1. Executive Summary

È stata effettuata una revisione di sicurezza applicativa (white-box security review) focalizzata sui vettori di autenticazione, gestione delle sessioni, multi-tenancy e perimetro delle API esposte.

L'architettura generale del progetto dimostra un elevato rigore progettuale (isolamento rigoroso dei tenant tramite `TenantResolutionMiddleware`, chiavi ES256 gestite da ring crittografico con rotazione, protezione CSRF multistrato nel BFF, e refresh token rotation con detection del riuso).

Tuttavia, l'analisi ha identificato **4 vulnerabilità** che richiedono interventi correttivi:
- **1 vulnerabilità di severità Alta**: mancata validazione dello stato di sessione e utente durante l'emissione di token di tenant per operatori di piattaforma.
- **2 vulnerabilità di severità Media/Alta**: Denial of Service (DoS) e Resource Exhaustion tramite inondazione delle partizioni del Rate Limiter ed esaurimento memoria nella cache dei template Liquid.
- **1 vulnerabilità di severità Bassa/Logica**: mancata transizione di stato ad account attivo al completamento della procedura di attivazione utente.

---

## 2. Metodologia di Analisi

L'audit ha seguito la tassonomia e i criteri di verifica adottati dalle skill di sicurezza **Strix**:
1. **API Security Testing (OWASP API Top 10:2023)**:
   - Verifica di autorizzazioni a livello di oggetto (BOLA/IDOR) e di funzione (BFLA).
   - Analisi del consumo di risorse e limiti di rate limiting (Unrestricted Resource Consumption).
   - Validazione dei confini multi-tenant (Cross-Tenant Access).
2. **Authentication & Session Lifecycle**:
   - Resilienza dei token di refresh a replay attack e rotazione crittografica.
   - Revoca immediata di token e chiusura sessioni (Deny-list e Security Stamp).
   - Gestione delle credenziali e attivazione account (Account Enrolment & Activation).

> [!NOTE]
> La suite autonoma DAST di Strix CLI richiede un runtime Docker o credenziali Strix Cloud attive per eseguire container di sandbox. L'analisi è stata condotta come code review mirata (SAST e Threat Modeling) su tutti i componenti core.

---

## 3. Matrice dei Risultati

| ID | Titolo | Severità | CWE | OWASP API 2023 | File Coinvolto |
| :--- | :--- | :---: | :--- | :--- | :--- |
| **SEC-01** | BFLA / Mancata validazione sessione in `OpenTenantAsync` | **Alta** | [CWE-613](https://cwe.mitre.org/data/definitions/613.html) | **API5:2023** | [PlatformAuthManager.cs](file:///app/backend/src/Auxilia.Application/Identity/PlatformAuthManager.cs#L259-L278) |
| **SEC-02** | Partition Flooding & DoS nel Rate Limiter tramite `X-Client-Id` | **Media/Alta** | [CWE-770](https://cwe.mitre.org/data/definitions/770.html) | **API4:2023** | [RateLimitingSetup.cs](file:///app/backend/src/Auxilia.Api/RateLimiting/RateLimitingSetup.cs#L101-L108) |
| **SEC-03** | Cache In-Memory Illimitata in `FluidTemplateRenderer` | **Media** | [CWE-400](https://cwe.mitre.org/data/definitions/400.html) | **API4:2023** | [FluidTemplateRenderer.cs](file:///app/backend/src/Auxilia.Infrastructure/Adapters/Templates/Fluid/FluidTemplateRenderer.cs#L14-L48) |
| **SEC-04** | Inconsistenza nello stato dell'account in `UseTokenAsync` | **Bassa** | [CWE-284](https://cwe.mitre.org/data/definitions/284.html) | **API5:2023** | [AccountLinkManager.cs](file:///app/backend/src/Auxilia.Application/Identity/AccountLinkManager.cs#L305-L327) |

---

## 4. Dettaglio delle Vulnerabilità e Patch Proposte

---

### SEC-01: BFLA e Mancata Validazione Sessione in `OpenTenantAsync`

#### Descrizione
L'endpoint `/api/v1/platform/tenants/{slug}/token` consente a un utente console (`System`) di ottenere un token di accesso limitato a un singolo tenant per eseguire operazioni di amministrazione tecnica.

Nel metodo `OpenTenantAsync`:
```csharp
public Task<Result<IssuedAccessToken>> OpenTenantAsync(string tenantSlug, Guid sessionId, string clientId, CancellationToken cancellationToken) =>
    operations.RunAsync(Operations.Identity.IssuePlatformTenantToken, new { TenantSlug = tenantSlug }, async scope =>
    {
        if (currentUser.ActorType != ActorType.Platform || currentUser.UserId is not { } platformUserId)
        {
            return Errors.Identity.PlatformAccessRequired();
        }

        var tenant = string.IsNullOrWhiteSpace(tenantSlug) ? null : await tenants.FindBySlugAsync(tenantSlug.Trim().ToLowerInvariant(), cancellationToken);
        if (tenant is null || tenant.Status == TenantStatus.Archived)
        {
            return Errors.Tenancy.TenantNotFound();
        }

        scope.SetEntity("Tenant", tenant.Id);
        var issued = await tokens.IssuePlatformAsync(
            new PlatformAccessTokenRequest(platformUserId, sessionId, clientId, tenant.Slug, PlatformIdentityPolicy.TenantTokenLifetime), cancellationToken);
        Log.Security.PlatformTenantAccess(logger, platformUserId, tenant.Slug);
        return Result.Success(issued);
    }, cancellationToken);
```

#### Meccanismo di Vulnerabilità
1. Il parametro `sessionId` inviato dal client o estratto dal token di accesso non viene validato sul database (`store.FindSessionAsync`).
2. Se un amministratore ha terminato la sessione (`EndSessionAsync` o `EndAllAsync`), o se l'utente è stato disattivato (`user.IsActive == false`), l'utente può continuare a generare nuovi access token di tenant finché il JWT di piattaforma non giunge a scadenza.
3. Inoltre, `session.UserId` non veniva confrontato con `platformUserId`, consentendo ipoteticamente a un operatore di associare token di tenant a identificatori di sessione non propri.

#### Patch Correttiva
Aggiunta dei controlli di esistenza della sessione, stato attivo della sessione e proprietà dell'utente:

```diff
--- a/backend/src/Auxilia.Application/Identity/PlatformAuthManager.cs
+++ b/backend/src/Auxilia.Application/Identity/PlatformAuthManager.cs
@@ -264,6 +264,18 @@ internal sealed class PlatformAuthManager : IPlatformAuthManager, IPlatformSessi
                 return Errors.Identity.PlatformAccessRequired();
             }
 
+            var session = await store.FindSessionAsync(sessionId, cancellationToken);
+            if (session is null || session.EndedAt is not null || session.UserId != platformUserId)
+            {
+                return Errors.Identity.RefreshTokenInvalid();
+            }
+
+            var user = await store.FindUserAsync(platformUserId, cancellationToken);
+            if (user is null || !user.IsActive)
+            {
+                return Errors.Identity.PlatformAccessRequired();
+            }
+
             var tenant = string.IsNullOrWhiteSpace(tenantSlug) ? null : await tenants.FindBySlugAsync(tenantSlug.Trim().ToLowerInvariant(), cancellationToken);
             if (tenant is null || tenant.Status == TenantStatus.Archived)
             {
```

---

### SEC-02: Partition Flooding & DoS nel Rate Limiter tramite `X-Client-Id`

#### Descrizione
Il middleware di rate limiting in [RateLimitingSetup.cs](file:///app/backend/src/Auxilia.Api/RateLimiting/RateLimitingSetup.cs#L101-L108) imposta un limitatore a livello di applicazione client:
```csharp
private static RateLimitPartition<string> ClientPartition(HttpContext context)
{
    var rules = Rules(context);
    var clientId = context.User.FindFirstValue(TokenClaims.Client) ?? context.Request.Headers["X-Client-Id"].FirstOrDefault();
    return rules is null || IsExempt(context) || string.IsNullOrWhiteSpace(clientId)
        ? RateLimitPartition.GetNoLimiter("none")
        : Sliding($"client:{clientId.Trim()}", rules.Client);
}
```

#### Meccanismo di Vulnerabilità
1. L'header HTTP `X-Client-Id` proviene da richieste non autenticate senza convalida di formato, lunghezza o esistenza a catalogo.
2. `PartitionedRateLimiter.GetSlidingWindowLimiter` di ASP.NET Core conserva internamente una mappa delle partizioni attive. Inviando un gran numero di richieste con stringhe `X-Client-Id` casuali (es. UUID o stringhe lunghe), l'attaccante forza l'allocazione incontrollata di oggetti in memoria (Memory Exhaustion DoS).
3. Inoltre, un attaccante esterno può impersonare un'applicazione legittima (es. inviando `X-Client-Id: auxilia-web`) e consumare l'intero budget rate limit a disposizione del client reale, provocando un disservizio per tutti i client legittimi (Client Starvation).

#### Patch Correttiva
Limitare la lunghezza di `X-Client-Id` al massimo consentito dal dominio (`ClientApplication.ClientIdMaxLength` = 100) e vincolare la partizione all'IP chiamante se la richiesta non è autenticata crittograficamente tramite token:

```diff
--- a/backend/src/Auxilia.Api/RateLimiting/RateLimitingSetup.cs
+++ b/backend/src/Auxilia.Api/RateLimiting/RateLimitingSetup.cs
@@ -1,6 +1,7 @@
 using System.Security.Claims;
 using System.Threading.RateLimiting;
 
+using Auxilia.Domain.Platform;
 using Auxilia.Diagnostics;
 using Auxilia.Infrastructure.Security.Tokens;
 
@@ -102,9 +103,17 @@ internal static class RateLimitingSetup
     private static RateLimitPartition<string> ClientPartition(HttpContext context)
     {
         var rules = Rules(context);
-        var clientId = context.User.FindFirstValue(TokenClaims.Client) ?? context.Request.Headers["X-Client-Id"].FirstOrDefault();
-        return rules is null || IsExempt(context) || string.IsNullOrWhiteSpace(clientId)
-            ? RateLimitPartition.GetNoLimiter("none")
-            : Sliding($"client:{clientId.Trim()}", rules.Client);
+        var clientId = context.User.FindFirstValue(TokenClaims.Client)
+            ?? context.Request.Headers["X-Client-Id"].FirstOrDefault();
+
+        if (rules is null || IsExempt(context) || string.IsNullOrWhiteSpace(clientId) || clientId.Length > ClientApplication.ClientIdMaxLength)
+        {
+            return RateLimitPartition.GetNoLimiter("none");
+        }
+
+        var trimmed = clientId.Trim();
+        var key = context.User.Identity?.IsAuthenticated == true
+            ? $"client:{trimmed}"
+            : $"client:{trimmed}:ip:{Ip(context)}";
+
+        return Sliding(key, rules.Client);
     }
```

---

### SEC-03: Cache In-Memory Illimitata in `FluidTemplateRenderer`

#### Descrizione
Il componente di rendering dei template Liquid in [FluidTemplateRenderer.cs](file:///app/backend/src/Auxilia.Infrastructure/Adapters/Templates/Fluid/FluidTemplateRenderer.cs#L14-L48) memorizza i template parsati in un dizionario concorrente:
```csharp
private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IFluidTemplate?> parsed = new(StringComparer.Ordinal);
```

#### Meccanismo di Vulnerabilità
1. `parsed` non implementa alcuna politica di rimozione (eviction) né un limite sul numero massimo di elementi o sui byte occupati.
2. Qualsiasi chiamata a `Render` o `IsValid` con un corpo template non identico a quelli già compilati inserisce una nuova voce permanente.
3. Se un utente autenticato o una funzione di preview/test invia template parametrici o generati dinamicamente, la memoria RAM del processo aumenta monotonamente fino al raggiungimento del limite del container/host, causando il crash del processo per `OutOfMemoryException`.

#### Patch Correttiva
Sostituzione del dizionario con un'istanza di `Microsoft.Extensions.Caching.Memory.MemoryCache` configurata con `SizeLimit = 1000` ed espulsione temporale scorrevole (`SlidingExpiration = 1 ora`):

```diff
--- a/backend/src/Auxilia.Infrastructure/Adapters/Templates/Fluid/FluidTemplateRenderer.cs
+++ b/backend/src/Auxilia.Infrastructure/Adapters/Templates/Fluid/FluidTemplateRenderer.cs
@@ -4,16 +4,23 @@ using Auxilia.Application.Abstractions.Channels;
 
 using Fluid;
+using Microsoft.Extensions.Caching.Memory;
 
 namespace Auxilia.Infrastructure.Adapters.Templates.Fluid;
 
-/// <summary>Liquid templates with Fluid; values are HTML-encoded in bodies. Parsed templates are cached by source.</summary>
-internal sealed class FluidTemplateRenderer : ITemplateRenderer
+/// <summary>Liquid templates with Fluid; values are HTML-encoded in bodies. Parsed templates are cached by source with a bounded size limit.</summary>
+internal sealed class FluidTemplateRenderer : ITemplateRenderer, IDisposable
 {
+    private const int MaxCachedTemplates = 1000;
     private static readonly FluidParser Parser = new();
 
-    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IFluidTemplate?> parsed = new(StringComparer.Ordinal);
+    private readonly IMemoryCache cache = new MemoryCache(new MemoryCacheOptions
+    {
+        SizeLimit = MaxCachedTemplates,
+    });
 
     public bool IsValid(string template) => Parse(template) is not null;
@@ -46,6 +53,23 @@ internal sealed class FluidTemplateRenderer : ITemplateRenderer
     private IFluidTemplate? Parse(string template)
     {
         ArgumentNullException.ThrowIfNull(template);
-        return parsed.GetOrAdd(template, source => Parser.TryParse(source, out var result, out _) ? result : null);
+        if (cache.TryGetValue(template, out IFluidTemplate? cached))
+        {
+            return cached;
+        }
+
+        var parsedTemplate = Parser.TryParse(template, out var result, out _) ? result : null;
+        cache.Set(template, parsedTemplate, new MemoryCacheEntryOptions
+        {
+            Size = 1,
+            SlidingExpiration = TimeSpan.FromHours(1),
+        });
+        return parsedTemplate;
+    }
+
+    public void Dispose()
+    {
+        if (cache is IDisposable disposable)
+        {
+            disposable.Dispose();
+        }
+    }
 }
```

---

### SEC-04: Inconsistenza nello Stato dell'Account in `UseTokenAsync`

#### Descrizione
Il flusso di attivazione account (`ActivateAccountRequest` via `/api/v1/auth/activate`) consuma un token monouso per impostare la password iniziale dell'utente in [AccountLinkManager.cs](file:///app/backend/src/Auxilia.Application/Identity/AccountLinkManager.cs#L305-L327):
```csharp
private async Task<Result<Guid>> UseTokenAsync(string token, UserTokenPurpose purpose, string password, IOperationScope scope, CancellationToken cancellationToken)
{
    // ... validazione token e user ...
    user.SetPassword(hasher.Hash(password), PasswordFormat.Identity, now);
    stored.Use(now);
    await store.SaveChangesAsync(cancellationToken);
    return user.Id;
}
```

#### Meccanismo di Vulnerabilità
1. Quando gli utenti vengono registrati o invitati con `IsActive = false` (in attesa di conferma e-mail e password), il completamento della procedura di attivazione imposta l'hash della password ma **non** esegue `user.SetActive(true)`.
2. Di conseguenza, l'utente non può effettuare il sign-in successivo poiché `PasswordAuthenticator` blocca gli account dove `!user.IsActive`, richiedendo un intervento manuale dell'amministratore.
3. Inoltre, se un account disattivato da un operatore dispone di un link di reset password non ancora scaduto, l'esecuzione di `ResetPasswordAsync` non verifica lo stato `user.IsActive`, consentendo la modifica della password su un account disabilitato.

#### Patch Correttiva
```diff
--- a/backend/src/Auxilia.Application/Identity/AccountLinkManager.cs
+++ b/backend/src/Auxilia.Application/Identity/AccountLinkManager.cs
@@ -314,6 +314,11 @@ internal sealed class AccountLinkManager : IAccountLinkManager
             return Errors.Identity.UserTokenInvalid();
         }
 
+        if (purpose == UserTokenPurpose.PasswordReset && !user.IsActive)
+        {
+            return Errors.Identity.UserTokenInvalid();
+        }
+
         scope.SetEntity("User", user.Id);
         var valid = await passwordPolicy.ValidateAsync(user, password, cancellationToken);
         if (valid.IsFailure)
@@ -322,6 +327,10 @@ internal sealed class AccountLinkManager : IAccountLinkManager
         }
 
         user.SetPassword(hasher.Hash(password), PasswordFormat.Identity, now);
+        if (purpose == UserTokenPurpose.Activation && !user.IsActive)
+        {
+            user.SetActive(true);
+        }
         stored.Use(now);
         await store.SaveChangesAsync(cancellationToken);
         return user.Id;
```

---

## 5. Verifiche di Sicurezza Positive Convalidate

L'audit ha confermato l'adozione di solide contromisure di sicurezza nelle seguenti aree chiave:

1. **Multi-Tenancy Isolation ([`TenantResolutionMiddleware.cs`](file:///app/backend/src/Auxilia.Api/Tenancy/TenantResolutionMiddleware.cs#L49-L55))**:
   - Qualsiasi disallineamento tra il claim `tenant` del JWT e l'header `X-Tenant` o l'Host viene intercettato immediatamente con `403 Forbidden` (`AUX-11004`) e log di audit di sicurezza dedicato.
2. **Token Security ([`SigningKeys.cs`](file:///app/backend/src/Auxilia.Infrastructure/Security/Tokens/SigningKeys.cs))**:
   - Utilizzo esclusivo di firme asimmetriche ES256 basate su ECDSA P-256.
   - Nessun algoritmo simmetrico o insicuro consentito. Esposizione JWKS con cache controllata a 300 secondi.
3. **BFF CSRF Protection ([`csrf.ts`](file:///app/frontend/apps/web/src/lib/bff/csrf.ts#L11-L23))**:
   - Protezione a doppio binario: cookie `SameSite=Lax` uniti alla validazione obbligatoria dell'header custom `x-requested-with: auxilia` e verifica del matching dell'header `Origin`.
4. **Refresh Token Rotation & Anti-Replay ([`SessionManager.cs`](file:///app/backend/src/Auxilia.Application/Identity/SessionManager.cs#L269-L276))**:
   - Ogni refresh emette un nuovo token e invalida il precedente. La presentazione di un token già consumato fa scattare l'allarme di furto di credenziali (`RefreshTokenReuse`) con revoca immediata di tutta la catena di sessioni dell'utente.

---

## 6. Conclusioni e Raccomandazioni

Le 4 patch proposte indirizzano in modo puntuale le criticità individuate senza introdurre breaking change architetturali:
- Risolvono l'esposizione a vettori di Denial of Service (CWE-400, CWE-770).
- Chiudono un potenziale bypass di autorizzazione per operatori di piattaforma (CWE-613).
- Armonizzano il ciclo di vita degli account durante la fase di attivazione e reset credenziali (CWE-284).
