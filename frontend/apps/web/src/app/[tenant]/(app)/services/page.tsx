import { getTranslations } from "next-intl/server";

import { ServicesTable } from "@/features/services";

/** `/{tenant}/services` (nav entry `services`, F08): the service catalog with its categories (`cases.services.view`). */
export default async function ServicesPage({ params }: PageProps<"/[tenant]/services">) {
  const { tenant } = await params;
  const t = await getTranslations();
  const title = t("nav.services");

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
      <ServicesTable tenant={tenant} label={title} />
    </div>
  );
}
