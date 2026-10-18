import path from "node:path";

import { auxctlSecret } from "./support/auxctl";
import { runSql } from "./support/database";
import { E2E, loadE2eEnv } from "./support/env";

/**
 * Before every run: a new temporary password for the tenant Administrator and a new activation token for the System
 * user (auxctl), so the suite can run again on the same database. Values reach the tests through `process.env`. The
 * connected accounts of tenant-mailbox.spec.ts are seeded again, with today's events.
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
  runSql(`auxilia_t_${E2E.tenant}`, path.join(__dirname, "support", "mailbox-seed.sql"));
}
