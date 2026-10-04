"use client";

import * as React from "react";
import { useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@auxilia/ui/components/select";

import { useNotify } from "@/lib/notify";
import { useCan } from "@/lib/permissions";

import { changeClientsTags, useClientMutation, useTags } from "../api";
import { DIRECTORY_PERMISSIONS } from "../permissions";

/** Adds or removes a tag on the selected clients (N01: bulk from the clients table). */
export function BulkTags({ tenant, clientIds }: { tenant: string; clientIds: readonly string[] }) {
  const t = useTranslations();
  const notify = useNotify();
  const canManage = useCan(DIRECTORY_PERMISSIONS.manageClients);
  const tags = useTags(tenant, canManage);
  const [tagId, setTagId] = React.useState<string>();
  const change = useClientMutation(tenant, (add: boolean) =>
    changeClientsTags(clientIds, add ? [tagId!] : [], add ? [] : [tagId!]),
  );
  if (!canManage || clientIds.length === 0 || (tags.data?.length ?? 0) === 0) {
    return null;
  }

  const run = (add: boolean) =>
    void change.mutateAsync(add).then(
      (result) => notify.success(t("app.clients.tags.bulkDone", { count: result.changed })),
      (error: unknown) => notify.error(error),
    );

  return (
    <div
      role="group"
      aria-label={t("app.clients.tags.bulk")}
      className="flex flex-wrap items-center gap-2"
    >
      <Select value={tagId ?? ""} onValueChange={setTagId}>
        <SelectTrigger className="w-44" aria-label={t("app.clients.tags.chooseTag")}>
          <SelectValue placeholder={t("app.clients.tags.chooseTag")} />
        </SelectTrigger>
        <SelectContent>
          {(tags.data ?? []).map((tag) => (
            <SelectItem key={tag.id} value={tag.id}>
              {tag.name}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
      <Button
        type="button"
        size="sm"
        variant="outline"
        disabled={!tagId || change.isPending}
        onClick={() => run(true)}
      >
        {t("app.clients.tags.addSelected", { count: clientIds.length })}
      </Button>
      <Button
        type="button"
        size="sm"
        variant="ghost"
        disabled={!tagId || change.isPending}
        onClick={() => run(false)}
      >
        {t("app.clients.tags.removeSelected", { count: clientIds.length })}
      </Button>
    </div>
  );
}
