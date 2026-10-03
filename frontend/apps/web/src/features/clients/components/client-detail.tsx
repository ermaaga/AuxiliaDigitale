"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { ArrowLeftIcon, Trash2Icon } from "lucide-react";
import { isApiError } from "@auxilia/api-client";
import { parseAsStringLiteral, useQueryState } from "nuqs";
import { useTranslations } from "next-intl";
import { Alert, AlertDescription } from "@auxilia/ui/components/alert";
import { Badge } from "@auxilia/ui/components/badge";
import { Button } from "@auxilia/ui/components/button";
import { Skeleton } from "@auxilia/ui/components/skeleton";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@auxilia/ui/components/tabs";

import { useConfirm } from "@/components/confirm/confirm-provider";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { UserAvatar } from "@/components/user-avatar";
import { tenantHref } from "@/lib/href";
import { useNotify } from "@/lib/notify";
import { DocumentsPanel, DOCUMENTS_PERMISSIONS } from "@/features/documents";
import { useCan } from "@/lib/permissions";

import { clientName, deleteClient, useClient, useClientMutation } from "../api";
import { DIRECTORY_PERMISSIONS } from "../permissions";
import { ClientAccess } from "./client-access";
import { ClientAssignment } from "./client-assignment";
import { ClientDataForm } from "./client-data-form";
import { ClientOverview } from "./client-overview";
import { ClientSpecializations } from "./client-specializations";
import { ClientStatusBadge } from "./client-status-badge";

const TABS = ["overview", "data", "assignment", "specializations", "documents", "access"] as const;

/**
 * Client 360° (F05, skill auxilia-ui-design): header with name, status, sign-in and the employee in charge; tabs for
 * the overview, personal data and custom fields, employee with history, specializations, documents (F14) and the account. Actions
 * appear only with their permission (the API checks again). Cases, documents, appointments, requests, tags and
 * consents join the tabs with their modules (B-08…, M-01).
 */
export function ClientDetail({ tenant, id }: { tenant: string; id: string }) {
  const t = useTranslations();
  const router = useRouter();
  const notify = useNotify();
  const confirm = useConfirm();
  const canDelete = useCan(DIRECTORY_PERMISSIONS.deleteClients);
  const canSeeDocuments = useCan(DOCUMENTS_PERMISSIONS.view);
  const [tab, setTab] = useQueryState(
    "tab",
    parseAsStringLiteral(TABS).withDefault("overview").withOptions({ history: "replace" }),
  );
  const query = useClient(tenant, id);
  const remove = useClientMutation(tenant, () => deleteClient(id));
  const back = (
    <Button variant="ghost" size="sm" className="self-start" asChild>
      <Link href={tenantHref(tenant, "/clients")}>
        <ArrowLeftIcon aria-hidden /> {t("app.clients.back")}
      </Link>
    </Button>
  );

  if (query.error) {
    return (
      <div className="flex flex-col gap-4">
        {back}
        {isApiError(query.error) && query.error.status === 404 ? (
          <Alert>
            <AlertDescription>{t("app.clients.notFound")}</AlertDescription>
          </Alert>
        ) : (
          <ApiErrorAlert error={query.error} onRetry={() => void query.refetch()} />
        )}
      </div>
    );
  }

  const client = query.data;
  if (!client) {
    return (
      <div className="flex flex-col gap-4" aria-busy="true">
        {back}
        <Skeleton className="h-10 w-72" />
        <Skeleton className="h-64 w-full" />
      </div>
    );
  }

  const name = clientName(client);
  const onDelete = async () => {
    const confirmed = await confirm({
      description: t("app.clients.deleteConfirm", { name }),
      confirmLabel: t("Delete"),
      variant: "destructive",
    });
    if (!confirmed) {
      return;
    }

    try {
      await remove.mutateAsync(undefined);
      notify.success("app.clients.deleted");
      router.push(tenantHref(tenant, "/clients"));
    } catch (error) {
      notify.error(error);
    }
  };

  return (
    <div className="flex flex-col gap-4">
      {back}
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex items-center gap-3">
          {client.account ? (
            <UserAvatar
              userId={client.account.userId}
              name={name}
              imageVersion={client.imageVersion}
              className="size-14 text-lg"
            />
          ) : null}
          <div className="flex flex-col gap-2">
            <h1 className="text-2xl font-semibold tracking-tight">{name}</h1>
            <div className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
              <ClientStatusBadge status={client.status} />
              {client.account ? (
                client.account.canSignIn ? (
                  <Badge variant="secondary">{t("app.clients.canSignIn")}</Badge>
                ) : (
                  <Badge variant="outline">{t("app.clients.signInDisabled")}</Badge>
                )
              ) : null}
              <span>
                {t("app.clients.employee")}:{" "}
                {client.employee?.fullName ?? t("app.clients.noEmployee")}
              </span>
            </div>
          </div>
        </div>
        {canDelete ? (
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
          <TabsTrigger value="assignment">{t("app.clients.tabs.assignment")}</TabsTrigger>
          <TabsTrigger value="specializations">{t("app.clients.tabs.specializations")}</TabsTrigger>
          {canSeeDocuments ? (
            <TabsTrigger value="documents">{t("app.documents.title")}</TabsTrigger>
          ) : null}
          <TabsTrigger value="access">{t("app.clients.tabs.access")}</TabsTrigger>
        </TabsList>
        <TabsContent value="overview" className="mt-4">
          <ClientOverview tenant={tenant} client={client} />
        </TabsContent>
        <TabsContent value="data" className="mt-4">
          <ClientDataForm tenant={tenant} client={client} />
        </TabsContent>
        <TabsContent value="assignment" className="mt-4">
          <ClientAssignment tenant={tenant} client={client} />
        </TabsContent>
        <TabsContent value="specializations" className="mt-4">
          <ClientSpecializations tenant={tenant} client={client} />
        </TabsContent>
        {canSeeDocuments ? (
          <TabsContent value="documents" className="mt-4">
            <DocumentsPanel tenant={tenant} label={t("app.documents.title")} clientId={client.id} />
          </TabsContent>
        ) : null}
        <TabsContent value="access" className="mt-4">
          <ClientAccess tenant={tenant} client={client} />
        </TabsContent>
      </Tabs>
    </div>
  );
}
