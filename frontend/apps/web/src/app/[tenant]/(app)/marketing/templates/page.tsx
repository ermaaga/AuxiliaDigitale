import { TemplatesPage } from "@/features/marketing";

/** `/{tenant}/marketing/templates` (N01): the e-mail templates. */
export default async function Page({ params }: PageProps<"/[tenant]/marketing/templates">) {
  const { tenant } = await params;
  return <TemplatesPage tenant={tenant} />;
}
