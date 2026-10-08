"use client";

import * as React from "react";
import { CheckIcon, CopyIcon, ExternalLinkIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";

import { QrCode } from "@/components/qr-code";

/** The setup key in groups of four, easier to type into an authenticator app. */
export function groupSecret(secret: string): string {
  return secret.replace(/(.{4})(?=.)/g, "$1 ");
}

/**
 * A new secret of an authenticator app (N02 System users, N04 tenant users): the QR code of the `otpauth://` URI, the
 * setup key to type by hand (copy button) and a link that opens the app on this device. Shown once, never stored.
 */
export function TotpEnrollment({ secret, uri }: { secret: string; uri: string }) {
  const t = useTranslations();
  const [copied, setCopied] = React.useState(false);

  async function copyKey() {
    try {
      await navigator.clipboard.writeText(secret);
      setCopied(true);
    } catch {
      // Clipboard not available: the key stays on screen to be typed.
    }
  }

  return (
    <>
      <p className="text-sm text-muted-foreground">{t("app.platform.activate.scanQr")}</p>
      <div className="flex justify-center">
        <QrCode value={uri} label={t("app.platform.activate.qrLabel")} />
      </div>
      <div className="flex flex-col gap-2 rounded-md border bg-muted/40 p-3">
        <p className="text-sm font-medium">{t("app.platform.activate.manual")}</p>
        <p className="text-sm text-muted-foreground">{t("app.platform.activate.manualKey")}</p>
        <code className="font-mono text-base break-all" data-testid="totp-secret">
          {groupSecret(secret)}
        </code>
        <div className="flex flex-wrap gap-2">
          <Button type="button" variant="outline" size="sm" onClick={() => void copyKey()}>
            {copied ? <CheckIcon aria-hidden /> : <CopyIcon aria-hidden />}
            {copied ? t("app.platform.activate.copied") : t("app.platform.activate.copyKey")}
          </Button>
          <Button type="button" variant="outline" size="sm" asChild>
            <a href={uri}>
              <ExternalLinkIcon aria-hidden /> {t("app.platform.activate.openApp")}
            </a>
          </Button>
        </div>
      </div>
    </>
  );
}
