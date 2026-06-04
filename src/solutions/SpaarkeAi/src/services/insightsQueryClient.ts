/**
 * insightsQueryClient.ts — Frontend HTTP client for the Spaarke Insights Assistant
 * (`POST /api/insights/assistant/query`).
 *
 * Implements R5 task 025 / D2-15 — subject resolution from chat context + HTTP
 * client wiring through the EXISTING `@spaarke/auth` library. This is the
 * frontend (browser-side) consumer of the Insights endpoint, used when the
 * SpaarkeAi shell dispatches an Insights query directly (e.g., the
 * `/ask-insights` slash command or a renderer-initiated retry). The
 * BFF-side tool-handler companion (task 024) is the server-side consumer the
 * chat agent's LLM tool-call path invokes; both surfaces share the same v1.0/v1.1
 * contract.
 *
 * ───────────────────────────────────────────────────────────────────────────
 * Contract: see `projects/spaarke-ai-platform-unification-r5/notes/
 *   insights-engine-assistant-integration-brief.md` (v1.0 BINDING) +
 *   `notes/insights-engine-contract-v1.1-request.md` (v1.1 SSE + citations.href).
 *
 * Reuse mandate (R5 CLAUDE.md §3.1): this module uses the EXISTING
 * `@spaarke/auth` `authenticatedFetch` for all outbound HTTP. ZERO new auth
 * primitives, ZERO parallel HTTP wrappers, ZERO new feature flags.
 *
 * ADR-028 (Spaarke Auth v2): tokens are NEVER snapshotted. `authenticatedFetch`
 * re-acquires a fresh token from the provider per call.
 *
 * ADR-013 §3.5 (Zone B boundary): this module is a pure HTTP consumer of the
 * Insights contract. It does NOT import any server-internal types from
 * `src/server/api/...`. Response DTOs are loose `Record<string, unknown>` so
 * v1.1 forward-compat additions (`citations[].href`, etc.) survive untouched.
 *
 * ADR-019: non-2xx responses are parsed as `application/problem+json` via
 * `authenticatedFetch`'s ApiError; the 5 ProblemDetails fields are
 * re-thrown as a typed `InsightsQueryError`.
 *
 * ADR-016: 429 responses surface structurally (with `retryAfterSeconds`); this
 * client does NOT auto-retry — the chat-agent orchestration (task 029) owns
 * retry policy across the Insights surface.
 *
 * v1.1 SSE opt-in: requests send `Accept: text/event-stream`. If the server
 * is v1.0-only (returns 406 Not Acceptable), the client retries ONCE with
 * `Accept: application/json` and parses the v1.0 single-shot envelope
 * (spec NFR-11 — graceful fallback).
 *
 * Correlation-id (spec FR-17 / SC-16): every call generates a fresh
 * `x-correlation-id` via `crypto.randomUUID()` and sets it as a request header.
 * The ID is returned on both success and error so the caller can include it
 * in UX + App Insights / Kusto end-to-end correlation traces.
 *
 * @see Task 025 POML: projects/spaarke-ai-platform-unification-r5/tasks/025-subject-resolution-http-client.poml
 * @see Task 024 POML: projects/spaarke-ai-platform-unification-r5/tasks/024-insights-query-tool-handler.poml (BFF-side companion)
 * @see Task 026 POML: projects/spaarke-ai-platform-unification-r5/tasks/026-... (renderer that consumes the discriminated-union result)
 */

import { ApiError, AuthError, authenticatedFetch as defaultAuthenticatedFetch } from "@spaarke/auth";
import type { AuthenticatedFetchFn } from "@spaarke/auth";

// ---------------------------------------------------------------------------
// Public types — subject resolution
// ---------------------------------------------------------------------------

/** Contract v1.0 §3.1 subject schemes (lowercase, exhaustive in Phase 1.5). */
export type SubjectScheme = "matter" | "project" | "invoice";

/**
 * A canonical subject reference. Emitted by `resolveSubject` when the host
 * launched the SpaarkeAi shell with a known entity logical name + GUID.
 */
export interface ResolvedSubject {
  readonly kind: "subject";
  /**
   * Canonical contract format `<scheme>:<guid>`. Submitted to the Insights
   * endpoint as the binding `subject` field per integration brief §3.
   */
  readonly subject: string;
  /** Mapped contract scheme token. */
  readonly scheme: SubjectScheme;
  /** GUID with braces stripped defensively. */
  readonly entityId: string;
}

/**
 * Sentinel emitted when the SpaarkeAi shell has no entity in context (e.g.,
 * welcome stage before launch) OR when the logical name is unmapped (e.g.,
 * `sprk_account`). Callers translate this into a contract-shaped
 * `subject.required`-equivalent client-side error WITHOUT a wasted BFF
 * round-trip (per integration brief §5.1).
 */
export interface NoSubject {
  readonly kind: "no-subject";
}

// ---------------------------------------------------------------------------
// Public types — request + response
// ---------------------------------------------------------------------------

/** Request shape submitted to `POST /api/insights/assistant/query` per contract §3. */
export interface InsightsQueryRequest {
  /** Natural-language query (1..500 chars per contract; not pre-validated here). */
  query: string;
  /** Canonical `<scheme>:<guid>` subject. Caller resolves via `resolveSubject`. */
  subject: string;
  /**
   * Optional intent override per spec FR-12 / SC-17. When undefined, the field
   * is OMITTED from the request body (server runs the LLM intent classifier).
   * When set, the server skips the classifier.
   */
  forceMode?: "playbook" | "rag";
  /** AbortSignal for cancellation (DOMException AbortError on abort). */
  signal?: AbortSignal;
}

/**
 * Playbook-path result. The `envelope` is the contract v1.0/v1.1 response
 * payload forwarded unchanged (loose typing preserves forward-compat fields
 * like v1.1 `citations[].href` and unknown SSE event payloads).
 */
export interface InsightsQueryPlaybookResult {
  readonly kind: "playbook";
  readonly correlationId: string;
  readonly envelope: Record<string, unknown>;
}

/** RAG-path result. Same shape semantics as the playbook variant. */
export interface InsightsQueryRagResult {
  readonly kind: "rag";
  readonly correlationId: string;
  readonly envelope: Record<string, unknown>;
}

/**
 * Discriminated union of the two server response paths. The renderer
 * (task 026) dispatches on `kind` to the path-specific UX.
 */
export type InsightsQueryResult = InsightsQueryPlaybookResult | InsightsQueryRagResult;

// ---------------------------------------------------------------------------
// Public types — typed error
// ---------------------------------------------------------------------------

/**
 * Stable contract error codes per integration brief §5.1. Used by the renderer
 * (task 026) to dispatch per-code UX. NOTE: this is a string-union type, not a
 * runtime enum — the wire `errorCode` is forwarded verbatim from the server
 * (or synthesized for `auth.401` / `rate-limit.429`), so unknown codes from
 * future contract versions still surface structurally.
 */
export type InsightsErrorCode =
  | "query.required"
  | "subject.required"
  | "subject.invalid"
  | "forceMode.invalid"
  | "conversationContext.invalid"
  | "auth.401" // synthetic — auth exhausted after `authenticatedFetch` retries
  | "rate-limit.429" // synthetic — accompanied by retryAfterSeconds when header present
  | "ai.insights.disabled"
  | "ai.rag.disabled"
  | "ai.intent-classification.disabled"
  | "ai.assistant-default-playbook.unconfigured"
  | "INSIGHTS_ASSISTANT_INTERNAL_ERROR";

/**
 * Typed error thrown by `callInsightsQuery` on any non-2xx outcome. Preserves
 * the 5 ProblemDetails fields per ADR-019 + an optional `retryAfterSeconds`
 * for 429 surfaces.
 *
 * The error MUST NEVER carry document content, prompt text, LLM raw output,
 * or stack traces (spec NFR-12). These are stripped server-side per ADR-018.
 */
export class InsightsQueryError extends Error {
  public readonly errorCode: string;
  public readonly correlationId: string;
  public readonly status: number;
  public readonly title: string;
  public readonly detail: string;
  public readonly retryAfterSeconds?: number;

  constructor(
    errorCode: string,
    correlationId: string,
    status: number,
    title: string,
    detail: string,
    retryAfterSeconds?: number,
  ) {
    super(`[${errorCode}] ${title}: ${detail}`);
    this.name = "InsightsQueryError";
    this.errorCode = errorCode;
    this.correlationId = correlationId;
    this.status = status;
    this.title = title;
    this.detail = detail;
    this.retryAfterSeconds = retryAfterSeconds;
    Object.setPrototypeOf(this, InsightsQueryError.prototype);
  }
}

// ---------------------------------------------------------------------------
// Implementation — subject resolution
// ---------------------------------------------------------------------------

/**
 * One-way mapping from Dataverse logical name → contract v1.0 scheme token.
 * Exhaustive in Phase 1.5; any other logical name returns `no-subject`.
 */
const SCHEME_BY_LOGICAL_NAME: Readonly<Record<string, SubjectScheme>> = Object.freeze({
  sprk_matter: "matter",
  sprk_project: "project",
  sprk_invoice: "invoice",
});

/**
 * Resolve a contract-shaped subject from the SpaarkeAi launch params.
 *
 * Inputs come from `launch-resolver.ts` and flow as React props through
 * `App.tsx` / `ThreePaneShell`. The function is intentionally pure (no React
 * state, no URL parsing) so it is trivially unit-testable + can be called
 * from any frontend surface (chat-pane orchestration, slash-command handler,
 * renderer retry).
 *
 * @param entityLogicalName - Dataverse logical name (e.g., `sprk_matter`).
 *                            `undefined` when no entity in context.
 * @param entityId          - GUID with or without braces (braces stripped
 *                            defensively here even though launch-resolver
 *                            already does this).
 * @returns A `ResolvedSubject` carrying the canonical `<scheme>:<guid>`
 *          string, or a `NoSubject` sentinel when context is missing /
 *          logical name is unmapped.
 *
 * @example
 *   resolveSubject('sprk_matter', 'da116923-d65a-f111-a825-3833c5d9bcb1')
 *   // → { kind: 'subject', subject: 'matter:da116923-d65a-f111-a825-3833c5d9bcb1',
 *   //     scheme: 'matter', entityId: 'da116923-d65a-f111-a825-3833c5d9bcb1' }
 *
 * @example
 *   resolveSubject('sprk_account', 'abc-123')
 *   // → { kind: 'no-subject' } — unmapped logical name; do not coerce.
 */
export function resolveSubject(
  entityLogicalName?: string,
  entityId?: string,
): ResolvedSubject | NoSubject {
  if (!entityLogicalName || !entityId) {
    return { kind: "no-subject" };
  }
  const scheme = SCHEME_BY_LOGICAL_NAME[entityLogicalName];
  if (!scheme) {
    return { kind: "no-subject" };
  }
  // Defensive braces stripping — launch-resolver already does this, but
  // tolerate inbound `{guid}` from other call sites (e.g., direct Xrm record IDs).
  const cleanId = entityId.replace(/^\{|\}$/g, "");
  if (!cleanId) {
    return { kind: "no-subject" };
  }
  return {
    kind: "subject",
    subject: `${scheme}:${cleanId}`,
    scheme,
    entityId: cleanId,
  };
}

// ---------------------------------------------------------------------------
// Implementation — HTTP client
// ---------------------------------------------------------------------------

/** The Insights Assistant endpoint relative path. `authenticatedFetch` resolves to BFF base URL. */
const INSIGHTS_ENDPOINT = "/api/insights/assistant/query";

/**
 * SSE event tag set emitted by the contract v1.1 server. Unknown event types
 * are silently skipped (forward-compat).
 */
type SseEventType = "progress" | "metadata" | "delta" | "result" | "complete" | "error";

/**
 * Internal helper: generate a fresh correlation id via `crypto.randomUUID()`
 * (browser native, available in all R5 supported runtimes per ADR-022).
 * Falls back to a Math.random()-based id if `crypto.randomUUID` is unavailable
 * (e.g., older jsdom test environments without the API).
 */
function newCorrelationId(): string {
  if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") {
    return crypto.randomUUID();
  }
  // Test-only fallback — production browsers always have crypto.randomUUID.
  return "fallback-" + Math.random().toString(36).slice(2, 10) + "-" + Date.now().toString(36);
}

/**
 * Build the request body. `forceMode` is OMITTED when undefined so the server
 * runs the intent classifier (per contract §3.2 — distinct from explicit null).
 */
function buildBody(query: string, subject: string, forceMode?: "playbook" | "rag"): string {
  const payload: Record<string, unknown> = { query, subject };
  if (forceMode !== undefined) {
    payload.forceMode = forceMode;
  }
  return JSON.stringify(payload);
}

/**
 * Parse an SSE response body into a single resolved envelope. Aggregates
 * `delta` events (per the v1.1 RAG path) into the `answer` field, then merges
 * any `complete` / `result` / `metadata` event payloads. Unknown events
 * are silently skipped (v1.2+ forward-compat).
 *
 * NOTE: the renderer (task 026) may switch to a generator/observer-based
 * variant for progressive rendering; this baseline returns the resolved
 * envelope so the simpler-of-the-two consumers (slash command path) gets
 * a single-shot result identical in shape to the v1.0 JSON path.
 */
async function parseSseResponse(response: Response): Promise<Record<string, unknown>> {
  if (!response.body) {
    return {};
  }
  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";
  const envelope: Record<string, unknown> = {};
  const deltaAccumulator: string[] = [];

  try {
    for (;;) {
      const { value, done } = await reader.read();
      if (done) {
        break;
      }
      buffer += decoder.decode(value, { stream: true });
      const events = buffer.split(/\n\n/);
      buffer = events.pop() ?? "";
      for (const ev of events) {
        if (!ev.trim()) {
          continue;
        }
        const lines = ev.split(/\n/);
        const eventLine = lines.find((l) => l.startsWith("event: "));
        const eventType = eventLine ? eventLine.slice(7).trim() : undefined;
        const dataLine = lines.find((l) => l.startsWith("data: "));
        if (!dataLine) {
          continue;
        }
        const raw = dataLine.slice(6).trim();
        // `data: [DONE]` sentinel per contract v1.1 §2.2 — terminate gracefully.
        if (raw === "[DONE]") {
          break;
        }
        let parsed: unknown;
        try {
          parsed = JSON.parse(raw);
        } catch {
          continue;
        }
        if (!parsed || typeof parsed !== "object") {
          continue;
        }
        const data = parsed as Record<string, unknown>;
        const tag = (eventType ?? (data.type as string | undefined)) as SseEventType | undefined;
        if (tag === "delta") {
          const content = (data.content as string | undefined) ?? (data.text as string | undefined);
          if (typeof content === "string") {
            deltaAccumulator.push(content);
          }
        } else if (tag === "result") {
          // Contract v1.1 §2.2: `result` event carries the v1.0-shaped envelope
          // EITHER directly OR wrapped in a `content` field. Handle both.
          if (
            data.content
            && typeof data.content === "object"
            && !Array.isArray(data.content)
          ) {
            Object.assign(envelope, data.content as Record<string, unknown>);
          } else if (typeof data.content === "string") {
            try {
              const parsedInner = JSON.parse(data.content as string) as Record<string, unknown>;
              Object.assign(envelope, parsedInner);
            } catch {
              // Fall through and merge the outer data so the consumer
              // at least gets the metadata fields.
              Object.assign(envelope, data);
            }
          } else {
            Object.assign(envelope, data);
          }
        } else if (tag === "complete" || tag === "metadata") {
          // Merge — `complete` is the canonical final payload; metadata is enrichment.
          Object.assign(envelope, data);
        }
        // Unknown events: silently skip (forward-compat).
      }
    }
  } finally {
    try {
      reader.releaseLock();
    } catch {
      // ignore
    }
  }

  // If the server streamed `delta` events without a final `complete.answer`,
  // synthesize `answer` from the accumulator.
  if (deltaAccumulator.length > 0 && envelope.answer === undefined) {
    envelope.answer = deltaAccumulator.join("");
  }
  return envelope;
}

/**
 * Determine the discriminated-union `kind` from the envelope's `path` field.
 * Defaults to `'rag'` defensively if `path` is missing — but this is unlikely
 * in conformant servers since contract §4 makes `path` REQUIRED on success.
 */
function discriminateKind(envelope: Record<string, unknown>): "playbook" | "rag" {
  const path = envelope.path;
  if (path === "playbook") {
    return "playbook";
  }
  if (path === "rag") {
    return "rag";
  }
  return "rag";
}

/**
 * Translate an `ApiError` (from `authenticatedFetch`) into an
 * `InsightsQueryError` preserving the 5 ProblemDetails fields + 429
 * `Retry-After` enrichment. The client-generated `correlationId` is used as
 * a fallback when the server didn't include one in the ProblemDetails (which
 * should never happen for the Insights endpoint — every response sets
 * `correlationId` per §5).
 */
function toInsightsError(
  err: ApiError,
  clientCorrelationId: string,
  retryAfterSeconds: number | undefined,
): InsightsQueryError {
  const pd = err.problemDetails;
  const status = err.status;
  const serverCorrelationId = (pd?.correlationId as string | undefined) ?? clientCorrelationId;
  const errorCode = (pd?.errorCode as string | undefined)
    ?? (status === 401 ? "auth.401" : undefined)
    ?? (status === 429 ? "rate-limit.429" : undefined)
    ?? "INSIGHTS_ASSISTANT_INTERNAL_ERROR";
  const title = pd?.title ?? (status === 401 ? "Unauthorized" : status === 429 ? "Too Many Requests" : "Insights query failed");
  const detail = pd?.detail ?? err.message;
  return new InsightsQueryError(errorCode, serverCorrelationId, status, title, detail, retryAfterSeconds);
}

/**
 * Extract `Retry-After` seconds from a response (only meaningful for 429).
 * Returns undefined when header absent or unparseable.
 *
 * NOTE: `authenticatedFetch` does NOT surface the original `Response` on
 * non-2xx, so 429 enrichment relies on the BFF echoing `Retry-After` into
 * the ProblemDetails payload (it does per ADR-016 +
 * `bff-extensions.md`). For SSE 200 success this is unused.
 */
function tryReadRetryAfter(pd: Record<string, unknown> | null | undefined): number | undefined {
  if (!pd) {
    return undefined;
  }
  const raw = pd.retryAfterSeconds ?? pd["retry-after"] ?? pd["Retry-After"];
  if (typeof raw === "number" && Number.isFinite(raw)) {
    return raw;
  }
  if (typeof raw === "string") {
    const parsed = Number.parseInt(raw, 10);
    if (Number.isFinite(parsed)) {
      return parsed;
    }
  }
  return undefined;
}

// ---------------------------------------------------------------------------
// callInsightsQuery — public HTTP entry point
// ---------------------------------------------------------------------------

/**
 * Optional override for the `authenticatedFetch` function — primarily for
 * unit tests. Production callers MUST omit this and rely on the singleton
 * `authenticatedFetch` from `@spaarke/auth` so the EXISTING auth pipeline
 * (Bearer header attachment, 401 retry with backoff, ProblemDetails parsing,
 * fresh-token-per-call per ADR-028) is used.
 */
export interface InsightsQueryClientOptions {
  authenticatedFetch?: AuthenticatedFetchFn;
}

/**
 * Invoke `POST /api/insights/assistant/query` per integration brief §3.
 *
 * Flow:
 *   1. Generate a fresh `x-correlation-id` per call (spec FR-17 / SC-16).
 *   2. POST with `Accept: text/event-stream` (v1.1 SSE opt-in).
 *   3. On 406 (v1.0-only server), retry ONCE with `Accept: application/json`
 *      (spec NFR-11 graceful fallback). The correlation-id is reused.
 *   4. On 200 + `text/event-stream`, parse SSE events; resolve to envelope.
 *   5. On 200 + `application/json`, parse single-shot envelope.
 *   6. On any non-2xx (after the 406 fallback opportunity), translate
 *      `ApiError` → `InsightsQueryError` preserving the 5 ProblemDetails
 *      fields per ADR-019.
 *   7. On `AuthError` (auth exhausted after `authenticatedFetch`'s retries),
 *      throw `InsightsQueryError` with synthetic `errorCode = 'auth.401'`.
 *
 * @param request - Query payload + optional `forceMode` + optional AbortSignal.
 * @param options - Test-injection seam; production callers omit.
 * @returns Discriminated-union `InsightsQueryResult` with `correlationId`.
 * @throws InsightsQueryError on non-2xx or auth exhaustion.
 *
 * @see Integration brief §3 (request schema) / §4 (response schema) / §5 (errors).
 * @see Contract v1.1 §2 (SSE opt-in) / §3 (`citations[].href` forward-compat).
 */
export async function callInsightsQuery(
  request: InsightsQueryRequest,
  options?: InsightsQueryClientOptions,
): Promise<InsightsQueryResult> {
  const { query, subject, forceMode, signal } = request;
  const fetchFn = options?.authenticatedFetch ?? defaultAuthenticatedFetch;
  const correlationId = newCorrelationId();
  const body = buildBody(query, subject, forceMode);

  // ── First attempt: opt into v1.1 SSE ──────────────────────────────────────
  let response: Response;
  try {
    response = await fetchFn(INSIGHTS_ENDPOINT, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Accept: "text/event-stream",
        "x-correlation-id": correlationId,
      },
      body,
      signal,
    });
  } catch (err) {
    // 406 v1.0-only deployment — retry once with JSON Accept (NFR-11 fallback).
    if (err instanceof ApiError && err.status === 406) {
      response = await fetchFn(INSIGHTS_ENDPOINT, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Accept: "application/json",
          "x-correlation-id": correlationId,
        },
        body,
        signal,
      });
    } else if (err instanceof AuthError) {
      // Auth exhausted after `authenticatedFetch`'s built-in 401 retries.
      throw new InsightsQueryError(
        "auth.401",
        correlationId,
        401,
        "Unauthorized",
        err.message || "Authentication failed after retry attempts.",
      );
    } else if (err instanceof ApiError) {
      const retryAfterSeconds = err.status === 429
        ? tryReadRetryAfter(err.problemDetails as Record<string, unknown> | null)
        : undefined;
      throw toInsightsError(err, correlationId, retryAfterSeconds);
    } else if (
      err instanceof DOMException
      || (err && typeof err === "object" && (err as { name?: string }).name === "AbortError")
    ) {
      // Cancellation — propagate the native DOMException unwrapped.
      throw err;
    } else {
      throw err;
    }
  }

  // ── Parse the resolved Response based on content-type ─────────────────────
  const contentType = response.headers.get("content-type") ?? "";
  let envelope: Record<string, unknown>;
  if (contentType.startsWith("text/event-stream")) {
    envelope = await parseSseResponse(response);
  } else {
    // application/json (or any other JSON-shaped success body).
    const parsed = await response.json();
    envelope = (parsed && typeof parsed === "object") ? (parsed as Record<string, unknown>) : {};
  }

  const kind = discriminateKind(envelope);
  if (kind === "playbook") {
    return { kind: "playbook", correlationId, envelope };
  }
  return { kind: "rag", correlationId, envelope };
}
