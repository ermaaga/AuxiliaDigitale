"use client";

import * as React from "react";
import { EyeIcon, EyeOffIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";

/**
 * The HTML of a rendered e-mail (written by staff): shown on request in an iframe sandboxed without scripts, so it never
 * runs in the page; the subject is always visible.
 */
export function EmailPreview({ subject, body }: { subject: string; body: string }) {
  const t = useTranslations();
  const [open, setOpen] = React.useState(false);
  return (
    <div className="flex flex-col gap-2">
      <p className="text-sm">
        <span className="text-muted-foreground">{t("Subject")}: </span>
        <span className="font-medium">{subject}</span>
      </p>
      <Button
        type="button"
        variant="outline"
        size="sm"
        className="self-start"
        aria-expanded={open}
        onClick={() => setOpen((current) => !current)}
      >
        {open ? <EyeOffIcon aria-hidden /> : <EyeIcon aria-hidden />}{" "}
        {t(open ? "app.marketing.templates.hidePreview" : "app.marketing.templates.showPreview")}
      </Button>
      {open ? (
        <iframe
          title={t("app.marketing.templates.preview")}
          sandbox=""
          srcDoc={body}
          className="h-80 w-full rounded-md border bg-white"
        />
      ) : null}
    </div>
  );
}
