"use client";

import { useFormatter, useTranslations } from "next-intl";
import { Card, CardContent, CardHeader, CardTitle } from "@auxilia/ui/components/card";

import { CustomFieldCell } from "@/components/custom-fields/custom-field-value";

import { useClientCustomFields, type ClientDetail } from "../api";
import { customFieldValues } from "./clients-table";

/** The client at a glance (F05): contacts, status since, employee, specializations and the custom fields (F20). */
export function ClientOverview({ tenant, client }: { tenant: string; client: ClientDetail }) {
  const t = useTranslations();
  const format = useFormatter();
  const customFields = useClientCustomFields(tenant);
  const values = customFieldValues(client.customFields);
  const rows: [string, string][] = [
    [t("Email"), client.email ?? "—"],
    [t("Phone"), client.phone ?? "—"],
    [
      t("BirthDate"),
      client.birthDate
        ? format.dateTime(new Date(client.birthDate), { dateStyle: "long", timeZone: "UTC" })
        : "—",
    ],
    [t("app.clients.fiscalCode"), client.fiscalCode ?? "—"],
    [t("Username"), client.account?.userName ?? "—"],
    [
      t("app.clients.statusSince"),
      format.dateTime(new Date(client.statusChangedAt), { dateStyle: "medium" }),
    ],
    [t("app.clients.employee"), client.employee?.fullName ?? t("app.clients.noEmployee")],
    [
      t("app.clients.tabs.specializations"),
      client.specializations.map((item) => item.name).join(", ") || "—",
    ],
  ];

  return (
    <div className="grid gap-4 lg:grid-cols-2">
      <Card>
        <CardHeader>
          <CardTitle>
            <h2 className="text-base font-semibold">{t("Details")}</h2>
          </CardTitle>
        </CardHeader>
        <CardContent>
          <dl className="grid gap-3 text-sm sm:grid-cols-2">
            {rows.map(([label, value]) => (
              <div key={label}>
                <dt className="text-muted-foreground">{label}</dt>
                <dd className="font-medium break-words">{value}</dd>
              </div>
            ))}
          </dl>
        </CardContent>
      </Card>
      {customFields.definitions.length > 0 ? (
        <Card>
          <CardHeader>
            <CardTitle>
              <h2 className="text-base font-semibold">{t("CustomFields")}</h2>
            </CardTitle>
          </CardHeader>
          <CardContent>
            <dl className="grid gap-3 text-sm sm:grid-cols-2">
              {[...customFields.definitions]
                .sort((a, b) => a.order - b.order)
                .map((definition) => (
                  <div key={definition.key}>
                    <dt className="text-muted-foreground">{definition.label}</dt>
                    <dd className="font-medium">
                      <CustomFieldCell
                        fields={[{ ...definition, groupName: null }]}
                        values={values}
                      />
                    </dd>
                  </div>
                ))}
            </dl>
          </CardContent>
        </Card>
      ) : null}
    </div>
  );
}
