import { getTranslations } from "next-intl/server";

import { AppointmentsView } from "@/features/appointments";

/**
 * `/{tenant}/appointments` (nav entry `appointments`, F13): one page for every role; the API decides what each one
 * sees (staff their calendar and the global one, clients their own).
 */
export default async function AppointmentsPage({ params }: PageProps<"/[tenant]/appointments">) {
  const { tenant } = await params;
  const t = await getTranslations();
  const title = t("nav.appointments");

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
      <AppointmentsView tenant={tenant} label={title} />
    </div>
  );
}
