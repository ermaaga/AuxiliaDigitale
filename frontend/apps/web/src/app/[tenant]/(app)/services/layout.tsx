import { requireNavigation } from "@/lib/api/navigation-guard";

/** `/{tenant}/services/…` opens only for the roles whose menu has `services` (F22). */
export default async function ServicesLayout({ children }: LayoutProps<"/[tenant]/services">) {
  await requireNavigation("services");
  return children;
}
