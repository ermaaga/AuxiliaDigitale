# ADR 0023 — Data Protection keys protected at rest by an Azure Key Vault key

- Status: Accepted (2026-10-18, packages approved by the user; issue #141) · Related: R4 (`docs/security/asvs-checklist.md`),
  #139 (VULN-01), H-03, ADR 0002, ADR 0011, ADR 0022

## Context
ASP.NET Data Protection encrypts every secret the application stores: tenant connection strings, JWT signing keys,
secret settings (SMTP passwords), TOTP secrets of platform and tenant users, OAuth tokens and IMAP passwords of the
connected accounts. Its key ring is persisted in `catalog.data_protection_keys` (ADR 0002) **without protection at
rest**: a copy of the Catalog alone (backup, read-only SQL access, debug dump) decrypts every secret of every tenant.
The fix depended on the hosting, decided in ADR 0022 (Azure Container Apps, one Key Vault per environment).

## Decision
1. **Key encryption key in Key Vault.** Each environment vault (`kv-auxilia-next-qa`, `kv-auxilia-next-prod`) holds an
   RSA 3072 key `dataprotection-kek` (operations `wrapKey`/`unwrapKey` only), created by `key-vault.bicep` inside the
   vault and never exported. Api, Worker and the auxctl job (runtime identity `id-auxilia-next-<env>`) have
   **Key Vault Crypto Service Encryption User** on that key only. The keys of qa and prod stay separate (runbook C6).
2. **Packages** (dependency policy, approved by the user): `Azure.Extensions.AspNetCore.DataProtection.Keys` 1.6.4 and
   `Azure.Identity` 1.21.0, Microsoft, MIT; transitive `Azure.Core`, `Azure.Security.KeyVault.Keys`,
   `Microsoft.Identity.Client` (+ `.Extensions.Msal`), `System.ClientModel`, `System.Memory.Data`,
   `System.Security.Cryptography.ProtectedData`, all MIT, no known advisory. Referenced by `Auxilia.Persistence.Catalog`
   only.
3. **Configuration.** `CatalogPersistence.AddCatalogPersistence(connectionString, DataProtectionKeyVault?)` calls
   `ProtectKeysWithAzureKeyVault` with a `ManagedIdentityCredential` (user-assigned, `AZURE_CLIENT_ID`; no developer or
   environment credentials). The hosts build the value with `DataProtectionKeyVault.From(DataProtection:KeyVaultKeyId,
   AZURE_CLIENT_ID, isDevelopment)`; the Bicep sets both variables on every backend container (`apps.bicep`).
4. **Never in Development.** In Development the key is ignored even when configured: local runs, `compose.local.yml`
   and the E2E suite keep the keys of their local database unprotected and need no Azure access. Outside Development
   the key is used when configured; local and test hosts that run with the default environment (auxctl in tests, the
   Worker test bus) configure none, so nothing changes for them. A value that is not an https URI fails the start.
5. **Versionless key id.** The vault wraps with the newest version of the key; each wrapped key records the version
   that wrapped it, so a manual rotation of `dataprotection-kek` keeps the older keys readable (old versions must stay
   enabled).

## Consequences
- A copy of the Catalog without access to the vault decrypts nothing; access to the vault is audited and revocable.
- Every new Data Protection key (every 90 days) costs one wrap operation; reading the ring at start costs one unwrap per
  key. Key Vault standard operations are billed per 10 000: negligible.
- **The key is the single point of failure**: if `dataprotection-kek` (or an older version still used) is deleted,
  every protected secret is lost. The vault has soft delete (90 days) and purge protection (ADR 0022); never disable
  key versions that wrapped keys still in the ring.
- The protection applies to keys created after it is enabled. On the first deploy of qa/prod the Catalog has no keys
  yet; an existing Catalog with plain keys needs a new ring (new key, re-encrypted secrets, old keys revoked) — not
  automated, to be planned if it ever happens.
- No fail-fast when the key is missing outside Development (local and test hosts run in Production by default); the
  Bicep always sets it, and the runbook checks that the stored keys are encrypted after the first start.
