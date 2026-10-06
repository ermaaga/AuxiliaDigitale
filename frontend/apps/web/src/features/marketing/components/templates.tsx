"use client";

import * as React from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { isApiError } from "@auxilia/api-client";
import { ArrowLeftIcon, PlusIcon, SendIcon, Trash2Icon } from "lucide-react";
import { useFormatter, useTranslations } from "next-intl";
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
import { Textarea } from "@auxilia/ui/components/textarea";

import { useConfirm } from "@/components/confirm/confirm-provider";
import { DataTable } from "@/components/data-table/data-table";
import type { DataTableColumn } from "@/components/data-table/table-model";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import type { LanguageOption } from "@/components/shell/language-switcher";
import { tenantHref } from "@/lib/href";
import { useNotify } from "@/lib/notify";
import { useCan } from "@/lib/permissions";

import {
  addSuppression,
  deleteTemplate,
  removeSuppression,
  saveTemplate,
  testTemplate,
  useEmailTemplate,
  useEmailTemplates,
  useMarketingMutation,
  useSuppressions,
  useTemplatePreview,
  type EmailTemplateListItem,
  type Suppression,
} from "../api";
import { MARKETING_PERMISSIONS } from "../permissions";
import { EmailPreview } from "./email-preview";

/** Placeholders a template can use (`CampaignModel` in the API). */
export const PLACEHOLDERS = ["firstName", "lastName", "fullName", "tenantName"] as const;

/** The e-mail templates of the campaigns (N01). */
export function TemplatesPage({ tenant }: { tenant: string }) {
  const t = useTranslations();
  const format = useFormatter();
  const canManage = useCan(MARKETING_PERMISSIONS.manageCampaigns);
  const templates = useEmailTemplates(tenant);
  const columns: DataTableColumn<EmailTemplateListItem>[] = [
    {
      id: "name",
      header: t("Name"),
      hideable: false,
      mobile: "title",
      cell: (template) => (
        <Link
          href={tenantHref(tenant, `/marketing/templates/${template.id}`)}
          className="font-medium underline-offset-4 hover:underline"
        >
          {template.name}
        </Link>
      ),
    },
    { id: "subject", header: t("Subject"), mobile: "detail", cell: (template) => template.subject },
    { id: "language", header: t("Language"), cell: (template) => template.language.toUpperCase() },
    {
      id: "campaigns",
      header: t("app.marketing.templates.campaigns"),
      cell: (template) => String(template.campaignCount),
    },
    {
      id: "updatedAt",
      header: t("app.marketing.updatedAt"),
      cell: (template) => format.dateTime(new Date(template.updatedAt), { dateStyle: "medium" }),
    },
  ];

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-sm text-muted-foreground">{t("app.marketing.templates.description")}</p>
        {canManage ? (
          <Button asChild>
            <Link href={tenantHref(tenant, "/marketing/templates/new")}>
              <PlusIcon aria-hidden /> {t("app.marketing.templates.new")}
            </Link>
          </Button>
        ) : null}
      </div>
      <DataTable
        label={t("app.marketing.nav.templates")}
        columns={columns}
        rows={templates.data}
        getRowId={(template) => template.id}
        totalCount={templates.data?.length ?? 0}
        page={1}
        pageSize={100}
        sort={null}
        onPageChange={() => {}}
        onPageSizeChange={() => {}}
        onSortChange={() => {}}
        isLoading={templates.isPending}
        error={templates.error}
        onRetry={() => void templates.refetch()}
        hidePaging
      />
    </div>
  );
}

/** A template: name, language, subject and HTML body with placeholders; preview (sandboxed) and test send once saved. */
export function TemplateEditor({
  tenant,
  id,
  languages,
}: {
  tenant: string;
  id?: string;
  languages: readonly LanguageOption[];
}) {
  const t = useTranslations();
  const template = useEmailTemplate(tenant, id);
  if (id && template.error) {
    return <ApiErrorAlert error={template.error} onRetry={() => void template.refetch()} />;
  }

  if (id && !template.data) {
    return <Skeleton className="h-96 w-full" aria-label={t("Loading")} />;
  }

  return (
    <TemplateForm
      key={template.data?.id ?? "new"}
      tenant={tenant}
      id={id}
      languages={languages}
      initial={
        template.data ?? { name: "", language: languages[0]?.code ?? "it", subject: "", body: "" }
      }
    />
  );
}

function TemplateForm({
  tenant,
  id,
  languages,
  initial,
}: {
  tenant: string;
  id?: string;
  languages: readonly LanguageOption[];
  initial: { name: string; language: string; subject: string; body: string };
}) {
  const t = useTranslations();
  const router = useRouter();
  const notify = useNotify();
  const confirm = useConfirm();
  const canManage = useCan(MARKETING_PERMISSIONS.manageCampaigns);
  const [values, setValues] = React.useState(initial);
  const [version, setVersion] = React.useState(0);
  const [testEmail, setTestEmail] = React.useState("");
  const preview = useTemplatePreview(tenant, id, version);
  const save = useMarketingMutation(tenant, () => saveTemplate(id, values));
  const remove = useMarketingMutation(tenant, () => deleteTemplate(id!));
  const test = useMarketingMutation(tenant, () => testTemplate(id!, testEmail.trim()));
  const errors = isApiError(save.error) ? save.error.fieldErrors : {};
  const set = (change: Partial<typeof values>) =>
    setValues((current) => ({ ...current, ...change }));
  const fieldError = (field: string) =>
    errors[field]?.[0] ? (
      <p id={`template-${field}-error`} className="text-sm text-destructive">
        {t(errors[field]![0]!)}
      </p>
    ) : null;

  const onSave = async () => {
    await save.mutateAsync(undefined).then(
      (savedId) => {
        notify.success("app.marketing.templates.saved");
        setVersion((current) => current + 1);
        if (!id) {
          router.push(tenantHref(tenant, `/marketing/templates/${savedId}`));
        }
      },
      (error: unknown) => {
        if (!(isApiError(error) && Object.keys(error.fieldErrors).length > 0)) {
          notify.error(error);
        }
      },
    );
  };

  return (
    <div className="flex flex-col gap-4">
      <Button variant="ghost" size="sm" className="self-start" asChild>
        <Link href={tenantHref(tenant, "/marketing/templates")}>
          <ArrowLeftIcon aria-hidden /> {t("app.marketing.templates.back")}
        </Link>
      </Button>
      <div className="grid gap-4 lg:grid-cols-2">
        <Card>
          <CardContent className="flex flex-col gap-3 pt-6">
            <div className="grid gap-3 sm:grid-cols-[1fr_10rem]">
              <div className="flex flex-col gap-1">
                <Label htmlFor="template-name">{t("Name")} *</Label>
                <Input
                  id="template-name"
                  value={values.name}
                  maxLength={100}
                  disabled={!canManage}
                  aria-invalid={errors.name ? true : undefined}
                  aria-describedby={errors.name ? "template-name-error" : undefined}
                  onChange={(event) => set({ name: event.target.value })}
                />
                {fieldError("name")}
              </div>
              <div className="flex flex-col gap-1">
                <Label htmlFor="template-language">{t("Language")} *</Label>
                <Select
                  value={values.language}
                  disabled={!canManage}
                  onValueChange={(language) => set({ language })}
                >
                  <SelectTrigger id="template-language">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {(languages.length > 0
                      ? languages
                      : [{ code: values.language, name: values.language }]
                    ).map((language) => (
                      <SelectItem key={language.code} value={language.code}>
                        {language.name}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            </div>
            <div className="flex flex-col gap-1">
              <Label htmlFor="template-subject">{t("Subject")} *</Label>
              <Input
                id="template-subject"
                value={values.subject}
                maxLength={300}
                disabled={!canManage}
                aria-invalid={errors.subject ? true : undefined}
                aria-describedby={errors.subject ? "template-subject-error" : undefined}
                onChange={(event) => set({ subject: event.target.value })}
              />
              {fieldError("subject")}
            </div>
            <div className="flex flex-col gap-1">
              <Label htmlFor="template-body">{t("app.marketing.templates.body")} *</Label>
              <Textarea
                id="template-body"
                rows={14}
                className="font-mono text-sm"
                value={values.body}
                disabled={!canManage}
                aria-invalid={errors.body ? true : undefined}
                aria-describedby={errors.body ? "template-body-error" : "template-placeholders"}
                onChange={(event) => set({ body: event.target.value })}
              />
              {fieldError("body")}
              <p id="template-placeholders" className="text-xs text-muted-foreground">
                {t("app.marketing.templates.placeholders")}{" "}
                {PLACEHOLDERS.map((name) => `{{ ${name} }}`).join(" · ")}
              </p>
            </div>
            {canManage ? (
              <div className="flex flex-wrap justify-between gap-2">
                {id ? (
                  <Button
                    type="button"
                    variant="outline"
                    disabled={remove.isPending}
                    onClick={async () => {
                      if (
                        await confirm({
                          description: t("app.marketing.templates.deleteText", {
                            name: initial.name,
                          }),
                          confirmLabel: t("Delete"),
                          variant: "destructive",
                        })
                      ) {
                        await remove.mutateAsync(undefined).then(
                          () => {
                            notify.success("app.marketing.templates.deleted");
                            router.push(tenantHref(tenant, "/marketing/templates"));
                          },
                          (error: unknown) => notify.error(error),
                        );
                      }
                    }}
                  >
                    <Trash2Icon aria-hidden /> {t("Delete")}
                  </Button>
                ) : (
                  <span />
                )}
                <Button
                  type="button"
                  disabled={save.isPending || !values.name.trim()}
                  onClick={() => void onSave()}
                >
                  {t("Save")}
                </Button>
              </div>
            ) : null}
          </CardContent>
        </Card>
        <Card className="self-start">
          <CardHeader>
            <CardTitle>
              <h2 className="text-base font-semibold">{t("app.marketing.templates.preview")}</h2>
            </CardTitle>
            <CardDescription>
              {id
                ? t("app.marketing.templates.previewHint")
                : t("app.marketing.templates.saveFirst")}
            </CardDescription>
          </CardHeader>
          <CardContent className="flex flex-col gap-3">
            {preview.error ? (
              <ApiErrorAlert error={preview.error} />
            ) : preview.data ? (
              <EmailPreview subject={preview.data.subject} body={preview.data.body} />
            ) : id ? (
              <Skeleton className="h-80 w-full" />
            ) : null}
            {id && canManage ? (
              <form
                className="flex flex-wrap items-end gap-2"
                onSubmit={(event) => {
                  event.preventDefault();
                  void test.mutateAsync(undefined).then(
                    () => notify.success("app.marketing.templates.testSent"),
                    (error: unknown) => notify.error(error),
                  );
                }}
              >
                <div className="flex flex-col gap-1">
                  <Label htmlFor="template-test">{t("app.marketing.templates.testEmail")}</Label>
                  <Input
                    id="template-test"
                    type="email"
                    className="w-64"
                    value={testEmail}
                    onChange={(event) => setTestEmail(event.target.value)}
                  />
                </div>
                <Button
                  type="submit"
                  variant="outline"
                  disabled={!testEmail.trim() || test.isPending}
                >
                  <SendIcon aria-hidden /> {t("app.marketing.templates.test")}
                </Button>
              </form>
            ) : null}
          </CardContent>
        </Card>
      </div>
    </div>
  );
}

/** Addresses marketing never writes to (N01; unsubscribe page deferred, D-24). */
export function SuppressionsPage({ tenant }: { tenant: string }) {
  const t = useTranslations();
  const format = useFormatter();
  const notify = useNotify();
  const canManage = useCan(MARKETING_PERMISSIONS.manageCampaigns);
  const suppressions = useSuppressions(tenant);
  const [email, setEmail] = React.useState("");
  const [reason, setReason] = React.useState("");
  const add = useMarketingMutation(tenant, () =>
    addSuppression(email.trim(), reason.trim() || null),
  );
  const remove = useMarketingMutation(tenant, (id: string) => removeSuppression(id));
  const emailError = isApiError(add.error) ? add.error.fieldErrors.email?.[0] : undefined;
  const columns: DataTableColumn<Suppression>[] = [
    {
      id: "email",
      header: t("Email"),
      hideable: false,
      mobile: "title",
      cell: (item) => item.email,
    },
    {
      id: "reason",
      header: t("app.marketing.suppressions.reason"),
      mobile: "detail",
      cell: (item) => item.reason ?? "—",
    },
    {
      id: "suppressedAt",
      header: t("app.marketing.suppressions.since"),
      cell: (item) => format.dateTime(new Date(item.suppressedAt), { dateStyle: "medium" }),
    },
    {
      id: "actions",
      header: t("Actions"),
      hideable: false,
      cell: (item) =>
        canManage ? (
          <Button
            type="button"
            variant="ghost"
            size="sm"
            disabled={remove.isPending}
            aria-label={t("app.marketing.suppressions.removeNamed", { email: item.email })}
            onClick={() =>
              void remove.mutateAsync(item.id).then(
                () => notify.success("app.marketing.suppressions.removed"),
                (error: unknown) => notify.error(error),
              )
            }
          >
            {t("app.marketing.suppressions.remove")}
          </Button>
        ) : null,
    },
  ];

  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm text-muted-foreground">{t("app.marketing.suppressions.description")}</p>
      {canManage ? (
        <form
          className="flex flex-wrap items-end gap-2"
          onSubmit={(event) => {
            event.preventDefault();
            void add.mutateAsync(undefined).then(
              () => {
                setEmail("");
                setReason("");
                notify.success("app.marketing.suppressions.added");
              },
              (error: unknown) => {
                if (!(isApiError(error) && Object.keys(error.fieldErrors).length > 0)) {
                  notify.error(error);
                }
              },
            );
          }}
        >
          <div className="flex flex-col gap-1">
            <Label htmlFor="suppression-email">{t("Email")}</Label>
            <Input
              id="suppression-email"
              type="email"
              className="w-64"
              value={email}
              aria-invalid={emailError ? true : undefined}
              aria-describedby={emailError ? "suppression-email-error" : undefined}
              onChange={(event) => setEmail(event.target.value)}
            />
          </div>
          <div className="flex flex-col gap-1">
            <Label htmlFor="suppression-reason">{t("app.marketing.suppressions.reason")}</Label>
            <Input
              id="suppression-reason"
              className="w-64"
              maxLength={200}
              value={reason}
              onChange={(event) => setReason(event.target.value)}
            />
          </div>
          <Button type="submit" disabled={!email.trim() || add.isPending}>
            <PlusIcon aria-hidden /> {t("app.marketing.suppressions.add")}
          </Button>
          {emailError ? (
            <p id="suppression-email-error" className="w-full text-sm text-destructive">
              {t(emailError)}
            </p>
          ) : null}
        </form>
      ) : null}
      <DataTable
        label={t("app.marketing.nav.suppressions")}
        columns={columns}
        rows={suppressions.data}
        getRowId={(item) => item.id}
        totalCount={suppressions.data?.length ?? 0}
        page={1}
        pageSize={100}
        sort={null}
        onPageChange={() => {}}
        onPageSizeChange={() => {}}
        onSortChange={() => {}}
        isLoading={suppressions.isPending}
        error={suppressions.error}
        onRetry={() => void suppressions.refetch()}
        hidePaging
      />
    </div>
  );
}
