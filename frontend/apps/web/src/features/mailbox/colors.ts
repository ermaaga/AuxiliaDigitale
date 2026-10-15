import type * as React from "react";

/** The palette of the accounts (`ConnectedAccount.Colors` of the API), tokens `--account-<name>` in light and dark. */
export const ACCOUNT_COLORS = [
  "blue",
  "green",
  "orange",
  "purple",
  "red",
  "teal",
  "pink",
  "yellow",
] as const;

export type AccountColor = (typeof ACCOUNT_COLORS)[number];

/** The token of an account colour; an unknown name (never sent by the API) falls back to the muted colour. */
export function accountColorVar(color: string): string {
  return (ACCOUNT_COLORS as readonly string[]).includes(color)
    ? `var(--account-${color})`
    : "var(--muted-foreground)";
}

/** Style of a swatch (dot, legend, calendar event) in the colour of an account. */
export function accountColorStyle(color: string): React.CSSProperties {
  return { backgroundColor: accountColorVar(color) };
}
