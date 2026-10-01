"use client";

import * as React from "react";
import { PlusIcon } from "lucide-react";
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
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@auxilia/ui/components/dialog";
import { Input } from "@auxilia/ui/components/input";
import { Label } from "@auxilia/ui/components/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@auxilia/ui/components/select";
import { Switch } from "@auxilia/ui/components/switch";
import { Textarea } from "@auxilia/ui/components/textarea";

import { useConfirm } from "@/components/confirm/confirm-provider";
import { badgeStyle } from "@/components/custom-fields/custom-field-value";
import { DataTable } from "@/components/data-table/data-table";
import type { DataTableColumn } from "@/components/data-table/table-model";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { FormField, applyApiErrors, useZodForm } from "@/components/forms/form";
import { useNotify } from "@/lib/notify";

import {
  CUSTOM_FIELD_TYPES,
  createCustomField,
  deleteCustomField,
  updateCustomField,
  useCustomFieldEntities,
  useCustomFields,
  useCustomizationMutation,
  type CustomField,
} from "../../customization-api";
import {
  customFieldBody,
  customFieldSchema,
  type CustomFieldValues,
} from "../../schemas/custom-field";

/**
 * Custom fields of the tenant's entities (F20, D-18): which extra values clients, cases… carry, how they show in grids
 * (group, badge colour) and whether a boolean is counted on the dashboards.
 */
export function TenantCustomFields({ slug }: { slug: string }) {
  const t = useTranslations();
  const notify = useNotify();
  const confirm = useConfirm();
  const entities = useCustomFieldEntities(slug);
  const [entity, setEntity] = React.useState<string>();
  const current =
    entity ??
    entities.data?.find((item) => item.code === "client")?.code ??
    entities.data?.[0]?.code;
  const fields = useCustomFields(slug, current);
  const [editing, setEditing] = React.useState<CustomField | "new">();
  const remove = useCustomizationMutation(slug, (id: string) => deleteCustomField(slug, id));
  const entityName = (code: string) =>
    t.has(`customFields.entities.${code}`) ? t(`customFields.entities.${code}`) : code;

  const columns: DataTableColumn<CustomField>[] = [
    {
      id: "label",
      header: t("app.platform.customFields.label"),
      hideable: false,
      mobile: "title",
      cell: (field) => (
        <span className="flex flex-col">
          <span className="font-medium">{field.label}</span>
          <code className="text-xs text-muted-foreground">{field.key}</code>
        </span>
      ),
    },
    {
      id: "type",
      header: t("app.platform.customFields.type"),
      cell: (field) => t(`app.platform.customFields.types.${field.type}`),
    },
    {
      id: "group",
      header: t("app.platform.customFields.group"),
      cell: (field) =>
        field.groupName ? (
          <Badge variant="outline" style={badgeStyle(field.badgeColor)}>
            {field.groupName}
          </Badge>
        ) : (
          "—"
        ),
    },
    {
      id: "flags",
      header: t("app.platform.customFields.flags"),
      cell: (field) => (
        <span className="flex flex-wrap gap-1">
          {field.isRequired ? (
            <Badge variant="secondary">{t("app.platform.customFields.required")}</Badge>
          ) : null}
          {field.visibleOnGrid ? (
            <Badge variant="secondary">{t("app.platform.customFields.onGrid")}</Badge>
          ) : null}
          {field.dashboardCounter ? (
            <Badge variant="secondary">{t("app.platform.customFields.counter")}</Badge>
          ) : null}
        </span>
      ),
    },
    {
      id: "order",
      header: t("app.platform.customFields.order"),
      cell: (field) => String(field.order),
    },
    {
      id: "actions",
      header: t("Actions"),
      hideable: false,
      cell: (field) => (
        <div className="flex flex-wrap gap-1">
          <Button
            variant="outline"
            size="sm"
            onClick={() => setEditing(field)}
            aria-label={t("app.platform.customFields.editNamed", { field: field.label })}
          >
            {t("Edit")}
          </Button>
          <Button
            variant="ghost"
            size="sm"
            disabled={remove.isPending}
            aria-label={t("app.platform.customFields.deleteNamed", { field: field.label })}
            onClick={async () => {
              if (
                await confirm({
                  title: t("app.platform.customFields.deleteTitle"),
                  description: t("app.platform.customFields.deleteText", { field: field.label }),
                  confirmLabel: t("Delete"),
                  variant: "destructive",
                })
              ) {
                await remove.mutateAsync(field.id).then(
                  () => notify.success("app.platform.customFields.deleted"),
                  (error: unknown) => notify.error(error),
                );
              }
            }}
          >
            {t("Delete")}
          </Button>
        </div>
      ),
    },
  ];

  return (
    <Card>
      <CardHeader className="flex flex-row flex-wrap items-end justify-between gap-3">
        <div className="flex flex-col gap-1.5">
          <CardTitle>
            <h2 className="text-base font-semibold">{t("app.platform.customFields.fieldsOf")}</h2>
          </CardTitle>
          <CardDescription>{t("app.platform.customFields.fieldsDescription")}</CardDescription>
        </div>
        <div className="flex flex-wrap items-end gap-2">
          <div className="flex flex-col gap-1">
            <Label htmlFor="custom-field-entity">{t("app.platform.customFields.entity")}</Label>
            <Select value={current ?? ""} onValueChange={setEntity}>
              <SelectTrigger id="custom-field-entity" className="w-48">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {(entities.data ?? []).map((item) => (
                  <SelectItem key={item.code} value={item.code}>
                    {entityName(item.code)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <Button size="sm" disabled={!current} onClick={() => setEditing("new")}>
            <PlusIcon aria-hidden /> {t("app.platform.customFields.new")}
          </Button>
        </div>
      </CardHeader>
      <CardContent>
        {entities.error ? (
          <ApiErrorAlert error={entities.error} onRetry={() => void entities.refetch()} />
        ) : null}
        <DataTable
          label={t("app.platform.customFields.fieldsOf")}
          columns={columns}
          rows={fields.data}
          getRowId={(field) => field.id}
          totalCount={fields.data?.length ?? 0}
          page={1}
          pageSize={100}
          sort={null}
          onPageChange={() => {}}
          onPageSizeChange={() => {}}
          onSortChange={() => {}}
          isLoading={fields.isPending}
          error={fields.error}
          onRetry={() => void fields.refetch()}
          hidePaging
        />
        {editing && current ? (
          <CustomFieldDialog
            slug={slug}
            entityType={current}
            entityName={entityName(current)}
            field={editing === "new" ? undefined : editing}
            nextOrder={(fields.data?.length ?? 0) + 1}
            onClose={() => setEditing(undefined)}
          />
        ) : null}
      </CardContent>
    </Card>
  );
}

function CustomFieldDialog({
  slug,
  entityType,
  entityName,
  field,
  nextOrder,
  onClose,
}: {
  slug: string;
  entityType: string;
  entityName: string;
  field?: CustomField;
  nextOrder: number;
  onClose: () => void;
}) {
  const t = useTranslations();
  const notify = useNotify();
  const form = useZodForm(customFieldSchema, {
    defaultValues: {
      key: field?.key ?? "",
      label: field?.label ?? "",
      type: (field?.type as CustomFieldValues["type"]) ?? "Text",
      options: (field?.options ?? []).join("\n"),
      isRequired: field?.isRequired ?? false,
      groupName: field?.groupName ?? "",
      badgeColor: field?.badgeColor ?? "",
      visibleOnGrid: field?.visibleOnGrid ?? false,
      dashboardCounter: field?.dashboardCounter ?? false,
      order: String(field?.order ?? nextOrder),
    },
  });
  const type = form.watch("type");
  const group = form.watch("groupName");
  const save = useCustomizationMutation(slug, async (values: CustomFieldValues): Promise<void> => {
    const body = customFieldBody(values);
    if (field) {
      await updateCustomField(slug, field.id, body);
    } else {
      await createCustomField(slug, { ...body, entityType, key: values.key, type: values.type });
    }
  });

  const submit = form.handleSubmit((values) =>
    save.mutateAsync(values).then(
      () => {
        notify.success("app.platform.customFields.saved");
        onClose();
      },
      (error: unknown) => {
        applyApiErrors(error, form.setError, [
          "key",
          "label",
          "options",
          "groupName",
          "badgeColor",
          "dashboardCounter",
          "order",
        ]);
      },
    ),
  );

  return (
    <Dialog open onOpenChange={(open) => (open ? undefined : onClose())}>
      <DialogContent closeLabel={t("Close")} className="max-h-[90dvh] overflow-y-auto sm:max-w-lg">
        <form className="flex flex-col gap-4" onSubmit={submit} noValidate>
          <DialogHeader>
            <DialogTitle>
              {field ? t("app.platform.customFields.edit") : t("app.platform.customFields.new")}
            </DialogTitle>
            <DialogDescription>{entityName}</DialogDescription>
          </DialogHeader>
          {save.error && Object.keys(form.formState.errors).length === 0 ? (
            <ApiErrorAlert error={save.error} />
          ) : null}
          <div className="grid gap-4 sm:grid-cols-2">
            <FormField
              control={form.control}
              name="key"
              label={t("app.platform.customFields.key")}
              description={t("app.platform.customFields.keyHint")}
            >
              {(item, props) => (
                <Input
                  {...item}
                  {...props}
                  disabled={Boolean(field)}
                  autoComplete="off"
                  spellCheck={false}
                />
              )}
            </FormField>
            <FormField
              control={form.control}
              name="label"
              label={t("app.platform.customFields.label")}
            >
              {(item, props) => <Input {...item} {...props} maxLength={100} />}
            </FormField>
          </div>
          <FormField control={form.control} name="type" label={t("app.platform.customFields.type")}>
            {(item, props) => (
              <Select value={item.value} onValueChange={item.onChange} disabled={Boolean(field)}>
                <SelectTrigger {...props} className="w-full" onBlur={item.onBlur} ref={item.ref}>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {CUSTOM_FIELD_TYPES.map((value) => (
                    <SelectItem key={value} value={value}>
                      {t(`app.platform.customFields.types.${value}`)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )}
          </FormField>
          {type === "Select" || type === "MultiSelect" ? (
            <FormField
              control={form.control}
              name="options"
              label={t("app.platform.customFields.options")}
              description={t("app.platform.customFields.optionsHint")}
            >
              {(item, props) => <Textarea {...item} {...props} rows={4} />}
            </FormField>
          ) : null}
          <div className="grid gap-4 sm:grid-cols-2">
            <FormField
              control={form.control}
              name="groupName"
              label={t("app.platform.customFields.group")}
              description={t("app.platform.customFields.groupHint")}
            >
              {(item, props) => <Input {...item} {...props} maxLength={50} />}
            </FormField>
            {group.trim() !== "" ? (
              <FormField
                control={form.control}
                name="badgeColor"
                label={t("app.platform.customFields.badgeColor")}
              >
                {(item, props) => (
                  <Input
                    {...item}
                    {...props}
                    placeholder="#72fa29"
                    maxLength={7}
                    spellCheck={false}
                  />
                )}
              </FormField>
            ) : null}
          </div>
          <FormField
            control={form.control}
            name="order"
            label={t("app.platform.customFields.order")}
          >
            {(item, props) => <Input {...item} {...props} inputMode="numeric" className="w-28" />}
          </FormField>
          <fieldset className="flex flex-col gap-3">
            <legend className="sr-only">{t("app.platform.customFields.flags")}</legend>
            <SwitchField
              form={form}
              name="isRequired"
              label={t("app.platform.customFields.requiredLabel")}
            />
            <SwitchField
              form={form}
              name="visibleOnGrid"
              label={t("app.platform.customFields.onGridLabel")}
            />
            {type === "Boolean" ? (
              <SwitchField
                form={form}
                name="dashboardCounter"
                label={t("app.platform.customFields.counterLabel")}
              />
            ) : null}
          </fieldset>
          <DialogFooter closeLabel={t("Cancel")}>
            <Button type="submit" disabled={form.formState.isSubmitting}>
              {t("Save")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function SwitchField({
  form,
  name,
  label,
}: {
  form: ReturnType<typeof useZodForm<typeof customFieldSchema>>;
  name: "isRequired" | "visibleOnGrid" | "dashboardCounter";
  label: string;
}) {
  const id = `custom-field-${name}`;
  const error = form.formState.errors[name]?.message;
  const t = useTranslations();
  return (
    <div className="flex flex-col gap-1">
      <div className="flex items-center gap-2">
        <Switch
          id={id}
          checked={form.watch(name)}
          onCheckedChange={(checked) => form.setValue(name, checked)}
          aria-describedby={error ? `${id}-error` : undefined}
        />
        <Label htmlFor={id}>{label}</Label>
      </div>
      {error ? (
        <p id={`${id}-error`} className="text-sm text-destructive">
          {t.has(error) ? t(error) : t("validation.invalid")}
        </p>
      ) : null}
    </div>
  );
}
