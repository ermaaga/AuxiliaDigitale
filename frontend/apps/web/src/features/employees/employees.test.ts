import { describe, expect, it } from "vitest";

import { queryKey } from "@/lib/api/query-keys";

import { employeeName, employeesKey } from "./api";
import {
  editEmployeeSchema,
  employeeBody,
  employeeSchema,
  newEmployeeSchema,
  type EmployeeValues,
} from "./schemas/employee";

const valid: EmployeeValues = {
  firstName: " Paola ",
  lastName: "Neri",
  birthDate: "1985-03-04",
  email: "paola.neri@example.test",
  phone: "",
  fiscalCode: "",
};

const errorsOf = (values: Record<string, unknown>) => {
  const result = employeeSchema.safeParse(values);
  return result.success
    ? {}
    : Object.fromEntries(result.error.issues.map((issue) => [issue.path[0], issue.message]));
};

describe("employee schema (Q55)", () => {
  it("accepts an employee without phone and fiscal code, sent as null", () => {
    expect(employeeBody(employeeSchema.parse(valid))).toEqual({
      firstName: "Paola",
      lastName: "Neri",
      birthDate: "1985-03-04",
      email: "paola.neri@example.test",
      phone: null,
      fiscalCode: null,
    });
  });

  it("normalizes an optional fiscal code and checks it when given", () => {
    expect(
      employeeBody(employeeSchema.parse({ ...valid, fiscalCode: "nrepla85c44h501x" })).fiscalCode,
    ).toBe("NREPLA85C44H501X");
    expect(errorsOf({ ...valid, fiscalCode: "XYZ" })).toEqual({
      fiscalCode: "validation.person.fiscalCode",
    });
  });

  it("requires names, birth date and e-mail with the API's translation keys", () => {
    expect(
      errorsOf({
        ...valid,
        firstName: "",
        lastName: " ",
        birthDate: "",
        email: "nope",
        phone: "1",
      }),
    ).toEqual({
      firstName: "validation.person.firstName",
      lastName: "validation.person.lastName",
      birthDate: "validation.person.birthDate",
      email: "validation.person.email",
      phone: "validation.person.phone",
    });
  });

  it("keeps sign-in on create and checks the user name on edit", () => {
    expect(newEmployeeSchema.parse({ ...valid, canSignIn: false }).canSignIn).toBe(false);
    expect(editEmployeeSchema.safeParse({ ...valid, userName: "paola neri" }).success).toBe(false);
    expect(editEmployeeSchema.safeParse({ ...valid, userName: "paola.neri" }).success).toBe(true);
  });
});

describe("employee helpers", () => {
  it("names an employee and nests every employee query under one prefix", () => {
    expect(employeeName({ firstName: "Paola", lastName: "Neri" })).toBe("Paola Neri");
    const prefix = employeesKey("demo");
    expect(prefix).toEqual(queryKey("demo", "directory", "employees"));
    for (const key of [
      employeesKey("demo", "list", { page: 1 }),
      employeesKey("demo", "detail", { id: "x" }),
      employeesKey("demo", "specializations"),
      employeesKey("demo", "administrators"),
    ]) {
      expect(key.slice(0, prefix.length)).toEqual([...prefix]);
    }
  });
});
