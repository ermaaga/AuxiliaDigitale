"use client";

import Link from "next/link";
import dynamic from "next/dynamic";
import { parseAsStringLiteral, useQueryState } from "nuqs";
import { useFormatter, useTranslations } from "next-intl";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@auxilia/ui/components/card";
import { Label } from "@auxilia/ui/components/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@auxilia/ui/components/select";
import { Skeleton } from "@auxilia/ui/components/skeleton";

import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { tenantHref } from "@/lib/href";

import {
  PERIODS,
  pointLabel,
  useDashboard,
  type DashboardCard,
  type DashboardChart,
  type DashboardList,
} from "../api";

// Charts are heavy and browser-only: loaded on demand (skill auxilia-ui-design).
const DashboardChartView = dynamic(() => import("./dashboard-chart"), {
  ssr: false,
  loading: () => <Skeleton className="h-64 w-full" />,
});

/**
 * The dashboard of the user's role (F27, skill auxilia-ui-design): KPI cards that open the filtered list, charts with
 * the period (week, month, year, all — Q43) and short lists (today, due soon, next appointments). Everything comes from
 * `GET /dashboard`: the modules decide what each role sees.
 */
export function DashboardView({ tenant }: { tenant: string }) {
  const t = useTranslations();
  const [period, setPeriod] = useQueryState(
    "period",
    parseAsStringLiteral(PERIODS).withDefault("month").withOptions({ history: "replace" }),
  );
  const dashboard = useDashboard(tenant, period);

  if (dashboard.error) {
    return <ApiErrorAlert error={dashboard.error} onRetry={() => void dashboard.refetch()} />;
  }

  const data = dashboard.data;
  return (
    <div className="flex flex-col gap-6" aria-busy={dashboard.isFetching || undefined}>
      <section
        aria-label={t("app.dashboard.figures")}
        className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4"
      >
        {data
          ? data.cards.map((card) => <Kpi key={card.key} tenant={tenant} card={card} />)
          : Array.from({ length: 4 }, (_, index) => (
              <Skeleton key={index} className="h-28 w-full" />
            ))}
      </section>

      {data && data.charts.length > 0 ? (
        <section className="flex flex-col gap-3" aria-label={t("app.dashboard.charts")}>
          <div className="flex items-end justify-end gap-2">
            <div className="flex flex-col gap-1">
              <Label htmlFor="dashboard-period">{t("app.dashboard.period")}</Label>
              <Select
                value={period}
                onValueChange={(value) => void setPeriod(value as (typeof PERIODS)[number])}
              >
                <SelectTrigger id="dashboard-period" className="w-40">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {PERIODS.map((item) => (
                    <SelectItem key={item} value={item}>
                      {t(`app.dashboard.periods.${item}` as "app.dashboard.periods.month")}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </div>
          <div className="grid gap-4 lg:grid-cols-2">
            {data.charts.map((chart) => (
              <ChartCard key={chart.key} chart={chart} />
            ))}
          </div>
        </section>
      ) : null}

      {data && data.lists.length > 0 ? (
        <section className="grid gap-4 lg:grid-cols-2" aria-label={t("app.dashboard.lists")}>
          {data.lists.map((list) => (
            <ListCard key={list.key} tenant={tenant} list={list} />
          ))}
        </section>
      ) : null}
    </div>
  );
}

function Kpi({ tenant, card }: { tenant: string; card: DashboardCard }) {
  const t = useTranslations();
  const format = useFormatter();
  const title = card.label ?? t(card.labelKey as "app.dashboard.openCases");
  const body = (
    <Card className="h-full transition-colors hover:bg-muted/50">
      <CardHeader className="pb-2">
        <CardTitle className="text-sm font-medium text-muted-foreground">{title}</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-1">
        <span className="text-3xl font-semibold">{format.number(Number(card.value))}</span>
        {card.detail ? (
          <span className="text-sm text-muted-foreground">
            {/^\d{4}-\d{2}-\d{2}$/.test(card.detail)
              ? t("app.dashboard.dueOn", {
                  date: format.dateTime(new Date(`${card.detail}T00:00:00`), {
                    dateStyle: "medium",
                  }),
                })
              : card.detail}
          </span>
        ) : null}
      </CardContent>
    </Card>
  );

  return card.link ? (
    <Link
      href={tenantHref(tenant, card.link)}
      className="rounded-xl focus-visible:outline-2 focus-visible:outline-ring"
    >
      {body}
    </Link>
  ) : (
    body
  );
}

function ChartCard({ chart }: { chart: DashboardChart }) {
  const t = useTranslations();
  const format = useFormatter();
  const title = t(chart.labelKey as "app.dashboard.casesPerService");
  const points = chart.points.map((point) => ({
    name: pointLabel(
      point,
      (key) => (t.has(key) ? t(key) : key),
      (date) => format.dateTime(date, { month: "short", year: "numeric" }),
      (date) => format.dateTime(date, { day: "2-digit", month: "short" }),
    ),
    value: Number(point.value),
  }));

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">{title}</CardTitle>
      </CardHeader>
      <CardContent>
        {points.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t("app.dashboard.noData")}</p>
        ) : (
          <figure className="flex flex-col gap-2">
            <DashboardChartView kind={chart.kind} points={points} />
            {/* The figures for screen readers and keyboard users (the chart itself is hidden from them). */}
            <table className="sr-only">
              <caption>{title}</caption>
              <tbody>
                {points.map((point) => (
                  <tr key={point.name}>
                    <th scope="row">{point.name}</th>
                    <td>{format.number(point.value)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </figure>
        )}
      </CardContent>
    </Card>
  );
}

function ListCard({ tenant, list }: { tenant: string; list: DashboardList }) {
  const t = useTranslations();
  const format = useFormatter();
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">
          {t(list.labelKey as "app.dashboard.appointmentsToday")}
        </CardTitle>
        {list.items.length === 0 ? (
          <CardDescription>{t("app.dashboard.nothing")}</CardDescription>
        ) : null}
      </CardHeader>
      {list.items.length > 0 ? (
        <CardContent>
          <ul
            className="flex flex-col divide-y"
            aria-label={t(list.labelKey as "app.dashboard.appointmentsToday")}
          >
            {list.items.map((item) => (
              <li key={item.id} className="flex flex-wrap items-center justify-between gap-2 py-2">
                <span className="flex min-w-0 flex-col">
                  {item.link ? (
                    <Link
                      href={tenantHref(tenant, item.link)}
                      className="truncate font-medium underline-offset-4 hover:underline"
                    >
                      {item.title}
                    </Link>
                  ) : (
                    <span className="truncate font-medium">{item.title}</span>
                  )}
                  {item.subtitle ? (
                    <span className="text-sm text-muted-foreground">{item.subtitle}</span>
                  ) : null}
                </span>
                <span className="flex items-center gap-2 text-sm text-muted-foreground">
                  {item.when
                    ? format.dateTime(new Date(item.when), {
                        dateStyle: "medium",
                        timeStyle: list.key === "casesDueSoon" ? undefined : "short",
                      })
                    : null}
                  {item.statusKey ? (
                    <span>· {t.has(item.statusKey) ? t(item.statusKey) : item.statusKey}</span>
                  ) : null}
                </span>
              </li>
            ))}
          </ul>
        </CardContent>
      ) : null}
    </Card>
  );
}
