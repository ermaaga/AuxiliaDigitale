import { getTranslations } from "next-intl/server";

import { NewEmployeeForm } from "@/features/employees/components/new-employee-form";

/** `/{tenant}/employees/new` (F06): the new employee form; the API allows it with `directory.employees.manage`. */
export default async function NewEmployeePage({ params }: PageProps<"/[tenant]/employees/new">) {
  const { tenant } = await params;
  const t = await getTranslations();

  return (
    <div className="flex max-w-3xl flex-col gap-4">
      <h1 className="text-2xl font-semibold tracking-tight">{t("CreateNewEmployee")}</h1>
      <NewEmployeeForm tenant={tenant} />
    </div>
  );
}
