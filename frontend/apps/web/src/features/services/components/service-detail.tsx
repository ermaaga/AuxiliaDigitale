"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { isApiError } from "@auxilia/api-client";
import { ArrowLeftIcon, Trash2Icon } from "lucide-react";
import { parseAsStringLiteral, useQueryState } from "nuqs";
import { useLocale, useTranslations } from "next-intl";
import { Alert, AlertDescription } from "@auxilia/ui/components/alert";
import { Badge } from "@auxilia/ui/components/badge";
import { Button } from "@auxilia/ui/components/button";
import { Card, CardContent } from "@auxilia/ui/components/card";
import { Skeleton } from "@auxilia/ui/components/skeleton";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@auxilia/ui/components/tabs";

import { useConfirm } from "@/components/confirm/confirm-provider";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { CASES_PERMISSIONS, CasesTable } from "@/features/cases";
import { tenantHref } from "@/lib/href";
import { useNotify } from "@/lib/notify";
import { useCan } from "@/lib/permissions";

import { deleteService, updateService, useService, useServiceMutation } from "../api";
import { SERVICES_PERMISSIONS } from "../permissions";
import { serviceBody, type ServiceOutput } from "../schemas/service";
import { ChecklistEditor } from "./checklist-editor";
import { FolderEditor } from "./folder-editor";
import { ServiceForm } from "./service-form";
import { euro } from "./services-table";

const TABS = ["data", "folders", "checklist", "cases"] as const;

/**
 * A service (F08): header with price, duration, category and status; tabs for its data (edit, delete — refused with
 * 409 when cases use it, then it can be deactivated), the folder template (F33) and its cases (F09).
 */
export function ServiceDetail({ tenant, id }: { tenant: string; id: string }) {
  const t = useTranslations();
  const locale = useLocale();
  const router = useRouter();
  const notify = useNotify();
  const confirm = useConfirm();
  const canManage = useCan(SERVICES_PERMISSIONS.manage);
  const canSeeCases = useCan(CASES_PERMISSIONS.view);
  const [tab, setTab] = useQueryState(
    "tab",
    parseAsStringLiteral(TABS).withDefault("data").withOptions({ history: "replace" }),
  );
  const query = useService(tenant, id);
  const save = useServiceMutation(tenant, (values: ServiceOutput) =>
    updateService(id, { ...serviceBody(values), isActive: values.isActive }),
  );
  const remove = useServiceMutation(tenant, () => deleteService(id));
  const backLink = (
    <Button variant="ghost" size="sm" className="self-start" asChild>
      <Link href={tenantHref(tenant, "/services")}>
        <ArrowLeftIcon aria-hidden /> {t("app.services.back")}
      </Link>
    </Button>
  );

  if (query.error) {
    return (
      <div className="flex flex-col gap-4">
        {backLink}
        {isApiError(query.error) && query.error.status === 404 ? (
          <Alert>
            <AlertDescription>{t("app.services.notFound")}</AlertDescription>
          </Alert>
        ) : (
          <ApiErrorAlert error={query.error} onRetry={() => void query.refetch()} />
        )}
      </div>
    );
  }

  const service = query.data;
  if (!service) {
    return (
      <div className="flex flex-col gap-4" aria-busy="true">
        {backLink}
        <Skeleton className="h-10 w-80" />
        <Skeleton className="h-64 w-full" />
      </div>
    );
  }

  const onDelete = async () => {
    const confirmed = await confirm({
      description: t("app.services.deleteConfirm", { name: service.name }),
      confirmLabel: t("Delete"),
      variant: "destructive",
    });
    if (confirmed) {
      try {
        await remove.mutateAsync(undefined);
        notify.success("app.services.deleted");
        router.push(tenantHref(tenant, "/services"));
      } catch (error) {
        notify.error(error);
      }
    }
  };

  return (
    <div className="flex flex-col gap-4">
      {backLink}
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex flex-col gap-2">
          <h1 className="text-2xl font-semibold tracking-tight">{service.name}</h1>
          <div className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
            <span>{euro(service.price, service.currency, locale)}</span>
            <span>·</span>
            <span>{t("app.services.days", { count: Number(service.durationDays) })}</span>
            {service.category ? <Badge variant="outline">{service.category.name}</Badge> : null}
            {service.isActive ? (
              <Badge variant="secondary">{t("Active")}</Badge>
            ) : (
              <Badge variant="outline">{t("Inactive")}</Badge>
            )}
          </div>
        </div>
        {canManage ? (
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
        <TabsList aria-label={service.name} className="h-auto flex-wrap">
          <TabsTrigger value="data">{t("app.services.tabs.data")}</TabsTrigger>
          <TabsTrigger value="folders">{t("FolderTemplate")}</TabsTrigger>
          <TabsTrigger value="checklist">{t("app.services.checklist.title")}</TabsTrigger>
          {canSeeCases ? <TabsTrigger value="cases">{t("nav.cases")}</TabsTrigger> : null}
        </TabsList>
        <TabsContent value="data" className="mt-4">
          <Card>
            <CardContent className="pt-6">
              <ServiceForm
                key={`${service.id}-${service.name}-${service.isActive}`}
                tenant={tenant}
                service={service}
                disabled={!canManage}
                submitLabel={t("Save")}
                error={save.error}
                onSubmit={async (values) => {
                  await save.mutateAsync(values);
                  notify.success("app.services.saved");
                }}
              />
            </CardContent>
          </Card>
        </TabsContent>
        <TabsContent value="folders" className="mt-4">
          <FolderEditor tenant={tenant} serviceId={service.id} canManage={canManage} />
        </TabsContent>
        <TabsContent value="checklist" className="mt-4">
          <ChecklistEditor tenant={tenant} serviceId={service.id} canManage={canManage} />
        </TabsContent>
        {canSeeCases ? (
          <TabsContent value="cases" className="mt-4">
            <CasesTable tenant={tenant} label={t("nav.cases")} serviceId={service.id} />
          </TabsContent>
        ) : null}
      </Tabs>
    </div>
  );
}
