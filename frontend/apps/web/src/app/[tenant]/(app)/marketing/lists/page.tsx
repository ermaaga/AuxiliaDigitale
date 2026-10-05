import { ListsPage } from "@/features/marketing";

/** `/{tenant}/marketing/lists` (N01): the static lists. */
export default async function Page({ params }: PageProps<"/[tenant]/marketing/lists">) {
  const { tenant } = await params;
  return <ListsPage tenant={tenant} />;
}
