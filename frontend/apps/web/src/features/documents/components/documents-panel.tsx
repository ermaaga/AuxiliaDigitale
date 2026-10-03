"use client";

import { useTranslations } from "next-intl";

import { useCan } from "@/lib/permissions";

import { DOCUMENTS_PERMISSIONS } from "../permissions";
import { DocumentAreasDialog } from "./document-areas-dialog";
import { DocumentUploader } from "./document-uploader";
import { DocumentsTable } from "./documents-table";

/**
 * Documents of the tenant or of one client (F14): the uploader (with `documents.files.manage`), the areas (with
 * `documents.areas.manage`) and the list. The page `/documents` and the client 360° tab use it.
 */
export function DocumentsPanel({
  tenant,
  label,
  clientId,
}: {
  tenant: string;
  label: string;
  clientId?: string;
}) {
  const t = useTranslations();
  const canUpload = useCan(DOCUMENTS_PERMISSIONS.manage);
  const canManageAreas = useCan(DOCUMENTS_PERMISSIONS.manageAreas);

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        {canUpload ? <DocumentUploader tenant={tenant} clientId={clientId} /> : <span />}
        {canManageAreas && clientId === undefined ? <DocumentAreasDialog tenant={tenant} /> : null}
      </div>
      <DocumentsTable tenant={tenant} label={label || t("Document")} clientId={clientId} />
    </div>
  );
}
