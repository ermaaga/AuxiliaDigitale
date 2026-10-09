"use client";

import * as React from "react";
import { isApiError } from "@auxilia/api-client";
import { DownloadIcon, ExternalLinkIcon, Trash2Icon } from "lucide-react";
import { useFormatter, useLocale, useTranslations } from "next-intl";
import { Alert, AlertDescription } from "@auxilia/ui/components/alert";
import { Button } from "@auxilia/ui/components/button";
import { Input } from "@auxilia/ui/components/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@auxilia/ui/components/select";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "@auxilia/ui/components/sheet";
import { Skeleton } from "@auxilia/ui/components/skeleton";
import { Textarea } from "@auxilia/ui/components/textarea";

import { useConfirm } from "@/components/confirm/confirm-provider";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { applyApiErrors, FormField, useZodForm } from "@/components/forms/form";
import { useNotify } from "@/lib/notify";

import {
  deleteDocument,
  documentContentUrl,
  PREVIEW_TYPES,
  updateDocument,
  useDocument,
  useDocumentAreas,
  useDocumentMutation,
  type DocumentDetail,
} from "../api";
import {
  editDocumentSchema,
  formatSize,
  referenceYears,
  splitFileName,
  type EditDocumentValues,
} from "../schemas/document";
import { DocumentPreview } from "./document-preview";
import { DocumentStatusBadge } from "./document-status-badge";

const NO_AREA = "none";

/**
 * The detail drawer of a document (F14): preview of images, PDFs and text files (`DocumentPreview`; PDF and images also
 * open inline in a new tab, the API never lets a page frame them), metadata edit (name without the extension, year, area, description; only when the API
 * allows it), download and delete with confirmation.
 */
export function DocumentDrawer({
  tenant,
  id,
  onClose,
}: {
  tenant: string;
  id: string | undefined;
  onClose: () => void;
}) {
  const t = useTranslations();
  const query = useDocument(tenant, id);

  return (
    <Sheet open={id !== undefined} onOpenChange={(open) => (open ? undefined : onClose())}>
      <SheetContent
        side="right"
        className="w-full overflow-y-auto sm:max-w-lg"
        closeLabel={t("Close")}
      >
        <SheetHeader>
          <SheetTitle>{query.data?.fileName ?? t("Details")}</SheetTitle>
          <SheetDescription>
            {query.data ? `${query.data.client.fullName}` : t("app.documents.loading")}
          </SheetDescription>
        </SheetHeader>
        <div className="flex flex-col gap-4 px-4 pb-6">
          {query.error ? (
            isApiError(query.error) && query.error.status === 404 ? (
              <Alert>
                <AlertDescription>{t("app.documents.notFound")}</AlertDescription>
              </Alert>
            ) : (
              <ApiErrorAlert error={query.error} onRetry={() => void query.refetch()} />
            )
          ) : query.data ? (
            <DocumentDetailBody
              key={query.data.id}
              tenant={tenant}
              document={query.data}
              onDeleted={onClose}
            />
          ) : (
            <div className="flex flex-col gap-3" aria-busy="true">
              <Skeleton className="h-40 w-full" />
              <Skeleton className="h-24 w-full" />
            </div>
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}

function DocumentDetailBody({
  tenant,
  document,
  onDeleted,
}: {
  tenant: string;
  document: DocumentDetail;
  onDeleted: () => void;
}) {
  const t = useTranslations();
  const locale = useLocale();
  const format = useFormatter();
  const notify = useNotify();
  const confirm = useConfirm();
  const areas = useDocumentAreas(tenant);
  const { base, extension } = splitFileName(document.fileName);
  const save = useDocumentMutation(tenant, (values: EditDocumentValues) =>
    updateDocument(document.id, {
      fileName: values.baseName + extension,
      referenceYear: values.referenceYear,
      areaId: values.areaId === NO_AREA ? null : values.areaId,
      description: values.description || null,
      // Custom fields of documents have no editor yet: the current values go back unchanged.
      customFields: document.customFields,
    }),
  );
  const remove = useDocumentMutation(tenant, () => deleteDocument(document.id));
  const form = useZodForm(editDocumentSchema, {
    defaultValues: {
      baseName: base,
      referenceYear: Number(document.referenceYear),
      areaId: document.area?.id ?? NO_AREA,
      description: document.description ?? "",
    },
    disabled: !document.canEdit,
  });
  const years = referenceYears();
  const current = Number(document.referenceYear);
  const yearChoices = years.includes(current) ? years : [...years, current];
  const previewable = PREVIEW_TYPES.includes(document.contentType);

  const submit = form.handleSubmit(async (values) => {
    try {
      await save.mutateAsync(values);
      notify.success("app.documents.saved");
    } catch (error) {
      if (isApiError(error)) {
        applyApiErrors(error, form.setError, ["referenceYear", "areaId", "description"], {
          fileName: "baseName",
        });
      }
    }
  });

  const onDelete = async () => {
    const confirmed = await confirm({
      description: t("app.documents.deleteConfirm", { name: document.fileName }),
      confirmLabel: t("Delete"),
      variant: "destructive",
    });
    if (!confirmed) {
      return;
    }

    try {
      await remove.mutateAsync(undefined);
      notify.success("app.documents.deleted");
      onDeleted();
    } catch (error) {
      notify.error(error);
    }
  };

  return (
    <>
      <DocumentPreview tenant={tenant} document={document} />
      <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
        <dt className="text-muted-foreground">{t("Client")}</dt>
        <dd>{document.client.fullName}</dd>
        {document.case ? (
          <>
            <dt className="text-muted-foreground">{t("app.documents.case")}</dt>
            <dd>
              {document.case.number} · {document.case.serviceName}
            </dd>
          </>
        ) : null}
        {document.folder ? (
          <>
            <dt className="text-muted-foreground">{t("Folder")}</dt>
            <dd>{document.folder.path}</dd>
          </>
        ) : null}
        <dt className="text-muted-foreground">{t("app.documents.size")}</dt>
        <dd>{formatSize(Number(document.size), locale)}</dd>
        <dt className="text-muted-foreground">{t("UploadedBy")}</dt>
        <dd>{document.uploadedBy?.fullName ?? "—"}</dd>
        <dt className="text-muted-foreground">{t("UploadDate")}</dt>
        <dd>
          {format.dateTime(new Date(document.uploadedAt), {
            dateStyle: "medium",
            timeStyle: "short",
          })}
        </dd>
        <dt className="text-muted-foreground">{t("Status")}</dt>
        <dd>
          {document.status === "Available" ? (
            t("app.documents.status.available")
          ) : (
            <DocumentStatusBadge status={document.status} />
          )}
        </dd>
      </dl>

      <div className="flex flex-wrap gap-2">
        <Button asChild>
          <a href={documentContentUrl(document.id)} download={document.fileName}>
            <DownloadIcon aria-hidden /> {t("Download")}
          </a>
        </Button>
        {previewable ? (
          <Button variant="outline" asChild>
            <a
              href={documentContentUrl(document.id, true)}
              target="_blank"
              rel="noopener noreferrer"
            >
              <ExternalLinkIcon aria-hidden /> {t("app.documents.viewer.newTab")}
            </a>
          </Button>
        ) : null}
        {document.canManage ? (
          <Button
            type="button"
            variant="outline"
            onClick={() => void onDelete()}
            disabled={remove.isPending}
          >
            <Trash2Icon aria-hidden /> {t("Delete")}
          </Button>
        ) : null}
      </div>

      <form onSubmit={(event) => void submit(event)} noValidate className="flex flex-col gap-4">
        {save.error &&
        !(isApiError(save.error) && Object.keys(save.error.fieldErrors).length > 0) ? (
          <ApiErrorAlert error={save.error} />
        ) : null}
        <FormField
          control={form.control}
          name="baseName"
          label={`${t("FileName")} *`}
          description={extension ? t("app.documents.extensionKept", { extension }) : undefined}
        >
          {(field, props) => <Input {...field} {...props} autoComplete="off" />}
        </FormField>
        <FormField control={form.control} name="referenceYear" label={`${t("ReferenceYear")} *`}>
          {(field, props) => (
            <Select
              value={String(field.value)}
              onValueChange={(value) => field.onChange(Number(value))}
              disabled={!document.canEdit}
            >
              <SelectTrigger {...props}>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {yearChoices.map((year) => (
                  <SelectItem key={year} value={String(year)}>
                    {year}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        </FormField>
        <FormField control={form.control} name="areaId" label={t("Area")}>
          {(field, props) => (
            <Select value={field.value} onValueChange={field.onChange} disabled={!document.canEdit}>
              <SelectTrigger {...props}>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={NO_AREA}>{t("app.documents.noArea")}</SelectItem>
                {(areas.data ?? [])
                  .filter((area) => area.isActive || area.id === document.area?.id)
                  .map((area) => (
                    <SelectItem key={area.id} value={area.id}>
                      {area.name}
                    </SelectItem>
                  ))}
              </SelectContent>
            </Select>
          )}
        </FormField>
        <FormField control={form.control} name="description" label={t("Description")}>
          {(field, props) => <Textarea {...field} {...props} rows={3} />}
        </FormField>
        {document.canEdit ? (
          <Button type="submit" className="self-end" disabled={save.isPending}>
            {t("Save")}
          </Button>
        ) : (
          <p className="text-sm text-muted-foreground">{t("app.documents.readOnly")}</p>
        )}
      </form>
    </>
  );
}
