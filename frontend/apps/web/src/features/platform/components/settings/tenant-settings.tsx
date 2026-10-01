"use client";

import * as React from "react";
import { isApiError } from "@auxilia/api-client";
import { useTranslations } from "next-intl";
import { Badge } from "@auxilia/ui/components/badge";
import { Button } from "@auxilia/ui/components/button";
import { Card, CardContent, CardHeader, CardTitle } from "@auxilia/ui/components/card";
import { Input } from "@auxilia/ui/components/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@auxilia/ui/components/select";
import { Skeleton } from "@auxilia/ui/components/skeleton";
import { Switch } from "@auxilia/ui/components/switch";

import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { useNotify } from "@/lib/notify";

import { useSaveSetting, useTenantSettings, type TenantSetting } from "../../settings-api";

/** A JSON value as the editor shows it (`7`, `true`, `Gradient`). */
export function displayValue(value: unknown): string {
  if (value === null || value === undefined) {
    return "";
  }

  return typeof value === "string" ? value : JSON.stringify(value);
}

/** The text of an input as the JSON value of its kind (`undefined` when it is not a number). */
export function parseInput(kind: string, text: string): unknown {
  if (kind === "integer" || kind === "number") {
    const trimmed = text.trim();
    const number = Number(trimmed);
    return trimmed === "" || Number.isNaN(number) ? undefined : number;
  }

  return text;
}

/** Settings grouped by module, in the order the API gives them (module, then key). */
export function groupByModule(settings: readonly TenantSetting[]) {
  const groups = new Map<string, TenantSetting[]>();
  for (const setting of settings) {
    groups.set(setting.module, [...(groups.get(setting.module) ?? []), setting]);
  }

  return [...groups.entries()].map(([module, items]) => ({ module, items }));
}

/**
 * The settings editor of a tenant (F23): every setting the tenant level allows, grouped by module, with its default,
 * the platform value and where the effective value comes from. A tenant value can be restored to the next level.
 */
export function TenantSettings({ slug }: { slug: string }) {
  const t = useTranslations();
  const settings = useTenantSettings(slug);

  if (settings.isPending) {
    return (
      <div className="flex flex-col gap-4" aria-busy>
        <Skeleton className="h-40 w-full" />
        <Skeleton className="h-40 w-full" />
      </div>
    );
  }

  if (settings.error) {
    return <ApiErrorAlert error={settings.error} onRetry={() => void settings.refetch()} />;
  }

  return (
    <div className="flex flex-col gap-4">
      {groupByModule(settings.data).map(({ module, items }) => {
        const nameKey = `modules.${module.toLowerCase()}.name`;
        return (
          <Card key={module}>
            <CardHeader>
              <CardTitle>
                <h2 className="text-base font-semibold">{t.has(nameKey) ? t(nameKey) : module}</h2>
              </CardTitle>
            </CardHeader>
            <CardContent>
              <ul className="flex flex-col divide-y">
                {items.map((setting) => (
                  <li key={setting.key} className="py-3 first:pt-0 last:pb-0">
                    <SettingRow slug={slug} setting={setting} />
                  </li>
                ))}
              </ul>
            </CardContent>
          </Card>
        );
      })}
    </div>
  );
}

function SettingRow({ slug, setting }: { slug: string; setting: TenantSetting }) {
  const t = useTranslations();
  const notify = useNotify();
  const save = useSaveSetting(slug);
  const id = `setting-${setting.key.replaceAll(".", "-")}`;
  const descriptionKey = `settings.${setting.key}.description`;
  const label = t.has(descriptionKey) ? t(descriptionKey) : setting.key;
  const fieldError = isApiError(save.error) ? save.error.fieldErrors.value?.[0] : undefined;
  const errorText = fieldError ? (t.has(fieldError) ? t(fieldError) : fieldError) : undefined;

  function run(value: unknown, done: string) {
    save.mutate({ key: setting.key, value }, { onSuccess: () => notify.success(done) });
  }

  const sourceText = t(`app.platform.settings.source.${setting.source}`);
  const details = [
    setting.kind === "secret"
      ? null
      : t("app.platform.settings.default", { value: displayValue(setting.defaultValue) }),
    setting.platformValue === null || setting.platformValue === undefined
      ? null
      : t("app.platform.settings.platformValue", { value: displayValue(setting.platformValue) }),
  ].filter(Boolean);

  return (
    <div className="grid gap-3 md:grid-cols-[minmax(0,1fr)_minmax(0,22rem)] md:items-start">
      <div className="flex min-w-0 flex-col gap-1">
        <label htmlFor={id} className="text-sm font-medium">
          {label}
        </label>
        <p
          id={`${id}-details`}
          className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground"
        >
          <code className="break-all">{setting.key}</code>
          <Badge variant={setting.source === "Tenant" ? "default" : "outline"}>
            {t("app.platform.settings.applies")}: {sourceText}
          </Badge>
          {details.map((text) => (
            <span key={text}>{text}</span>
          ))}
        </p>
      </div>
      <div className="flex flex-col gap-2">
        <SettingControl
          key={JSON.stringify(setting.effectiveValue ?? null) + String(setting.hasTenantValue)}
          id={id}
          label={label}
          setting={setting}
          busy={save.isPending}
          invalid={Boolean(errorText)}
          onSave={(value) => run(value, "app.platform.settings.saved")}
        />
        {errorText ? (
          <p id={`${id}-error`} className="text-sm text-destructive">
            {errorText}
          </p>
        ) : save.error ? (
          <ApiErrorAlert error={save.error} />
        ) : null}
        {setting.hasTenantValue ? (
          <Button
            variant="ghost"
            size="sm"
            className="self-start"
            disabled={save.isPending}
            onClick={() => run(null, "app.platform.settings.restored")}
            aria-label={t("app.platform.settings.resetSetting", { setting: label })}
          >
            {t("app.platform.settings.reset")}
          </Button>
        ) : null}
      </div>
    </div>
  );
}

function SettingControl({
  id,
  label,
  setting,
  busy,
  invalid,
  onSave,
}: {
  id: string;
  label: string;
  setting: TenantSetting;
  busy: boolean;
  invalid: boolean;
  onSave: (value: unknown) => void;
}) {
  const t = useTranslations();
  const effective = displayValue(setting.effectiveValue);
  // Keyed by the effective value (see SettingRow): a saved or restored value starts a fresh input.
  const [text, setText] = React.useState(setting.kind === "secret" ? "" : effective);
  const describedBy = [`${id}-details`, invalid ? `${id}-error` : null].filter(Boolean).join(" ");

  if (setting.kind === "boolean") {
    return (
      <div className="flex items-center gap-2">
        <Switch
          id={id}
          checked={setting.effectiveValue === true}
          disabled={busy}
          aria-describedby={describedBy}
          onCheckedChange={(checked) => onSave(checked)}
        />
        <span className="text-sm" aria-hidden>
          {setting.effectiveValue === true
            ? t("app.platform.settings.on")
            : t("app.platform.settings.off")}
        </span>
      </div>
    );
  }

  if (setting.kind === "choice") {
    return (
      <Select value={effective} disabled={busy} onValueChange={(value) => onSave(value)}>
        <SelectTrigger id={id} className="w-full" aria-describedby={describedBy}>
          <SelectValue placeholder={label} />
        </SelectTrigger>
        <SelectContent>
          {(setting.choices ?? []).map((choice) => (
            <SelectItem key={choice} value={choice}>
              {choice}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    );
  }

  const secret = setting.kind === "secret";
  const value = parseInput(setting.kind, text);
  return (
    <form
      className="flex gap-2"
      noValidate
      onSubmit={(event) => {
        event.preventDefault();
        onSave(value ?? text);
      }}
    >
      <Input
        id={id}
        type={secret ? "password" : setting.kind === "string" ? "text" : "number"}
        inputMode={setting.kind === "integer" ? "numeric" : undefined}
        autoComplete={secret ? "new-password" : "off"}
        placeholder={
          secret
            ? setting.hasTenantValue
              ? t("app.platform.settings.secretStored")
              : t("app.platform.settings.secretEmpty")
            : undefined
        }
        value={text}
        aria-invalid={invalid || undefined}
        aria-describedby={describedBy}
        onChange={(event) => setText(event.target.value)}
      />
      <Button
        type="submit"
        variant="outline"
        disabled={busy || (!secret && text === effective) || (secret && text === "")}
        aria-label={t("app.platform.settings.saveSetting", { setting: label })}
      >
        {t("Save")}
      </Button>
    </form>
  );
}
