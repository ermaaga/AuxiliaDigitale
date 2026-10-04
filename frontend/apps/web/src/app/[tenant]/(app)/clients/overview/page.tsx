import { getTranslations } from "next-intl/server";

import { ClientsOverview } from "@/features/clients/components/clients-overview";

/** `/{tenant}/clients/overview` (F07): the clients overview with filters, selection and export (PDF of the legacy). */
export default async function ClientsOverviewPage({
  params,
}: PageProps<"/[tenant]/clients/overview">) {
  const { tenant } = await params;
  const t = await getTranslations();
  const title = t("app.clients.overview.title");

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
      <ClientsOverview tenant={tenant} title={title} />
    </div>
  );
}
