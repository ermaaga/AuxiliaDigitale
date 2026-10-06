import { CampaignWizard } from "@/features/marketing";

/** `/{tenant}/marketing/campaigns/new` (N01): the wizard of a new campaign. */
export default async function Page({ params }: PageProps<"/[tenant]/marketing/campaigns/new">) {
  const { tenant } = await params;
  return <CampaignWizard tenant={tenant} />;
}
