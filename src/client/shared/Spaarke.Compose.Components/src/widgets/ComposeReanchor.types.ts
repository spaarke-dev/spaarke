/**
 * ComposeReanchor.types.ts — return-from-Word re-anchoring contracts (FR-27, task 054).
 *
 * Project: spaarkeai-compose-r2, task 054 (Phase 5 Word).
 *
 * Mirrors the BFF re-anchor payload (`AnnotationReanchorService` +
 * `ComposeEndpoints.ReanchorAnnotationsResponse`) — the deterministic engine scores each prior
 * Compose anchor against the reloaded Word document into three confidence bands (≥0.85 auto /
 * 0.6–0.85 review / <0.6 orphan, + a Spike-6 ambiguity guard). The Workspace renders a summary
 * banner ("N re-anchored, M need review") + a conflict panel to resolve the flagged/orphaned
 * anchors; ambiguous anchors are flagged, NEVER silently dropped (FR-27).
 *
 * Constraints honored:
 *   - ADR-021: consuming components are Fluent v9 + dark-mode (semantic tokens).
 *   - ADR-028: the fetch hook uses `@spaarke/auth` `authenticatedFetch` (never raw Bearer).
 *   - ADR-030: the PaneEventBus signal (`compose_reanchor_ready`) is a TYPED additive discriminant
 *     on the `workspace` channel — no `any` payload; subscribers narrow on `event.type`.
 *
 * @see src/server/api/Sprk.Bff.Api/Services/Compose/AnnotationReanchorService.cs (source of truth)
 * @see ./ComposeReanchorBanner.tsx · ./ComposeReanchorConflictPanel.tsx (consumers)
 * @see ./useComposeReanchor.ts (fetch hook)
 */

import type { ComposeDocumentRef } from '../types/compose-contracts';

// ---------------------------------------------------------------------------
// Re-anchor payload (mirrors the BFF JSON — camelCase, string-enum band)
// ---------------------------------------------------------------------------

/**
 * The confidence band a prior anchor was assigned to. Serialized as a camelCase string by the BFF
 * (`CamelCaseStringEnumConverter`).
 *
 *  - `'auto'`   — ≥0.85 and unambiguous; re-anchored silently.
 *  - `'review'` — 0.6–0.85, OR a demoted near-tie; flagged for the user.
 *  - `'orphan'` — <0.6 or no viable match; surfaced, never silently dropped (FR-27).
 */
export type ReanchorBand = 'auto' | 'review' | 'orphan';

/** One prior anchor's re-anchor outcome (mirror of BFF `ReanchoredAnnotation`). */
export interface ReanchoredAnnotation {
  /** Stable id of the prior Compose anchored-annotation. */
  id: string;
  /** Annotation kind (e.g. `'comment'`, `'insertion-suggestion'`) — carried through for the UI. */
  type: string;
  /** Short, Tier-1-safe body/label preview the conflict UI shows so the user can recognize it. */
  preview?: string | null;
  /** The assigned band. */
  band: ReanchorBand;
  /** Combined confidence score in [0,1] (content-match weighted with structural proximity). */
  confidence: number;
  /** 0-based paragraph index the anchor re-landed at; `-1` for an orphan. */
  matchedParagraphIndex: number;
  /** Content-match similarity component in [0,1]. */
  contentSimilarity: number;
  /** Structural-proximity component in [0,1]. */
  structuralProximity: number;
  /** True when a competing near-tie candidate made the match ambiguous (Spike 6 §5.3). */
  ambiguous: boolean;
  /** Short preview of the matched paragraph (null for an orphan). */
  matchedParagraphPreview?: string | null;
}

/** The full re-anchor summary (mirror of BFF `ReanchorSummary`). */
export interface ReanchorSummary {
  /** SPE drive-item id the summary is for (echoed by the BFF). */
  documentSpeId?: string | null;
  /** Total prior anchors scored. */
  total: number;
  /** Count re-anchored automatically (band `'auto'`). */
  autoCount: number;
  /** Count flagged for review (band `'review'`). */
  reviewCount: number;
  /** Count orphaned (band `'orphan'`). */
  orphanCount: number;
  /** Every re-anchored annotation, in input order. */
  annotations: ReanchoredAnnotation[];
  /** ISO-8601 timestamp the summary was computed. */
  computedAtUtc: string;
}

/** BFF response envelope for `POST /api/compose/document/{id}/reanchor-annotations`. */
export interface ReanchorAnnotationsResponse {
  documentSpeId: string;
  driveId?: string | null;
  summary: ReanchorSummary;
  correlationId: string;
}

/**
 * A prior Compose anchor sent to the BFF to re-locate (mirror of BFF `PriorAnchor`). Sourced from
 * the Compose session's `anchoredAnnotations` (design §8): `textPattern` = `anchor.textPattern`,
 * `paragraphHint` = `anchor.paragraphHint`.
 */
export interface PriorAnchorInput {
  id: string;
  type: string;
  textPattern: string;
  paragraphHint: number;
  preview?: string | null;
}

// ---------------------------------------------------------------------------
// Typed PaneEventBus signal (ADR-030 — additive discriminant, no `any`)
// ---------------------------------------------------------------------------

/**
 * `compose_reanchor_ready` — additive discriminant on the `workspace` PaneEventBus channel
 * (ADR-030). Emitted by the return-from-Word flow once a re-anchor summary is available, so the
 * Workspace can surface the banner. TYPED (no `any` payload); subscribers narrow on
 * `event.type === 'compose_reanchor_ready'` before reading `summary`.
 *
 * Additive per ADR-030: existing `workspace`-channel subscribers tolerate the unknown discriminant.
 */
export interface ComposeReanchorReadyEvent {
  /** Always `'compose_reanchor_ready'`. */
  type: 'compose_reanchor_ready';
  /** Document the summary is for (Tier-1 safe pointer). */
  documentRef: ComposeDocumentRef;
  /** The banded re-anchor summary (identifiers + counts; no raw document content). */
  summary: ReanchorSummary;
  /** ChatSession id correlating this to the active Compose session. */
  sessionId: string;
  /** ISO-8601 UTC timestamp. */
  timestamp: string;
}

// ---------------------------------------------------------------------------
// Conflict-resolution intent (Workspace → host)
// ---------------------------------------------------------------------------

/**
 * How the user chose to resolve a flagged (review) or orphaned anchor in the conflict panel. The
 * panel is presentational — it emits an intent; the host applies it against Compose session state.
 *
 *  - `'accept'`  — accept the proposed re-anchor at `matchedParagraphIndex` (review anchors).
 *  - `'keep'`    — keep the annotation pending/unanchored for now (defer).
 *  - `'discard'` — drop the annotation (explicit user action — the ONLY path that removes one;
 *                  the engine never drops silently, FR-27).
 */
export type ReanchorResolution = 'accept' | 'keep' | 'discard';

/** A single resolution decision emitted by the conflict panel. */
export interface ReanchorResolutionDecision {
  annotationId: string;
  resolution: ReanchorResolution;
}
