import { requireNavigation } from "@/lib/api/navigation-guard";

/** `/{tenant}/employees/…` opens only for the roles whose menu has `employees` (F22). */
export default async function EmployeesLayout({ children }: LayoutProps<"/[tenant]/employees">) {
  await requireNavigation("employees");
  return children;
}
