import { existsSync, readFileSync } from "node:fs";
import path from "node:path";

/** Repository root and the API/web addresses of the suite (override with E2E_API_URL / E2E_BASE_URL). */
export const REPO_ROOT = path.resolve(__dirname, "../../../../..");
export const API_URL = process.env.E2E_API_URL ?? "http://localhost:5299";
export const BASE_URL = process.env.E2E_BASE_URL ?? "http://localhost:3400";

/** The seeded tenant, its Administrator, the other tenant and the System user (e2e/prepare.sh). */
export const E2E = {
  tenant: "demo",
  otherTenant: "beta",
  userName: "mario.rossi",
  systemEmail: "ops@example.test",
  /** Client pages (B-04): an Administrator only and an Employee only. */
  administrator: { userName: "anna.bianchi", fullName: "Anna Bianchi" },
  employee: { userName: "paola.neri", fullName: "Paola Neri" },
  /** Profile page (B-05): an Employee whose password, picture, language and theme the spec changes. */
  profileUser: { userName: "laura.verdi", fullName: "Laura Verdi" },
  /** Document pages (B-13): a client seeded by prepare.sh, whose documents the spec uploads and deletes. */
  documentsClient: { id: "0199aaaa-0000-7000-8000-000000000041", fullName: "Elena Russo" },
  /** Case pages (B-14): a client and a service with the folder "Redditi", seeded by prepare.sh. */
  casesClient: { id: "0199aaaa-0000-7000-8000-000000000051", fullName: "Marco Ferri" },
  caseService: { name: "Dichiarazione E2E", folder: "Redditi" },
} as const;

/** Loads `e2e/.env.e2e` (written by prepare.sh) into `process.env` without overriding what is already set. */
export function loadE2eEnv(file = path.join(__dirname, "..", ".env.e2e")): void {
  if (!existsSync(file)) {
    return;
  }

  for (const line of readFileSync(file, "utf8").split(/\r?\n/)) {
    const match = /^([A-Z0-9_]+)=(.*)$/.exec(line);
    if (match && process.env[match[1]!] === undefined) {
      process.env[match[1]!] = match[2]!;
    }
  }
}

export function required(name: string): string {
  const value = process.env[name];
  if (!value) {
    throw new Error(`${name} is not set: run \`pnpm --filter web e2e:prepare\` first.`);
  }

  return value;
}
