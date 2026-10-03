import { ClientDetail } from "@/features/clients/components/client-detail";

/** `/{tenant}/clients/{id}` (F05): the client 360° view; what the caller may see and do is decided by the API. */
export default async function ClientPage({ params }: PageProps<"/[tenant]/clients/[id]">) {
  const { tenant, id } = await params;
  return <ClientDetail tenant={tenant} id={decodeURIComponent(id)} />;
}
