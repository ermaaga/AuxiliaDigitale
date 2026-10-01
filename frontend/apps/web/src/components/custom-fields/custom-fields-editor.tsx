"use client";

import * as React from "react";
import { useTranslations } from "next-intl";
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

import type { CustomFieldDefinition } from "@/components/data-table/table-model";

import type { CustomFieldValues } from "./custom-field-value";

const NONE = "__none__";

/**
 * Inputs of the custom fields of an entity for create and edit forms (F20, legacy `CustomFieldsEditor`), by type. The
 * API validates the values again (`AUX-20018`): its field errors `customFields.<key>` are shown under each input.
 */
export function CustomFieldsEditor({
  idPrefix,
  definitions,
  values,
  onChange,
  errors,
}: {
  idPrefix: string;
  definitions: readonly CustomFieldDefinition[];
  values: CustomFieldValues;
  onChange: (values: CustomFieldValues) => void;
  /** API field errors (`customFields.<key>` → translation keys). */
  errors?: Readonly<Record<string, readonly string[]>>;
}) {
  const t = useTranslations();
  const set = (key: string, value: unknown) => onChange({ ...values, [key]: value });
  const sorted = [...definitions].sort((a, b) => a.order - b.order);

  return (
    <div className="grid gap-4 sm:grid-cols-2">
      {sorted.map((definition) => {
        const id = `${idPrefix}-${definition.key}`;
        const error = errors?.[`customFields.${definition.key}`]?.[0];
        const errorId = error ? `${id}-error` : undefined;
        const value = values[definition.key];
        const label = definition.isRequired ? `${definition.label} *` : definition.label;

        return (
          <div key={definition.key} className="flex flex-col gap-2">
            {definition.type === "Boolean" ? (
              <div className="flex items-center gap-2 pt-6">
                <Checkbox
                  id={id}
                  checked={value === true}
                  aria-describedby={errorId}
                  aria-invalid={error ? true : undefined}
                  onCheckedChange={(checked) => set(definition.key, checked === true)}
                />
                <Label htmlFor={id}>{label}</Label>
              </div>
            ) : definition.type === "MultiSelect" ? (
              <fieldset className="flex flex-col gap-2" aria-describedby={errorId}>
                <legend className="mb-1 text-sm font-medium">{label}</legend>
                {(definition.options ?? []).map((option) => {
                  const chosen = Array.isArray(value) ? (value as string[]) : [];
                  return (
                    <div key={option} className="flex items-center gap-2">
                      <Checkbox
                        id={`${id}-${option}`}
                        checked={chosen.includes(option)}
                        onCheckedChange={(checked) =>
                          set(
                            definition.key,
                            checked === true
                              ? [...chosen, option]
                              : chosen.filter((item) => item !== option),
                          )
                        }
                      />
                      <Label htmlFor={`${id}-${option}`}>{option}</Label>
                    </div>
                  );
                })}
              </fieldset>
            ) : (
              <>
                <Label htmlFor={id}>{label}</Label>
                {definition.type === "Select" ? (
                  <Select
                    value={typeof value === "string" ? value : NONE}
                    onValueChange={(next) => set(definition.key, next === NONE ? null : next)}
                  >
                    <SelectTrigger
                      id={id}
                      className="w-full"
                      aria-describedby={errorId}
                      aria-invalid={error ? true : undefined}
                    >
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value={NONE}>—</SelectItem>
                      {(definition.options ?? []).map((option) => (
                        <SelectItem key={option} value={option}>
                          {option}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                ) : (
                  <Input
                    id={id}
                    type={
                      definition.type === "Number"
                        ? "number"
                        : definition.type === "Date"
                          ? "date"
                          : "text"
                    }
                    step={definition.type === "Number" ? "any" : undefined}
                    value={value === null || value === undefined ? "" : String(value)}
                    aria-describedby={errorId}
                    aria-invalid={error ? true : undefined}
                    onChange={(event) => {
                      const text = event.target.value;
                      set(
                        definition.key,
                        text === "" ? null : definition.type === "Number" ? Number(text) : text,
                      );
                    }}
                  />
                )}
              </>
            )}
            {error ? (
              <p id={errorId} className="text-sm text-destructive">
                {t.has(error) ? t(error) : t("validation.invalid")}
              </p>
            ) : null}
          </div>
        );
      })}
    </div>
  );
}
