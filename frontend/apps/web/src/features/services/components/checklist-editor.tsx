"use client";

import * as React from "react";
import { isApiError } from "@auxilia/api-client";
import { ArrowDownIcon, ArrowUpIcon, PlusIcon, Trash2Icon } from "lucide-react";
import { useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@auxilia/ui/components/card";
import { Checkbox } from "@auxilia/ui/components/checkbox";
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

import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { useNotify } from "@/lib/notify";

import {
  saveChecklist,
  useServiceChecklist,
  useServiceFolders,
  useServiceMutation,
  type ChecklistItem,
  type ChecklistItemInput,
} from "../api";

/** Server limit (`ServiceChecklistItem` in the API). */
export const CHECKLIST_NAME_MAX = 200;
const NO_FOLDER = "none";

type Row = ChecklistItemInput & { key: string };

/** Moves the row at `index` by `by` (−1 up, +1 down); the same list when it cannot move. */
export function moved<T>(rows: readonly T[], index: number, by: -1 | 1): T[] {
  const target = index + by;
  if (target < 0 || target >= rows.length) {
    return [...rows];
  }

  const next = [...rows];
  [next[index], next[target]] = [next[target]!, next[index]!];
  return next;
}

/**
 * The document checklist of a service (B-26, F09): what its cases need, optionally the folder of the template where it
 * goes, required or not, in order; saved as a whole (items kept keep the ticks of the cases).
 */
export function ChecklistEditor({
  tenant,
  serviceId,
  canManage,
}: {
  tenant: string;
  serviceId: string;
  canManage: boolean;
}) {
  const checklist = useServiceChecklist(tenant, serviceId);
  return checklist.error ? (
    <ApiErrorAlert error={checklist.error} onRetry={() => void checklist.refetch()} />
  ) : checklist.data ? (
    <ChecklistForm
      tenant={tenant}
      serviceId={serviceId}
      canManage={canManage}
      initial={checklist.data}
    />
  ) : (
    <Skeleton className="h-24 w-full" />
  );
}

const toRows = (items: readonly ChecklistItem[]): Row[] =>
  items.map((item) => ({
    key: item.id,
    id: item.id,
    name: item.name,
    folderId: item.folderId,
    required: item.required,
  }));

function ChecklistForm({
  tenant,
  serviceId,
  canManage,
  initial,
}: {
  tenant: string;
  serviceId: string;
  canManage: boolean;
  initial: readonly ChecklistItem[];
}) {
  const t = useTranslations();
  const notify = useNotify();
  const folders = useServiceFolders(tenant, serviceId);
  const [rows, setRows] = React.useState<Row[]>(() => toRows(initial));
  const save = useServiceMutation(tenant, (items: ChecklistItemInput[]) =>
    saveChecklist(serviceId, items),
  );

  const update = (index: number, change: Partial<Row>) =>
    setRows((current) =>
      current.map((row, position) => (position === index ? { ...row, ...change } : row)),
    );
  const fieldError = (index: number, field: string) =>
    isApiError(save.error) ? save.error.fieldErrors[`items[${index}].${field}`]?.[0] : undefined;

  const onSave = async () => {
    await save
      .mutateAsync(
        rows.map((row) => ({
          id: row.id,
          name: row.name,
          folderId: row.folderId,
          required: row.required,
        })),
      )
      .then(
        (saved) => {
          setRows(toRows(saved));
          notify.success("app.services.checklist.saved");
        },
        (error: unknown) => {
          if (!(isApiError(error) && Object.keys(error.fieldErrors).length > 0)) {
            notify.error(error);
          }
        },
      );
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2 className="text-base font-semibold">{t("app.services.checklist.title")}</h2>
        </CardTitle>
        <CardDescription>{t("app.services.checklist.description")}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        {rows.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t("app.services.checklist.empty")}</p>
        ) : (
          <ol className="flex flex-col gap-3">
            {rows.map((row, index) => {
              const nameError = fieldError(index, "name");
              const folderError = fieldError(index, "folderId");
              return (
                <li
                  key={row.key}
                  className="grid gap-2 rounded-md border p-3 sm:grid-cols-[1fr_14rem_auto_auto] sm:items-end"
                >
                  <div className="flex flex-col gap-1">
                    <Label htmlFor={`checklist-name-${row.key}`}>
                      {t("app.services.checklist.name")}
                    </Label>
                    <Input
                      id={`checklist-name-${row.key}`}
                      value={row.name}
                      maxLength={CHECKLIST_NAME_MAX}
                      disabled={!canManage}
                      aria-invalid={nameError ? true : undefined}
                      aria-describedby={nameError ? `checklist-name-${row.key}-error` : undefined}
                      onChange={(event) => update(index, { name: event.target.value })}
                    />
                    {nameError ? (
                      <p
                        id={`checklist-name-${row.key}-error`}
                        className="text-sm text-destructive"
                      >
                        {t(nameError)}
                      </p>
                    ) : null}
                  </div>
                  <div className="flex flex-col gap-1">
                    <Label htmlFor={`checklist-folder-${row.key}`}>
                      {t("app.services.checklist.folder")}
                    </Label>
                    <Select
                      value={row.folderId ?? NO_FOLDER}
                      disabled={!canManage}
                      onValueChange={(value) =>
                        update(index, { folderId: value === NO_FOLDER ? null : value })
                      }
                    >
                      <SelectTrigger
                        id={`checklist-folder-${row.key}`}
                        aria-invalid={folderError ? true : undefined}
                      >
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value={NO_FOLDER}>
                          {t("app.services.checklist.noFolder")}
                        </SelectItem>
                        {(folders.data ?? []).map((folder) => (
                          <SelectItem key={folder.id} value={folder.id}>
                            {folder.path}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  </div>
                  <span className="flex items-center gap-2 sm:pb-2">
                    <Checkbox
                      id={`checklist-required-${row.key}`}
                      checked={row.required}
                      disabled={!canManage}
                      onCheckedChange={(value) => update(index, { required: value === true })}
                    />
                    <Label htmlFor={`checklist-required-${row.key}`} className="font-normal">
                      {t("app.services.checklist.required")}
                    </Label>
                  </span>
                  {canManage ? (
                    <span className="flex gap-1">
                      <Button
                        type="button"
                        variant="ghost"
                        size="icon"
                        disabled={index === 0}
                        aria-label={t("app.services.checklist.up", { name: row.name })}
                        onClick={() => setRows((current) => moved(current, index, -1))}
                      >
                        <ArrowUpIcon aria-hidden />
                      </Button>
                      <Button
                        type="button"
                        variant="ghost"
                        size="icon"
                        disabled={index === rows.length - 1}
                        aria-label={t("app.services.checklist.down", { name: row.name })}
                        onClick={() => setRows((current) => moved(current, index, 1))}
                      >
                        <ArrowDownIcon aria-hidden />
                      </Button>
                      <Button
                        type="button"
                        variant="ghost"
                        size="icon"
                        aria-label={t("app.services.checklist.remove", { name: row.name })}
                        onClick={() =>
                          setRows((current) => current.filter((_, position) => position !== index))
                        }
                      >
                        <Trash2Icon aria-hidden />
                      </Button>
                    </span>
                  ) : null}
                </li>
              );
            })}
          </ol>
        )}
        {canManage ? (
          <div className="flex flex-wrap justify-between gap-2">
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() =>
                setRows((current) => [
                  ...current,
                  { key: crypto.randomUUID(), id: null, name: "", folderId: null, required: true },
                ])
              }
            >
              <PlusIcon aria-hidden /> {t("app.services.checklist.add")}
            </Button>
            <Button type="button" size="sm" disabled={save.isPending} onClick={() => void onSave()}>
              {t("Save")}
            </Button>
          </div>
        ) : null}
      </CardContent>
    </Card>
  );
}
