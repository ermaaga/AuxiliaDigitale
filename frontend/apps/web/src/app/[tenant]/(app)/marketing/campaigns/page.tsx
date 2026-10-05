import { CampaignsPage } from "@/features/marketing";

/** `/{tenant}/marketing/campaigns` (N01): the campaigns with their results. */
export default async function Page({ params }: PageProps<"/[tenant]/marketing/campaigns">) {
  const { tenant } = await params;
  return <CampaignsPage tenant={tenant} />;
}
