import type { ConnectedAccount, MailFolder, MailMessage } from "./api";

/** Messages of a page of the list. */
export const MAIL_PAGE_SIZE = 25;

/**
 * The document of a message's HTML for a sandboxed `iframe` (`srcdoc`, no scripts, no same origin). The frame inherits
 * the page's CSP, so remote images, scripts, fonts and connections stay blocked; links open in a new tab without a
 * referrer. The HTML is third-party content and is never inserted in the page itself.
 */
export function mailFrameDocument(html: string): string {
  return [
    "<!doctype html>",
    '<html><head><meta charset="utf-8">',
    '<meta name="referrer" content="no-referrer">',
    '<base target="_blank">',
    "<style>",
    "html{color-scheme:light}body{margin:12px;font:14px/1.5 system-ui,sans-serif;color:#111;background:#fff;overflow-wrap:anywhere}",
    "img{max-width:100%;height:auto}table{max-width:100%}pre{white-space:pre-wrap}",
    "</style></head><body>",
    html,
    "</body></html>",
  ].join("");
}

/** Whether the HTML asks for images on the network (blocked: the reader says so). */
export function hasRemoteImages(html: string | null | undefined): boolean {
  return (
    !!html &&
    (/<img\b[^>]*\bsrc\s*=\s*["']?\s*(?:https?:)?\/\//i.test(html) ||
      /url\(\s*["']?\s*(?:https?:)?\/\//i.test(html))
  );
}

/** The sender as shown in the list: the name, otherwise the address. */
export function senderLabel(message: Pick<MailMessage, "fromName" | "fromAddress">): string {
  return message.fromName?.trim() || message.fromAddress || "";
}

/** The recipients of a sent message as shown in the list (names only when present). */
export function recipientsLabel(recipients: readonly string[]): string {
  return recipients
    .map((recipient) => recipient.replace(/\s*<[^>]*>\s*$/, "").trim() || recipient)
    .join(", ");
}

/** The accounts with mail, each with its folders in order (inbox, sent). */
export function mailAccounts(
  accounts: readonly ConnectedAccount[] | undefined,
  folders: readonly MailFolder[] | undefined,
) {
  return (accounts ?? [])
    .filter((account) => account.hasMail)
    .map((account) => ({
      account,
      folders: (folders ?? [])
        .filter((folder) => folder.accountId === account.id)
        .sort((a, b) => (a.kind === b.kind ? 0 : a.kind === "Inbox" ? -1 : 1)),
    }));
}

/** The address the sender proposed for "File into case" is looked up with: the sender of a received message. */
export function proposedClientEmail(
  message: Pick<MailMessage, "fromAddress">,
  accountAddress: string | undefined,
): string | undefined {
  const from = message.fromAddress?.trim().toLowerCase();
  return from && from !== accountAddress?.toLowerCase() ? from : undefined;
}

/** File size for people (kB/MB, base 1024). */
export function fileSize(bytes: number, locale: string): string {
  const format = (value: number, unit: string) =>
    `${new Intl.NumberFormat(locale, { maximumFractionDigits: value < 10 ? 1 : 0 }).format(value)} ${unit}`;
  if (bytes < 1024) {
    return `${bytes} B`;
  }

  return bytes < 1024 * 1024 ? format(bytes / 1024, "kB") : format(bytes / (1024 * 1024), "MB");
}
