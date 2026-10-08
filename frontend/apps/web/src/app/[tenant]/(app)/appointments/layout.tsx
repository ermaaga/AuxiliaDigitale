import { requireNavigation } from "@/lib/api/navigation-guard";

/** `/{tenant}/appointments/…` opens only for the roles whose menu has `appointments` (F22). */
export default async function AppointmentsLayout({
  children,
}: LayoutProps<"/[tenant]/appointments">) {
  await requireNavigation("appointments");
  return children;
}
