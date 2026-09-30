import { auxctlSecret } from "./support/auxctl";
import { E2E, loadE2eEnv } from "./support/env";

/**
 * Before every run: a new temporary password for the tenant Administrator and a new activation token for the System
 * user (auxctl), so the suite can run again on the same database. Values reach the tests through `process.env`.
 */
export default function globalSetup() {
  loadE2eEnv();
  process.env.E2E_TEMP_PASSWORD = auxctlSecret(
    "users",
    "reset-password",
    "--tenant",
    E2E.tenant,
    "--user",
    E2E.userName,
  );
  process.env.E2E_ACTIVATION_TOKEN = auxctlSecret(
    "platform",
    "users",
    "reset",
    "--email",
    E2E.systemEmail,
  );
}
