import { getTranslations } from "next-intl/server";

import { NewClientWizard } from "@/features/clients/components/new-client-wizard";

/** `/{tenant}/clients/new` (F05): the new client wizard; the API allows it with `directory.clients.manage`. */
export default async function NewClientPage({ params }: PageProps<"/[tenant]/clients/new">) {
  const { tenant } = await params;
  const t = await getTranslations();

  return (
    <div className="flex max-w-3xl flex-col gap-4">
      <h1 className="text-2xl font-semibold tracking-tight">{t("AddClient")}</h1>
      <NewClientWizard tenant={tenant} />
    </div>
  );
}
