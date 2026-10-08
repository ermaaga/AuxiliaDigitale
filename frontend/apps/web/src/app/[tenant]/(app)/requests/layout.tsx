import { requireNavigation } from "@/lib/api/navigation-guard";

/** `/{tenant}/requests/…` opens only for the roles whose menu has `requests` (F22). */
export default async function RequestsLayout({ children }: LayoutProps<"/[tenant]/requests">) {
  await requireNavigation("requests");
  return children;
}
