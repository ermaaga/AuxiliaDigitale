"use client";

import * as React from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { isApiError } from "@auxilia/api-client";
import { ArrowLeftIcon, PlusIcon, SendIcon, XIcon } from "lucide-react";
import { useFormatter, useTranslations } from "next-intl";
import { Badge } from "@auxilia/ui/components/badge";
import { Button } from "@auxilia/ui/components/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@auxilia/ui/components/card";
import { Input } from "@auxilia/ui/components/input";
import { Label } from "@auxilia/ui/components/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@auxilia/ui/components/select";
import { Skeleton } from "@auxilia/ui/components/skeleton";

import { useConfirm } from "@/components/confirm/confirm-provider";
import { DataTable } from "@/components/data-table/data-table";
import { FilterSelect } from "@/components/data-table/filters";
import type { DataTableColumn } from "@/components/data-table/table-model";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { tenantHref } from "@/lib/href";
import { useNotify } from "@/lib/notify";
import { useCan } from "@/lib/permissions";

import {
  RECIPIENT_STATUSES,
  campaignAudience,
  cancelCampaign,
  createCampaign,
  deleteCampaign,
  sendCampaign,
  useCampaign,
  useCampaignRecipients,
  useCampaigns,
  useEmailTemplates,
  useMarketingMutation,
  useSegments,
  useStaticLists,
  useTemplatePreview,
  type Campaign,
  type CampaignRecipient,
} from "../api";
import { MARKETING_PERMISSIONS } from "../permissions";

export function campaignVariant(
  status: string,
): "default" | "secondary" | "destructive" | "outline" {
  return status === "Sent"
    ? "default"
    : status === "Failed"
      ? "destructive"
      : status === "Sending"
        ? "secondary"
        : "outline";
}

/** Share of the recipients handled (sent, failed or excluded), 0–100. */
export function campaignProgress(
  campaign: Pick<
    Campaign,
    "recipientCount" | "sentCount" | "failedCount" | "excludedCount" | "status"
  >,
): number {
  const total = Number(campaign.recipientCount);
  if (campaign.status === "Sent") {
    return 100;
  }

  return total === 0
    ? 0
    : Math.min(
        100,
        Math.round(
          ((Number(campaign.sentCount) +
            Number(campaign.failedCount) +
            Number(campaign.excludedCount)) *
            100) /
            total,
        ),
      );
}

/** The campaigns (N01), newest first, refreshed while one is sending. */
export function CampaignsPage({ tenant }: { tenant: string }) {
  const t = useTranslations();
  const format = useFormatter();
  const canManage = useCan(MARKETING_PERMISSIONS.manageCampaigns);
  const [page, setPage] = React.useState(1);
  const [pageSize, setPageSize] = React.useState(25);
  const campaigns = useCampaigns(tenant, page, pageSize);
  const columns: DataTableColumn<Campaign>[] = [
    {
      id: "name",
      header: t("Name"),
      hideable: false,
      mobile: "title",
      cell: (campaign) => (
        <Link
          href={tenantHref(tenant, `/marketing/campaigns/${campaign.id}`)}
          className="font-medium underline-offset-4 hover:underline"
        >
          {campaign.name}
        </Link>
      ),
    },
    {
      id: "audience",
      header: t("app.marketing.campaigns.audience"),
      cell: (campaign) => campaign.audienceName ?? "—",
    },
    {
      id: "status",
      header: t("Status"),
      mobile: "detail",
      cell: (campaign) => (
        <Badge variant={campaignVariant(campaign.status)}>
          {t(`app.marketing.campaigns.status.${campaign.status}`)}
        </Badge>
      ),
    },
    {
      id: "results",
      header: t("app.marketing.campaigns.results"),
      cell: (campaign) =>
        t("app.marketing.campaigns.counters", {
          sent: Number(campaign.sentCount),
          excluded: Number(campaign.excludedCount),
          failed: Number(campaign.failedCount),
        }),
    },
    {
      id: "createdAt",
      header: t("app.marketing.campaigns.createdAt"),
      cell: (campaign) => format.dateTime(new Date(campaign.createdAt), { dateStyle: "medium" }),
    },
  ];

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-sm text-muted-foreground">{t("app.marketing.campaigns.description")}</p>
        {canManage ? (
          <Button asChild>
            <Link href={tenantHref(tenant, "/marketing/campaigns/new")}>
              <PlusIcon aria-hidden /> {t("app.marketing.campaigns.new")}
            </Link>
          </Button>
        ) : null}
      </div>
      <DataTable
        label={t("app.marketing.nav.campaigns")}
        columns={columns}
        rows={campaigns.data?.items}
        getRowId={(campaign) => campaign.id}
        totalCount={Number(campaigns.data?.totalCount ?? 0)}
        page={page}
        pageSize={pageSize}
        sort={null}
        onPageChange={setPage}
        onPageSizeChange={(size) => {
          setPageSize(size);
          setPage(1);
        }}
        onSortChange={() => {}}
        isLoading={campaigns.isPending}
        error={campaigns.error}
        onRetry={() => void campaigns.refetch()}
      />
    </div>
  );
}

const STEPS = ["audience", "template", "summary"] as const;

/** A new campaign in three steps (skill auxilia-ui-design "wizard"): name and audience, template with its preview, summary. */
export function CampaignWizard({ tenant }: { tenant: string }) {
  const t = useTranslations();
  const router = useRouter();
  const notify = useNotify();
  const segments = useSegments(tenant);
  const lists = useStaticLists(tenant);
  const templates = useEmailTemplates(tenant);
  const [step, setStep] = React.useState(0);
  const [name, setName] = React.useState("");
  const [audience, setAudience] = React.useState<string>();
  const [templateId, setTemplateId] = React.useState<string>();
  const preview = useTemplatePreview(tenant, templateId, 0);
  const [kind, audienceId] = audience?.split(":") ?? [];
  const create = useMarketingMutation(tenant, () =>
    createCampaign({
      name: name.trim(),
      templateId: templateId!,
      segmentId: kind === "segment" ? audienceId! : null,
      listId: kind === "list" ? audienceId! : null,
    }),
  );
  const audienceName =
    kind === "segment"
      ? segments.data?.find((item) => item.id === audienceId)?.name
      : lists.data?.find((item) => item.id === audienceId)?.name;
  const templateName = templates.data?.find((item) => item.id === templateId)?.name;
  const canNext =
    step === 0
      ? name.trim() !== "" && audience !== undefined
      : step === 1
        ? templateId !== undefined
        : true;

  return (
    <div className="flex flex-col gap-4">
      <Button variant="ghost" size="sm" className="self-start" asChild>
        <Link href={tenantHref(tenant, "/marketing/campaigns")}>
          <ArrowLeftIcon aria-hidden /> {t("app.marketing.campaigns.back")}
        </Link>
      </Button>
      <ol className="flex flex-wrap gap-2 text-sm" aria-label={t("app.marketing.campaigns.steps")}>
        {STEPS.map((item, index) => (
          <li key={item} aria-current={index === step ? "step" : undefined}>
            <Badge variant={index === step ? "default" : "outline"}>
              {index + 1}. {t(`app.marketing.campaigns.step.${item}`)}
            </Badge>
          </li>
        ))}
      </ol>
      <Card>
        <CardContent className="flex flex-col gap-4 pt-6">
          {step === 0 ? (
            <>
              <div className="flex flex-col gap-1 sm:max-w-md">
                <Label htmlFor="campaign-name">{t("Name")} *</Label>
                <Input
                  id="campaign-name"
                  value={name}
                  maxLength={100}
                  onChange={(event) => setName(event.target.value)}
                />
              </div>
              <div className="flex flex-col gap-1 sm:max-w-md">
                <Label htmlFor="campaign-audience">{t("app.marketing.campaigns.audience")} *</Label>
                <Select value={audience ?? ""} onValueChange={setAudience}>
                  <SelectTrigger id="campaign-audience">
                    <SelectValue placeholder={t("app.marketing.campaigns.chooseAudience")} />
                  </SelectTrigger>
                  <SelectContent>
                    {(segments.data ?? []).map((segment) => (
                      <SelectItem key={segment.id} value={`segment:${segment.id}`}>
                        {t("app.marketing.campaigns.segmentOption", { name: segment.name })}
                      </SelectItem>
                    ))}
                    {(lists.data ?? []).map((list) => (
                      <SelectItem key={list.id} value={`list:${list.id}`}>
                        {t("app.marketing.campaigns.listOption", {
                          name: list.name,
                          count: Number(list.memberCount),
                        })}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            </>
          ) : step === 1 ? (
            <>
              <div className="flex flex-col gap-1 sm:max-w-md">
                <Label htmlFor="campaign-template">{t("app.marketing.campaigns.template")} *</Label>
                <Select value={templateId ?? ""} onValueChange={setTemplateId}>
                  <SelectTrigger id="campaign-template">
                    <SelectValue placeholder={t("app.marketing.campaigns.chooseTemplate")} />
                  </SelectTrigger>
                  <SelectContent>
                    {(templates.data ?? []).map((template) => (
                      <SelectItem key={template.id} value={template.id}>
                        {template.name} · {template.language.toUpperCase()}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              {preview.data ? (
                <div className="flex flex-col gap-2">
                  <p className="text-sm">
                    <span className="text-muted-foreground">{t("Subject")}: </span>
                    <span className="font-medium">{preview.data.subject}</span>
                  </p>
                  <iframe
                    title={t("app.marketing.templates.preview")}
                    sandbox=""
                    srcDoc={preview.data.body}
                    className="h-64 w-full rounded-md border bg-white"
                  />
                </div>
              ) : null}
            </>
          ) : (
            <dl className="grid gap-2 text-sm sm:grid-cols-3">
              <div>
                <dt className="text-muted-foreground">{t("Name")}</dt>
                <dd className="font-medium">{name}</dd>
              </div>
              <div>
                <dt className="text-muted-foreground">{t("app.marketing.campaigns.audience")}</dt>
                <dd className="font-medium">{audienceName}</dd>
              </div>
              <div>
                <dt className="text-muted-foreground">{t("app.marketing.campaigns.template")}</dt>
                <dd className="font-medium">{templateName}</dd>
              </div>
              <p className="text-muted-foreground sm:col-span-3">
                {t("app.marketing.campaigns.summaryHint")}
              </p>
            </dl>
          )}
          {create.error &&
          !(isApiError(create.error) && Object.keys(create.error.fieldErrors).length > 0) ? (
            <ApiErrorAlert error={create.error} />
          ) : null}
          <div className="flex justify-between gap-2">
            <Button
              type="button"
              variant="outline"
              disabled={step === 0}
              onClick={() => setStep((current) => current - 1)}
            >
              {t("Back")}
            </Button>
            {step < STEPS.length - 1 ? (
              <Button
                type="button"
                disabled={!canNext}
                onClick={() => setStep((current) => current + 1)}
              >
                {t("app.marketing.campaigns.next")}
              </Button>
            ) : (
              <Button
                type="button"
                disabled={create.isPending}
                onClick={() =>
                  void create.mutateAsync(undefined).then(
                    (id) => {
                      notify.success("app.marketing.campaigns.created");
                      router.push(tenantHref(tenant, `/marketing/campaigns/${id}`));
                    },
                    (error: unknown) => notify.error(error),
                  )
                }
              >
                {t("app.marketing.campaigns.createDraft")}
              </Button>
            )}
          </div>
        </CardContent>
      </Card>
    </div>
  );
}

/** A campaign: status, counters and progress while sending, "send now" with the recipient count, cancel, delete, recipients. */
export function CampaignDetail({ tenant, id }: { tenant: string; id: string }) {
  const t = useTranslations();
  const format = useFormatter();
  const router = useRouter();
  const notify = useNotify();
  const confirm = useConfirm();
  const canManage = useCan(MARKETING_PERMISSIONS.manageCampaigns);
  const canSend = useCan(MARKETING_PERMISSIONS.sendCampaigns);
  const campaign = useCampaign(tenant, id);
  const [status, setStatus] = React.useState<string>();
  const [page, setPage] = React.useState(1);
  const [pageSize, setPageSize] = React.useState(25);
  const recipients = useCampaignRecipients(
    tenant,
    id,
    status,
    page,
    pageSize,
    campaign.data !== undefined && campaign.data.status !== "Draft",
  );
  const send = useMarketingMutation(tenant, () => sendCampaign(id));
  const cancel = useMarketingMutation(tenant, () => cancelCampaign(id));
  const remove = useMarketingMutation(tenant, () => deleteCampaign(id));
  const busy = send.isPending || cancel.isPending || remove.isPending;

  if (campaign.error) {
    return <ApiErrorAlert error={campaign.error} onRetry={() => void campaign.refetch()} />;
  }

  if (!campaign.data) {
    return <Skeleton className="h-64 w-full" aria-label={t("Loading")} />;
  }

  const value = campaign.data;
  const progress = campaignProgress(value);
  const onSend = async () => {
    const count = await campaignAudience(id).catch((error: unknown) => {
      notify.error(error);
      return undefined;
    });
    if (
      count !== undefined &&
      (await confirm({
        title: t("app.marketing.campaigns.sendTitle"),
        description: t("app.marketing.campaigns.sendText", { count: Number(count) }),
        confirmLabel: t("app.marketing.campaigns.send"),
      }))
    ) {
      await send.mutateAsync(undefined).then(
        () => notify.success("app.marketing.campaigns.queued"),
        (error: unknown) => notify.error(error),
      );
    }
  };

  const columns: DataTableColumn<CampaignRecipient>[] = [
    {
      id: "name",
      header: t("Name"),
      hideable: false,
      mobile: "title",
      cell: (recipient) => recipient.fullName,
    },
    {
      id: "email",
      header: t("Email"),
      mobile: "detail",
      cell: (recipient) => recipient.email ?? "—",
    },
    {
      id: "status",
      header: t("Status"),
      mobile: "detail",
      cell: (recipient) => (
        <span className="flex flex-col">
          <span>{t(`app.marketing.campaigns.recipientStatus.${recipient.status}`)}</span>
          {recipient.exclusion ? (
            <span className="text-xs text-muted-foreground">
              {t(`app.marketing.campaigns.exclusion.${recipient.exclusion}`)}
            </span>
          ) : null}
          {recipient.errorCode ? (
            <code className="text-xs text-destructive">{recipient.errorCode}</code>
          ) : null}
        </span>
      ),
    },
    {
      id: "processedAt",
      header: t("app.marketing.campaigns.processedAt"),
      cell: (recipient) =>
        recipient.processedAt
          ? format.dateTime(new Date(recipient.processedAt), {
              dateStyle: "short",
              timeStyle: "short",
            })
          : "—",
    },
  ];

  return (
    <div className="flex flex-col gap-4">
      <Button variant="ghost" size="sm" className="self-start" asChild>
        <Link href={tenantHref(tenant, "/marketing/campaigns")}>
          <ArrowLeftIcon aria-hidden /> {t("app.marketing.campaigns.back")}
        </Link>
      </Button>
      <Card>
        <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-3">
          <div className="flex flex-col gap-1.5">
            <CardTitle>
              <h2 className="text-base font-semibold">{value.name}</h2>
            </CardTitle>
            <CardDescription>
              {value.audienceName ?? "—"} · {value.templateName}
            </CardDescription>
          </div>
          <Badge variant={campaignVariant(value.status)}>
            {t(`app.marketing.campaigns.status.${value.status}`)}
          </Badge>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          {value.status !== "Draft" ? (
            <div className="flex flex-col gap-1">
              <div
                role="progressbar"
                aria-label={t("app.marketing.campaigns.progress")}
                aria-valuemin={0}
                aria-valuemax={100}
                aria-valuenow={progress}
                className="h-2 w-full overflow-hidden rounded-full bg-muted"
              >
                <div className="h-full bg-primary" style={{ width: `${progress}%` }} />
              </div>
              <dl className="grid gap-2 text-sm sm:grid-cols-4">
                {(
                  [
                    ["recipients", value.recipientCount],
                    ["sent", value.sentCount],
                    ["excluded", value.excludedCount],
                    ["failed", value.failedCount],
                  ] as const
                ).map(([key, count]) => (
                  <div key={key}>
                    <dt className="text-muted-foreground">
                      {t(`app.marketing.campaigns.count.${key}`)}
                    </dt>
                    <dd className="text-lg font-semibold">{String(count)}</dd>
                  </div>
                ))}
              </dl>
            </div>
          ) : (
            <p className="text-sm text-muted-foreground">
              {t("app.marketing.campaigns.draftHint")}
            </p>
          )}
          {value.errorMessage ? (
            <p className="text-sm text-destructive">
              <code>{value.errorCode}</code> {value.errorMessage}
            </p>
          ) : null}
          <div className="flex flex-wrap gap-2">
            {value.status === "Draft" && canSend ? (
              <Button type="button" disabled={busy} onClick={() => void onSend()}>
                <SendIcon aria-hidden /> {t("app.marketing.campaigns.send")}
              </Button>
            ) : null}
            {(value.status === "Draft" || value.status === "Sending") && canSend ? (
              <Button
                type="button"
                variant="outline"
                disabled={busy}
                onClick={async () => {
                  if (
                    await confirm({
                      description: t("app.marketing.campaigns.cancelText"),
                      confirmLabel: t("app.marketing.campaigns.cancel"),
                      variant: "destructive",
                    })
                  ) {
                    await cancel.mutateAsync(undefined).then(
                      () => notify.success("app.marketing.campaigns.cancelled"),
                      (error: unknown) => notify.error(error),
                    );
                  }
                }}
              >
                <XIcon aria-hidden /> {t("app.marketing.campaigns.cancel")}
              </Button>
            ) : null}
            {value.status !== "Sending" && canManage ? (
              <Button
                type="button"
                variant="ghost"
                disabled={busy}
                onClick={async () => {
                  if (
                    await confirm({
                      description: t("app.marketing.campaigns.deleteText", { name: value.name }),
                      confirmLabel: t("Delete"),
                      variant: "destructive",
                    })
                  ) {
                    await remove.mutateAsync(undefined).then(
                      () => {
                        notify.success("app.marketing.campaigns.deleted");
                        router.push(tenantHref(tenant, "/marketing/campaigns"));
                      },
                      (error: unknown) => notify.error(error),
                    );
                  }
                }}
              >
                {t("Delete")}
              </Button>
            ) : null}
          </div>
        </CardContent>
      </Card>
      {value.status !== "Draft" ? (
        <Card>
          <CardHeader>
            <CardTitle>
              <h2 className="text-base font-semibold">{t("app.marketing.campaigns.recipients")}</h2>
            </CardTitle>
          </CardHeader>
          <CardContent>
            <DataTable
              label={t("app.marketing.campaigns.recipients")}
              columns={columns}
              rows={recipients.data?.items}
              getRowId={(recipient) => recipient.clientId}
              totalCount={Number(recipients.data?.totalCount ?? 0)}
              page={page}
              pageSize={pageSize}
              sort={null}
              onPageChange={setPage}
              onPageSizeChange={(size) => {
                setPageSize(size);
                setPage(1);
              }}
              onSortChange={() => {}}
              isLoading={recipients.isPending}
              error={recipients.error}
              onRetry={() => void recipients.refetch()}
              filtered={status !== undefined}
              toolbar={
                <FilterSelect
                  id="campaign-recipient-status"
                  label={t("Status")}
                  value={status}
                  onChange={(next) => {
                    setStatus(next);
                    setPage(1);
                  }}
                  options={RECIPIENT_STATUSES.map((item) => ({
                    value: item,
                    label: t(`app.marketing.campaigns.recipientStatus.${item}`),
                  }))}
                />
              }
            />
          </CardContent>
        </Card>
      ) : null}
    </div>
  );
}
