"use client";

import * as React from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { Badge } from "@auxilia/ui/components/badge";
import { Button } from "@auxilia/ui/components/button";
import { Card, CardContent, CardHeader, CardTitle } from "@auxilia/ui/components/card";
import { Label } from "@auxilia/ui/components/label";

import { Combobox, type ComboboxOption } from "@/components/combobox";
import { useConfirm } from "@/components/confirm/confirm-provider";
import { DataTable } from "@/components/data-table/data-table";
import type { DataTableColumn } from "@/components/data-table/table-model";
import { UserAvatar } from "@/components/user-avatar";
import { clientName } from "@/features/clients";
import { tenantHref } from "@/lib/href";
import { useNotify } from "@/lib/notify";
import { useCan } from "@/lib/permissions";

import {
  assignClient,
  unassignClient,
  useClientSearch,
  useEmployeeClients,
  useEmployeeMutation,
  type ClientListItem,
  type EmployeeDetail,
} from "../api";
import { EMPLOYEE_PERMISSIONS } from "../permissions";

const PAGE_SIZE = 10;

/**
 * The clients in charge of an employee (F06, legacy "assigned clients"): name, e-mail, phone, status, link to the
 * client; assign a client (it leaves its previous employee, history kept) or remove one (confirmation), with
 * `directory.clients.assign`.
 */
export function EmployeeClients({
  tenant,
  employee,
}: {
  tenant: string;
  employee: EmployeeDetail;
}) {
  const t = useTranslations();
  const notify = useNotify();
  const confirm = useConfirm();
  const canAssign = useCan(EMPLOYEE_PERMISSIONS.assignClients);
  const [page, setPage] = React.useState(1);
  const [search, setSearch] = React.useState("");
  const [chosen, setChosen] = React.useState<ComboboxOption>();
  const clients = useEmployeeClients(tenant, employee.id, page, PAGE_SIZE);
  const candidates = useClientSearch(tenant, search, canAssign);
  const assign = useEmployeeMutation(tenant, (clientId: string) =>
    assignClient(clientId, employee.id),
  );
  const unassign = useEmployeeMutation(tenant, unassignClient);

  const onAssign = async () => {
    if (!chosen) {
      return;
    }

    try {
      await assign.mutateAsync(chosen.value);
      setChosen(undefined);
      notify.success("app.employees.clientAssigned");
    } catch (error) {
      notify.error(error);
    }
  };

  const onUnassign = async (client: ClientListItem) => {
    const confirmed = await confirm({
      description: t("app.employees.unassignConfirm", { name: clientName(client) }),
      confirmLabel: t("app.employees.unassign"),
      variant: "destructive",
    });
    if (!confirmed) {
      return;
    }

    try {
      await unassign.mutateAsync(client.id);
      notify.success("app.employees.clientUnassigned");
    } catch (error) {
      notify.error(error);
    }
  };

  // Clients of other employees (or of nobody); the chosen one stays listed while the search changes.
  const options: ComboboxOption[] = (candidates.data?.items ?? [])
    .filter((item) => item.employee?.userId !== employee.id)
    .map((item) => ({
      value: item.id,
      label: clientName(item),
      description: item.employee?.fullName ?? item.email ?? undefined,
    }));
  if (chosen && !options.some((option) => option.value === chosen.value)) {
    options.unshift(chosen);
  }

  const columns: DataTableColumn<ClientListItem>[] = [
    {
      id: "name",
      header: t("Name"),
      mobile: "title",
      cell: (row) => (
        <span className="flex items-center gap-2">
          <UserAvatar
            userId={row.userId}
            name={clientName(row)}
            imageVersion={row.imageVersion}
            size="sm"
          />
          <Link
            href={tenantHref(tenant, `/clients/${row.id}`)}
            className="font-medium underline-offset-4 hover:underline"
          >
            {row.lastName} {row.firstName}
          </Link>
        </span>
      ),
    },
    { id: "email", header: t("Email"), cell: (row) => row.email ?? "—" },
    { id: "phone", header: t("Phone"), cell: (row) => row.phone ?? "—" },
    {
      id: "status",
      header: t("Status"),
      cell: (row) =>
        row.status === "Active" ? (
          <Badge>{t("Active")}</Badge>
        ) : (
          <Badge variant="outline">{t("Inactive")}</Badge>
        ),
    },
    ...(canAssign
      ? [
          {
            id: "actions",
            header: t("app.employees.actions"),
            cell: (row: ClientListItem) => (
              <Button
                type="button"
                size="sm"
                variant="outline"
                onClick={() => void onUnassign(row)}
                disabled={unassign.isPending}
                aria-label={`${t("app.employees.unassign")}: ${clientName(row)}`}
              >
                {t("app.employees.unassign")}
              </Button>
            ),
          },
        ]
      : []),
  ];

  return (
    <div className="flex flex-col gap-4">
      {canAssign ? (
        <Card>
          <CardHeader>
            <CardTitle>
              <h2 className="text-base font-semibold">{t("app.employees.assignClient")}</h2>
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="flex flex-wrap items-end gap-2">
              <div className="flex min-w-64 flex-col gap-2">
                <Label htmlFor="employee-assign-client">{t("Client")}</Label>
                <Combobox
                  id="employee-assign-client"
                  value={chosen?.value}
                  onChange={(value) => setChosen(options.find((option) => option.value === value))}
                  onSearch={setSearch}
                  loading={candidates.isFetching}
                  placeholder={t("app.employees.chooseClient")}
                  options={options}
                />
              </div>
              <Button
                type="button"
                onClick={() => void onAssign()}
                disabled={!chosen || assign.isPending}
              >
                {t("app.clients.assign")}
              </Button>
            </div>
          </CardContent>
        </Card>
      ) : null}
      <DataTable
        label={t("AssignedClients")}
        columns={columns}
        rows={clients.data?.items}
        getRowId={(row) => row.id}
        totalCount={Number(clients.data?.totalCount ?? 0)}
        page={page}
        pageSize={PAGE_SIZE}
        sort={null}
        onPageChange={setPage}
        onPageSizeChange={() => undefined}
        onSortChange={() => undefined}
        isLoading={clients.isPending}
        error={clients.error}
        onRetry={() => void clients.refetch()}
      />
    </div>
  );
}
