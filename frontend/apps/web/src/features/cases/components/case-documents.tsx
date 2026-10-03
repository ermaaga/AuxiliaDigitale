"use client";

import * as React from "react";
import { DownloadIcon, FolderIcon, FolderOpenIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import { Card, CardContent, CardHeader, CardTitle } from "@auxilia/ui/components/card";
import { Skeleton } from "@auxilia/ui/components/skeleton";

import { caseZipUrl, DocumentsPanel } from "@/features/documents";
import { useConfirm } from "@/components/confirm/confirm-provider";

import { useServiceFolders, type CaseDetail } from "../api";

/** Indentation of the tree levels (classes, not inline styles: the CSP allows no style attributes). */
const INDENT = [
  "pl-0",
  "pl-3",
  "pl-6",
  "pl-9",
  "pl-12",
  "pl-14",
  "pl-16",
  "pl-20",
  "pl-24",
  "pl-28",
  "pl-32",
];

/**
 * The documents of a case (F33, legacy "InProgress" content): the folder tree of the service ("All documents" at the
 * root) filters the list and is where uploads go; each row can move to another folder; the root and every folder
 * download as a ZIP (after a confirmation, legacy).
 */
export function CaseDocuments({ tenant, value }: { tenant: string; value: CaseDetail }) {
  const t = useTranslations();
  const confirm = useConfirm();
  const folders = useServiceFolders(tenant, value.service.id);
  const [folder, setFolder] = React.useState<string | undefined>();
  const tree = folders.data ?? [];
  const chosen = tree.find((item) => item.id === folder);

  const zip = async (folderId?: string) => {
    const confirmed = await confirm({
      description: t("DownloadZipConfirm"),
      confirmLabel: t("Download"),
    });
    if (confirmed) {
      window.location.assign(caseZipUrl(value.id, folderId));
    }
  };

  return (
    <div className="grid gap-4 lg:grid-cols-[16rem_1fr]">
      <Card>
        <CardHeader>
          <CardTitle>
            <h2 className="text-base font-semibold">{t("Folder")}</h2>
          </CardTitle>
        </CardHeader>
        <CardContent>
          {folders.isPending ? (
            <Skeleton className="h-24 w-full" />
          ) : (
            <ul className="flex flex-col gap-1 text-sm" aria-label={t("app.cases.folders")}>
              {[
                { id: undefined, name: t("AllDocuments"), depth: 0 },
                ...tree.map((item) => ({
                  id: item.id,
                  name: item.name,
                  depth: Number(item.depth),
                })),
              ].map((item) => (
                <li
                  key={item.id ?? "root"}
                  className={`flex items-center gap-1 ${INDENT[Math.min(item.depth, INDENT.length - 1)]}`}
                >
                  <Button
                    type="button"
                    variant={folder === item.id ? "secondary" : "ghost"}
                    size="sm"
                    className="flex-1 justify-start"
                    aria-pressed={folder === item.id}
                    onClick={() => setFolder(item.id)}
                  >
                    {folder === item.id ? (
                      <FolderOpenIcon aria-hidden />
                    ) : (
                      <FolderIcon aria-hidden />
                    )}
                    <span className="truncate">{item.name}</span>
                  </Button>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    aria-label={t("app.cases.downloadZip", { name: item.name })}
                    onClick={() => void zip(item.id)}
                  >
                    <DownloadIcon aria-hidden />
                  </Button>
                </li>
              ))}
            </ul>
          )}
        </CardContent>
      </Card>
      <DocumentsPanel
        tenant={tenant}
        label={chosen ? chosen.path : t("AllDocuments")}
        clientId={value.client.id}
        caseId={value.id}
        folderId={folder}
        folders={tree.map((item) => ({ id: item.id, path: item.path }))}
      />
    </div>
  );
}
