import { requireNavigation } from "@/lib/api/navigation-guard";

/** `/{tenant}/mail/…` opens only for the roles whose menu has `mail` (module `mailbox`, N05). */
export default async function MailLayout({ children }: LayoutProps<"/[tenant]/mail">) {
  await requireNavigation("mail");
  return children;
}
