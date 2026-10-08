import { requireNavigation } from "@/lib/api/navigation-guard";

/** `/{tenant}/cases/…` opens only for the roles whose menu has `cases` (F22). */
export default async function CasesLayout({ children }: LayoutProps<"/[tenant]/cases">) {
  await requireNavigation("cases");
  return children;
}
