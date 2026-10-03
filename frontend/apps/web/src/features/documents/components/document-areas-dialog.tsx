"use client";

import * as React from "react";
import { isApiError } from "@auxilia/api-client";
import { useTranslations } from "next-intl";
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

import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { useNotify } from "@/lib/notify";

import {
  createDocumentArea,
  updateDocumentArea,
  useDocumentAreas,
  useDocumentMutation,
  type DocumentArea,
} from "../api";

/**
 * The document areas (F14: legacy free text, now a managed list; `documents.areas.manage`): add, rename and
 * (de)activate. Inactive areas stay on their documents and are not offered for new ones.
 */
export function DocumentAreasDialog({ tenant }: { tenant: string }) {
  const t = useTranslations();
  const notify = useNotify();
  const areas = useDocumentAreas(tenant);
  const [name, setName] = React.useState("");
  const create = useDocumentMutation(tenant, (value: string) => createDocumentArea(value));

  const add = async (event: React.FormEvent) => {
    event.preventDefault();
    try {
      await create.mutateAsync(name.trim());
      setName("");
      notify.success("app.documents.areas.saved");
    } catch {
      // Shown by the alert below.
    }
  };

  return (
    <Dialog>
      <DialogTrigger asChild>
        <Button type="button" variant="outline">
          {t("app.documents.areas.title")}
        </Button>
      </DialogTrigger>
      <DialogContent closeLabel={t("Close")}>
        <DialogHeader>
          <DialogTitle>{t("app.documents.areas.title")}</DialogTitle>
          <DialogDescription>{t("app.documents.areas.description")}</DialogDescription>
        </DialogHeader>
        <form onSubmit={(event) => void add(event)} className="flex items-end gap-2">
          <div className="flex flex-1 flex-col gap-1">
            <Label htmlFor="document-area-new">{t("app.documents.areas.new")}</Label>
            <Input
              id="document-area-new"
              value={name}
              maxLength={100}
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
          aria-label={t("app.documents.areas.title")}
        >
          {(areas.data ?? []).map((area) => (
            <AreaRow key={area.id} tenant={tenant} area={area} />
          ))}
        </ul>
      </DialogContent>
    </Dialog>
  );
}

function AreaRow({ tenant, area }: { tenant: string; area: DocumentArea }) {
  const t = useTranslations();
  const notify = useNotify();
  const [name, setName] = React.useState(area.name);
  const save = useDocumentMutation(tenant, (input: { name: string; isActive: boolean }) =>
    updateDocumentArea(area.id, input.name, input.isActive),
  );
  const run = async (input: { name: string; isActive: boolean }) => {
    try {
      await save.mutateAsync(input);
      notify.success("app.documents.areas.saved");
    } catch (error) {
      notify.error(error);
      if (isApiError(error)) {
        setName(area.name);
      }
    }
  };

  return (
    <li className="flex items-center gap-2">
      <Input
        aria-label={t("app.documents.areas.rename", { name: area.name })}
        value={name}
        maxLength={100}
        onChange={(event) => setName(event.target.value)}
        onBlur={() =>
          name.trim() && name.trim() !== area.name
            ? void run({ name: name.trim(), isActive: area.isActive })
            : undefined
        }
      />
      <span className="flex items-center gap-2 text-sm text-muted-foreground">
        <Switch
          checked={area.isActive}
          aria-label={t("app.documents.areas.active", { name: area.name })}
          onCheckedChange={(checked) => void run({ name: area.name, isActive: checked })}
          disabled={save.isPending}
        />
        {Number(area.documentCount)}
      </span>
    </li>
  );
}
