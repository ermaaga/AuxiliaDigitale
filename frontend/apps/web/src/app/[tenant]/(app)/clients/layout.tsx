import { requireNavigation } from "@/lib/api/navigation-guard";

/** `/{tenant}/clients/…` opens only for the roles whose menu has `clients` (F22). */
export default async function ClientsLayout({ children }: LayoutProps<"/[tenant]/clients">) {
  await requireNavigation("clients");
  return children;
}
