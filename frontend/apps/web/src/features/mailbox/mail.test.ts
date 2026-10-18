import { describe, expect, it } from "vitest";

import type { ConnectedAccount, MailFolder } from "./api";
import {
  fileSize,
  hasRemoteImages,
  mailAccounts,
  mailFrameDocument,
  proposedClientEmail,
  recipientsLabel,
  senderLabel,
} from "./mail";

const account = (id: string, input: Partial<ConnectedAccount> = {}): ConnectedAccount => ({
  id,
  provider: "imap",
  address: `${id}@pec.it`,
  displayName: null,
  color: "green",
  hasMail: true,
  hasCalendar: false,
  status: "Active",
  lastErrorCode: null,
  imapHost: "imaps.pec.aruba.it",
  imapPort: 993,
  connectedAt: "2026-10-01T08:00:00Z",
  mailSyncedAt: null,
  calendarSyncedAt: null,
  ...input,
});

const folder = (id: string, accountId: string, kind: "Inbox" | "Sent"): MailFolder => ({
  id,
  accountId,
  kind,
  name: kind,
  total: 3,
  unread: 1,
});

describe("mail page helpers", () => {
  it("wraps the HTML of a message for a sandboxed frame, links in a new tab without referrer", () => {
    const document = mailFrameDocument("<p>Ciao</p>");

    expect(document).toContain('<base target="_blank">');
    expect(document).toContain('<meta name="referrer" content="no-referrer">');
    expect(document).toContain("<p>Ciao</p>");
    expect(document.startsWith("<!doctype html>")).toBe(true);
  });

  it("tells when the HTML asks for images on the network", () => {
    expect(hasRemoteImages('<img src="https://tracker.example/p.gif">')).toBe(true);
    expect(hasRemoteImages("<IMG width=1 SRC=//cdn.example/x.png>")).toBe(true);
    expect(hasRemoteImages('<div style="background:url(http://x.example/b.png)">')).toBe(true);
    expect(hasRemoteImages('<img src="data:image/png;base64,AAAA">')).toBe(false);
    expect(hasRemoteImages('<img src="cid:logo">')).toBe(false);
    expect(hasRemoteImages(null)).toBe(false);
  });

  it("shows senders and recipients by name when they have one", () => {
    expect(senderLabel({ fromName: " Mario Rossi ", fromAddress: "mario@example.com" })).toBe(
      "Mario Rossi",
    );
    expect(senderLabel({ fromName: null, fromAddress: "mario@example.com" })).toBe(
      "mario@example.com",
    );
    expect(senderLabel({ fromName: null, fromAddress: null })).toBe("");
    expect(recipientsLabel(["Studio <studio@example.com>", "altro@example.com"])).toBe(
      "Studio, altro@example.com",
    );
  });

  it("keeps the accounts with mail, each with inbox before sent", () => {
    const grouped = mailAccounts(
      [account("a"), account("b", { hasMail: false, hasCalendar: true }), account("c")],
      [folder("s", "a", "Sent"), folder("i", "a", "Inbox")],
    );

    expect(grouped.map((item) => item.account.id)).toEqual(["a", "c"]);
    expect(grouped[0]?.folders.map((item) => item.id)).toEqual(["i", "s"]);
    expect(grouped[1]?.folders).toEqual([]);
    expect(mailAccounts(undefined, undefined)).toEqual([]);
  });

  it("proposes the sender as the client, not the owner of the mailbox", () => {
    expect(proposedClientEmail({ fromAddress: " Mario@Example.com " }, "studio@pec.it")).toBe(
      "mario@example.com",
    );
    expect(proposedClientEmail({ fromAddress: "studio@pec.it" }, "Studio@pec.it")).toBeUndefined();
    expect(proposedClientEmail({ fromAddress: null }, undefined)).toBeUndefined();
  });

  it("writes file sizes for people", () => {
    expect(fileSize(512, "it")).toBe("512 B");
    expect(fileSize(1536, "it")).toBe("1,5 kB");
    expect(fileSize(25 * 1024 * 1024, "en")).toBe("25 MB");
  });
});
