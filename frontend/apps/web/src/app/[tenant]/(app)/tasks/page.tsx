import { getTranslations } from "next-intl/server";

import { TasksTable } from "@/features/tasks";

/** `/{tenant}/tasks` (nav entry `tasks`, B-26): the staff's tasks; `?open=` (notification link) opens one. */
export default async function TasksPage({ params }: PageProps<"/[tenant]/tasks">) {
  const { tenant } = await params;
  const t = await getTranslations();

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-2xl font-semibold tracking-tight">{t("nav.tasks")}</h1>
      <TasksTable tenant={tenant} />
    </div>
  );
}
