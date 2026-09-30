// Static fallback bundles (skill auxilia-localization): messages/{en,it}.json generated from the shipped translations of
// the backend data-migrations, used when the API cannot serve the tenant's bundle (the login page must always render)
// and in the platform console (no tenant). Run `pnpm --filter web i18n:sync` after adding keys; a test fails if stale.
import { readFile, readdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";

const seedDirectory = new URL(
  "../../../../backend/src/Auxilia.Persistence.Tenant/DataMigrations/Localization/",
  import.meta.url,
);
const outputDirectory = new URL("../messages/", import.meta.url);

export async function buildBundles() {
  const files = (await readdir(seedDirectory)).filter((name) => name.endsWith(".json")).sort();
  const bundles = { en: {}, it: {} };
  for (const file of files) {
    const entries = JSON.parse(await readFile(new URL(file, seedDirectory), "utf8"));
    for (const entry of entries) {
      bundles.en[entry.key] = entry.en;
      bundles.it[entry.key] = entry.it;
    }
  }

  for (const language of Object.keys(bundles)) {
    bundles[language] = Object.fromEntries(
      Object.entries(bundles[language]).sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0)),
    );
  }

  return bundles;
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const bundles = await buildBundles();
  for (const [language, messages] of Object.entries(bundles)) {
    await writeFile(
      new URL(`${language}.json`, outputDirectory),
      JSON.stringify(messages, null, 2) + "\n",
    );
  }

  console.log(`messages written: ${Object.keys(bundles.en).length} keys`);
}
