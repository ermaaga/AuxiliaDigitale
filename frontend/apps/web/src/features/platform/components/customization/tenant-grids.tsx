"use client";

import * as React from "react";
import { ArrowDownIcon, ArrowUpIcon } from "lucide-react";
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
import { Checkbox } from "@auxilia/ui/components/checkbox";
import { Label } from "@auxilia/ui/components/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@auxilia/ui/components/select";
import { Skeleton } from "@auxilia/ui/components/skeleton";

import { useConfirm } from "@/components/confirm/confirm-provider";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { useNotify } from "@/lib/notify";

import { roleLabel } from "../../labels";
import {
  moveColumn,
  resetGridLayout,
  saveGridLayout,
  useCustomizationMutation,
  useGrids,
  type Grid,
} from "../../customization-api";

/** Grid layouts per role (F21, D-18): which columns each role sees and in which order. */
export function TenantGrids({ slug }: { slug: string }) {
  const t = useTranslations();
  const grids = useGrids(slug);

  if (grids.isPending) {
    return <Skeleton className="h-64 w-full" aria-busy />;
  }

  if (grids.error) {
    return <ApiErrorAlert error={grids.error} onRetry={() => void grids.refetch()} />;
  }

  if (grids.data.length === 0) {
    return <p className="text-sm text-muted-foreground">{t("app.platform.grids.none")}</p>;
  }

  return (
    <div className="flex flex-col gap-4">
      {grids.data.map((grid) => (
        <GridCard key={grid.key} slug={slug} grid={grid} />
      ))}
    </div>
  );
}

function GridCard({ slug, grid }: { slug: string; grid: Grid }) {
  const t = useTranslations();
  const [role, setRole] = React.useState(grid.layouts[0]?.role ?? "");
  const layout = grid.layouts.find((item) => item.role === role);
  const name = t.has(grid.nameKey) ? t(grid.nameKey) : grid.key;
  const selectId = `grid-role-${grid.key.replaceAll(".", "-")}`;
  const headingId = `grid-${grid.key.replaceAll(".", "-")}`;

  // One region per grid: several grids repeat column names (e.g. "Username"), so their controls are told apart.
  return (
    <section aria-labelledby={headingId}>
      <Card>
        <CardHeader className="flex flex-row flex-wrap items-end justify-between gap-3">
          <div className="flex flex-col gap-1.5">
            <CardTitle>
              <h2 id={headingId} className="text-base font-semibold">
                {name}
              </h2>
            </CardTitle>
            <CardDescription>
              <code>{grid.key}</code>
            </CardDescription>
          </div>
          <div className="flex flex-col gap-1">
            <Label htmlFor={selectId}>{t("Role")}</Label>
            <Select value={role} onValueChange={setRole}>
              <SelectTrigger id={selectId} className="w-48">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {grid.layouts.map((item) => (
                  <SelectItem key={item.role} value={item.role}>
                    {roleLabel(t, item.role)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        </CardHeader>
        <CardContent>
          {layout ? (
            <LayoutEditor
              key={`${role}:${JSON.stringify(layout.columns)}`}
              slug={slug}
              grid={grid}
              name={name}
              role={role}
              initial={layout.columns}
              isCustomized={layout.isCustomized}
            />
          ) : null}
        </CardContent>
      </Card>
    </section>
  );
}

function LayoutEditor({
  slug,
  grid,
  name,
  role,
  initial,
  isCustomized,
}: {
  slug: string;
  grid: Grid;
  name: string;
  role: string;
  initial: ReadonlyArray<{ key: string; visible: boolean }>;
  isCustomized: boolean;
}) {
  const t = useTranslations();
  const notify = useNotify();
  const confirm = useConfirm();
  const [columns, setColumns] = React.useState(() => [...initial]);
  const save = useCustomizationMutation(slug, () => saveGridLayout(slug, grid.key, role, columns));
  const reset = useCustomizationMutation(slug, () => resetGridLayout(slug, grid.key, role));
  const definitions = new Map(grid.columns.map((column) => [column.key, column]));
  const label = (key: string) => {
    const labelKey = definitions.get(key)?.labelKey ?? key;
    return t.has(labelKey) ? t(labelKey) : key;
  };

  return (
    <form
      className="flex flex-col gap-3"
      onSubmit={(event) => {
        event.preventDefault();
        // Not per-call callbacks: the refreshed grids remount this editor.
        save.mutateAsync(undefined).then(
          () => notify.success("app.platform.grids.saved"),
          () => undefined,
        );
      }}
    >
      <p className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
        {isCustomized ? (
          <Badge>{t("app.platform.grids.customized")}</Badge>
        ) : (
          <Badge variant="outline">{t("app.platform.grids.default")}</Badge>
        )}
        {t("app.platform.grids.hint")}
      </p>
      {save.error ? <ApiErrorAlert error={save.error} /> : null}
      <ol
        className="flex flex-col divide-y rounded-md border"
        aria-label={t("app.platform.grids.columnsOf", { grid: name, role: roleLabel(t, role) })}
      >
        {columns.map((column, index) => {
          const definition = definitions.get(column.key);
          const id = `grid-${grid.key.replaceAll(".", "-")}-${column.key}`;
          const text = label(column.key);
          return (
            <li key={column.key} className="flex items-center gap-3 px-3 py-2">
              <Checkbox
                id={id}
                checked={column.visible}
                disabled={definition?.canHide === false}
                onCheckedChange={(checked) =>
                  setColumns((current) =>
                    current.map((item) =>
                      item.key === column.key ? { ...item, visible: checked === true } : item,
                    ),
                  )
                }
              />
              <Label htmlFor={id} className="flex-1">
                {text}
              </Label>
              <span className="hidden gap-1 sm:flex">
                {definition?.sortable ? (
                  <Badge variant="outline">{t("app.platform.grids.sortable")}</Badge>
                ) : null}
                {definition?.filterable ? (
                  <Badge variant="outline">{t("app.platform.grids.filterable")}</Badge>
                ) : null}
              </span>
              <Button
                type="button"
                variant="ghost"
                size="icon"
                disabled={index === 0}
                aria-label={t("app.platform.grids.moveUp", { column: text })}
                onClick={() => setColumns((current) => moveColumn(current, index, -1))}
              >
                <ArrowUpIcon aria-hidden />
              </Button>
              <Button
                type="button"
                variant="ghost"
                size="icon"
                disabled={index === columns.length - 1}
                aria-label={t("app.platform.grids.moveDown", { column: text })}
                onClick={() => setColumns((current) => moveColumn(current, index, 1))}
              >
                <ArrowDownIcon aria-hidden />
              </Button>
            </li>
          );
        })}
      </ol>
      <div className="flex flex-wrap gap-2">
        <Button type="submit" disabled={save.isPending}>
          {t("Save")}
        </Button>
        {isCustomized ? (
          <Button
            type="button"
            variant="outline"
            disabled={reset.isPending}
            onClick={async () => {
              if (
                await confirm({
                  title: t("app.platform.grids.resetTitle"),
                  description: t("app.platform.grids.resetText", {
                    grid: name,
                    role: roleLabel(t, role),
                  }),
                  confirmLabel: t("app.platform.grids.reset"),
                })
              ) {
                await reset.mutateAsync(undefined).then(
                  () => notify.success("app.platform.grids.resetDone"),
                  (error: unknown) => notify.error(error),
                );
              }
            }}
          >
            {t("app.platform.grids.reset")}
          </Button>
        ) : null}
      </div>
    </form>
  );
}
