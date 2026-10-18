import { describe, expect, it } from "vitest";

import { isOAuthProvider } from "./api";
import { ACCOUNT_COLORS, accountColorStyle, accountColorVar } from "./colors";
import { capabilitiesSchema, imapSchema, securityForPort } from "./schemas";

const imap = {
  address: "studio@pec.it",
  userName: "studio@pec.it",
  password: "secret",
  host: "imaps.pec.aruba.it",
  port: "993",
  security: "SslOnConnect",
};

describe("connected account helpers", () => {
  it("paints an account with its colour token, and only with the palette of the API", () => {
    expect(ACCOUNT_COLORS).toHaveLength(8);
    expect(accountColorVar("teal")).toBe("var(--account-teal)");
    expect(accountColorVar("red;background:url(x)")).toBe("var(--muted-foreground)");
    expect(accountColorStyle("blue")).toEqual({ backgroundColor: "var(--account-blue)" });
  });

  it("knows the OAuth providers and the usual connection type of a port", () => {
    expect(isOAuthProvider("google")).toBe(true);
    expect(isOAuthProvider("microsoft")).toBe(true);
    expect(isOAuthProvider("imap")).toBe(false);
    expect(securityForPort(993)).toBe("SslOnConnect");
    expect(securityForPort(143)).toBe("StartTls");
    expect(securityForPort(undefined)).toBe("StartTls");
  });
});

describe("connected account schemas", () => {
  it("accepts a PEC mailbox and turns the port into a number", () => {
    const parsed = imapSchema.safeParse(imap);
    expect(parsed.success).toBe(true);
    expect(parsed.data?.port).toBe(993);
  });

  it("refuses what the API would refuse before the connection test", () => {
    const errors = (input: object) =>
      imapSchema.safeParse({ ...imap, ...input }).error?.issues.map((issue) => issue.message);

    expect(errors({ address: "not-an-address" })).toEqual(["validation.mailbox.address"]);
    expect(errors({ host: "10.0.0.1" })).toEqual(["validation.mailbox.host"]);
    expect(errors({ host: "localhost" })).toEqual(["validation.mailbox.host"]);
    expect(errors({ port: "70000" })).toEqual(["validation.mailbox.port"]);
    expect(errors({ userName: " " })).toEqual(["validation.mailbox.userName"]);
    expect(errors({ password: "" })).toEqual(["validation.mailbox.password"]);
    expect(errors({ security: "None" })).toEqual(["validation.mailbox.port"]);
  });

  it("asks the provider for mail, calendar or both, never for nothing", () => {
    expect(capabilitiesSchema.safeParse({ mail: false, calendar: true }).success).toBe(true);
    expect(
      capabilitiesSchema.safeParse({ mail: false, calendar: false }).error?.issues[0]?.message,
    ).toBe("validation.mailbox.capabilities");
  });
});
