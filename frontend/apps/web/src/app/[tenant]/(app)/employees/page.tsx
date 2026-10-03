import { getTranslations } from "next-intl/server";

import { EmployeesTable } from "@/features/employees/components/employees-table";

/** `/{tenant}/employees` (nav entry `employees`, F06): the employee list; the API allows it with `directory.employees.view`. */
export default async function EmployeesPage({ params }: PageProps<"/[tenant]/employees">) {
  const { tenant } = await params;
  const t = await getTranslations();
  const title = t("Employees");

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
      <EmployeesTable tenant={tenant} title={title} />
    </div>
  );
}
