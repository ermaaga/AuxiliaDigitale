import { ListDetail } from "@/features/marketing";

/** `/{tenant}/marketing/lists/{id}` (N01): the members of a static list. */
export default async function Page({ params }: PageProps<"/[tenant]/marketing/lists/[id]">) {
  const { tenant, id } = await params;
  return <ListDetail tenant={tenant} id={decodeURIComponent(id)} />;
}
