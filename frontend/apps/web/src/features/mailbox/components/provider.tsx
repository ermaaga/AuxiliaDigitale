import { MailIcon } from "lucide-react";

type Translate = (key: string) => string;

/** The name of a provider of the API (`google`, `microsoft`, `imap`). */
export function providerLabel(t: Translate, provider: string): string {
  switch (provider) {
    case "google":
      return t("app.connectedAccounts.providers.google");
    case "microsoft":
      return t("app.connectedAccounts.providers.microsoft");
    default:
      return t("app.connectedAccounts.providers.imap");
  }
}

/** Decorative mark of a provider (the name is always written next to it; no third-party logos). */
export function ProviderMark({ provider }: { provider: string }) {
  const letter = provider === "google" ? "G" : provider === "microsoft" ? "M" : undefined;
  return (
    <span
      aria-hidden
      className="flex size-5 shrink-0 items-center justify-center rounded-sm border bg-muted text-xs font-semibold text-foreground"
    >
      {letter ?? <MailIcon className="size-4" />}
    </span>
  );
}
