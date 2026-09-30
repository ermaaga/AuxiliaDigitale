"use client";

import * as React from "react";
import { zodResolver } from "@hookform/resolvers/zod";
import { isApiError } from "@auxilia/api-client";
import { Label } from "@auxilia/ui/components/label";
import { useTranslations } from "next-intl";
import {
  Controller,
  useForm,
  type Control,
  type ControllerRenderProps,
  type FieldPath,
  type FieldValues,
  type Resolver,
  type UseFormProps,
  type UseFormSetError,
} from "react-hook-form";
import type { z } from "zod";

/**
 * react-hook-form with a zod schema (skill auxilia-frontend-feature). Schema messages are translation keys
 * (`z.string().min(1, "validation.notEmpty")`); the schema mirrors the server limits, the API stays the authority.
 */
export function useZodForm<TSchema extends z.ZodType<FieldValues, FieldValues>>(
  schema: TSchema,
  options?: Omit<UseFormProps<z.input<TSchema>, unknown, z.output<TSchema>>, "resolver">,
) {
  // The resolver's generics cannot be inferred from a generic schema; the cast only restates them.
  const resolver = zodResolver(schema as never) as unknown as Resolver<
    z.input<TSchema>,
    unknown,
    z.output<TSchema>
  >;
  return useForm<z.input<TSchema>, unknown, z.output<TSchema>>({ ...options, resolver });
}

/**
 * Puts the field errors of an API validation failure (400, `errors: { field: [keys] }`) on the form fields; returns
 * whether any field matched (otherwise show the error as an alert).
 */
export function applyApiErrors<TValues extends FieldValues>(
  error: unknown,
  setError: UseFormSetError<TValues>,
  fields: readonly FieldPath<TValues>[],
): boolean {
  if (!isApiError(error)) {
    return false;
  }

  let matched = false;
  for (const [field, keys] of Object.entries(error.fieldErrors)) {
    if ((fields as readonly string[]).includes(field) && keys[0]) {
      setError(field as FieldPath<TValues>, { type: "server", message: keys[0] });
      matched = true;
    }
  }

  return matched;
}

/** Accessible props for the control of a field (`id`, `aria-invalid`, `aria-describedby`). */
export type FieldControlProps = {
  id: string;
  "aria-invalid"?: boolean;
  "aria-describedby"?: string;
};

/**
 * A labelled form field (skill auxilia-ui-design: label, description and error linked with `aria-describedby`); the
 * error message is a translation key (zod schema or API) shown translated.
 */
export function FormField<TValues extends FieldValues, TName extends FieldPath<TValues>>({
  control,
  name,
  label,
  description,
  children,
}: {
  control: Control<TValues, unknown, FieldValues>;
  name: TName;
  label: string;
  description?: string;
  children: (
    field: ControllerRenderProps<TValues, TName>,
    props: FieldControlProps,
  ) => React.ReactNode;
}) {
  const t = useTranslations();
  const id = React.useId();
  return (
    <Controller
      control={control}
      name={name}
      render={({ field, fieldState }) => {
        const message = fieldState.error?.message;
        const describedBy = [
          description ? `${id}-description` : undefined,
          message ? `${id}-error` : undefined,
        ]
          .filter(Boolean)
          .join(" ");
        return (
          <div className="flex flex-col gap-2">
            <Label htmlFor={id}>{label}</Label>
            {children(field, {
              id,
              "aria-invalid": message ? true : undefined,
              "aria-describedby": describedBy || undefined,
            })}
            {description ? (
              <p id={`${id}-description`} className="text-sm text-muted-foreground">
                {description}
              </p>
            ) : null}
            {message ? (
              <p id={`${id}-error`} className="text-sm text-destructive">
                {t.has(message) ? t(message) : t("validation.invalid")}
              </p>
            ) : null}
          </div>
        );
      }}
    />
  );
}
