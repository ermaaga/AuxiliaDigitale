import { SegmentBuilder } from "@/features/marketing";

/** `/{tenant}/marketing/segments/{id}` (N01): a segment in the builder. */
export default async function Page({ params }: PageProps<"/[tenant]/marketing/segments/[id]">) {
  const { tenant, id } = await params;
  return <SegmentBuilder tenant={tenant} id={decodeURIComponent(id)} />;
}
