"use client";

import * as React from "react";
import { useFormatter, useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@auxilia/ui/components/card";
import { Label } from "@auxilia/ui/components/label";

import { Combobox } from "@/components/combobox";
import { useConfirm } from "@/components/confirm/confirm-provider";
import { useNotify } from "@/lib/notify";
import { useCan } from "@/lib/permissions";

import {
  employeeName,
  makeDefaultEmployee,
  setEmployeeAdministrator,
  useAdministrators,
  useEmployeeMutation,
  type EmployeeDetail,
} from "../api";
import { EMPLOYEE_PERMISSIONS } from "../permissions";

/**
 * Overview of an employee (F06): workload (clients in charge; open cases and appointments of the week join with B-08
 * and B-16), creation date, default employee (Q31: exactly one, only who can sign in) and the administrator the
 * employee reports to (Q32).
 */
export function EmployeeOverview({
  tenant,
  employee,
}: {
  tenant: string;
  employee: EmployeeDetail;
}) {
  const t = useTranslations();
  const format = useFormatter();
  const notify = useNotify();
  const confirm = useConfirm();
  const canManage = useCan(EMPLOYEE_PERMISSIONS.manage);
  const administrators = useAdministrators(tenant, canManage);
  const [administrator, setAdministrator] = React.useState<string | undefined>(
    employee.administrator?.userId,
  );
  const makeDefault = useEmployeeMutation(tenant, () => makeDefaultEmployee(employee.id));
  const saveAdministrator = useEmployeeMutation(tenant, (userId: string | null) =>
    setEmployeeAdministrator(employee.id, userId),
  );

  const onDefault = async () => {
    const confirmed = await confirm({
      description: t("app.employees.makeDefaultConfirm", { name: employeeName(employee) }),
      confirmLabel: t("SetDefaultEmployeeTitle"),
    });
    if (!confirmed) {
      return;
    }

    try {
      await makeDefault.mutateAsync(undefined);
      notify.success("app.employees.defaultSet");
    } catch (error) {
      notify.error(error);
    }
  };

  const onAdministrator = async () => {
    try {
      await saveAdministrator.mutateAsync(administrator ?? null);
      notify.success("app.clients.saved");
    } catch (error) {
      notify.error(error);
    }
  };

  // The current administrator stays a choice even when it can no longer sign in.
  const options = [
    ...(employee.administrator &&
    !(administrators.data ?? []).some((item) => item.userId === employee.administrator?.userId)
      ? [employee.administrator]
      : []),
    ...(administrators.data ?? []),
  ].map((item) => ({ value: item.userId, label: item.fullName }));

  return (
    <div className="grid gap-4 md:grid-cols-2">
      <Card>
        <CardHeader>
          <CardTitle>
            <h2 className="text-base font-semibold">{t("app.employees.workload")}</h2>
          </CardTitle>
        </CardHeader>
        <CardContent>
          <dl className="grid gap-3 text-sm sm:grid-cols-2">
            <div>
              <dt className="text-muted-foreground">{t("AssignedClients")}</dt>
              <dd className="text-2xl font-semibold">
                {Number(employee.workload.assignedClients)}
              </dd>
            </div>
            <div>
              <dt className="text-muted-foreground">{t("CreatedAt")}</dt>
              <dd className="font-medium">
                {format.dateTime(new Date(employee.createdAt), { dateStyle: "medium" })}
              </dd>
            </div>
            <div className="sm:col-span-2">
              <dt className="text-muted-foreground">{t("app.employees.specializations")}</dt>
              <dd>
                {employee.specializations.length > 0
                  ? employee.specializations.map((item) => item.name).join(", ")
                  : "—"}
              </dd>
            </div>
          </dl>
        </CardContent>
      </Card>
      <Card>
        <CardHeader>
          <CardTitle>
            <h2 className="text-base font-semibold">{t("DefaultEmployee")}</h2>
          </CardTitle>
          <CardDescription>{t("app.employees.defaultHint")}</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-3">
          <p className="text-sm">
            {employee.isDefault ? t("app.employees.isDefault") : t("app.employees.isNotDefault")}
          </p>
          {canManage && !employee.isDefault ? (
            <Button
              type="button"
              variant="outline"
              className="self-start"
              onClick={() => void onDefault()}
              disabled={makeDefault.isPending || !employee.canSignIn}
            >
              {t("SetDefaultEmployeeTitle")}
            </Button>
          ) : null}
        </CardContent>
      </Card>
      <Card className="md:col-span-2">
        <CardHeader>
          <CardTitle>
            <h2 className="text-base font-semibold">{t("app.employees.administrator")}</h2>
          </CardTitle>
          <CardDescription>{t("app.employees.administratorHint")}</CardDescription>
        </CardHeader>
        <CardContent>
          {canManage ? (
            <div className="flex flex-wrap items-end gap-2">
              <div className="flex min-w-64 flex-col gap-2">
                <Label htmlFor="employee-administrator">{t("Administrator")}</Label>
                <Combobox
                  id="employee-administrator"
                  value={administrator}
                  onChange={setAdministrator}
                  loading={administrators.isPending}
                  placeholder={t("app.employees.noAdministrator")}
                  options={options}
                />
              </div>
              <Button
                type="button"
                onClick={() => void onAdministrator()}
                disabled={
                  saveAdministrator.isPending ||
                  (administrator ?? null) === (employee.administrator?.userId ?? null)
                }
              >
                {t("Save")}
              </Button>
            </div>
          ) : (
            <p className="text-sm">
              {employee.administrator?.fullName ?? t("app.employees.noAdministrator")}
            </p>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
