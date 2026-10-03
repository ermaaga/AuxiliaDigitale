import { describe, expect, it } from "vitest";

import { queryKey } from "@/lib/api/query-keys";

import { clientName, clientsKey } from "./api";
import { customFieldValues } from "./components/clients-table";
import {
  clientSchema,
  editClientSchema,
  newClientSchema,
  normalizeFiscalCode,
  personBody,
  stepOf,
  type ClientValues,
} from "./schemas/client";

const valid: ClientValues = {
  firstName: " Mario ",
  lastName: "Rossi",
  birthDate: "1980-01-01",
  email: "mario.rossi@example.test",
  phone: "",
  fiscalCode: "rss mra 80a01 h501u",
};

const errorsOf = (values: Record<string, unknown>) => {
  const result = clientSchema.safeParse(values);
  return result.success
    ? {}
    : Object.fromEntries(result.error.issues.map((issue) => [issue.path[0], issue.message]));
};

describe("client schema (mirrors Person and ClientRules)", () => {
  it("accepts a valid client, trims names and normalizes the fiscal code in the body", () => {
    const parsed = clientSchema.parse(valid);
    expect(parsed.firstName).toBe("Mario");
    expect(personBody(parsed)).toEqual({
      firstName: "Mario",
      lastName: "Rossi",
      birthDate: "1980-01-01",
      email: "mario.rossi@example.test",
      phone: null,
      fiscalCode: "RSSMRA80A01H501U",
    });
  });

  it("reports every invalid field with the API's translation keys", () => {
    expect(
      errorsOf({
        firstName: " ",
        lastName: "x".repeat(101),
        birthDate: "2999-01-01",
        email: "nope",
        phone: "12ab",
        fiscalCode: "RSSMRA80Z01H501U",
      }),
    ).toEqual({
      firstName: "validation.person.firstName",
      lastName: "validation.person.lastName",
      birthDate: "validation.person.birthDate",
      email: "validation.person.email",
      phone: "validation.person.phone",
      fiscalCode: "validation.person.fiscalCode",
    });
    expect(errorsOf({ ...valid, birthDate: "1899-12-31" })).toEqual({
      birthDate: "validation.person.birthDate",
    });
    expect(errorsOf({ ...valid, birthDate: "" })).toEqual({
      birthDate: "validation.person.birthDate",
    });
  });

  it("accepts the phone rule (Q53) and omocodia (Q54)", () => {
    expect(errorsOf({ ...valid, phone: "+39 (06) 123-4567" })).toEqual({});
    expect(errorsOf({ ...valid, phone: "1234567" })).toEqual({ phone: "validation.person.phone" });
    expect(errorsOf({ ...valid, fiscalCode: "RSSMRA80A01H50MU" })).toEqual({});
    expect(normalizeFiscalCode(" rss mra ")).toBe("RSSMRA");
  });

  it("checks the user name on edit and keeps the employee on create", () => {
    expect(editClientSchema.safeParse({ ...valid, userName: "mario rossi" }).success).toBe(false);
    expect(editClientSchema.safeParse({ ...valid, userName: "mario.rossi" }).success).toBe(true);
    expect(newClientSchema.parse({ ...valid, employeeUserId: "" }).employeeUserId).toBe("");
  });

  it("sends a server error back to the wizard step holding the field", () => {
    expect(stepOf("fiscalCode")).toBe(0);
    expect(stepOf("email")).toBe(1);
    expect(stepOf("employeeUserId")).toBe(1);
    expect(stepOf("customFields.vip")).toBe(1);
    expect(stepOf("unknown")).toBe(0);
  });
});

describe("client helpers", () => {
  it("reads custom field values only from a JSON object", () => {
    expect(customFieldValues({ vip: true })).toEqual({ vip: true });
    expect(customFieldValues([1, 2])).toEqual({});
    expect(customFieldValues(null)).toEqual({});
    expect(customFieldValues("x")).toEqual({});
  });

  it("names a client and nests every client query under one prefix", () => {
    expect(clientName({ firstName: "Mario", lastName: "Rossi" })).toBe("Mario Rossi");
    const prefix = clientsKey("demo");
    expect(prefix).toEqual(queryKey("demo", "directory", "clients"));
    for (const key of [
      clientsKey("demo", "list", { page: 1 }),
      clientsKey("demo", "detail", { id: "x" }),
      clientsKey("demo", "specializations"),
    ]) {
      expect(key.slice(0, prefix.length)).toEqual([...prefix]);
    }

    expect(clientsKey("demo", "detail", { id: "x" })).not.toEqual(
      clientsKey("demo", "detail", { id: "y" }),
    );
  });
});
