import { SegmentBuilder } from "@/features/marketing";

/** `/{tenant}/marketing/segments/new` (N01): the segment builder with the live count. */
export default async function Page({ params }: PageProps<"/[tenant]/marketing/segments/new">) {
  const { tenant } = await params;
  return <SegmentBuilder tenant={tenant} />;
}
