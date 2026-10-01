"use client";

import * as React from "react";
import Link from "next/link";
import { LockIcon, PlusIcon } from "lucide-react";
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
import { DataTable } from "@/components/data-table/data-table";
import type { DataTableColumn } from "@/components/data-table/table-model";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { FormField, applyApiErrors, useZodForm } from "@/components/forms/form";
import { tenantConsoleHref } from "@/lib/href";
import { useNotify } from "@/lib/notify";

import {
  SPECIALIZATION_ROLES,
  createSpecialization,
  deactivateSpecialization,
  updateSpecialization,
  useAccessMutation,
  useSpecializations,
  type Specialization,
} from "../../access-api";
import { roleLabel } from "../../labels";
import {
  specializationBody,
  specializationSchema,
  type SpecializationValues,
} from "../../schemas/specialization";

const ALL = "all";

/**
 * Specializations of the Client and Employee roles (F12, D-18): name, contacts and the private flag that makes the
 * cases of their services private (F10). Deleting deactivates; members are managed on the specialization's page.
 */
export function TenantSpecializations({ slug }: { slug: string }) {
  const t = useTranslations();
  const notify = useNotify();
  const confirm = useConfirm();
  const [role, setRole] = React.useState<string>(ALL);
  const list = useSpecializations(slug, role === ALL ? undefined : role);
  const [editing, setEditing] = React.useState<Specialization | "new">();
  const deactivate = useAccessMutation(slug, (id: string) => deactivateSpecialization(slug, id));

  const columns: DataTableColumn<Specialization>[] = [
    {
      id: "name",
      header: t("app.platform.specializations.name"),
      hideable: false,
      mobile: "title",
      cell: (item) => (
        <span className="flex items-center gap-2">
          <Link
            className="font-medium underline-offset-4 hover:underline"
            href={tenantConsoleHref(slug, `/specializations/${item.id}`)}
          >
            {item.name}
          </Link>
          {item.isPrivate ? (
            <Badge variant="secondary">
              <LockIcon aria-hidden /> {t("app.platform.specializations.private")}
            </Badge>
          ) : null}
        </span>
      ),
    },
    { id: "role", header: t("Role"), cell: (item) => roleLabel(t, item.role) },
    { id: "email", header: t("Email"), cell: (item) => item.email ?? "—" },
    {
      id: "workPhone",
      header: t("app.platform.specializations.workPhone"),
      cell: (item) => item.workPhone ?? "—",
    },
    {
      id: "members",
      header: t("app.platform.specializations.members"),
      cell: (item) => String(item.memberCount),
    },
    {
      id: "actions",
      header: t("Actions"),
      hideable: false,
      cell: (item) => (
        <div className="flex flex-wrap gap-1">
          <Button variant="outline" size="sm" asChild>
            <Link
              href={tenantConsoleHref(slug, `/specializations/${item.id}`)}
              aria-label={t("app.platform.specializations.membersNamed", { name: item.name })}
            >
              {t("app.platform.specializations.assign")}
            </Link>
          </Button>
          <Button
            variant="outline"
            size="sm"
            onClick={() => setEditing(item)}
            aria-label={t("app.platform.specializations.editNamed", { name: item.name })}
          >
            {t("Edit")}
          </Button>
          <Button
            variant="ghost"
            size="sm"
            disabled={deactivate.isPending}
            aria-label={t("app.platform.specializations.deleteNamed", { name: item.name })}
            onClick={async () => {
              if (
                await confirm({
                  title: t("app.platform.specializations.deleteTitle"),
                  description: t("app.platform.specializations.deleteText", { name: item.name }),
                  confirmLabel: t("Delete"),
                  variant: "destructive",
                })
              ) {
                await deactivate.mutateAsync(item.id).then(
                  () => notify.success("app.platform.specializations.deleted"),
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
            <h2 className="text-base font-semibold">{t("app.platform.specializations.list")}</h2>
          </CardTitle>
          <CardDescription>{t("app.platform.specializations.listDescription")}</CardDescription>
        </div>
        <div className="flex flex-wrap items-end gap-2">
          <div className="flex flex-col gap-1">
            <Label htmlFor="specialization-role">{t("Role")}</Label>
            <Select value={role} onValueChange={setRole}>
              <SelectTrigger id="specialization-role" className="w-40">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={ALL}>{t("app.platform.specializations.allRoles")}</SelectItem>
                {SPECIALIZATION_ROLES.map((item) => (
                  <SelectItem key={item} value={item}>
                    {roleLabel(t, item)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <Button size="sm" onClick={() => setEditing("new")}>
            <PlusIcon aria-hidden /> {t("app.platform.specializations.new")}
          </Button>
        </div>
      </CardHeader>
      <CardContent>
        <DataTable
          label={t("app.platform.specializations.list")}
          columns={columns}
          rows={list.data}
          getRowId={(item) => item.id}
          totalCount={list.data?.length ?? 0}
          page={1}
          pageSize={100}
          sort={null}
          onPageChange={() => {}}
          onPageSizeChange={() => {}}
          onSortChange={() => {}}
          isLoading={list.isPending}
          error={list.error}
          onRetry={() => void list.refetch()}
          hidePaging
        />
        {editing ? (
          <SpecializationDialog
            slug={slug}
            specialization={editing === "new" ? undefined : editing}
            onClose={() => setEditing(undefined)}
          />
        ) : null}
      </CardContent>
    </Card>
  );
}

export function SpecializationDialog({
  slug,
  specialization,
  onClose,
}: {
  slug: string;
  specialization?: Specialization;
  onClose: () => void;
}) {
  const t = useTranslations();
  const notify = useNotify();
  const form = useZodForm(specializationSchema, {
    defaultValues: {
      name: specialization?.name ?? "",
      role: (specialization?.role as SpecializationValues["role"] | undefined) ?? "Employee",
      description: specialization?.description ?? "",
      email: specialization?.email ?? "",
      workPhone: specialization?.workPhone ?? "",
      isPrivate: specialization?.isPrivate ?? false,
    },
  });
  const save = useAccessMutation(slug, async (values: SpecializationValues): Promise<void> => {
    const body = specializationBody(values);
    if (specialization) {
      await updateSpecialization(slug, specialization.id, body);
    } else {
      await createSpecialization(slug, { ...body, role: values.role });
    }
  });

  const submit = form.handleSubmit((values) =>
    save.mutateAsync(values).then(
      () => {
        notify.success("app.platform.specializations.saved");
        onClose();
      },
      (error: unknown) => {
        applyApiErrors(error, form.setError, ["name", "role", "description", "email", "workPhone"]);
      },
    ),
  );

  return (
    <Dialog open onOpenChange={(open) => (open ? undefined : onClose())}>
      <DialogContent closeLabel={t("Close")} className="max-h-[90dvh] overflow-y-auto sm:max-w-lg">
        <form className="flex flex-col gap-4" onSubmit={submit} noValidate>
          <DialogHeader>
            <DialogTitle>
              {specialization
                ? t("app.platform.specializations.edit")
                : t("app.platform.specializations.new")}
            </DialogTitle>
            <DialogDescription>
              {t("app.platform.specializations.dialogDescription")}
            </DialogDescription>
          </DialogHeader>
          {save.error && Object.keys(form.formState.errors).length === 0 ? (
            <ApiErrorAlert error={save.error} />
          ) : null}
          <div className="grid gap-4 sm:grid-cols-2">
            <FormField
              control={form.control}
              name="name"
              label={t("app.platform.specializations.name")}
            >
              {(item, props) => <Input {...item} {...props} maxLength={100} />}
            </FormField>
            <FormField
              control={form.control}
              name="role"
              label={t("Role")}
              description={specialization ? t("app.platform.specializations.roleFixed") : undefined}
            >
              {(item, props) => (
                <Select
                  value={item.value}
                  onValueChange={item.onChange}
                  disabled={Boolean(specialization)}
                >
                  <SelectTrigger {...props} className="w-full" onBlur={item.onBlur} ref={item.ref}>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {SPECIALIZATION_ROLES.map((value) => (
                      <SelectItem key={value} value={value}>
                        {roleLabel(t, value)}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            </FormField>
          </div>
          <FormField
            control={form.control}
            name="description"
            label={t("app.platform.specializations.descriptionLabel")}
          >
            {(item, props) => <Textarea {...item} {...props} rows={3} maxLength={1000} />}
          </FormField>
          <div className="grid gap-4 sm:grid-cols-2">
            <FormField control={form.control} name="email" label={t("Email")}>
              {(item, props) => (
                <Input {...item} {...props} type="email" autoComplete="off" maxLength={256} />
              )}
            </FormField>
            <FormField
              control={form.control}
              name="workPhone"
              label={t("app.platform.specializations.workPhone")}
            >
              {(item, props) => (
                <Input {...item} {...props} type="tel" autoComplete="off" maxLength={50} />
              )}
            </FormField>
          </div>
          <div className="flex flex-col gap-1">
            <div className="flex items-center gap-2">
              <Switch
                id="specialization-private"
                checked={form.watch("isPrivate")}
                onCheckedChange={(checked) => form.setValue("isPrivate", checked)}
                aria-describedby="specialization-private-hint"
              />
              <Label htmlFor="specialization-private">
                {t("app.platform.specializations.privateLabel")}
              </Label>
            </div>
            <p id="specialization-private-hint" className="text-sm text-muted-foreground">
              {t("app.platform.specializations.privateHint")}
            </p>
          </div>
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
