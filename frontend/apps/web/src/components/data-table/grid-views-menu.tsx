"use client";

import * as React from "react";
import { BookmarkIcon, CheckIcon, StarIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import { Checkbox } from "@auxilia/ui/components/checkbox";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@auxilia/ui/components/dialog";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@auxilia/ui/components/dropdown-menu";
import { Input } from "@auxilia/ui/components/input";
import { Label } from "@auxilia/ui/components/label";

import { useConfirm } from "@/components/confirm/confirm-provider";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { useNotify } from "@/lib/notify";

import { currentView, useGridViews, useSaveGridView, type GridView } from "./use-grid-views";

/** What a list gives the views menu: its grid, the state in the URL and how to apply a view to it. */
export type GridViewsBinding = {
  tenant: string;
  gridKey: string;
  filters: Record<string, string | undefined>;
  /** No filter and no sort yet: the default view applies when the list opens. */
  pristine: boolean;
  applyView: (view: { filters: Record<string, string>; sort: string | null | undefined }) => void;
};

/** The views binding of a list from its table state (`useTableState`). */
export function gridViews(
  tenant: string,
  gridKey: string,
  table: Pick<GridViewsBinding, "filters" | "pristine" | "applyView">,
): GridViewsBinding {
  return {
    tenant,
    gridKey,
    filters: table.filters,
    pristine: table.pristine,
    applyView: table.applyView,
  };
}

type Props = GridViewsBinding & {
  sort: string | null;
  hiddenColumns: readonly string[];
  onHiddenColumns: (hidden: readonly string[]) => void;
};

/**
 * Personal views of a list (F21): apply one (columns, filters, sort), save the current state as a new view or over the
 * one in use, make it the default (applied when the list opens untouched) and delete it.
 */
export function GridViewsMenu(props: Props) {
  const t = useTranslations();
  const notify = useNotify();
  const confirm = useConfirm();
  const views = useGridViews(props.tenant, props.gridKey);
  const { save, remove } = useSaveGridView(props.tenant, props.gridKey);
  const [activeId, setActiveId] = React.useState<string | null>(null);
  const [saving, setSaving] = React.useState(false);
  const applied = React.useRef(false);
  const active = views.data?.find((view) => view.id === activeId);

  const { applyView, onHiddenColumns, pristine } = props;
  const apply = React.useCallback(
    (view: GridView) => {
      onHiddenColumns(view.hiddenColumns);
      applyView({ filters: view.filters, sort: view.sort });
      setActiveId(view.id);
    },
    [applyView, onHiddenColumns],
  );

  // The default view, once, when the list opens without filters or sort of its own (a shared link keeps its own).
  React.useEffect(() => {
    if (applied.current || !views.data) {
      return;
    }

    const preferred = views.data.find((view) => view.isDefault);
    if (!preferred || !pristine) {
      applied.current = true;
      return;
    }

    const timer = window.setTimeout(() => {
      applied.current = true;
      apply(preferred);
    });
    return () => window.clearTimeout(timer);
  }, [views.data, pristine, apply]);

  if (views.isError) {
    // A grid the user's roles do not see has no views: the list works without the menu.
    return null;
  }

  const update = async (view: GridView, isDefault: boolean, withCurrentState: boolean) => {
    const body = withCurrentState
      ? currentView(view.name, props.hiddenColumns, props.filters, props.sort, isDefault)
      : {
          name: view.name,
          hiddenColumns: view.hiddenColumns,
          filters: view.filters,
          sort: view.sort,
          isDefault,
        };
    await save.mutateAsync({ id: view.id, body }).then(
      () => notify.success("common.table.views.saved"),
      (error: unknown) => notify.error(error),
    );
  };

  const onDelete = async (view: GridView) => {
    const confirmed = await confirm({
      title: t("common.table.views.deleteTitle", { name: view.name }),
      description: t("common.table.views.deleteText"),
      confirmLabel: t("Delete"),
      variant: "destructive",
    });
    if (confirmed) {
      await remove.mutateAsync(view.id).then(
        () => {
          if (activeId === view.id) {
            setActiveId(null);
          }

          notify.success("common.table.views.deleted");
        },
        (error: unknown) => notify.error(error),
      );
    }
  };

  return (
    <>
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button type="button" variant="outline" size="sm">
            <BookmarkIcon aria-hidden /> {active ? active.name : t("common.table.views.title")}
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end" className="w-64">
          <DropdownMenuLabel>{t("common.table.views.title")}</DropdownMenuLabel>
          {views.data?.length ? (
            views.data.map((view) => (
              <DropdownMenuItem key={view.id} onSelect={() => apply(view)}>
                {view.id === activeId ? <CheckIcon aria-hidden /> : <span className="size-4" />}
                <span className="flex-1 truncate">{view.name}</span>
                {view.isDefault ? (
                  <StarIcon aria-label={t("common.table.views.default")} className="fill-current" />
                ) : null}
              </DropdownMenuItem>
            ))
          ) : (
            <p className="px-2 py-1.5 text-sm text-muted-foreground">
              {t("common.table.views.none")}
            </p>
          )}
          <DropdownMenuSeparator />
          <DropdownMenuItem onSelect={() => setSaving(true)}>
            {t("common.table.views.saveNew")}
          </DropdownMenuItem>
          {active ? (
            <>
              <DropdownMenuItem onSelect={() => void update(active, active.isDefault, true)}>
                {t("common.table.views.saveOver", { name: active.name })}
              </DropdownMenuItem>
              <DropdownMenuItem onSelect={() => void update(active, !active.isDefault, false)}>
                {active.isDefault
                  ? t("common.table.views.unsetDefault")
                  : t("common.table.views.setDefault")}
              </DropdownMenuItem>
              <DropdownMenuItem variant="destructive" onSelect={() => void onDelete(active)}>
                {t("common.table.views.delete", { name: active.name })}
              </DropdownMenuItem>
            </>
          ) : null}
        </DropdownMenuContent>
      </DropdownMenu>
      {saving ? (
        <SaveViewDialog
          onClose={() => setSaving(false)}
          onSave={async (name, isDefault) => {
            const created = await save.mutateAsync({
              body: currentView(name, props.hiddenColumns, props.filters, props.sort, isDefault),
            });
            setActiveId(created.id);
            notify.success("common.table.views.saved");
          }}
        />
      ) : null}
    </>
  );
}

function SaveViewDialog({
  onClose,
  onSave,
}: {
  onClose: () => void;
  onSave: (name: string, isDefault: boolean) => Promise<void>;
}) {
  const t = useTranslations();
  const [name, setName] = React.useState("");
  const [isDefault, setIsDefault] = React.useState(false);
  const [error, setError] = React.useState<unknown>(null);
  const [pending, setPending] = React.useState(false);

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    setPending(true);
    setError(null);
    try {
      await onSave(name, isDefault);
      onClose();
    } catch (failure) {
      setError(failure);
    } finally {
      setPending(false);
    }
  };

  return (
    <Dialog open onOpenChange={(open) => (open ? undefined : onClose())}>
      <DialogContent closeLabel={t("Close")}>
        <form onSubmit={(event) => void submit(event)} className="flex flex-col gap-4">
          <DialogHeader>
            <DialogTitle>{t("common.table.views.saveNew")}</DialogTitle>
            <DialogDescription>{t("common.table.views.saveText")}</DialogDescription>
          </DialogHeader>
          {error ? <ApiErrorAlert error={error} /> : null}
          <div className="flex flex-col gap-2">
            <Label htmlFor="grid-view-name">{t("common.table.views.name")}</Label>
            <Input
              id="grid-view-name"
              value={name}
              maxLength={60}
              required
              autoFocus
              onChange={(event) => setName(event.target.value)}
            />
          </div>
          <div className="flex items-center gap-2">
            <Checkbox
              id="grid-view-default"
              checked={isDefault}
              onCheckedChange={(checked) => setIsDefault(checked === true)}
            />
            <Label htmlFor="grid-view-default">{t("common.table.views.useAsDefault")}</Label>
          </div>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose}>
              {t("Cancel")}
            </Button>
            <Button type="submit" disabled={pending || name.trim().length === 0}>
              {t("Save")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
