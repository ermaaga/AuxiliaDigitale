"use client";

import * as React from "react";
import { useTranslations } from "next-intl";
import { Trash2Icon } from "lucide-react";
import { Button } from "@auxilia/ui/components/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@auxilia/ui/components/dialog";
import { Input } from "@auxilia/ui/components/input";
import { Label } from "@auxilia/ui/components/label";
import { Switch } from "@auxilia/ui/components/switch";

import { useConfirm } from "@/components/confirm/confirm-provider";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { useNotify } from "@/lib/notify";

import {
  createCategory,
  deleteCategory,
  updateCategory,
  useServiceCategories,
  useServiceMutation,
  type ServiceCategory,
} from "../api";
import { CATEGORY_NAME_MAX } from "../schemas/service";

/**
 * The service categories (F08, Q26: the legacy app had no page): add, rename, (de)activate, delete when no service
 * uses them (otherwise the API answers 409 and the category is deactivated instead).
 */
export function CategoriesDialog({ tenant }: { tenant: string }) {
  const t = useTranslations();
  const notify = useNotify();
  const categories = useServiceCategories(tenant);
  const [name, setName] = React.useState("");
  const create = useServiceMutation(tenant, (value: string) => createCategory(value, null));

  const add = async (event: React.FormEvent) => {
    event.preventDefault();
    try {
      await create.mutateAsync(name.trim());
      setName("");
      notify.success("app.services.categorySaved");
    } catch {
      // Shown by the alert below.
    }
  };

  return (
    <Dialog>
      <DialogTrigger asChild>
        <Button type="button" variant="outline">
          {t("app.services.categories")}
        </Button>
      </DialogTrigger>
      <DialogContent closeLabel={t("Close")}>
        <DialogHeader>
          <DialogTitle>{t("app.services.categories")}</DialogTitle>
          <DialogDescription>{t("app.services.categoriesDescription")}</DialogDescription>
        </DialogHeader>
        <form onSubmit={(event) => void add(event)} className="flex items-end gap-2">
          <div className="flex flex-1 flex-col gap-1">
            <Label htmlFor="service-category-new">{t("app.services.newCategory")}</Label>
            <Input
              id="service-category-new"
              value={name}
              maxLength={CATEGORY_NAME_MAX}
              onChange={(event) => setName(event.target.value)}
            />
          </div>
          <Button type="submit" disabled={name.trim().length === 0 || create.isPending}>
            {t("Add")}
          </Button>
        </form>
        {create.error ? <ApiErrorAlert error={create.error} /> : null}
        <ul
          className="flex max-h-80 flex-col gap-2 overflow-y-auto"
          aria-label={t("app.services.categories")}
        >
          {(categories.data ?? []).map((category) => (
            <CategoryRow key={category.id} tenant={tenant} category={category} />
          ))}
        </ul>
      </DialogContent>
    </Dialog>
  );
}

function CategoryRow({ tenant, category }: { tenant: string; category: ServiceCategory }) {
  const t = useTranslations();
  const notify = useNotify();
  const confirm = useConfirm();
  const [name, setName] = React.useState(category.name);
  const save = useServiceMutation(tenant, (input: { name: string; isActive: boolean }) =>
    updateCategory(category.id, input.name, category.description ?? null, input.isActive),
  );
  const remove = useServiceMutation(tenant, () => deleteCategory(category.id));
  const run = async (input: { name: string; isActive: boolean }) => {
    try {
      await save.mutateAsync(input);
      notify.success("app.services.categorySaved");
    } catch (error) {
      setName(category.name);
      notify.error(error);
    }
  };

  const onDelete = async () => {
    const confirmed = await confirm({
      description: t("app.services.deleteCategoryConfirm", { name: category.name }),
      confirmLabel: t("Delete"),
      variant: "destructive",
    });
    if (confirmed) {
      try {
        await remove.mutateAsync(undefined);
        notify.success("app.services.categoryDeleted");
      } catch (error) {
        notify.error(error);
      }
    }
  };

  return (
    <li className="flex items-center gap-2">
      <Input
        aria-label={t("app.services.renameCategory", { name: category.name })}
        value={name}
        maxLength={CATEGORY_NAME_MAX}
        onChange={(event) => setName(event.target.value)}
        onBlur={() =>
          name.trim() && name.trim() !== category.name
            ? void run({ name: name.trim(), isActive: category.isActive })
            : undefined
        }
      />
      <Switch
        checked={category.isActive}
        aria-label={t("app.services.categoryActive", { name: category.name })}
        onCheckedChange={(checked) => void run({ name: category.name, isActive: checked })}
        disabled={save.isPending}
      />
      <span
        className="w-8 text-right text-sm text-muted-foreground"
        title={t("app.services.servicesCount")}
      >
        {Number(category.serviceCount)}
      </span>
      <Button
        type="button"
        variant="ghost"
        size="icon"
        aria-label={t("app.services.deleteCategory", { name: category.name })}
        onClick={() => void onDelete()}
        disabled={remove.isPending}
      >
        <Trash2Icon aria-hidden />
      </Button>
    </li>
  );
}
