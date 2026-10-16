import { getTranslations } from "next-intl/server";

import { MailView } from "@/features/mailbox";

/** `/{tenant}/mail` (nav entry `mail`, N05 #100): the user's own connected mailboxes. */
export default async function MailPage({ params }: PageProps<"/[tenant]/mail">) {
  const { tenant } = await params;
  const t = await getTranslations();

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-2xl font-semibold tracking-tight">{t("nav.mail")}</h1>
      <MailView tenant={tenant} />
    </div>
  );
}
