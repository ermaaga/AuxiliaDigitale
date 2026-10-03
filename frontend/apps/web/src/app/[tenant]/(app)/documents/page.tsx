import { getTranslations } from "next-intl/server";

import { DocumentsPanel } from "@/features/documents";

/**
 * `/{tenant}/documents` (nav entry `documents`, F14): one page for every staff role; the API decides what each one
 * sees (`documents.files.view`, F10).
 */
export default async function DocumentsPage({ params }: PageProps<"/[tenant]/documents">) {
  const { tenant } = await params;
  const t = await getTranslations();
  const title = t("app.documents.title");

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
      <DocumentsPanel tenant={tenant} label={title} />
    </div>
  );
}
