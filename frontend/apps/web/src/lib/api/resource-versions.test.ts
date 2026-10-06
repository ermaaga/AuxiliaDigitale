import { readFileSync } from "node:fs";
import path from "node:path";

import { afterEach, describe, expect, it, vi } from "vitest";

import { createBffClient } from "./client";
import { VERSIONED_WRITES, isVersionedWrite, resourceVersions } from "./resource-versions";

const ORIGIN = "http://app.test";
const TASK = "0199aaaa-0000-7000-8000-000000000001";

type Reply = { status: number; etag?: string };

/** A fetch that answers each request in turn and records them. */
function scriptedFetch(...replies: Reply[]) {
  const requests: Request[] = [];
  const fetch = vi.fn(async (request: Request) => {
    requests.push(request);
    const reply = replies.shift() ?? { status: 200 };
    return new Response(reply.status === 204 ? null : JSON.stringify({}), {
      status: reply.status,
      headers: { "content-type": "application/json", ...(reply.etag ? { etag: reply.etag } : {}) },
    });
  });
  return { fetch, requests };
}

const task = {
  title: "x",
  notes: null,
  dueOn: null,
  assigneeUserId: TASK,
  clientId: null,
  caseId: null,
};

afterEach(() => resourceVersions.clear());

describe("optimistic concurrency (F29)", () => {
  it("lists exactly the writes the API protects (every operation documenting 428)", () => {
    const contract = JSON.parse(
      readFileSync(path.resolve(__dirname, "../../../../../../backend/openapi/v1.json"), "utf8"),
    ) as { paths: Record<string, Record<string, { responses?: Record<string, unknown> }>> };
    const protectedWrites = Object.entries(contract.paths).flatMap(([route, operations]) =>
      Object.entries(operations)
        .filter(([, operation]) => operation.responses?.["428"])
        .map(([method]) => `${method.toUpperCase()} ${route}`),
    );

    expect([...VERSIONED_WRITES].sort()).toEqual(protectedWrites.sort());
  });

  it("matches the routes of the protected writes only", () => {
    expect(isVersionedWrite("put", `/api/v1/tasks/${TASK}`)).toBe(true);
    expect(isVersionedWrite("DELETE", `/api/v1/tasks/${TASK}`)).toBe(true);
    expect(isVersionedWrite("POST", `/api/v1/tasks/${TASK}/complete`)).toBe(false);
    expect(isVersionedWrite("PUT", `/api/v1/clients/${TASK}/employee`)).toBe(false);
  });

  it("sends back the version of the last read, then forgets it after the write", async () => {
    const { fetch, requests } = scriptedFetch(
      { status: 200, etag: 'W/"v1"' },
      { status: 200 },
      { status: 200, etag: 'W/"v2"' },
    );
    const client = createBffClient("tenant", { baseUrl: ORIGIN, fetch });

    await client.GET("/api/v1/tasks/{id}", { params: { path: { id: TASK } } });
    await client.PUT("/api/v1/tasks/{id}", { params: { path: { id: TASK } }, body: task });

    expect(requests[1]!.headers.get("if-match")).toBe('W/"v1"');
    // The version is unknown after the write: the next write reads the resource first.
    await client.DELETE("/api/v1/tasks/{id}", { params: { path: { id: TASK } } });
    expect(requests[2]!.method).toBe("GET");
    expect(requests[2]!.url).toBe(`${ORIGIN}/api/bff/tasks/${TASK}`);
    expect(requests[3]!.headers.get("if-match")).toBe('W/"v2"');
  });

  it("forgets the version on 412 and keeps versions apart per tenant of the console", async () => {
    const { fetch, requests } = scriptedFetch(
      { status: 200, etag: 'W/"a"' },
      { status: 412 },
      { status: 200, etag: 'W/"b"' },
      { status: 200 },
    );
    const acme = createBffClient("platform", { baseUrl: ORIGIN, fetch, tenant: "acme" });
    const beta = createBffClient("platform", { baseUrl: ORIGIN, fetch, tenant: "beta" });
    const key = { params: { path: { id: TASK } } };

    await acme.GET("/api/v1/localization/keys/{id}", key);
    await acme.DELETE("/api/v1/localization/keys/{id}", key);
    await beta.DELETE("/api/v1/localization/keys/{id}", key);

    expect(requests[1]!.headers.get("if-match")).toBe('W/"a"');
    expect(requests[2]!.method).toBe("GET");
    expect(requests[2]!.headers.get("x-tenant")).toBe("beta");
    expect(requests[3]!.headers.get("if-match")).toBe('W/"b"');
  });

  it("leaves writes of unversioned routes alone", async () => {
    const { fetch, requests } = scriptedFetch({ status: 204 });
    const client = createBffClient("tenant", { baseUrl: ORIGIN, fetch });

    await client.POST("/api/v1/tasks/{id}/complete", { params: { path: { id: TASK } } });

    expect(requests).toHaveLength(1);
    expect(requests[0]!.headers.get("if-match")).toBeNull();
  });
});
