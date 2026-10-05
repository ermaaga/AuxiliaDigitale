import { SegmentsPage } from "@/features/marketing";

/** `/{tenant}/marketing/segments` (N01): the dynamic segments. */
export default async function Page({ params }: PageProps<"/[tenant]/marketing/segments">) {
  const { tenant } = await params;
  return <SegmentsPage tenant={tenant} />;
}
