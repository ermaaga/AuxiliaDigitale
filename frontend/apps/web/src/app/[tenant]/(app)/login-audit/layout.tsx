import { requireNavigation } from "@/lib/api/navigation-guard";

/** `/{tenant}/login-audit/…` opens only for the roles whose menu has `loginAudit` (F22). */
export default async function LoginAuditLayout({ children }: LayoutProps<"/[tenant]/login-audit">) {
  await requireNavigation("loginAudit");
  return children;
}
