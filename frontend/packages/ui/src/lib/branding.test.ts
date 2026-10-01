import { describe, expect, it } from "vitest";

import {
  DEFAULT_BRANDING,
  TEXT_CONTRAST,
  UI_CONTRAST,
  brandingStyleSheet,
  brandingTokens,
  contrastRatio,
  ensureContrast,
  parseHexColor,
  readableForeground,
  toHex,
} from "./branding";

const hex = (value: string | undefined) => {
  const color = parseHexColor(value);
  if (color === null) {
    throw new Error(`not a colour: ${value}`);
  }
  return color;
};
const black = hex("#000000");
const white = hex("#ffffff");
const darkBackground = hex("#0a0a0a");
/** `--card` of the dark theme: brand text sits on the sign-in card. */
const darkCard = hex("#171717");

describe("colour maths", () => {
  it("parses #rgb and #rrggbb and rejects anything else", () => {
    expect(parseHexColor("#fff")).toEqual({ r: 255, g: 255, b: 255 });
    expect(parseHexColor(" 667EEA ")).toEqual({ r: 102, g: 126, b: 234 });
    expect(parseHexColor("red")).toBeNull();
    expect(parseHexColor("#12345")).toBeNull();
    expect(parseHexColor("#fff;}body{display:none")).toBeNull();
    expect(parseHexColor(null)).toBeNull();
    expect(toHex({ r: 102, g: 126, b: 234 })).toBe("#667eea");
  });

  it("computes WCAG contrast ratios", () => {
    expect(contrastRatio(black, white)).toBeCloseTo(21, 5);
    expect(contrastRatio(white, white)).toBe(1);
    expect(contrastRatio(hex("#767676"), white)).toBeCloseTo(4.54, 2);
  });

  it("picks the more readable text colour", () => {
    expect(readableForeground(hex("#ffeb3b"))).not.toEqual(white);
    expect(readableForeground(hex("#1e3a8a"))).toEqual(white);
  });

  it("keeps passing colours and darkens or lightens the others", () => {
    expect(ensureContrast(hex("#1e3a8a"), white, TEXT_CONTRAST)).toEqual(hex("#1e3a8a"));
    expect(
      contrastRatio(ensureContrast(hex("#667eea"), white, TEXT_CONTRAST), white),
    ).toBeGreaterThanOrEqual(TEXT_CONTRAST);
    expect(
      contrastRatio(ensureContrast(hex("#1e3a8a"), darkBackground, UI_CONTRAST), darkBackground),
    ).toBeGreaterThanOrEqual(UI_CONTRAST);
  });
});

describe("tenant branding tokens", () => {
  const brands = [
    "#667eea",
    "#764ba2",
    "#ffeb3b",
    "#00bcd4",
    "#e91e63",
    "#000000",
    "#ffffff",
    "#808080",
    "#10b981",
  ];

  it.each(brands)(
    "primary %s gives AA text in both themes and visible components in dark mode",
    (primary) => {
      const tokens = brandingTokens({ primaryColor: primary, accentColor: "#764ba2" });
      for (const theme of [tokens.light, tokens.dark]) {
        expect(
          contrastRatio(hex(theme["--primary"]), hex(theme["--primary-foreground"])),
        ).toBeGreaterThanOrEqual(TEXT_CONTRAST);
        expect(
          contrastRatio(hex(theme["--accent"]), hex(theme["--accent-foreground"])),
        ).toBeGreaterThanOrEqual(TEXT_CONTRAST);
      }

      expect(contrastRatio(hex(tokens.light["--ring"]), white)).toBeGreaterThanOrEqual(UI_CONTRAST);
      expect(contrastRatio(hex(tokens.dark["--primary"]), darkBackground)).toBeGreaterThanOrEqual(
        UI_CONTRAST,
      );
    },
  );

  it("meets the contrast rules for every colour of a coarse RGB grid", () => {
    const levels = [0, 51, 102, 153, 204, 255];
    for (const r of levels) {
      for (const g of levels) {
        for (const b of levels) {
          const tokens = brandingTokens({
            primaryColor: toHex({ r, g, b }),
            accentColor: toHex({ r: b, g: r, b: g }),
          });
          for (const theme of [tokens.light, tokens.dark]) {
            expect(
              contrastRatio(hex(theme["--primary"]), hex(theme["--primary-foreground"])),
            ).toBeGreaterThanOrEqual(TEXT_CONTRAST);
            expect(
              contrastRatio(hex(theme["--primary-hover"]), hex(theme["--primary-foreground"])),
            ).toBeGreaterThanOrEqual(
              contrastRatio(hex(theme["--primary"]), hex(theme["--primary-foreground"])),
            );
            expect(
              contrastRatio(hex(theme["--accent"]), hex(theme["--accent-foreground"])),
            ).toBeGreaterThanOrEqual(TEXT_CONTRAST);
          }

          expect(
            contrastRatio(hex(tokens.dark["--primary"]), darkBackground),
          ).toBeGreaterThanOrEqual(UI_CONTRAST);
          expect(contrastRatio(hex(tokens.light["--primary-text"]), white)).toBeGreaterThanOrEqual(
            TEXT_CONTRAST,
          );
          for (const surface of [darkBackground, darkCard]) {
            expect(
              contrastRatio(hex(tokens.dark["--primary-text"]), surface),
            ).toBeGreaterThanOrEqual(TEXT_CONTRAST);
            expect(contrastRatio(hex(tokens.dark["--ring"]), surface)).toBeGreaterThanOrEqual(
              UI_CONTRAST,
            );
          }
          expect(contrastRatio(hex(tokens.light["--ring"]), white)).toBeGreaterThanOrEqual(
            UI_CONTRAST,
          );
          expect(contrastRatio(hex(tokens.dark["--ring"]), darkBackground)).toBeGreaterThanOrEqual(
            UI_CONTRAST,
          );
        }
      }
    }
  });

  it("keeps an accessible brand colour unchanged and prefers white text", () => {
    expect(brandingTokens({ primaryColor: "#1e3a8a" }).light["--primary"]).toBe("#1e3a8a");
    const legacy = brandingTokens(null).light;
    expect(legacy["--primary-foreground"]).toBe("#ffffff");
    expect(legacy["--primary"]).not.toBe(DEFAULT_BRANDING.primaryColor);
    expect(brandingTokens({ primaryColor: "#ffeb3b" }).light).toMatchObject({
      "--primary": "#ffeb3b",
      "--primary-foreground": "#0a0a0a",
    });
  });

  it("falls back to the platform brand for missing or invalid colours", () => {
    expect(brandingTokens(null)).toEqual(brandingTokens(DEFAULT_BRANDING));
    expect(brandingTokens({ primaryColor: "javascript:alert(1)" })).toEqual(brandingTokens(null));
    expect(brandingTokens({ primaryColor: "#123456" }).light["--brand-gradient"]).toBe(
      "linear-gradient(135deg, #123456, #123456)",
    );
  });

  it("keeps brand links readable on dark cards (#2b6cb0 gave 4.2:1)", () => {
    const tokens = brandingTokens({ primaryColor: "#2b6cb0" });
    expect(contrastRatio(hex(tokens.dark["--primary-text"]), darkCard)).toBeGreaterThanOrEqual(
      TEXT_CONTRAST,
    );
  });

  it("uses the primary colour alone for a solid fill", () => {
    const gradient = brandingTokens({ primaryColor: "#225588", accentColor: "#aa3366" });
    const solid = brandingTokens({
      primaryColor: "#225588",
      accentColor: "#aa3366",
      themeFill: "Solid",
    });

    expect(gradient.light["--brand-gradient"]).toBe("linear-gradient(135deg, #225588, #aa3366)");
    expect(solid.light["--brand-gradient"]).toBe("linear-gradient(135deg, #225588, #225588)");
    expect(solid.dark["--brand-gradient"]).toMatch(/^linear-gradient\(135deg, (#\w{6}), \1\)$/);
    expect(solid.light["--primary"]).toBe(gradient.light["--primary"]);
  });

  it("writes light tokens on :root and dark tokens on .dark only", () => {
    const css = brandingStyleSheet({ primaryColor: "#10b981", accentColor: "</style><script>" });
    expect(css).toMatch(/^:root\{--primary:#[0-9a-f]{6};.*\}\.dark\{--primary:#[0-9a-f]{6};.*\}$/);
    expect(css).not.toContain("<");
  });
});

describe("default tokens", () => {
  it("tokens.css holds exactly the platform brand computed here", async () => {
    const { readFile } = await import("node:fs/promises");
    const css = await readFile(new URL("../styles/tokens.css", import.meta.url), "utf8");
    const block = (selector: string) => {
      const body =
        new RegExp(`${selector.replace(".", "\\.")} \\{([\\s\\S]*?)\\n\\}`).exec(css)?.[1] ?? "";
      return Object.fromEntries(
        [...body.matchAll(/(--[\w-]+):\s*([^;]+);\s*\/\* brand \*\//g)].map((match) => [
          match[1],
          match[2],
        ]),
      );
    };
    const tokens = brandingTokens(null);

    expect(block(":root")).toEqual(tokens.light);
    expect(block(".dark")).toEqual(tokens.dark);
  });
});
