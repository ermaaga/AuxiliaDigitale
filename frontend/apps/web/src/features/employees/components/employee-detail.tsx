"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { ArrowLeftIcon, Trash2Icon } from "lucide-react";
import { isApiError } from "@auxilia/api-client";
import { parseAsStringLiteral, useQueryState } from "nuqs";
import { useTranslations } from "next-intl";
import { Alert, AlertDescription } from "@auxilia/ui/components/alert";
import { Button } from "@auxilia/ui/components/button";
import { Skeleton } from "@auxilia/ui/components/skeleton";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@auxilia/ui/components/tabs";

import { useConfirm } from "@/components/confirm/confirm-provider";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { UserAvatar } from "@/components/user-avatar";
import { tenantHref } from "@/lib/href";
import { useNotify } from "@/lib/notify";
import { useCan } from "@/lib/permissions";

import { deleteEmployee, employeeName, useEmployee, useEmployeeMutation } from "../api";
import { EMPLOYEE_PERMISSIONS } from "../permissions";
import { EmployeeAccess } from "./employee-access";
import { EmployeeClients } from "./employee-clients";
import { EmployeeDataForm } from "./employee-data-form";
import { EmployeeOverview } from "./employee-overview";
import { EmployeeSpecializations } from "./employee-specializations";
import { EmployeeStatus } from "./employee-status";

const TABS = ["overview", "data", "clients", "specializations", "access"] as const;

/**
 * Employee detail (F06, legacy `Admin/EmployeeDetail`): header with picture, name, status and default badge; tabs for
 * the overview (workload, default, administrator Q32), personal data, clients in charge (assign/unassign), Employee
 * specializations and the account. Actions appear only with their permission (the API checks again).
 */
export function EmployeeDetail({ tenant, id }: { tenant: string; id: string }) {
  const t = useTranslations();
  const router = useRouter();
  const notify = useNotify();
  const confirm = useConfirm();
  const canManage = useCan(EMPLOYEE_PERMISSIONS.manage);
  const [tab, setTab] = useQueryState(
    "tab",
    parseAsStringLiteral(TABS).withDefault("overview").withOptions({ history: "replace" }),
  );
  const query = useEmployee(tenant, id);
  const remove = useEmployeeMutation(tenant, () => deleteEmployee(id));
  const back = (
    <Button variant="ghost" size="sm" className="self-start" asChild>
      <Link href={tenantHref(tenant, "/employees")}>
        <ArrowLeftIcon aria-hidden /> {t("app.employees.back")}
      </Link>
    </Button>
  );

  if (query.error) {
    return (
      <div className="flex flex-col gap-4">
        {back}
        {isApiError(query.error) && query.error.status === 404 ? (
          <Alert>
            <AlertDescription>{t("app.employees.notFound")}</AlertDescription>
          </Alert>
        ) : (
          <ApiErrorAlert error={query.error} onRetry={() => void query.refetch()} />
        )}
      </div>
    );
  }

  const employee = query.data;
  if (!employee) {
    return (
      <div className="flex flex-col gap-4" aria-busy="true">
        {back}
        <Skeleton className="h-10 w-72" />
        <Skeleton className="h-64 w-full" />
      </div>
    );
  }

  const name = employeeName(employee);
  const onDelete = async () => {
    const confirmed = await confirm({
      description: t("app.employees.deleteConfirm", { name }),
      confirmLabel: t("Delete"),
      variant: "destructive",
    });
    if (!confirmed) {
      return;
    }

    try {
      await remove.mutateAsync(undefined);
      notify.success("app.employees.deleted");
      router.push(tenantHref(tenant, "/employees"));
    } catch (error) {
      notify.error(error);
    }
  };

  return (
    <div className="flex flex-col gap-4">
      {back}
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex items-center gap-3">
          <UserAvatar
            userId={employee.id}
            name={name}
            imageVersion={employee.imageVersion}
            className="size-14 text-lg"
          />
          <div className="flex flex-col gap-2">
            <h1 className="text-2xl font-semibold tracking-tight">{name}</h1>
            <EmployeeStatus canSignIn={employee.canSignIn} isDefault={employee.isDefault} />
          </div>
        </div>
        {canManage && !employee.isDefault ? (
          <Button
            type="button"
            variant="outline"
            onClick={() => void onDelete()}
            disabled={remove.isPending}
          >
            <Trash2Icon aria-hidden /> {t("Delete")}
          </Button>
        ) : null}
      </header>

      <Tabs value={tab} onValueChange={(value) => void setTab(value as (typeof TABS)[number])}>
        <TabsList aria-label={name} className="h-auto flex-wrap">
          <TabsTrigger value="overview">{t("Overview")}</TabsTrigger>
          <TabsTrigger value="data">{t("app.clients.tabs.data")}</TabsTrigger>
          <TabsTrigger value="clients">{t("AssignedClients")}</TabsTrigger>
          <TabsTrigger value="specializations">{t("app.clients.tabs.specializations")}</TabsTrigger>
          <TabsTrigger value="access">{t("app.clients.tabs.access")}</TabsTrigger>
        </TabsList>
        <TabsContent value="overview" className="mt-4">
          <EmployeeOverview tenant={tenant} employee={employee} />
        </TabsContent>
        <TabsContent value="data" className="mt-4">
          <EmployeeDataForm tenant={tenant} employee={employee} />
        </TabsContent>
        <TabsContent value="clients" className="mt-4">
          <EmployeeClients tenant={tenant} employee={employee} />
        </TabsContent>
        <TabsContent value="specializations" className="mt-4">
          <EmployeeSpecializations tenant={tenant} employee={employee} />
        </TabsContent>
        <TabsContent value="access" className="mt-4">
          <EmployeeAccess tenant={tenant} employee={employee} />
        </TabsContent>
      </Tabs>
    </div>
  );
}
