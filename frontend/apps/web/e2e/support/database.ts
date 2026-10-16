import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";
import path from "node:path";

import { REPO_ROOT } from "./env";

/**
 * Runs a SQL file on a tenant database through psql in the Postgres container of deploy/compose.dev.yml (the one
 * prepare.sh seeds), stopping at the first error.
 */
export function runSql(database: string, file: string): void {
  execFileSync(
    "docker",
    [
      "compose",
      "-f",
      path.join(REPO_ROOT, "deploy/compose.dev.yml"),
      "exec",
      "-T",
      "postgres",
      "psql",
      "-v",
      "ON_ERROR_STOP=1",
      "-q",
      "-U",
      process.env.AUXILIA_PG_USER ?? "auxilia",
      "-d",
      database,
    ],
    { input: readFileSync(file), stdio: ["pipe", "ignore", "inherit"] },
  );
}
