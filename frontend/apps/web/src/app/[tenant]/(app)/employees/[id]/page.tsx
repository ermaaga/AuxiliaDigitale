import { EmployeeDetail } from "@/features/employees/components/employee-detail";

/** `/{tenant}/employees/{id}` (F06): the employee detail; what the caller may do is decided by the API. */
export default async function EmployeePage({ params }: PageProps<"/[tenant]/employees/[id]">) {
  const { tenant, id } = await params;
  return <EmployeeDetail tenant={tenant} id={decodeURIComponent(id)} />;
}
