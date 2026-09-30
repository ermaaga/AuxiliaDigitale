"use client";

import { useQuery } from "@tanstack/react-query";
import { unwrap } from "@auxilia/api-client";
import { useTranslations } from "next-intl";

import { createBffClient } from "@/lib/api/client";
import { queryKey } from "@/lib/api/query-keys";

/** The tenant's password rules (`GET /auth/password-policy`, F35) shown next to a new-password field. */
export function PasswordPolicy({ tenant, id }: { tenant: string; id: string }) {
  const t = useTranslations("app.auth.policy");
  const { data } = useQuery({
    queryKey: queryKey(tenant, "identity", "password-policy"),
    queryFn: async () =>
      unwrap(await createBffClient("tenant", { tenant }).GET("/api/v1/auth/password-policy")),
    staleTime: 5 * 60_000,
  });

  if (!data) {
    return null;
  }

  const rules = [
    t("minLength", { count: Number(data.minLength) }),
    ...(data.requireUppercase ? [t("uppercase")] : []),
    ...(data.requireLowercase ? [t("lowercase")] : []),
    ...(data.requireDigit ? [t("digit")] : []),
    ...(data.requireSpecial ? [t("special")] : []),
    ...(Number(data.historyCount) > 0 ? [t("history", { count: Number(data.historyCount) })] : []),
  ];

  return (
    <div id={id} className="text-sm text-muted-foreground">
      <p>{t("title")}</p>
      <ul className="list-disc pl-5">
        {rules.map((rule) => (
          <li key={rule}>{rule}</li>
        ))}
      </ul>
    </div>
  );
}
