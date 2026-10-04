import { describe, expect, it } from "vitest";

import { pointLabel } from "../dashboard/api";
import { exportQuery, fileNameOf } from "./api";

describe("exports", () => {
  it("send the list's parameters without paging, plus format, columns, ids and language", () => {
    const query = exportQuery(
      {
        view: "all",
        page: 3,
        pageSize: 25,
        sort: "-lastName",
        "filter[status]": "Active",
        "filter[email]": undefined,
        showAll: false,
      },
      "pdf",
      ["lastName", "status"],
      "it",
      ["a", "b"],
    );
    expect(Object.fromEntries(query)).toEqual({
      view: "all",
      sort: "-lastName",
      "filter[status]": "Active",
      showAll: "false",
      format: "pdf",
      columns: "lastName,status",
      ids: "a,b",
      language: "it",
    });
    expect(exportQuery({}, "csv", [], "en").has("columns")).toBe(false);
  });

  it("read the file name of the download", () => {
    expect(
      fileNameOf(
        "attachment; filename=Clienti_20261004_101500.csv; filename*=UTF-8''Clienti_20261004_101500.csv",
        "x",
      ),
    ).toBe("Clienti_20261004_101500.csv");
    expect(fileNameOf('attachment; filename="Servizi.xlsx"', "x")).toBe("Servizi.xlsx");
    expect(fileNameOf(null, "fallback.pdf")).toBe("fallback.pdf");
  });
});

describe("dashboard points", () => {
  const translate = (key: string) => `t:${key}`;
  const month = (date: Date) => `m:${date.getMonth() + 1}`;
  const day = (date: Date) => `d:${date.getDate()}`;

  it("translate statuses and format months and days", () => {
    expect(pointLabel({ label: "Pending", labelKey: "Pending" }, translate, month, day)).toBe(
      "t:Pending",
    );
    expect(pointLabel({ label: "2026-10" }, translate, month, day)).toBe("m:10");
    expect(pointLabel({ label: "2026-10-04" }, translate, month, day)).toBe("d:4");
    expect(pointLabel({ label: "730" }, translate, month, day)).toBe("730");
  });
});
