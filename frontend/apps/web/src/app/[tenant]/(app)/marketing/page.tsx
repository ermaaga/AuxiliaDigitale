import { redirect } from "next/navigation";

import { tenantHref } from "@/lib/href";

/** `/{tenant}/marketing` (the nav entry) opens the campaigns. */
export default async function MarketingPage({ params }: PageProps<"/[tenant]/marketing">) {
  const { tenant } = await params;
  redirect(tenantHref(tenant, "/marketing/campaigns"));
}
