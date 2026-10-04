"use client";

import { useFormatter, useTranslations } from "next-intl";
import { Badge } from "@auxilia/ui/components/badge";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@auxilia/ui/components/card";
import { Checkbox } from "@auxilia/ui/components/checkbox";
import { Label } from "@auxilia/ui/components/label";

import { useNotify } from "@/lib/notify";

import { setChecklistItem, useCaseMutation, type CaseDetail } from "../api";

/** Required items not ticked yet ("missing", B-26). */
export function missingItems(value: Pick<CaseDetail, "checklist">) {
  return value.checklist.filter((item) => item.required && !item.checkedAt);
}

/**
 * The document checklist of the case (B-26, skill auxilia-ui-design "document checklist with missing highlights"):
 * the items of its service, ticked by staff when the case has them; required ones not ticked are marked missing.
 */
export function CaseChecklist({ tenant, value }: { tenant: string; value: CaseDetail }) {
  const t = useTranslations();
  const format = useFormatter();
  const notify = useNotify();
  const mark = useCaseMutation(tenant, ({ itemId, done }: { itemId: string; done: boolean }) =>
    setChecklistItem(value.id, itemId, done),
  );
  if (value.checklist.length === 0) {
    return null;
  }

  const editable = value.canManage && value.status !== "Completed";
  const missing = missingItems(value).length;
  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2 className="text-base font-semibold">{t("app.cases.checklist.title")}</h2>
        </CardTitle>
        <CardDescription role="status">
          {missing > 0
            ? t("app.cases.checklist.missing", { count: missing })
            : t("app.cases.checklist.complete")}
        </CardDescription>
      </CardHeader>
      <CardContent>
        <ul className="flex flex-col gap-2">
          {value.checklist.map((item) => (
            <li key={item.itemId} className="flex flex-wrap items-center justify-between gap-2">
              <span className="flex items-center gap-2">
                <Checkbox
                  id={`checklist-${item.itemId}`}
                  checked={Boolean(item.checkedAt)}
                  disabled={!editable || mark.isPending}
                  onCheckedChange={(checked) =>
                    void mark
                      .mutateAsync({ itemId: item.itemId, done: checked === true })
                      .catch((error: unknown) => notify.error(error))
                  }
                />
                <Label htmlFor={`checklist-${item.itemId}`} className="font-normal">
                  {item.name}
                </Label>
                {item.required && !item.checkedAt ? (
                  <Badge variant="destructive">{t("app.cases.checklist.missingBadge")}</Badge>
                ) : null}
              </span>
              {item.checkedAt ? (
                <span className="text-xs text-muted-foreground">
                  {t("app.cases.checklist.checkedBy", {
                    name: item.checkedBy?.fullName ?? "—",
                    date: format.dateTime(new Date(item.checkedAt), { dateStyle: "short" }),
                  })}
                </span>
              ) : null}
            </li>
          ))}
        </ul>
      </CardContent>
    </Card>
  );
}
