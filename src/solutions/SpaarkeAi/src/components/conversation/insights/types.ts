/**
 * types.ts — Discriminated-union response envelope + helpers for the Insights
 * Assistant chat-tool response renderer (R5 task 026 / D2-16).
 *
 * The Insights HTTP contract returns ONE shape on every 200 OK success, but
 * with three distinct semantic cases (per integration brief §4.6):
 *
 *   - `path: 'playbook'` + `structuredResult.kind: 'inference'`
 *     → Predictive / structured envelope. Rendered via task 017's
 *       `StructuredOutputStreamWidget` (REUSE per R5 CLAUDE.md §3.1).
 *
 *   - `path: 'playbook'` + `structuredResult.kind: 'decline'`
 *     → 200 OK with a structured "no" — playbook gate-fail. Renders as a
 *       Fluent v9 `MessageBar intent="warning"` with `answer` (the
 *       `DeclineResponse.Explanation`) + plain-text `suggestedActions`
 *       (per integration brief §4.5 + §6 D3: plain strings in v1).
 *
 *   - `path: 'rag'` + `structuredResult.kind: 'observation'`
 *     → Citation-grounded RAG prose. The `answer` contains `[n]` citation
 *       tokens which we tokenize and render as Fluent buttons. Click
 *       handling is STUBBED (`console.debug`); task 027 (D2-17) wires the
 *       real PaneEventBus dispatch.
 *
 * One additional UI-only sub-case is detected at render time on the RAG
 * branch (not a distinct server response shape):
 *
 *   - "Empty result" = `path: 'rag'` AND `citations.length === 0` AND
 *     `answer.trim() === ''`. Per integration brief §4.4 anti-hallucination
 *     guarantee, the renderer MUST NOT pass the empty `answer` verbatim; it
 *     renders a muted "couldn't find anything" hint instead.
 *
 * Typed as a TypeScript discriminated union so the renderer enforces
 * exhaustive case handling via `assertNever`. The `envelope` payloads remain
 * loose (`Record<string, unknown>`) because:
 *
 *   1. ADR-013 §3.5 Zone B facade boundary — R5 must NEVER import Insights
 *      server-internal types.
 *   2. v1.1 forward-compat fields (`citations[].href`, etc.) must survive
 *      unknown-key passthrough without type changes.
 *
 * Callers building an `InsightsResponse` from the wire payload
 * (`callInsightsQuery` in `insightsQueryClient.ts`) use `fromEnvelope()` to
 * project the loose `Record<string, unknown>` into the discriminated union.
 *
 * @see projects/spaarke-ai-platform-unification-r5/notes/insights-engine-assistant-integration-brief.md §4
 * @see src/solutions/SpaarkeAi/src/services/insightsQueryClient.ts (task 025)
 * @see src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/StructuredOutputStreamWidget.tsx (task 017)
 */

// ---------------------------------------------------------------------------
// Citation — uniform shape across playbook + RAG paths (brief §4.3)
// ---------------------------------------------------------------------------

/**
 * Citation reference returned by the Insights endpoint. Uniform shape across
 * both `playbook` and `rag` paths per brief §4.3 (load-bearing UX
 * simplification).
 *
 * Citation rendering for `[n]` clickable tokens is currently STUBBED in this
 * renderer; the seam for task 027 (D2-17) lives in `RagResponseRenderer`.
 * v1.1 adds an optional `href` field for direct source-document linking; v1.0
 * has display-name-only fallback per spec FR-14.
 */
export interface Citation {
  /** 1-based citation number — matches the `[n]` token in `answer` text. */
  readonly n: number;
  /** Display name of the source (document title, observation name, etc.). */
  readonly source: string;
  /** Short excerpt (≤280 chars per contract) shown in the citation list. */
  readonly excerpt: string;
  /** Optional observation GUID (when present, used to resolve in-pane preview). */
  readonly observationId?: string;
  /** Chunk identifier within the observation's content. */
  readonly chunkId: string;
  /**
   * v1.1 optional direct link to the source document. Absent (or `null`) on
   * v1.0 deployments — renderer falls back to display-name-only mode per
   * spec FR-14. Task 027 wires the click handler that consumes this field.
   */
  readonly href?: string | null;
}

// ---------------------------------------------------------------------------
// Diagnostics — observability metadata returned on every response (brief §4)
// ---------------------------------------------------------------------------

/**
 * Observability diagnostics returned by the endpoint. The renderer does not
 * surface these directly to end-users — they exist for correlation traces,
 * task 028 (confidence-floor badge), and future debugging tooling.
 */
export interface Diagnostics {
  readonly intentSource: 'classifier' | 'forceMode' | 'classifier-fallback';
  readonly classifierBelowThreshold: boolean;
  readonly elapsedMs: number;
  readonly cacheHit: boolean;
  /**
   * Optional conversation correlation ID; when present, the playbook
   * renderer uses this as the `correlationId` on the dispatched
   * `widget_load` event so the receiving widget can disambiguate concurrent
   * structured-output streams (FR-06).
   */
  readonly conversationId?: string;
}

// ---------------------------------------------------------------------------
// Decline + RAG observation envelope shapes (loose typing — Zone B boundary)
// ---------------------------------------------------------------------------

/**
 * `DeclineResponse` shape returned in `structuredResult.envelope` when the
 * playbook's evidence-sufficiency gate fails (brief §4.5).
 *
 * Field names match the server contract (camelCase). Note: the brief uses
 * PascalCase in the JSON sample (`Explanation`, `SuggestedActions`), but
 * actual wire shape is camelCase per the Insights team's ASP.NET Core
 * serialization defaults. Callers should tolerate both — `fromEnvelope`
 * normalises before constructing the union member.
 */
export interface DeclineEnvelope {
  readonly reason: string;
  readonly minimumEvidenceNeeded?: string;
  readonly suggestedActions: readonly string[];
  readonly confidenceInDecline: number;
  /**
   * The user-facing explanation. Surfaced as the `MessageBar` body of the
   * decline UI. Mirrored in the outer envelope's `answer` field per §4.5.
   */
  readonly explanation: string;
}

/**
 * Playbook-inference envelope — the `InsightArtifact` JSON returned by a
 * predictive playbook (e.g. predict-matter-cost@v1). Kept LOOSE per
 * ADR-013 §3.5 Zone B boundary — only the shape the
 * `StructuredOutputStreamWidget`'s schema fields project against matters
 * here. Concrete envelopes per playbook are server-internal and may evolve.
 */
export type PlaybookInferenceEnvelope = Readonly<Record<string, unknown>>;

/**
 * RAG observation envelope. The `summary` field is the LLM-synthesized
 * narrative; the `results` array carries individual hit metadata (LOOSE
 * shape — server-internal).
 */
export interface RagObservationEnvelope {
  readonly results: readonly unknown[];
  readonly summary: string;
}

// ---------------------------------------------------------------------------
// Discriminated-union response — the public type the renderer dispatches on
// ---------------------------------------------------------------------------

/**
 * Common fields shared across all three success cases — kept here so the
 * union members stay narrow + readable.
 */
interface InsightsResponseBase {
  /** User-facing answer text. Empty on RAG empty-result; populated otherwise. */
  readonly answer: string;
  /** Uniform citation list (brief §4.3). Always present (possibly empty). */
  readonly citations: readonly Citation[];
  /** Confidence in [0, 1]. Used by task 028 (D2-18) confidence-floor badge. */
  readonly confidence: number;
  /** Observability diagnostics; not surfaced directly. */
  readonly diagnostics: Diagnostics;
}

/**
 * Playbook-inference success — predictive / structured envelope. Rendered via
 * task 017's `StructuredOutputStreamWidget` in static mode.
 */
export interface PlaybookInferenceResponse extends InsightsResponseBase {
  readonly path: 'playbook';
  readonly playbookId: string;
  readonly structuredResult: {
    readonly kind: 'inference';
    readonly envelope: PlaybookInferenceEnvelope;
  };
}

/**
 * Playbook-decline success — playbook gate-fail, NOT an error. Rendered as
 * a Fluent `MessageBar intent="warning"` with `suggestedActions` list.
 */
export interface PlaybookDeclineResponse extends InsightsResponseBase {
  readonly path: 'playbook';
  /** Per brief §4.5, `playbookId` may still be present on decline. */
  readonly playbookId: string;
  readonly structuredResult: {
    readonly kind: 'decline';
    readonly envelope: DeclineEnvelope;
  };
}

/**
 * RAG observation success — citation-grounded prose with `[n]` tokens.
 * Empty-result is detected at render time via `isEmptyResult` (no separate
 * union member — same server shape with empty data).
 */
export interface RagObservationResponse extends InsightsResponseBase {
  readonly path: 'rag';
  readonly playbookId: null;
  readonly structuredResult: {
    readonly kind: 'observation';
    readonly envelope: RagObservationEnvelope;
  };
}

/**
 * Discriminated union of the three success cases. The renderer
 * (`InsightsResponseRenderer`) discriminates via `path` + `structuredResult.kind`
 * and routes to the appropriate sub-renderer with exhaustiveness enforced via
 * `assertNever` defaults.
 *
 * @example
 *   function render(r: InsightsResponse) {
 *     switch (r.path) {
 *       case 'playbook':
 *         switch (r.structuredResult.kind) {
 *           case 'inference': return <PlaybookResponseRenderer response={r} />;
 *           case 'decline':   return <DeclineResponseRenderer response={r} />;
 *           default: return assertNever(r.structuredResult);
 *         }
 *       case 'rag': return <RagResponseRenderer response={r} />;
 *       default: return assertNever(r);
 *     }
 *   }
 */
export type InsightsResponse =
  | PlaybookInferenceResponse
  | PlaybookDeclineResponse
  | RagObservationResponse;

// ---------------------------------------------------------------------------
// Error variant — task 029 / D2-19 surfaces the 12 binding error codes
// ---------------------------------------------------------------------------

/**
 * Error variant of the response envelope — supplied to the renderer when the
 * HTTP client (task 025 `callInsightsQuery`) throws an `InsightsQueryError`.
 * Distinct from the three success variants above (uses `path: 'error'` as
 * the discriminant).
 *
 * Per ADR-018 (no information leakage):
 *   - The renderer ONLY surfaces `errorCode` (mapped to a user-facing message
 *     via `insightsErrorMessages.ts`) + `correlationId` (opaque ops-debugging
 *     key) + optional `retryAfterSeconds` (countdown UX for 429).
 *   - `detail` is Console-logged for diagnostics but NEVER rendered.
 *   - `title` is Console-logged but NEVER rendered.
 *   - Unknown ProblemDetails extensions in `unknownExtensions` are
 *     Console-logged but NEVER rendered (v1.1+ forward-compat).
 *
 * Per ADR-013 §3.5 (Zone B boundary): this shape is R5-local — it mirrors the
 * `InsightsQueryError` thrown by the HTTP client but is NOT imported FROM the
 * client (so the renderer module can be tested in isolation without dragging
 * the HTTP client's transitive dependencies into the test runner).
 */
export interface InsightsErrorResponse {
  readonly path: 'error';
  /**
   * The stable `errorCode` extension from the contract's ProblemDetails. One
   * of the 12 v1.0 codes OR a synthetic key (`auth.401`, `rate-limit.429`).
   * Unknown codes fall through to the generic
   * `INSIGHTS_ASSISTANT_INTERNAL_ERROR` message via `getUserMessageForErrorCode`.
   */
  readonly errorCode: string;
  /**
   * The opaque correlation-id (per-Assistant-turn ops-debugging key). Visible
   * in the rendered UI (small mono-font, copyable), surfaced for support
   * ticket lookups. Treated as untrusted display text per ADR-018.
   */
  readonly correlationId: string;
  /** HTTP status code (kept for diagnostics + Test 1 assertions; NOT rendered). */
  readonly status: number;
  /** ProblemDetails title (Console-logged for diagnostics; NEVER rendered per ADR-018). */
  readonly title: string;
  /** ProblemDetails detail (Console-logged for diagnostics; NEVER rendered per ADR-018). */
  readonly detail: string;
  /**
   * Parsed `Retry-After` header value for 429 responses (delta-seconds).
   * Drives the countdown UX. Absent for non-429 responses.
   */
  readonly retryAfterSeconds?: number;
  /**
   * Unknown ProblemDetails extensions (v1.1+ forward-compat). Console-logged
   * for diagnostics; NEVER rendered. Per ADR-019 + R5 §3.5 Zone B boundary.
   */
  readonly unknownExtensions?: Readonly<Record<string, unknown>>;
}

// ---------------------------------------------------------------------------
// Runtime guards — empty-result + decline detection
// ---------------------------------------------------------------------------

/**
 * Returns true iff the response is a RAG observation with no citations and an
 * empty answer. Per brief §4.4, the renderer MUST render a hint instead of
 * the empty `answer` (anti-hallucination guarantee).
 */
export function isEmptyResult(response: InsightsResponse): boolean {
  return (
    response.path === 'rag'
    && response.citations.length === 0
    && response.answer.trim() === ''
  );
}

/**
 * Returns true iff the response is a playbook decline (200 OK structured "no").
 * Narrows the union member to `PlaybookDeclineResponse` for downstream use.
 */
export function isDecline(
  response: InsightsResponse,
): response is PlaybookDeclineResponse {
  return (
    response.path === 'playbook'
    && response.structuredResult.kind === 'decline'
  );
}

/**
 * Returns true iff the response is a playbook inference (rendered via the
 * structured-output widget).
 */
export function isPlaybookInference(
  response: InsightsResponse,
): response is PlaybookInferenceResponse {
  return (
    response.path === 'playbook'
    && response.structuredResult.kind === 'inference'
  );
}

/** Returns true iff the response is a RAG observation. */
export function isRagObservation(
  response: InsightsResponse,
): response is RagObservationResponse {
  return response.path === 'rag';
}

/**
 * Returns true iff the input is an error variant (task 029 / D2-19). Used by
 * the top-level renderer to dispatch to `InsightsErrorRenderer` before the
 * existing success-case discrimination (decline / empty / playbook / RAG).
 *
 * Accepts a wider input type (`InsightsResponse | InsightsErrorResponse`) so
 * the renderer can call this on the union without first narrowing.
 */
export function isError(
  value: InsightsResponse | InsightsErrorResponse,
): value is InsightsErrorResponse {
  return (value as { path?: string }).path === 'error';
}

// ---------------------------------------------------------------------------
// Exhaustiveness helper
// ---------------------------------------------------------------------------

/**
 * Compile-time exhaustiveness guard. Used in `default` branches of switches
 * over `InsightsResponse.path` / `structuredResult.kind` so adding a new
 * union member without updating the renderer fails the build.
 */
export function assertNever(value: never): never {
  throw new Error(`Unhandled InsightsResponse discriminant: ${JSON.stringify(value)}`);
}

// ---------------------------------------------------------------------------
// Citation-token tokenizer — pure helper (extracted for testability)
// ---------------------------------------------------------------------------

/**
 * A single token produced by `tokenizeCitations`. The RAG renderer maps each
 * token to either a `<Text>` (for `'text'`) or a Fluent v9 `<Button>` (for
 * `'citation'`).
 */
export type AnswerToken =
  | { readonly type: 'text'; readonly content: string }
  | { readonly type: 'citation'; readonly n: number };

/**
 * Tokenize a RAG-path `answer` string into alternating text + citation
 * segments preserving original order. Citations match the regex `\[(\d+)\]`.
 *
 * Examples:
 *
 *   tokenizeCitations("Hello [1] world [2].")
 *   → [
 *       { type: 'text',     content: 'Hello ' },
 *       { type: 'citation', n: 1 },
 *       { type: 'text',     content: ' world ' },
 *       { type: 'citation', n: 2 },
 *       { type: 'text',     content: '.' },
 *     ]
 *
 *   tokenizeCitations("Plain text only.")
 *   → [{ type: 'text', content: 'Plain text only.' }]
 *
 *   tokenizeCitations("")
 *   → []
 *
 * Pure function — no React, no hooks, no DOM. Trivially unit-testable.
 */
export function tokenizeCitations(answer: string): readonly AnswerToken[] {
  if (answer.length === 0) {
    return [];
  }
  const tokens: AnswerToken[] = [];
  const pattern = /\[(\d+)\]/g;
  let lastIndex = 0;
  let match: RegExpExecArray | null;
  // Reset `lastIndex` defensively — pattern is a fresh literal but eslint
  // sometimes flags reuse. The local `let match` shadows any outer state.
  pattern.lastIndex = 0;
  while ((match = pattern.exec(answer)) !== null) {
    const matchStart = match.index;
    const matchEnd = pattern.lastIndex;
    if (matchStart > lastIndex) {
      tokens.push({
        type: 'text',
        content: answer.slice(lastIndex, matchStart),
      });
    }
    const n = Number.parseInt(match[1], 10);
    if (Number.isFinite(n)) {
      tokens.push({ type: 'citation', n });
    } else {
      // Should never happen given `\d+` capture; treat as plain text fallback.
      tokens.push({
        type: 'text',
        content: answer.slice(matchStart, matchEnd),
      });
    }
    lastIndex = matchEnd;
  }
  if (lastIndex < answer.length) {
    tokens.push({ type: 'text', content: answer.slice(lastIndex) });
  }
  return tokens;
}
