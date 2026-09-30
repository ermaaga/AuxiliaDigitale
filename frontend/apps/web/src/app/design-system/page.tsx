import { notFound } from "next/navigation";
import { BrandingStyle } from "@auxilia/ui/components/branding-style";

import { Showcase } from "./showcase";

// Internal preview of the design system for developers and PR screenshots (skill auxilia-ui-design): tokens,
// components, light/dark and a tenant brand from `?primary=%23e91e63&accent=%23ff9800`. Not served in production,
// so its texts are not localized.
export default async function DesignSystemPage(props: PageProps<"/design-system">) {
  if (process.env.NODE_ENV === "production") {
    notFound();
  }

  const query = await props.searchParams;
  const value = (name: string) => (typeof query[name] === "string" ? query[name] : undefined);
  const primary = value("primary");
  const accent = value("accent");

  return (
    <>
      {primary || accent ? (
        <BrandingStyle branding={{ primaryColor: primary, accentColor: accent }} />
      ) : null}
      <Showcase />
    </>
  );
}
