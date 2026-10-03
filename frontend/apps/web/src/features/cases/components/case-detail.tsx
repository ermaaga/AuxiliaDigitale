"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { isApiError } from "@auxilia/api-client";
import { ArrowLeftIcon, ArrowRightIcon, Trash2Icon, Undo2Icon } from "lucide-react";
import { useFormatter, useLocale, useTranslations } from "next-intl";
import { Alert, AlertDescription } from "@auxilia/ui/components/alert";
import { Badge } from "@auxilia/ui/components/badge";
import { Button } from "@auxilia/ui/components/button";
import { Card, CardContent, CardHeader, CardTitle } from "@auxilia/ui/components/card";
import { Skeleton } from "@auxilia/ui/components/skeleton";

import { useConfirm } from "@/components/confirm/confirm-provider";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { tenantHref } from "@/lib/href";
import { useNotify } from "@/lib/notify";

import {
  advanceCase,
  deleteCase,
  formatMoney,
  moveCaseBack,
  useCase,
  useCaseMutation,
  type CaseDetail as Detail,
} from "../api";
import { CaseDocuments } from "./case-documents";
import { CasePayments } from "./case-payments";
import { CaseStatusBadge } from "./case-status-badge";
import { CaseStepper } from "./case-stepper";
import { CaseTimeline } from "./case-timeline";
import { CompleteCaseDialog } from "./complete-case-dialog";

/**
 * The detail of a case (F09, skill auxilia-ui-design "Case detail"): header "service — client", status stepper with
 * forward (confirmation) / back / complete, the content of the status (legacy: service on Inserted, documents and
 * folders on InProgress, summary on Sent and Completed), payments with the balance due and the timeline. Actions
 * appear only when the API allows them (`canManage`, `canDelete`).
 */
export function CaseDetail({ tenant, id }: { tenant: string; id: string }) {
  const t = useTranslations();
  const router = useRouter();
  const notify = useNotify();
  const confirm = useConfirm();
  const query = useCase(tenant, id);
  const advance = useCaseMutation(tenant, () => advanceCase(id));
  const back = useCaseMutation(tenant, () => moveCaseBack(id));
  const remove = useCaseMutation(tenant, () => deleteCase(id));
  const backLink = (
    <Button variant="ghost" size="sm" className="self-start" asChild>
      <Link href={tenantHref(tenant, "/cases")}>
        <ArrowLeftIcon aria-hidden /> {t("app.cases.back")}
      </Link>
    </Button>
  );

  if (query.error) {
    return (
      <div className="flex flex-col gap-4">
        {backLink}
        {isApiError(query.error) && query.error.status === 404 ? (
          <Alert>
            <AlertDescription>{t("app.cases.notFound")}</AlertDescription>
          </Alert>
        ) : (
          <ApiErrorAlert error={query.error} onRetry={() => void query.refetch()} />
        )}
      </div>
    );
  }

  const value = query.data;
  if (!value) {
    return (
      <div className="flex flex-col gap-4" aria-busy="true">
        {backLink}
        <Skeleton className="h-10 w-80" />
        <Skeleton className="h-16 w-full" />
        <Skeleton className="h-64 w-full" />
      </div>
    );
  }

  const run = async (action: () => Promise<unknown>, success: string) => {
    try {
      await action();
      notify.success(success);
    } catch (error) {
      notify.error(error);
    }
  };

  const onForward = async () => {
    if (
      await confirm({ description: t("ConfirmStatusChange"), confirmLabel: t("app.cases.forward") })
    ) {
      await run(() => advance.mutateAsync(undefined), "app.cases.advanced");
    }
  };

  const onDelete = async () => {
    const confirmed = await confirm({
      description: t("app.cases.deleteConfirm", { number: value.number }),
      confirmLabel: t("Delete"),
      variant: "destructive",
    });
    if (confirmed) {
      try {
        await remove.mutateAsync(undefined);
        notify.success("DeletedSubscriptionSuccesfully");
        router.push(tenantHref(tenant, "/cases"));
      } catch (error) {
        notify.error(error);
      }
    }
  };

  const busy = advance.isPending || back.isPending || remove.isPending;
  const manage = value.canManage && value.status !== "Completed";

  return (
    <div className="flex flex-col gap-4">
      {backLink}
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex flex-col gap-2">
          <h1 className="text-2xl font-semibold tracking-tight">
            {value.service.name} — {value.client.fullName}
          </h1>
          <div className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
            <span>{value.number}</span>
            <CaseStatusBadge status={value.status} rejected={value.isRejected} />
            {value.specialization ? (
              <Badge variant="outline">
                {value.specialization.name}
                {value.specialization.isPrivate ? ` · ${t("app.cases.private")}` : ""}
              </Badge>
            ) : null}
            <Link
              href={tenantHref(tenant, `/clients/${value.client.id}`)}
              className="underline-offset-4 hover:underline"
            >
              {t("app.cases.openClient")}
            </Link>
          </div>
        </div>
        {value.canDelete ? (
          <Button type="button" variant="outline" onClick={() => void onDelete()} disabled={busy}>
            <Trash2Icon aria-hidden /> {t("Delete")}
          </Button>
        ) : null}
      </header>

      <CaseStepper status={value.status} />
      {manage ? (
        <div className="flex flex-wrap gap-2">
          {value.status === "InProgress" || value.status === "Sent" ? (
            <Button
              type="button"
              variant="outline"
              disabled={busy}
              onClick={() => void run(() => back.mutateAsync(undefined), "app.cases.movedBack")}
            >
              <Undo2Icon aria-hidden /> {t("Back")}
            </Button>
          ) : null}
          {value.status === "Sent" ? (
            <CompleteCaseDialog tenant={tenant} value={value} />
          ) : (
            <Button type="button" disabled={busy} onClick={() => void onForward()}>
              {t("app.cases.forward")} <ArrowRightIcon aria-hidden />
            </Button>
          )}
        </div>
      ) : null}

      <StatusContent tenant={tenant} value={value} />
      <div className="grid gap-4 lg:grid-cols-2">
        <CasePayments tenant={tenant} value={value} />
        <CaseTimeline value={value} />
      </div>
    </div>
  );
}

/** The content of each status (legacy `SubscriptionDetail`). */
function StatusContent({ tenant, value }: { tenant: string; value: Detail }) {
  const t = useTranslations();
  const locale = useLocale();
  const format = useFormatter();
  const date = (input: string | null | undefined) =>
    input ? format.dateTime(new Date(input), { dateStyle: "medium" }) : "—";

  if (value.status === "InProgress") {
    return <CaseDocuments tenant={tenant} value={value} />;
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2 className="text-base font-semibold">
            {value.status === "Inserted" ? t("Membership") : t("app.cases.summary")}
          </h2>
        </CardTitle>
      </CardHeader>
      <CardContent>
        <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
          <dt className="text-muted-foreground">{t("app.cases.service")}</dt>
          <dd>{value.service.name}</dd>
          {value.status === "Inserted" ? (
            <>
              <dt className="text-muted-foreground">{t("Description")}</dt>
              <dd>{value.service.description ?? "—"}</dd>
              <dt className="text-muted-foreground">{t("Price")}</dt>
              <dd>{formatMoney(value.price, value.currency, locale)}</dd>
              <dt className="text-muted-foreground">{t("DurationDays")}</dt>
              <dd>{Number(value.service.durationDays)}</dd>
            </>
          ) : (
            <>
              <dt className="text-muted-foreground">{t("Client")}</dt>
              <dd>{value.client.fullName}</dd>
            </>
          )}
          <dt className="text-muted-foreground">{t("StartDate")}</dt>
          <dd>{date(value.startedOn)}</dd>
          <dt className="text-muted-foreground">{t("app.cases.dueOn")}</dt>
          <dd>{date(value.dueOn)}</dd>
          {value.status === "Completed" ? (
            <>
              <dt className="text-muted-foreground">{t("EndDate")}</dt>
              <dd>{date(value.expiresOn)}</dd>
              <dt className="text-muted-foreground">{t("AmountPaid")}</dt>
              <dd>{formatMoney(value.amountPaid, value.currency, locale)}</dd>
              <dt className="text-muted-foreground">{t("app.cases.outcome")}</dt>
              <dd>{value.isRejected ? t("Rejected") : t("Completed")}</dd>
            </>
          ) : null}
        </dl>
        {value.status !== "Inserted" ? (
          <p className="mt-3 text-sm text-muted-foreground">{t("app.cases.documentsHint")}</p>
        ) : null}
      </CardContent>
    </Card>
  );
}
