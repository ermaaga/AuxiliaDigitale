import { ServiceDetail } from "@/features/services";

/** `/{tenant}/services/{id}` (F08, F33): a service with its data, folder template and cases. */
export default async function ServicePage({ params }: PageProps<"/[tenant]/services/[id]">) {
  const { tenant, id } = await params;
  return <ServiceDetail tenant={tenant} id={decodeURIComponent(id)} />;
}
