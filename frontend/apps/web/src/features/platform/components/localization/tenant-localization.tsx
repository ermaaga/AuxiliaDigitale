"use client";

import * as React from "react";
import { PencilIcon, PlusIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import { Badge } from "@auxilia/ui/components/badge";
import { Button } from "@auxilia/ui/components/button";
import { Card, CardContent } from "@auxilia/ui/components/card";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@auxilia/ui/components/dialog";
import { Input } from "@auxilia/ui/components/input";
import { Label } from "@auxilia/ui/components/label";
import { Textarea } from "@auxilia/ui/components/textarea";

import { useConfirm } from "@/components/confirm/confirm-provider";
import { DataTable } from "@/components/data-table/data-table";
import { FilterSelect, SearchFilter } from "@/components/data-table/filters";
import type { DataTableColumn } from "@/components/data-table/table-model";
import { useTableState } from "@/components/data-table/use-table-state";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { FormField, applyApiErrors, useZodForm } from "@/components/forms/form";
import { useNotify } from "@/lib/notify";

import {
  createKey,
  deleteKey,
  removeTranslation,
  setTranslation,
  translationOf,
  updateKey,
  useCategories,
  useLanguageStats,
  useLocalizationMutation,
  useResourceKeys,
  type LanguageStats,
  type ResourceKey,
} from "../../localization-api";
import {
  TRANSLATION_MAX_LENGTH,
  initialTranslations,
  keyDetailsSchema,
  resourceKeySchema,
  type ResourceKeyValues,
} from "../../schemas/localization";

/**
 * Labels and translations of a tenant (F24, D-18): search by key or text, filter by category or missing language,
 * edit a translation in place, add, describe and delete keys. Clients see a change at their next bundle refresh.
 */
export function TenantLocalization({ slug }: { slug: string }) {
  const t = useTranslations();
  const notify = useNotify();
  const confirm = useConfirm();
  const table = useTableState(["search", "category", "missingLanguage"] as const);
  const languages = useLanguageStats(slug);
  const categories = useCategories(slug);
  const active = (languages.data ?? []).filter((language) => language.isActive);
  const keys = useResourceKeys(slug, {
    page: table.page,
    pageSize: table.pageSize,
    sort: table.sort ?? undefined,
    search: table.filters.search,
    category: table.filters.category,
    missingLanguage: table.filters.missingLanguage,
  });
  const [dialog, setDialog] = React.useState<
    { kind: "new" } | { kind: "edit"; key: ResourceKey }
  >();
  const remove = useLocalizationMutation(slug, (id: string) => deleteKey(slug, id));

  const columns: DataTableColumn<ResourceKey>[] = [
    {
      id: "key",
      header: t("Key"),
      sortField: "key",
      hideable: false,
      mobile: "title",
      cell: (key) => (
        <span className="flex max-w-72 flex-col gap-1">
          <code className="text-xs break-all">{key.key}</code>
          <span className="flex flex-wrap gap-1">
            <Badge variant="outline">{key.category}</Badge>
            {key.isSystem ? (
              <Badge variant="secondary">{t("app.platform.localization.system")}</Badge>
            ) : null}
          </span>
          {key.description ? (
            <span className="text-xs text-muted-foreground">{key.description}</span>
          ) : null}
        </span>
      ),
    },
    ...active.map((language): DataTableColumn<ResourceKey> => ({
      id: `lang:${language.code}`,
      header: language.name,
      cell: (key) => <TranslationCell slug={slug} resourceKey={key} language={language} />,
    })),
    {
      id: "actions",
      header: t("Actions"),
      hideable: false,
      cell: (key) => (
        <div className="flex flex-wrap gap-1">
          <Button
            variant="outline"
            size="sm"
            onClick={() => setDialog({ kind: "edit", key })}
            aria-label={t("app.platform.localization.editKey", { key: key.key })}
          >
            {t("Edit")}
          </Button>
          <Button
            variant="ghost"
            size="sm"
            disabled={remove.isPending}
            aria-label={t("app.platform.localization.deleteKey", { key: key.key })}
            onClick={async () => {
              if (
                await confirm({
                  title: t("app.platform.localization.deleteTitle"),
                  description: t("app.platform.localization.deleteText", { key: key.key }),
                  confirmLabel: t("Delete"),
                  variant: "destructive",
                })
              ) {
                await remove.mutateAsync(key.id).then(
                  () => notify.success("app.platform.localization.deleted"),
                  (error: unknown) => notify.error(error),
                );
              }
            }}
          >
            {t("Delete")}
          </Button>
        </div>
      ),
    },
  ];

  const toolbar = (
    <>
      <SearchFilter
        id="localization-search"
        label={t("Search")}
        placeholder={t("app.platform.localization.searchPlaceholder")}
        value={table.filters.search}
        onChange={(value) => table.setFilter("search", value)}
      />
      <FilterSelect
        id="localization-category"
        label={t("app.platform.localization.category")}
        value={table.filters.category}
        onChange={(value) => table.setFilter("category", value)}
        options={(categories.data ?? []).map((category) => ({
          value: category.category,
          label: `${category.category} (${Number(category.keyCount)})`,
        }))}
      />
      <FilterSelect
        id="localization-missing"
        label={t("app.platform.localization.missingIn")}
        value={table.filters.missingLanguage}
        onChange={(value) => table.setFilter("missingLanguage", value)}
        options={active.map((language) => ({ value: language.code, label: language.name }))}
      />
      {table.hasFilters ? (
        <Button type="button" variant="ghost" size="sm" onClick={table.clearFilters}>
          {t("ClearFilters")}
        </Button>
      ) : null}
    </>
  );

  return (
    <div className="flex flex-col gap-4">
      <Card>
        <CardContent className="flex flex-wrap items-center justify-between gap-3">
          {languages.error ? (
            <ApiErrorAlert error={languages.error} onRetry={() => void languages.refetch()} />
          ) : (
            <ul
              className="flex flex-wrap gap-2"
              aria-label={t("app.platform.localization.languages")}
            >
              {active.map((language) => (
                <li key={language.code}>
                  <button
                    type="button"
                    className="rounded-md border px-3 py-1.5 text-sm hover:bg-accent hover:text-accent-foreground"
                    onClick={() => table.setFilter("missingLanguage", language.code)}
                    aria-label={t("app.platform.localization.showMissing", {
                      language: language.name,
                      count: Number(language.missingCount),
                    })}
                  >
                    <span className="font-medium">{language.name}</span>{" "}
                    <span className="text-muted-foreground">
                      {t("app.platform.localization.missingCount", {
                        count: Number(language.missingCount),
                      })}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
          <Button
            size="sm"
            onClick={() => setDialog({ kind: "new" })}
            disabled={active.length === 0}
          >
            <PlusIcon aria-hidden /> {t("app.platform.localization.newKey")}
          </Button>
        </CardContent>
      </Card>
      <DataTable
        label={t("app.platform.localization.keys")}
        columns={columns}
        rows={keys.data?.items}
        getRowId={(key) => key.id}
        totalCount={Number(keys.data?.totalCount ?? 0)}
        page={table.page}
        pageSize={table.pageSize}
        sort={table.sort}
        onPageChange={table.setPage}
        onPageSizeChange={table.setPageSize}
        onSortChange={table.setSort}
        isLoading={keys.isPending || languages.isPending}
        error={keys.error}
        onRetry={() => void keys.refetch()}
        toolbar={toolbar}
        filtered={table.hasFilters}
      />
      {dialog?.kind === "new" ? (
        <NewKeyDialog slug={slug} languages={active} onClose={() => setDialog(undefined)} />
      ) : null}
      {dialog?.kind === "edit" ? (
        <KeyDetailsDialog
          slug={slug}
          resourceKey={dialog.key}
          onClose={() => setDialog(undefined)}
        />
      ) : null}
    </div>
  );
}

/** One translation: the value with an edit button, or "missing" with an add button; edited in place. */
function TranslationCell({
  slug,
  resourceKey,
  language,
}: {
  slug: string;
  resourceKey: ResourceKey;
  language: LanguageStats;
}) {
  const t = useTranslations();
  const notify = useNotify();
  const confirm = useConfirm();
  const translation = translationOf(resourceKey, language.code);
  const [editing, setEditing] = React.useState(false);
  const [text, setText] = React.useState("");
  const save = useLocalizationMutation(slug, (value: string) =>
    setTranslation(slug, resourceKey.id, language.code, value),
  );
  const remove = useLocalizationMutation(slug, () =>
    removeTranslation(slug, resourceKey.id, language.code),
  );
  const id = `tr-${resourceKey.id}-${language.code}`;
  const label = t("app.platform.localization.translationOf", {
    key: resourceKey.key,
    language: language.name,
  });

  if (editing) {
    const submit = () =>
      save.mutateAsync(text).then(
        () => {
          notify.success("app.platform.localization.saved");
          setEditing(false);
        },
        () => undefined,
      );
    return (
      <form
        className="flex min-w-56 flex-col gap-2"
        onSubmit={(event) => {
          event.preventDefault();
          void submit();
        }}
      >
        <Label htmlFor={id} className="sr-only">
          {label}
        </Label>
        <Textarea
          id={id}
          value={text}
          rows={2}
          maxLength={TRANSLATION_MAX_LENGTH}
          autoFocus
          aria-invalid={save.error ? true : undefined}
          onChange={(event) => setText(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === "Escape") {
              setEditing(false);
            } else if (event.key === "Enter" && !event.shiftKey) {
              event.preventDefault();
              void submit();
            }
          }}
        />
        {save.error ? <ApiErrorAlert error={save.error} /> : null}
        <div className="flex gap-1">
          <Button type="submit" size="sm" disabled={save.isPending || text.trim() === ""}>
            {t("Save")}
          </Button>
          <Button type="button" size="sm" variant="ghost" onClick={() => setEditing(false)}>
            {t("Cancel")}
          </Button>
        </div>
      </form>
    );
  }

  return (
    <div className="flex min-w-40 items-start gap-1">
      {translation ? (
        <span className="flex-1 break-words whitespace-pre-wrap">
          {translation.value}
          {translation.isCustomized ? (
            <Badge variant="outline" className="ml-1 align-middle">
              {t("app.platform.localization.customized")}
            </Badge>
          ) : null}
        </span>
      ) : (
        <span className="flex-1">
          <Badge variant="destructive">{t("app.platform.localization.missing")}</Badge>
        </span>
      )}
      <Button
        type="button"
        variant="ghost"
        size="icon"
        aria-label={
          translation
            ? t("app.platform.localization.editTranslation", {
                key: resourceKey.key,
                language: language.name,
              })
            : t("app.platform.localization.addTranslation", {
                key: resourceKey.key,
                language: language.name,
              })
        }
        onClick={() => {
          setText(translation?.value ?? "");
          save.reset();
          setEditing(true);
        }}
      >
        {translation ? <PencilIcon aria-hidden /> : <PlusIcon aria-hidden />}
      </Button>
      {translation && !language.isDefault ? (
        <Button
          type="button"
          variant="ghost"
          size="sm"
          disabled={remove.isPending}
          aria-label={t("app.platform.localization.removeTranslation", {
            key: resourceKey.key,
            language: language.name,
          })}
          onClick={async () => {
            if (
              await confirm({
                description: t("app.platform.localization.removeText", {
                  key: resourceKey.key,
                  language: language.name,
                }),
                confirmLabel: t("Remove"),
                variant: "destructive",
              })
            ) {
              await remove.mutateAsync(undefined).then(
                () => notify.success("app.platform.localization.removed"),
                (error: unknown) => notify.error(error),
              );
            }
          }}
        >
          {t("Remove")}
        </Button>
      ) : null}
    </div>
  );
}

function NewKeyDialog({
  slug,
  languages,
  onClose,
}: {
  slug: string;
  languages: readonly LanguageStats[];
  onClose: () => void;
}) {
  const t = useTranslations();
  const notify = useNotify();
  const form = useZodForm(resourceKeySchema, {
    defaultValues: { key: "", category: "app", description: "" },
  });
  const [values, setValues] = React.useState<Record<string, string>>({});
  const save = useLocalizationMutation(slug, (input: ResourceKeyValues) =>
    createKey(slug, {
      key: input.key,
      category: input.category,
      description: input.description || null,
      translations: initialTranslations(values),
    }),
  );

  const submit = form.handleSubmit((input) =>
    save.mutateAsync(input).then(
      () => {
        notify.success("app.platform.localization.created");
        onClose();
      },
      (error: unknown) => {
        applyApiErrors(error, form.setError, ["key", "category", "description"]);
      },
    ),
  );

  return (
    <Dialog open onOpenChange={(open) => (open ? undefined : onClose())}>
      <DialogContent closeLabel={t("Close")} className="max-h-[90dvh] overflow-y-auto sm:max-w-lg">
        <form className="flex flex-col gap-4" onSubmit={submit} noValidate>
          <DialogHeader>
            <DialogTitle>{t("app.platform.localization.newKey")}</DialogTitle>
            <DialogDescription>{t("app.platform.localization.newKeyHint")}</DialogDescription>
          </DialogHeader>
          {save.error && Object.keys(form.formState.errors).length === 0 ? (
            <ApiErrorAlert error={save.error} />
          ) : null}
          <FormField control={form.control} name="key" label={t("Key")}>
            {(field, props) => (
              <Input {...field} {...props} autoComplete="off" spellCheck={false} />
            )}
          </FormField>
          <div className="grid gap-4 sm:grid-cols-2">
            <FormField
              control={form.control}
              name="category"
              label={t("app.platform.localization.category")}
            >
              {(field, props) => (
                <Input {...field} {...props} autoComplete="off" spellCheck={false} />
              )}
            </FormField>
            <FormField control={form.control} name="description" label={t("Description")}>
              {(field, props) => <Input {...field} {...props} />}
            </FormField>
          </div>
          {languages.map((language) => (
            <div key={language.code} className="flex flex-col gap-2">
              <Label htmlFor={`new-key-${language.code}`}>{language.name}</Label>
              <Textarea
                id={`new-key-${language.code}`}
                rows={2}
                maxLength={TRANSLATION_MAX_LENGTH}
                value={values[language.code] ?? ""}
                onChange={(event) =>
                  setValues((current) => ({ ...current, [language.code]: event.target.value }))
                }
              />
            </div>
          ))}
          <DialogFooter closeLabel={t("Cancel")}>
            <Button type="submit" disabled={form.formState.isSubmitting}>
              {t("Save")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function KeyDetailsDialog({
  slug,
  resourceKey,
  onClose,
}: {
  slug: string;
  resourceKey: ResourceKey;
  onClose: () => void;
}) {
  const t = useTranslations();
  const notify = useNotify();
  const form = useZodForm(keyDetailsSchema, {
    defaultValues: { category: resourceKey.category, description: resourceKey.description ?? "" },
  });
  const save = useLocalizationMutation(slug, (input: { category: string; description: string }) =>
    updateKey(slug, resourceKey.id, {
      category: input.category,
      description: input.description || null,
    }),
  );

  const submit = form.handleSubmit((input) =>
    save.mutateAsync(input).then(
      () => {
        notify.success("app.platform.localization.saved");
        onClose();
      },
      (error: unknown) => {
        applyApiErrors(error, form.setError, ["category", "description"]);
      },
    ),
  );

  return (
    <Dialog open onOpenChange={(open) => (open ? undefined : onClose())}>
      <DialogContent closeLabel={t("Close")}>
        <form className="flex flex-col gap-4" onSubmit={submit} noValidate>
          <DialogHeader>
            <DialogTitle>
              {t("app.platform.localization.editKey", { key: resourceKey.key })}
            </DialogTitle>
            <DialogDescription>
              <code>{resourceKey.key}</code>
            </DialogDescription>
          </DialogHeader>
          {save.error && Object.keys(form.formState.errors).length === 0 ? (
            <ApiErrorAlert error={save.error} />
          ) : null}
          <FormField
            control={form.control}
            name="category"
            label={t("app.platform.localization.category")}
          >
            {(field, props) => (
              <Input {...field} {...props} autoComplete="off" spellCheck={false} />
            )}
          </FormField>
          <FormField control={form.control} name="description" label={t("Description")}>
            {(field, props) => <Input {...field} {...props} />}
          </FormField>
          <DialogFooter closeLabel={t("Cancel")}>
            <Button type="submit" disabled={form.formState.isSubmitting}>
              {t("Save")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
