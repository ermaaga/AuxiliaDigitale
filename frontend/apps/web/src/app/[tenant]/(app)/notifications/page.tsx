import { getTranslations } from "next-intl/server";

import { NotificationsPage } from "@/features/notifications";

/** `/{tenant}/notifications` (F16): the user's notifications and preferences (every role). */
export default async function Notifications({ params }: PageProps<"/[tenant]/notifications">) {
  const { tenant } = await params;
  const t = await getTranslations();

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-2xl font-semibold tracking-tight">{t("Notifications")}</h1>
      <NotificationsPage tenant={tenant} />
    </div>
  );
}
