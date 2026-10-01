"use client";

import * as React from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
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
import { Input } from "@auxilia/ui/components/input";
import { Skeleton } from "@auxilia/ui/components/skeleton";

import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { FormField, applyApiErrors, useZodForm } from "@/components/forms/form";
import { useNotify } from "@/lib/notify";

import { administratorSchema } from "../../schemas/tenant";
import {
  createAdministrator,
  sendInvitation,
  tenantKey,
  useAdministrators,
} from "../../tenant-api";

type Invitation = { invitationSent: boolean; invitationErrorCode: string | null };

/**
 * Administrators of the tenant (technical endpoints, tenant-scoped platform token): the first one is created here and
 * invited by e-mail; the link is never shown to the System (D-21). Without a sending account the invitation waits.
 */
export function TenantAdministratorsCard({ slug }: { slug: string }) {
  const t = useTranslations();
  const notify = useNotify();
  const client = useQueryClient();
  const administrators = useAdministrators(slug, true);
  const [outcome, setOutcome] = React.useState<Invitation>();

  function report(invitation: Invitation) {
    setOutcome(invitation);
    if (invitation.invitationSent) {
      notify.success("app.platform.admins.sent");
    }

    void client.invalidateQueries({ queryKey: tenantKey(slug, "administrators") });
  }

  const resend = useMutation({
    mutationFn: (userId: string) => sendInvitation(slug, userId),
    onSuccess: report,
    onError: notify.error,
  });

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2 className="text-base font-semibold">{t("app.platform.admins.title")}</h2>
        </CardTitle>
        <CardDescription>{t("app.platform.admins.description")}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        {outcome && !outcome.invitationSent ? (
          <p role="status" className="text-sm text-muted-foreground">
            {t("app.platform.admins.notSent", { code: outcome.invitationErrorCode ?? "—" })}
          </p>
        ) : null}
        {administrators.error ? (
          <ApiErrorAlert
            error={administrators.error}
            onRetry={() => void administrators.refetch()}
          />
        ) : administrators.isPending ? (
          <Skeleton className="h-10 w-full" />
        ) : administrators.data.length === 0 ? (
          <FirstAdministratorForm slug={slug} onCreated={report} />
        ) : (
          <ul className="flex flex-col divide-y">
            {administrators.data.map((admin) => (
              <li
                key={admin.userId}
                className="flex flex-wrap items-center justify-between gap-2 py-2"
              >
                <span className="flex flex-col">
                  <span className="text-sm font-medium break-all">
                    {admin.email ?? admin.userName}
                  </span>
                  <Badge variant={admin.isActivated ? "secondary" : "outline"} className="w-fit">
                    {admin.isActivated
                      ? t("app.platform.admins.activated")
                      : t("app.platform.admins.pending")}
                  </Badge>
                </span>
                {!admin.isActivated ? (
                  <Button
                    variant="outline"
                    size="sm"
                    disabled={resend.isPending}
                    onClick={() => resend.mutate(admin.userId)}
                  >
                    {t("app.platform.admins.resend")}
                  </Button>
                ) : null}
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  );
}

function FirstAdministratorForm({
  slug,
  onCreated,
}: {
  slug: string;
  onCreated: (invitation: Invitation) => void;
}) {
  const t = useTranslations();
  const form = useZodForm(administratorSchema, {
    defaultValues: { email: "", firstName: "", lastName: "" },
  });
  const [error, setError] = React.useState<unknown>();

  const submit = form.handleSubmit(async (values) => {
    setError(undefined);
    try {
      onCreated(await createAdministrator(slug, values));
    } catch (failure) {
      if (!applyApiErrors(failure, form.setError, ["email", "firstName", "lastName"])) {
        setError(failure);
      }
    }
  });

  return (
    <form className="flex flex-col gap-3" onSubmit={submit} noValidate>
      <p className="text-sm text-muted-foreground">{t("app.platform.admins.none")}</p>
      {error ? <ApiErrorAlert error={error} /> : null}
      <FormField control={form.control} name="email" label={t("Email")}>
        {(field, props) => <Input {...field} {...props} type="email" autoComplete="off" />}
      </FormField>
      <div className="grid gap-3 sm:grid-cols-2">
        <FormField control={form.control} name="firstName" label={t("FirstName")}>
          {(field, props) => <Input {...field} {...props} autoComplete="off" />}
        </FormField>
        <FormField control={form.control} name="lastName" label={t("LastName")}>
          {(field, props) => <Input {...field} {...props} autoComplete="off" />}
        </FormField>
      </div>
      <div>
        <Button type="submit" disabled={form.formState.isSubmitting}>
          {t("app.platform.admins.create")}
        </Button>
      </div>
    </form>
  );
}
