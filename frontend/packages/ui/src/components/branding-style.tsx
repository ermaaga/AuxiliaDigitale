import { brandingStyleSheet, type TenantBranding } from "../lib/branding";

/**
 * Overrides the brand tokens with the tenant's colours (render it in the tenant layout, after the global styles).
 * The CSS is generated from parsed colours only, so tenant input can never inject markup or rules.
 */
function BrandingStyle({
  branding,
  nonce,
}: {
  branding: TenantBranding | null | undefined;
  nonce?: string;
}) {
  return (
    <style
      id="tenant-branding"
      nonce={nonce}
      // Safe: brandingStyleSheet emits only token names and hex colours it computed itself.
      dangerouslySetInnerHTML={{ __html: brandingStyleSheet(branding) }}
    />
  );
}

export { BrandingStyle };
