import { renderToStaticMarkup } from "react-dom/server";
import { HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";

import { AuthCard } from "@/features/auth/components/auth-card";
import { apiHandler, apiOk, apiProblem, mswServer, useMsw } from "@/test/msw";

import {
  DEFAULT_BRANDING,
  brandIdentity,
  brandingImageUrl,
  loginBackground,
  type Branding,
} from "./branding";
import { loadBranding } from "./server";

vi.mock("next/headers", () => ({ headers: async () => new Headers() }));

const branding = (overrides: Partial<Branding> = {}): Branding => ({
  ...DEFAULT_BRANDING,
  ...overrides,
  background: { ...DEFAULT_BRANDING.background, ...overrides.background },
});

describe("branding of the tenant app", () => {
  it("builds same-origin image URLs carrying the version", () => {
    expect(brandingImageUrl("acme", "logo", "0123abcd")).toBe("/api/branding/acme/logo?v=0123abcd");
    expect(brandingImageUrl("acme", "background", "a b")).toBe(
      "/api/branding/acme/background?v=a%20b",
    );
  });

  it("shows the logo only when the app name is off and a logo exists", () => {
    expect(brandIdentity("acme", branding())).toEqual({ appName: "Auxilia Digitale" });
    expect(brandIdentity("acme", branding({ useAppName: false }))).toEqual({
      appName: "Auxilia Digitale",
    });
    expect(
      brandIdentity("acme", branding({ appName: "Studio", useAppName: false, logoVersion: "v1" })),
    ).toEqual({ appName: "Studio", logoUrl: "/api/branding/acme/logo?v=v1" });
  });

  it("writes the login background from parsed colours only", () => {
    expect(loginBackground("acme", DEFAULT_BRANDING.background)).toEqual({
      backgroundImage: "linear-gradient(135deg, #667eea, #764ba2)",
      color: "#ffffff",
    });
    expect(
      loginBackground("acme", { ...DEFAULT_BRANDING.background, kind: "Solid", color: "#FFEE00" }),
    ).toEqual({ backgroundColor: "#ffee00", color: "#0a0a0a" });
    expect(
      loginBackground("acme", {
        ...DEFAULT_BRANDING.background,
        kind: "Solid",
        color: "red;background:url(x)",
      }),
    ).toBeUndefined();
    expect(
      loginBackground("acme", { ...DEFAULT_BRANDING.background, startColor: "url(javascript:x)" }),
    ).toBeUndefined();
  });

  it("covers an image with a veil, and falls back to the gradient without one", () => {
    const image = loginBackground("acme", {
      ...DEFAULT_BRANDING.background,
      kind: "Image",
      imageVersion: "v9",
    });
    expect(image?.backgroundImage).toContain('url("/api/branding/acme/background?v=v9")');
    expect(image?.color).toBe("#ffffff");
    expect(
      loginBackground("acme", { ...DEFAULT_BRANDING.background, kind: "Image" })?.backgroundImage,
    ).toBe("linear-gradient(135deg, #667eea, #764ba2)");
  });

  it("renders the logo with the app name as alternative text, and the background panel", () => {
    const html = renderToStaticMarkup(
      <AuthCard
        appName="Studio Rossi"
        logoUrl="/api/branding/acme/logo?v=v1"
        background={{ backgroundColor: "#123456", color: "#ffffff" }}
        title="Accedi"
      >
        <p>form</p>
      </AuthCard>,
    );

    expect(html).toContain('alt="Studio Rossi"');
    expect(html).toContain("background-color:#123456");
    expect(html).not.toContain("bg-brand");
    expect(html).not.toContain(">Studio Rossi<");

    const plain = renderToStaticMarkup(
      <AuthCard appName="Auxilia Digitale" title="Accedi">
        <p>form</p>
      </AuthCard>,
    );
    expect(plain).toContain("bg-brand");
    expect(plain).toContain(">Auxilia Digitale<");
  });
});

describe("loading the branding", () => {
  useMsw();

  it("reads the public branding of the tenant", async () => {
    let tenant: string | null = null;
    mswServer.use(
      apiHandler("get", "/api/v1/branding", ({ request }) => {
        tenant = request.headers.get("x-tenant");
        return apiOk("get", "/api/v1/branding", branding({ appName: "Studio Alfa" }));
      }),
    );

    expect((await loadBranding("acme")).appName).toBe("Studio Alfa");
    expect(tenant).toBe("acme");
  });

  it("falls back to the platform branding when the API fails", async () => {
    mswServer.use(apiHandler("get", "/api/v1/branding", () => apiProblem(503, "AUX-11010")));
    expect(await loadBranding("down")).toEqual(DEFAULT_BRANDING);

    mswServer.use(apiHandler("get", "/api/v1/branding", () => HttpResponse.error()));
    expect(await loadBranding("offline")).toEqual(DEFAULT_BRANDING);
  });
});
