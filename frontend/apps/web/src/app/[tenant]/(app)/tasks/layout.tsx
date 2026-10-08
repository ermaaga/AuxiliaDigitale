import { requireNavigation } from "@/lib/api/navigation-guard";

/** `/{tenant}/tasks/…` opens only for the roles whose menu has `tasks` (F22). */
export default async function TasksLayout({ children }: LayoutProps<"/[tenant]/tasks">) {
  await requireNavigation("tasks");
  return children;
}
