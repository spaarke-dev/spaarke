/**
 * ReferenceResolver.test.ts — R6 task 083 / D-D-04.
 *
 * Tests the references resolver per the POML acceptance criteria + CLAUDE.md
 * NFR-01 (non-blocking) + ADR-014 (tenantId in cache keys) contracts.
 */

import {
  resolveAll,
  invalidateSession,
  createScopeFetch,
  createFileLookupFromSessionMap,
  __resetCacheForTests,
  type ResolverContext,
  type OpenWorkspaceTab,
  type SessionFileMetadata,
  type ScopeLookupResult,
} from "./ReferenceResolver";
import type { Reference } from "./CommandRouter";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const TENANT_A = "tenant-aaaa-1111";
const TENANT_B = "tenant-bbbb-2222";
const SESSION_A = "session-xxxx-1111";

function mkScope(value: string, raw?: string): Reference {
  return { kind: "scope", value, raw: raw ?? `#${value}` };
}
function mkEntity(value: string, raw?: string): Reference {
  return { kind: "entity", value, raw: raw ?? `@${value}` };
}
function mkFile(value: string, raw?: string): Reference {
  return { kind: "filename", value, raw: raw ?? `#${value}` };
}

function ctxFor(overrides: Partial<ResolverContext> = {}): ResolverContext {
  return {
    tenantId: TENANT_A,
    sessionId: SESSION_A,
    ...overrides,
  };
}

beforeEach(() => {
  __resetCacheForTests();
});

// ---------------------------------------------------------------------------
// Empty + invariant baselines
// ---------------------------------------------------------------------------

describe("resolveAll — baseline behaviour", () => {
  it("returns an empty array for empty input", async () => {
    const out = await resolveAll([], ctxFor());
    expect(out).toEqual([]);
  });

  it("never rejects when the host degrades all paths", async () => {
    // No scopeFetch, no fileLookup, no entityContext, no openTabs.
    const refs = [mkScope("scope"), mkEntity("matter"), mkFile("contract.docx")];
    const out = await resolveAll(refs, ctxFor());
    expect(out).toHaveLength(3);
    for (const r of out) {
      expect(r.resolved).toBe(false);
      expect(r.canonicalId).toBeNull();
      expect(r.displayName).toBe(r.rawToken);
      expect(r.metadata).toBeNull();
    }
  });
});

// ---------------------------------------------------------------------------
// Per-type resolution
// ---------------------------------------------------------------------------

describe("scope resolution", () => {
  it("resolves via scopeFetch when host provides the adapter", async () => {
    const scopeFetch = jest.fn(
      async (): Promise<ScopeLookupResult | null> => ({
        id: "skill-summarize-action",
        displayName: "Summarize Action",
        kind: "skill",
      }),
    );
    const out = await resolveAll([mkScope("scope")], ctxFor({ scopeFetch }));
    expect(out[0]).toEqual({
      type: "scope",
      rawToken: "#scope",
      canonicalId: "skill-summarize-action",
      displayName: "Summarize Action",
      metadata: { kind: "skill" },
      resolved: true,
    });
    expect(scopeFetch).toHaveBeenCalledWith("scope");
  });

  it("degrades when the scopeFetch returns null", async () => {
    const scopeFetch = jest.fn(async () => null);
    const out = await resolveAll([mkScope("scope")], ctxFor({ scopeFetch }));
    expect(out[0].resolved).toBe(false);
    expect(out[0].canonicalId).toBeNull();
  });

  it("degrades when scopeFetch throws (NFR-01 non-blocking)", async () => {
    const scopeFetch = jest.fn(async () => {
      throw new Error("simulated upstream error");
    });
    const out = await resolveAll([mkScope("scope")], ctxFor({ scopeFetch }));
    expect(out[0].resolved).toBe(false);
  });
});

describe("entity resolution", () => {
  it("resolves @matter to the host entity context", async () => {
    const out = await resolveAll(
      [mkEntity("matter")],
      ctxFor({
        entityContext: { entityType: "matter", entityId: "m-1234", displayName: "Acme v. Beta" },
      }),
    );
    expect(out[0]).toEqual({
      type: "entity",
      rawToken: "@matter",
      canonicalId: "m-1234",
      displayName: "Acme v. Beta",
      metadata: { entityType: "matter", source: "host-entity-context" },
      resolved: true,
    });
  });

  it("degrades when @<other> doesn't match the host entityContext", async () => {
    const out = await resolveAll(
      [mkEntity("opposing-counsel")],
      ctxFor({
        entityContext: { entityType: "matter", entityId: "m-1234" },
      }),
    );
    expect(out[0].resolved).toBe(false);
    expect(out[0].canonicalId).toBeNull();
    expect(out[0].displayName).toBe("@opposing-counsel");
  });

  it("degrades when entityContext is null", async () => {
    const out = await resolveAll([mkEntity("matter")], ctxFor({ entityContext: null }));
    expect(out[0].resolved).toBe(false);
  });

  it("is case-insensitive on the @<entityType> match", async () => {
    const out = await resolveAll(
      [mkEntity("MATTER", "@MATTER")],
      ctxFor({ entityContext: { entityType: "Matter", entityId: "m-9" } }),
    );
    expect(out[0].resolved).toBe(true);
    expect(out[0].canonicalId).toBe("m-9");
  });
});

describe("file resolution", () => {
  const tab: OpenWorkspaceTab = {
    id: "wstab-7-document-summary",
    widgetType: "document-summary",
    displayName: "contract.docx",
    widgetData: { filename: "contract.docx" },
  };

  it("resolves a file ref via open workspace tabs (no network)", async () => {
    const fileLookup = jest.fn(async () => null);
    const out = await resolveAll(
      [mkFile("contract.docx")],
      ctxFor({ openTabs: [tab], fileLookup }),
    );
    expect(out[0].resolved).toBe(true);
    expect(out[0].canonicalId).toBe("wstab-7-document-summary");
    expect(out[0].displayName).toBe("contract.docx");
    expect(out[0].metadata).toEqual({ source: "open-tab", widgetType: "document-summary" });
    expect(fileLookup).not.toHaveBeenCalled(); // tab-first wins
  });

  it("falls back to fileLookup when no open-tab matches", async () => {
    const fileLookup = jest.fn(
      async (): Promise<SessionFileMetadata | null> => ({
        documentId: "doc-abc",
        filename: "motion-to-dismiss.pdf",
      }),
    );
    const out = await resolveAll(
      [mkFile("motion-to-dismiss.pdf")],
      ctxFor({ openTabs: [tab], fileLookup }),
    );
    expect(out[0].resolved).toBe(true);
    expect(out[0].canonicalId).toBe("doc-abc");
    expect(out[0].metadata).toEqual({ source: "session-files-index" });
    expect(fileLookup).toHaveBeenCalledWith("motion-to-dismiss.pdf");
  });

  it("degrades when neither tabs nor fileLookup match", async () => {
    const fileLookup = jest.fn(async () => null);
    const out = await resolveAll(
      [mkFile("unknown.txt")],
      ctxFor({ openTabs: [tab], fileLookup }),
    );
    expect(out[0].resolved).toBe(false);
    expect(out[0].canonicalId).toBeNull();
  });

  it("degrades silently when fileLookup throws (NFR-01)", async () => {
    const fileLookup = jest.fn(async () => {
      throw new Error("simulated 500");
    });
    const out = await resolveAll([mkFile("anything.pdf")], ctxFor({ fileLookup }));
    expect(out[0].resolved).toBe(false);
  });

  it("matches case-insensitively on the filename", async () => {
    const out = await resolveAll(
      [mkFile("CONTRACT.DOCX", "#CONTRACT.DOCX")],
      ctxFor({ openTabs: [tab] }),
    );
    expect(out[0].resolved).toBe(true);
    expect(out[0].canonicalId).toBe(tab.id);
  });
});

// ---------------------------------------------------------------------------
// Caching
// ---------------------------------------------------------------------------

describe("caching", () => {
  it("does not re-fetch on a cache hit (scope)", async () => {
    const scopeFetch = jest.fn(
      async (): Promise<ScopeLookupResult | null> => ({
        id: "x",
        displayName: "X",
        kind: "skill",
      }),
    );
    const ctx = ctxFor({ scopeFetch });
    await resolveAll([mkScope("scope")], ctx);
    await resolveAll([mkScope("scope")], ctx);
    await resolveAll([mkScope("scope")], ctx);
    expect(scopeFetch).toHaveBeenCalledTimes(1);
  });

  it("does not re-fetch on a cache hit (file)", async () => {
    const fileLookup = jest.fn(
      async (): Promise<SessionFileMetadata | null> => ({
        documentId: "d",
        filename: "f.pdf",
      }),
    );
    const ctx = ctxFor({ fileLookup });
    await resolveAll([mkFile("f.pdf")], ctx);
    await resolveAll([mkFile("f.pdf")], ctx);
    expect(fileLookup).toHaveBeenCalledTimes(1);
  });

  it("invalidateSession clears entries for that session only", async () => {
    const scopeFetch = jest.fn(
      async (): Promise<ScopeLookupResult | null> => ({
        id: "s",
        displayName: "S",
        kind: "skill",
      }),
    );
    const ctx = ctxFor({ scopeFetch });

    await resolveAll([mkScope("scope")], ctx); // populate cache
    expect(scopeFetch).toHaveBeenCalledTimes(1);

    invalidateSession(SESSION_A);

    await resolveAll([mkScope("scope")], ctx);
    expect(scopeFetch).toHaveBeenCalledTimes(2);
  });

  it("invalidateSession does not clear entries for OTHER sessions", async () => {
    const scopeFetch = jest.fn(
      async (): Promise<ScopeLookupResult | null> => ({
        id: "s",
        displayName: "S",
        kind: "skill",
      }),
    );
    await resolveAll([mkScope("scope")], ctxFor({ sessionId: "session-A", scopeFetch }));
    await resolveAll([mkScope("scope")], ctxFor({ sessionId: "session-B", scopeFetch }));
    expect(scopeFetch).toHaveBeenCalledTimes(2);

    invalidateSession("session-A");

    await resolveAll([mkScope("scope")], ctxFor({ sessionId: "session-B", scopeFetch }));
    expect(scopeFetch).toHaveBeenCalledTimes(2); // session-B cache survived

    await resolveAll([mkScope("scope")], ctxFor({ sessionId: "session-A", scopeFetch }));
    expect(scopeFetch).toHaveBeenCalledTimes(3); // session-A cache cleared
  });

  it("isolates cache by tenantId (ADR-014)", async () => {
    const scopeFetch = jest.fn(
      async (): Promise<ScopeLookupResult | null> => ({
        id: "x",
        displayName: "X",
        kind: "skill",
      }),
    );

    await resolveAll([mkScope("scope")], ctxFor({ tenantId: TENANT_A, scopeFetch }));
    await resolveAll([mkScope("scope")], ctxFor({ tenantId: TENANT_B, scopeFetch }));

    // Same rawToken+sessionId, different tenantId → DIFFERENT cache keys.
    expect(scopeFetch).toHaveBeenCalledTimes(2);
  });

  it("skips caching but still resolves when tenantId is missing", async () => {
    const scopeFetch = jest.fn(
      async (): Promise<ScopeLookupResult | null> => ({
        id: "y",
        displayName: "Y",
        kind: "skill",
      }),
    );
    const ctx: ResolverContext = { tenantId: "", sessionId: SESSION_A, scopeFetch };

    await resolveAll([mkScope("scope")], ctx);
    await resolveAll([mkScope("scope")], ctx);
    // No caching when tenantId is empty (ADR-014 fallback).
    expect(scopeFetch).toHaveBeenCalledTimes(2);
  });
});

// ---------------------------------------------------------------------------
// Race / concurrent calls
// ---------------------------------------------------------------------------

describe("concurrency", () => {
  it("does not double-fetch the same token across concurrent resolveAll calls", async () => {
    // Build a manually-resolvable promise so we can hold the resolution open
    // while issuing the second call. This proves the in-flight de-dup.
    let resolveFetch: (val: ScopeLookupResult | null) => void = () => undefined;
    const scopeFetch = jest.fn(
      () =>
        new Promise<ScopeLookupResult | null>((resolve) => {
          resolveFetch = resolve;
        }),
    );

    const ctx = ctxFor({ scopeFetch });
    const callA = resolveAll([mkScope("scope")], ctx);
    const callB = resolveAll([mkScope("scope")], ctx);

    // Only one underlying scopeFetch invocation despite two concurrent callers.
    expect(scopeFetch).toHaveBeenCalledTimes(1);

    resolveFetch({ id: "shared", displayName: "Shared", kind: "skill" });
    const [resA, resB] = await Promise.all([callA, callB]);
    expect(resA[0]).toEqual(resB[0]);
    expect(scopeFetch).toHaveBeenCalledTimes(1);
  });
});

// ---------------------------------------------------------------------------
// Mixed message (closes Phase D exit criterion 4)
// ---------------------------------------------------------------------------

describe("mixed resolved + unresolved in one message", () => {
  it("handles a composition like `/draft response to @opposing-counsel about #motion-to-dismiss.pdf`", async () => {
    const tab: OpenWorkspaceTab = {
      id: "wstab-9-document-summary",
      widgetType: "document-summary",
      displayName: "motion-to-dismiss.pdf",
      widgetData: { filename: "motion-to-dismiss.pdf" },
    };

    const refs: Reference[] = [
      mkEntity("opposing-counsel"),
      mkFile("motion-to-dismiss.pdf"),
    ];

    const out = await resolveAll(
      refs,
      ctxFor({
        // Host entity context is a matter — `@opposing-counsel` does NOT match
        // and degrades gracefully (NFR-01).
        entityContext: { entityType: "matter", entityId: "m-x" },
        openTabs: [tab],
      }),
    );

    expect(out).toHaveLength(2);
    expect(out[0].resolved).toBe(false); // entity degraded
    expect(out[0].displayName).toBe("@opposing-counsel");
    expect(out[1].resolved).toBe(true); // file resolved via open tab
    expect(out[1].canonicalId).toBe(tab.id);
  });
});

// ---------------------------------------------------------------------------
// Adapter helpers
// ---------------------------------------------------------------------------

describe("createScopeFetch adapter", () => {
  /**
   * Minimal Response-shaped mock. `jest-environment-jsdom` does NOT polyfill
   * the global `Response` constructor, so tests construct lightweight mocks
   * that satisfy the `ok` getter + async `json()` method consumed by the
   * adapter. This is sufficient — the adapter intentionally calls only
   * `res.ok` and `await res.json()`.
   */
  function mockRes(status: number, body: unknown): Response {
    return {
      ok: status >= 200 && status < 300,
      status,
      json: async () => body,
    } as unknown as Response;
  }

  it("queries each catalog in order and returns the first non-empty match", async () => {
    const calls: string[] = [];
    const auth = jest.fn(async (url: string) => {
      calls.push(url);
      // Skills returns empty; actions returns a hit.
      if (url.includes("/skills?")) return mockRes(200, { items: [] });
      if (url.includes("/actions?")) {
        return mockRes(200, { items: [{ id: "act-1", displayName: "Action One" }] });
      }
      return mockRes(200, { items: [] });
    });

    const scopeFetch = createScopeFetch("https://bff", auth);
    const result = await scopeFetch("summarize-action");

    expect(result).toEqual({
      id: "act-1",
      displayName: "Action One",
      kind: "action",
    });
    expect(calls[0]).toContain("/skills?");
    expect(calls[1]).toContain("/actions?");
    // Should short-circuit before tools/knowledge/personas.
    expect(calls).toHaveLength(2);
  });

  it("returns null when every catalog misses", async () => {
    const auth = jest.fn(async () => mockRes(200, { items: [] }));
    const scopeFetch = createScopeFetch("https://bff", auth);
    const result = await scopeFetch("no-such-thing");
    expect(result).toBeNull();
    expect(auth).toHaveBeenCalledTimes(5); // 5 catalogs
  });

  it("skips a catalog that 5xxs and tries the next (non-blocking)", async () => {
    const auth = jest.fn(async (url: string) => {
      if (url.includes("/skills?")) return mockRes(500, null);
      if (url.includes("/actions?")) {
        return mockRes(200, { items: [{ id: "act-2", displayName: "Action Two" }] });
      }
      return mockRes(200, { items: [] });
    });
    const scopeFetch = createScopeFetch("https://bff", auth);
    const result = await scopeFetch("x");
    expect(result?.id).toBe("act-2");
  });
});

describe("createFileLookupFromSessionMap adapter", () => {
  it("returns the matching file (case-insensitive)", async () => {
    const map = new Map<string, SessionFileMetadata>([
      ["d1", { documentId: "d1", filename: "Contract.docx" }],
      ["d2", { documentId: "d2", filename: "engagement-letter.pdf" }],
    ]);
    const lookup = createFileLookupFromSessionMap(map);
    const result = await lookup("contract.docx");
    expect(result).toEqual({ documentId: "d1", filename: "Contract.docx" });
  });

  it("returns null on miss", async () => {
    const lookup = createFileLookupFromSessionMap(new Map());
    const result = await lookup("nothing.pdf");
    expect(result).toBeNull();
  });
});
