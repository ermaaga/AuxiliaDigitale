"use client";

import { useQuery } from "@tanstack/react-query";
import { useTranslations } from "next-intl";
import { unwrap } from "@auxilia/api-client";
import { Badge } from "@auxilia/ui/components/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@auxilia/ui/components/card";
import { Skeleton } from "@auxilia/ui/components/skeleton";

import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { createBffClient } from "@/lib/api/client";
import { queryKey } from "@/lib/api/query-keys";

/**
 * Languages of a tenant with their translation progress (`GET /localization/languages`, a technical endpoint): the
 * console BFF calls it with a tenant-scoped platform token (D-21), never with the console token.
 */
export function TenantLanguages({ tenant }: { tenant: string }) {
  const t = useTranslations();
  const query = useQuery({
    queryKey: queryKey(tenant, "localization", "languages"),
    queryFn: async () =>
      unwrap(await createBffClient("platform", { tenant }).GET("/api/v1/localization/languages")),
  });

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2 className="text-base font-semibold">{t("app.platform.tenant.languages")}</h2>
        </CardTitle>
      </CardHeader>
      <CardContent>
        {query.error ? (
          <ApiErrorAlert error={query.error} onRetry={() => void query.refetch()} />
        ) : query.isPending ? (
          <div className="flex flex-col gap-2" aria-busy="true">
            <Skeleton className="h-5 w-2/3" />
            <Skeleton className="h-5 w-1/2" />
          </div>
        ) : (
          <ul className="flex flex-col divide-y">
            {query.data.map((language) => (
              <li
                key={language.code}
                className="flex flex-wrap items-center justify-between gap-2 py-2"
              >
                <span className="flex items-center gap-2">
                  <span className="font-medium">{language.name}</span>
                  <code className="text-xs text-muted-foreground">{language.code}</code>
                  {language.isDefault ? (
                    <Badge variant="secondary">{t("app.platform.tenant.default")}</Badge>
                  ) : null}
                  {language.isActive ? null : (
                    <Badge variant="outline">{t("app.platform.tenant.inactive")}</Badge>
                  )}
                </span>
                <span className="text-sm text-muted-foreground">
                  {t("app.platform.tenant.missing", { count: Number(language.missingCount) })}
                </span>
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  );
}
