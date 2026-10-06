"use client";

import * as React from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { isApiError } from "@auxilia/api-client";
import { ArrowLeftIcon, PlusIcon, Trash2Icon } from "lucide-react";
import { useTranslations } from "next-intl";
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
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { useActiveServices } from "@/features/cases";
import {
  useAssignableEmployees,
  useClientCustomFields,
  useClientSpecializations,
  useTags,
} from "@/features/clients";
import { tenantHref } from "@/lib/href";
import { useNotify } from "@/lib/notify";
import { useCan } from "@/lib/permissions";

import {
  deleteSegment,
  saveSegment,
  useMarketingMutation,
  useSegment,
  useSegmentFields,
  useSegmentPreview,
  type SegmentRule,
} from "../api";
import { MARKETING_PERMISSIONS } from "../permissions";
import {
  CASE_STATUSES,
  CLIENT_STATUSES,
  VALUE_KINDS,
  emptyCondition,
  emptyRule,
  fromRule,
  toRule,
  type ConditionDraft,
  type GroupDraft,
  type RuleDraft,
} from "../rule";

export const SEGMENT_NAME_MAX = 100;

/** A segment (N01): name, description and the rule, with the live count of the clients it selects. */
export function SegmentBuilder({ tenant, id }: { tenant: string; id?: string }) {
  const t = useTranslations();
  const segment = useSegment(tenant, id);
  if (id && segment.error) {
    return <ApiErrorAlert error={segment.error} onRetry={() => void segment.refetch()} />;
  }

  if (id && !segment.data) {
    return <Skeleton className="h-96 w-full" aria-label={t("Loading")} />;
  }

  return (
    <SegmentForm
      key={segment.data?.id ?? "new"}
      tenant={tenant}
      id={id}
      initial={{
        name: segment.data?.name ?? "",
        description: segment.data?.description ?? "",
        rule: segment.data ? fromRule(segment.data.rule) : emptyRule(),
      }}
    />
  );
}

function SegmentForm({
  tenant,
  id,
  initial,
}: {
  tenant: string;
  id?: string;
  initial: { name: string; description: string; rule: RuleDraft };
}) {
  const t = useTranslations();
  const router = useRouter();
  const notify = useNotify();
  const confirm = useConfirm();
  const canManage = useCan(MARKETING_PERMISSIONS.manageAudiences);
  const [name, setName] = React.useState(initial.name);
  const [description, setDescription] = React.useState(initial.description);
  const [draft, setDraft] = React.useState(initial.rule);
  const [debounced, setDebounced] = React.useState<SegmentRule | undefined>(() =>
    toRule(initial.rule),
  );
  const preview = useSegmentPreview(tenant, debounced);
  const save = useMarketingMutation(tenant, (rule: SegmentRule) =>
    saveSegment(id, { name: name.trim(), description: description.trim() || null, rule }),
  );
  const remove = useMarketingMutation(tenant, () => deleteSegment(id!));
  const rule = toRule(draft);

  React.useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(toRule(draft)), 500);
    return () => window.clearTimeout(timer);
  }, [draft]);

  const fieldErrors = isApiError(save.error) ? save.error.fieldErrors : {};
  const onSave = async () => {
    if (!rule) {
      return;
    }

    await save.mutateAsync(rule).then(
      (savedId) => {
        notify.success("app.marketing.segments.saved");
        if (!id) {
          router.push(tenantHref(tenant, `/marketing/segments/${savedId}`));
        }
      },
      (error: unknown) => {
        if (!(isApiError(error) && Object.keys(error.fieldErrors).length > 0)) {
          notify.error(error);
        }
      },
    );
  };

  const onDelete = async () => {
    if (
      await confirm({
        description: t("app.marketing.segments.deleteText", { name: initial.name }),
        confirmLabel: t("Delete"),
        variant: "destructive",
      })
    ) {
      await remove.mutateAsync(undefined).then(
        () => {
          notify.success("app.marketing.segments.deleted");
          router.push(tenantHref(tenant, "/marketing/segments"));
        },
        (error: unknown) => notify.error(error),
      );
    }
  };

  return (
    <div className="flex flex-col gap-4">
      <Button variant="ghost" size="sm" className="self-start" asChild>
        <Link href={tenantHref(tenant, "/marketing/segments")}>
          <ArrowLeftIcon aria-hidden /> {t("app.marketing.segments.back")}
        </Link>
      </Button>
      <div className="grid gap-4 lg:grid-cols-[1fr_20rem]">
        <div className="flex flex-col gap-4">
          <Card>
            <CardContent className="grid gap-3 pt-6 sm:grid-cols-2">
              <div className="flex flex-col gap-1">
                <Label htmlFor="segment-name">{t("Name")} *</Label>
                <Input
                  id="segment-name"
                  value={name}
                  maxLength={SEGMENT_NAME_MAX}
                  disabled={!canManage}
                  aria-invalid={fieldErrors.name ? true : undefined}
                  aria-describedby={fieldErrors.name ? "segment-name-error" : undefined}
                  onChange={(event) => setName(event.target.value)}
                />
                {fieldErrors.name ? (
                  <p id="segment-name-error" className="text-sm text-destructive">
                    {t(fieldErrors.name[0]!)}
                  </p>
                ) : null}
              </div>
              <div className="flex flex-col gap-1">
                <Label htmlFor="segment-description">{t("Description")}</Label>
                <Textarea
                  id="segment-description"
                  rows={1}
                  maxLength={500}
                  disabled={!canManage}
                  value={description}
                  onChange={(event) => setDescription(event.target.value)}
                />
              </div>
            </CardContent>
          </Card>
          <RuleEditor
            tenant={tenant}
            draft={draft}
            onChange={setDraft}
            disabled={!canManage}
            errors={fieldErrors}
          />
          {canManage ? (
            <div className="flex flex-wrap justify-between gap-2">
              {id ? (
                <Button
                  type="button"
                  variant="outline"
                  disabled={remove.isPending}
                  onClick={() => void onDelete()}
                >
                  <Trash2Icon aria-hidden /> {t("Delete")}
                </Button>
              ) : (
                <span />
              )}
              <Button
                type="button"
                disabled={!rule || !name.trim() || save.isPending}
                onClick={() => void onSave()}
              >
                {t("Save")}
              </Button>
            </div>
          ) : null}
        </div>
        <Card className="self-start">
          <CardHeader>
            <CardTitle>
              <h2 className="text-base font-semibold">{t("app.marketing.segments.preview")}</h2>
            </CardTitle>
            <CardDescription>{t("app.marketing.segments.previewHint")}</CardDescription>
          </CardHeader>
          <CardContent className="flex flex-col gap-2" aria-live="polite">
            {!rule ? (
              <p className="text-sm text-muted-foreground">
                {t("app.marketing.segments.incomplete")}
              </p>
            ) : preview.error ? (
              <ApiErrorAlert error={preview.error} />
            ) : !preview.data ? (
              <Skeleton className="h-16 w-full" />
            ) : (
              <>
                <p className="text-2xl font-semibold">
                  {t("app.marketing.segments.count", { count: Number(preview.data.count) })}
                </p>
                <ul className="flex flex-col gap-1 text-sm">
                  {preview.data.sample.map((member) => (
                    <li key={member.id}>
                      <Link
                        href={tenantHref(tenant, `/clients/${member.id}`)}
                        className="underline-offset-4 hover:underline"
                      >
                        {member.fullName}
                      </Link>
                      {member.email ? (
                        <span className="text-muted-foreground"> · {member.email}</span>
                      ) : null}
                    </li>
                  ))}
                </ul>
              </>
            )}
          </CardContent>
        </Card>
      </div>
    </div>
  );
}

/** The conditions of the rule and its groups, joined by "all" (AND) or "any" (OR). */
function RuleEditor({
  tenant,
  draft,
  onChange,
  disabled,
  errors,
}: {
  tenant: string;
  draft: RuleDraft;
  onChange: (draft: RuleDraft) => void;
  disabled: boolean;
  errors: Record<string, readonly string[]>;
}) {
  const t = useTranslations();
  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2 className="text-base font-semibold">{t("app.marketing.segments.rule")}</h2>
        </CardTitle>
        <CardDescription>{t("app.marketing.segments.ruleHint")}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {errors.rule ? <p className="text-sm text-destructive">{t(errors.rule[0]!)}</p> : null}
        <Conditions
          tenant={tenant}
          id="rule"
          match={draft.match}
          conditions={draft.conditions}
          disabled={disabled}
          errors={errors}
          errorPrefix="rule.conditions"
          onChange={(match, conditions) => onChange({ ...draft, match, conditions })}
        />
        {draft.groups.map((group, index) => (
          <fieldset key={index} className="flex flex-col gap-3 rounded-md border p-3">
            <legend className="px-1 text-sm font-medium">
              {t("app.marketing.segments.group", { number: index + 1 })}
            </legend>
            <Conditions
              tenant={tenant}
              id={`group-${index}`}
              match={group.match}
              conditions={group.conditions}
              disabled={disabled}
              errors={errors}
              errorPrefix={`rule.groups[${index}].conditions`}
              onChange={(match, conditions) =>
                onChange({
                  ...draft,
                  groups: draft.groups.map((item, position) =>
                    position === index ? ({ match, conditions } as GroupDraft) : item,
                  ),
                })
              }
            />
            {!disabled ? (
              <Button
                type="button"
                variant="ghost"
                size="sm"
                className="self-start"
                onClick={() =>
                  onChange({
                    ...draft,
                    groups: draft.groups.filter((_, position) => position !== index),
                  })
                }
              >
                <Trash2Icon aria-hidden /> {t("app.marketing.segments.removeGroup")}
              </Button>
            ) : null}
          </fieldset>
        ))}
        {!disabled ? (
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="self-start"
            onClick={() =>
              onChange({
                ...draft,
                groups: [...draft.groups, { match: "any", conditions: [emptyCondition()] }],
              })
            }
          >
            <PlusIcon aria-hidden /> {t("app.marketing.segments.addGroup")}
          </Button>
        ) : null}
      </CardContent>
    </Card>
  );
}

function Conditions({
  tenant,
  id,
  match,
  conditions,
  disabled,
  errors,
  errorPrefix,
  onChange,
}: {
  tenant: string;
  id: string;
  match: "all" | "any";
  conditions: ConditionDraft[];
  disabled: boolean;
  errors: Record<string, readonly string[]>;
  errorPrefix: string;
  onChange: (match: "all" | "any", conditions: ConditionDraft[]) => void;
}) {
  const t = useTranslations();
  const fields = useSegmentFields(tenant);
  const update = (index: number, change: Partial<ConditionDraft>) =>
    onChange(
      match,
      conditions.map((condition, position) =>
        position === index ? { ...condition, ...change } : condition,
      ),
    );

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-2 text-sm">
        <Label htmlFor={`${id}-match`}>{t("app.marketing.segments.match")}</Label>
        <Select
          value={match}
          disabled={disabled}
          onValueChange={(value) => onChange(value as "all" | "any", conditions)}
        >
          <SelectTrigger id={`${id}-match`} className="w-56">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">{t("app.marketing.segments.matchAll")}</SelectItem>
            <SelectItem value="any">{t("app.marketing.segments.matchAny")}</SelectItem>
          </SelectContent>
        </Select>
      </div>
      <ul className="flex flex-col gap-2">
        {conditions.map((condition, index) => {
          const operators =
            fields.data?.find((field) => field.field === condition.field)?.operators ?? [];
          const error = (["field", "op", "value", "key"] as const)
            .map((part) => errors[`${errorPrefix}[${index}].${part}`]?.[0])
            .find((value) => value !== undefined);
          return (
            <li key={index} className="flex flex-col gap-1">
              <div className="flex flex-wrap items-end gap-2">
                <Select
                  value={condition.field}
                  disabled={disabled}
                  onValueChange={(field) =>
                    update(index, {
                      field,
                      op: fields.data?.find((item) => item.field === field)?.operators[0] ?? "is",
                      value: "",
                      key: "",
                    })
                  }
                >
                  <SelectTrigger className="w-44" aria-label={t("app.marketing.segments.field")}>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {(fields.data ?? []).map((field) => (
                      <SelectItem key={field.field} value={field.field}>
                        {t(`app.marketing.segments.fields.${field.field}`)}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <Select
                  value={condition.op}
                  disabled={disabled}
                  onValueChange={(op) =>
                    update(index, { op, value: op === "none" ? "" : condition.value })
                  }
                >
                  <SelectTrigger className="w-40" aria-label={t("app.marketing.segments.operator")}>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {operators.map((op) => (
                      <SelectItem key={op} value={op}>
                        {t(`app.marketing.segments.operators.${op}`)}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                {condition.op !== "none" ? (
                  <ConditionValue
                    tenant={tenant}
                    condition={condition}
                    disabled={disabled}
                    onChange={(change) => update(index, change)}
                  />
                ) : null}
                {!disabled && conditions.length > 1 ? (
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    aria-label={t("app.marketing.segments.removeCondition")}
                    onClick={() =>
                      onChange(
                        match,
                        conditions.filter((_, position) => position !== index),
                      )
                    }
                  >
                    <Trash2Icon aria-hidden />
                  </Button>
                ) : null}
              </div>
              {error ? <p className="text-sm text-destructive">{t(error)}</p> : null}
            </li>
          );
        })}
      </ul>
      {!disabled ? (
        <Button
          type="button"
          variant="ghost"
          size="sm"
          className="self-start"
          onClick={() => onChange(match, [...conditions, emptyCondition()])}
        >
          <PlusIcon aria-hidden /> {t("app.marketing.segments.addCondition")}
        </Button>
      ) : null}
    </div>
  );
}

/** The value of a condition, by the kind of its field. */
function ConditionValue({
  tenant,
  condition,
  disabled,
  onChange,
}: {
  tenant: string;
  condition: ConditionDraft;
  disabled: boolean;
  onChange: (change: Partial<ConditionDraft>) => void;
}) {
  const t = useTranslations();
  const kind = VALUE_KINDS[condition.field];
  const employees = useAssignableEmployees(tenant, kind === "employee");
  const tags = useTags(tenant, kind === "tag");
  const specializations = useClientSpecializations(tenant, kind === "specialization");
  const services = useActiveServices(tenant, "", kind === "service");
  const customFields = useClientCustomFields(tenant);
  const label = t("app.marketing.segments.value");

  const choose = (options: readonly { value: string; label: string }[]) => (
    <Select
      value={condition.value}
      disabled={disabled}
      onValueChange={(value) => onChange({ value })}
    >
      <SelectTrigger className="w-56" aria-label={label}>
        <SelectValue placeholder={t("app.marketing.segments.chooseValue")} />
      </SelectTrigger>
      <SelectContent>
        {options.map((option) => (
          <SelectItem key={option.value} value={option.value}>
            {option.label}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );

  switch (kind) {
    case "status":
      return choose(CLIENT_STATUSES.map((status) => ({ value: status, label: t(status) })));
    case "caseStatus":
      return choose(CASE_STATUSES.map((status) => ({ value: status, label: t(status) })));
    case "employee":
      return choose(
        (employees.data ?? []).map((employee) => ({
          value: employee.userId,
          label: employee.fullName,
        })),
      );
    case "tag":
      return choose((tags.data ?? []).map((tag) => ({ value: tag.id, label: tag.name })));
    case "specialization":
      return choose(
        (specializations.data ?? []).map((specialization) => ({
          value: specialization.id,
          label: specialization.name,
        })),
      );
    case "service":
      return choose(
        (services.data?.items ?? []).map((service) => ({ value: service.id, label: service.name })),
      );
    case "number":
      return (
        <Input
          type="number"
          min={0}
          max={130}
          className="w-28"
          aria-label={label}
          disabled={disabled}
          value={condition.value}
          onChange={(event) => onChange({ value: event.target.value })}
        />
      );
    case "date":
      return (
        <Input
          type="date"
          className="w-44"
          aria-label={label}
          disabled={disabled}
          value={condition.value}
          onChange={(event) => onChange({ value: event.target.value })}
        />
      );
    default:
      return (
        <>
          <Select
            value={condition.key}
            disabled={disabled}
            onValueChange={(key) => onChange({ key })}
          >
            <SelectTrigger className="w-48" aria-label={t("app.marketing.segments.customField")}>
              <SelectValue placeholder={t("app.marketing.segments.chooseField")} />
            </SelectTrigger>
            <SelectContent>
              {customFields.definitions.map((definition) => (
                <SelectItem key={definition.key} value={definition.key}>
                  {definition.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          {customFields.definitions.find((definition) => definition.key === condition.key)?.type ===
          "Boolean" ? (
            choose([
              { value: "true", label: t("Yes") },
              { value: "false", label: t("No") },
            ])
          ) : (
            <Input
              className="w-44"
              aria-label={label}
              disabled={disabled}
              value={condition.value}
              onChange={(event) => onChange({ value: event.target.value })}
            />
          )}
        </>
      );
  }
}
