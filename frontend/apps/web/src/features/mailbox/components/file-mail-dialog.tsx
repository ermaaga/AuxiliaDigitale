"use client";

import * as React from "react";
import { useQueryClient } from "@tanstack/react-query";
import { isApiError } from "@auxilia/api-client";
import { useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import { Checkbox } from "@auxilia/ui/components/checkbox";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@auxilia/ui/components/dialog";
import { Label } from "@auxilia/ui/components/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@auxilia/ui/components/select";
import { Textarea } from "@auxilia/ui/components/textarea";

import { Combobox } from "@/components/combobox";
import { useConfirm } from "@/components/confirm/confirm-provider";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { useCases, useServiceFolders } from "@/features/cases";
import { useClients } from "@/features/clients";
import { queryKey } from "@/lib/api/query-keys";
import { useNotify } from "@/lib/notify";

import { fileMessage, mailboxKey, type MailMessage, type MailMessageDetail } from "../api";

/** Code of "already filed into this case" (`Errors.Mailbox.MailAlreadyFiled`). */
export const ALREADY_FILED = "AUX-30030";

const NO_FOLDER = "none";
const DESCRIPTION_MAX = 1000;

/**
 * "File into case" (N05 #100): the client (proposed from the sender's address when it is a client's e-mail), one of
 * the client's cases the user may see (the API applies F10 again), the case folder, the attachments to file as separate
 * documents and a description. The original `.eml` is always filed; a message already in the case asks before filing
 * it again.
 */
export function FileMailDialog({
  tenant,
  message,
  attachments,
  proposedEmail,
  open,
  onOpenChange,
}: {
  tenant: string;
  message: MailMessage;
  attachments: MailMessageDetail["attachments"];
  proposedEmail: string | undefined;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const t = useTranslations();
  const notify = useNotify();
  const confirm = useConfirm();
  const client = useQueryClient();
  const [clientSearch, setClientSearch] = React.useState("");
  // undefined until the user picks: the client proposed from the sender applies meanwhile.
  const [picked, setPicked] = React.useState<string | null>();
  const [caseId, setCaseId] = React.useState<string>();
  const [folderId, setFolderId] = React.useState(NO_FOLDER);
  const [chosen, setChosen] = React.useState<ReadonlySet<number>>(() => new Set());
  const [description, setDescription] = React.useState("");
  const [error, setError] = React.useState<unknown>();
  const [pending, setPending] = React.useState(false);
  const proposed = useClients(tenant, {
    view: "all",
    page: 1,
    pageSize: 5,
    "filter[email]": proposedEmail ?? "-",
  });
  const clients = useClients(tenant, {
    view: "all",
    page: 1,
    pageSize: 20,
    "filter[fullName]": clientSearch || undefined,
  });
  // The sender is proposed as the client when exactly one client has that e-mail.
  const matches = (proposed.data?.items ?? []).filter(
    (item) => item.email?.toLowerCase() === proposedEmail,
  );
  const proposedClient = proposedEmail && matches.length === 1 ? matches[0] : undefined;
  const clientId = picked === undefined ? proposedClient?.id : (picked ?? undefined);
  const cases = useCases(tenant, {
    page: 1,
    pageSize: 100,
    showAll: true,
    showCompleted: true,
    sort: "-startedOn",
    "filter[clientId]": clientId,
  });
  const selectedCase = clientId ? cases.data?.items.find((item) => item.id === caseId) : undefined;
  const folders = useServiceFolders(tenant, selectedCase?.service.id ?? "");

  const clientOptions = [
    ...(proposedClient ? [proposedClient] : []),
    ...(clients.data?.items ?? []).filter((item) => item.id !== proposedClient?.id),
  ].map((item) => ({
    value: item.id,
    label: `${item.lastName} ${item.firstName}`,
    description: item.email ?? undefined,
  }));

  const submit = async (force: boolean) => {
    if (!caseId) {
      return;
    }

    setPending(true);
    setError(undefined);
    try {
      await fileMessage(message.id, {
        caseId,
        folderId: folderId === NO_FOLDER ? null : folderId,
        attachments: [...chosen],
        description: description.trim() || null,
        force,
      });
      await Promise.all([
        client.invalidateQueries({ queryKey: mailboxKey(tenant) }),
        client.invalidateQueries({ queryKey: queryKey(tenant, "documents") }),
      ]);
      notify.success("app.mail.filed");
      onOpenChange(false);
    } catch (failure) {
      if (!force && isApiError(failure) && failure.errorCode === ALREADY_FILED) {
        setPending(false);
        if (
          await confirm({
            description: t("app.mail.alreadyFiled"),
            confirmLabel: t("app.mail.fileAgain"),
          })
        ) {
          await submit(true);
        }

        return;
      }

      setError(failure);
    } finally {
      setPending(false);
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent closeLabel={t("Close")} className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{t("app.mail.fileTitle")}</DialogTitle>
          <DialogDescription>{t("app.mail.fileDescription")}</DialogDescription>
        </DialogHeader>
        <form
          className="flex flex-col gap-4"
          noValidate
          onSubmit={(event) => {
            event.preventDefault();
            void submit(false);
          }}
        >
          {error ? <ApiErrorAlert error={error} /> : null}
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="file-mail-client">{`${t("Client")} *`}</Label>
            <Combobox
              id="file-mail-client"
              options={clientOptions}
              value={clientId}
              onChange={(value) => {
                setPicked(value ?? null);
                setCaseId(undefined);
                setFolderId(NO_FOLDER);
              }}
              placeholder={t("app.documents.chooseClient")}
              onSearch={setClientSearch}
              loading={clients.isFetching}
            />
            {proposedClient && clientId === proposedClient.id ? (
              <p className="text-xs text-muted-foreground">{t("app.mail.clientFromSender")}</p>
            ) : null}
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="file-mail-case">{`${t("app.mail.case")} *`}</Label>
            <Select
              value={caseId ?? ""}
              disabled={!clientId || cases.isFetching}
              onValueChange={(value) => {
                setCaseId(value);
                setFolderId(NO_FOLDER);
              }}
            >
              <SelectTrigger id="file-mail-case" className="w-full">
                <SelectValue placeholder={t("app.mail.chooseCase")} />
              </SelectTrigger>
              <SelectContent>
                {(clientId ? (cases.data?.items ?? []) : []).map((item) => (
                  <SelectItem key={item.id} value={item.id}>
                    {`${item.number} · ${item.service.name}`}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            {clientId && cases.isSuccess && cases.data.items.length === 0 ? (
              <p className="text-xs text-muted-foreground">{t("app.mail.noCases")}</p>
            ) : null}
          </div>
          {selectedCase && (folders.data?.length ?? 0) > 0 ? (
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="file-mail-folder">{t("app.mail.folder")}</Label>
              <Select value={folderId} onValueChange={setFolderId}>
                <SelectTrigger id="file-mail-folder" className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={NO_FOLDER}>{t("app.mail.noFolder")}</SelectItem>
                  {folders.data!.map((folder) => (
                    <SelectItem key={folder.id} value={folder.id}>
                      {folder.path}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          ) : null}
          {attachments.length > 0 ? (
            <fieldset className="flex flex-col gap-2">
              <legend className="mb-1 text-sm font-medium">{t("app.mail.fileAttachments")}</legend>
              {attachments.map((attachment) => {
                const index = Number(attachment.index);
                const id = `file-mail-attachment-${index}`;
                return (
                  <span key={index} className="flex items-center gap-2">
                    <Checkbox
                      id={id}
                      checked={chosen.has(index)}
                      onCheckedChange={(value) =>
                        setChosen((current) => {
                          const next = new Set(current);
                          if (value === true) {
                            next.add(index);
                          } else {
                            next.delete(index);
                          }

                          return next;
                        })
                      }
                    />
                    <Label htmlFor={id} className="font-normal break-all">
                      {attachment.fileName}
                    </Label>
                  </span>
                );
              })}
            </fieldset>
          ) : null}
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="file-mail-description">{t("app.mail.descriptionLabel")}</Label>
            <Textarea
              id="file-mail-description"
              value={description}
              maxLength={DESCRIPTION_MAX}
              onChange={(event) => setDescription(event.target.value)}
              rows={2}
            />
          </div>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              {t("Cancel")}
            </Button>
            <Button type="submit" disabled={!caseId || pending}>
              {t("app.mail.file")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
