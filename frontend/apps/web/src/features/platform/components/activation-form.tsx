"use client";

import * as React from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { unwrap } from "@auxilia/api-client";
import { Button } from "@auxilia/ui/components/button";
import { Input } from "@auxilia/ui/components/input";

import { TotpEnrollment } from "@/components/totp-enrollment";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { FormField, applyApiErrors, useZodForm } from "@/components/forms/form";
import { PasswordInput } from "@/features/auth/components/password-input";
import { createBffClient } from "@/lib/api/client";
import { platformHref } from "@/lib/href";

import {
  PLATFORM_PASSWORD_MIN_LENGTH,
  activationSchema,
  activationTokenSchema,
} from "../schemas/activation";

type Enrollment = { activationToken: string; secret: string; uri: string };

/**
 * Activation of a System account (N02, D-22): the activation code from `auxctl` starts the enrolment (the API returns
 * the authenticator secret once), then the password and the first code of the app complete it. The page never stores
 * the secret; reloading starts a new enrolment.
 */
export function ActivationForm({ token }: { token?: string }) {
  const t = useTranslations();
  const [enrollment, setEnrollment] = React.useState<Enrollment>();
  const [done, setDone] = React.useState(false);

  if (done) {
    return (
      <div className="flex flex-col gap-4" role="status">
        <p>{t("app.auth.activate.success")}</p>
        <Button asChild>
          <Link href={platformHref("/login")}>{t("Login")}</Link>
        </Button>
      </div>
    );
  }

  return enrollment ? (
    <CompleteStep enrollment={enrollment} onDone={() => setDone(true)} />
  ) : (
    <TokenStep token={token} onEnrolled={setEnrollment} />
  );
}

function TokenStep({
  token,
  onEnrolled,
}: {
  token?: string;
  onEnrolled: (enrollment: Enrollment) => void;
}) {
  const t = useTranslations();
  const form = useZodForm(activationTokenSchema, {
    defaultValues: { activationToken: token ?? "" },
  });
  const [error, setError] = React.useState<unknown>();

  const submit = form.handleSubmit(async ({ activationToken }) => {
    setError(undefined);
    try {
      const result = unwrap(
        await createBffClient("platform").POST("/api/v1/platform/auth/enrollment", {
          body: { activationToken },
        }),
      );
      onEnrolled({ activationToken, secret: result.secret, uri: result.uri });
    } catch (failure) {
      if (!applyApiErrors(failure, form.setError, [])) {
        setError(failure);
      }
    }
  });

  return (
    <form className="flex flex-col gap-4" onSubmit={submit} noValidate>
      {error ? <ApiErrorAlert error={error} /> : null}
      <FormField
        control={form.control}
        name="activationToken"
        label={t("app.platform.activate.token")}
      >
        {(field, props) => <Input {...field} {...props} autoComplete="off" spellCheck={false} />}
      </FormField>
      <Button type="submit" disabled={form.formState.isSubmitting}>
        {t("app.platform.activate.start")}
      </Button>
      <BackToLogin />
    </form>
  );
}

function CompleteStep({ enrollment, onDone }: { enrollment: Enrollment; onDone: () => void }) {
  const t = useTranslations();
  const form = useZodForm(activationSchema, {
    defaultValues: { password: "", confirmPassword: "", code: "" },
  });
  const [error, setError] = React.useState<unknown>();

  const submit = form.handleSubmit(async ({ password, code }) => {
    setError(undefined);
    try {
      unwrap(
        await createBffClient("platform").POST("/api/v1/platform/auth/activate", {
          body: { activationToken: enrollment.activationToken, password, code },
        }),
      );
      onDone();
    } catch (failure) {
      if (!applyApiErrors(failure, form.setError, ["password", "code"])) {
        setError(failure);
      }
    }
  });

  return (
    <form className="flex flex-col gap-4" onSubmit={submit} noValidate>
      {error ? <ApiErrorAlert error={error} /> : null}
      <TotpEnrollment secret={enrollment.secret} uri={enrollment.uri} />
      <FormField
        control={form.control}
        name="password"
        label={t("NewPassword")}
        description={t("app.platform.activate.passwordHint", {
          count: PLATFORM_PASSWORD_MIN_LENGTH,
        })}
      >
        {(field, props) => <PasswordInput {...field} {...props} autoComplete="new-password" />}
      </FormField>
      <FormField control={form.control} name="confirmPassword" label={t("ConfirmPassword")}>
        {(field, props) => <PasswordInput {...field} {...props} autoComplete="new-password" />}
      </FormField>
      <FormField
        control={form.control}
        name="code"
        label={t("app.platform.login.code")}
        description={t("app.platform.activate.codeHint")}
      >
        {(field, props) => (
          <Input
            {...field}
            {...props}
            inputMode="numeric"
            autoComplete="one-time-code"
            maxLength={7}
          />
        )}
      </FormField>
      <Button type="submit" disabled={form.formState.isSubmitting}>
        {t("app.auth.activate.submit")}
      </Button>
      <BackToLogin />
    </form>
  );
}

function BackToLogin() {
  const t = useTranslations();
  return (
    <Link
      className="text-center text-sm text-primary-text underline-offset-4 hover:underline"
      href={platformHref("/login")}
    >
      {t("app.auth.backToLogin")}
    </Link>
  );
}
