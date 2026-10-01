import { ApiError } from "@auxilia/api-client";
import { describe, expect, it, vi } from "vitest";

import { applyApiErrors } from "@/components/forms/form";
import {
  applyLayout,
  customFieldColumns,
  type CustomFieldDefinition,
  nextSort,
  pageCount,
  pageLocally,
  sortDirection,
  type DataTableColumn,
} from "./table-model";

type Row = { name: string; custom: Record<string, string> };

const column = (id: string): DataTableColumn<Row> => ({ id, header: id, cell: (row) => row.name });

describe("server-side table model", () => {
  it("cycles a header sort through ascending, descending and none", () => {
    expect(nextSort(null, "name")).toBe("name");
    expect(nextSort("name", "name")).toBe("-name");
    expect(nextSort("-name", "name")).toBeNull();
    expect(nextSort("-date", "name")).toBe("name");
    expect(sortDirection("name", "name")).toBe("asc");
    expect(sortDirection("-name", "name")).toBe("desc");
    expect(sortDirection("-date", "name")).toBeUndefined();
  });

  it("counts at least one page", () => {
    expect(pageCount(0, 25)).toBe(1);
    expect(pageCount(25, 25)).toBe(1);
    expect(pageCount(26, 25)).toBe(2);
    expect(pageCount(10, 0)).toBe(10);
  });

  it("orders and hides columns by the role grid layout, unknown columns last", () => {
    const columns = ["a", "b", "c", "d"].map(column);
    expect(applyLayout(columns, undefined).columns.map((c) => c.id)).toEqual(["a", "b", "c", "d"]);

    const { columns: ordered, hidden } = applyLayout(columns, [
      { key: "c", visible: true, order: 1 },
      { key: "a", visible: false, order: 2 },
      { key: "b", visible: true, order: 3 },
    ]);
    expect(ordered.map((c) => c.id)).toEqual(["c", "a", "b", "d"]);
    expect(hidden).toEqual(["a"]);
  });

  it("adds the custom fields visible on grid, in their order, one column per group", () => {
    const field = (key: string, order: number, extra: Partial<CustomFieldDefinition> = {}) => ({
      key,
      label: key.toUpperCase(),
      type: "Boolean",
      visibleOnGrid: true,
      order,
      ...extra,
    });
    const columns = customFieldColumns<Row>(
      [
        field("size", 3, { type: "Text" }),
        field("secret", 0, { visibleOnGrid: false }),
        field("caf", 1, { groupName: "Area" }),
        field("patronato", 2, { groupName: "Area" }),
      ],
      (row, fields) => fields.map((item) => row.custom[item.key]).join("|"),
    );
    expect(columns.map((c) => [c.id, c.header])).toEqual([
      ["cfg:Area", "Area"],
      ["cf:size", "SIZE"],
    ]);
    expect(columns[0]!.cell({ name: "x", custom: { caf: "yes", patronato: "no" } })).toBe("yes|no");
    expect(columns[1]!.cell({ name: "x", custom: { size: "XL" } })).toBe("XL");
  });
});

describe("local paging", () => {
  const rows = ["b10", "a", "B2", "c"].map((name) => ({ name }));
  const options = { page: 1, pageSize: 2, sortValue: (row: { name: string }) => row.name };

  it("sorts like the API (natural, case-insensitive) and pages", () => {
    expect(pageLocally(rows, { ...options, sort: "name" })).toEqual({
      rows: [{ name: "a" }, { name: "B2" }],
      totalCount: 4,
      page: 1,
    });
    expect(pageLocally(rows, { ...options, sort: "-name", page: 2 }).rows).toEqual([
      { name: "B2" },
      { name: "a" },
    ]);
    expect(pageLocally(rows, { ...options, sort: null }).rows).toEqual([
      { name: "b10" },
      { name: "a" },
    ]);
  });

  it("shows the last page when the page is past the end (e.g. after filtering)", () => {
    expect(pageLocally(rows, { ...options, sort: "name", page: 9 })).toEqual({
      rows: [{ name: "b10" }, { name: "c" }],
      totalCount: 4,
      page: 2,
    });
    expect(pageLocally([], { ...options, sort: null })).toEqual({
      rows: [],
      totalCount: 0,
      page: 1,
    });
  });
});

describe("form kit", () => {
  it("puts API validation errors on the known fields only", () => {
    const setError = vi.fn();
    const error = new ApiError(400, {
      errorCode: "validation",
      errors: { email: ["validation.email"], other: ["validation.required"] },
    });

    expect(applyApiErrors<{ email: string }>(error, setError, ["email"])).toBe(true);
    expect(setError).toHaveBeenCalledExactlyOnceWith("email", {
      type: "server",
      message: "validation.email",
    });
    expect(applyApiErrors(new Error("boom"), setError, [])).toBe(false);
    expect(applyApiErrors(new ApiError(409, undefined), setError, [])).toBe(false);
  });
});
