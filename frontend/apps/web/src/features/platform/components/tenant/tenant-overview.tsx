"use client";

import { useFormatter, useTranslations } from "next-intl";
import { ArchiveIcon, PauseIcon, PlayIcon, RotateCwIcon } from "lucide-react";
import { Alert, AlertDescription } from "@auxilia/ui/components/alert";
import { Badge } from "@auxilia/ui/components/badge";
import { Button } from "@auxilia/ui/components/button";
import { Card, CardContent, CardHeader, CardTitle } from "@auxilia/ui/components/card";

import { useConfirm } from "@/components/confirm/confirm-provider";
import { useNotify } from "@/lib/notify";

import { statusLabel } from "../../tenant-status";
import {
  tenantAction,
  useTenant,
  useTenantMutation,
  type TenantAction,
  type TenantDetail,
} from "../../tenant-api";
import { TenantLanguages } from "../tenant-languages";
import { TenantAdministratorsCard } from "./tenant-administrators-card";
import { TenantEditDialog } from "./tenant-edit-dialog";
import { TenantModulesCard } from "./tenant-modules-card";
import { TenantPlanCard } from "./tenant-plan-card";
import { TenantRunsCard } from "./tenant-runs-card";

const DONE: Record<TenantAction, string> = {
  suspend: "app.platform.tenant.suspended",
  reactivate: "app.platform.tenant.reactivated",
  archive: "app.platform.tenant.archived",
  provisioning: "app.platform.tenant.requeued",
};

/**
 * The page of a tenant in the console (N02): details and edit, status actions (archive only, never delete, D-25),
 * provisioning progress and runs, plan, module overrides per role, first Administrator, languages. Technical data only
 * (D-21); the parts that need the tenant database appear once it is active.
 */
export function TenantOverview({ initial }: { initial: TenantDetail }) {
  const t = useTranslations();
  const format = useFormatter();
  const confirm = useConfirm();
  const notify = useNotify();
  const { data: tenant } = useTenant(initial);
  const action = useTenantMutation(tenant.slug, (kind: TenantAction) =>
    tenantAction(tenant.slug, kind),
  );

  const archived = tenant.status === "Archived";
  const hasDatabase = tenant.status === "Active" || tenant.status === "Suspended";

  async function run(kind: TenantAction) {
    const question =
      kind === "suspend"
        ? {
            description: t("app.platform.tenant.confirmSuspend", { name: tenant.displayName }),
            confirmLabel: t("app.platform.tenant.suspend"),
            variant: "destructive" as const,
          }
        : kind === "archive"
          ? {
              description: t("app.platform.tenant.confirmArchive", { name: tenant.displayName }),
              confirmLabel: t("app.platform.tenant.archive"),
              variant: "destructive" as const,
            }
          : undefined;
    if (question && !(await confirm(question))) {
      return;
    }

    action.mutate(kind, {
      onSuccess: () => notify.success(DONE[kind]),
      onError: (error) => notify.error(error),
    });
  }

  const details: [string, React.ReactNode][] = [
    [t("app.platform.tenants.slug"), <code key="slug">{tenant.slug}</code>],
    [
      t("app.platform.tenant.language"),
      tenant.defaultLanguage === "en" ? t("English") : t("Italian"),
    ],
    [t("app.platform.tenant.timeZone"), tenant.timeZone],
    [
      t("app.platform.tenants.schemaVersion"),
      <span key="schema" className="break-all">
        {tenant.schemaVersion ?? "—"}
      </span>,
    ],
    ...(tenant.archivedAt
      ? [
          [
            t("app.platform.tenant.archivedAt"),
            format.dateTime(new Date(tenant.archivedAt), { dateStyle: "medium" }),
          ] as [string, React.ReactNode],
        ]
      : []),
  ];

  return (
    <div className="flex max-w-5xl flex-col gap-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex flex-col gap-1">
          <div className="flex flex-wrap items-center gap-2">
            <h1 className="text-2xl font-semibold tracking-tight">{tenant.displayName}</h1>
            <Badge
              variant={
                tenant.status === "Active"
                  ? "secondary"
                  : tenant.status === "Archived"
                    ? "outline"
                    : "destructive"
              }
            >
              {statusLabel(t, tenant.status)}
            </Badge>
          </div>
          <p className="text-sm text-muted-foreground">{t("app.platform.tenant.description")}</p>
        </div>
        <div className="flex flex-wrap gap-2">
          {tenant.status === "Provisioning" || tenant.status === "MigrationFailed" ? (
            <Button
              variant="outline"
              onClick={() => void run("provisioning")}
              disabled={action.isPending}
            >
              <RotateCwIcon aria-hidden /> {t("app.platform.tenant.retry")}
            </Button>
          ) : null}
          {tenant.status === "Active" ? (
            <Button
              variant="outline"
              onClick={() => void run("suspend")}
              disabled={action.isPending}
            >
              <PauseIcon aria-hidden /> {t("app.platform.tenant.suspend")}
            </Button>
          ) : null}
          {tenant.status === "Suspended" ? (
            <Button
              variant="outline"
              onClick={() => void run("reactivate")}
              disabled={action.isPending}
            >
              <PlayIcon aria-hidden /> {t("app.platform.tenant.reactivate")}
            </Button>
          ) : null}
          {!archived ? (
            <Button
              variant="outline"
              onClick={() => void run("archive")}
              disabled={action.isPending}
            >
              <ArchiveIcon aria-hidden /> {t("app.platform.tenant.archive")}
            </Button>
          ) : null}
        </div>
      </div>

      {tenant.status === "Provisioning" ? (
        <Alert role="status">
          <AlertDescription>{t("app.platform.tenant.provisioning")}</AlertDescription>
        </Alert>
      ) : tenant.status === "MigrationFailed" ? (
        <Alert variant="destructive">
          <AlertDescription>{t("app.platform.tenant.migrationFailed")}</AlertDescription>
        </Alert>
      ) : archived ? (
        <Alert>
          <AlertDescription>{t("app.platform.tenant.archivedNotice")}</AlertDescription>
        </Alert>
      ) : null}

      <div className="grid gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader className="flex flex-row items-center justify-between gap-2">
            <CardTitle>
              <h2 className="text-base font-semibold">{t("app.platform.tenant.details")}</h2>
            </CardTitle>
            {!archived ? <TenantEditDialog tenant={tenant} /> : null}
          </CardHeader>
          <CardContent>
            <dl className="grid gap-x-6 gap-y-3 sm:grid-cols-2">
              {details.map(([term, value]) => (
                <div key={term} className="flex flex-col gap-1">
                  <dt className="text-sm text-muted-foreground">{term}</dt>
                  <dd className="text-sm">{value}</dd>
                </div>
              ))}
            </dl>
          </CardContent>
        </Card>
        <TenantPlanCard tenant={tenant} readOnly={archived} />
        <TenantRunsCard runs={tenant.runs} />
        {hasDatabase ? <TenantAdministratorsCard slug={tenant.slug} /> : null}
      </div>
      <TenantModulesCard slug={tenant.slug} readOnly={archived} />
      {hasDatabase ? <TenantLanguages tenant={tenant.slug} /> : null}
    </div>
  );
}
