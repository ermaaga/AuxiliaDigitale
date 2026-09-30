import { getTranslations } from "next-intl/server";

import { LoginAttemptsTable } from "@/features/identity/components/login-attempts-table";

/** `/{tenant}/login-audit` (nav entry `loginAudit`, F35): the API allows it only with `identity.loginAttempts.view`. */
export default async function LoginAuditPage({ params }: PageProps<"/[tenant]/login-audit">) {
  const { tenant } = await params;
  const t = await getTranslations();
  const title = t("LoginAuditLog");

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
      <LoginAttemptsTable tenant={tenant} title={title} />
    </div>
  );
}
