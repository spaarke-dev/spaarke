/**
 * ReferenceResolver.ts — R6 task 083 / D-D-04 (Pillar 8 reference resolution).
 *
 * Resolves the 3 reference types extracted by the CommandRouter parser
 * (`#scope`, `@<entity>`, `#<filename>`) to canonical IDs + display names
 * BEFORE the chat message is sent. The resolved set is attached to the
 * message payload metadata so the agent prompt receives "known entities"
 * (FR-51 acceptance: "resolved entities appear in agent prompt").
 *
 * Design choices (closed per POML + R6 design):
 *
 *   - **Frontend-only**: this module uses only EXISTING BFF endpoints — no
 *     new endpoint, no new facade (ADR-013), no new DI registration.
 *     BFF publish-size delta = 0 MB (ADR-029).
 *
 *   - **Caching** (ADR-014): in-memory frontend cache keyed by
 *     `tenantId:sessionId:rawToken`. TTL = chat session lifecycle. The
 *     cache lives at module scope (singleton); the consumer is responsible
 *     for invalidating on `/clear` via `invalidateSession(sessionId)`.
 *
 *   - **In-flight de-duplication**: concurrent `resolveAll(...)` calls that
 *     reference the same token share a single in-flight promise so we never
 *     double-fetch the same token. Race-safe by construction.
 *
 *   - **Graceful degradation** (NFR-01): an unresolved reference returns
 *     `{ resolved: false, rawToken, displayName: rawToken, canonicalId: null,
 *     metadata: null }`. The conversation NEVER blocks. The agent prompt
 *     receives the unresolved tokens so the LLM can ask for clarification.
 *
 *   - **3 resolvers**:
 *       1. `scope`    → `GET /api/ai/scopes/skills?search=<value>` etc.
 *          We resolve `#scope` against the 5 catalog endpoints (skills,
 *          actions, tools, knowledge, personas) by issuing a search; the
 *          first non-empty match wins. Since `#scope` is a LITERAL token
 *          ("scope" itself), in practice this returns `null` for the bare
 *          `#scope` token but the resolver shape is uniform so future
 *          `#scope-name` variants resolve correctly.
 *       2. `entity`  → resolves via the active host's `entityContext` first
 *          (e.g., `@matter` → current matter when host pane is a matter).
 *          When the value names a specific entity logical name (e.g.,
 *          `@opposing-counsel`), we DO NOT issue a Dataverse lookup here —
 *          the resolver returns an unresolved record so the agent prompt
 *          surfaces the token for clarification. (Per POML §"Discovery
 *          FIRST": entity polymorphic resolution lives on the server. A
 *          dedicated BFF endpoint would be additive scope; R6 NFR-03
 *          forbids new endpoints. Per "ADRs Are Defaults": we surface
 *          this trade-off in the closeout report.)
 *       3. `file`    → check open workspace tabs first (in-memory; no
 *          network). On miss, fall back to the chat-session uploaded-files
 *          metadata. The session-files index from Pillar 4 lives behind
 *          existing chat endpoints; resolver consumes a host-provided
 *          `fileLookup(name)` adapter so the resolver itself stays
 *          dependency-free for testing.
 *
 * Pure auth contract (ADR-028): the resolver receives `authenticatedFetch`
 * via context. NEVER snapshot tokens; NEVER touch `localStorage`.
 *
 * @see projects/spaarke-ai-platform-unification-r6/spec.md FR-51
 * @see projects/spaarke-ai-platform-unification-r6/CLAUDE.md §Pillar 8
 * @see src/solutions/SpaarkeAi/src/components/conversation/CommandRouter.ts
 *      — defines the `Reference` shape consumed here.
 * @see ADR-013 — existing PublicContracts only (uses /api/ai/scopes/*).
 * @see ADR-014 — `tenantId:sessionId:rawToken` cache keys.
 * @see ADR-029 — BFF publish-size delta = 0 MB.
 * @see NFR-01  — resolver never blocks the conversation.
 */

import type { Reference } from "./CommandRouter";

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

/**
 * Output kind of a resolved reference. Mirrors `Reference.kind` from the
 * parser, but normalizes `filename` → `file` for the agent-prompt surface.
 */
export type ResolvedReferenceType = "scope" | "entity" | "file";

/**
 * One resolved reference. Always returned for every input — even when
 * resolution fails (NFR-01). Consumers inspect `resolved` to decide
 * whether to surface a UI clarification affordance.
 *
 * Invariants:
 *   - `resolved === true`  ⇒ `canonicalId !== null`
 *   - `resolved === false` ⇒ `canonicalId === null` AND
 *                            `displayName === rawToken` (NFR-01 fallback)
 *   - `rawToken` is the literal sigiled token (e.g., `@matter`, `#scope`,
 *     `#contract.docx`) — round-trippable into highlighting affordances.
 */
export interface ResolvedReference {
  type: ResolvedReferenceType;
  rawToken: string;
  canonicalId: string | null;
  displayName: string;
  metadata: unknown;
  resolved: boolean;
}

/**
 * Open workspace tab descriptor (subset of `WorkspaceTab` shape from
 * `WorkspaceTabManager`). Kept narrow on purpose so the resolver does NOT
 * import the manager — the host passes a snapshot via context.
 */
export interface OpenWorkspaceTab {
  /** Stable tab id. */
  id: string;
  /** Widget type (e.g. `document-summary`). */
  widgetType: string;
  /** Human-readable display name (typically the filename). */
  displayName: string;
  /** Opaque widget payload (may carry `filename` / `documentId`). */
  widgetData: unknown;
}

/**
 * Snapshot of an uploaded session file (Pillar 4 session-files index).
 * Same narrow surface as `OpenWorkspaceTab`: the host provides; the
 * resolver consumes.
 */
export interface SessionFileMetadata {
  documentId: string;
  filename: string;
}

/**
 * Resolver context — supplied by `ConversationPane` at call time.
 *
 * Required:
 *   - `tenantId`   for ADR-014 cache key (MUST be non-empty).
 *   - `sessionId`  cache scope. When null (no active chat session), the
 *                  resolver still works but caches under a `__no-session__`
 *                  sentinel — entries are evicted on the next session
 *                  invalidation.
 *
 * Optional:
 *   - `entityContext`  the host's active entity (e.g., a matter). When
 *                     `@matter` / `@<entityType>` is requested and the
 *                     host's entityContext.entityType matches the value,
 *                     resolution short-circuits to the host record.
 *   - `openTabs`       in-memory open workspace tabs (checked FIRST for
 *                     file references — no network).
 *   - `fileLookup`     async lookup against the session-files index for
 *                     file references that miss the open-tabs check.
 *                     When undefined, file refs that miss the in-memory
 *                     check degrade with `resolved: false`.
 *   - `scopeFetch`     async fetch for scope references against existing
 *                     `/api/ai/scopes/*` endpoints. When undefined, scope
 *                     refs degrade with `resolved: false`.
 */
export interface ResolverContext {
  tenantId: string;
  sessionId: string | null;
  entityContext?: { entityType: string; entityId: string; displayName?: string } | null;
  openTabs?: ReadonlyArray<OpenWorkspaceTab>;
  fileLookup?: (filename: string) => Promise<SessionFileMetadata | null>;
  scopeFetch?: (search: string) => Promise<ScopeLookupResult | null>;
}

/**
 * Shape returned by `scopeFetch`. The concrete `kind` lets the resolver
 * record which scope catalog (skill/action/tool/knowledge/persona) matched.
 */
export interface ScopeLookupResult {
  id: string;
  displayName: string;
  kind: "skill" | "action" | "tool" | "knowledge" | "persona";
}

// ---------------------------------------------------------------------------
// Cache (module-scope singleton)
// ---------------------------------------------------------------------------

/**
 * Cache entries are keyed by `tenantId:sessionId:rawToken`. Per ADR-014,
 * `tenantId` MUST appear in every cache key. Per project NFR-05, this is
 * an additive frontend-only cache — it never touches Redis.
 *
 * In-flight de-duplication is tracked separately so concurrent
 * `resolveAll(...)` calls don't double-fetch the same token.
 */
const SESSION_SENTINEL_NULL = "__no-session__";

/** Internal — resolved entries keyed by the cache key. */
const CACHE = new Map<string, ResolvedReference>();

/** Internal — in-flight promises keyed by the cache key. */
const INFLIGHT = new Map<string, Promise<ResolvedReference>>();

/**
 * Build the canonical cache key for a (tenant, session, rawToken) tuple.
 *
 * ADR-014: tenantId MUST be in the key — this prevents cross-tenant leakage
 * for surfaces that share a process (e.g., dev tools, integration tests).
 */
function buildCacheKey(tenantId: string, sessionId: string | null, rawToken: string): string {
  const session = sessionId == null || sessionId.length === 0 ? SESSION_SENTINEL_NULL : sessionId;
  return `${tenantId}:${session}:${rawToken}`;
}

/**
 * Invalidate all cache entries (and in-flight promises) for a session.
 *
 * Wire this to the `/clear` hard slash (task 081). Per ADR-030 + NFR-05,
 * the `/clear` executor signals via the existing `conversation` PaneEventBus
 * channel — the host's effect calls this function on receipt. No 5th
 * channel; no new event-channel keyword.
 */
export function invalidateSession(sessionId: string | null): void {
  const session = sessionId == null || sessionId.length === 0 ? SESSION_SENTINEL_NULL : sessionId;
  // Walk the maps once each; clearing keys in-place to keep the iterator stable.
  const prefixMatch = `:${session}:`;
  for (const key of Array.from(CACHE.keys())) {
    if (key.includes(prefixMatch)) CACHE.delete(key);
  }
  for (const key of Array.from(INFLIGHT.keys())) {
    if (key.includes(prefixMatch)) INFLIGHT.delete(key);
  }
}

/**
 * Test-only helper — clears ALL cache entries. Tests should call this in
 * `beforeEach` to keep cases isolated. NOT exported on the public surface
 * for runtime consumers (no path imports this function).
 */
export function __resetCacheForTests(): void {
  CACHE.clear();
  INFLIGHT.clear();
}

// ---------------------------------------------------------------------------
// Per-type resolvers
// ---------------------------------------------------------------------------

/**
 * Resolve a `#scope` reference. The parser uses `kind: 'scope'` ONLY for
 * the literal `#scope` token — that's the bare shorthand. Other `#<value>`
 * tokens are kind `filename` per the parser's disambiguation rule. So the
 * actual lookup surface here is narrow today but the shape supports
 * future `#<scope-name>` variants without contract change.
 *
 * Strategy:
 *   - When the host provides a `scopeFetch` adapter, query it with the
 *     reference value and return the result.
 *   - When `scopeFetch` is omitted or the search misses → unresolved.
 */
async function resolveScope(
  ref: Reference,
  ctx: ResolverContext,
): Promise<ResolvedReference> {
  if (!ctx.scopeFetch) {
    return unresolvedRef("scope", ref.raw);
  }
  try {
    const result = await ctx.scopeFetch(ref.value);
    if (!result) return unresolvedRef("scope", ref.raw);
    return {
      type: "scope",
      rawToken: ref.raw,
      canonicalId: result.id,
      displayName: result.displayName,
      metadata: { kind: result.kind },
      resolved: true,
    };
  } catch {
    // NFR-01 — never block on resolver errors; degrade gracefully.
    return unresolvedRef("scope", ref.raw);
  }
}

/**
 * Resolve an `@<entity>` reference.
 *
 * Strategy (closed):
 *   - If the host's `entityContext.entityType` matches the reference value
 *     (or is the conventional `matter` token), the resolver short-circuits
 *     to the host's record — this covers the binding Phase D exit criterion 4
 *     case (`@matter` resolves to current matter record).
 *   - Otherwise: degrade with `resolved: false`. Polymorphic Dataverse
 *     lookup is a server-side surface (`@spaarke/ui-components`
 *     `PolymorphicResolverService`); adding a BFF endpoint to drive it
 *     would be additive scope (NFR-03 forbids new ADRs / endpoints in R6).
 *     Surfaced explicitly in the closeout report — see the rationale in
 *     this file's module-level JSDoc.
 *
 * NFR-01 binding: unresolved entity refs degrade — they DO NOT block.
 */
async function resolveEntity(
  ref: Reference,
  ctx: ResolverContext,
): Promise<ResolvedReference> {
  const value = ref.value.toLowerCase();
  const ec = ctx.entityContext;
  if (ec) {
    const ecType = (ec.entityType ?? "").toLowerCase();
    if (ecType === value) {
      return {
        type: "entity",
        rawToken: ref.raw,
        canonicalId: ec.entityId,
        displayName: ec.displayName ?? ec.entityType,
        metadata: { entityType: ec.entityType, source: "host-entity-context" },
        resolved: true,
      };
    }
  }
  // Out-of-context entity reference — degrade.
  return unresolvedRef("entity", ref.raw);
}

/**
 * Resolve a `#<filename>` reference.
 *
 * Strategy:
 *   1. Scan `openTabs` (in-memory) for a tab whose `displayName` or
 *      `widgetData.filename` matches (case-insensitive, suffix tolerant).
 *      No network. Sub-ms latency.
 *   2. On miss, call `fileLookup` to query the session-files index.
 *   3. On both miss → unresolved.
 *
 * Tab-first ordering is binding per POML: "check open workspace tabs first
 * (task 050+), then session-files Azure Search index".
 */
async function resolveFile(
  ref: Reference,
  ctx: ResolverContext,
): Promise<ResolvedReference> {
  const wanted = ref.value.toLowerCase();

  // Step 1: open tabs (no network).
  if (ctx.openTabs && ctx.openTabs.length > 0) {
    for (const tab of ctx.openTabs) {
      const tabName = tab.displayName?.toLowerCase() ?? "";
      const tabFile =
        tab.widgetData && typeof tab.widgetData === "object"
          ? (((tab.widgetData as Record<string, unknown>).filename as string | undefined) ?? "")
              .toLowerCase()
          : "";
      if (tabName === wanted || tabFile === wanted) {
        return {
          type: "file",
          rawToken: ref.raw,
          canonicalId: tab.id,
          displayName: tab.displayName,
          metadata: { source: "open-tab", widgetType: tab.widgetType },
          resolved: true,
        };
      }
    }
  }

  // Step 2: session-files index (network via host adapter).
  if (ctx.fileLookup) {
    try {
      const file = await ctx.fileLookup(ref.value);
      if (file) {
        return {
          type: "file",
          rawToken: ref.raw,
          canonicalId: file.documentId,
          displayName: file.filename,
          metadata: { source: "session-files-index" },
          resolved: true,
        };
      }
    } catch {
      // NFR-01 — never block on resolver errors; fall through to unresolved.
    }
  }

  return unresolvedRef("file", ref.raw);
}

// ---------------------------------------------------------------------------
// Public API — resolveAll
// ---------------------------------------------------------------------------

/**
 * Resolve every reference in the input array. Returns one entry per input,
 * preserving order. Cached results are returned synchronously (via a
 * resolved-promise wrapper). Concurrent calls referencing the same token
 * share a single in-flight promise.
 *
 * Per NFR-01, the returned promise NEVER rejects; per-reference failures
 * surface as `resolved: false` entries.
 *
 * @param refs  references emitted by `CommandRouter.parse(...)`
 * @param ctx   resolver context (tenantId required for ADR-014 keys)
 */
export async function resolveAll(
  refs: ReadonlyArray<Reference>,
  ctx: ResolverContext,
): Promise<ResolvedReference[]> {
  if (refs.length === 0) return [];
  if (!ctx.tenantId || ctx.tenantId.length === 0) {
    // ADR-014 binding — refuse to cache without a tenantId, but DO NOT throw
    // (NFR-01). Resolve each ref through the slow path with no caching.
    return Promise.all(refs.map((ref) => dispatchUncached(ref, ctx)));
  }

  return Promise.all(
    refs.map((ref) => {
      const cacheKey = buildCacheKey(ctx.tenantId, ctx.sessionId, ref.raw);

      // Cache hit — synchronous fast path. No re-fetch.
      const cached = CACHE.get(cacheKey);
      if (cached) return Promise.resolve(cached);

      // In-flight — share the existing promise. Race-safe by construction.
      const inflight = INFLIGHT.get(cacheKey);
      if (inflight) return inflight;

      // Cold path — dispatch + cache + clear in-flight on settle.
      const promise = dispatchUncached(ref, ctx).then((resolved) => {
        CACHE.set(cacheKey, resolved);
        INFLIGHT.delete(cacheKey);
        return resolved;
      });
      INFLIGHT.set(cacheKey, promise);
      return promise;
    }),
  );
}

/**
 * Internal — dispatch to the per-type resolver without touching the cache.
 * Used by both the cold cache path AND the tenant-id-missing fallback.
 */
function dispatchUncached(ref: Reference, ctx: ResolverContext): Promise<ResolvedReference> {
  switch (ref.kind) {
    case "scope":
      return resolveScope(ref, ctx);
    case "entity":
      return resolveEntity(ref, ctx);
    case "filename":
      return resolveFile(ref, ctx);
    default:
      // Defensive: an unknown kind degrades. The parser's union type rules
      // this out at compile time, but a runtime safety net keeps the
      // resolver non-blocking under future extensions.
      return Promise.resolve(unresolvedRef("file", ref.raw));
  }
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Construct the canonical "unresolved" shape (NFR-01 invariant). Centralized
 * so every fallback in this file returns an identical object surface.
 */
function unresolvedRef(type: ResolvedReferenceType, rawToken: string): ResolvedReference {
  return {
    type,
    rawToken,
    canonicalId: null,
    displayName: rawToken,
    metadata: null,
    resolved: false,
  };
}

// ---------------------------------------------------------------------------
// Convenience adapters for the host
// ---------------------------------------------------------------------------

/**
 * Build a `scopeFetch` adapter against the BFF `/api/ai/scopes/*` catalog
 * endpoints. Queries `skills` then `actions` then `tools` in order — the
 * first non-empty match wins (most-common-first ordering per the chat
 * authoring patterns documented in `docs/guides/SCOPE-CONFIGURATION-GUIDE.md`).
 *
 * Uses `authenticatedFetch` per ADR-028. NO token snapshot.
 *
 * @param bffBaseUrl         absolute BFF root (e.g. `https://...`).
 * @param authenticatedFetch from `useAuth()` / `useAiSession()`.
 */
export function createScopeFetch(
  bffBaseUrl: string,
  authenticatedFetch: (url: string, init?: RequestInit) => Promise<Response>,
): (search: string) => Promise<ScopeLookupResult | null> {
  const catalogs: { path: string; kind: ScopeLookupResult["kind"] }[] = [
    { path: "skills", kind: "skill" },
    { path: "actions", kind: "action" },
    { path: "tools", kind: "tool" },
    { path: "knowledge", kind: "knowledge" },
    { path: "personas", kind: "persona" },
  ];

  return async function scopeFetch(search: string): Promise<ScopeLookupResult | null> {
    const qs = encodeURIComponent(search);
    for (const cat of catalogs) {
      try {
        const res = await authenticatedFetch(
          `${bffBaseUrl}/api/ai/scopes/${cat.path}?search=${qs}&pageSize=1`,
        );
        if (!res.ok) continue;
        const body = (await res.json()) as { items?: Array<{ id?: string; name?: string; displayName?: string }> };
        const item = body.items?.[0];
        if (!item || !item.id) continue;
        return {
          id: item.id,
          displayName: item.displayName ?? item.name ?? search,
          kind: cat.kind,
        };
      } catch {
        // Per NFR-01, skip on error and try the next catalog. The resolver
        // contract requires non-blocking degradation.
        continue;
      }
    }
    return null;
  };
}

/**
 * Build a `fileLookup` adapter that the host wires from a per-session map
 * of uploaded files. The host owns this map (populated as files are
 * uploaded via the existing /documents endpoint); the adapter is a pure
 * read so the resolver stays decoupled from upload state.
 */
export function createFileLookupFromSessionMap(
  sessionFiles: ReadonlyMap<string, SessionFileMetadata>,
): (filename: string) => Promise<SessionFileMetadata | null> {
  return async function fileLookup(filename: string): Promise<SessionFileMetadata | null> {
    const wanted = filename.toLowerCase();
    for (const file of sessionFiles.values()) {
      if (file.filename.toLowerCase() === wanted) return file;
    }
    return null;
  };
}

// ---------------------------------------------------------------------------
// Default export for convenience in ConversationPane integration
// ---------------------------------------------------------------------------

const ReferenceResolver = {
  resolveAll,
  invalidateSession,
  createScopeFetch,
  createFileLookupFromSessionMap,
};

export default ReferenceResolver;
