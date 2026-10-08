import { execFileSync } from "node:child_process";
import path from "node:path";

import { REPO_ROOT, required } from "./env";

/**
 * Runs auxctl (the built MigrationRunner, Release) against the E2E Catalog and returns what it printed after
 * "(shown only now…): " — a temporary password or an activation token.
 */
export function auxctlSecret(...args: string[]): string {
  const output = auxctl(...args);
  const secret = /shown only now[^)]*\): (\S+)/.exec(output)?.[1];
  if (!secret) {
    throw new Error(`auxctl ${args.join(" ")} printed no secret:\n${output.slice(-2000)}`);
  }

  return secret;
}

/** Runs auxctl and returns what it printed (fails when the command fails). */
export function auxctl(...args: string[]): string {
  return execFileSync(
    "dotnet",
    [
      "run",
      "--project",
      path.join(REPO_ROOT, "backend/src/Auxilia.MigrationRunner"),
      "-c",
      "Release",
      "--no-build",
      "--",
      ...args,
    ],
    {
      encoding: "utf8",
      env: {
        ...process.env,
        ConnectionStrings__Catalog: required("E2E_CATALOG_CONNECTION"),
        AuxiliaLogging__Storage: "none",
      },
    },
  );
}
