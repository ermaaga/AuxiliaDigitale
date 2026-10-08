import { getTranslations } from "next-intl/server";

import { MarketingNav } from "@/features/marketing";
import { requireNavigation } from "@/lib/api/navigation-guard";

/** `/{tenant}/marketing/…` (N01, nav entry `marketing`): campaigns, segments, static lists, templates, suppressions. */
export default async function MarketingLayout({
  children,
  params,
}: LayoutProps<"/[tenant]/marketing">) {
  const { tenant } = await params;
  await requireNavigation("marketing");
  const t = await getTranslations();
  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-2xl font-semibold tracking-tight">{t("nav.marketing")}</h1>
      <MarketingNav tenant={tenant} />
      {children}
    </div>
  );
}
