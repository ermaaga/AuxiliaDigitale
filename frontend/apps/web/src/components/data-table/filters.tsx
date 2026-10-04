"use client";

import * as React from "react";
import { useTranslations } from "next-intl";
import { Input } from "@auxilia/ui/components/input";
import { Label } from "@auxilia/ui/components/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@auxilia/ui/components/select";

const ALL = "all";

/**
 * Text search of a table toolbar: typing updates the field at once and `onChange` (the URL filter) 300 ms after the
 * last keystroke; a filter cleared elsewhere (e.g. "Clear filters") empties the field.
 */
export function SearchFilter({
  id,
  label,
  placeholder,
  value,
  onChange,
}: {
  id: string;
  label: string;
  placeholder: string;
  value: string | undefined;
  onChange: (value: string | undefined) => void;
}) {
  const [text, setText] = React.useState(value ?? "");
  const [applied, setApplied] = React.useState(value);

  // Adjust the field when the filter changes from outside (render-time update, no effect needed).
  if (value !== applied) {
    setApplied(value);
    if ((value ?? "") !== text.trim()) {
      setText(value ?? "");
    }
  }

  // The latest callback, so a parent re-render does not restart the delay.
  const onChangeRef = React.useRef(onChange);
  React.useEffect(() => {
    onChangeRef.current = onChange;
  });

  React.useEffect(() => {
    const next = text.trim() || undefined;
    if (next === value) {
      return;
    }

    const timer = window.setTimeout(() => onChangeRef.current(next), 300);
    return () => window.clearTimeout(timer);
  }, [text, value]);

  return (
    <div className="flex min-w-48 flex-1 flex-col gap-1 sm:max-w-64">
      <Label htmlFor={id}>{label}</Label>
      <Input
        id={id}
        type="search"
        placeholder={placeholder}
        value={text}
        onChange={(event) => setText(event.target.value)}
      />
    </div>
  );
}

/** A select filter of a table toolbar with an "All" choice (no filter). */
export function FilterSelect({
  id,
  label,
  value,
  onChange,
  options,
}: {
  id: string;
  label: string;
  value: string | undefined;
  onChange: (value: string | undefined) => void;
  options: readonly { value: string; label: string }[];
}) {
  const t = useTranslations();
  return (
    <div className="flex flex-col gap-1">
      <Label htmlFor={id}>{label}</Label>
      <Select
        value={value ?? ALL}
        onValueChange={(next) => onChange(next === ALL ? undefined : next)}
      >
        <SelectTrigger id={id} className="w-40">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value={ALL}>{t("common.table.all")}</SelectItem>
          {options.map((option) => (
            <SelectItem key={option.value} value={option.value}>
              {option.label}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    </div>
  );
}

/** A day filter of a table toolbar (`YYYY-MM-DD` in the URL); empty = no filter. */
export function DateFilter({
  id,
  label,
  value,
  onChange,
}: {
  id: string;
  label: string;
  value: string | undefined;
  onChange: (value: string | undefined) => void;
}) {
  return (
    <div className="flex min-w-36 flex-col gap-1">
      <Label htmlFor={id}>{label}</Label>
      <Input
        id={id}
        type="date"
        value={value ?? ""}
        onChange={(event) => onChange(event.target.value || undefined)}
      />
    </div>
  );
}

/** The instant a local day starts (`from`) or the next one starts (`to`, exclusive end), for date-range filters. */
export function dayBoundary(day: string | undefined, edge: "from" | "to"): string | undefined {
  if (!day || !/^\d{4}-\d{2}-\d{2}$/.test(day)) {
    return undefined;
  }

  const start = new Date(`${day}T00:00:00`);
  if (edge === "to") {
    start.setDate(start.getDate() + 1);
  }

  return start.toISOString();
}
