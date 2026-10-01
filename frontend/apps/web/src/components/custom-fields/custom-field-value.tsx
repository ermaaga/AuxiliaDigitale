import * as React from "react";
import { parseHexColor, readableForeground, toHex } from "@auxilia/ui/lib/branding";

import type { CustomFieldDefinition } from "@/components/data-table/table-model";

export type CustomFieldValues = Readonly<Record<string, unknown>>;

/** A value as text (dates as stored `yyyy-MM-dd`, multi-selects comma-separated); empty → `undefined`. */
export function customFieldText(
  definition: CustomFieldDefinition,
  value: unknown,
): string | undefined {
  if (value === null || value === undefined || value === "") {
    return undefined;
  }

  if (Array.isArray(value)) {
    return value.length === 0 ? undefined : value.join(", ");
  }

  if (typeof value === "boolean") {
    return value ? definition.label : undefined;
  }

  return String(value);
}

/** Badge colours from the configured colour: text white or ink, whichever reads (WCAG AA); the platform badge otherwise. */
export function badgeStyle(color: string | null | undefined): React.CSSProperties | undefined {
  const parsed = parseHexColor(color);
  return parsed
    ? {
        backgroundColor: toHex(parsed),
        color: toHex(readableForeground(parsed)),
        borderColor: "transparent",
      }
    : undefined;
}

/**
 * The fields of a grid column for one record (F20, legacy `DataGrid`): a true boolean is a badge with the field name in
 * its colour, other values are shown as text (prefixed by the label when several fields share the column).
 */
export function CustomFieldCell({
  fields,
  values,
}: {
  fields: readonly CustomFieldDefinition[];
  values: CustomFieldValues | null | undefined;
}) {
  const items = fields
    .map((field) => ({ field, text: customFieldText(field, values?.[field.key]) }))
    .filter(
      (item): item is { field: CustomFieldDefinition; text: string } => item.text !== undefined,
    );
  if (items.length === 0) {
    return <span className="text-muted-foreground">—</span>;
  }

  return (
    <span className="flex flex-wrap gap-1">
      {items.map(({ field, text }) =>
        field.type === "Boolean" ? (
          <span
            key={field.key}
            className="inline-flex items-center rounded-md border px-2 py-0.5 text-xs font-medium"
            style={badgeStyle(field.badgeColor)}
          >
            {text}
          </span>
        ) : (
          <span key={field.key}>{fields.length > 1 ? `${field.label}: ${text}` : text}</span>
        ),
      )}
    </span>
  );
}
