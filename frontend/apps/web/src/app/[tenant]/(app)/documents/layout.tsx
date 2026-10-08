import { requireNavigation } from "@/lib/api/navigation-guard";

/** `/{tenant}/documents/…` opens only for the roles whose menu has `documents` (F22). */
export default async function DocumentsLayout({ children }: LayoutProps<"/[tenant]/documents">) {
  await requireNavigation("documents");
  return children;
}
