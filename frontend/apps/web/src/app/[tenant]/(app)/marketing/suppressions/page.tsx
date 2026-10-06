import { SuppressionsPage } from "@/features/marketing";

/** `/{tenant}/marketing/suppressions` (N01): addresses marketing never writes to. */
export default async function Page({ params }: PageProps<"/[tenant]/marketing/suppressions">) {
  const { tenant } = await params;
  return <SuppressionsPage tenant={tenant} />;
}
