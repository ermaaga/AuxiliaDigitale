import { getTranslations } from "next-intl/server";

import { RequestsInbox } from "@/features/requests";

/** `/{tenant}/requests` (nav entry `requests`, F15): the inbox of every role; the API decides the boxes. */
export default async function RequestsPage({ params }: PageProps<"/[tenant]/requests">) {
  const { tenant } = await params;
  const t = await getTranslations();
  const title = t("nav.requests");

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
      <RequestsInbox tenant={tenant} label={title} />
    </div>
  );
}
