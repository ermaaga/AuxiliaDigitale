import { getTranslations } from "next-intl/server";

import { CasesTable } from "@/features/cases";

/**
 * `/{tenant}/cases` (nav entry `cases`, F09): one page for every role; the API decides what each one sees
 * (`cases.cases.view`, F10).
 */
export default async function CasesPage({ params }: PageProps<"/[tenant]/cases">) {
  const { tenant } = await params;
  const t = await getTranslations();
  const title = t("nav.cases");

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
      <CasesTable tenant={tenant} label={title} />
    </div>
  );
}
