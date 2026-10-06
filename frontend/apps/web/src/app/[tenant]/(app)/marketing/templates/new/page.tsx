import { TemplateEditor } from "@/features/marketing";
import { tenantLanguages } from "@/i18n/bundles";

/** `/{tenant}/marketing/templates/new` (N01): a new e-mail template. */
export default async function Page({ params }: PageProps<"/[tenant]/marketing/templates/new">) {
  const { tenant } = await params;
  return <TemplateEditor tenant={tenant} languages={(await tenantLanguages(tenant)) ?? []} />;
}
