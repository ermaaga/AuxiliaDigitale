import { requireNavigation } from "@/lib/api/navigation-guard";

/** `/{tenant}/sessions/…` opens only for the roles whose menu has `sessions` (F22). */
export default async function SessionsLayout({ children }: LayoutProps<"/[tenant]/sessions">) {
  await requireNavigation("sessions");
  return children;
}
