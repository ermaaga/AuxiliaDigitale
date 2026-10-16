"use client";

import * as React from "react";
import Link from "next/link";
import { DownloadIcon, FolderInputIcon, ImageOffIcon, PaperclipIcon } from "lucide-react";
import { useFormatter, useLocale, useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import { Skeleton } from "@auxilia/ui/components/skeleton";

import { ApiErrorAlert } from "@/components/errors/api-error-alert";
import { tenantHref } from "@/lib/href";
import { useCan } from "@/lib/permissions";

import {
  FILE_MAIL_PERMISSION,
  mailAttachmentUrl,
  mailEmlUrl,
  useMailMessage,
  type ConnectedAccount,
} from "../api";
import { accountName } from "../calendar";
import { accountColorStyle } from "../colors";
import { fileSize, hasRemoteImages, mailFrameDocument, proposedClientEmail } from "../mail";
import { FileMailDialog } from "./file-mail-dialog";

/**
 * The reading pane (N05 #100): the message read live from the provider. The HTML body is shown only inside an iframe
 * sandboxed without scripts nor same origin (remote images stay blocked by the page's CSP, the reader says so), a text
 * body as text; attachments and the original `.eml` are downloads through the BFF; "File into case" for whoever may.
 */
export function MailReader({
  tenant,
  id,
  accounts,
}: {
  tenant: string;
  id: string;
  accounts: readonly ConnectedAccount[];
}) {
  const t = useTranslations();
  const format = useFormatter();
  const locale = useLocale();
  const canFile = useCan(FILE_MAIL_PERMISSION);
  const detail = useMailMessage(tenant, id);
  const [filing, setFiling] = React.useState(false);
  const headingRef = React.useRef<HTMLHeadingElement>(null);

  // A newly opened message takes the focus (keyboard and screen reader users land on it).
  React.useEffect(() => {
    if (detail.data?.message.id === id) {
      headingRef.current?.focus();
    }
  }, [detail.data?.message.id, id]);

  if (detail.isPending) {
    return (
      <div className="flex flex-col gap-3" aria-busy>
        <Skeleton className="h-8 w-2/3" />
        <Skeleton className="h-4 w-1/2" />
        <Skeleton className="h-72 w-full" />
      </div>
    );
  }

  if (detail.error) {
    return <ApiErrorAlert error={detail.error} onRetry={() => void detail.refetch()} />;
  }

  const { message, text, html, truncated, attachments, filedIn } = detail.data;
  const account = accounts.find((item) => item.id === message.accountId);
  const from = message.fromName
    ? `${message.fromName} <${message.fromAddress ?? ""}>`
    : (message.fromAddress ?? t("app.mail.unknownSender"));

  return (
    <article className="flex flex-col gap-4">
      <header className="flex flex-col gap-2">
        <h2
          ref={headingRef}
          tabIndex={-1}
          className="text-xl font-semibold break-words outline-none"
        >
          {message.subject || t("app.mail.noSubject")}
        </h2>
        <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-sm">
          <dt className="text-muted-foreground">{t("app.mail.from")}</dt>
          <dd className="break-all">{from}</dd>
          {message.to.length > 0 ? (
            <>
              <dt className="text-muted-foreground">{t("app.mail.toLabel")}</dt>
              <dd className="break-all">{message.to.join(", ")}</dd>
            </>
          ) : null}
          {message.cc.length > 0 ? (
            <>
              <dt className="text-muted-foreground">{t("app.mail.cc")}</dt>
              <dd className="break-all">{message.cc.join(", ")}</dd>
            </>
          ) : null}
          <dt className="text-muted-foreground">{t("app.mail.date")}</dt>
          <dd>
            <time dateTime={message.receivedAt}>
              {format.dateTime(new Date(message.receivedAt), {
                dateStyle: "full",
                timeStyle: "short",
              })}
            </time>
          </dd>
          {account ? (
            <>
              <dt className="text-muted-foreground">{t("app.mail.account")}</dt>
              <dd className="flex items-center gap-2">
                <span
                  aria-hidden
                  className="size-2.5 shrink-0 rounded-full"
                  style={accountColorStyle(account.color)}
                />
                <span className="break-all">{accountName(account)}</span>
              </dd>
            </>
          ) : null}
        </dl>
        <div className="flex flex-wrap gap-2">
          <Button asChild variant="outline" size="sm">
            <a href={mailEmlUrl(message.id)} download>
              <DownloadIcon aria-hidden /> {t("app.mail.downloadEml")}
            </a>
          </Button>
          {canFile ? (
            <Button type="button" size="sm" onClick={() => setFiling(true)}>
              <FolderInputIcon aria-hidden /> {t("app.mail.file")}
            </Button>
          ) : null}
        </div>
        {filedIn.length > 0 ? (
          <p className="text-sm text-muted-foreground">
            {t("app.mail.filedIn")}{" "}
            {filedIn.map((filed, index) => (
              <React.Fragment key={filed.caseId}>
                {index > 0 ? ", " : null}
                <Link
                  className="underline underline-offset-4"
                  href={tenantHref(tenant, `/cases/${filed.caseId}`)}
                >
                  {filed.caseNumber}
                </Link>
              </React.Fragment>
            ))}
          </p>
        ) : null}
      </header>

      {attachments.length > 0 ? (
        <section aria-label={t("app.mail.attachments")} className="flex flex-col gap-1">
          <h3 className="text-sm font-medium">{t("app.mail.attachments")}</h3>
          <ul className="flex flex-wrap gap-2">
            {attachments.map((attachment) => (
              <li key={attachment.index}>
                <Button asChild variant="outline" size="sm">
                  <a
                    href={mailAttachmentUrl(message.id, Number(attachment.index))}
                    download={attachment.fileName}
                  >
                    <PaperclipIcon aria-hidden />
                    <span className="max-w-56 truncate">{attachment.fileName}</span>
                    <span className="text-muted-foreground">
                      {fileSize(Number(attachment.size), locale)}
                    </span>
                  </a>
                </Button>
              </li>
            ))}
          </ul>
        </section>
      ) : null}

      {truncated ? (
        <p className="text-sm text-muted-foreground">{t("app.mail.truncated")}</p>
      ) : null}
      {html ? (
        <>
          {hasRemoteImages(html) ? (
            <p className="flex items-center gap-2 text-sm text-muted-foreground">
              <ImageOffIcon aria-hidden className="size-4" /> {t("app.mail.imagesBlocked")}
            </p>
          ) : null}
          <iframe
            title={t("app.mail.body")}
            sandbox="allow-popups allow-popups-to-escape-sandbox"
            referrerPolicy="no-referrer"
            srcDoc={mailFrameDocument(html)}
            className="h-[32rem] w-full rounded-md border bg-white"
          />
        </>
      ) : text ? (
        <pre className="rounded-md border p-3 text-sm whitespace-pre-wrap break-words font-sans">
          {text}
        </pre>
      ) : (
        <p className="text-sm text-muted-foreground">{t("app.mail.noBody")}</p>
      )}

      {filing ? (
        <FileMailDialog
          tenant={tenant}
          message={message}
          attachments={attachments}
          proposedEmail={proposedClientEmail(message, account?.address)}
          open
          onOpenChange={setFiling}
        />
      ) : null}
    </article>
  );
}
