"use client";

import * as React from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useTranslations } from "next-intl";
import { Badge } from "@auxilia/ui/components/badge";
import { Button } from "@auxilia/ui/components/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@auxilia/ui/components/card";
import { Checkbox } from "@auxilia/ui/components/checkbox";
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@auxilia/ui/components/dialog";
import { Label } from "@auxilia/ui/components/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@auxilia/ui/components/select";

import { DataTable } from "@/components/data-table/data-table";
import type { DataTableColumn } from "@/components/data-table/table-model";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { useNotify } from "@/lib/notify";

import { TENANT_ROLES, nameOf, rolesText } from "../../labels";
import {
  saveModuleOverride,
  tenantKey,
  useTenantModules,
  type TenantModule,
} from "../../tenant-api";

type Mode = "plan" | "enabled" | "disabled";

/** Visibility of each module per role (ARCHITECTURE §5.2): plan, override for this tenant (D-18), result. */
export function TenantModulesCard({ slug, readOnly }: { slug: string; readOnly: boolean }) {
  const t = useTranslations();
  const modules = useTenantModules(slug);
  const [editing, setEditing] = React.useState<TenantModule>();

  const columns: DataTableColumn<TenantModule>[] = [
    {
      id: "module",
      header: t("app.platform.modules.module"),
      hideable: false,
      mobile: "title",
      cell: (module) => (
        <span className="font-medium">{nameOf(t, module.nameKey, module.code)}</span>
      ),
    },
    {
      id: "kind",
      header: t("app.platform.modules.kind"),
      cell: (module) =>
        module.kind === "Core" ? (
          <Badge variant="outline">{t("app.platform.modules.core")}</Badge>
        ) : (
          t("app.platform.modules.optional")
        ),
    },
    {
      id: "planRoles",
      header: t("app.platform.modules.planRoles"),
      cell: (module) => (module.kind === "Core" ? "—" : rolesText(t, module.planRoles)),
    },
    {
      id: "override",
      header: t("app.platform.modules.override"),
      cell: (module) =>
        module.override === null
          ? t("app.platform.modules.noOverride")
          : module.override.isEnabled
            ? rolesText(t, module.override.roles)
            : t("app.platform.modules.disabled"),
    },
    {
      id: "effective",
      header: t("app.platform.modules.effective"),
      cell: (module) => rolesText(t, module.effectiveRoles),
    },
    ...(readOnly
      ? []
      : [
          {
            id: "actions",
            header: t("Actions"),
            hideable: false,
            cell: (module: TenantModule) =>
              module.kind === "Core" ? null : (
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => setEditing(module)}
                  aria-label={t("app.platform.modules.edit", {
                    module: nameOf(t, module.nameKey, module.code),
                  })}
                >
                  {t("Edit")}
                </Button>
              ),
          } satisfies DataTableColumn<TenantModule>,
        ]),
  ];

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2 className="text-base font-semibold">{t("app.platform.modules.title")}</h2>
        </CardTitle>
        <CardDescription>{t("app.platform.modules.description")}</CardDescription>
      </CardHeader>
      <CardContent>
        <DataTable
          label={t("app.platform.modules.title")}
          columns={columns}
          rows={modules.data}
          getRowId={(module) => module.code}
          totalCount={modules.data?.length ?? 0}
          page={1}
          pageSize={100}
          sort={null}
          onPageChange={() => {}}
          onPageSizeChange={() => {}}
          onSortChange={() => {}}
          isLoading={modules.isPending}
          error={modules.error}
          onRetry={() => void modules.refetch()}
          hidePaging
        />
        {editing ? (
          <ModuleOverrideDialog
            slug={slug}
            module={editing}
            onClose={() => setEditing(undefined)}
          />
        ) : null}
      </CardContent>
    </Card>
  );
}

function ModuleOverrideDialog({
  slug,
  module,
  onClose,
}: {
  slug: string;
  module: TenantModule;
  onClose: () => void;
}) {
  const t = useTranslations();
  const notify = useNotify();
  const client = useQueryClient();
  const [mode, setMode] = React.useState<Mode>(
    module.override === null ? "plan" : module.override.isEnabled ? "enabled" : "disabled",
  );
  const [roles, setRoles] = React.useState<string[]>(module.override?.roles ?? module.planRoles);
  const save = useMutation({
    mutationFn: () =>
      saveModuleOverride(
        slug,
        module.code,
        mode === "plan"
          ? null
          : { isEnabled: mode === "enabled", roles: mode === "enabled" ? roles : [] },
      ),
    onSuccess: (modules) => {
      client.setQueryData(tenantKey(slug, "modules"), modules);
      notify.success("app.platform.modules.saved");
      onClose();
    },
  });
  const name = nameOf(t, module.nameKey, module.code);

  return (
    <Dialog open onOpenChange={(open) => (open ? undefined : onClose())}>
      <DialogContent closeLabel={t("Close")}>
        <form
          className="flex flex-col gap-4"
          onSubmit={(event) => {
            event.preventDefault();
            save.mutate();
          }}
        >
          <DialogHeader>
            <DialogTitle>{t("app.platform.modules.edit", { module: name })}</DialogTitle>
          </DialogHeader>
          {save.error ? <ApiErrorAlert error={save.error} /> : null}
          <div className="flex flex-col gap-1">
            <Label htmlFor="module-mode">{t("app.platform.modules.override")}</Label>
            <Select value={mode} onValueChange={(value) => setMode(value as Mode)}>
              <SelectTrigger id="module-mode" className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="plan">
                  {t("app.platform.modules.followPlan")} ({rolesText(t, module.planRoles)})
                </SelectItem>
                <SelectItem value="enabled">{t("app.platform.modules.enabledFor")}</SelectItem>
                <SelectItem value="disabled">{t("app.platform.modules.disabled")}</SelectItem>
              </SelectContent>
            </Select>
          </div>
          {mode === "enabled" ? (
            <fieldset className="flex flex-col gap-2">
              <legend className="mb-1 text-sm font-medium">{t("Role")}</legend>
              {TENANT_ROLES.map((role) => (
                <div key={role} className="flex items-center gap-2">
                  <Checkbox
                    id={`module-role-${role}`}
                    checked={roles.includes(role)}
                    onCheckedChange={(checked) =>
                      setRoles((current) =>
                        checked === true
                          ? [...current, role]
                          : current.filter((item) => item !== role),
                      )
                    }
                  />
                  <Label htmlFor={`module-role-${role}`}>{t(role)}</Label>
                </div>
              ))}
            </fieldset>
          ) : null}
          <DialogFooter closeLabel={t("Cancel")}>
            <Button
              type="submit"
              disabled={save.isPending || (mode === "enabled" && roles.length === 0)}
            >
              {t("Save")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
