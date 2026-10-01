"use client";

import { useFormatter, useTranslations } from "next-intl";
import { Card, CardContent, CardHeader, CardTitle } from "@auxilia/ui/components/card";
import { Label } from "@auxilia/ui/components/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@auxilia/ui/components/select";

import { useNotify } from "@/lib/notify";

import { nameOf } from "../../labels";
import { changePlan, usePlans, useTenantMutation, type TenantDetail } from "../../tenant-api";

/** The plan of the tenant (modules per role); a change starts now and ends the current period. */
export function TenantPlanCard({ tenant, readOnly }: { tenant: TenantDetail; readOnly: boolean }) {
  const t = useTranslations();
  const format = useFormatter();
  const notify = useNotify();
  const plans = usePlans();
  const change = useTenantMutation(tenant.slug, (code: string) => changePlan(tenant.slug, code));

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2 className="text-base font-semibold">{t("app.platform.tenants.plan")}</h2>
        </CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        {tenant.plan ? (
          <p className="text-sm">
            <span className="font-medium">{nameOf(t, tenant.plan.nameKey, tenant.plan.code)}</span>{" "}
            <span className="text-muted-foreground">
              ·{" "}
              {t("app.platform.plan.since", {
                date: format.dateTime(new Date(tenant.plan.validFrom), { dateStyle: "medium" }),
              })}
            </span>
          </p>
        ) : (
          <p className="text-sm text-muted-foreground">—</p>
        )}
        {!readOnly && (plans.data?.length ?? 0) > 1 ? (
          <div className="flex flex-col gap-1">
            <Label htmlFor="tenant-plan">{t("app.platform.plan.change")}</Label>
            <Select
              value={tenant.plan?.code}
              disabled={change.isPending}
              onValueChange={(code) =>
                change.mutate(code, {
                  onSuccess: () => notify.success("app.platform.plan.changed"),
                  onError: (error) => notify.error(error),
                })
              }
            >
              <SelectTrigger id="tenant-plan" className="w-full sm:w-64">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {plans.data!.map((plan) => (
                  <SelectItem key={plan.code} value={plan.code}>
                    {nameOf(t, plan.nameKey, plan.code)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        ) : null}
      </CardContent>
    </Card>
  );
}
