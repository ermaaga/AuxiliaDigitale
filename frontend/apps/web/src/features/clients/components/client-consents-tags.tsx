"use client";

import * as React from "react";
import { useFormatter, useTranslations } from "next-intl";
import { Badge } from "@auxilia/ui/components/badge";
import { Button } from "@auxilia/ui/components/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@auxilia/ui/components/card";
import { Checkbox } from "@auxilia/ui/components/checkbox";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@auxilia/ui/components/dialog";
import { Input } from "@auxilia/ui/components/input";
import { Label } from "@auxilia/ui/components/label";
import { Skeleton } from "@auxilia/ui/components/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@auxilia/ui/components/table";

import { useConfirm } from "@/components/confirm/confirm-provider";
import { badgeStyle } from "@/components/custom-fields/custom-field-value";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { useNotify } from "@/lib/notify";
import { useCan } from "@/lib/permissions";

import {
  createTag,
  deleteTag,
  recordConsent,
  setClientTags,
  updateTag,
  useClientConsents,
  useClientMutation,
  useClientTags,
  useTags,
  type ClientConsents,
  type Tag,
} from "../api";
import { DIRECTORY_PERMISSIONS } from "../permissions";

/** Server limits (`Tag`, `Consent` in the API). */
export const TAG_NAME_MAX = 50;
export const CONSENT_NOTE_MAX = 500;
export const CONSENT_VERSION_MAX = 50;

/** A badge colour the API accepts (`#rrggbb`), or nothing. */
export function tagColor(value: string): string | null | undefined {
  const trimmed = value.trim();
  return trimmed === "" ? null : /^#[0-9a-fA-F]{6}$/.test(trimmed) ? trimmed : undefined;
}

/** Consents and tags of a client (N01, M-01; skill auxilia-ui-design "Client 360°: Consents & tags"). */
export function ClientConsentsTags({ tenant, clientId }: { tenant: string; clientId: string }) {
  return (
    <div className="grid gap-4 lg:grid-cols-2">
      <ClientTagsCard tenant={tenant} clientId={clientId} />
      <ClientConsentsCard tenant={tenant} clientId={clientId} />
    </div>
  );
}

function ClientTagsCard({ tenant, clientId }: { tenant: string; clientId: string }) {
  const t = useTranslations();
  const notify = useNotify();
  const canManage = useCan(DIRECTORY_PERMISSIONS.manageClients);
  const canManageTags = useCan(DIRECTORY_PERMISSIONS.manageTags);
  const tags = useTags(tenant);
  const mine = useClientTags(tenant, clientId);
  const [editing, setEditing] = React.useState<ReadonlySet<string>>();
  const [managing, setManaging] = React.useState(false);
  const save = useClientMutation(tenant, (ids: readonly string[]) => setClientTags(clientId, ids));
  const chosen = editing ?? new Set((mine.data ?? []).map((tag) => tag.id));

  const onSave = async () => {
    await save.mutateAsync([...chosen]).then(
      () => {
        setEditing(undefined);
        notify.success("app.clients.tags.saved");
      },
      (error: unknown) => notify.error(error),
    );
  };

  return (
    <Card>
      <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-2">
        <div className="flex flex-col gap-1.5">
          <CardTitle>
            <h2 className="text-base font-semibold">{t("app.clients.tags.title")}</h2>
          </CardTitle>
          <CardDescription>{t("app.clients.tags.description")}</CardDescription>
        </div>
        {canManageTags ? (
          <Button type="button" variant="outline" size="sm" onClick={() => setManaging(true)}>
            {t("app.clients.tags.manage")}
          </Button>
        ) : null}
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        {mine.error ? (
          <ApiErrorAlert error={mine.error} onRetry={() => void mine.refetch()} />
        ) : null}
        {!mine.data || !tags.data ? (
          <Skeleton className="h-16 w-full" />
        ) : tags.data.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t("app.clients.tags.none")}</p>
        ) : canManage ? (
          <>
            <ul className="flex flex-col gap-2">
              {tags.data.map((tag) => (
                <li key={tag.id} className="flex items-center gap-2">
                  <Checkbox
                    id={`client-tag-${tag.id}`}
                    checked={chosen.has(tag.id)}
                    onCheckedChange={(checked) => {
                      const next = new Set(chosen);
                      if (checked === true) {
                        next.add(tag.id);
                      } else {
                        next.delete(tag.id);
                      }

                      setEditing(next);
                    }}
                  />
                  <Label htmlFor={`client-tag-${tag.id}`} className="font-normal">
                    <Badge variant="outline" style={badgeStyle(tag.color)}>
                      {tag.name}
                    </Badge>
                  </Label>
                </li>
              ))}
            </ul>
            <Button
              type="button"
              size="sm"
              className="self-end"
              disabled={editing === undefined || save.isPending}
              onClick={() => void onSave()}
            >
              {t("Save")}
            </Button>
          </>
        ) : (
          <span className="flex flex-wrap gap-1">
            {mine.data.length === 0
              ? "—"
              : mine.data.map((tag) => (
                  <Badge key={tag.id} variant="outline" style={badgeStyle(tag.color)}>
                    {tag.name}
                  </Badge>
                ))}
          </span>
        )}
        {managing ? (
          <ManageTagsDialog
            tenant={tenant}
            tags={tags.data ?? []}
            onClose={() => setManaging(false)}
          />
        ) : null}
      </CardContent>
    </Card>
  );
}

/** Administrators create, rename, recolour and delete the tags (N01). */
function ManageTagsDialog({
  tenant,
  tags,
  onClose,
}: {
  tenant: string;
  tags: readonly Tag[];
  onClose: () => void;
}) {
  const t = useTranslations();
  const notify = useNotify();
  const confirm = useConfirm();
  const [name, setName] = React.useState("");
  const [color, setColor] = React.useState("");
  const create = useClientMutation(tenant, () => createTag(name.trim(), tagColor(color) ?? null));
  const rename = useClientMutation(
    tenant,
    (tag: { id: string; name: string; color: string | null }) =>
      updateTag(tag.id, tag.name, tag.color),
  );
  const remove = useClientMutation(tenant, (id: string) => deleteTag(id));
  const colorInvalid = tagColor(color) === undefined;

  return (
    <Dialog open onOpenChange={(open) => (open ? null : onClose())}>
      <DialogContent closeLabel={t("Close")} className="max-h-[90dvh] overflow-y-auto sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{t("app.clients.tags.manage")}</DialogTitle>
          <DialogDescription>{t("app.clients.tags.manageDescription")}</DialogDescription>
        </DialogHeader>
        <form
          className="flex flex-wrap items-end gap-2"
          onSubmit={(event) => {
            event.preventDefault();
            void create.mutateAsync(undefined).then(
              () => {
                setName("");
                setColor("");
                notify.success("app.clients.tags.created");
              },
              (error: unknown) => notify.error(error),
            );
          }}
        >
          <div className="flex flex-col gap-1">
            <Label htmlFor="new-tag-name">{t("Name")}</Label>
            <Input
              id="new-tag-name"
              className="w-48"
              maxLength={TAG_NAME_MAX}
              value={name}
              onChange={(event) => setName(event.target.value)}
            />
          </div>
          <div className="flex flex-col gap-1">
            <Label htmlFor="new-tag-color">{t("app.clients.tags.color")}</Label>
            <Input
              id="new-tag-color"
              className="w-28"
              placeholder="#72FA29"
              value={color}
              aria-invalid={colorInvalid || undefined}
              onChange={(event) => setColor(event.target.value)}
            />
          </div>
          <Button
            type="submit"
            size="sm"
            disabled={!name.trim() || colorInvalid || create.isPending}
          >
            {t("app.clients.tags.new")}
          </Button>
        </form>
        <ul className="flex flex-col gap-2">
          {tags.map((tag) => (
            <li key={tag.id} className="flex flex-wrap items-center justify-between gap-2">
              <Badge variant="outline" style={badgeStyle(tag.color)}>
                {tag.name}
              </Badge>
              <span className="text-xs text-muted-foreground">
                {t("app.clients.tags.count", { count: tag.clientCount })}
              </span>
              <span className="flex gap-1">
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  aria-label={t("app.clients.tags.renameNamed", { name: tag.name })}
                  onClick={() => {
                    const next = window.prompt(t("app.clients.tags.renamePrompt"), tag.name);
                    if (next && next.trim() && next.trim() !== tag.name) {
                      void rename
                        .mutateAsync({ id: tag.id, name: next.trim(), color: tag.color })
                        .catch((error: unknown) => notify.error(error));
                    }
                  }}
                >
                  {t("Edit")}
                </Button>
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  aria-label={t("app.clients.tags.deleteNamed", { name: tag.name })}
                  onClick={async () => {
                    if (
                      await confirm({
                        description: t("app.clients.tags.deleteText", {
                          name: tag.name,
                          count: tag.clientCount,
                        }),
                        confirmLabel: t("Delete"),
                        variant: "destructive",
                      })
                    ) {
                      await remove.mutateAsync(tag.id).then(
                        () => notify.success("app.clients.tags.deleted"),
                        (error: unknown) => notify.error(error),
                      );
                    }
                  }}
                >
                  {t("Delete")}
                </Button>
              </span>
            </li>
          ))}
        </ul>
      </DialogContent>
    </Dialog>
  );
}

function ClientConsentsCard({ tenant, clientId }: { tenant: string; clientId: string }) {
  const t = useTranslations();
  const format = useFormatter();
  const notify = useNotify();
  const confirm = useConfirm();
  const canManage = useCan(DIRECTORY_PERMISSIONS.manageClients);
  const consents = useClientConsents(tenant, clientId);
  const [version, setVersion] = React.useState("");
  const [note, setNote] = React.useState("");
  const record = useClientMutation(
    tenant,
    (change: { purpose: string; channel: string; granted: boolean }) =>
      recordConsent(clientId, {
        ...change,
        version: version.trim() || null,
        note: note.trim() || null,
      }),
  );
  const when = (instant: string) =>
    format.dateTime(new Date(instant), { dateStyle: "medium", timeStyle: "short" });
  const label = (purpose: string, channel: string) =>
    `${t(`app.clients.consents.purposes.${purpose}`)} · ${t(`app.clients.consents.channels.${channel}`)}`;

  const change = async (state: ClientConsents["current"][number]) => {
    const granted = !state.granted;
    if (
      await confirm({
        description: t(
          granted ? "app.clients.consents.grantText" : "app.clients.consents.revokeText",
          {
            consent: label(state.purpose, state.channel),
          },
        ),
        confirmLabel: t(granted ? "app.clients.consents.grant" : "app.clients.consents.revoke"),
        variant: granted ? "default" : "destructive",
      })
    ) {
      await record.mutateAsync({ purpose: state.purpose, channel: state.channel, granted }).then(
        () => {
          setNote("");
          notify.success("app.clients.consents.saved");
        },
        (error: unknown) => notify.error(error),
      );
    }
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2 className="text-base font-semibold">{t("app.clients.consents.title")}</h2>
        </CardTitle>
        <CardDescription>{t("app.clients.consents.description")}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {consents.error ? (
          <ApiErrorAlert error={consents.error} onRetry={() => void consents.refetch()} />
        ) : !consents.data ? (
          <Skeleton className="h-32 w-full" />
        ) : (
          <>
            <ul className="flex flex-col gap-2">
              {consents.data.current.map((state) => (
                <li
                  key={`${state.purpose}-${state.channel}`}
                  className="flex flex-wrap items-center justify-between gap-2"
                >
                  <span className="flex flex-col">
                    <span className="font-medium">{label(state.purpose, state.channel)}</span>
                    <span className="text-xs text-muted-foreground">
                      {state.since && state.source
                        ? t("app.clients.consents.since", {
                            date: when(state.since),
                            source: t(`app.clients.consents.sources.${state.source}`),
                          })
                        : t("app.clients.consents.never")}
                    </span>
                  </span>
                  <span className="flex items-center gap-2">
                    <Badge variant={state.granted ? "default" : "outline"}>
                      {t(
                        state.granted
                          ? "app.clients.consents.granted"
                          : "app.clients.consents.notGranted",
                      )}
                    </Badge>
                    {canManage ? (
                      <Button
                        type="button"
                        variant="outline"
                        size="sm"
                        disabled={record.isPending}
                        aria-label={t(
                          state.granted
                            ? "app.clients.consents.revokeNamed"
                            : "app.clients.consents.grantNamed",
                          {
                            consent: label(state.purpose, state.channel),
                          },
                        )}
                        onClick={() => void change(state)}
                      >
                        {t(
                          state.granted
                            ? "app.clients.consents.revoke"
                            : "app.clients.consents.grant",
                        )}
                      </Button>
                    ) : null}
                  </span>
                </li>
              ))}
            </ul>
            {canManage ? (
              <div className="grid gap-2 sm:grid-cols-2">
                <div className="flex flex-col gap-1">
                  <Label htmlFor="consent-version">{t("app.clients.consents.version")}</Label>
                  <Input
                    id="consent-version"
                    maxLength={CONSENT_VERSION_MAX}
                    value={version}
                    onChange={(event) => setVersion(event.target.value)}
                  />
                </div>
                <div className="flex flex-col gap-1">
                  <Label htmlFor="consent-note">{t("app.clients.consents.note")}</Label>
                  <Input
                    id="consent-note"
                    maxLength={CONSENT_NOTE_MAX}
                    value={note}
                    onChange={(event) => setNote(event.target.value)}
                  />
                </div>
              </div>
            ) : null}
            <div className="flex flex-col gap-2">
              <h3 className="text-sm font-medium">{t("app.clients.consents.history")}</h3>
              {consents.data.history.length === 0 ? (
                <p className="text-sm text-muted-foreground">
                  {t("app.clients.consents.noHistory")}
                </p>
              ) : (
                <Table aria-label={t("app.clients.consents.history")}>
                  <TableHeader>
                    <TableRow>
                      <TableHead>{t("app.clients.consents.when")}</TableHead>
                      <TableHead>{t("app.clients.consents.consent")}</TableHead>
                      <TableHead>{t("Status")}</TableHead>
                      <TableHead>{t("app.clients.consents.source")}</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {consents.data.history.map((item) => (
                      <TableRow key={item.id}>
                        <TableCell>{when(item.recordedAt)}</TableCell>
                        <TableCell>
                          {label(item.purpose, item.channel)}
                          {item.note ? (
                            <span className="block text-xs text-muted-foreground">{item.note}</span>
                          ) : null}
                        </TableCell>
                        <TableCell>
                          {t(
                            item.granted
                              ? "app.clients.consents.granted"
                              : "app.clients.consents.revoked",
                          )}
                        </TableCell>
                        <TableCell>
                          {t(`app.clients.consents.sources.${item.source}`)}
                          {item.recordedBy ? (
                            <span className="block text-xs text-muted-foreground">
                              {item.recordedBy}
                            </span>
                          ) : null}
                          {item.version ? (
                            <span className="block text-xs text-muted-foreground">
                              v. {item.version}
                            </span>
                          ) : null}
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              )}
            </div>
          </>
        )}
      </CardContent>
    </Card>
  );
}
