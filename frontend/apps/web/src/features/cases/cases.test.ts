import { describe, expect, it } from "vitest";

import { queryKey } from "@/lib/api/query-keys";

import { casesKey, formatMoney } from "./api";
import { completeCaseSchema, newCaseSchema, paymentSchema, today } from "./schemas/case";

describe("case schemas (mirror the API limits)", () => {
  it("accepts amounts with a comma or a dot and at most two decimals", () => {
    expect(
      completeCaseSchema.parse({ amountPaid: "150,5", rejected: false, note: "" }).amountPaid,
    ).toBe("150.5");
    expect(
      completeCaseSchema.safeParse({ amountPaid: "0", rejected: true, note: "" }).success,
    ).toBe(true);
    expect(
      completeCaseSchema.safeParse({ amountPaid: "1.234", rejected: false, note: "" }).error
        ?.issues[0]?.message,
    ).toBe("validation.cases.amount");
    expect(
      paymentSchema.safeParse({ amount: "0", paidOn: "2026-10-01", note: "" }).error?.issues[0]
        ?.message,
    ).toBe("validation.cases.amount");
    expect(
      paymentSchema
        .safeParse({ amount: "10", paidOn: "", note: "x".repeat(501) })
        .error?.issues.map((issue) => issue.path[0]),
    ).toEqual(["paidOn", "note"]);
  });

  it("requires client, service and start, and a due date not before the start", () => {
    const base = {
      clientId: "c",
      serviceId: "s",
      startedOn: "2026-10-03",
      dueOn: "",
      employeeUserId: "",
    };
    expect(newCaseSchema.safeParse(base).success).toBe(true);
    expect(
      newCaseSchema.safeParse({ ...base, dueOn: "2026-10-01" }).error?.issues[0],
    ).toMatchObject({
      path: ["dueOn"],
      message: "validation.cases.dueOn",
    });
    expect(
      newCaseSchema
        .safeParse({ ...base, clientId: "", serviceId: "" })
        .error?.issues.map((issue) => issue.message),
    ).toEqual(["validation.cases.client", "validation.cases.service"]);
  });
});

describe("case helpers", () => {
  it("formats money in the page language and builds the query keys", () => {
    // Intl puts a no-break space before the euro sign.
    expect(formatMoney(150.5, "EUR", "it").replace(/\s/g, " ")).toBe("150,50 €");
    expect(formatMoney("10", "EUR", "en")).toBe("€10.00");
    expect(casesKey("demo")).toEqual(queryKey("demo", "cases", "cases"));
  });

  it("gives today as a date input value", () => {
    expect(today(new Date(2026, 9, 3, 23, 30))).toBe("2026-10-03");
  });
});
