import { redirect } from "next/navigation";

import { tenantHref } from "@/lib/href";

/** `/{tenant}` opens the dashboard (after sign-in every role lands there, F01). */
export default async function TenantHome({ params }: PageProps<"/[tenant]">) {
  const { tenant } = await params;
  redirect(tenantHref(tenant, "/dashboard"));
}
