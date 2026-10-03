"use client";

import * as React from "react";
import { useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@auxilia/ui/components/card";
import { Checkbox } from "@auxilia/ui/components/checkbox";
import { Label } from "@auxilia/ui/components/label";
import { Skeleton } from "@auxilia/ui/components/skeleton";

import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { useNotify } from "@/lib/notify";
import { useCan } from "@/lib/permissions";

import {
  setEmployeeSpecializations,
  useEmployeeMutation,
  useEmployeeSpecializations,
  type EmployeeDetail,
} from "../api";
import { EMPLOYEE_PERMISSIONS } from "../permissions";

/** The specializations of an employee (F06: several, only active Employee specializations). */
export function EmployeeSpecializations({
  tenant,
  employee,
}: {
  tenant: string;
  employee: EmployeeDetail;
}) {
  const t = useTranslations();
  const notify = useNotify();
  const canEdit = useCan(EMPLOYEE_PERMISSIONS.manage);
  const available = useEmployeeSpecializations(tenant);
  const [chosen, setChosen] = React.useState<ReadonlySet<string>>(
    () => new Set(employee.specializations.map((item) => item.id)),
  );
  const save = useEmployeeMutation(tenant, (ids: readonly string[]) =>
    setEmployeeSpecializations(employee.id, ids),
  );

  const toggle = (id: string, on: boolean) => {
    const next = new Set(chosen);
    if (on) {
      next.add(id);
    } else {
      next.delete(id);
    }

    setChosen(next);
  };

  const onSave = async () => {
    try {
      await save.mutateAsync([...chosen]);
      notify.success("app.clients.saved");
    } catch (error) {
      notify.error(error);
    }
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2 className="text-base font-semibold">{t("app.clients.tabs.specializations")}</h2>
        </CardTitle>
        <CardDescription>{t("app.employees.specializationsHint")}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {available.error ? (
          <ApiErrorAlert error={available.error} onRetry={() => void available.refetch()} />
        ) : !available.data ? (
          <Skeleton className="h-24 w-full" />
        ) : available.data.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t("app.employees.noSpecializations")}</p>
        ) : (
          <fieldset className="grid gap-3 sm:grid-cols-2" disabled={!canEdit}>
            <legend className="sr-only">{t("app.clients.tabs.specializations")}</legend>
            {available.data.map((item) => (
              <div key={item.id} className="flex items-center gap-2">
                <Checkbox
                  id={`employee-spec-${item.id}`}
                  checked={chosen.has(item.id)}
                  onCheckedChange={(checked) => toggle(item.id, checked === true)}
                />
                <Label htmlFor={`employee-spec-${item.id}`}>{item.name}</Label>
              </div>
            ))}
          </fieldset>
        )}
        {canEdit && available.data && available.data.length > 0 ? (
          <Button
            type="button"
            className="self-end"
            onClick={() => void onSave()}
            disabled={save.isPending}
          >
            {t("Save")}
          </Button>
        ) : null}
      </CardContent>
    </Card>
  );
}
