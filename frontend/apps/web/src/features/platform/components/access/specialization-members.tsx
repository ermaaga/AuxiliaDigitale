"use client";

import * as React from "react";
import Link from "next/link";
import { ArrowLeftIcon, LockIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import { Alert, AlertDescription } from "@auxilia/ui/components/alert";
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
import { Input } from "@auxilia/ui/components/input";
import { Label } from "@auxilia/ui/components/label";
import { Skeleton } from "@auxilia/ui/components/skeleton";

import { useConfirm } from "@/components/confirm/confirm-provider";
import { DataTable } from "@/components/data-table/data-table";
import type { DataTableColumn } from "@/components/data-table/table-model";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { tenantConsoleHref } from "@/lib/href";
import { useNotify } from "@/lib/notify";

import {
  addSpecializationMembers,
  removeSpecializationMember,
  useAccessMutation,
  useSpecializationCandidates,
  useSpecializationMembers,
  useSpecializations,
  type Specialization,
  type SpecializationMember,
} from "../../access-api";
import { roleLabel } from "../../labels";

/** Delay before a typed search reaches the API. */
const SEARCH_DELAY_MS = 300;

/** A user as shown in the lists: full name with the user name, or the user name alone. */
export function memberName(member: SpecializationMember): string {
  return member.fullName ? `${member.fullName} (${member.userName})` : member.userName;
}

/**
 * The users holding a specialization (F12, legacy `AssignSpecialization`, Q33: reachable from the list and back):
 * remove them one by one, add several users of the specialization's role at once.
 */
export function SpecializationMembers({ slug, id }: { slug: string; id: string }) {
  const t = useTranslations();
  const list = useSpecializations(slug);
  const specialization = list.data?.find((item) => item.id === id);
  const back = (
    <Button variant="ghost" size="sm" className="self-start" asChild>
      <Link href={tenantConsoleHref(slug, "/specializations")}>
        <ArrowLeftIcon aria-hidden /> {t("app.platform.specializations.back")}
      </Link>
    </Button>
  );

  if (list.error) {
    return <ApiErrorAlert error={list.error} onRetry={() => void list.refetch()} />;
  }

  if (!list.data) {
    return <Skeleton className="h-64 w-full" />;
  }

  if (!specialization) {
    return (
      <div className="flex flex-col gap-4">
        {back}
        <Alert>
          <AlertDescription>{t("app.platform.specializations.notFound")}</AlertDescription>
        </Alert>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-4">
      {back}
      <Details specialization={specialization} />
      <Members slug={slug} specialization={specialization} />
      <Candidates slug={slug} specialization={specialization} />
    </div>
  );
}

function Details({ specialization }: { specialization: Specialization }) {
  const t = useTranslations();
  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2 className="flex flex-wrap items-center gap-2 text-base font-semibold">
            {specialization.name}
            <Badge variant="outline">{roleLabel(t, specialization.role)}</Badge>
            {specialization.isPrivate ? (
              <Badge variant="secondary">
                <LockIcon aria-hidden /> {t("app.platform.specializations.private")}
              </Badge>
            ) : null}
          </h2>
        </CardTitle>
        {specialization.description ? (
          <CardDescription>{specialization.description}</CardDescription>
        ) : null}
      </CardHeader>
      <CardContent>
        <dl className="grid gap-2 text-sm sm:grid-cols-2">
          <div>
            <dt className="text-muted-foreground">{t("Email")}</dt>
            <dd>{specialization.email ?? "—"}</dd>
          </div>
          <div>
            <dt className="text-muted-foreground">{t("app.platform.specializations.workPhone")}</dt>
            <dd>{specialization.workPhone ?? "—"}</dd>
          </div>
        </dl>
      </CardContent>
    </Card>
  );
}

function Members({ slug, specialization }: { slug: string; specialization: Specialization }) {
  const t = useTranslations();
  const notify = useNotify();
  const confirm = useConfirm();
  const members = useSpecializationMembers(slug, specialization.id);
  const remove = useAccessMutation(slug, (userId: string) =>
    removeSpecializationMember(slug, specialization.id, userId),
  );

  const columns: DataTableColumn<SpecializationMember>[] = [
    {
      id: "name",
      header: t("app.platform.specializations.user"),
      hideable: false,
      mobile: "title",
      cell: (member) => (
        <span className="flex flex-wrap items-center gap-2">
          {memberName(member)}
          {member.isActive ? null : (
            <Badge variant="outline">{t("app.platform.specializations.inactiveUser")}</Badge>
          )}
        </span>
      ),
    },
    { id: "email", header: t("Email"), cell: (member) => member.email ?? "—" },
    {
      id: "actions",
      header: t("Actions"),
      hideable: false,
      cell: (member) => (
        <Button
          variant="ghost"
          size="sm"
          disabled={remove.isPending}
          aria-label={t("app.platform.specializations.removeNamed", { user: memberName(member) })}
          onClick={async () => {
            if (
              await confirm({
                title: t("app.platform.specializations.removeTitle"),
                description: t("app.platform.specializations.removeText", {
                  user: memberName(member),
                  name: specialization.name,
                }),
                confirmLabel: t("app.platform.specializations.remove"),
                variant: "destructive",
              })
            ) {
              await remove.mutateAsync(member.userId).then(
                () => notify.success("app.platform.specializations.removed"),
                (error: unknown) => notify.error(error),
              );
            }
          }}
        >
          {t("app.platform.specializations.remove")}
        </Button>
      ),
    },
  ];

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2 className="text-base font-semibold">{t("app.platform.specializations.members")}</h2>
        </CardTitle>
      </CardHeader>
      <CardContent>
        <DataTable
          label={t("app.platform.specializations.members")}
          columns={columns}
          rows={members.data}
          getRowId={(member) => member.userId}
          totalCount={members.data?.length ?? 0}
          page={1}
          pageSize={100}
          sort={null}
          onPageChange={() => {}}
          onPageSizeChange={() => {}}
          onSortChange={() => {}}
          isLoading={members.isPending}
          error={members.error}
          onRetry={() => void members.refetch()}
          hidePaging
        />
      </CardContent>
    </Card>
  );
}

function Candidates({ slug, specialization }: { slug: string; specialization: Specialization }) {
  const t = useTranslations();
  const notify = useNotify();
  const [text, setText] = React.useState("");
  const [search, setSearch] = React.useState("");
  const timer = React.useRef<ReturnType<typeof setTimeout>>(undefined);
  const [selected, setSelected] = React.useState<ReadonlySet<string>>(new Set());
  const candidates = useSpecializationCandidates(slug, specialization.id, search);
  const add = useAccessMutation(slug, (userIds: string[]) =>
    addSpecializationMembers(slug, specialization.id, userIds),
  );
  const role = roleLabel(t, specialization.role);

  React.useEffect(() => () => clearTimeout(timer.current), []);

  function type(value: string) {
    setText(value);
    clearTimeout(timer.current);
    timer.current = setTimeout(() => setSearch(value.trim()), SEARCH_DELAY_MS);
  }

  function toggle(userId: string, checked: boolean) {
    setSelected((current) => {
      const next = new Set(current);
      if (checked) {
        next.add(userId);
      } else {
        next.delete(userId);
      }

      return next;
    });
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2 className="text-base font-semibold">{t("app.platform.specializations.addTitle")}</h2>
        </CardTitle>
        <CardDescription>
          {t("app.platform.specializations.addDescription", { role })}
        </CardDescription>
      </CardHeader>
      <CardContent>
        <form
          className="flex flex-col gap-3"
          onSubmit={(event) => {
            event.preventDefault();
            add.mutateAsync([...selected]).then(
              () => {
                setSelected(new Set());
                notify.success("app.platform.specializations.added");
              },
              (error: unknown) => notify.error(error),
            );
          }}
        >
          <div className="flex flex-col gap-1">
            <Label htmlFor="specialization-candidate-search">{t("Search")}</Label>
            <Input
              id="specialization-candidate-search"
              type="search"
              value={text}
              maxLength={100}
              onChange={(event) => type(event.target.value)}
              placeholder={t("app.platform.specializations.searchPlaceholder")}
            />
          </div>
          {candidates.error ? (
            <ApiErrorAlert error={candidates.error} onRetry={() => void candidates.refetch()} />
          ) : null}
          {candidates.data && candidates.data.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              {t("app.platform.specializations.noCandidates", { role })}
            </p>
          ) : null}
          {candidates.data && candidates.data.length > 0 ? (
            <ul
              className="flex max-h-80 flex-col divide-y overflow-y-auto rounded-md border"
              aria-label={t("app.platform.specializations.candidates", { role })}
            >
              {candidates.data.map((candidate) => {
                const id = `candidate-${candidate.userId}`;
                return (
                  <li key={candidate.userId} className="flex items-center gap-3 px-3 py-2">
                    <Checkbox
                      id={id}
                      checked={selected.has(candidate.userId)}
                      onCheckedChange={(checked) => toggle(candidate.userId, checked === true)}
                    />
                    <Label htmlFor={id} className="flex flex-1 flex-col items-start gap-0.5">
                      <span>{memberName(candidate)}</span>
                      {candidate.email ? (
                        <span className="text-xs text-muted-foreground">{candidate.email}</span>
                      ) : null}
                    </Label>
                  </li>
                );
              })}
            </ul>
          ) : null}
          {candidates.isPending ? <Skeleton className="h-24 w-full" /> : null}
          <Button
            type="submit"
            className="self-start"
            disabled={selected.size === 0 || add.isPending}
          >
            {t("app.platform.specializations.addSelected", { count: selected.size })}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}
