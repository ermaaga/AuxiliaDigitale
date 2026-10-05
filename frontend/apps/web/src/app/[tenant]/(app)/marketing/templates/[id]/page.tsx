import { TemplateEditor } from "@/features/marketing";
import { tenantLanguages } from "@/i18n/bundles";

/** `/{tenant}/marketing/templates/{id}` (N01): an e-mail template with its preview and test send. */
export default async function Page({ params }: PageProps<"/[tenant]/marketing/templates/[id]">) {
  const { tenant, id } = await params;
  return (
    <TemplateEditor
      tenant={tenant}
      id={decodeURIComponent(id)}
      languages={(await tenantLanguages(tenant)) ?? []}
    />
  );
}
