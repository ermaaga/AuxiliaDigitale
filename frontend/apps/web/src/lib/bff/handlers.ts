import { callApi, forwardableRequestHeaders, toBrowserResponse } from "./api";
import { readBffConfig, type BffArea, type BffConfig } from "./config";
import { csrfRefusal } from "./csrf";
import { Problems } from "./problem";
import {
  SESSION_COOKIE,
  isTenantSlug,
  sessionCookieOptions,
  sessionFromTokens,
  type BffSession,
  type TokenResponse,
} from "./session";
import { sessionStore, type SessionStore } from "./session-store";
import { remainingSeconds, withFreshAccessToken } from "./tokens";

/** Everything a handler needs; tests pass their own. */
export type BffContext = { config: BffConfig; store: SessionStore; now: () => number };

export function defaultContext(): BffContext {
  return { config: readBffConfig(), store: sessionStore(), now: Date.now };
}

const TENANT_TOKEN_MARGIN_MS = 30_000;

/** Paths the browser may never reach through a BFF: tokens are handled by the auth routes of the BFF itself. */
function isAllowedPath(area: BffArea, path: string): boolean {
  const segments = path.split("/");
  if (
    segments.some(
      (segment) => segment === "" || segment === "." || segment === ".." || /[\\/]/.test(segment),
    )
  ) {
    return false;
  }

  if (area === "tenant") {
    return (
      !path.startsWith("platform/") &&
      path !== "auth/token" &&
      path !== "auth/logout" &&
      path !== "auth/password/change"
    );
  }

  return path !== "platform/auth/token" && path !== "platform/auth/logout";
}

async function readSession(
  request: Request,
  area: BffArea,
  context: BffContext,
): Promise<BffSession | undefined> {
  const id = cookieValue(request, SESSION_COOKIE[area]);
  if (id === undefined) {
    return undefined;
  }

  const session = await context.store.get(id);
  return session?.area === area ? session : undefined;
}

function cookieValue(request: Request, name: string): string | undefined {
  const header = request.headers.get("cookie");
  if (!header) {
    return undefined;
  }

  for (const part of header.split(";")) {
    const [key, ...rest] = part.trim().split("=");
    if (key === name) {
      return decodeURIComponent(rest.join("="));
    }
  }

  return undefined;
}

function setSessionCookie(
  response: Response,
  area: BffArea,
  session: BffSession,
  context: BffContext,
): Response {
  const options = sessionCookieOptions(remainingSeconds(context.config, session, context.now));
  response.headers.append(
    "set-cookie",
    `${SESSION_COOKIE[area]}=${session.id}; Path=${options.path}; Max-Age=${options.maxAge}; HttpOnly; Secure; SameSite=Lax`,
  );
  return response;
}

function clearSessionCookie(response: Response, area: BffArea): Response {
  response.headers.append(
    "set-cookie",
    `${SESSION_COOKIE[area]}=; Path=/; Max-Age=0; HttpOnly; Secure; SameSite=Lax`,
  );
  return response;
}

async function safeCall(call: () => Promise<Response>): Promise<Response> {
  try {
    return await call();
  } catch {
    return Problems.apiUnavailable();
  }
}

/**
 * Sign-in (`POST /api/auth/login` and `/api/platform-auth/login`): the body of the API token request plus, for the
 * tenant app, the tenant slug. On success the tokens are stored server-side and only the session cookie is returned.
 */
export async function login(
  request: Request,
  area: BffArea,
  context: BffContext = defaultContext(),
): Promise<Response> {
  return issueSession(request, area, context, (input) => {
    const grantType = input.grantType ?? "password";
    if (grantType === "refresh_token" || typeof grantType !== "string") {
      return undefined;
    }

    return {
      path: area === "tenant" ? "auth/token" : "platform/auth/token",
      body:
        area === "tenant"
          ? { grantType, userName: input.userName, password: input.password, code: input.code }
          : { grantType, email: input.email, password: input.password, code: input.code },
    };
  });
}

/**
 * Change of an expired password (`POST /api/auth/password/change`, F35): the API answers with a token pair, so it goes
 * through the BFF like a sign-in and never through the proxy.
 */
export async function changeExpiredPassword(
  request: Request,
  context: BffContext = defaultContext(),
): Promise<Response> {
  return issueSession(request, "tenant", context, (input) => ({
    path: "auth/password/change",
    body: {
      userName: input.userName,
      currentPassword: input.currentPassword,
      newPassword: input.newPassword,
    },
  }));
}

/** Calls an API endpoint that issues tokens and turns the answer into a BFF session. */
async function issueSession(
  request: Request,
  area: BffArea,
  context: BffContext,
  toApiCall: (
    input: Record<string, unknown>,
  ) => { path: string; body: Record<string, unknown> } | undefined,
): Promise<Response> {
  if (csrfRefusal(request, context.config.publicOrigin)) {
    return Problems.csrf();
  }

  let input: Record<string, unknown>;
  try {
    input = (await request.json()) as Record<string, unknown>;
  } catch {
    return Problems.badRequest();
  }

  const tenant = area === "tenant" ? input.tenant : undefined;
  if (area === "tenant" && !isTenantSlug(tenant)) {
    return Problems.badRequest();
  }

  const call = toApiCall(input);
  if (call === undefined) {
    return Problems.badRequest();
  }

  return safeCall(async () => {
    const response = await callApi(
      context.config,
      area,
      {
        method: "POST",
        path: call.path,
        tenant: tenant as string | undefined,
        withClientSecret: true,
        headers: { "content-type": "application/json" },
        body: JSON.stringify(call.body),
      },
      request,
    );

    if (!response.ok) {
      return toBrowserResponse(response);
    }

    const tokens = (await response.json()) as TokenResponse;
    const previous = await readSession(request, area, context);
    if (previous) {
      await context.store.delete(previous.id);
    }

    const session = sessionFromTokens(area, tokens, context.now(), tenant as string | undefined);
    await context.store.set(session, remainingSeconds(context.config, session, context.now));
    const result = Response.json(area === "tenant" ? { tenant: session.tenant } : {}, {
      status: 200,
      headers: { "cache-control": "no-store" },
    });
    return setSessionCookie(result, area, session, context);
  });
}

/** Sign-out: the API ends the session (and revokes its tokens), then the BFF forgets it. Always clears the cookie. */
export async function logout(
  request: Request,
  area: BffArea,
  context: BffContext = defaultContext(),
): Promise<Response> {
  if (csrfRefusal(request, context.config.publicOrigin)) {
    return Problems.csrf();
  }

  const session = await readSession(request, area, context);
  if (session) {
    try {
      const fresh = await withFreshAccessToken(context.config, context.store, session, context.now);
      if (fresh) {
        await callApi(
          context.config,
          area,
          {
            method: "POST",
            path: area === "tenant" ? "auth/logout" : "platform/auth/logout",
            tenant: fresh.tenant,
            accessToken: fresh.accessToken,
          },
          request,
        );
      }
    } catch {
      // The API is not reachable: the BFF session still ends; the API session expires on its own.
    }

    await context.store.delete(session.id);
  }

  return clearSessionCookie(
    new Response(null, { status: 204, headers: { "cache-control": "no-store" } }),
    area,
  );
}

/** Whether the browser has a session (never the tokens). */
export async function sessionInfo(
  request: Request,
  area: BffArea,
  context: BffContext = defaultContext(),
): Promise<Response> {
  const session = await readSession(request, area, context);
  return Response.json(
    session
      ? { authenticated: true, tenant: session.tenant ?? null }
      : { authenticated: false, tenant: null },
    { headers: { "cache-control": "no-store" } },
  );
}

/**
 * `/api/bff/[...path]` and `/api/platform-bff/[...path]` → `/api/v1/[...path]` with the session's access token,
 * the client application and the tenant. Anonymous calls (e.g. the tenant's translations on the login page) are
 * forwarded without a token. In the console, a request with `X-Tenant` outside `platform/…` uses a tenant-scoped
 * platform token (D-21: technical endpoints of that tenant only), obtained and cached here.
 */
export async function proxy(
  request: Request,
  area: BffArea,
  segments: string[],
  context: BffContext = defaultContext(),
): Promise<Response> {
  if (csrfRefusal(request, context.config.publicOrigin)) {
    return Problems.csrf();
  }

  const path = segments.map((segment) => encodeURIComponent(segment)).join("/");
  if (
    segments.some((segment) => /[\\/]/.test(segment)) ||
    !isAllowedPath(area, segments.join("/"))
  ) {
    return Problems.forbiddenPath();
  }

  let session = await readSession(request, area, context);
  if (session) {
    session = await withFreshAccessToken(context.config, context.store, session, context.now).catch(
      () => session,
    );
    if (session === undefined) {
      return clearSessionCookie(Problems.unauthenticated(), area);
    }
  }

  let tenant = session?.tenant;
  let accessToken = session?.accessToken;
  if (area === "tenant" && tenant === undefined) {
    // Anonymous tenant-app call: the tenant comes from the page (login, activation, translations).
    const requested = request.headers.get("x-tenant");
    tenant = isTenantSlug(requested) ? requested : undefined;
  }

  if (area === "platform" && !path.startsWith("platform/")) {
    const requested = request.headers.get("x-tenant");
    if (!session || !isTenantSlug(requested)) {
      return session ? Problems.badRequest() : Problems.unauthenticated();
    }

    const tenantToken = await tenantScopedToken(request, session, requested, context);
    if (tenantToken instanceof Response) {
      return tenantToken;
    }

    tenant = requested;
    accessToken = tenantToken;
  }

  const hasBody = !["GET", "HEAD"].includes(request.method.toUpperCase());
  return safeCall(async () =>
    toBrowserResponse(
      await callApi(
        context.config,
        area,
        {
          method: request.method,
          path,
          search: new URL(request.url).search,
          headers: forwardableRequestHeaders(request),
          body: hasBody ? await request.arrayBuffer() : undefined,
          accessToken,
          tenant,
        },
        request,
      ),
    ),
  );
}

/** A tenant-scoped platform token for the console session, reused until it is about to expire. */
async function tenantScopedToken(
  request: Request,
  session: BffSession,
  tenant: string,
  context: BffContext,
): Promise<string | Response> {
  const cached = session.tenantTokens?.[tenant];
  if (cached && cached.expiresAt - TENANT_TOKEN_MARGIN_MS > context.now()) {
    return cached.token;
  }

  let response: Response;
  try {
    response = await callApi(
      context.config,
      "platform",
      {
        method: "POST",
        path: `platform/tenants/${tenant}/token`,
        accessToken: session.accessToken,
      },
      request,
    );
  } catch {
    return Problems.apiUnavailable();
  }

  if (!response.ok) {
    return toBrowserResponse(response);
  }

  const issued = (await response.json()) as { accessToken: string; expiresIn: number };
  const updated: BffSession = {
    ...session,
    tenantTokens: {
      ...session.tenantTokens,
      [tenant]: { token: issued.accessToken, expiresAt: context.now() + issued.expiresIn * 1000 },
    },
  };
  await context.store.set(updated, remainingSeconds(context.config, updated, context.now));
  return issued.accessToken;
}

/** Branding images a tenant may have (S-02). */
export const BRANDING_ASSETS = ["logo", "background"] as const;
export type BrandingAsset = (typeof BRANDING_ASSETS)[number];

/**
 * A branding image of a tenant (`GET /api/branding/{tenant}/{asset}?v=…`), for `<img>` and CSS backgrounds that
 * cannot send the tenant header: anonymous, no session, only the image types the API stores. The API answers with
 * the hash as ETag and caches a URL that names the current version for a year.
 */
export async function brandingImage(
  request: Request,
  tenant: string,
  asset: string,
  context: BffContext = defaultContext(),
): Promise<Response> {
  if (!isTenantSlug(tenant) || !(BRANDING_ASSETS as readonly string[]).includes(asset)) {
    return Problems.forbiddenPath();
  }

  const version = new URL(request.url).searchParams.get("v");
  const headers = new Headers();
  const ifNoneMatch = request.headers.get("if-none-match");
  if (ifNoneMatch !== null) {
    headers.set("if-none-match", ifNoneMatch);
  }

  return safeCall(async () => {
    const response = await callApi(
      context.config,
      "tenant",
      {
        method: "GET",
        path: `branding/${asset}`,
        search: version ? `?v=${encodeURIComponent(version)}` : "",
        headers,
        tenant,
      },
      request,
    );
    const type = response.headers.get("content-type") ?? "";
    if (response.ok && !/^image\/(png|jpeg|webp)$/.test(type)) {
      return Problems.apiUnavailable();
    }

    const browser = toBrowserResponse(response);
    browser.headers.set("x-content-type-options", "nosniff");
    return browser;
  });
}
