"use client";

import * as React from "react";
import { isApiError } from "@auxilia/api-client";
import { useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@auxilia/ui/components/dialog";
import { Input } from "@auxilia/ui/components/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@auxilia/ui/components/select";
import { Textarea } from "@auxilia/ui/components/textarea";

import { Combobox } from "@/components/combobox";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { applyApiErrors, FormField, useZodForm } from "@/components/forms/form";
import { useClients } from "@/features/clients";
import { useCurrentUserId } from "@/lib/permissions";
import { useNotify } from "@/lib/notify";

import {
  createTask,
  taskBody,
  updateTask,
  useTaskAssignees,
  useTaskMutation,
  type TaskItem,
} from "../api";
import { NOTES_MAX, TITLE_MAX, taskSchema, type TaskValues } from "../schemas/task";

const FIELDS = ["title", "notes", "dueOn", "assigneeUserId", "clientId", "caseId"] as const;

/** A client (and case) the new task is about, fixed by the page that opens the dialog. */
export type TaskContext = {
  clientId: string;
  clientName: string;
  caseId?: string;
  caseNumber?: string;
};

/**
 * New or changed task (B-26): title, notes, due date, assignee (me by default) and, from the task list, the client it
 * is about; from a client or a case the links are fixed.
 */
export function TaskDialog({
  tenant,
  task,
  context,
  open,
  onOpenChange,
}: {
  tenant: string;
  task?: TaskItem;
  context?: TaskContext;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const t = useTranslations();
  const notify = useNotify();
  const me = useCurrentUserId();
  const [clientSearch, setClientSearch] = React.useState("");
  const fixedClient =
    task?.client ?? (context ? { id: context.clientId, fullName: context.clientName } : null);
  const fixedCase =
    task?.case ??
    (context?.caseId ? { id: context.caseId, number: context.caseNumber ?? "" } : null);
  const assignees = useTaskAssignees(tenant, open);
  const clients = useClients(tenant, {
    view: "all",
    page: 1,
    pageSize: 20,
    "filter[fullName]": clientSearch || undefined,
  });
  const form = useZodForm(taskSchema, {
    defaultValues: {
      title: task?.title ?? "",
      notes: task?.notes ?? "",
      dueOn: task?.dueOn ?? "",
      assigneeUserId: task?.assignee.id ?? me ?? "",
      clientId: fixedClient?.id ?? "",
      caseId: fixedCase?.id ?? "",
    },
  });
  const save = useTaskMutation(tenant, (values: TaskValues) =>
    task ? updateTask(task.id, taskBody(values)) : createTask(taskBody(values)),
  );

  const submit = form.handleSubmit(async (values) => {
    try {
      await save.mutateAsync(values);
      notify.success(task ? "app.tasks.saved" : "app.tasks.created");
      onOpenChange(false);
      if (!task) {
        form.reset();
      }
    } catch (error) {
      if (isApiError(error)) {
        applyApiErrors(error, form.setError, FIELDS);
      }
    }
  });

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent closeLabel={t("Close")} className="max-h-[90dvh] overflow-y-auto sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{task ? t("app.tasks.edit") : t("app.tasks.new")}</DialogTitle>
          <DialogDescription>{t("app.tasks.dialogDescription")}</DialogDescription>
        </DialogHeader>
        <form onSubmit={(event) => void submit(event)} noValidate className="flex flex-col gap-4">
          {save.error &&
          !(isApiError(save.error) && Object.keys(save.error.fieldErrors).length > 0) ? (
            <ApiErrorAlert error={save.error} />
          ) : null}
          <FormField control={form.control} name="title" label={`${t("app.tasks.title")} *`}>
            {(field, props) => <Input {...props} {...field} maxLength={TITLE_MAX} />}
          </FormField>
          <FormField control={form.control} name="notes" label={t("app.tasks.notes")}>
            {(field, props) => <Textarea {...props} {...field} rows={3} maxLength={NOTES_MAX} />}
          </FormField>
          <div className="grid gap-4 sm:grid-cols-2">
            <FormField control={form.control} name="dueOn" label={t("app.tasks.dueOn")}>
              {(field, props) => <Input {...props} {...field} type="date" />}
            </FormField>
            <FormField
              control={form.control}
              name="assigneeUserId"
              label={`${t("app.tasks.assignee")} *`}
            >
              {(field, props) => (
                <Select value={field.value} onValueChange={field.onChange}>
                  <SelectTrigger {...props}>
                    <SelectValue placeholder={t("app.tasks.chooseAssignee")} />
                  </SelectTrigger>
                  <SelectContent>
                    {(assignees.data ?? []).map((assignee) => (
                      <SelectItem key={assignee.id} value={assignee.id}>
                        {assignee.fullName}
                        {assignee.id === me ? ` (${t("app.tasks.me")})` : ""}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            </FormField>
          </div>
          {fixedClient ? (
            <p className="text-sm">
              <span className="text-muted-foreground">{t("Client")}: </span>
              <span className="font-medium">{fixedClient.fullName}</span>
              {fixedCase ? (
                <>
                  <span className="text-muted-foreground"> · {t("app.tasks.case")}: </span>
                  <span className="font-medium">{fixedCase.number}</span>
                </>
              ) : null}
            </p>
          ) : (
            <FormField control={form.control} name="clientId" label={t("Client")}>
              {(field, props) => (
                <Combobox
                  {...props}
                  options={(clients.data?.items ?? []).map((client) => ({
                    value: client.id,
                    label: `${client.lastName} ${client.firstName}`,
                    description: client.email ?? undefined,
                  }))}
                  value={field.value || undefined}
                  onChange={(value) => field.onChange(value ?? "")}
                  placeholder={t("app.tasks.chooseClient")}
                  onSearch={setClientSearch}
                  loading={clients.isFetching}
                />
              )}
            </FormField>
          )}
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              {t("Cancel")}
            </Button>
            <Button type="submit" disabled={save.isPending}>
              {t("Save")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
