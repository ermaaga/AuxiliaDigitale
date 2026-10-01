import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { readBffConfig } from "./config";
import { csrfRefusal } from "./csrf";
import {
  brandingImage,
  changeExpiredPassword,
  login,
  logout,
  proxy,
  sessionInfo,
  type BffContext,
} from "./handlers";
import { SESSION_COOKIE, type BffSession } from "./session";
import { InMemorySessionStore } from "./session-store";
import { withFreshAccessToken } from "./tokens";
import { contentSecurityPolicy } from "@/lib/security/csp";

const ORIGIN = "https://app.test";
const API = "http://api.test";

type Call = { url: string; method: string; headers: Headers; body: string | undefined };

let clock = 1_000_000;
let calls: Call[];
let replies: Array<(call: Call) => Response>;
let context: BffContext;

function reply(status: number, body?: unknown, headers: Record<string, string> = {}) {
  return () =>
    new Response(body === undefined ? null : JSON.stringify(body), {
      status,
      headers: { "content-type": "application/json", ...headers },
    });
}

const tokens = (suffix: string, expiresIn = 600) => ({
  accessToken: `access-${suffix}`,
  tokenType: "Bearer",
  expiresIn,
  refreshToken: `refresh-${suffix}`,
});

function browser(path: string, init: RequestInit & { cookie?: string } = {}) {
  const headers = new Headers(init.headers);
  if (init.method && init.method !== "GET") {
    headers.set("origin", ORIGIN);
    headers.set("x-requested-with", "auxilia");
  }

  if (init.cookie) {
    headers.set("cookie", init.cookie);
  }

  headers.set("x-forwarded-for", "203.0.113.9, 10.0.0.1");
  headers.set("user-agent", "vitest-browser");
  return new Request(`${ORIGIN}${path}`, { ...init, headers });
}

async function signedIn(
  area: "tenant" | "platform" = "tenant",
  expiresIn = 600,
): Promise<{ cookie: string; id: string }> {
  replies.push(reply(200, tokens("1", expiresIn)));
  const body =
    area === "tenant"
      ? { tenant: "acme", userName: "mario", password: "pw" }
      : { email: "ops@x.test", password: "pw", code: "123456" };
  const response = await login(
    browser("/api/auth/login", { method: "POST", body: JSON.stringify(body) }),
    area,
    context,
  );
  expect(response.status).toBe(200);
  const cookie = response.headers.get("set-cookie")!.split(";")[0]!;
  return { cookie, id: cookie.split("=")[1]! };
}

beforeEach(() => {
  clock = 1_000_000;
  calls = [];
  replies = [];
  context = {
    config: {
      ...readBffConfig({
        AUXILIA_API_URL: API,
        AUXILIA_WEB_CLIENT_SECRET: "web-secret",
        AUXILIA_CONSOLE_CLIENT_SECRET: "console-secret",
      }),
      publicOrigin: ORIGIN,
    },
    store: new InMemorySessionStore(() => clock),
    now: () => clock,
  };
  vi.stubGlobal("fetch", async (input: string, init: RequestInit) => {
    const body =
      init.body === undefined || init.body === null
        ? undefined
        : typeof init.body === "string"
          ? init.body
          : new TextDecoder().decode(init.body as ArrayBuffer);
    const call = {
      url: input,
      method: init.method ?? "GET",
      headers: new Headers(init.headers),
      body,
    };
    calls.push(call);
    const next = replies.shift();
    if (!next) {
      throw new Error(`Unexpected API call ${call.method} ${call.url}`);
    }

    return next(call);
  });
});

afterEach(() => vi.unstubAllGlobals());

describe("CSRF", () => {
  it("lets safe methods through and requires the header and the origin on the others", () => {
    expect(csrfRefusal(new Request(`${ORIGIN}/x`), ORIGIN)).toBeNull();
    const post = (headers: Record<string, string>) =>
      new Request(`${ORIGIN}/x`, { method: "POST", headers });
    expect(csrfRefusal(post({ origin: ORIGIN }), ORIGIN)).toBe("missing-header");
    expect(
      csrfRefusal(post({ origin: "https://evil.test", "x-requested-with": "auxilia" }), ORIGIN),
    ).toBe("origin-mismatch");
    expect(csrfRefusal(post({ "x-requested-with": "auxilia" }), ORIGIN)).toBe("origin-mismatch");
    expect(csrfRefusal(post({ origin: ORIGIN, "x-requested-with": "auxilia" }), ORIGIN)).toBeNull();
    expect(
      csrfRefusal(post({ origin: ORIGIN, "x-requested-with": "auxilia" }), undefined),
    ).toBeNull();
  });
});

describe("login and session", () => {
  it("stores the tokens server-side and returns only an opaque __Host- cookie", async () => {
    replies.push(reply(200, tokens("1")));
    const response = await login(
      browser("/api/auth/login", {
        method: "POST",
        body: JSON.stringify({ tenant: "acme", userName: "mario", password: "pw" }),
      }),
      "tenant",
      context,
    );

    expect(response.status).toBe(200);
    expect(await response.json()).toEqual({ tenant: "acme" });
    const cookie = response.headers.get("set-cookie")!;
    expect(cookie).toMatch(
      /^__Host-aux_sid=[\w-]{43}; Path=\/; Max-Age=43200; HttpOnly; Secure; SameSite=Lax$/,
    );
    expect(cookie).not.toContain("access-1");

    const [call] = calls;
    expect(call!.url).toBe(`${API}/api/v1/auth/token`);
    expect(Object.fromEntries(call!.headers)).toMatchObject({
      "x-client-id": "web-bff",
      "x-client-secret": "web-secret",
      "x-tenant": "acme",
      "x-forwarded-for": "10.0.0.1",
      "user-agent": "vitest-browser",
    });
    expect(JSON.parse(call!.body!)).toEqual({
      grantType: "password",
      userName: "mario",
      password: "pw",
    });

    const info = await sessionInfo(
      browser("/api/auth/session", { cookie: cookie.split(";")[0] }),
      "tenant",
      context,
    );
    expect(await info.json()).toEqual({ authenticated: true, tenant: "acme" });
  });

  it("passes API refusals through and refuses bad input, refresh grants and cross-site posts", async () => {
    replies.push(
      reply(
        401,
        { errorCode: "AUX-12001" },
        { "content-type": "application/problem+json", "set-cookie": "x=1" },
      ),
    );
    const refused = await login(
      browser("/api/auth/login", {
        method: "POST",
        body: JSON.stringify({ tenant: "acme", userName: "mario", password: "no" }),
      }),
      "tenant",
      context,
    );
    expect(refused.status).toBe(401);
    expect(await refused.json()).toEqual({ errorCode: "AUX-12001" });
    expect(refused.headers.get("set-cookie")).toBeNull();

    const bad = (body: unknown) =>
      login(
        browser("/api/auth/login", { method: "POST", body: JSON.stringify(body) }),
        "tenant",
        context,
      );
    expect((await bad({ tenant: "../acme", userName: "u", password: "p" })).status).toBe(400);
    expect(
      (await bad({ tenant: "acme", grantType: "refresh_token", refreshToken: "x" })).status,
    ).toBe(400);
    expect(
      (
        await login(
          browser("/api/auth/login", { method: "POST", body: "not json" }),
          "tenant",
          context,
        )
      ).status,
    ).toBe(400);

    const crossSite = new Request(`${ORIGIN}/api/auth/login`, {
      method: "POST",
      headers: { origin: "https://evil.test", "x-requested-with": "auxilia" },
      body: "{}",
    });
    expect((await login(crossSite, "tenant", context)).status).toBe(403);
    expect(calls).toHaveLength(1);
  });

  it("console sign-in uses the console client and its own cookie", async () => {
    const { cookie } = await signedIn("platform");
    expect(cookie.startsWith(`${SESSION_COOKIE.platform}=`)).toBe(true);
    expect(calls[0]!.url).toBe(`${API}/api/v1/platform/auth/token`);
    expect(calls[0]!.headers.get("x-client-id")).toBe("console");
    expect(calls[0]!.headers.get("x-tenant")).toBeNull();

    // A console cookie is not a tenant-app session.
    const info = await sessionInfo(
      browser("/api/auth/session", {
        cookie: cookie.replace(SESSION_COOKIE.platform, SESSION_COOKIE.tenant),
      }),
      "tenant",
      context,
    );
    expect(await info.json()).toEqual({ authenticated: false, tenant: null });
  });

  it("logout ends the API session, forgets the tokens and clears the cookie", async () => {
    const { cookie, id } = await signedIn();
    replies.push(reply(204));

    const response = await logout(
      browser("/api/auth/logout", { method: "POST", cookie }),
      "tenant",
      context,
    );

    expect(response.status).toBe(204);
    expect(response.headers.get("set-cookie")).toContain("Max-Age=0");
    expect(calls[1]!.url).toBe(`${API}/api/v1/auth/logout`);
    expect(calls[1]!.headers.get("authorization")).toBe("Bearer access-1");
    expect(await context.store.get(id)).toBeUndefined();
  });

  it("logout still ends the BFF session when the API is down", async () => {
    const { cookie, id } = await signedIn();
    replies.push(() => {
      throw new TypeError("fetch failed");
    });

    expect(
      (await logout(browser("/api/auth/logout", { method: "POST", cookie }), "tenant", context))
        .status,
    ).toBe(204);
    expect(await context.store.get(id)).toBeUndefined();
  });
});

describe("expired password", () => {
  it("changes it through the BFF and opens a session with the new tokens", async () => {
    replies.push(reply(200, tokens("9")));

    const response = await changeExpiredPassword(
      browser("/api/auth/password/change", {
        method: "POST",
        body: JSON.stringify({
          tenant: "acme",
          userName: "mario",
          currentPassword: "old",
          newPassword: "new one!",
          extra: "x",
        }),
      }),
      context,
    );

    expect(response.status).toBe(200);
    expect(response.headers.get("set-cookie")).toMatch(/^__Host-aux_sid=/);
    expect(calls[0]!.url).toBe(`${API}/api/v1/auth/password/change`);
    expect(calls[0]!.headers.get("x-client-secret")).toBe("web-secret");
    expect(JSON.parse(calls[0]!.body!)).toEqual({
      userName: "mario",
      currentPassword: "old",
      newPassword: "new one!",
    });
  });

  it("passes the API refusal through without a session", async () => {
    replies.push(reply(400, { errorCode: "AUX-12042" }));

    const response = await changeExpiredPassword(
      browser("/api/auth/password/change", {
        method: "POST",
        body: JSON.stringify({
          tenant: "acme",
          userName: "m",
          currentPassword: "a",
          newPassword: "b",
        }),
      }),
      context,
    );

    expect(response.status).toBe(400);
    expect(response.headers.get("set-cookie")).toBeNull();
  });
});

describe("proxy", () => {
  it("adds token, client and the session's tenant, forwards allowed headers only", async () => {
    const { cookie } = await signedIn();
    replies.push(
      reply(200, { items: [] }, { etag: '"v1"', "set-cookie": "api=1", server: "kestrel" }),
    );

    const response = await proxy(
      browser("/api/bff/identity/login-attempts?page=2", {
        cookie,
        headers: { "x-tenant": "other", "if-none-match": '"v0"', cookie2: "x" },
      }),
      "tenant",
      ["identity", "login-attempts"],
      context,
    );

    expect(response.status).toBe(200);
    expect(response.headers.get("etag")).toBe('"v1"');
    expect(response.headers.get("set-cookie")).toBeNull();
    expect(response.headers.get("server")).toBeNull();
    const call = calls[1]!;
    expect(call.url).toBe(`${API}/api/v1/identity/login-attempts?page=2`);
    expect(Object.fromEntries(call.headers)).toMatchObject({
      authorization: "Bearer access-1",
      "x-client-id": "web-bff",
      "x-tenant": "acme",
      "if-none-match": '"v0"',
    });
    expect(call.headers.get("x-client-secret")).toBeNull();
    expect(call.headers.get("cookie")).toBeNull();
  });

  it("forwards anonymous calls with the tenant of the page", async () => {
    replies.push(reply(200, { Save: "Salva" }));

    const response = await proxy(
      browser("/api/bff/i18n/it", { headers: { "x-tenant": "acme" } }),
      "tenant",
      ["i18n", "it"],
      context,
    );

    expect(response.status).toBe(200);
    expect(calls[0]!.headers.get("authorization")).toBeNull();
    expect(calls[0]!.headers.get("x-tenant")).toBe("acme");
  });

  it.each([
    ["tenant", ["auth", "token"]],
    ["tenant", ["auth", "logout"]],
    ["tenant", ["auth", "password", "change"]],
    ["tenant", ["platform", "me"]],
    ["tenant", ["..", "health"]],
    ["tenant", ["a/b"]],
    ["platform", ["platform", "auth", "token"]],
  ] as const)("refuses %s path %j", async (area, segments) => {
    const response = await proxy(browser("/api/bff/x"), area, [...segments], context);
    expect(response.status).toBe(404);
    expect(calls).toHaveLength(0);
  });

  it("requires the CSRF header on mutations and sends the body", async () => {
    const { cookie } = await signedIn();
    const noHeader = new Request(`${ORIGIN}/api/bff/me/password`, {
      method: "POST",
      headers: { cookie, origin: ORIGIN },
      body: "{}",
    });
    expect((await proxy(noHeader, "tenant", ["me", "password"], context)).status).toBe(403);

    replies.push(reply(204));
    const response = await proxy(
      browser("/api/bff/me/password", {
        method: "POST",
        cookie,
        headers: { "content-type": "application/json" },
        body: '{"a":1}',
      }),
      "tenant",
      ["me", "password"],
      context,
    );
    expect(response.status).toBe(204);
    expect(calls[1]!.body).toBe('{"a":1}');
  });

  it("maps an unreachable API to 502", async () => {
    replies.push(() => {
      throw new TypeError("fetch failed");
    });
    const response = await proxy(
      browser("/api/bff/i18n/it", { headers: { "x-tenant": "acme" } }),
      "tenant",
      ["i18n", "it"],
      context,
    );
    expect(response.status).toBe(502);
    expect((await response.json()).errorCode).toBe("AUX-WEB-502");
  });
});

describe("refresh", () => {
  it("refreshes an expiring token once for concurrent requests (rotating refresh tokens)", async () => {
    const { cookie, id } = await signedIn("tenant", 60);
    clock += 45_000;
    replies.push(reply(200, tokens("2")), reply(200, {}), reply(200, {}));

    const [first, second] = await Promise.all([
      proxy(browser("/api/bff/me", { cookie }), "tenant", ["me"], context),
      proxy(browser("/api/bff/me", { cookie }), "tenant", ["me"], context),
    ]);

    expect([first.status, second.status]).toEqual([200, 200]);
    const refreshes = calls.filter((call) => call.body?.includes("refresh_token"));
    expect(refreshes).toHaveLength(1);
    expect(JSON.parse(refreshes[0]!.body!)).toEqual({
      grantType: "refresh_token",
      refreshToken: "refresh-1",
    });
    expect(refreshes[0]!.headers.get("x-client-secret")).toBe("web-secret");
    expect(
      calls.slice(-2).every((call) => call.headers.get("authorization") === "Bearer access-2"),
    ).toBe(true);
    expect((await context.store.get(id))!.refreshToken).toBe("refresh-2");
  });

  it("ends the session when the API refuses the refresh", async () => {
    const { cookie, id } = await signedIn("tenant", 10);
    replies.push(reply(401, { errorCode: "AUX-12015" }));

    const response = await proxy(browser("/api/bff/me", { cookie }), "tenant", ["me"], context);

    expect(response.status).toBe(401);
    expect(response.headers.get("set-cookie")).toContain("Max-Age=0");
    expect(await context.store.get(id)).toBeUndefined();
  });

  it("keeps a fresh token and expires sessions after their absolute lifetime", async () => {
    const { id } = await signedIn();
    const session = (await context.store.get(id)) as BffSession;
    expect(await withFreshAccessToken(context.config, context.store, session, context.now)).toEqual(
      session,
    );
    expect(calls).toHaveLength(1);

    clock += 43_200_000;
    expect(await context.store.get(id)).toBeUndefined();
  });
});

describe("console technical endpoints", () => {
  it("uses a cached tenant-scoped platform token for tenant paths and the console token for platform paths", async () => {
    const { cookie } = await signedIn("platform");
    replies.push(
      reply(200, { accessToken: "tenant-token", tokenType: "Bearer", expiresIn: 600 }),
      reply(200, []),
      reply(200, []),
      reply(200, {}),
    );

    const keys = () =>
      proxy(
        browser("/api/platform-bff/localization/keys", { cookie, headers: { "x-tenant": "acme" } }),
        "platform",
        ["localization", "keys"],
        context,
      );
    expect((await keys()).status).toBe(200);
    expect((await keys()).status).toBe(200);
    expect(
      (
        await proxy(
          browser("/api/platform-bff/platform/me", { cookie }),
          "platform",
          ["platform", "me"],
          context,
        )
      ).status,
    ).toBe(200);

    expect(calls.map((call) => call.url)).toEqual([
      `${API}/api/v1/platform/auth/token`,
      `${API}/api/v1/platform/tenants/acme/token`,
      `${API}/api/v1/localization/keys`,
      `${API}/api/v1/localization/keys`,
      `${API}/api/v1/platform/me`,
    ]);
    expect(calls[1]!.headers.get("authorization")).toBe("Bearer access-1");
    expect(calls[2]!.headers.get("authorization")).toBe("Bearer tenant-token");
    expect(calls[2]!.headers.get("x-tenant")).toBe("acme");
    expect(calls[4]!.headers.get("authorization")).toBe("Bearer access-1");
  });

  it("needs a session and a valid tenant for tenant paths", async () => {
    expect(
      (
        await proxy(
          browser("/api/platform-bff/localization/keys", { headers: { "x-tenant": "acme" } }),
          "platform",
          ["localization", "keys"],
          context,
        )
      ).status,
    ).toBe(401);
    const { cookie } = await signedIn("platform");
    expect(
      (
        await proxy(
          browser("/api/platform-bff/localization/keys", { cookie }),
          "platform",
          ["localization", "keys"],
          context,
        )
      ).status,
    ).toBe(400);
    replies.push(reply(404, { errorCode: "AUX-11009" }));
    expect(
      (
        await proxy(
          browser("/api/platform-bff/localization/keys", {
            cookie,
            headers: { "x-tenant": "gone" },
          }),
          "platform",
          ["localization", "keys"],
          context,
        )
      ).status,
    ).toBe(404);
  });
});

describe("branding images", () => {
  const png = new Uint8Array([0x89, 0x50, 0x4e, 0x47]);
  const image = (type: string) => () =>
    new Response(png, {
      status: 200,
      headers: {
        "content-type": type,
        etag: '"abc"',
        "cache-control": "public, max-age=31536000, immutable",
        "set-cookie": "x=1",
      },
    });

  it("serves the image of the tenant anonymously with its cache headers", async () => {
    replies.push(image("image/png"));
    const response = await brandingImage(
      browser("/api/branding/acme/logo?v=abc", { cookie: "aux_session=whatever" }),
      "acme",
      "logo",
      context,
    );

    expect(response.status).toBe(200);
    expect(new Uint8Array(await response.arrayBuffer())).toEqual(png);
    expect(response.headers.get("etag")).toBe('"abc"');
    expect(response.headers.get("cache-control")).toContain("immutable");
    expect(response.headers.get("x-content-type-options")).toBe("nosniff");
    expect(response.headers.get("set-cookie")).toBeNull();
    expect(calls[0]!.url).toBe(`${API}/api/v1/branding/logo?v=abc`);
    expect(calls[0]!.headers.get("x-tenant")).toBe("acme");
    expect(calls[0]!.headers.get("authorization")).toBeNull();
  });

  it("passes revalidation through", async () => {
    replies.push(() => new Response(null, { status: 304, headers: { etag: '"abc"' } }));
    const response = await brandingImage(
      browser("/api/branding/acme/background", { headers: { "if-none-match": '"abc"' } }),
      "acme",
      "background",
      context,
    );

    expect(response.status).toBe(304);
    expect(calls[0]!.headers.get("if-none-match")).toBe('"abc"');
  });

  it("refuses unknown assets, invalid tenants and anything but an image", async () => {
    expect((await brandingImage(browser("/x"), "acme", "favicon", context)).status).toBe(404);
    expect((await brandingImage(browser("/x"), "../etc", "logo", context)).status).toBe(404);
    expect(calls).toHaveLength(0);

    replies.push(image("image/svg+xml"));
    expect((await brandingImage(browser("/x"), "acme", "logo", context)).status).toBe(502);
    replies.push(reply(404, { errorCode: "AUX-20009" }));
    expect((await brandingImage(browser("/x"), "acme", "logo", context)).status).toBe(404);
  });
});

describe("content security policy", () => {
  it("allows scripts only with the nonce and eval only in development", () => {
    const production = contentSecurityPolicy("abc", false, "https://api.test");
    expect(production).toContain("script-src 'self' 'nonce-abc' 'strict-dynamic'");
    expect(production).not.toContain("unsafe-eval");
    expect(production).toContain("connect-src 'self' https://api.test");
    expect(production).toContain("frame-ancestors 'none'");
    expect(production).toContain("upgrade-insecure-requests");
    expect(contentSecurityPolicy("abc", true)).toContain("'unsafe-eval'");
    expect(contentSecurityPolicy("abc", true)).toContain("connect-src 'self';");
  });
});
