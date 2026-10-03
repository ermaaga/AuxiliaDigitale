"use client";

import { useTranslations } from "next-intl";

import { useCan } from "@/lib/permissions";

import { DOCUMENTS_PERMISSIONS } from "../permissions";
import { DocumentAreasDialog } from "./document-areas-dialog";
import { DocumentUploader } from "./document-uploader";
import { DocumentsTable, type FolderChoice } from "./documents-table";

/**
 * Documents of the tenant, of one client or of one case and folder (F14, F33): the uploader (with
 * `documents.files.manage`), the areas (with `documents.areas.manage`, tenant page only) and the list. The page
 * `/documents`, the client 360° tab and the case documents use it.
 */
export function DocumentsPanel({
  tenant,
  label,
  clientId,
  caseId,
  folderId,
  folders,
}: {
  tenant: string;
  label: string;
  clientId?: string;
  caseId?: string;
  folderId?: string;
  folders?: readonly FolderChoice[];
}) {
  const t = useTranslations();
  const canUpload = useCan(DOCUMENTS_PERMISSIONS.manage);
  const canManageAreas = useCan(DOCUMENTS_PERMISSIONS.manageAreas);

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        {canUpload ? (
          <DocumentUploader
            tenant={tenant}
            clientId={clientId}
            caseId={caseId}
            folderId={folderId}
          />
        ) : (
          <span />
        )}
        {canManageAreas && clientId === undefined ? <DocumentAreasDialog tenant={tenant} /> : null}
      </div>
      <DocumentsTable
        tenant={tenant}
        label={label || t("Document")}
        clientId={clientId}
        caseId={caseId}
        folderId={folderId}
        folders={folders}
      />
    </div>
  );
}
