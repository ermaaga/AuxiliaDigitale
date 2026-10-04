"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { isApiError } from "@auxilia/api-client";
import { ArrowLeftIcon, LockIcon, Trash2Icon } from "lucide-react";
import { useFormatter, useTranslations } from "next-intl";
import { Alert, AlertDescription } from "@auxilia/ui/components/alert";
import { Badge } from "@auxilia/ui/components/badge";
import { Button } from "@auxilia/ui/components/button";
import { Card, CardContent } from "@auxilia/ui/components/card";
import { Skeleton } from "@auxilia/ui/components/skeleton";
import { Textarea } from "@auxilia/ui/components/textarea";

import { useConfirm } from "@/components/confirm/confirm-provider";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { applyApiErrors, FormField, useZodForm } from "@/components/forms/form";
import { tenantHref } from "@/lib/href";
import { useNotify } from "@/lib/notify";

import {
  closeRequest,
  deleteRequest,
  replyToRequest,
  useRequest,
  useRequestMutation,
  type RequestDetail,
} from "../api";
import { MESSAGE_MAX, replySchema } from "../schemas/request";
import { RequestStatusBadge } from "./request-status-badge";

/**
 * A request thread (F15): subject, type, status, who asked whom; the messages oldest first (the caller's on the
 * right); a reply while it is open, close (with confirmation), delete for Administrators (with confirmation).
 */
export function RequestThread({ tenant, id }: { tenant: string; id: string }) {
  const t = useTranslations();
  const query = useRequest(tenant, id);
  const backLink = (
    <Button variant="ghost" size="sm" className="self-start" asChild>
      <Link href={tenantHref(tenant, "/requests")}>
        <ArrowLeftIcon aria-hidden /> {t("app.requests.back")}
      </Link>
    </Button>
  );

  if (query.error) {
    return (
      <div className="flex flex-col gap-4">
        {backLink}
        {isApiError(query.error) && query.error.status === 404 ? (
          <Alert>
            <AlertDescription>{t("errors.AUX-17006")}</AlertDescription>
          </Alert>
        ) : (
          <ApiErrorAlert error={query.error} onRetry={() => void query.refetch()} />
        )}
      </div>
    );
  }

  if (!query.data) {
    return (
      <div className="flex flex-col gap-4" aria-busy="true">
        {backLink}
        <Skeleton className="h-10 w-80" />
        <Skeleton className="h-64 w-full" />
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-4">
      {backLink}
      <Thread tenant={tenant} value={query.data} />
    </div>
  );
}

function Thread({ tenant, value }: { tenant: string; value: RequestDetail }) {
  const t = useTranslations();
  const format = useFormatter();
  const router = useRouter();
  const notify = useNotify();
  const confirm = useConfirm();
  const reply = useRequestMutation(tenant, (message: string) => replyToRequest(value.id, message));
  const close = useRequestMutation(tenant, () => closeRequest(value.id));
  const remove = useRequestMutation(tenant, () => deleteRequest(value.id));
  const form = useZodForm(replySchema, { defaultValues: { message: "" } });

  const submit = form.handleSubmit(async (values) => {
    try {
      await reply.mutateAsync(values.message);
      form.reset({ message: "" });
      notify.success("app.requests.replied");
    } catch (error) {
      if (!(isApiError(error) && applyApiErrors(error, form.setError, ["message"]))) {
        notify.error(error);
      }
    }
  });

  const onClose = async () => {
    if (
      await confirm({
        description: t("app.requests.closeConfirm"),
        confirmLabel: t("app.requests.close"),
      })
    ) {
      await close
        .mutateAsync(undefined)
        .then(() => notify.success("app.requests.closed"), notify.error);
    }
  };

  const onDelete = async () => {
    if (
      await confirm({
        description: t("app.requests.deleteConfirm"),
        confirmLabel: t("Delete"),
        variant: "destructive",
      })
    ) {
      try {
        await remove.mutateAsync(undefined);
        notify.success("app.requests.deleted");
        router.push(tenantHref(tenant, "/requests"));
      } catch (error) {
        notify.error(error);
      }
    }
  };

  return (
    <>
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex flex-col gap-2">
          <h1 className="text-2xl font-semibold tracking-tight">{value.subject}</h1>
          <div className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
            <RequestStatusBadge status={value.status} />
            <Badge variant="outline">
              {t(`app.requests.type.${value.type}` as "app.requests.type.General")}
            </Badge>
            <span>
              {value.sender.fullName} → {value.recipient?.fullName ?? t("app.requests.office")}
            </span>
          </div>
        </div>
        <div className="flex flex-wrap gap-2">
          {value.canClose ? (
            <Button
              type="button"
              variant="outline"
              onClick={() => void onClose()}
              disabled={close.isPending}
            >
              <LockIcon aria-hidden /> {t("app.requests.close")}
            </Button>
          ) : null}
          {value.canDelete ? (
            <Button
              type="button"
              variant="outline"
              onClick={() => void onDelete()}
              disabled={remove.isPending}
            >
              <Trash2Icon aria-hidden /> {t("Delete")}
            </Button>
          ) : null}
        </div>
      </header>

      <ol className="flex flex-col gap-3" aria-label={t("app.requests.thread")}>
        {value.messages.map((message) => (
          <li key={message.id} className={`flex ${message.mine ? "justify-end" : "justify-start"}`}>
            <Card
              className={`w-full max-w-xl ${message.mine ? "bg-accent text-accent-foreground" : ""}`}
            >
              <CardContent className="flex flex-col gap-1 pt-4">
                <span className="text-sm font-medium">{message.author.fullName}</span>
                <p className="whitespace-pre-wrap text-sm">{message.body}</p>
                <span className="text-xs text-muted-foreground">
                  {format.dateTime(new Date(message.sentAt), {
                    dateStyle: "medium",
                    timeStyle: "short",
                  })}
                </span>
              </CardContent>
            </Card>
          </li>
        ))}
      </ol>

      {value.canReply ? (
        <form onSubmit={(event) => void submit(event)} noValidate className="flex flex-col gap-2">
          <FormField control={form.control} name="message" label={t("app.requests.reply")}>
            {(field, props) => <Textarea {...field} {...props} rows={4} maxLength={MESSAGE_MAX} />}
          </FormField>
          <Button type="submit" className="self-end" disabled={form.formState.isSubmitting}>
            {t("Send")}
          </Button>
        </form>
      ) : value.status === "Closed" ? (
        <p className="text-sm text-muted-foreground">{t("app.requests.isClosed")}</p>
      ) : null}
    </>
  );
}
