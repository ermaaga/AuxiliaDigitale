"use client";

import * as React from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { isApiError } from "@auxilia/api-client";
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
import { Switch } from "@auxilia/ui/components/switch";
import { brandingTokens } from "@auxilia/ui/lib/branding";
import type { Control, FieldPath } from "react-hook-form";

import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { FormField, applyApiErrors, useZodForm } from "@/components/forms/form";
import { brandingImageUrl, loginBackground } from "@/features/branding/branding";
import { useNotify } from "@/lib/notify";

import {
  BRANDING_SETTING_KEYS,
  brandingSchema,
  brandingValues,
  changedSettings,
  sixDigitColor,
  type BrandingValues,
} from "../../schemas/branding";
import {
  saveBrandingImage,
  saveSetting,
  useTenantBranding,
  type BrandingImageKind,
  type TenantBranding,
} from "../../settings-api";
import { tenantKey } from "../../tenant-api";

/** Setting key → form field, for the field errors of a refused setting. */
const FIELD_OF_SETTING = Object.fromEntries(
  Object.entries(BRANDING_SETTING_KEYS).map(([field, key]) => [key, field]),
) as Record<string, keyof BrandingValues>;

/**
 * Branding of a tenant (F23): app name or logo, theme colours and fill, login background (gradient, colour or image),
 * with a live preview of what the tenant app will show. Only changed values are stored at the tenant level.
 */
export function TenantBrandingEditor({ slug }: { slug: string }) {
  const branding = useTenantBranding(slug);

  if (branding.isPending) {
    return <Skeleton className="h-96 w-full" aria-busy />;
  }

  if (branding.error) {
    return <ApiErrorAlert error={branding.error} onRetry={() => void branding.refetch()} />;
  }

  return <BrandingForm slug={slug} branding={branding.data} />;
}

function BrandingForm({ slug, branding }: { slug: string; branding: TenantBranding }) {
  const t = useTranslations();
  const notify = useNotify();
  const client = useQueryClient();
  const initial = React.useMemo(() => brandingValues(branding), [branding]);
  const form = useZodForm(brandingSchema, { values: initial });
  const [error, setError] = React.useState<unknown>();
  const values = form.watch() as BrandingValues;

  const submit = form.handleSubmit(async (next) => {
    setError(undefined);
    let key: string | undefined;
    try {
      for (const [setting, value] of Object.entries(changedSettings(initial, next))) {
        key = setting;
        await saveSetting(slug, setting, value);
      }

      notify.success("app.platform.branding.saved");
    } catch (failure) {
      // The API names the field `value`: it belongs to the field of the refused setting.
      const field = key ? FIELD_OF_SETTING[key] : undefined;
      if (!field || !applyApiErrors(failure, form.setError, [field], { value: field })) {
        setError(failure);
      }
    } finally {
      await client.invalidateQueries({ queryKey: tenantKey(slug, "branding") });
      await client.invalidateQueries({ queryKey: tenantKey(slug, "settings") });
    }
  });

  return (
    <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_22rem] xl:items-start">
      <form className="flex flex-col gap-4" onSubmit={submit} noValidate>
        {error ? <ApiErrorAlert error={error} /> : null}
        <Card>
          <CardHeader>
            <CardTitle>
              <h2 className="text-base font-semibold">{t("app.platform.branding.identity")}</h2>
            </CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            <FormField
              control={form.control}
              name="useAppName"
              label={t("app.platform.branding.useAppName")}
            >
              {(field, props) => (
                <Switch
                  {...props}
                  checked={field.value}
                  onCheckedChange={field.onChange}
                  onBlur={field.onBlur}
                  ref={field.ref}
                />
              )}
            </FormField>
            <FormField
              control={form.control}
              name="appName"
              label={t("app.platform.branding.appName")}
            >
              {(field, props) => <Input {...field} {...props} maxLength={100} />}
            </FormField>
            <ImageField
              slug={slug}
              image="logo"
              label={t("app.platform.branding.logo")}
              hint={t("app.platform.branding.logoHint")}
              version={branding.logoVersion}
            />
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>
              <h2 className="text-base font-semibold">{t("app.platform.branding.theme")}</h2>
            </CardTitle>
            <CardDescription>{t("app.platform.branding.contrastNote")}</CardDescription>
          </CardHeader>
          <CardContent className="grid gap-4 sm:grid-cols-3">
            <ChoiceField
              control={form.control}
              name="themeFill"
              label={t("app.platform.branding.fillLabel")}
              choices={["Gradient", "Solid"]}
              choiceLabel={(choice) => t(`app.platform.branding.fill.${choice}`)}
            />
            <ColorField
              control={form.control}
              name="primaryColor"
              label={t("app.platform.branding.primaryColor")}
            />
            <ColorField
              control={form.control}
              name="accentColor"
              label={t("app.platform.branding.accentColor")}
            />
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>
              <h2 className="text-base font-semibold">{t("app.platform.branding.background")}</h2>
            </CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            <ChoiceField
              control={form.control}
              name="backgroundKind"
              label={t("app.platform.branding.backgroundKind")}
              choices={["Gradient", "Solid", "Image"]}
              choiceLabel={(choice) => t(`app.platform.branding.kind.${choice}`)}
            />
            {values.backgroundKind === "Gradient" ? (
              <div className="grid gap-4 sm:grid-cols-2">
                <ColorField
                  control={form.control}
                  name="backgroundStartColor"
                  label={t("app.platform.branding.startColor")}
                />
                <ColorField
                  control={form.control}
                  name="backgroundEndColor"
                  label={t("app.platform.branding.endColor")}
                />
              </div>
            ) : null}
            {values.backgroundKind === "Solid" ? (
              <ColorField
                control={form.control}
                name="backgroundColor"
                label={t("app.platform.branding.color")}
              />
            ) : null}
            {values.backgroundKind === "Image" ? (
              <ImageField
                slug={slug}
                image="background"
                label={t("app.platform.branding.backgroundImage")}
                hint={t("app.platform.branding.backgroundHint")}
                version={branding.background.imageVersion}
              />
            ) : null}
          </CardContent>
        </Card>

        <div>
          <Button type="submit" disabled={form.formState.isSubmitting}>
            {t("Save")}
          </Button>
        </div>
      </form>
      <BrandingPreview slug={slug} values={values} branding={branding} />
    </div>
  );
}

/**
 * What the tenant app will look like: the login background and a button in the tenant colours (the same token
 * computation as the app, so the preview shows the accessible shades actually used).
 */
function BrandingPreview({
  slug,
  values,
  branding,
}: {
  slug: string;
  values: BrandingValues;
  branding: TenantBranding;
}) {
  const t = useTranslations();
  const tokens = brandingTokens({
    primaryColor: values.primaryColor,
    accentColor: values.accentColor,
    themeFill: values.themeFill,
  }).light;
  const background = loginBackground(slug, {
    kind: values.backgroundKind,
    startColor: values.backgroundStartColor,
    endColor: values.backgroundEndColor,
    color: values.backgroundColor,
    imageVersion: branding.background.imageVersion,
  });
  const showLogo = !values.useAppName && branding.logoVersion;

  return (
    <Card className="xl:sticky xl:top-20">
      <CardHeader>
        <CardTitle>
          <h2 className="text-base font-semibold">{t("app.platform.branding.preview")}</h2>
        </CardTitle>
      </CardHeader>
      <CardContent
        className="flex flex-col gap-3"
        style={tokens as React.CSSProperties}
        data-testid="branding-preview"
      >
        <div
          className={
            background
              ? "flex h-36 items-end rounded-md p-4"
              : "flex h-36 items-end rounded-md bg-brand p-4 text-white"
          }
          style={background}
          aria-hidden
        >
          {showLogo ? null : (
            <span className="text-xl font-semibold drop-shadow-sm">{values.appName}</span>
          )}
        </div>
        <div className="flex items-center justify-between gap-2 rounded-md border p-3">
          {showLogo ? (
            // eslint-disable-next-line @next/next/no-img-element -- a same-origin preview of the uploaded logo
            <img
              src={brandingImageUrl(slug, "logo", branding.logoVersion!)}
              alt={values.appName}
              className="h-8 w-auto max-w-32 object-contain"
            />
          ) : (
            <span className="truncate font-semibold">{values.appName}</span>
          )}
          <Button type="button" size="sm" tabIndex={-1} aria-hidden>
            {t("app.platform.branding.previewButton")}
          </Button>
        </div>
        <div className="h-3 rounded-full bg-brand" aria-hidden />
      </CardContent>
    </Card>
  );
}

function ChoiceField<TName extends FieldPath<BrandingValues>>({
  control,
  name,
  label,
  choices,
  choiceLabel,
}: {
  control: Control<BrandingValues>;
  name: TName;
  label: string;
  choices: readonly string[];
  choiceLabel: (choice: string) => string;
}) {
  return (
    <FormField control={control} name={name} label={label}>
      {(field, props) => (
        <Select value={String(field.value)} onValueChange={field.onChange}>
          <SelectTrigger {...props} className="w-full" onBlur={field.onBlur} ref={field.ref}>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {choices.map((choice) => (
              <SelectItem key={choice} value={choice}>
                {choiceLabel(choice)}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      )}
    </FormField>
  );
}

/** A hex colour: text input (the value) with a native picker beside it. */
function ColorField({
  control,
  name,
  label,
}: {
  control: Control<BrandingValues>;
  name: FieldPath<BrandingValues>;
  label: string;
}) {
  return (
    <FormField control={control} name={name} label={label}>
      {(field, props) => (
        <div className="flex gap-2">
          <input
            type="color"
            className="h-9 w-11 shrink-0 cursor-pointer rounded-md border bg-background p-1"
            value={sixDigitColor(String(field.value))}
            onChange={(event) => field.onChange(event.target.value)}
            tabIndex={-1}
            aria-hidden
          />
          <Input
            {...props}
            name={field.name}
            value={String(field.value)}
            onChange={field.onChange}
            onBlur={field.onBlur}
            ref={field.ref}
            maxLength={7}
            spellCheck={false}
            autoComplete="off"
          />
        </div>
      )}
    </FormField>
  );
}

/** Uploads or removes an image right away (outside the settings form); the answer is the new branding. */
function ImageField({
  slug,
  image,
  label,
  hint,
  version,
}: {
  slug: string;
  image: BrandingImageKind;
  label: string;
  hint: string;
  version: string | null | undefined;
}) {
  const t = useTranslations();
  const notify = useNotify();
  const client = useQueryClient();
  const id = `branding-${image}`;
  const input = React.useRef<HTMLInputElement>(null);
  const save = useMutation({
    mutationFn: (file: File | null) => saveBrandingImage(slug, image, file),
    onSuccess: (next, file) => {
      client.setQueryData(tenantKey(slug, "branding"), next);
      notify.success(
        file ? "app.platform.branding.imageSaved" : "app.platform.branding.imageRemoved",
      );
      if (input.current) {
        input.current.value = "";
      }
    },
  });
  const fieldError = isApiError(save.error) ? save.error.fieldErrors.file?.[0] : undefined;
  const errorText = fieldError ? (t.has(fieldError) ? t(fieldError) : fieldError) : undefined;

  return (
    <div className="flex flex-col gap-2">
      <Label htmlFor={id}>{label}</Label>
      <div className="flex flex-wrap items-center gap-3">
        <div className="flex h-16 w-28 items-center justify-center overflow-hidden rounded-md border bg-muted">
          {version ? (
            // eslint-disable-next-line @next/next/no-img-element -- same-origin image of the tenant, cached by version
            <img
              src={brandingImageUrl(slug, image, version)}
              alt={label}
              className="max-h-full max-w-full object-contain"
            />
          ) : (
            <span className="text-xs text-muted-foreground">
              {t("app.platform.branding.noImage")}
            </span>
          )}
        </div>
        <div className="flex flex-col gap-2">
          <Input
            ref={input}
            id={id}
            type="file"
            accept="image/png,image/jpeg,image/webp"
            disabled={save.isPending}
            aria-describedby={`${id}-hint${errorText ? ` ${id}-error` : ""}`}
            aria-invalid={errorText ? true : undefined}
            onChange={(event) => {
              const file = event.target.files?.[0];
              if (file) {
                save.mutate(file);
              }
            }}
          />
          {version ? (
            <Button
              type="button"
              variant="outline"
              size="sm"
              className="self-start"
              disabled={save.isPending}
              onClick={() => save.mutate(null)}
            >
              {t("app.platform.branding.removeImage", { image: label })}
            </Button>
          ) : null}
        </div>
      </div>
      <p id={`${id}-hint`} className="text-sm text-muted-foreground">
        {hint}
      </p>
      {errorText ? (
        <p id={`${id}-error`} className="text-sm text-destructive">
          {errorText}
        </p>
      ) : save.error ? (
        <ApiErrorAlert error={save.error} />
      ) : null}
    </div>
  );
}
