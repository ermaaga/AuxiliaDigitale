import { getTranslations } from "next-intl/server";

import { ClientsTable } from "@/features/clients/components/clients-table";

/**
 * `/{tenant}/clients` (nav entry `clients`, F05): one page for every staff role; the API decides what each one sees
 * (`directory.clients.view`, "my clients" = assigned to the calling employee).
 */
export default async function ClientsPage({ params }: PageProps<"/[tenant]/clients">) {
  const { tenant } = await params;
  const t = await getTranslations();
  const title = t("Clients");

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
      <ClientsTable tenant={tenant} title={title} />
    </div>
  );
}
