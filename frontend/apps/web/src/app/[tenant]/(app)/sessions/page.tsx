import { getTranslations } from "next-intl/server";

import { SessionsPage } from "@/features/sessions";

/** `/{tenant}/sessions` (nav entry `sessions`, F17): the open sessions of the tenant (Administrators). */
export default async function Sessions({ params }: PageProps<"/[tenant]/sessions">) {
  const { tenant } = await params;
  const t = await getTranslations();
  const title = t("ActiveSessions");

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
      <SessionsPage tenant={tenant} label={title} />
    </div>
  );
}
