import { CampaignDetail } from "@/features/marketing";

/** `/{tenant}/marketing/campaigns/{id}` (N01): a campaign, send now, results. */
export default async function Page({ params }: PageProps<"/[tenant]/marketing/campaigns/[id]">) {
  const { tenant, id } = await params;
  return <CampaignDetail tenant={tenant} id={decodeURIComponent(id)} />;
}
