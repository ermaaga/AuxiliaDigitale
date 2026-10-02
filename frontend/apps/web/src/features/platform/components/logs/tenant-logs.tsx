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
import { Input } from "@auxilia/ui/components/input";
import { Label } from "@auxilia/ui/components/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@auxilia/ui/components/select";
import { Skeleton } from "@auxilia/ui/components/skeleton";

import { FilterSelect, SearchFilter } from "@/components/data-table/filters";
import { useTableState } from "@/components/data-table/use-table-state";
import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { useNotify } from "@/lib/notify";

import {
  DEBUG_DURATIONS,
  LOG_FILTERS,
  LOG_LEVELS,
  debugUntil,
  disableDebug,
  enableDebug,
  levelVariant,
  useLogLevelMutation,
  useTenantLogLevel,
  useTenantLogs,
  type TenantLogEntry,
} from "../../logs-api";

/**
 * The Log page of a tenant (F25, D-17, D-28): temporary debug level, then the events of its daily log files, newest
 * first, filtered by UTC days, minimum level, event code, trace, user and text (filters in the URL).
 */
export function TenantLogs({ slug }: { slug: string }) {
  return (
    <div className="flex flex-col gap-4">
      <LogLevelCard slug={slug} />
      <LogEvents slug={slug} />
    </div>
  );
}

function LogLevelCard({ slug }: { slug: string }) {
  const t = useTranslations();
  const format = useFormatter();
  const notify = useNotify();
  const level = useTenantLogLevel(slug);
  const [minutes, setMinutes] = React.useState<string>(String(DEBUG_DURATIONS[1].minutes));
  const enable = useLogLevelMutation(slug, (until: string) => enableDebug(slug, until));
  const disable = useLogLevelMutation(slug, () => disableDebug(slug));
  const debugUntilValue = level.data?.debugUntil;

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t("app.platform.logs.levelTitle")}</CardTitle>
        <CardDescription>{t("app.platform.logs.levelHint")}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        {level.error ? (
          <ApiErrorAlert error={level.error} onRetry={() => void level.refetch()} />
        ) : !level.data ? (
          <Skeleton className="h-9 w-72" />
        ) : (
          <>
            <p role="status" className="text-sm">
              {debugUntilValue
                ? t("app.platform.logs.debugActive", {
                    until: format.dateTime(new Date(debugUntilValue), {
                      dateStyle: "short",
                      timeStyle: "short",
                    }),
                  })
                : t("app.platform.logs.levelDefault", { level: level.data.defaultLevel })}
            </p>
            <div className="flex flex-wrap items-end gap-3">
              <div className="flex flex-col gap-1">
                <Label htmlFor="log-debug-duration">{t("app.platform.logs.duration")}</Label>
                <Select value={minutes} onValueChange={setMinutes}>
                  <SelectTrigger id="log-debug-duration" className="w-40">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {DEBUG_DURATIONS.map((duration) => (
                      <SelectItem key={duration.key} value={String(duration.minutes)}>
                        {t(`app.platform.logs.${duration.key}`)}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <Button
                type="button"
                disabled={enable.isPending}
                onClick={() =>
                  enable.mutate(debugUntil(new Date(), Number(minutes)), {
                    onSuccess: () => notify.success("app.platform.logs.debugEnabled"),
                    onError: (error) => notify.error(error),
                  })
                }
              >
                {t("app.platform.logs.enableDebug")}
              </Button>
              {debugUntilValue ? (
                <Button
                  type="button"
                  variant="outline"
                  disabled={disable.isPending}
                  onClick={() =>
                    disable.mutate(undefined, {
                      onSuccess: () => notify.success("app.platform.logs.debugDisabled"),
                      onError: (error) => notify.error(error),
                    })
                  }
                >
                  {t("app.platform.logs.disableDebug")}
                </Button>
              ) : null}
            </div>
          </>
        )}
      </CardContent>
    </Card>
  );
}

function LogEvents({ slug }: { slug: string }) {
  const t = useTranslations();
  const table = useTableState(LOG_FILTERS);
  const filters = table.filters;
  const logs = useTenantLogs(slug, filters);
  const events = logs.data?.pages.flatMap((page) => page.items) ?? [];

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t("app.platform.logs.events")}</CardTitle>
        <CardDescription>{t("app.platform.logs.timesNote")}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        <div
          role="group"
          aria-label={t("app.platform.logs.filters")}
          className="flex flex-wrap items-end gap-3"
        >
          <DayFilter
            id="log-from"
            label={t("app.platform.logs.from")}
            value={filters.from}
            onChange={(value) => table.setFilter("from", value)}
          />
          <DayFilter
            id="log-to"
            label={t("app.platform.logs.to")}
            value={filters.to}
            onChange={(value) => table.setFilter("to", value)}
          />
          <FilterSelect
            id="log-level"
            label={t("app.platform.logs.minimumLevel")}
            value={filters.level}
            onChange={(value) => table.setFilter("level", value)}
            options={LOG_LEVELS.map((level) => ({ value: level, label: level }))}
          />
          <SearchFilter
            id="log-code"
            label={t("app.platform.logs.code")}
            placeholder="AUX-11001"
            value={filters.code}
            onChange={(value) => table.setFilter("code", value)}
          />
          <SearchFilter
            id="log-trace"
            label={t("app.platform.logs.traceId")}
            placeholder=""
            value={filters.traceId}
            onChange={(value) => table.setFilter("traceId", value)}
          />
          <SearchFilter
            id="log-user"
            label={t("app.platform.logs.userId")}
            placeholder=""
            value={filters.userId}
            onChange={(value) => table.setFilter("userId", value)}
          />
          <SearchFilter
            id="log-text"
            label={t("app.platform.logs.text")}
            placeholder={t("app.platform.logs.textPlaceholder")}
            value={filters.text}
            onChange={(value) => table.setFilter("text", value)}
          />
          {table.hasFilters ? (
            <Button type="button" variant="ghost" size="sm" onClick={table.clearFilters}>
              {t("ClearFilters")}
            </Button>
          ) : null}
        </div>

        {logs.error ? (
          <ApiErrorAlert error={logs.error} onRetry={() => void logs.refetch()} />
        ) : logs.isPending ? (
          <div className="flex flex-col gap-2" aria-busy="true">
            {Array.from({ length: 5 }, (_, index) => (
              <Skeleton key={index} className="h-10 w-full" />
            ))}
          </div>
        ) : events.length === 0 ? (
          <p role="status" className="rounded-md border p-4 text-sm text-muted-foreground">
            {t("app.platform.logs.empty")}
          </p>
        ) : (
          <ul
            aria-label={t("app.platform.logs.events")}
            className="flex flex-col divide-y rounded-md border"
          >
            {events.map((event, index) => (
              <LogEventRow
                key={`${event.timestamp}-${index}`}
                event={event}
                onTrace={(traceId) => table.setFilter("traceId", traceId)}
              />
            ))}
          </ul>
        )}

        {logs.hasNextPage ? (
          <Button
            type="button"
            variant="outline"
            className="self-center"
            disabled={logs.isFetchingNextPage}
            onClick={() => void logs.fetchNextPage()}
          >
            {t("app.platform.logs.loadOlder")}
          </Button>
        ) : null}
      </CardContent>
    </Card>
  );
}

/** A UTC day (`yyyy-MM-dd`) of the search range. */
function DayFilter({
  id,
  label,
  value,
  onChange,
}: {
  id: string;
  label: string;
  value: string | undefined;
  onChange: (value: string | undefined) => void;
}) {
  return (
    <div className="flex flex-col gap-1">
      <Label htmlFor={id}>{label}</Label>
      <Input
        id={id}
        type="date"
        className="w-40"
        value={value ?? ""}
        onChange={(event) => onChange(event.target.value || undefined)}
      />
    </div>
  );
}

/** One event: time, level, code and message; the details open below (exception, trace, user, other fields). */
function LogEventRow({
  event,
  onTrace,
}: {
  event: TenantLogEntry;
  onTrace: (traceId: string) => void;
}) {
  const t = useTranslations();
  const format = useFormatter();
  const properties = Object.entries(event.properties);

  return (
    <li>
      <details className="group">
        <summary className="flex cursor-pointer flex-wrap items-baseline gap-x-3 gap-y-1 px-3 py-2 text-sm hover:bg-accent/50">
          <time
            dateTime={event.timestamp}
            className="font-mono text-xs text-muted-foreground tabular-nums"
          >
            {format.dateTime(new Date(event.timestamp), {
              dateStyle: "short",
              timeStyle: "medium",
            })}
          </time>
          <Badge variant={levelVariant(event.level)}>{event.level}</Badge>
          {event.eventCode ? <code className="text-xs">{event.eventCode}</code> : null}
          <span className="min-w-0 flex-1 break-words">{event.message}</span>
        </summary>
        <dl className="grid gap-x-4 gap-y-2 border-t px-3 py-3 text-sm sm:grid-cols-[max-content_1fr]">
          {event.traceId ? (
            <Detail label={t("app.platform.logs.traceId")}>
              <span className="flex flex-wrap items-center gap-2">
                <code className="text-xs break-all">{event.traceId}</code>
                <Button
                  type="button"
                  variant="link"
                  size="sm"
                  className="h-auto p-0"
                  onClick={() => onTrace(event.traceId!)}
                >
                  {t("app.platform.logs.filterTrace")}
                </Button>
              </span>
            </Detail>
          ) : null}
          {event.userId ? (
            <Detail label={t("app.platform.logs.userId")}>
              <code className="text-xs break-all">{event.userId}</code>
            </Detail>
          ) : null}
          {event.operation ? (
            <Detail label={t("app.platform.logs.operation")}>
              <code className="text-xs">{event.operation}</code>
            </Detail>
          ) : null}
          {event.source ? (
            <Detail label={t("app.platform.logs.source")}>
              <code className="text-xs break-all">{event.source}</code>
            </Detail>
          ) : null}
          {event.exception ? (
            <Detail label={t("app.platform.logs.exception")}>
              <pre className="max-h-80 overflow-auto rounded-md border bg-background p-2 text-xs whitespace-pre-wrap">
                {event.exception}
              </pre>
            </Detail>
          ) : null}
          {properties.length > 0 ? (
            <Detail label={t("app.platform.logs.properties")}>
              <dl className="grid grid-cols-[max-content_1fr] gap-x-3 gap-y-1 text-xs">
                {properties.map(([name, value]) => (
                  <React.Fragment key={name}>
                    <dt className="text-muted-foreground">{name}</dt>
                    <dd className="font-mono break-all">{value}</dd>
                  </React.Fragment>
                ))}
              </dl>
            </Detail>
          ) : null}
        </dl>
      </details>
    </li>
  );
}

function Detail({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <>
      <dt className="font-medium text-muted-foreground">{label}</dt>
      <dd className="min-w-0">{children}</dd>
    </>
  );
}
