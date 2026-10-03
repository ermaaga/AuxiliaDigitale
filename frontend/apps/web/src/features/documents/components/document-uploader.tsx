"use client";

import * as React from "react";
import { isApiError } from "@auxilia/api-client";
import { useQueryClient } from "@tanstack/react-query";
import { UploadIcon, XIcon } from "lucide-react";
import { useLocale, useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import { Card, CardContent, CardHeader, CardTitle } from "@auxilia/ui/components/card";
import { Input } from "@auxilia/ui/components/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@auxilia/ui/components/select";
import { Textarea } from "@auxilia/ui/components/textarea";

import { Combobox } from "@/components/combobox";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { applyApiErrors, FormField, useZodForm } from "@/components/forms/form";
import { useClients } from "@/features/clients";
import { useNotify } from "@/lib/notify";

import { documentsKey, uploadDocuments, useDocumentAreas } from "../api";
import { formatSize, MAX_FILES, referenceYears, uploadSchema } from "../schemas/document";

const NO_AREA = "none";

/**
 * The uploader of documents (F14, legacy `DocumentUploader`): choose, drag & drop or paste (Ctrl/Cmd+V) up to 50
 * files, the same metadata for all, a custom name only for one file, upload with progress. Client, case and folder
 * come from the page when given; otherwise the client is chosen. The API checks type, content and size.
 */
export function DocumentUploader({
  tenant,
  clientId,
  caseId,
  folderId,
}: {
  tenant: string;
  clientId?: string;
  caseId?: string;
  folderId?: string;
}) {
  const t = useTranslations();
  const locale = useLocale();
  const notify = useNotify();
  const queries = useQueryClient();
  const input = React.useRef<HTMLInputElement>(null);
  const [open, setOpen] = React.useState(false);
  const [files, setFiles] = React.useState<File[]>([]);
  const [dragging, setDragging] = React.useState(false);
  const [progress, setProgress] = React.useState<number | undefined>();
  const [error, setError] = React.useState<unknown>();
  const [fileErrors, setFileErrors] = React.useState<Readonly<Record<number, string>>>({});
  const [clientSearch, setClientSearch] = React.useState("");
  const areas = useDocumentAreas(tenant);
  const clients = useClients(tenant, {
    view: "all",
    page: 1,
    pageSize: 20,
    "filter[fullName]": clientSearch || undefined,
  });
  const years = referenceYears();
  const form = useZodForm(uploadSchema, {
    defaultValues: {
      clientId: clientId ?? "",
      referenceYear: new Date().getFullYear(),
      areaId: NO_AREA,
      description: "",
      fileName: "",
    },
  });

  const add = React.useCallback((added: Iterable<File>) => {
    setFiles((current) => [...current, ...Array.from(added)].slice(0, MAX_FILES));
    setFileErrors({});
  }, []);

  // Legacy: files pasted from the clipboard join the selection while the uploader is open.
  React.useEffect(() => {
    if (!open) {
      return;
    }

    const onPaste = (event: ClipboardEvent) => {
      const pasted = event.clipboardData?.files;
      if (pasted && pasted.length > 0) {
        event.preventDefault();
        add(pasted);
      }
    };
    document.addEventListener("paste", onPaste);
    return () => document.removeEventListener("paste", onPaste);
  }, [open, add]);

  const submit = form.handleSubmit(async (values) => {
    setError(undefined);
    setFileErrors({});
    setProgress(0);
    try {
      const result = await uploadDocuments(
        files,
        {
          clientId: values.clientId,
          caseId,
          folderId,
          referenceYear: values.referenceYear,
          areaId: values.areaId === NO_AREA ? undefined : values.areaId,
          description: values.description,
          fileName: files.length === 1 ? values.fileName : undefined,
        },
        setProgress,
      );
      notify.success(
        result.documentIds.length === 1 ? "app.documents.uploadedOne" : "app.documents.uploaded",
      );
      setFiles([]);
      form.reset({ ...values, description: "", fileName: "" });
      await queries.invalidateQueries({ queryKey: documentsKey(tenant) });
    } catch (failure) {
      setError(failure);
      if (isApiError(failure)) {
        // `files[i]`: the message of one file; the other fields go on the form.
        const perFile: Record<number, string> = {};
        for (const [field, messages] of Object.entries(failure.fieldErrors)) {
          const match = /^files\[(\d+)\]$/.exec(field);
          if (match && messages[0]) {
            perFile[Number(match[1])] = messages[0];
          }
        }

        setFileErrors(perFile);
        applyApiErrors(failure, form.setError, [
          "clientId",
          "referenceYear",
          "areaId",
          "description",
          "fileName",
        ]);
      }
    } finally {
      setProgress(undefined);
    }
  });

  if (!open) {
    return (
      <Button type="button" className="self-start" onClick={() => setOpen(true)}>
        <UploadIcon aria-hidden /> {t("UploadDocument")}
      </Button>
    );
  }

  const uploading = progress !== undefined;
  const clientOptions = (clients.data?.items ?? []).map((client) => ({
    value: client.id,
    label: `${client.lastName} ${client.firstName}`,
    description: client.email ?? undefined,
  }));

  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between">
        <CardTitle>
          <h2 className="text-base font-semibold">{t("UploadDocument")}</h2>
        </CardTitle>
        <Button
          type="button"
          variant="ghost"
          size="icon"
          aria-label={t("Close")}
          onClick={() => setOpen(false)}
        >
          <XIcon aria-hidden />
        </Button>
      </CardHeader>
      <CardContent>
        <form onSubmit={(event) => void submit(event)} noValidate className="flex flex-col gap-4">
          {error &&
          Object.keys(fileErrors).length === 0 &&
          !(isApiError(error) && Object.keys(error.fieldErrors).length > 0) ? (
            <ApiErrorAlert error={error} />
          ) : null}
          <div
            role="button"
            tabIndex={0}
            aria-describedby="documents-drop-hint"
            className={`flex flex-col items-center justify-center gap-2 rounded-md border-2 border-dashed p-6 text-center text-sm ${dragging ? "border-primary bg-accent" : "border-muted-foreground/30"}`}
            onClick={() => input.current?.click()}
            onKeyDown={(event) => {
              if (event.key === "Enter" || event.key === " ") {
                event.preventDefault();
                input.current?.click();
              }
            }}
            onDragOver={(event) => {
              event.preventDefault();
              setDragging(true);
            }}
            onDragLeave={() => setDragging(false)}
            onDrop={(event) => {
              event.preventDefault();
              setDragging(false);
              add(event.dataTransfer.files);
            }}
          >
            <UploadIcon aria-hidden className="size-6 text-muted-foreground" />
            <span className="font-medium">{t("SelectFiles")}</span>
            <span id="documents-drop-hint" className="text-muted-foreground">
              {t("app.documents.dropHint", { max: MAX_FILES })}
            </span>
          </div>
          {/* Outside the drop zone: an input inside a role="button" is a nested interactive control (axe). */}
          <input
            ref={input}
            type="file"
            multiple
            className="sr-only"
            aria-label={t("SelectFiles")}
            tabIndex={-1}
            onChange={(event) => {
              if (event.target.files) {
                add(event.target.files);
              }

              event.target.value = "";
            }}
          />

          {files.length > 0 ? (
            <ul className="flex flex-col gap-1 text-sm" aria-label={t("app.documents.selected")}>
              {files.map((file, index) => (
                <li
                  key={`${file.name}-${index}`}
                  className="flex flex-col rounded-md border px-3 py-2"
                >
                  <span className="flex items-center justify-between gap-2">
                    <span className="truncate">{file.name}</span>
                    <span className="flex items-center gap-2 text-muted-foreground">
                      {formatSize(file.size, locale)}
                      <Button
                        type="button"
                        variant="ghost"
                        size="icon"
                        aria-label={t("app.documents.removeFile", { name: file.name })}
                        disabled={uploading}
                        onClick={() =>
                          setFiles((current) => current.filter((_, position) => position !== index))
                        }
                      >
                        <XIcon aria-hidden />
                      </Button>
                    </span>
                  </span>
                  {fileErrors[index] ? (
                    <span role="alert" className="text-destructive">
                      {t.has(fileErrors[index]) ? t(fileErrors[index]) : fileErrors[index]}
                    </span>
                  ) : null}
                </li>
              ))}
            </ul>
          ) : null}

          <div className="grid gap-4 sm:grid-cols-2">
            {clientId === undefined ? (
              <FormField control={form.control} name="clientId" label={`${t("Client")} *`}>
                {(field, props) => (
                  <Combobox
                    {...props}
                    options={clientOptions}
                    value={field.value || undefined}
                    onChange={(value) => field.onChange(value ?? "")}
                    placeholder={t("app.documents.chooseClient")}
                    onSearch={setClientSearch}
                    loading={clients.isFetching}
                  />
                )}
              </FormField>
            ) : null}
            <FormField
              control={form.control}
              name="referenceYear"
              label={`${t("ReferenceYear")} *`}
            >
              {(field, props) => (
                <Select
                  value={String(field.value)}
                  onValueChange={(value) => field.onChange(Number(value))}
                >
                  <SelectTrigger {...props}>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {years.map((year) => (
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
                <Select value={field.value} onValueChange={field.onChange}>
                  <SelectTrigger {...props}>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value={NO_AREA}>{t("app.documents.noArea")}</SelectItem>
                    {(areas.data ?? [])
                      .filter((area) => area.isActive)
                      .map((area) => (
                        <SelectItem key={area.id} value={area.id}>
                          {area.name}
                        </SelectItem>
                      ))}
                  </SelectContent>
                </Select>
              )}
            </FormField>
            <FormField
              control={form.control}
              name="fileName"
              label={t("app.documents.customName")}
              description={t("app.documents.customNameHint")}
            >
              {(field, props) => (
                <Input {...field} {...props} autoComplete="off" disabled={files.length !== 1} />
              )}
            </FormField>
            <div className="sm:col-span-2">
              <FormField control={form.control} name="description" label={t("Description")}>
                {(field, props) => <Textarea {...field} {...props} rows={2} />}
              </FormField>
            </div>
          </div>

          {uploading ? (
            <progress
              className="h-2 w-full"
              max={1}
              value={progress}
              aria-label={t("app.documents.uploading")}
            />
          ) : null}
          <Button type="submit" className="self-end" disabled={files.length === 0 || uploading}>
            <UploadIcon aria-hidden /> {t("Upload")}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}
