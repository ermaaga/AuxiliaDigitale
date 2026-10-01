/*
 * Tenant branding as design tokens (skill auxilia-ui-design, F23). The tenant chooses a primary and an accent colour
 * (legacy "theme": primary/secondary, gradient or solid); the UI derives every token from them and never shows text
 * below WCAG 2.2 AA: if a brand colour has less than 4.5:1 against its text, an accessible shade of it is used.
 */

export type Rgb = { r: number; g: number; b: number };

/** What the tenant configures (public branding endpoint, S-02). Colours are `#rgb` or `#rrggbb`. */
export type TenantBranding = {
  primaryColor?: string | null;
  accentColor?: string | null;
  /** `Solid`: brand surfaces use the primary colour alone instead of the primary → accent gradient. */
  themeFill?: string | null;
};

export type BrandingTokens = { light: Record<string, string>; dark: Record<string, string> };

/** Minimum contrast of text (WCAG 1.4.3) and of UI components against their background (WCAG 1.4.11). */
export const TEXT_CONTRAST = 4.5;
export const UI_CONTRAST = 3;

/** Legacy default theme (#667eea → #764ba2): the platform brand when a tenant has none. */
export const DEFAULT_BRANDING: { primaryColor: string; accentColor: string } = {
  primaryColor: "#667eea",
  accentColor: "#764ba2",
};

const WHITE: Rgb = { r: 255, g: 255, b: 255 };
/** Near-black text, as `--foreground` (oklch 0.145). */
const INK: Rgb = { r: 10, g: 10, b: 10 };
/** Dark theme page background, as `--background` in `.dark` (oklch 0.145). */
const DARK_BACKGROUND: Rgb = INK;
/**
 * Dark theme cards and popovers, as `--card` in `.dark` (oklch 0.205): lighter than the page, so brand text (links on
 * the sign-in card) and focus rings are checked against it — what passes here passes on the page too.
 */
const DARK_SURFACE: Rgb = { r: 23, g: 23, b: 23 };

export function parseHexColor(value: string | null | undefined): Rgb | null {
  const digits = /^#?([0-9a-f]{3}|[0-9a-f]{6})$/i.exec(value?.trim() ?? "")?.[1];
  if (digits === undefined) {
    return null;
  }

  const hex = digits.length === 3 ? [...digits].map((digit) => digit + digit).join("") : digits;
  return {
    r: Number.parseInt(hex.slice(0, 2), 16),
    g: Number.parseInt(hex.slice(2, 4), 16),
    b: Number.parseInt(hex.slice(4, 6), 16),
  };
}

export function toHex({ r, g, b }: Rgb): string {
  return (
    "#" +
    [r, g, b]
      .map((channel) =>
        Math.round(clamp(channel, 0, 255))
          .toString(16)
          .padStart(2, "0"),
      )
      .join("")
  );
}

/** WCAG 2.x relative luminance. */
export function relativeLuminance({ r, g, b }: Rgb): number {
  const linear = (channel: number) => {
    const value = channel / 255;
    return value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * linear(r) + 0.7152 * linear(g) + 0.0722 * linear(b);
}

/** WCAG 2.x contrast ratio, 1–21. */
export function contrastRatio(first: Rgb, second: Rgb): number {
  const [a, b] = [relativeLuminance(first), relativeLuminance(second)];
  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
}

/** White or near-black, whichever reads better on `background`. */
export function readableForeground(background: Rgb): Rgb {
  return contrastRatio(background, WHITE) >= contrastRatio(background, INK) ? WHITE : INK;
}

/** Linear mix: `amount` 0 = `from`, 1 = `to`; rounded to whole channels, so checks match the emitted hex. */
export function mix(from: Rgb, to: Rgb, amount: number): Rgb {
  const t = clamp(amount, 0, 1);
  const channel = (a: number, b: number) => Math.round(a + (b - a) * t);
  return { r: channel(from.r, to.r), g: channel(from.g, to.g), b: channel(from.b, to.b) };
}

/**
 * The closest shade of `color` (mixed towards black or white, in 2% steps) with at least `minimum` contrast against
 * `against`. The colour itself when it already passes.
 */
export function ensureContrast(color: Rgb, against: Rgb, minimum: number): Rgb {
  if (contrastRatio(color, against) >= minimum) {
    return color;
  }

  const target = relativeLuminance(against) > 0.5 ? { r: 0, g: 0, b: 0 } : WHITE;
  for (let step = 1; step <= 50; step++) {
    const shade = mix(color, target, step / 50);
    if (contrastRatio(shade, against) >= minimum) {
      return shade;
    }
  }

  return target;
}

/**
 * A filled colour (button, badge) and its text at AA contrast. White text is preferred (as in the legacy theme): a
 * mid-tone brand colour is darkened until white passes; only clearly light colours (≥ 7:1 with dark text, e.g. yellow)
 * keep dark text.
 */
function filled(color: Rgb): { background: Rgb; foreground: Rgb } {
  if (contrastRatio(color, WHITE) >= TEXT_CONTRAST) {
    return { background: color, foreground: WHITE };
  }

  if (contrastRatio(color, INK) >= 7) {
    return { background: color, foreground: INK };
  }

  return { background: ensureContrast(color, WHITE, TEXT_CONTRAST), foreground: WHITE };
}

/**
 * Hover shade of a filled colour: 12% towards black under light text, towards white under dark text, so hovering a
 * button never lowers the contrast of its label (a translucent `bg-primary/90` would, e.g. 3.9:1 on the default brand).
 */
function hovered({ background, foreground }: { background: Rgb; foreground: Rgb }): Rgb {
  const towards = relativeLuminance(foreground) > 0.5 ? { r: 0, g: 0, b: 0 } : WHITE;
  return mix(background, towards, 0.12);
}

/** As {@link filled}, and still ≥ 3:1 against the dark page; otherwise a lighter shade with dark text. */
function filledOnDark(color: Rgb): { background: Rgb; foreground: Rgb } {
  const candidate = filled(ensureContrast(color, DARK_BACKGROUND, UI_CONTRAST));
  if (contrastRatio(candidate.background, DARK_BACKGROUND) >= UI_CONTRAST) {
    return candidate;
  }

  return { background: ensureContrast(color, INK, TEXT_CONTRAST), foreground: INK };
}

/**
 * CSS variables for the tenant brand, light and dark. `--primary-text` is the brand as text on the page (links), since a
 * light brand colour can fill a button (with dark text) but cannot be text on white. Invalid or missing colours fall back to the platform brand.
 * In dark mode the brand is lightened until filled components stand out from the page (≥ 3:1).
 */
export function brandingTokens(branding: TenantBranding | null | undefined): BrandingTokens {
  const tenantPrimary = parseHexColor(branding?.primaryColor);
  const primary = tenantPrimary ?? parseHexColor(DEFAULT_BRANDING.primaryColor)!;
  // Without a brand of its own the tenant gets the whole platform theme; with one, a missing accent follows the primary.
  const accent =
    parseHexColor(branding?.accentColor) ??
    tenantPrimary ??
    parseHexColor(DEFAULT_BRANDING.accentColor)!;

  const lightPrimary = filled(primary);
  const lightAccent = mix(accent, WHITE, 0.88);
  const lightAccentText = ensureContrast(accent, lightAccent, TEXT_CONTRAST);

  const darkPrimary = filledOnDark(primary);
  const darkAccent = mix(accent, DARK_BACKGROUND, 0.75);
  const darkAccentText = ensureContrast(accent, darkAccent, TEXT_CONTRAST);
  const solid = branding?.themeFill === "Solid";
  const gradient = (from: Rgb, to: Rgb) =>
    `linear-gradient(135deg, ${toHex(from)}, ${toHex(solid ? from : to)})`;

  return {
    light: {
      "--primary": toHex(lightPrimary.background),
      "--primary-foreground": toHex(lightPrimary.foreground),
      "--primary-hover": toHex(hovered(lightPrimary)),
      "--primary-text": toHex(ensureContrast(primary, WHITE, TEXT_CONTRAST)),
      "--ring": toHex(ensureContrast(primary, WHITE, UI_CONTRAST)),
      "--accent": toHex(lightAccent),
      "--accent-foreground": toHex(lightAccentText),
      "--sidebar-primary": toHex(lightPrimary.background),
      "--sidebar-primary-foreground": toHex(lightPrimary.foreground),
      "--brand-gradient": gradient(primary, accent),
    },
    dark: {
      "--primary": toHex(darkPrimary.background),
      "--primary-foreground": toHex(darkPrimary.foreground),
      "--primary-hover": toHex(hovered(darkPrimary)),
      "--primary-text": toHex(ensureContrast(primary, DARK_SURFACE, TEXT_CONTRAST)),
      "--ring": toHex(ensureContrast(primary, DARK_SURFACE, UI_CONTRAST)),
      "--accent": toHex(darkAccent),
      "--accent-foreground": toHex(darkAccentText),
      "--sidebar-primary": toHex(darkPrimary.background),
      "--sidebar-primary-foreground": toHex(darkPrimary.foreground),
      "--brand-gradient": gradient(
        mix(primary, DARK_BACKGROUND, 0.35),
        mix(accent, DARK_BACKGROUND, 0.35),
      ),
    },
  };
}

/** The style sheet that overrides the default tokens (values are generated here, never copied from input). */
export function brandingStyleSheet(branding: TenantBranding | null | undefined): string {
  const tokens = brandingTokens(branding);
  const block = (variables: Record<string, string>) =>
    Object.entries(variables)
      .map(([name, value]) => `${name}:${value};`)
      .join("");
  return `:root{${block(tokens.light)}}.dark{${block(tokens.dark)}}`;
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value));
}
