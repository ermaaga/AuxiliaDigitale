import { getTranslations } from "next-intl/server";

import { ExportsPage } from "@/features/exports";

/** `/{tenant}/exports` (F26): the user's queued exports and their files (link of the `export.ready` notification). */
export default async function Exports({ params }: PageProps<"/[tenant]/exports">) {
  const { tenant } = await params;
  const t = await getTranslations();

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-2xl font-semibold tracking-tight">{t("app.exports.title")}</h1>
      <ExportsPage tenant={tenant} />
    </div>
  );
}
