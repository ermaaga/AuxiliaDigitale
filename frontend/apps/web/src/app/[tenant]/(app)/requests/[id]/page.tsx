import { RequestThread } from "@/features/requests";

/** `/{tenant}/requests/{id}` (F15): the thread of a request with reply and close. */
export default async function RequestPage({ params }: PageProps<"/[tenant]/requests/[id]">) {
  const { tenant, id } = await params;
  return <RequestThread tenant={tenant} id={decodeURIComponent(id)} />;
}
