import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { Button } from "@auxilia/ui/components/button";

import { ClientsTable } from "@/features/clients/components/clients-table";
import { tenantHref } from "@/lib/href";

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
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
        <Button variant="outline" size="sm" asChild>
          <Link href={tenantHref(tenant, "/clients/overview")}>
            {t("app.clients.overview.title")}
          </Link>
        </Button>
      </div>
      <ClientsTable tenant={tenant} title={title} />
    </div>
  );
}
