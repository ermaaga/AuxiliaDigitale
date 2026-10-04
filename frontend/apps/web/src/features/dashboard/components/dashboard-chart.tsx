"use client";

import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  Legend,
  Line,
  LineChart,
  Pie,
  PieChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";

/** Colours of the series from the design tokens (light and dark themes, tenant branding). */
const SERIES = [
  "var(--chart-1)",
  "var(--chart-2)",
  "var(--chart-3)",
  "var(--chart-4)",
  "var(--chart-5)",
];

export type ChartPoint = { name: string; value: number };

/**
 * One chart of the dashboard (recharts, loaded on demand). Decorative for assistive technologies: the same figures are
 * in the table next to it (see `DashboardCharts`).
 */
export default function DashboardChart({
  kind,
  points,
}: {
  kind: string;
  points: readonly ChartPoint[];
}) {
  return (
    <div className="h-64 w-full" aria-hidden inert>
      <ResponsiveContainer width="100%" height="100%">
        {kind === "pie" ? (
          <PieChart accessibilityLayer={false}>
            <Pie
              data={[...points]}
              dataKey="value"
              nameKey="name"
              innerRadius="45%"
              outerRadius="80%"
              isAnimationActive={false}
            >
              {points.map((point, index) => (
                <Cell key={point.name} fill={SERIES[index % SERIES.length]} />
              ))}
            </Pie>
            <Tooltip />
            <Legend />
          </PieChart>
        ) : kind === "line" ? (
          <LineChart data={[...points]} accessibilityLayer={false}>
            <CartesianGrid strokeDasharray="3 3" stroke="var(--border)" />
            <XAxis dataKey="name" tick={{ fill: "var(--muted-foreground)", fontSize: 12 }} />
            <YAxis tick={{ fill: "var(--muted-foreground)", fontSize: 12 }} width={56} />
            <Tooltip />
            <Line
              type="monotone"
              dataKey="value"
              stroke="var(--primary)"
              strokeWidth={2}
              isAnimationActive={false}
            />
          </LineChart>
        ) : (
          <BarChart data={[...points]} accessibilityLayer={false}>
            <CartesianGrid strokeDasharray="3 3" stroke="var(--border)" />
            <XAxis dataKey="name" tick={{ fill: "var(--muted-foreground)", fontSize: 12 }} />
            <YAxis
              allowDecimals={false}
              tick={{ fill: "var(--muted-foreground)", fontSize: 12 }}
              width={40}
            />
            <Tooltip />
            <Bar dataKey="value" fill="var(--primary)" isAnimationActive={false} />
          </BarChart>
        )}
      </ResponsiveContainer>
    </div>
  );
}
