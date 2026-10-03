"use client";

import * as React from "react";
import {
  ArrowDownIcon,
  ArrowUpIcon,
  CheckIcon,
  FolderIcon,
  FolderPlusIcon,
  PencilIcon,
  Trash2Icon,
  XIcon,
} from "lucide-react";
import { useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@auxilia/ui/components/card";
import { Input } from "@auxilia/ui/components/input";
import { Skeleton } from "@auxilia/ui/components/skeleton";

import { useConfirm } from "@/components/confirm/confirm-provider";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { useNotify } from "@/lib/notify";

import {
  createFolder,
  deleteFolder,
  movedOrder,
  renameFolder,
  reorderFolders,
  useServiceFolders,
  useServiceMutation,
  type ServiceFolder,
} from "../api";

/** Fixed indentation classes per depth (no inline styles under the CSP); deeper folders keep the last one. */
const INDENT = ["pl-0", "pl-6", "pl-12", "pl-18", "pl-24", "pl-30"] as const;
const FOLDER_NAME_MAX = 200;

/** What is being typed: a new folder under `parentId` (null = root) or the new name of `folderId`. */
type Draft = { kind: "create"; parentId: string | null } | { kind: "rename"; folderId: string };

/**
 * The folder template of a service (F33, legacy `ServiceFolderTemplateEditor`): a tree under "All documents" with
 * root folders and subfolders, inline rename (Enter saves, Esc cancels), move up/down among siblings and delete with
 * its subfolders. Every write answers the whole tree. Read-only without `canManage`.
 */
export function FolderEditor({
  tenant,
  serviceId,
  canManage,
}: {
  tenant: string;
  serviceId: string;
  canManage: boolean;
}) {
  const t = useTranslations();
  const notify = useNotify();
  const confirm = useConfirm();
  const folders = useServiceFolders(tenant, serviceId);
  const [draft, setDraft] = React.useState<Draft | null>(null);
  const write = useServiceMutation(tenant, (action: () => Promise<unknown>) => action());

  const run = async (action: () => Promise<unknown>, success?: string) => {
    try {
      await write.mutateAsync(action);
      setDraft(null);
      if (success) {
        notify.success(success);
      }
    } catch (error) {
      notify.error(error);
    }
  };

  const onDelete = async (folder: ServiceFolder) => {
    const confirmed = await confirm({
      title: t("DeleteFolder"),
      description: `${folder.path} — ${t("DeleteFolderConfirm")}`,
      confirmLabel: t("Delete"),
      variant: "destructive",
    });
    if (confirmed) {
      await run(() => deleteFolder(serviceId, folder.id), "app.services.folderDeleted");
    }
  };

  const items = folders.data ?? [];
  const nameEditor = (initial: string, onSave: (name: string) => void, label: string) => (
    <NameEditor
      initial={initial}
      label={label}
      disabled={write.isPending}
      onSave={onSave}
      onCancel={() => setDraft(null)}
    />
  );
  const createUnder = (parentId: string | null) =>
    draft?.kind === "create" && draft.parentId === parentId
      ? nameEditor(
          "",
          (name) =>
            void run(() => createFolder(serviceId, name, parentId), "app.services.folderCreated"),
          t("FolderName"),
        )
      : null;

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t("FolderTemplateTitle")}</CardTitle>
        <CardDescription>{t("FolderTemplateDescription")}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        {folders.error ? (
          <ApiErrorAlert error={folders.error} onRetry={() => void folders.refetch()} />
        ) : null}
        <div className="flex items-center justify-between gap-2">
          <span className="flex items-center gap-2 font-medium">
            <FolderIcon aria-hidden className="size-4" /> {t("AllDocuments")}
          </span>
          {canManage ? (
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => setDraft({ kind: "create", parentId: null })}
            >
              <FolderPlusIcon aria-hidden /> {t("AddRootFolder")}
            </Button>
          ) : null}
        </div>
        {folders.isPending ? (
          <Skeleton className="h-24 w-full" />
        ) : items.length === 0 && !draft ? (
          <p className="text-sm text-muted-foreground">{t("NoFoldersDefined")}</p>
        ) : (
          <ul className="flex flex-col gap-1 border-l pl-3" aria-label={t("FolderTemplate")}>
            {createUnder(null) ? <li>{createUnder(null)}</li> : null}
            {items.map((folder) => {
              const depth = Math.min(Number(folder.depth), INDENT.length - 1);
              const up = movedOrder(items, folder, -1);
              const down = movedOrder(items, folder, 1);
              const renaming = draft?.kind === "rename" && draft.folderId === folder.id;
              return (
                <li key={folder.id} className={`flex flex-col gap-1 ${INDENT[depth]}`}>
                  {renaming ? (
                    nameEditor(
                      folder.name,
                      (name) =>
                        void run(
                          () => renameFolder(serviceId, folder.id, name),
                          "app.services.folderRenamed",
                        ),
                      t("EditFolderName"),
                    )
                  ) : (
                    <div className="flex flex-wrap items-center gap-1">
                      <span className="flex min-w-0 flex-1 items-center gap-2">
                        <FolderIcon aria-hidden className="size-4 shrink-0 text-muted-foreground" />
                        <span className="truncate">{folder.name}</span>
                      </span>
                      {canManage ? (
                        <span className="flex items-center">
                          <Button
                            type="button"
                            variant="ghost"
                            size="icon"
                            aria-label={t("app.services.moveFolderUp", { name: folder.name })}
                            disabled={!up || write.isPending}
                            onClick={() =>
                              up &&
                              void run(() => reorderFolders(serviceId, folder.parentId ?? null, up))
                            }
                          >
                            <ArrowUpIcon aria-hidden />
                          </Button>
                          <Button
                            type="button"
                            variant="ghost"
                            size="icon"
                            aria-label={t("app.services.moveFolderDown", { name: folder.name })}
                            disabled={!down || write.isPending}
                            onClick={() =>
                              down &&
                              void run(() =>
                                reorderFolders(serviceId, folder.parentId ?? null, down),
                              )
                            }
                          >
                            <ArrowDownIcon aria-hidden />
                          </Button>
                          <Button
                            type="button"
                            variant="ghost"
                            size="icon"
                            aria-label={`${t("AddSubfolder")}: ${folder.name}`}
                            disabled={write.isPending}
                            onClick={() => setDraft({ kind: "create", parentId: folder.id })}
                          >
                            <FolderPlusIcon aria-hidden />
                          </Button>
                          <Button
                            type="button"
                            variant="ghost"
                            size="icon"
                            aria-label={`${t("EditFolderName")}: ${folder.name}`}
                            disabled={write.isPending}
                            onClick={() => setDraft({ kind: "rename", folderId: folder.id })}
                          >
                            <PencilIcon aria-hidden />
                          </Button>
                          <Button
                            type="button"
                            variant="ghost"
                            size="icon"
                            aria-label={`${t("DeleteFolder")}: ${folder.name}`}
                            disabled={write.isPending}
                            onClick={() => void onDelete(folder)}
                          >
                            <Trash2Icon aria-hidden />
                          </Button>
                        </span>
                      ) : null}
                    </div>
                  )}
                  {createUnder(folder.id) ? (
                    <div className="pl-6">{createUnder(folder.id)}</div>
                  ) : null}
                </li>
              );
            })}
          </ul>
        )}
      </CardContent>
    </Card>
  );
}

function NameEditor({
  initial,
  label,
  disabled,
  onSave,
  onCancel,
}: {
  initial: string;
  label: string;
  disabled: boolean;
  onSave: (name: string) => void;
  onCancel: () => void;
}) {
  const t = useTranslations();
  const [name, setName] = React.useState(initial);
  const value = name.trim();
  const save = () => (value ? onSave(value) : undefined);

  return (
    <span className="flex items-center gap-1">
      <Input
        aria-label={label}
        value={name}
        maxLength={FOLDER_NAME_MAX}
        autoFocus
        disabled={disabled}
        onChange={(event) => setName(event.target.value)}
        onKeyDown={(event) => {
          if (event.key === "Enter") {
            event.preventDefault();
            save();
          } else if (event.key === "Escape") {
            event.preventDefault();
            onCancel();
          }
        }}
      />
      <Button
        type="button"
        variant="ghost"
        size="icon"
        aria-label={t("SaveFolderName")}
        disabled={disabled || !value}
        onClick={save}
      >
        <CheckIcon aria-hidden />
      </Button>
      <Button
        type="button"
        variant="ghost"
        size="icon"
        aria-label={t("Cancel")}
        disabled={disabled}
        onClick={onCancel}
      >
        <XIcon aria-hidden />
      </Button>
    </span>
  );
}
