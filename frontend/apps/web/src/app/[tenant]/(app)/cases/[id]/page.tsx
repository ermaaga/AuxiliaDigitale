import { CaseDetail } from "@/features/cases/components/case-detail";

/** `/{tenant}/cases/{id}` (F09): a case with its stepper, status content, payments and timeline. */
export default async function CasePage({ params }: PageProps<"/[tenant]/cases/[id]">) {
  const { tenant, id } = await params;
  return <CaseDetail tenant={tenant} id={decodeURIComponent(id)} />;
}
