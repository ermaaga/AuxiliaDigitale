"use client";

import * as React from "react";
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
import { Skeleton } from "@auxilia/ui/components/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@auxilia/ui/components/table";

import { useConfirm } from "@/components/confirm/confirm-provider";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { useNotify } from "@/lib/notify";

import {
  byModule,
  differsFromDefaults,
  resetRolePermissions,
  setRolePermissions,
  toggledPermissions,
  useAccessMutation,
  useRolePermissions,
  type RolePermission,
} from "../../access-api";
import { TENANT_ROLES, nameOf, roleLabel } from "../../labels";

/**
 * Permissions of the tenant roles (F22, D-18): one table per module, one column per role. A change is saved at once
 * and applies to the next request of every user of the role.
 */
export function TenantPermissions({ slug }: { slug: string }) {
  const t = useTranslations();
  const notify = useNotify();
  const confirm = useConfirm();
  const permissions = useRolePermissions(slug);
  const set = useAccessMutation(slug, (input: { role: string; codes: string[] }) =>
    setRolePermissions(slug, input.role, input.codes),
  );
  const reset = useAccessMutation(slug, (role: string) => resetRolePermissions(slug, role));
  const busy = set.isPending || reset.isPending || permissions.isFetching;
  const describe = (code: string) => nameOf(t, `permissions.${code}.description`, code);

  function toggle(permission: RolePermission, role: string, granted: boolean) {
    const all = permissions.data ?? [];
    set.mutateAsync({ role, codes: toggledPermissions(all, role, permission.code, granted) }).then(
      () =>
        notify.success(
          granted ? "app.platform.permissions.granted" : "app.platform.permissions.revoked",
        ),
      (error: unknown) => notify.error(error),
    );
  }

  async function restore(role: string) {
    if (
      await confirm({
        title: t("app.platform.permissions.resetTitle"),
        description: t("app.platform.permissions.resetText", { role: roleLabel(t, role) }),
        confirmLabel: t("app.platform.permissions.reset"),
      })
    ) {
      await reset.mutateAsync(role).then(
        () => notify.success("app.platform.permissions.resetDone"),
        (error: unknown) => notify.error(error),
      );
    }
  }

  if (permissions.error) {
    return <ApiErrorAlert error={permissions.error} onRetry={() => void permissions.refetch()} />;
  }

  if (!permissions.data) {
    return <Skeleton className="h-64 w-full" />;
  }

  const all = permissions.data;
  return (
    <div className="flex flex-col gap-4">
      <Card>
        <CardHeader>
          <CardTitle>
            <h2 className="text-base font-semibold">{t("app.platform.permissions.roles")}</h2>
          </CardTitle>
          <CardDescription>{t("app.platform.permissions.rolesDescription")}</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-wrap gap-3">
          {TENANT_ROLES.map((role) => {
            const customized = differsFromDefaults(all, role);
            return (
              <div key={role} className="flex items-center gap-2 rounded-md border px-3 py-2">
                <span className="font-medium">{roleLabel(t, role)}</span>
                {customized ? (
                  <Badge>{t("app.platform.permissions.customized")}</Badge>
                ) : (
                  <Badge variant="outline">{t("app.platform.permissions.default")}</Badge>
                )}
                {customized ? (
                  <Button
                    variant="ghost"
                    size="sm"
                    disabled={busy}
                    aria-label={t("app.platform.permissions.resetNamed", {
                      role: roleLabel(t, role),
                    })}
                    onClick={() => void restore(role)}
                  >
                    {t("app.platform.permissions.reset")}
                  </Button>
                ) : null}
              </div>
            );
          })}
        </CardContent>
      </Card>
      {byModule(all).map(([module, items]) => {
        const moduleName = nameOf(t, `modules.${module}.name`, module);
        return (
          <Card key={module}>
            <CardHeader>
              <CardTitle>
                <h2 className="text-base font-semibold">{moduleName}</h2>
              </CardTitle>
            </CardHeader>
            <CardContent>
              <Table aria-label={moduleName}>
                <TableHeader>
                  <TableRow>
                    <TableHead>{t("app.platform.permissions.permission")}</TableHead>
                    {TENANT_ROLES.map((role) => (
                      <TableHead key={role} className="w-28 text-center">
                        {roleLabel(t, role)}
                      </TableHead>
                    ))}
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {items.map((permission) => (
                    <TableRow key={permission.code}>
                      <TableCell>
                        <span className="flex flex-col">
                          <span>{describe(permission.code)}</span>
                          <code className="text-xs text-muted-foreground">{permission.code}</code>
                        </span>
                      </TableCell>
                      {TENANT_ROLES.map((role) => {
                        const granted = permission.roles.includes(role);
                        return (
                          <TableCell key={role} className="text-center">
                            <Checkbox
                              checked={granted}
                              disabled={busy}
                              aria-label={t("app.platform.permissions.toggle", {
                                permission: describe(permission.code),
                                role: roleLabel(t, role),
                              })}
                              onCheckedChange={(checked) =>
                                toggle(permission, role, checked === true)
                              }
                            />
                          </TableCell>
                        );
                      })}
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        );
      })}
    </div>
  );
}
