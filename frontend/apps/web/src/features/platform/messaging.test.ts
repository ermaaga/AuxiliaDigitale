import { describe, expect, it } from "vitest";

import { messagingKey, smtpSettingsOf, type MessagingAccount } from "./messaging-api";
import { tenantKey } from "./tenant-api";
import { rulesRequest, smtpAccountSchema, testMessageSchema } from "./schemas/messaging";

const valid = {
  name: "Ufficio",
  host: "smtp.example.test",
  port: "587",
  security: "StartTls",
  username: "office",
  password: "",
  fromAddress: "office@example.test",
  fromName: "Ufficio",
};

describe("SMTP account form", () => {
  it("accepts a complete account and an empty password (the stored one is kept)", () => {
    expect(smtpAccountSchema.safeParse(valid).success).toBe(true);
  });

  it("refuses missing host, invalid port and sender address with translation keys", () => {
    const result = smtpAccountSchema.safeParse({
      ...valid,
      host: " ",
      port: "70000",
      fromAddress: "nobody",
    });
    expect(result.error?.issues.map((issue) => [issue.path[0], issue.message])).toEqual([
      ["host", "validation.messaging.host"],
      ["port", "validation.messaging.port"],
      ["fromAddress", "validation.email"],
    ]);
    expect(smtpAccountSchema.safeParse({ ...valid, port: "25a" }).success).toBe(false);
    expect(smtpAccountSchema.safeParse({ ...valid, port: "0" }).success).toBe(false);
  });

  it("reads the settings of an account with defaults for what is missing", () => {
    expect(smtpSettingsOf(undefined)).toEqual({
      host: "",
      port: 587,
      security: "StartTls",
      username: null,
      fromAddress: "",
      fromName: null,
    });
    const account = {
      settings: { host: "mail.test", port: 465, security: "SslOnConnect", fromAddress: "a@b.test" },
    } as unknown as MessagingAccount;
    expect(smtpSettingsOf(account)).toMatchObject({
      host: "mail.test",
      port: 465,
      security: "SslOnConnect",
    });
  });

  it("needs a valid recipient for a test", () => {
    expect(
      testMessageSchema.safeParse({ recipient: "anna@example.test", language: "it" }).success,
    ).toBe(true);
    expect(testMessageSchema.safeParse({ recipient: "anna", language: "it" }).success).toBe(false);
    expect(
      testMessageSchema.safeParse({ recipient: "anna@example.test", language: "de" }).success,
    ).toBe(false);
  });
});

describe("sender rules", () => {
  it("turns rows into the request: any role is null, priorities are whole and not negative", () => {
    expect(
      rulesRequest([
        { key: "1", purpose: "Marketing", role: "*", accountId: "a", priority: 2.7 },
        { key: "2", purpose: "Notification", role: "Employee", accountId: "b", priority: -3 },
        {
          key: "3",
          purpose: "Transactional",
          role: "Client",
          accountId: "c",
          priority: Number.NaN,
        },
      ]),
    ).toEqual([
      { purpose: "Marketing", role: null, accountId: "a", priority: 2 },
      { purpose: "Notification", role: "Employee", accountId: "b", priority: 0 },
      { purpose: "Transactional", role: "Client", accountId: "c", priority: 0 },
    ]);
  });
});

describe("query keys", () => {
  it("nests every messaging query under the prefix the mutations invalidate", () => {
    const prefix = tenantKey("demo", "messaging");
    for (const key of [
      messagingKey("demo", "accounts"),
      messagingKey("demo", "rules", { channel: "Email" }),
      messagingKey("demo", "outbound", { page: 2 }),
    ]) {
      expect(key.slice(0, prefix.length)).toEqual([...prefix]);
    }
  });
});
