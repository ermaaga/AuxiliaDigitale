"use client";

import * as React from "react";
import { useFormatter, useTranslations } from "next-intl";
import { Badge } from "@auxilia/ui/components/badge";
import { Button } from "@auxilia/ui/components/button";
import { Card, CardContent, CardHeader, CardTitle } from "@auxilia/ui/components/card";
import { Label } from "@auxilia/ui/components/label";

import { Combobox } from "@/components/combobox";
import { useConfirm } from "@/components/confirm/confirm-provider";
import { DataTable } from "@/components/data-table/data-table";
import type { DataTableColumn } from "@/components/data-table/table-model";
import { useNotify } from "@/lib/notify";
import { useCan } from "@/lib/permissions";

import {
  assignClientEmployee,
  unassignClientEmployee,
  useAssignableEmployees,
  useClientMutation,
  type ClientDetail,
} from "../api";
import { DIRECTORY_PERMISSIONS } from "../permissions";

type Assignment = ClientDetail["assignments"][number];

/**
 * The employee in charge of a client and every past assignment (F05, legacy admin detail): hand the client to another
 * employee (the previous assignment ends, history kept) or to nobody, with `directory.clients.assign`.
 */
export function ClientAssignment({ tenant, client }: { tenant: string; client: ClientDetail }) {
  const t = useTranslations();
  const format = useFormatter();
  const notify = useNotify();
  const confirm = useConfirm();
  const canAssign = useCan(DIRECTORY_PERMISSIONS.assignClients);
  const employees = useAssignableEmployees(tenant, canAssign);
  const [chosen, setChosen] = React.useState<string | undefined>();
  const assign = useClientMutation(tenant, (employeeUserId: string) =>
    assignClientEmployee(client.id, employeeUserId),
  );
  const unassign = useClientMutation(tenant, () => unassignClientEmployee(client.id));

  const onAssign = async () => {
    if (!chosen) {
      return;
    }

    try {
      await assign.mutateAsync(chosen);
      setChosen(undefined);
      notify.success("app.clients.assigned");
    } catch (error) {
      notify.error(error);
    }
  };

  const onUnassign = async () => {
    const confirmed = await confirm({
      description: t("app.clients.unassignConfirm", { name: client.employee?.fullName ?? "" }),
      confirmLabel: t("app.clients.unassign"),
      variant: "destructive",
    });
    if (!confirmed) {
      return;
    }

    try {
      await unassign.mutateAsync(undefined);
      notify.success("app.clients.unassigned");
    } catch (error) {
      notify.error(error);
    }
  };

  const date = (value: string) =>
    format.dateTime(new Date(value), { dateStyle: "medium", timeStyle: "short" });
  const columns: DataTableColumn<Assignment>[] = [
    {
      id: "employee",
      header: t("Employee"),
      mobile: "title",
      cell: (row) => (
        <span className="flex items-center gap-2">
          {row.employeeName ?? "—"}
          {row.endedAt ? null : <Badge variant="secondary">{t("app.clients.current")}</Badge>}
        </span>
      ),
    },
    { id: "assignedAt", header: t("From"), cell: (row) => date(row.assignedAt) },
    {
      id: "endedAt",
      header: t("app.clients.until"),
      cell: (row) => (row.endedAt ? date(row.endedAt) : "—"),
    },
  ];

  return (
    <div className="flex flex-col gap-4">
      <Card>
        <CardHeader>
          <CardTitle>
            <h2 className="text-base font-semibold">{t("app.clients.employee")}</h2>
          </CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <p className="text-sm">{client.employee?.fullName ?? t("app.clients.noEmployee")}</p>
          {canAssign ? (
            <div className="flex flex-wrap items-end gap-2">
              <div className="flex min-w-64 flex-col gap-2">
                <Label htmlFor="client-assign-employee">{t("app.clients.assignTo")}</Label>
                <Combobox
                  id="client-assign-employee"
                  value={chosen}
                  onChange={setChosen}
                  loading={employees.isPending}
                  placeholder={t("app.clients.chooseEmployee")}
                  options={(employees.data ?? [])
                    .filter((item) => item.userId !== client.employee?.userId)
                    .map((item) => ({ value: item.userId, label: item.fullName }))}
                />
              </div>
              <Button
                type="button"
                onClick={() => void onAssign()}
                disabled={!chosen || assign.isPending}
              >
                {t("app.clients.assign")}
              </Button>
              {client.employee ? (
                <Button
                  type="button"
                  variant="outline"
                  onClick={() => void onUnassign()}
                  disabled={unassign.isPending}
                >
                  {t("app.clients.unassign")}
                </Button>
              ) : null}
            </div>
          ) : null}
        </CardContent>
      </Card>
      <DataTable
        label={t("app.clients.history")}
        columns={columns}
        rows={client.assignments}
        getRowId={(row) => `${row.employeeUserId}:${row.assignedAt}`}
        totalCount={client.assignments.length}
        page={1}
        pageSize={Math.max(client.assignments.length, 1)}
        sort={null}
        onPageChange={() => undefined}
        onPageSizeChange={() => undefined}
        onSortChange={() => undefined}
        hidePaging
      />
    </div>
  );
}
