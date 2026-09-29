// License gate for the pnpm workspace (auxilia-dependency-policy).
// Allowed SPDX ids: .github/license-allowlist.json; per-package exceptions: .github/license-exceptions.json.
import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const readJson = (file) => JSON.parse(readFileSync(path.join(repoRoot, file), "utf8"));

const allowed = new Set(readJson(".github/license-allowlist.json"));
const exceptions = Object.entries(readJson(".github/license-exceptions.json")).filter(
  ([key]) => !key.startsWith("_"),
);

const matchesPattern = (name, pattern) =>
  pattern.endsWith("*") ? name.startsWith(pattern.slice(0, -1)) : name === pattern;

const isException = (license, name) =>
  exceptions.some(
    ([lic, patterns]) =>
      lic === license && patterns.some((pattern) => matchesPattern(name, pattern)),
  );

// Accepts simple SPDX expressions: "A OR B" (any allowed) and "A AND B" (all allowed).
const isAllowedExpression = (expression, name) => {
  const clean = expression.replace(/[()]/g, "").trim();
  if (clean.includes(" OR "))
    return clean.split(" OR ").some((id) => isAllowedExpression(id, name));
  if (clean.includes(" AND "))
    return clean.split(" AND ").every((id) => isAllowedExpression(id, name));
  return allowed.has(clean) || isException(clean, name);
};

const output = execFileSync("pnpm", ["licenses", "list", "--json"], {
  cwd: path.join(repoRoot, "frontend"),
  encoding: "utf8",
  maxBuffer: 64 * 1024 * 1024,
});
const byLicense = JSON.parse(output);

const violations = [];
let count = 0;
for (const [license, packages] of Object.entries(byLicense)) {
  for (const pkg of packages) {
    count++;
    if (!isAllowedExpression(license, pkg.name))
      violations.push(`${pkg.name}@${pkg.versions?.join(",")}: ${license}`);
  }
}

if (violations.length > 0) {
  console.error(`License check failed (${violations.length} of ${count} packages):`);
  for (const violation of violations) console.error(`  - ${violation}`);
  process.exit(1);
}
console.log(`License check passed: ${count} packages.`);
