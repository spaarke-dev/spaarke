/**
 * ComposeWorkspace.tsx — workspace-level orchestrator for the Spaarke Compose surface.
 *
 * Project:   spaarkeai-compose-r1
 * Tasks:     042 (W5)  — initial orchestrator
 *            050 (W6)  — checkout-on-mount (POST /api/documents/{id}/checkout)
 *            051 (W7)  — multi-tab UX (BroadcastChannel + ConflictDialog + handlers)
 *            R2/R3 refactor — decompose 1795 LOC → ~400 LOC + 3 hooks; FU-1 heartbeat-gate fix
 *
 * Purpose:
 *   Composes the three Compose Phase-4 surfaces into a single mountable widget:
 *   - W4-045 `ComposeEditor`     (shared lib `@spaarke/compose-components`)
 *   - W4-043 `ComposeToolbar`    (SpaarkeAi solution, sibling file)
 *   - W4-044 `ComposeEmptyState` (SpaarkeAi solution, sibling file)
 *
 *   The workspace owns the document-context state machine, the BFF load/save
 *   wiring, and the PaneEventBus subscribers for Flows 1/2/5 per
 *   `COMPOSE_FLOW_RECEIVER_MATRIX`. The multi-tab UX (BroadcastChannel) +
 *   checkout lifecycle (probe + acquire + conflict resolution) + heartbeat
 *   (gated on `checkoutStatus === 'acquired'`) are extracted to three hooks
 *   under `./hooks/`.
 *
 * Refactor history:
 *   - W5-042 / W6-050 / W7-051: 1795 LOC monolith (orchestrator + checkout +
 *     broadcast + conflict handlers + heartbeat duplicated in ComposeEditor).
 *   - R2/R3 refactor (this version): decomposed to ~400 LOC orchestrator +
 *     3 hooks (`useComposeBroadcastChannel`, `useComposeCheckoutLifecycle`,
 *     `useComposeHeartbeatGate`). FU-1 heartbeat-gate bug fixed by hoisting
 *     the heartbeat to this workspace level and gating on
 *     `checkoutStatus === 'acquired'`. Behaviour is otherwise preserved.
 *
 * Constraints honored (BINDING):
 *   - ADR-021: Fluent v9 only; `makeStyles` + `tokens.*` (semantic).
 *   - ADR-022: React 19.
 *   - ADR-028: `authenticatedFetch` from `@spaarke/auth` only.
 *   - ADR-030: typed PaneEventBus event signatures.
 *   - CLAUDE.md §3 sub-agent write boundary — no `.claude/` writes.
 *   - CLAUDE.md §6.5 ADR Conflict Resolution — no new tensions surfaced.
 *   - LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md 21 host MUSTs.
 *
 * @see projects/spaarkeai-compose-r1/tasks/042-frontend-create-compose-workspace.poml
 * @see ./hooks/useComposeBroadcastChannel.ts
 * @see ./hooks/useComposeCheckoutLifecycle.ts
 * @see ./hooks/useComposeHeartbeatGate.ts
 * @see ./ComposeWorkspace.types.ts
 * @see src/server/api/Sprk.Bff.Api/Api/ComposeEndpoints.cs (Load + Save contracts)
 * @see docs/architecture/LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md
 */

import * as React from 'react';
import {
  makeStyles,
  tokens,
  mergeClasses,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Text,
  Spinner,
} from '@fluentui/react-components';
import { ComposeBannerStack } from './ComposeBannerStack';
// ai-advanced-capabilities-nda-r1 task 030 — review-summary docked panel (FR-07). Mirrors the
// ComposeCommentThread/ComposeFindReplace docked-panel convention; mounted below alongside the
// SAME `compose_advisory_comments` data task 031's onAdvisoryComments handler already receives.
// task 032 (FR-16 summary-panel restore) — `deriveOverallRisk` reused verbatim (§11 reuse-first) to
// combine multiple findings outputs' `overallRisk` into ONE worst-severity value on reopen, the SAME
// severity rule the panel/gutter already use for per-finding badge coloring.
import { type NdaReviewFindingSummary, deriveOverallRisk } from './AgreementReviewSummaryPanel';
import {
  ComposeEditor,
  type ComposeEditorHandle,
  type ComposeEditorDocumentRef,
  type ComposeDraftPayload,
  type AdvisoryCommentInput,
} from './ComposeEditor';
import type { ComposeActionEnqueue } from './ComposeAiToolbar';
// spaarkeai-compose-r1 task 093: deep-import from `@spaarke/ai-widgets/events`
// rather than the barrel `@spaarke/ai-widgets` to skip the barrel's side-effect
// widget registration (`register-workspace-widgets.ts` transitively pulls in
// `@spaarke/ai-outputs` subpaths, which LegalWorkspace's standalone Rollup
// cannot resolve). Matches the SpaarkeAi `useWorkspaceLayouts` adapter pattern
// (documented in `src/solutions/SpaarkeAi/src/hooks/useWorkspaceLayouts.ts`).
import { useDispatchPaneEvent, usePaneEvent, type WorkspacePaneEvent } from '@spaarke/ai-widgets/events';
// Compose three-pane coordination — WORKSPACE leg (task 104 / E2E-R5). Typed
// receivers for Flow 3 (compose_context_insert) + Flow 5 (compose_assistant_insert).
import { useComposeWorkspaceReceivers } from './useComposeWorkspaceReceivers';
// task 113 (UAT defect 4): the host-injected active-document registrar. When present (SpaarkeAi
// mount, under ComposeActionBridgeProvider), a Browse/direct transient mount is registered with the
// active chat session so chat "summarize this document" + "edit in Compose" resolve it. Null on a
// standalone LegalWorkspace mount (no bridge provider) → registration is skipped, Save still works.
import {
  useComposeActiveDocumentRegistration,
  useRegisterComposeRedlineAcceptHandler,
  useRegisterComposeVisibilityHandler,
  useRegisterComposeInsertSuggestionHandler,
  useRegisterComposeSaveHandler,
  useComposeSaveCompleted,
} from '../context/composeActionBridge';
import { authenticatedFetch, ApiError } from '@spaarke/auth';
// FR-02 (task 011): "Search for Document" opens the standard Dataverse lookup dialog
// (Xrm.Utility.lookupObjects) scoped to `sprk_document`, then resolves the picked
// record's SPE pointer via Xrm.WebApi.retrieveRecord — the SAME two Xrm primitives
// `DataverseLookupField` and the 1c ribbon launcher (`DocumentComposeLaunch.ts`)
// already use. No new lookup UI, no new BFF endpoint (ADR-039: this is a data query,
// not a dispatch).
import {
  createXrmNavigationService,
  createXrmDataService,
  RichFilePreviewDialog,
  SendEmailDialog,
  type LookupResult,
} from '@spaarke/ui-components';
// FR-14 (task 051) — "Create Summary Memo" toolbar control: shared types + pure email-body formatting
// for the persisted review-memo record (render-from-persisted; see file docblock).
import {
  buildReviewMemoEmailBody,
  buildReviewMemoEmailSubject,
  selectMemoNegativeMessage,
  MEMO_NO_MEMO_MESSAGE,
  type ReviewMemoReadResponse,
} from './reviewMemoFormatting';

// FIX #5 (UAT): the separate `ComposeToolbar` command bar (Open-in-Word + Save +
// Push) was folded into the consolidated single-row `ComposeFormatToolbar` that
// lives inside `ComposeEditor`. ComposeWorkspace still OWNS the handler binding —
// it resolves Open-in-Word here via `useDocumentActions` and threads the bound
// callbacks (+ Save / Push) down to `ComposeEditor`, which forwards them to the
// toolbar's "Word" dropdown + right-aligned Save button. `ComposeToolbar.tsx` is
// retained for its own standalone tests + potential reuse, but is no longer
// rendered here (one toolbar row, not two).
import { useDocumentActions } from '@spaarke/document-operations';
import { ComposeEmptyState } from './ComposeEmptyState';
import { ComposeConflictDialog } from './ComposeConflictDialog';
// Return-from-Word re-anchor UX (task 054 — BUILT; mounted here by task 103, gap 3.5).
import { ComposeReanchorBanner } from './ComposeReanchorBanner';
import { ComposeExternalChangeBanner } from './ComposeExternalChangeBanner';
import { ComposeReanchorConflictPanel } from './ComposeReanchorConflictPanel';
import { useComposeReanchor } from './useComposeReanchor';
import type { ReanchorResolutionDecision } from './ComposeReanchor.types';
// Word round-trip shuttle client callers (task 103 — gaps 3.1 / 3.4 / poll half of 3.5).
import {
  useComposePullAnnotations,
  useComposeCheckChanges,
  anchoredAnnotationsToPriorAnchors,
} from './useComposeWordShuttle';
import { composeWorkspaceReducer, INITIAL_STATE } from './ComposeWorkspace.types';
import type { ComposeReviewFindingsDegraded } from './ComposeWorkspace.types';
import { useComposeBroadcastChannel, useComposeCheckoutLifecycle, useComposeHeartbeatGate } from './hooks';
import type {
  ComposeDocumentRef,
  ComposeUploadRef,
  ComposeDraftSeedRef,
  ComposeAssistantToWorkspaceFlow,
  AnchoredAnnotation,
  DefinedTerm,
  ComposeActionHistoryEntry,
  ParaIdMapEntry,
  ImportedRevision,
  ImportedComment,
  // ai-advanced-capabilities-nda-r1 task 040 (comment-export wiring fix)
  ComposeAnchoredComment,
  // FR-03 (task 011, spaarkeai-compose-fidelity-r4.5): shared normalize-target type for the
  // Load/Upload/Browse->project projection hydration sites (see `normalizeProjection` below).
  ComposeServerProjection,
  // G7 (task 022): the Save split-button choice threaded into triggerSave.
  ComposeSaveMode,
} from '../types/compose-contracts';
// R4 FR-06 (task 032, the write-path cutover): the op-log schema constant stamped on the operation log
// `triggerSave` sends — the server (ComposeShadowPatchEngine) validates it against the version it compiles
// against, so both ends agree on the contract shape.
import { COMPOSE_OPERATION_SCHEMA_VERSION } from '../types/compose-operations';

// Re-export for consumers wiring the FR-29/FR-33 rehydrate state (annotations authoring UX is a
// follow-up task; this workspace only receives + stores what LoadAsync's response carries).
export type { AnchoredAnnotation, DefinedTerm, ComposeActionHistoryEntry } from '../types/compose-contracts';

/**
 * Rebuild the rich `ComposeAssistantToWorkspaceFlow` (Flow 5 reducer payload)
 * from the typed `workspace` channel event (task 104). Only reached on the
 * LEGACY no-`ledgerRef` path (the `ledgerRef` path materializes directly and
 * returns first). Zero casts — every field is read from the now-typed
 * WorkspacePaneEvent compose fields with honest defaults for the R1 shape.
 */
function toAssistantInsertPayload(event: WorkspacePaneEvent): ComposeAssistantToWorkspaceFlow {
  return {
    type: 'compose_assistant_insert',
    documentRef: event.documentRef ?? { speDriveItemId: '' },
    sourceNodeId: event.sourceNodeId ?? '',
    sourcePlaybookId: event.sourcePlaybookId ?? '',
    contentHtml: event.contentHtml ?? '',
    format: event.format ?? 'html',
    insertMode: event.insertMode ?? 'insert-at-cursor',
    requireUserConfirm: event.requireUserConfirm ?? true,
    sessionId: event.sessionId ?? '',
    timestamp: event.timestamp ?? new Date().toISOString(),
    ledgerRef: event.ledgerRef,
  };
}

// Re-export types for backwards-compatible consumer imports.
export type { ComposeCheckoutStatus, ComposeWorkspaceState, ComposeWorkspaceAction } from './ComposeWorkspace.types';

/**
 * Mint a client-generated DOCUMENT session id for a mount door that has no server round-trip
 * (Wave 2 / UAT-R3 Test #3: the Browse-direct-upload path). The assistant-upload path receives a
 * server sessionId (`POST /api/compose/upload`); Browse bytes are LOCAL, so there is no
 * server-assigned id — this is the tab-lifetime document session id that AI-edit dispatch threads
 * as `documentSessionId` so a compose EDIT routes to the DOCUMENT session (redline) instead of
 * being misclassified INFORMATIONAL (prose card). Prefers `crypto.randomUUID()` when available and
 * degrades to a timestamp+random id so jsdom/older runtimes without it still mint a stable value.
 */
function mintDocumentSessionId(): string {
  const c = (globalThis as { crypto?: { randomUUID?: () => string } }).crypto;
  if (c?.randomUUID) {
    return c.randomUUID();
  }
  return `compose-doc-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;
}

/**
 * G7 (FR-06, task 022): mint the stable transient-draft dedup key, ONCE per transient mount. Sent on
 * every create-on-save so repeated calls (concurrent saves, a re-created mount, a new tab) dedup to ONE
 * `sprk_document` record via the server `sprk_composetransientkey_uk` alt-key instead of minting
 * duplicates (the 8-duplicate defect). Minted at the mount dispatch site (never per-save — a per-save
 * mint would BE the bug) and carried on `documentRef.transientKey`. Same `crypto.randomUUID()`-preferring
 * shape as {@link mintDocumentSessionId} so jsdom/older runtimes still mint a stable value.
 */
function mintTransientKey(): string {
  const c = (globalThis as { crypto?: { randomUUID?: () => string } }).crypto;
  if (c?.randomUUID) {
    return c.randomUUID();
  }
  return `compose-tk-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;
}

// ASP.NET Core deserializes byte[] request-body fields from a base64 string (System.Text.Json
// convention) — every Compose client caller that sends raw document bytes (Save's retained-original
// encode, and FR-03/task 011's project() browse round-trip) shares this ONE encoder rather than
// forking the conversion. Iterate (not spread) to avoid a call-stack overflow on large documents.
function arrayBufferToBase64(buf: ArrayBuffer): string {
  const view = new Uint8Array(buf);
  let binary = '';
  for (let i = 0; i < view.length; i++) binary += String.fromCharCode(view[i]);
  return btoa(binary);
}

/** Raw wire shape of a `projection` field on a Compose bytes->response payload (Load / Upload /
 * Project) — every field optional so an older BFF build (predating the projection wiring) still
 * normalizes cleanly. */
interface RawComposeProjectionPayload {
  status?: 'success' | 'partial' | 'failed';
  canEdit?: boolean;
  html?: string;
  warnings?: { code: string; count: number }[];
  schemaVersion?: string;
}

// FR-01 (task 010) / FR-03 (task 011): normalizes a raw wire `projection`
// field into the `ComposeServerProjection` shape the reducer/editor expect, defaulting every field
// defensively. `undefined`/`null` (an older BFF, or a failed/unreachable projection call) normalizes
// to `null` — since task 013 (F-2 "one reader") the editor has no client-side fallback reader left,
// so a null projection now renders an explicit error/unavailable state instead. Shared by the
// stored-doc Load, assistant-upload (task 010), and Browse->project (task 011) hydration sites so
// the three doors do not fork the same defaulting logic three times over.
function normalizeProjection(p: RawComposeProjectionPayload | null | undefined): ComposeServerProjection | null {
  if (!p) return null;
  return {
    status: p.status ?? 'failed',
    canEdit: p.canEdit ?? false,
    html: p.html ?? '',
    warnings: Array.isArray(p.warnings) ? p.warnings : [],
    schemaVersion: p.schemaVersion ?? 'compose-html-v1',
  };
}

// Item 7 (UAT round-4): the single generic starter scaffold "Open template" mounts today. A neutral
// title + body the user overwrites — the seam for a future template picker (each future template is
// just another HTML string / fetched body routed through the same born-in-editor mount). Intentionally
// plain (no firm branding, no letterhead) so it is a safe default for any document type.
const COMPOSE_BLANK_TEMPLATE_HTML =
  '<h1>Document title</h1><p>Start writing here. Replace this text with your content.</p>';

// ---------------------------------------------------------------------------
// FR-02 (task 011) — `sprk_document` field constants for the Search lookup
// ---------------------------------------------------------------------------
// Mirrors the field constants in `src/solutions/SpaarkeAi/src/ribbon/DocumentComposeLaunch.ts`
// (the 1c ribbon launcher) — same schema, same resolution shape. Update both if the
// schema is ever renamed.
const SEARCH_FIELD_DOCUMENT_ID = 'sprk_documentid';
const SEARCH_FIELD_GRAPH_ITEM_ID = 'sprk_graphitemid';
const SEARCH_FIELD_DRIVE_ID = 'sprk_graphdriveid';
const SEARCH_FIELD_DISPLAY_NAME = 'sprk_filename';
const SEARCH_DOCUMENT_SELECT = `?$select=${SEARCH_FIELD_DOCUMENT_ID},${SEARCH_FIELD_GRAPH_ITEM_ID},${SEARCH_FIELD_DRIVE_ID},${SEARCH_FIELD_DISPLAY_NAME}`;

// ---------------------------------------------------------------------------
// FR-04 draft-into-editor — render-follows-store types + seams (task 016)
// ---------------------------------------------------------------------------

// `ComposeDraftPayload` (the client mirror of `ComposeDraftDisposition.cs`) is owned by
// ComposeEditor (which performs the insertion) and imported above, so both sides of the
// materialize seam share ONE type.

/**
 * A single stored `compose`-disposition ledger output projected to the client by the
 * session-ledger read endpoint (task-016 report INTEGRATION HOOK #1). The workspace
 * re-materializes the editor FROM this STORED entry (ADR-040 store-before-render), never from
 * a client-only event payload — which is what makes the draft refresh-durable.
 */
export interface ComposeLedgerOutput {
  /** Addressable ledger key `{bindingId}@t{n}` — the provenance stamp. */
  key: string;
  bindingId: string;
  turn: number;
  /** Always `'compose'`. */
  disposition: string;
  /** The Compose-owned structured-edit payload. */
  payload: ComposeDraftPayload;
}

/**
 * FR-16 (ai-advanced-capabilities-agreements-r1 task 030) — one flagged clause in a DURABLE
 * agreement-review payload. Two schema vintages coexist (structural mirror of SpaarkeAi's live-path
 * `NdaReviewFlaggedSection` in `useNdaReviewAdvisoryCommentsBridge.ts`): the pre-split shape carried
 * `explanation`; the task-002 schema split replaced it with discrete `flaggedClause` (grounded fact)
 * + `assessment` (reasoned judgment). Both re-materialize identically via
 * {@link projectLedgerFindingsToAdvisoryComments}. Every field is loose/optional — a durable ledger
 * replay may be a legacy row OR a partial/malformed LLM shape, so nothing here is trusted structurally.
 */
export interface ComposeReviewFlaggedSection {
  /** Verbatim quoted clause excerpt — the placement anchor (→ AdvisoryCommentInput.targetText). Tier-3. */
  quotedText?: string;
  /** Pre-split advisory explanation (legacy vintage). Tier-3. */
  explanation?: string;
  /** task-002 grounded-fact field (post-split vintage). Tier-3. */
  flaggedClause?: string;
  /** task-002 reasoned-judgment field (post-split vintage). Tier-3. */
  assessment?: string;
  /** Clause reference from the review output (e.g. "3.2"). */
  sectionRef?: string;
  /** Coarse qualitative risk signal (never a numeric score, per ADR-039). */
  riskLevel?: string;
  /** Firm-standard / playbook reference the flag cites. */
  standardRef?: string;
}

/**
 * FR-16 (task 030) — the durable agreement-review superset of {@link ComposeDraftPayload}. The general
 * `agreement-review` Action emits `{ overallRisk, flaggedSections[] }`; its Binding now declares the
 * `compose` disposition (Informational→Compose flip, task 030) so the review's SessionOutput is stored
 * (ADR-040) and re-materialized on reopen as advisory comment threads — NEVER a redline. Kept LOCAL to
 * ComposeWorkspace (the payload's only consumer) rather than widening the shared
 * ComposeDraftPayload/edit vocabulary: a review result is not an edit.
 */
interface ComposeReviewPayload extends ComposeDraftPayload {
  /** Server-asserted overall risk rating — carried for the summary panel (restore is task 032). */
  overallRisk?: string;
  /** Per-clause advisory findings (the durable-review payload). */
  flaggedSections?: ComposeReviewFlaggedSection[];
}

/**
 * Projects a durable agreement-review payload's `flaggedSections[]` into {@link AdvisoryCommentInput}s
 * for re-materialization on document reopen (FR-16, task 030) — the metadata-preserving path
 * (riskLevel/sectionRef/standardRef + BOTH vintages) so the restored gutter Review Notes render
 * identically to the live review. NOT routed via `registerAiReviewComments` (which drops that
 * metadata — spec FR-16 / CLAUDE.md §11).
 *
 * §11 decision (documented): this is the structural mirror of SpaarkeAi's live-path
 * `projectFlaggedSectionsToAdvisoryComments` (`useNdaReviewAdvisoryCommentsBridge.ts`), deliberately
 * NOT hoisted into a shared module. That helper is SpaarkeAi-side and produces a PaneEventBus event
 * shape; this reads the raw ledger payload and produces the ComposeEditor input directly. A ~15-line
 * pure map local to each package is cheaper than a new SpaarkeAi→Compose.Components coupling, and the
 * two copies share one authored convention (both handle both vintages the same way, cross-referenced
 * in each JSDoc). Exported for direct unit testing.
 *
 * Defensive against a malformed/partial ledger replay: an entry that is not an object, is missing a
 * usable `quotedText`, or carries NONE of `explanation`/`flaggedClause`/`assessment`, is skipped
 * (never thrown) — the caller sees fewer items, never a crash, never a partial mid-loop placement.
 */
export function projectLedgerFindingsToAdvisoryComments(flaggedSections: readonly unknown[]): AdvisoryCommentInput[] {
  const asNonEmptyString = (value: unknown): string | undefined =>
    typeof value === 'string' && value.trim().length > 0 ? value : undefined;
  const items: AdvisoryCommentInput[] = [];
  for (const raw of flaggedSections) {
    if (raw === null || typeof raw !== 'object') continue;
    const section = raw as Record<string, unknown>;
    const targetText = asNonEmptyString(section.quotedText);
    if (!targetText) continue;
    const legacyExplanation = asNonEmptyString(section.explanation);
    const flaggedClause = asNonEmptyString(section.flaggedClause);
    const assessment = asNonEmptyString(section.assessment);
    // Legacy vintage: use `explanation` verbatim. Post-split vintage (no `explanation`): compose the
    // thread text / legacy-degrade source from the discrete fields (mirrors the bridge), AND carry the
    // discrete fields through so gutter/export render the structured form with no string-parsing (task 052).
    const explanation = legacyExplanation ?? [flaggedClause, assessment].filter(Boolean).join('\n\n');
    if (!explanation) continue;
    items.push({
      targetText,
      explanation,
      sectionRef: asNonEmptyString(section.sectionRef),
      riskLevel: asNonEmptyString(section.riskLevel),
      standardRef: asNonEmptyString(section.standardRef),
      flaggedClause,
      assessment,
    });
  }
  return items;
}

/**
 * Task 032 — structural shape guard for a STORED compose output, mirroring the inline check task 030
 * introduced inline (detected by `flaggedSections[]`, never a disposition/bindingId allowlist — see
 * task 030's §11 rationale). Used by the untargeted (reopen / refresh-durability) materialize pass to
 * partition ALL of a session's stored compose outputs into findings vs. edit/comment/redline BEFORE
 * selecting what to replay (the FR-16 coexistence fix — findings are never evicted by a later edit).
 */
function isFindingsShapedComposeOutput(output: ComposeLedgerOutput): boolean {
  const payload = output.payload as ComposeReviewPayload | undefined;
  return Array.isArray(payload?.flaggedSections);
}

/**
 * Task 032 (031-residual dedupe guard — see notes/031-execution-notes.md "Residual risk"). A stable,
 * order-independent content signature for a set of advisory placements. The LIVE
 * `compose_advisory_comments` event carries no `ledgerRef` today (verified:
 * `useNdaReviewAdvisoryCommentsBridge.ts` never sets it, though the wire type has room for one), so
 * exact-key idempotency (`lastMaterializedKey`) is unavailable for the live→ledger race 031 escalated
 * (a same-mount `externalChange` re-trigger re-running the ledger materializer WHILE the live path's
 * placement already exists in the SAME editor instance — `placeAdvisoryComments` has no idempotency
 * of its own). This content signature is the fallback: the SAME set of quoted clauses, materialized
 * via either path, dedupes to the SAME token; a genuinely DIFFERENT review (different clauses) gets a
 * different signature and is NOT suppressed.
 */
function computeAdvisorySignature(items: readonly { targetText: string }[]): string {
  return items
    .map(item => item.targetText.trim().toLowerCase())
    .sort()
    .join('␟');
}

/**
 * Task 032 (FR-16 128KB budget, Leg B — visible notice, not chunking; see
 * `ComposeReviewFindingsDegraded`'s JSDoc in `ComposeWorkspace.types.ts` for the full rationale).
 * Best-effort sessionStorage read/write — never throws (private-browsing / quota / SSR-safe), mirrors
 * `ComposeBannerStack.tsx`'s `readImportWarningsDismissed`/`writeImportWarningsDismissed` convention.
 * Scope is honest and narrow: tab-lifetime only (a brand-new tab/device has no marker to compare
 * against — closing that gap needs a server-side truncation-marker passthrough, out of this task's
 * read-only `src/server/**` boundary).
 */
const REVIEW_FINDINGS_MARKER_PREFIX = 'spaarke.compose.reviewFindingsMarker:';

function readReviewFindingsMarker(sessionId: string): { count: number } | null {
  if (typeof window === 'undefined' || !window.sessionStorage || !sessionId) return null;
  try {
    const raw = window.sessionStorage.getItem(REVIEW_FINDINGS_MARKER_PREFIX + sessionId);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as { count?: unknown };
    const count = typeof parsed.count === 'number' && Number.isFinite(parsed.count) ? parsed.count : 0;
    return { count };
  } catch {
    return null;
  }
}

function writeReviewFindingsMarker(sessionId: string, count: number): void {
  if (typeof window === 'undefined' || !window.sessionStorage || !sessionId) return;
  try {
    window.sessionStorage.setItem(REVIEW_FINDINGS_MARKER_PREFIX + sessionId, JSON.stringify({ count }));
  } catch {
    // Best-effort — a failed persist just means the degraded-restore detector has nothing to compare
    // against next time; it never blocks the (successful) restore that triggered this write.
  }
}

// The render-follows-store signal ComposeWorkspace consumes is Flow 5
// (`workspace.compose_assistant_insert`) additively carrying the `ledgerRef` of the stored
// compose output. Per spike-0 §3b/§4 the editor-insertion signal IS Flow 5 — there is no
// `compose_action_request`. The `ledgerRef?` additive field now lives on the shared
// `ComposeAssistantToWorkspaceFlow` contract (compose-contracts.ts, task 016 HOOK #3), and the
// materialize capability lives on `ComposeEditorHandle.materializeComposeDraft` (ComposeEditor,
// HOOK #2) — both formalized in this integration pass, so no local interface hacks remain here.

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

/**
 * Props for `ComposeWorkspace`. See file header for state-machine semantics.
 */
export interface ComposeWorkspaceProps {
  /**
   * Optional initial document pointer. When supplied, the workspace fetches
   * DOCX bytes on mount via `GET /api/compose/documents/{speId}?driveId&tenantId`.
   * When `undefined` or `null`, renders `ComposeEmptyState` (Path A/B picker).
   */
  initialDocumentRef?: ComposeDocumentRef | null;

  /**
   * FR-03 (task 012): optional transient upload-mount pointer. When supplied (and no
   * `initialDocumentRef`), the workspace fetches the Assistant-uploaded file's retained
   * bytes via `POST /api/compose/upload` and mounts them into the editor as a TRANSIENT
   * working draft (no `sprk_document`; create-on-save on first Save). Mutually exclusive
   * with `initialDocumentRef` — an upload has no SPE pointer.
   */
  initialUploadRef?: ComposeUploadRef | null;

  /**
   * DEF-08: optional AI-drafted full-document SEED. When supplied (and no `initialDocumentRef` /
   * `initialUploadRef`), the workspace materializes the drafted document into the editor as a
   * TRANSIENT working draft (create-on-save on first Save). Two shapes (see {@link ComposeDraftSeedRef}):
   *  - Part A `{ ledgerRef, sessionId }` — resolve the body from
   *    `GET /api/ai/chat/sessions/{sessionId}/compose-outputs` (the `compose-draft-document` output).
   *  - Part B `{ html }` — the drafted body supplied inline ("Open in Compose" affordance).
   * Mutually exclusive with `initialDocumentRef` and `initialUploadRef`.
   */
  initialDraftRef?: ComposeDraftSeedRef | null;

  /** Optional initial ChatSession id (correlation). */
  initialSessionId?: string;

  /**
   * FR-33 (task 102, gap 4.1): optional Matter id completing the cross-version resume key
   * (`DocumentId + MatterId`, design.md §8). When the host is a Matter workspace, forwarding it on
   * the Load request lets the BFF resume the SAME session for this document + matter across a Word
   * round-trip (a new DOCX version never changes the key), restoring prior annotations, defined
   * terms, and action history. Optional — omitting it preserves the DocumentId-only resume match
   * (backward compatible). The BFF binds it as an optional query param.
   */
  matterId?: string;

  /** BFF base URL (host only, e.g. `https://host.azurewebsites.net`). */
  bffBaseUrl: string;

  /** SPE driveId — required query param for the BFF Load endpoint. */
  driveId: string;

  /** Microsoft Entra tenant id (multi-tenant scoping per ADR-015 Tier 3). */
  tenantId: string;

  /**
   * FR-05 create-on-save (task 100): the user's Business-Unit SPE container id, resolved
   * CLIENT-SIDE by the host via `EntityCreationService.resolveUserBuDefaults`
   * (`businessunit.sprk_containerid`) — the SAME convention the 7 Create*Wizards use (Fork A,
   * owner-approved 2026-07-09). Threaded into a transient (Browse/Upload) draft's `documentRef`
   * so the first Save persists it as a new `sprk_document` in this container. Undefined when the
   * host has no Dataverse context (standalone) or is still resolving — a transient Save is gated
   * until it is present. The BFF does NOT resolve BU→container (multi-container INV-7).
   */
  containerId?: string;

  /**
   * FR-05 create-on-save (task 100): invoked once a transient draft is persisted as a NEW
   * `sprk_document` on first Save, with the server-minted `sprk_documentid`. The host wires this
   * to `useCreateOnSaveAssociation.associate(newDocumentId)` so a chosen parent association is
   * written (a no-op when the user chose "none"). Non-fatal — the document already exists.
   */
  onCreateOnSaveComplete?: (newSprkDocumentId: string) => void | Promise<void>;

  /** Called when the user clicks Browse in the empty state. */
  onBrowseRequested?: () => void;

  /** Called when the user clicks the "Open Document" CTA (task 039 P2 label; formerly "Search for Document") in the empty state. */
  onSearchRequested?: () => void;

  /** Called once the workspace has mounted (LEGALWORKSPACE-EMBEDDED-MODE §6). */
  onComposeMount?: () => void;

  /** Called when the workspace is about to unmount (same contract). */
  onComposeUnmount?: () => void;

  /** Optional className passed to the root container (for host styling). */
  className?: string;

  /**
   * FR-18 host serialization seam (task 032). Forwarded to ComposeEditor → the
   * inline AI toolbar so rapid toolbar-action clicks serialize through the host's
   * `useSerialActionQueue`-backed queue. The SpaarkeAi host supplies this by
   * bridging ConversationPane's `dispatchComposeAction` across panes (shared
   * context / launch context); a standalone Path-A mount may omit it (the
   * toolbar falls back to its own unserialized dispatcher). See
   * `ComposeActionEnqueue`.
   */
  enqueueComposeAction?: ComposeActionEnqueue;

  /**
   * spaarkeai-compose-r2 (multi-Compose-tab) — the id of the WORKSPACE TAB this editor is
   * mounted in, when hosted as a keep-alive workspace tab (SpaarkeAi). Multiple Compose tabs
   * can be mounted simultaneously (each hidden except the active one); this id lets THIS instance
   * tab-scope its active-document re-registration to `tab_change` events that target THIS tab —
   * so two mounted editors never fight over the session's active document. Omitted on the
   * standalone / layout-door single-instance mounts (the legacy widgetType/seed discriminant is
   * used then).
   */
  workspaceTabId?: string;

  /**
   * spaarkeai-compose-r2 (multi-Compose-tab) — whether THIS editor's workspace tab is the ACTIVE
   * (visible) tab. Defaults to `true` (standalone / single-instance mounts are always "active").
   * When `false`, this instance suppresses the load-time active-document auto-registrations and
   * the visibility-conduit visible=true register, so only the ACTIVE tab's document is the
   * session's active document. The becoming-active re-registration flows through the tab-scoped
   * `tab_change` effect.
   */
  isActiveTab?: boolean;

  /**
   * ai-advanced-capabilities-analysis-hub-r1 task 041 (FR-13): the ACTIVE work type — the
   * product surface the user launched (e.g. `'agreement-analysis'` for an Agreement Review).
   * Forwarded UNCHANGED to `<ComposeEditor activeWorkType>`, which threads it into
   * `getToolsForSurface(surface, activeWorkType)` (`ComposeAiToolbar.tsx:490`) so the inline AI
   * toolbar / Review-Note menu surface work-type-scoped tools alongside the shared `['*']`
   * primitives. Optional — omitting it preserves `ComposeEditor`'s own `'*'` default (unscoped,
   * every existing non-Agreement-Review mount is unaffected). This is pure pass-through; NO
   * tool-filtering logic lives in `ComposeWorkspace` (reuse the shipped `getToolsForSurface`,
   * never reimplement).
   */
  activeWorkType?: string;
}

// ---------------------------------------------------------------------------
// Styles — Fluent v9 semantic tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    width: '100%',
    height: '100%',
    // FIX #6 (spaarkeai-compose-r2): `minHeight: 0` lets root shrink as a flex child of the Direct
    // widget's bounded host (ComposeDirectWidget) instead of growing to its content height. Without
    // it, `editorSlot` (flex:1) could not resolve a bounded height, an outer container would become
    // the scroller, and the sticky ComposeFormatToolbar would scroll away. Harmless for the standalone
    // / layout-door mounts (a block parent ignores it).
    minHeight: 0,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    boxSizing: 'border-box',
    overflow: 'hidden',
  },
  toolbarSlot: {
    flexShrink: 0,
  },
  bannerStack: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXS,
    paddingInline: tokens.spacingHorizontalM,
    paddingBlock: tokens.spacingVerticalXS,
    flexShrink: 0,
  },
  editorSlot: {
    flex: 1,
    minHeight: 0,
    display: 'flex',
    flexDirection: 'column',
  },
  // FR-01 (task 010): the native file input backing "Browse / open file" is never
  // visually rendered — it is triggered programmatically via a ref. `display: none`
  // is layout, not color, so this is ADR-021-compliant (semantic tokens govern color;
  // this token-free rule governs visibility only).
  hiddenBrowseInput: {
    display: 'none',
  },
  loadingState: {
    display: 'flex',
    flex: 1,
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    columnGap: tokens.spacingHorizontalS,
    rowGap: tokens.spacingVerticalS,
    color: tokens.colorNeutralForeground2,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export function ComposeWorkspace(props: ComposeWorkspaceProps): React.JSX.Element {
  const styles = useStyles();
  const {
    initialDocumentRef,
    initialUploadRef,
    initialDraftRef,
    initialSessionId,
    matterId,
    bffBaseUrl,
    driveId,
    tenantId,
    containerId,
    onCreateOnSaveComplete,
    onBrowseRequested,
    onSearchRequested,
    onComposeMount,
    onComposeUnmount,
    className,
    enqueueComposeAction,
    workspaceTabId,
    isActiveTab = true,
    activeWorkType,
  } = props;

  const [state, dispatch] = React.useReducer(composeWorkspaceReducer, INITIAL_STATE);

  // spaarkeai-compose-r2 (multi-Compose-tab): keep the latest active-tab flag in a ref so the
  // async load effects + the single-slot visibility conduit handler (both of which capture their
  // closure at mount / dep-change time) can read whether THIS instance is the active tab WITHOUT
  // re-subscribing. Only the ACTIVE tab's instance may claim the session's active document.
  const isActiveTabRef = React.useRef<boolean>(isActiveTab);
  React.useEffect(() => {
    isActiveTabRef.current = isActiveTab;
  }, [isActiveTab]);

  // FR-05 (task 100): keep the latest host-resolved BU container id in a ref so the Browse
  // handler + upload effect (whose closures would otherwise capture a stale prop) always thread
  // the current value into `mountTransient` → `documentRef.containerId`. triggerSave also falls
  // back to this ref if the reducer state predates resolution (async-resolve race).
  const containerIdRef = React.useRef<string | undefined>(containerId);
  React.useEffect(() => {
    containerIdRef.current = containerId;
  }, [containerId]);

  // Imperative editor ref for save (TipTap → DOCX bytes).
  const editorRef = React.useRef<ComposeEditorHandle | null>(null);

  // -------------------------------------------------------------------------
  // FR-34 D-F3 honest content-render ack (task 071)
  // -------------------------------------------------------------------------
  // A chat-opened Compose tab that carries a full-document draft SEED (DEF-08
  // Part A: `initialDraftRef.{ledgerRef,sessionId}`) rides a server
  // `workspace_open_tab` frame whose tool result is WAITING on a client ack
  // (SendWorkspaceArtifactHandler.WaitForAckAsync). WorkspacePane DEFERS that ack
  // for a seeded frame — the tab SHELL opening is not the content rendering — and
  // fires it only when this render signal arrives. We emit the signal ONLY after
  // the seed has actually materialized in the editor (`mountDraftHtml` → status
  // 'loaded' + non-null `seedHtml`), correlated back to the waiting frame by
  // `ledgerRef`. On a seed FAILURE (ledger miss / fetch error → the 'error' state)
  // the signal NEVER fires → the server's WaitForAckAsync times out → the tool
  // result fails HONESTLY (never a fabricated "the draft is in the editor").
  // No content on the bus (ADR-015 identifiers-only); additive discriminant,
  // typed, no `any` (ADR-030). Only Part A (a real ledgerRef → a real server ack)
  // arms this; Part B inline-html "Open in Compose" is a client affordance with no
  // ack-gated server frame, so it never arms the signal.
  const dispatchPaneEvent = useDispatchPaneEvent();
  const pendingDraftRenderSignalRef = React.useRef<{ ledgerRef: string; sessionId?: string } | null>(null);

  // FR-01 (task 010): hidden native file input backing "Browse / open file". Triggered
  // programmatically from handleBrowseRequested; see the input element rendered below.
  const browseFileInputRef = React.useRef<HTMLInputElement | null>(null);

  // -------------------------------------------------------------------------
  // FR-02 (task 011) — Search-resolved drive id override
  // -------------------------------------------------------------------------
  // The `driveId` PROP reflects whichever document launched this workspace mount (the
  // 1c ribbon/launch-context document, or "" for a bare empty-state mount — see
  // composeEditor.registration.ts). A Search-selected `sprk_document` can live in a
  // DIFFERENT SPE drive than that prop. `searchResolvedDriveId` overrides it so the
  // EXISTING Load/Save leg (same endpoint, same effect) keys off the correct drive for
  // whichever document is actually loaded — no new load path is introduced.
  const [searchResolvedDriveId, setSearchResolvedDriveId] = React.useState<string | null>(null);
  const effectiveDriveId = searchResolvedDriveId ?? driveId;

  // FR-04 render-follows-store bookkeeping (task 016). `lastMaterializedKey` makes
  // materialization idempotent across refresh + duplicate Flow-5 signals (never double-apply
  // the same stored draft). `composeDraftError` surfaces a soft failure without crashing.
  const [lastMaterializedKey, setLastMaterializedKey] = React.useState<string | null>(null);
  const [composeDraftError, setComposeDraftError] = React.useState<string | null>(null);

  // -------------------------------------------------------------------------
  // #1(b) — "Open preview" for the document persisted by the last Save
  // FIX #7a — the host (ConversationPane) save-completed conduit. When a Save persists a document,
  // we hand the persisted id + filename to the Assistant so it POSTs a PERSISTENT "Saved '{filename}'
  // to the DMS." chat message with an "Open preview" affordance (replacing the transient banner's
  // preview link). Held in a ref so triggerSave's useCallback need not depend on the (identity-
  // flipping) delegate. Null on a standalone LegalWorkspace mount → the editor's own Saved ✓ banner
  // remains the only confirmation there.
  const notifyComposeSaveCompleted = useComposeSaveCompleted();
  const notifyComposeSaveCompletedRef = React.useRef(notifyComposeSaveCompleted);
  notifyComposeSaveCompletedRef.current = notifyComposeSaveCompleted;

  // -------------------------------------------------------------------------
  // "Open Document" — preview the source Dataverse Document in a modal.
  // Reuses the SHARED `RichFilePreviewDialog` (@spaarke/ui-components) + the BFF
  // `GET /api/documents/{id}/preview-url` endpoint — the SAME mechanism the
  // ConversationPane "Open preview" chat affordance (FIX #7a) uses. No new
  // component and no new endpoint (§11 reuse / §10 BFF hygiene). The button on the
  // format toolbar opens this modal, keyed off the current doc's sprk_document id.
  // -------------------------------------------------------------------------
  const previewNavigationService = React.useMemo(() => createXrmNavigationService(), []);
  const [documentPreviewOpen, setDocumentPreviewOpen] = React.useState(false);
  // Fetch the ephemeral iframe preview URL for the current document (RichFilePreview's
  // `fetchPreviewUrl` contract). ADR-028: goes through `@spaarke/auth` authenticatedFetch —
  // no raw fetch/bearer. Non-fatal on failure (the modal shows its own fallback).
  const fetchDocumentPreviewUrl = React.useCallback(async (): Promise<string | null> => {
    const docId = state.documentRef?.sprkDocumentId;
    if (!docId || !bffBaseUrl) return null;
    try {
      const response = await authenticatedFetch(
        `${bffBaseUrl}/api/documents/${encodeURIComponent(docId)}/preview-url`,
        { method: 'GET' }
      );
      const data = (await response.json()) as { previewUrl?: string };
      return data.previewUrl ?? null;
    } catch {
      return null;
    }
  }, [state.documentRef?.sprkDocumentId, bffBaseUrl]);

  // -------------------------------------------------------------------------
  // FR-29 / FR-33 (R2, tasks 060/102) — anchored annotations + defined-terms +
  // action-history rehydrate, and annotation save-on-mutation
  // -------------------------------------------------------------------------
  // Restored from the Load response's `anchoredAnnotations`/`definedTermsTracking`/`actionHistory`
  // fields (design.md §8) — task 102 (gaps 4.1/4.2/4.4) wired the BFF Load endpoint to (a) RESUME
  // the prior session for this document+matter and (b) PROJECT these three collections onto the
  // wire response. `anchoredAnnotations`/`definedTermsTracking` are the single mutable store for
  // this workspace (no parallel store); `actionHistory` is a read-only projection restored for a
  // future Context-pane render.
  const [anchoredAnnotations, setAnchoredAnnotations] = React.useState<AnchoredAnnotation[]>([]);
  const [definedTermsTracking, setDefinedTermsTracking] = React.useState<DefinedTerm[]>([]);
  const [actionHistory, setActionHistory] = React.useState<ComposeActionHistoryEntry[]>([]);

  // Save-on-mutation sync marker (gap 4.3). Holds a JSON snapshot of the annotation collections as
  // last SYNCED with the server (hydrated from Load OR just persisted). The persist effect below
  // POSTs to the session-annotations route only when the live state DIVERGES from this snapshot —
  // so a hydrate never writes back, and any real mutation (accept/reject/edit, or the Word-return
  // reanchor write-back once mounted) durably persists so it survives a reopen past the Redis TTL.
  // Initialized to the EMPTY snapshot so a still-empty session never triggers a spurious write.
  const annotationsSnapshot = React.useCallback(
    (a: AnchoredAnnotation[], d: DefinedTerm[]): string =>
      JSON.stringify({ anchoredAnnotations: a, definedTermsTracking: d }),
    []
  );
  const syncedAnnotationsRef = React.useRef<string>(annotationsSnapshot([], []));

  // -------------------------------------------------------------------------
  // Mount/Unmount host hooks per LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT §6
  // -------------------------------------------------------------------------
  React.useEffect(() => {
    onComposeMount?.();
    return () => {
      onComposeUnmount?.();
    };
    // Intentionally fire-once.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // -------------------------------------------------------------------------
  // Kick off initial load if initialDocumentRef supplied
  // -------------------------------------------------------------------------
  // Issue #572 (belt-and-braces to the ribbon guard): a documentRef WITHOUT a
  // drive id means the document is only half-provisioned in SPE — the BFF Load
  // endpoint cannot fetch it. Stay in the 'empty' state (Browse/Search picker)
  // with an informational banner instead of dispatching a hard load error.
  const missingDrivePointer = Boolean(initialDocumentRef?.speDriveItemId) && !driveId;

  React.useEffect(() => {
    if (!initialDocumentRef) return;
    if (state.status !== 'empty' && state.status !== 'error') return;
    if (!initialDocumentRef.speDriveItemId) return;
    if (!driveId) return; // half-provisioned — honest empty state, not an error

    dispatch({
      kind: 'requestLoad',
      documentRef: initialDocumentRef,
      sessionId: initialSessionId ?? '',
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [initialDocumentRef?.speDriveItemId, driveId]);

  // -------------------------------------------------------------------------
  // BFF Load — GET /api/compose/documents/{speId}?driveId&tenantId&documentRecordId&displayName
  // -------------------------------------------------------------------------
  React.useEffect(() => {
    if (state.status !== 'loading') return;
    if (!state.documentRef) return;
    if (!bffBaseUrl) {
      dispatch({
        kind: 'loadFailed',
        errorMessage: 'BFF base URL is not configured. Cannot load document.',
      });
      return;
    }
    if (!tenantId) {
      // Truly misconfigured host — tenant id is a static host-config value.
      dispatch({
        kind: 'loadFailed',
        errorMessage: 'Tenant id is required to load Compose documents. Check the host configuration.',
      });
      return;
    }
    if (!effectiveDriveId) {
      // Half-provisioned document (missing SPE drive pointer) — not a host
      // misconfiguration. Route back to the empty state; the informational
      // banner below explains the situation. (Normally unreachable — the
      // initial-load effect gates requestLoad on driveId, and the Search flow
      // (FR-02/task 011) gates on the resolved SPE pointer before dispatching
      // requestLoad — but defensive for any other dispatch path.)
      dispatch({ kind: 'reset' });
      return;
    }

    const ac = new AbortController();
    const docRef = state.documentRef;

    (async () => {
      try {
        const qs = new URLSearchParams({ driveId: effectiveDriveId, tenantId });
        if (docRef.sprkDocumentId) qs.set('documentRecordId', docRef.sprkDocumentId);
        if (docRef.fileName) qs.set('displayName', docRef.fileName);
        // FR-29/FR-33 (R2, tasks 060/102 gap 4.1): forward the known prior session id — and, when
        // the host is a Matter workspace, the matter id — so the BFF RESUMES that session (design.md
        // §8 — annotations are keyed to document identity + matter, surviving a re-open) instead of
        // minting a fresh empty one. Purely additive — each omitted when unknown.
        if (initialSessionId) qs.set('sessionId', initialSessionId);
        if (matterId) qs.set('matterId', matterId);

        const url = `${bffBaseUrl}/api/compose/documents/${encodeURIComponent(docRef.speDriveItemId)}?${qs.toString()}`;

        const response = await authenticatedFetch(url, { method: 'GET', signal: ac.signal });

        if (!response.ok) {
          const msg =
            response.status === 404
              ? 'Document not found. It may have been deleted or moved.'
              : response.status === 403
                ? 'You do not have permission to open this document.'
                : `Failed to load document (HTTP ${response.status}).`;
          dispatch({ kind: 'loadFailed', errorMessage: msg });
          return;
        }

        const payload = (await response.json()) as {
          documentSpeId: string;
          driveId: string;
          sessionId: string;
          documentRecordId?: string;
          // ASP.NET Core serializes byte[] as a base64-encoded string in JSON,
          // NOT as a JSON array of numbers. Decode with atob() below.
          content: string;
          eTag?: string;
          // R3 FR-06 (task 027): the load-time SPE version id — carried back as `baselineVersionId` on a
          // dirty-loaded save so the server can re-fetch this baseline without the client bytes.
          versionId?: string;
          fileName?: string;
          size: number;
          correlationId?: string;
          // FR-29/FR-33 (R2, tasks 060/102, design.md §8): the three collections the BFF Load
          // response now projects from the resumed/created session (gaps 4.2/4.4). Parsed
          // defensively (optional) so an older BFF that predates the wiring still loads.
          anchoredAnnotations?: AnchoredAnnotation[];
          definedTermsTracking?: DefinedTerm[];
          actionHistory?: ComposeActionHistoryEntry[];
          // task 052 fast-follow (FR-08/FR-24/FR-25 wire gap): the server pre-parse paraId map +
          // recovered Word revisions/comments the BFF Load response now projects (ComposeEndpoints.cs
          // `LoadComposeDocumentResponse`). Parsed defensively (optional) so an older BFF that
          // predates the wiring still loads — `loadSucceeded` normalizes an omitted field to `[]`.
          paraIdMap?: ParaIdMapEntry[];
          importedRevisions?: ImportedRevision[];
          importedComments?: ImportedComment[];
          // The server DOCX→editor projection. Optional so an older BFF (no projection) still parses —
          // task 013 (F-2): the editor now renders an error/unavailable state, not a mammoth fallback.
          projection?: {
            status?: 'success' | 'partial' | 'failed';
            canEdit?: boolean;
            html?: string;
            warnings?: { code: string; count: number }[];
            schemaVersion?: string;
          };
          // G1 (FR-01, task 020): the persisted cross-session origin marker (Path A only — an
          // existing sprk_document record; ComposeEndpoints.cs LoadComposeDocumentResponse.origin).
          // Parsed defensively (optional) so an older BFF that predates the field still loads;
          // undefined/null normalize to `null` below — the BINDING null-handling contract treats
          // that the SAME as 'imported', never strict-equal to 'authored'.
          origin?: 'authored' | 'imported' | null;
        };

        // Decode base64 -> bytes. atob() returns a binary string (one char per byte).
        const binary = atob(payload.content ?? '');
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) {
          bytes[i] = binary.charCodeAt(i);
        }
        if (ac.signal.aborted) return;
        const hydratedAnnotations = Array.isArray(payload.anchoredAnnotations) ? payload.anchoredAnnotations : [];
        const hydratedDefinedTerms = Array.isArray(payload.definedTermsTracking) ? payload.definedTermsTracking : [];
        setAnchoredAnnotations(hydratedAnnotations);
        setDefinedTermsTracking(hydratedDefinedTerms);
        setActionHistory(Array.isArray(payload.actionHistory) ? payload.actionHistory : []);
        // gap 4.3: mark the just-hydrated collections as server-synced so the persist effect below
        // does NOT write them straight back — only a subsequent LOCAL mutation persists.
        syncedAnnotationsRef.current = annotationsSnapshot(hydratedAnnotations, hydratedDefinedTerms);
        // task 052 fast-follow: same Array.isArray defensive-parse convention as the three
        // collections above — an omitted OR malformed (non-array) field degrades to `[]` rather
        // than forwarding a non-array value through the atomic `loadSucceeded` mount contract.
        const hydratedParaIdMap = Array.isArray(payload.paraIdMap) ? payload.paraIdMap : [];
        const hydratedImportedRevisions = Array.isArray(payload.importedRevisions) ? payload.importedRevisions : [];
        const hydratedImportedComments = Array.isArray(payload.importedComments) ? payload.importedComments : [];
        // Task 011: normalize the server projection defensively via the shared `normalizeProjection`
        // helper (also used by Upload + Browse->project). An older BFF (no projection field) → null →
        // (task 013, F-2) the editor renders an explicit error/unavailable state.
        const hydratedProjection = normalizeProjection(payload.projection);
        dispatch({
          kind: 'loadSucceeded',
          docxBytes: bytes.buffer,
          etag: payload.eTag ?? null,
          versionId: payload.versionId ?? null,
          sessionId: payload.sessionId ?? '',
          sprkDocumentId: payload.documentRecordId,
          fileName: payload.fileName,
          // Set ATOMICALLY with docxBytes (the ComposeEditor mount contract).
          paraIdMap: hydratedParaIdMap,
          importedRevisions: hydratedImportedRevisions,
          importedComments: hydratedImportedComments,
          projection: hydratedProjection,
          // G1 (FR-01, task 020): normalize undefined (older BFF / Path B continuation) to `null`.
          origin: payload.origin ?? null,
        });
      } catch (err) {
        if (ac.signal.aborted) return;
        const message = err instanceof Error ? err.message : String(err);
        dispatch({
          kind: 'loadFailed',
          errorMessage: `Failed to load document: ${message}`,
        });
      }
    })();

    return () => ac.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [
    state.status,
    state.documentRef?.speDriveItemId,
    bffBaseUrl,
    effectiveDriveId,
    tenantId,
    initialSessionId,
    matterId,
  ]);

  // -------------------------------------------------------------------------
  // FR-29 (task 102, gap 4.3) — persist anchored annotations + defined-terms on MUTATION
  // -------------------------------------------------------------------------
  // Closes the write half so annotations survive a reopen: whenever the live annotation state
  // DIVERGES from the last server-synced snapshot (a local mutation — accept/reject/edit, or the
  // Word-return reanchor write-back once that UI is mounted), POST it to the session-annotations
  // route (`POST /api/compose/sessions/{sessionId}/annotations`, ComposeService.SaveComposeAnnotations
  // → the ChatSession's mutable collections → Redis hot + Cosmos warm tiers). A hydrate does NOT
  // write back (the Load effect seeds `syncedAnnotationsRef` to the hydrated snapshot). Reuses the
  // existing `anchoredAnnotations`/`definedTermsTracking` store — no parallel store, no new service.
  React.useEffect(() => {
    if (state.status !== 'loaded' && state.status !== 'saving') return;
    if (!bffBaseUrl || !tenantId || !state.sessionId) return;

    const snapshot = annotationsSnapshot(anchoredAnnotations, definedTermsTracking);
    if (snapshot === syncedAnnotationsRef.current) return; // hydrated or unchanged — no write

    const ac = new AbortController();
    (async () => {
      try {
        const url = `${bffBaseUrl}/api/compose/sessions/${encodeURIComponent(state.sessionId)}/annotations`;
        const response = await authenticatedFetch(url, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ tenantId, anchoredAnnotations, definedTermsTracking }),
          signal: ac.signal,
        });
        if (response.ok && !ac.signal.aborted) {
          // Mark this state as server-synced so we don't re-POST it on the next unrelated render.
          syncedAnnotationsRef.current = snapshot;
        }
      } catch {
        // Non-fatal: a failed annotation persist must never break the editing session. The next
        // mutation retries (the snapshot still differs from the synced ref).
      }
    })();

    return () => ac.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [anchoredAnnotations, definedTermsTracking, state.status, state.sessionId, bffBaseUrl, tenantId]);

  // -------------------------------------------------------------------------
  // Word round-trip shuttle (task 103) — pull (3.4) / reanchor + poll (3.5)
  // -------------------------------------------------------------------------
  // The pull-annotations (051), check-changes (053), and reanchor (054) endpoints + the 054 reanchor
  // banner/panel were all BUILT but never CONNECTED. This block wires them: a return-from-Word
  // poll-on-focus that pulls the current native annotations (3.4) + re-anchors prior anchors (3.5)
  // into the mounted banner/panel. (The "Push to Word" leg (3.1) has been retired.)
  const { summary: reanchorSummary, reanchor: runReanchor, reset: resetReanchor } = useComposeReanchor({ bffBaseUrl });
  const { pull: pullAnnotations } = useComposePullAnnotations({ bffBaseUrl });
  const { checkChanges } = useComposeCheckChanges({ bffBaseUrl });

  // FIX #5 (UAT): Open-in-Word (Web + Desktop) handlers for the consolidated
  // toolbar's "Word" dropdown. Bound HERE (the host) and threaded to ComposeEditor
  // so the shared-lib editor stays decoupled from `@spaarke/document-operations`.
  // Safe to call at mount — the hook only allocates `useState`/`useCallback`; the
  // authenticated fetch fires lazily on click.
  const { openInWeb, openInDesktop, isActing: isWordActing } = useDocumentActions({ bffBaseUrl });

  const [reanchorPanelOpen, setReanchorPanelOpen] = React.useState(false);
  const [pulledAnnotationCount, setPulledAnnotationCount] = React.useState(0);

  // gaps 3.4/3.5 — return-from-Word: on window focus (the user came back from Word), poll
  // check-changes; when the document changed, PULL the current native annotations (3.4) and
  // RE-ANCHOR prior anchors (3.5). The poll fallback needs no webhook secrets (owner task 056 /
  // DEF-03); it drives the same Redis-backed delta/etag substrate the webhook would. The change
  // signal is internal React state (the reanchor summary → banner) — NO PaneEventBus discriminant
  // is emitted (task 104 froze the compose bus discriminant set; adding one is forbidden).
  const runReturnFromWordCheck = React.useCallback(async (): Promise<void> => {
    const speId = state.documentRef?.speDriveItemId;
    if (state.status !== 'loaded') return;
    if (!speId || !effectiveDriveId || !bffBaseUrl || !tenantId) return;
    try {
      const changes = await checkChanges({ documentSpeId: speId, containerId: effectiveDriveId });
      if (!changes.changed) return;

      // 3.4 — surface what Word added (native comments/revisions). Count is test-observable.
      try {
        const pulled = await pullAnnotations({ documentSpeId: speId, driveId: effectiveDriveId, tenantId });
        setPulledAnnotationCount((pulled.comments?.length ?? 0) + (pulled.revisions?.length ?? 0));
      } catch {
        // non-fatal — reanchor is the primary return-from-Word affordance.
      }

      // 3.5 — re-anchor the prior Compose anchors against the updated document → banner/panel.
      const priorAnchors = anchoredAnnotationsToPriorAnchors(anchoredAnnotations);
      await runReanchor({ documentSpeId: speId, driveId: effectiveDriveId, tenantId, priorAnchors });

      // G8 (FR-07, task 030): surface the external change + refresh the projection. Detection resolved
      // by document/version identity (checkChanges above), never content match (NFR-02/I-7).
      //   • CLEAN editor → remount transparently from the server-authoritative bytes. requestLoad
      //     carries externalChange:true so the "Document updated…" banner still renders post-reload.
      //   • DIRTY editor → do NOT remount (NFR-08 — that would silently discard unsaved edits). Set the
      //     banner flag instead; the banner offers an explicit Reload the user chooses (discard-and-
      //     remount is a user action, never silent). This is the guarded path the escalation trigger
      //     requires — a dirty doc is deferred to the user, not silently remounted.
      const editorDirty = editorRef.current?.isDirty() ?? false;
      if (editorDirty) {
        dispatch({ kind: 'externalChangeDetected' });
      } else if (state.documentRef) {
        dispatch({
          kind: 'requestLoad',
          documentRef: state.documentRef,
          sessionId: state.sessionId,
          externalChange: true,
        });
      }
    } catch {
      // Poll/reanchor failures are non-fatal — the editing session continues.
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [
    state.status,
    state.documentRef,
    state.sessionId,
    effectiveDriveId,
    bffBaseUrl,
    tenantId,
    checkChanges,
    pullAnnotations,
    runReanchor,
    anchoredAnnotations,
  ]);

  // UAT #5 fix (task 053): Compose runs EMBEDDED in an iframe (LegalWorkspace embedded mode / model-driven
  // host). Returning from the Word-for-Web tab fires the document's `visibilitychange` (→ visible) reliably,
  // but the iframe `window`'s `focus` event often does NOT — so a `focus`-only listener meant the
  // external-change check never ran and the banner never appeared. Listen for BOTH; an in-flight guard stops
  // the two events (which can both fire on a single return) from double-running the check and double-advancing
  // the shared SPE delta cursor.
  const returnCheckInFlightRef = React.useRef(false);
  React.useEffect(() => {
    if (state.status !== 'loaded') return;
    if (!state.documentRef?.speDriveItemId) return;
    const runGuarded = (): void => {
      if (returnCheckInFlightRef.current) return;
      returnCheckInFlightRef.current = true;
      void runReturnFromWordCheck().finally(() => {
        returnCheckInFlightRef.current = false;
      });
    };
    const onFocus = (): void => runGuarded();
    const onVisibility = (): void => {
      if (document.visibilityState === 'visible') runGuarded();
    };
    window.addEventListener('focus', onFocus);
    document.addEventListener('visibilitychange', onVisibility);
    return () => {
      window.removeEventListener('focus', onFocus);
      document.removeEventListener('visibilitychange', onVisibility);
    };
  }, [state.status, state.documentRef?.speDriveItemId, runReturnFromWordCheck]);

  // gap 3.5 — resolve a flagged/orphaned anchor from the conflict panel. Discard is the only path
  // that removes an annotation (explicit user action — the engine never drops one silently, FR-27);
  // accept/keep retain it. The mutation flows through the existing annotations-persist effect
  // (gap 4.3) so it survives a reopen.
  const handleReanchorResolve = React.useCallback((decision: ReanchorResolutionDecision): void => {
    if (decision.resolution === 'discard') {
      setAnchoredAnnotations(prev => prev.filter(a => a.id !== decision.annotationId));
    }
  }, []);

  // -------------------------------------------------------------------------
  // Multi-tab BroadcastChannel hook — owns "focus-me" + "force-closed" signaling
  // -------------------------------------------------------------------------
  // When a sibling tab posts `force-closed` (after a successful discard), this
  // tab transitions to 'cancelled'.
  const handleForceClosedFromOther = React.useCallback((): void => {
    dispatch({ kind: 'checkoutCancelled' });
  }, []);

  const { postFocusMe, postForceClosed } = useComposeBroadcastChannel(
    state.documentRef?.sprkDocumentId,
    state.sessionId,
    handleForceClosedFromOther
  );

  // -------------------------------------------------------------------------
  // SPE check-out lifecycle hook — owns probe + acquire + force-close + cancel
  // -------------------------------------------------------------------------
  const { forceCloseAndAcquire, discardAndCancel } = useComposeCheckoutLifecycle({
    state,
    dispatch,
    bffBaseUrl,
    postForceClosed,
  });

  // -------------------------------------------------------------------------
  // FU-1 fix — heartbeat gated on checkoutStatus === 'acquired'
  // -------------------------------------------------------------------------
  // Previously lived in ComposeEditor and fired regardless of checkout state;
  // hoisted here and gated so a cancelled tab no longer heart-beats a lock it
  // doesn't hold.
  useComposeHeartbeatGate(state.checkoutStatus, state.documentRef?.sprkDocumentId, bffBaseUrl);

  // -------------------------------------------------------------------------
  // Conflict dialog handler — "Go to that session"
  // -------------------------------------------------------------------------
  const handleGoToOtherSession = React.useCallback((): void => {
    // eslint-disable-next-line no-console
    console.info('[ComposeWorkspace] Conflict dialog: Go to that session');
    postFocusMe();
    dispatch({ kind: 'checkoutCancelled' });
  }, [postFocusMe]);

  // -------------------------------------------------------------------------
  // BFF Save — POST /api/compose/documents/{speId}/save
  // -------------------------------------------------------------------------
  const triggerSave = React.useCallback(
    async (saveMode: ComposeSaveMode = 'version'): Promise<void> => {
      if (state.status !== 'loaded') return;
      if (!state.documentRef || !editorRef.current) return;

      // G7 (FR-06, task 022): "Save New Document" (fork) forces the create-on-save path — a brand-new
      // sprk_document record — EVEN when the doc already has a real SPE item. A fresh transient key is
      // minted so the fork gets its OWN dedup identity, and `forkNew` tells the server to SKIP the
      // transient-key dedup lookup for this call (a deliberate new document, not a new version).
      const forkNew = saveMode === 'new';
      // FR-05 (task 100): a TRANSIENT (Browse/Upload) draft has NO SPE drive-item — it persists via
      // create-on-save into the client-resolved BU container, not the replace path. Branch on the
      // absence of a real speDriveItemId (mountTransient sets it to ''), OR on a deliberate Save-New fork.
      const isTransientCreate = forkNew || !state.documentRef.speDriveItemId;
      // G7: the dedup key to send on a create-on-save. A fork mints a fresh key (its own identity going
      // forward); a normal transient save reuses the mount-time key so repeated saves dedup to ONE record.
      const effectiveTransientKey = forkNew ? mintTransientKey() : state.documentRef.transientKey;
      const saveContainerId = state.documentRef.containerId ?? containerIdRef.current;
      // UAT 2026-07-19 P2: prefer the drive the document actually lives in (captured from the save
      // response after a create-on-save — the born-in-editor doc lands in the BU container's drive,
      // which the host `driveId` prop does NOT identify) over the host default. This is the drive the
      // replace-path save + baseline re-fetch must target.
      const saveDriveId = state.documentRef.driveId ?? effectiveDriveId;

      if (!bffBaseUrl || !tenantId) {
        dispatch({
          kind: 'saveFailed',
          errorMessage: 'Cannot save — BFF base URL or tenant configuration missing.',
        });
        return;
      }
      if (isTransientCreate) {
        if (!saveContainerId) {
          // gap 1.4: don't abort silently as the pre-100 code did — surface an honest, actionable
          // banner. The container resolves from the user's Business Unit; if it's missing the BU is
          // unconfigured (or we're in a non-Dataverse host).
          dispatch({
            kind: 'saveFailed',
            errorMessage:
              'Cannot save this new document — your Business Unit has no storage container configured. ' +
              'Contact an administrator to set the container on your Business Unit.',
          });
          return;
        }
      } else if (!saveDriveId) {
        dispatch({
          kind: 'saveFailed',
          errorMessage: 'Cannot save — SPE drive configuration missing.',
        });
        return;
      }

      dispatch({ kind: 'requestSave' });
      try {
        // R3 FR-01 (task 027): the client STOPS authoring `.docx` bytes. It sends a STRUCTURED, paraId-
        // keyed payload and the SERVER authors the bytes — delta-onto-original for a loaded doc, full
        // render for a born-in-editor doc. `editorRef.current.isDirty()` is the editor's OWN authoritative
        // dirty flag (ComposeEditor's internal `dirtyRef`), read fresh at Save time (NOT the local `isDirty`
        // React state, which only gates the toolbar button and can lag an in-flight import). `state.docxBytes`
        // is the retained ORIGINAL mount bytes — the ONLY bytes the client ever sends (byte-identical
        // passthrough of the RETAINED original; never a reconstruction).
        const editorIsDirty = editorRef.current.isDirty();

        // R4 FR-06 (task 032, the write-path cutover; task 023 retired the paragraph-diff export this replaced):
        // a dirty save of a LOADED doc sends the ordered, rebased task-003 OPERATION LOG (ID-anchored
        // (paraId, runIndex, run-local-offset) ops) the server applies via ComposeShadowPatchEngine — the ONLY
        // dirty-save capture path. Read ONCE here (serializing resets the log + the editor dirty flag). Ops whose
        // anchor landed in later-deleted content (`deletedContentFlag`) are surfaced by the snapshot and EXCLUDED
        // from what we apply — never-silently-dropped, but not re-applied onto content a later edit removed. The
        // born-in-editor create-on-save path authors the whole document via `contentModel`, so it sends no op-log.
        // UAT #1A fix (task 050): an IMPORTED transient mount (Browse/upload — `state.docxBytes` present) that is
        // dirty ALSO captures the op-log, so its create-on-save applies TRACKED redlines via the engine (not the
        // renderer). Only a true born-in-editor doc (`!state.docxBytes`) skips the op-log on the transient path.
        const opLogSnapshot =
          editorIsDirty && (!isTransientCreate || !!state.docxBytes) ? editorRef.current.serializeOperationLog() : null;
        const operationLog = opLogSnapshot
          ? {
              schemaVersion: COMPOSE_OPERATION_SCHEMA_VERSION,
              operations: opLogSnapshot.orderedOps
                .filter(entry => !entry.deletedContentFlag)
                .map(entry => entry.operation),
            }
          : undefined;

        // C2 fix (UAT 2026-07-20): the load-time paraId map — sent on every save so the server can stamp
        // MINTED ids physically onto the retained-original baseline's id-less paragraphs before the
        // synthesizer resolves (a redline accept / edit on an originally-id-less paragraph, or ANY paragraph
        // of an uploaded doc whose ids are all client-minted, otherwise fails with "w14:paraId matches no
        // paragraph in the retained original"). Read-only (no dirty-flag side effect); empty for a
        // born-in-editor doc (no snapshot). The server only applies it when an editedParagraphs delta is
        // present (see ComposeService), so sending it on a clean/born-in-editor save is harmless.
        const paraIdMap = editorRef.current.getBaselineParaIdMap?.() ?? [];

        // Task 040 (comment-export wiring fix): pending AI/user REDLINES persist through the
        // ID-ANCHORED op-log (`operationLog` below) — the interceptor captures the underlying
        // `insertText`/`deleteRange`/`replaceRange` steps as granular, paraId+offset-anchored
        // operations, and `ComposeShadowPatchEngine` applies them to emit `w:ins`/`w:del`. Comments
        // (BOTH the FR-23 session Comments-panel threads AND the NDA-REVIEW advisory threads, task
        // 031's `getAdvisoryCommentThreads()`) ride a SEPARATE, paraId+run-range-anchored path:
        // `ComposeEditorHandle.getAnchoredComments()` resolves each thread's live `commentAnchor` mark
        // span to a durable `(paraId, run-local range)` (D2) and returns `ComposeAnchoredComment[]`,
        // sent below in the `comments` field — `ComposeShadowPatchEngine.ApplyComment` bakes each as a
        // native `w:comment` (ADR-049). This REPLACES the retired `annotations` field
        // (`DocxAnnotationInput`, text-anchored via `targetText`): the server's `SaveComposeDocumentBody`
        // never deserialized an `annotations` property, so every comment previously sent that way was
        // silently dropped (session comments AND advisory comments alike). `?.()` guards an older
        // editor build without the handle.
        const anchoredComments: ComposeAnchoredComment[] =
          typeof editorRef.current.getAnchoredComments === 'function' ? editorRef.current.getAnchoredComments() : [];

        // Base64-encode the RETAINED ORIGINAL bytes via the shared module-level encoder (see
        // `arrayBufferToBase64` above — also used by the FR-03/task 011 browse->project round-trip).
        const encodeRetained = arrayBufferToBase64;

        // FR-05 (task 100): the create-on-save route carries no id in the path (the draft has no drive-item
        // yet) and sends `containerId`; the replace route carries the SPE id + driveId. Both hit
        // ComposeService.SaveAsync — the server branches on DocumentSpeId presence.
        const url = isTransientCreate
          ? `${bffBaseUrl}/api/compose/documents/create-on-save`
          : `${bffBaseUrl}/api/compose/documents/${encodeURIComponent(state.documentRef.speDriveItemId)}/save`;

        // Four structured cases (the client authors NO bytes):
        //  1. Loaded + dirty       → { editedParagraphs, baselineVersionId, content?(retained fast-path) }
        //  2. Loaded + clean       → { content: retained byte-identical } (FR-06a)
        //  3. Born-in-editor        → { contentModel } (AI-draft/blank/edited-browse-local → server renders)
        //  4. Unedited browse-local → { content: retained byte-identical } (FR-06a)
        // Task 040: session + advisory comments ride `comments` (ComposeAnchoredComment[]) in every case;
        // redlines ride the ID-anchored `operationLog` (case 1 only — see below).
        let requestBody: Record<string, unknown>;
        if (isTransientCreate) {
          // UAT #1A fix (task 050): the born-in-editor discriminant is retained-bytes presence ONLY — the SAME
          // signal the replace path uses (`bornInEditor = !state.docxBytes`). Previously this ALSO OR'd in
          // `editorIsDirty`, so a DIRTY imported transient (Browse/upload mount WITH original bytes) rendered from
          // the content model → plain untracked runs → NO redline in Word, and the server stamped it Authored
          // (which then forced every later reopen-save onto the clean branch). Now an imported transient (has
          // bytes) sends its retained original + the tracked op-log so the engine applies w:ins/w:del
          // (create-on-save carries no DocumentRecordId → cleanApply is false → trackChanges true). Only a TRUE
          // born-in-editor doc (no retained bytes) renders from the content model.
          const bornInEditorRender = !state.docxBytes;
          requestBody = {
            containerId: saveContainerId,
            tenantId,
            sessionId: state.sessionId,
            displayName: state.documentRef.fileName ?? null,
            comments: anchoredComments,
            // C2: the baseline paraId map — needed on the imported-transient op-log branch so the server can stamp
            // client-minted ids onto the retained baseline before the engine resolves anchors (harmless on the
            // born-in-editor render branch — the server skips the stamp when ContentModel is present).
            paraIdMap,
            // G7 (task 022): the transient dedup key (repeated create-on-save → ONE record) + the Save-New
            // fork flag (forkNew=true skips dedup → a deliberately new record). effectiveTransientKey is the
            // mount-time key for a normal transient save, or a freshly-minted one for a fork.
            transientKey: effectiveTransientKey,
            forkNew,
            ...(bornInEditorRender
              ? { contentModel: editorRef.current.buildContentModel() }
              : {
                  // Imported transient (Browse/upload-mounted, has original bytes): send the retained ORIGINAL
                  // bytes as the baseline + the tracked op-log so the server applies redlines via
                  // ComposeShadowPatchEngine — NOT the renderer. operationLog is undefined on a clean mount →
                  // the server persists the retained bytes byte-identical (FR-06a).
                  content: encodeRetained(state.docxBytes!),
                  operationLog,
                }),
          };
        } else {
          // task 039 (UAT round 1+2, born-in-editor 2nd-save fix): a BORN-IN-EDITOR doc (blank page / AI-draft)
          // holds NO retained original bytes (`!state.docxBytes`, the SAME discriminant the create path's
          // `bornInEditorRender` uses) and its create-on-save returned the drive-ITEM id as VersionId — there is
          // no real SPE baseline version to re-fetch. So on EVERY in-session replace save it RE-AUTHORS via
          // `contentModel` (mirroring the create path), never entering the baseline-less op-log path that 400s.
          // The server renders the .docx and ReplaceFileContentAsUserAsync's it onto the EXISTING item (same
          // speDriveItemId) — updating in place, no duplicate record. A LOADED/imported doc (docxBytes present)
          // keeps the op-log + baseline path below EXACTLY as-is → tracked changes (REQ-2 not regressed).
          //
          // G1/G2 (FR-01/FR-02, tasks 020/021 — Candidate A): a REOPENED AUTHORED doc (persisted
          // sprk_composeorigin=authored, `state.docxBytes` present) now takes the SAME op-log path as an
          // imported doc — it sends its retained baseline + operation log, and the SERVER applies the ops
          // CLEAN (plain runs, physical deletes, no redlines) by reading the durable marker (engine
          // trackChanges:false). This is the highest-fidelity path: only document.xml is re-serialized, every
          // other package part + untouched subtree stays byte-identical — NOT a re-author-from-content-model
          // (which drops headers/footers/styles on rich docs and violates ADR-049 I-1/I-2/I-4). The client no
          // longer branches on origin here; the durable marker drives clean-vs-tracked server-side (NFR-02 —
          // never inferred). See notes/g2-clean-apply-decision.md (operator resolution 2026-07-29).
          //
          // The ONLY contentModel case that remains on the replace route is an in-session BORN-IN-EDITOR
          // re-save (`!state.docxBytes`): a blank/AI-draft doc holds NO retained baseline (its create-on-save
          // returned the drive-ITEM id as VersionId, not a real SPE version), so the baseline-less op-log path
          // 400s (task 039). It re-authors via contentModel, mirroring the create path — this is authored
          // ORIGINATION through the renderer (the two-byte-author split), never authored EDITING through the op
          // log; both stay clean.
          const bornInEditor = !state.docxBytes;
          requestBody = {
            // UAT 2026-07-19 P2: the drive the doc lives in (documentRef.driveId after a create-on-save),
            // falling back to the host default — so a born-in-editor doc's second save + baseline re-fetch
            // target the correct drive.
            driveId: saveDriveId,
            tenantId,
            sessionId: state.sessionId,
            // G2: the server reads the durable sprk_composeorigin marker for THIS record to pick clean-vs-
            // tracked apply — so documentRecordId MUST ride every reopened-doc save (already sent since G1).
            documentRecordId: state.documentRef.sprkDocumentId ?? null,
            displayName: state.documentRef.fileName ?? null,
            comments: anchoredComments,
            // C2: the baseline paraId map so the server can stamp minted ids before the engine resolves anchors
            // (harmless on the contentModel branch — the server skips the stamp when ContentModel is present).
            paraIdMap,
            ...(bornInEditor
              ? // In-session born-in-editor re-save: re-author from the content model (no retained baseline to
                // delta onto — task 039). The renderer authors clean bytes; no op-log.
                { contentModel: editorRef.current.buildContentModel() }
              : {
                  // Loaded/imported OR reopened-authored dirty save. The load-time version id = the op-log's BASE
                  // VERSION; lets the server re-fetch the baseline even without the client bytes. The server
                  // applies the op-log tracked (imported) or CLEAN (authored, per the durable marker) — REQ-1/REQ-2.
                  baselineVersionId: state.versionId ?? undefined,
                  // Same-session fast-path: still holding the retained original → send it (byte-identical).
                  content: state.docxBytes ? encodeRetained(state.docxBytes) : undefined,
                  // R4 FR-06 (task 032): the ordered, rebased operation log the server applies via the Patch
                  // Engine onto the resolved baseline (undefined on a clean save → baseline persists byte-identical).
                  operationLog,
                }),
          };
        }

        const response = await authenticatedFetch(url, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(requestBody),
        });

        if (!response.ok) {
          // Try to extract ProblemDetails.detail so the banner surfaces the
          // actual server-side reason (BFF puts exception name + message +
          // TraceId in `detail`). Fall back to a generic message if the body
          // isn't JSON.
          let detail = '';
          try {
            const problem = (await response.clone().json()) as {
              detail?: string;
              title?: string;
            };
            detail = problem.detail ?? problem.title ?? '';
          } catch {
            detail = (await response.text().catch(() => '')).slice(0, 400);
          }
          // UAT #10/#11 (task 052): a 423 means the doc is held by a Word-for-web CO-AUTHORING lock (Spaarke
          // never does a formal checkout, so a 423 is ALWAYS co-authoring). There is no programmatic unlock —
          // flag it so the banner shows the honest "Open in Word" bar with Retry + Reload-from-Word (not a fake
          // Unlock, and not the old misleading "checked out — check it in" copy). The server detail already
          // carries the honest message.
          if (response.status === 423) {
            dispatch({
              kind: 'saveFailed',
              errorMessage:
                detail ||
                'This document is open in Word — close it there, then Retry. It also releases automatically within a few minutes. Your Compose changes are safe and still pending.',
              isLock: true,
            });
            return;
          }
          const msg =
            response.status === 403
              ? `You do not have permission to save this document. ${detail}`.trim()
              : `Failed to save document (HTTP ${response.status})${detail ? `: ${detail}` : ''}.`;
          dispatch({ kind: 'saveFailed', errorMessage: msg });
          return;
        }

        const payload = (await response.json()) as {
          documentSpeId: string;
          documentRecordId?: string;
          // #1(b): the create-on-save response's new-document id. `documentRecordId` is the
          // established field (the `sprk_documentid`); `documentId` is read defensively in case the
          // sibling BFF change names it that. Either drives the Saved ✓ banner's "Open preview" link.
          documentId?: string;
          // UAT 2026-07-19 P2: driveId + versionId of the just-saved SPE version. Retained via
          // saveSucceeded so a subsequent replace-path save of a born-in-editor doc (no retained
          // bytes) resolves its baseline by re-fetching this version. Optional (older BFF omits them).
          driveId?: string;
          versionId?: string;
          eTag?: string;
          size: number;
          wasPromotedThisSave: boolean;
          // Prong 1 (task 055): best-effort partial-apply summary — present only when some ops couldn't be
          // anchored server-side (the save still succeeded with the resolvable edits). Absent on the common
          // clean-batch path. Drives the honest "N edits couldn't be saved — please redo them" banner.
          partialApply?: {
            total: number;
            appliedCount: number;
            unresolvedCount: number;
          } | null;
        };

        // #1(b): the persisted document id drives the Assistant's persistent "Saved to the DMS" chat
        // affordance (FIX #7a below).
        const savedDocumentId = payload.documentRecordId ?? payload.documentId;
        if (savedDocumentId) {
          // FIX #7a: report the completed Save to the Assistant so it posts a PERSISTENT confirmation
          // + "Open preview" chat message. No-op (null delegate) on a standalone mount. The transient
          // in-editor banner's preview link is dropped (below) now that the persistent affordance lives
          // in chat; the banner's success signal is unaffected.
          notifyComposeSaveCompletedRef.current?.({
            documentRecordId: savedDocumentId,
            fileName: state.documentRef.fileName,
          });
        }

        dispatch({
          kind: 'saveSucceeded',
          sprkDocumentId: payload.documentRecordId,
          // gap 1.7: carry the server-minted SPE id back into documentRef.speDriveItemId so a second
          // Save on this mount takes the replace path (no longer transient).
          documentSpeId: payload.documentSpeId,
          etag: payload.eTag ?? null,
          // UAT 2026-07-19 P2: retain the just-saved drive id + version id so the replace-path second
          // save of a born-in-editor doc can resolve its baseline (server re-fetch by versionId).
          driveId: payload.driveId,
          versionId: payload.versionId,
          // Prong 1 (task 055): surface a best-effort partial-apply outcome (some ops couldn't be anchored)
          // so the banner prompts the user to redo just those edits. Only carried when the server actually
          // recovered — a clean batch omits it (→ null → clears any prior partial-apply banner).
          partialApply: payload.partialApply && payload.partialApply.unresolvedCount > 0 ? payload.partialApply : null,
        });
        // Clear the local dirty flag so the Save button disables until the
        // next edit. ComposeEditor's internal dirtyRef also resets on the
        // next load; here we mirror that for post-save.
        setIsDirty(false);

        // task 038 (zero-error guardrails): NOW that the save is confirmed (200), commit the persisted
        // op-log batch + recompute the editor's dirty flag. `serializeOperationLog()` no longer resets on
        // read (that was the data-loss bug — a 422 emptied the log BEFORE the POST, so a retry re-sent an
        // empty log and lost every valid text edit in the batch); this post-200 commit is what finally drops
        // the batch, and ONLY on success. A rejected save returned at the `!response.ok` guard above, so it
        // never reaches here — the op-log + dirty flag survive for a retry that re-sends the same edits.
        // Called AFTER setIsDirty(false) so `commitSaved`'s onDirtyChange (true iff concurrent edits arrived
        // during the in-flight save) is the last writer and leaves the Save state correct. Gated on having
        // actually SENT an op-log: the born-in-editor create-on-save path re-derives its whole content model
        // each save (buildContentModel), so it needs no op-log commit.
        if (operationLog) {
          editorRef.current?.commitSaved?.();
        }

        // FR-05 (task 100, gap 1.8): once a transient draft is persisted as a NEW sprk_document,
        // let the host write any chosen parent association (associate() no-ops on "none"). The
        // document already exists, so an association failure is non-fatal — do not surface it as a
        // save failure.
        if (isTransientCreate && onCreateOnSaveComplete && payload.documentRecordId) {
          try {
            await onCreateOnSaveComplete(payload.documentRecordId);
          } catch (assocErr) {
            // eslint-disable-next-line no-console
            console.warn('[ComposeWorkspace] create-on-save association write failed (non-fatal):', assocErr);
          }
        }
      } catch (err) {
        const message = err instanceof Error ? err.message : String(err);
        dispatch({ kind: 'saveFailed', errorMessage: `Save failed: ${message}` });
      }
    },
    [
      state.status,
      state.documentRef,
      state.docxBytes,
      state.sessionId,
      bffBaseUrl,
      effectiveDriveId,
      tenantId,
      onCreateOnSaveComplete,
    ]
  );

  // G10 (FR-09, task 040): manual "Refresh Profile" — re-run the Document Profile on demand for a PROMOTED
  // doc (it has a sprk_document record to profile). Fire-and-forget on the server (202); best-effort here —
  // a failure is non-fatal (the profile is background/best-effort anyway). Only meaningful once the doc is
  // promoted, so the button is wired (below) only when sprkDocumentId exists.
  // UAT #9 (task 054): transient "profiling…" spinner state so the manual click gives visible feedback.
  const [isRefreshingProfile, setIsRefreshingProfile] = React.useState(false);
  const triggerRefreshProfile = React.useCallback(async (): Promise<void> => {
    const recordId = state.documentRef?.sprkDocumentId;
    if (!recordId || !bffBaseUrl || !tenantId) return;
    // UAT #9 (task 054): the profile re-run is a fire-and-forget 202 with no server-visible result. Surface
    // a brief "profiling…" spinner on the button so the user SEES that the click did something (the UAT
    // complaint was that neither the automatic re-trigger nor the manual button gave any visible signal).
    setIsRefreshingProfile(true);
    try {
      await authenticatedFetch(`${bffBaseUrl}/api/compose/documents/${encodeURIComponent(recordId)}/refresh-profile`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          tenantId,
          documentSpeId: state.documentRef?.speDriveItemId || undefined,
          eTag: state.etag || undefined,
        }),
      });
      // Keep the spinner visible briefly so a fast 202 still registers as a deliberate action.
      window.setTimeout(() => setIsRefreshingProfile(false), 1500);
    } catch (err) {
      setIsRefreshingProfile(false);
      // eslint-disable-next-line no-console
      console.warn('[ComposeWorkspace] refresh-profile request failed (non-fatal):', err);
    }
  }, [state.documentRef?.sprkDocumentId, state.documentRef?.speDriveItemId, state.etag, bffBaseUrl, tenantId]);

  // FIX #1b — publish the editor's Save into the cross-pane bridge so the Assistant's "Add the
  // document to the DMS" chip (ConversationPane) drives the SAME create-on-save / save-to-matter
  // flow (`triggerSave`) via a DIRECT call — no PaneEventBus discriminant. No-op outside the bridge
  // provider (standalone LegalWorkspace mount).
  //
  // spaarkeai-compose-r2 (multi-Compose-tab): the bridge Save slot is single-writer (last-mounted
  // instance wins). Gate the bridge-facing wrapper so an INACTIVE tab's instance holding the slot
  // does NOT save the WRONG document when the chip is issued while a DIFFERENT Compose tab is active —
  // only the ACTIVE tab services the chip. The toolbar / Ctrl+S paths call `triggerSave` DIRECTLY and
  // stay ungated (a hidden inactive tab has no reachable toolbar/focus, so those can only fire on the
  // active tab anyway). The wrapper is stable identity (reads a ref) yet always dispatches the LATEST
  // triggerSave, so the chip hits the current save state.
  const triggerSaveRef = React.useRef(triggerSave);
  triggerSaveRef.current = triggerSave;
  const handleBridgeSave = React.useCallback((): void | Promise<void> => {
    if (isActiveTabRef.current === false) return;
    return triggerSaveRef.current();
  }, []);
  useRegisterComposeSaveHandler(handleBridgeSave);

  // Keyboard shortcut: Ctrl/Cmd+S → save.
  React.useEffect(() => {
    if (state.status !== 'loaded') return;
    const handler = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') {
        e.preventDefault();
        void triggerSave();
      }
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [state.status, triggerSave]);

  // -------------------------------------------------------------------------
  // FR-04 draft-into-editor — render-follows-store materialization (task 016)
  // -------------------------------------------------------------------------
  // Materializes the drafted content into the editor FROM the stored ledger entry (ADR-040:
  // storage precedes rendering; the client re-reads the durable ledger, never a client buffer).
  // `targetLedgerRef` selects a specific stored output ({bindingId}@t{n}); when omitted, the
  // CURRENT (highest-turn) compose output for the session is materialized — the refresh-durable
  // and supersession/undo-replace resolution (FR-17 foundation).
  // DEF-13 — turn an AI edit's rationale into an anchored COMMENT annotation (see the call site in
  // materializeComposeDraftFromLedger for the full pipeline rationale). Pure state update; dedups by
  // the ledger key's derived annotation id so re-materialize (refresh/duplicate signal) is idempotent.
  const registerAiEditReasonComment = React.useCallback(
    (payload: ComposeDraftPayload, provenance: { ledgerRef: string; bindingId: string }): void => {
      const rationale = payload?.rationale?.trim();
      const targetText = payload?.target_text?.trim();
      if (!rationale || !targetText) return; // no reason, or no span to anchor a comment to
      const annotationId = `ai-edit-reason:${provenance.ledgerRef}`;
      setAnchoredAnnotations(prev => {
        if (prev.some(a => a.id === annotationId)) return prev; // idempotent
        const comment: AnchoredAnnotation = {
          id: annotationId,
          type: 'comment',
          anchor: { textPattern: targetText, paragraphHint: -1, spanId: provenance.ledgerRef },
          body: rationale,
          author: 'Spaarke Assistant',
          timestamp: new Date().toISOString(),
          source: 'ai',
          provenance: { bindingId: provenance.bindingId, ledgerRef: provenance.ledgerRef },
        };
        return [...prev, comment];
      });
    },
    []
  );

  // DEF-11 — register a whole-document revision's REVIEW FLAGS (`flag-risks` intent → payload.comments)
  // as anchored `comment` AnchoredAnnotations, persisted via the FR-29 session-annotations endpoint
  // (gap-4.3 effect) so they survive a reopen and show in the annotations sidebar. No accept/reject
  // (flags carry no edit). Deduped by the ledger key + index so a re-materialize (refresh / duplicate
  // signal) never appends duplicates.
  // NOTE (task 040, comment-export wiring fix): these flags do NOT currently export as native
  // `w:comment`s on Save/export — the retired `annotations`→PushAnnotations→DocxAnnotationWriter path
  // this comment used to describe was never wired to Save (`SaveComposeDocumentBody` has no
  // `annotations` property) and PushAnnotations itself is no longer called (Push-to-Word was
  // retired). Task 040 wires the FR-23 session Comments-panel threads + NDA-REVIEW advisory threads
  // (paraId+range-anchored `ComposeAnchoredComment`s) into the Save `comments` field; these
  // `textPattern`-anchored AI-review flags are a SEPARATE data source (the FR-29 AnchoredAnnotation
  // store) and are out of that task's scope — tracked as a follow-on, not fixed here.
  const registerAiReviewComments = React.useCallback(
    (
      comments: Array<{ target_text?: string; comment?: string }>,
      provenance: { ledgerRef: string; bindingId: string }
    ): void => {
      const flags = comments
        .map((c, i) => ({ i, target: c?.target_text?.trim() ?? '', body: c?.comment?.trim() ?? '' }))
        .filter(c => c.target.length > 0 && c.body.length > 0);
      if (flags.length === 0) return;
      setAnchoredAnnotations(prev => {
        const existing = new Set(prev.map(a => a.id));
        const additions: AnchoredAnnotation[] = [];
        for (const flag of flags) {
          const annotationId = `ai-review:${provenance.ledgerRef}#${flag.i}`;
          if (existing.has(annotationId)) continue;
          additions.push({
            id: annotationId,
            type: 'comment',
            anchor: { textPattern: flag.target, paragraphHint: -1, spanId: provenance.ledgerRef },
            body: flag.body,
            author: 'Spaarke Assistant',
            timestamp: new Date().toISOString(),
            source: 'ai',
            provenance: { bindingId: provenance.bindingId, ledgerRef: provenance.ledgerRef },
          });
        }
        return additions.length > 0 ? [...prev, ...additions] : prev;
      });
    },
    []
  );

  // -------------------------------------------------------------------------
  // ai-advanced-capabilities-nda-r1 task 030 / agreements-r1 task 032 — review-summary docked panel
  // state (FR-07/FR-16). Moved ABOVE `materializeComposeDraftFromLedger` (was declared after it) so
  // the ledger-restore path below can populate it too — reopen restores rows + badges + overallRisk,
  // not just the gutter (task 032 gap #1). Captures the SAME `advisoryComments` projection the LIVE
  // `onAdvisoryComments` receiver further down also writes to (ADR-040 — one ledgered NDA-REVIEW
  // result, two renderings, never a second server read).
  const [reviewSummaryFindings, setReviewSummaryFindings] = React.useState<readonly NdaReviewFindingSummary[]>([]);
  const [reviewSummaryOpen, setReviewSummaryOpen] = React.useState<boolean>(false);
  const [reviewSummaryFailedCount, setReviewSummaryFailedCount] = React.useState<number>(0);
  // Task 032 — server-asserted overall risk (the event/payload field task 030 planted but nothing
  // consumed yet — see `ComposeReviewPayload.overallRisk`'s JSDoc: "carried for the summary panel
  // (restore is task 032)"). Combined across MULTIPLE findings outputs via `deriveOverallRisk`
  // (worst-of). Threaded to `AgreementReviewSummaryPanel`'s existing (currently inert per UAT
  // round-5 #2) `overallRisk` prop — NOT re-introducing the removed banner, just completing the data
  // path so it is available/correct rather than silently dropped.
  const [reviewSummaryOverallRisk, setReviewSummaryOverallRisk] = React.useState<string | undefined>(undefined);
  // Task 032 (128KB budget, Leg B) — see `ComposeReviewFindingsDegraded` JSDoc for the full rationale.
  const [reviewFindingsDegraded, setReviewFindingsDegraded] = React.useState<ComposeReviewFindingsDegraded | null>(
    null
  );
  // Which session `reviewSummaryFindings` currently reflects — reset the accumulator when the
  // untargeted (reopen) pass starts materializing a DIFFERENT session's outputs, so a document switch
  // never leaves a PRIOR document's stale findings visible. The LIVE `onAdvisoryComments` handler
  // (wholesale-replace semantics, unchanged) also updates this ref so the two paths never fight.
  const reviewSummarySessionRef = React.useRef<string | null>(null);

  // FR-14 (ai-advanced-capabilities-agreements-r1 task 051) — "Create Summary Memo" toolbar control
  // state. `memoActionInFlight` gates BOTH actions (they are mutually exclusive, user-triggered one at
  // a time — the toolbar disables both + spins the trigger while either is running).
  // `memoActionMessage` surfaces the honest negative state ("generate the review memo first" — 404, no
  // memo persisted yet) or a transient network-failure message; never a silent empty export.
  const [memoActionInFlight, setMemoActionInFlight] = React.useState(false);
  const [memoActionMessage, setMemoActionMessage] = React.useState<string | null>(null);
  const [memoEmailOpen, setMemoEmailOpen] = React.useState(false);
  const [memoEmailSubject, setMemoEmailSubject] = React.useState('');
  const [memoEmailBody, setMemoEmailBody] = React.useState('');

  /**
   * Reads the ProblemDetails `code` extension from a non-OK memo response (agreements-r1 UAT round-1 #2)
   * so the toolbar can tell "session not bound to an Analysis" (promote first — the review is NOT lost)
   * apart from "no memo persisted yet" (generate first). Never throws — a missing/unparseable body
   * yields `null`, so a genuine transport/server error still falls through to generic handling.
   */
  const readMemoProblemCode = React.useCallback(async (response: Response): Promise<string | null> => {
    try {
      const body = (await response.clone().json()) as { code?: unknown } | null;
      return typeof body?.code === 'string' ? body.code : null;
    } catch {
      return null;
    }
  }, []);

  /**
   * Shared READ of the persisted review-memo record (render-from-persisted, the project's binding FR-14
   * constraint: both the .docx download and the email prefill derive from the SAME server-persisted
   * `sprk_analysisoutput` row task 050's POST assembled — never a client-side re-derivation).
   * Returns a discriminated outcome so the two distinct negative states are surfaced accurately
   * (agreements-r1 UAT round-1 #2): `no-memo` (nothing generated yet for the bound Analysis) vs.
   * `session-not-bound` (the direct-Compose door — promote to an Analysis first). Throws only on a
   * genuine transport/server failure.
   */
  const fetchReviewMemo = React.useCallback(async (): Promise<
    { kind: 'ok'; memo: ReviewMemoReadResponse } | { kind: 'negative'; message: string }
  > => {
    if (!bffBaseUrl || !state.sessionId) return { kind: 'negative', message: MEMO_NO_MEMO_MESSAGE };
    const response = await authenticatedFetch(
      `${bffBaseUrl}/api/ai/chat/sessions/${encodeURIComponent(state.sessionId)}/review-memo`,
      { method: 'GET' }
    );
    if (response.ok) {
      return { kind: 'ok', memo: (await response.json()) as ReviewMemoReadResponse };
    }
    const negative = selectMemoNegativeMessage(response.status, await readMemoProblemCode(response));
    if (negative) return { kind: 'negative', message: negative };
    throw new ApiError(`Failed to read the review memo (${response.status})`, response.status);
  }, [bffBaseUrl, state.sessionId, readMemoProblemCode]);

  /**
   * "Generate memo" — downloads the SERVER-RENDERED .docx (title, doc/analysis metadata, per-section
   * table) via the docx READ endpoint. A blob download, not a client-side render — the .docx byte
   * authoring stays server-side (ComposeDocumentRenderer), matching every other Compose document write.
   */
  const handleGenerateMemo = React.useCallback(async (): Promise<void> => {
    if (!bffBaseUrl || !state.sessionId || memoActionInFlight) return;
    setMemoActionInFlight(true);
    setMemoActionMessage(null);
    try {
      const response = await authenticatedFetch(
        `${bffBaseUrl}/api/ai/chat/sessions/${encodeURIComponent(state.sessionId)}/review-memo/docx`,
        { method: 'GET' }
      );
      if (!response.ok) {
        // Split negatives (agreements-r1 UAT round-1 #2): 404/no-memo → "generate first";
        // 400/session-not-bound → "promote to an Analysis first" (never a dead-end "Failed (400)").
        const negative = selectMemoNegativeMessage(response.status, await readMemoProblemCode(response));
        if (negative) {
          setMemoActionMessage(negative);
          return;
        }
        throw new ApiError(`Failed to generate the review memo (${response.status})`, response.status);
      }

      const blob = await response.blob();
      const disposition = response.headers.get('content-disposition') ?? '';
      const match = /filename\*?=(?:UTF-8''|")?([^";]+)"?/i.exec(disposition);
      const fileName = match?.[1] ? decodeURIComponent(match[1]) : 'Review Summary Memo.docx';

      const url = URL.createObjectURL(blob);
      try {
        const link = document.createElement('a');
        link.href = url;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
      } finally {
        URL.revokeObjectURL(url);
      }
    } catch (err) {
      setMemoActionMessage(err instanceof ApiError ? err.message : 'Could not generate the review memo.');
    } finally {
      setMemoActionInFlight(false);
    }
  }, [bffBaseUrl, state.sessionId, memoActionInFlight, readMemoProblemCode]);

  /**
   * "Email memo" — reads the persisted memo (JSON) and opens the canonical `<EmailComposer />`
   * (ADR-045) prefilled with a deterministic HTML body + the "Review Summary Memo — {analysis name}"
   * subject. Opens the dialog only; the user must act to send (never auto-send).
   */
  const handleEmailMemo = React.useCallback(async (): Promise<void> => {
    if (!bffBaseUrl || !state.sessionId || memoActionInFlight) return;
    setMemoActionInFlight(true);
    setMemoActionMessage(null);
    try {
      const outcome = await fetchReviewMemo();
      if (outcome.kind === 'negative') {
        // 'no-memo' → generate first; 'session-not-bound' → promote to an Analysis first
        // (agreements-r1 UAT round-1 #2). Never opens the composer with blank content.
        setMemoActionMessage(outcome.message);
        return;
      }
      setMemoEmailSubject(buildReviewMemoEmailSubject(outcome.memo));
      setMemoEmailBody(buildReviewMemoEmailBody(outcome.memo));
      setMemoEmailOpen(true);
    } catch (err) {
      setMemoActionMessage(err instanceof ApiError ? err.message : 'Could not load the review memo.');
    } finally {
      setMemoActionInFlight(false);
    }
  }, [bffBaseUrl, state.sessionId, memoActionInFlight, fetchReviewMemo]);
  // Task 032 — exact-key idempotency for findings outputs materialized via the LEDGER path (`key:`
  // tokens, scoped by session — ledger turn numbers are session-local so a bare key could collide
  // across two different sessions) PLUS content-signature bookkeeping for the 031-residual dedupe
  // guard (`sig:` tokens — see `computeAdvisorySignature`'s JSDoc).
  const materializedFindingsKeysRef = React.useRef<Set<string>>(new Set());

  // DEF-11: a whole-document revision (`compose-revise-document`) carries a CHANGE LIST (`edits[]`)
  // and/or REVIEW FLAGS (`comments[]`). A single-edit draft (compose-draft-alternative /
  // compose-draft-document) has neither — it keeps the shipped single-materialize path. Extracted
  // (task 032) so the untargeted (reopen) pass can select + materialize the latest EDIT-shaped output
  // independently of the findings loop below (the coexistence fix — a later edit no longer evicts an
  // earlier review's findings durability).
  const materializeEditOutput = React.useCallback(
    (editor: ComposeEditorHandle, target: ComposeLedgerOutput): void => {
      const provenance = {
        ledgerRef: target.key, // {bindingId}@t{n} provenance
        bindingId: target.bindingId,
        turn: target.turn,
      };
      const editList = Array.isArray(target.payload?.edits) ? target.payload.edits : null;
      const commentList = Array.isArray(target.payload?.comments) ? target.payload.comments : null;
      if (editList && editList.length > 0) {
        editor.materializeComposeEdits(editList, provenance); // multi-change redline
      } else {
        editor.materializeComposeDraft(target.payload, provenance); // single-edit redline (unchanged)
      }
      setLastMaterializedKey(target.key);
      setComposeDraftError(null);

      // DEF-13 — register the AI edit's REASON as an anchored COMMENT annotation on the change,
      // persisted via the FR-29 session-annotations endpoint (gap-4.3 effect) — see the task-040
      // NOTE on `registerAiReviewComments` above: this rationale does NOT currently export as a
      // native `w:comment` on Save (that PushAnnotations/DocxAnnotationWriter path is retired/never
      // wired to Save); tracked as a follow-on, out of task 040's scope. Anchored to the edit's
      // `target_text` (the redline range); skipped for an insertion-style draft with no target (no
      // span to anchor a comment to) or an empty rationale. Deduped by the ledger key so a
      // refresh/duplicate materialize never appends a second copy.
      // DEF-11: a whole-document revision's REVIEW FLAGS become anchored comments (flag-risks intent).
      // A single-edit draft instead contributes its rationale as ONE anchored comment (DEF-13).
      if (commentList && commentList.length > 0) {
        registerAiReviewComments(commentList, provenance);
      } else {
        registerAiEditReasonComment(target.payload, {
          ledgerRef: target.key,
          bindingId: target.bindingId,
        });
      }
    },
    [registerAiEditReasonComment, registerAiReviewComments]
  );

  // FR-16 (task 030, extended task 032) — DURABLE AGREEMENT-REVIEW findings materialization. A
  // compose-disposition output carrying `flaggedSections[]` is an agreement-review result (the review
  // Binding's Informational→Compose flip, task 030), NOT an edit/comment payload. Re-materializes each
  // flagged clause as a PERSISTENT advisory comment thread via ComposeEditorHandle.placeAdvisoryComments
  // — the SAME metadata-preserving path the LIVE review dispatch uses (the onAdvisoryComments receiver
  // below) — so reopening a reviewed document restores the gutter Review Notes AND the summary panel
  // (task 032 gap #1) deterministically, with riskLevel/sectionRef/standardRef/flaggedClause/assessment
  // intact and ZERO LLM re-run (the stored SessionOutput is READ, never re-dispatched). Routed here, NOT
  // via registerAiReviewComments (which DROPS that metadata — spec FR-16 / CLAUDE.md §11).
  const materializeFindingsOutput = React.useCallback(
    (
      editor: ComposeEditorHandle,
      output: ComposeLedgerOutput,
      reviewPayload: ComposeReviewPayload,
      flaggedSections: readonly unknown[]
    ): void => {
      const sessionScope = state.sessionId;
      const keyToken = `${sessionScope}::key:${output.key}`;
      if (materializedFindingsKeysRef.current.has(keyToken)) return; // exact-key idempotency (ledger reentry)

      const advisoryItems = projectLedgerFindingsToAdvisoryComments(flaggedSections);
      if (advisoryItems.length === 0) {
        // A findings-shaped payload that yields NO usable items (empty/malformed flaggedSections):
        // log + skip gracefully — never crash, never partial-place, never fall through to a redline.
        // eslint-disable-next-line no-console
        console.warn(
          '[ComposeWorkspace] compose findings payload carried no usable flagged sections — nothing re-materialized:',
          output.key
        );
        materializedFindingsKeysRef.current.add(keyToken);
        // Task 032 (128KB budget, Leg B, acceptance criterion 5): the entry EXISTS but nothing usable
        // is inside it — a corrupted/partial payload. Surface a visible degraded-restore notice rather
        // than a silent no-op (never silent absence).
        setReviewFindingsDegraded(prev => prev ?? { expectedCount: 0, reason: 'malformed' });
        return;
      }

      // Task 032 (031-residual dedupe guard): the LIVE onAdvisoryComments path may already have
      // placed this EXACT set of clauses in this mount (e.g. a same-mount `externalChange` re-trigger
      // racing the live placement — notes/031-execution-notes.md "Residual risk"). placeAdvisoryComments
      // has no idempotency of its own, so check the content signature BEFORE placing.
      const signatureToken = `${sessionScope}::sig:${computeAdvisorySignature(advisoryItems)}`;
      if (materializedFindingsKeysRef.current.has(signatureToken)) {
        materializedFindingsKeysRef.current.add(keyToken); // cover this ledger key too — no re-check needed
        return;
      }

      const result = editor.placeAdvisoryComments(advisoryItems);
      if (result.failed.length > 0) {
        // Surface unresolved anchors the SAME way the live onAdvisoryComments path does
        // (FR-19 "do not guess" — reported, never silently placed). Tier-3 targetText is not
        // logged beyond this placement-failure report.
        // eslint-disable-next-line no-console
        console.warn(
          `[ComposeWorkspace] ${result.failed.length} of ${advisoryItems.length} re-materialized advisory ` +
            'comment(s) could not be anchored (strict resolution failed):',
          result.failed
        );
      }

      materializedFindingsKeysRef.current.add(keyToken);
      materializedFindingsKeysRef.current.add(signatureToken);

      // Task 032 gap #1 — summary-panel restore: mirror the live onAdvisoryComments capture so reopen
      // repopulates rows + risk badges, not just the gutter. AGGREGATED across MULTIPLE findings
      // outputs (task 032 gap #3 coexistence) — never wholesale-replaced, unlike the live path (which
      // only ever has ONE result per turn).
      setReviewSummaryFindings(prev => [
        ...prev,
        ...advisoryItems.map(item => ({
          sectionRef: item.sectionRef,
          quotedText: item.targetText,
          riskLevel: item.riskLevel,
          explanation: item.explanation,
          standardRef: item.standardRef,
        })),
      ]);
      setReviewSummaryFailedCount(prev => prev + result.failed.length);
      if (reviewPayload.overallRisk) {
        setReviewSummaryOverallRisk(prev => {
          const combined = deriveOverallRisk([{ riskLevel: prev }, { riskLevel: reviewPayload.overallRisk }]);
          return combined ?? prev; // an unrecognized severity string never erases a KNOWN prior value
        });
      }
      // A successful restore supersedes any earlier degraded notice for this session.
      setReviewFindingsDegraded(null);

      // Task 032 (128KB budget, Leg B) — refresh the durability marker with the latest known-good
      // count so a LATER reopen (same tab) can detect a truncated/skipped entry.
      writeReviewFindingsMarker(sessionScope, advisoryItems.length);
    },
    [state.sessionId]
  );

  // Dispatches a single resolved ledger output to the findings or edit/comment/redline branch — the
  // TARGETED materialize path (a specific known `{bindingId}@t{n}` from a Flow-5 signal or the FR-16
  // idempotent-duplicate-signal test). The UNTARGETED (reopen) path below does NOT use this — it
  // processes ALL findings outputs + the latest edit output directly (task 032 gap #3 coexistence).
  const materializeSingleOutput = React.useCallback(
    (editor: ComposeEditorHandle, target: ComposeLedgerOutput): void => {
      const reviewPayload = target.payload as ComposeReviewPayload;
      const flaggedSections = Array.isArray(reviewPayload.flaggedSections) ? reviewPayload.flaggedSections : null;
      if (flaggedSections) {
        materializeFindingsOutput(editor, target, reviewPayload, flaggedSections);
        return;
      }
      // Idempotent — never double-apply the same stored draft (refresh / duplicate signal).
      if (target.key === lastMaterializedKey) return;
      materializeEditOutput(editor, target);
    },
    [materializeFindingsOutput, materializeEditOutput, lastMaterializedKey]
  );

  const materializeComposeDraftFromLedger = React.useCallback(
    async (targetLedgerRef?: string): Promise<void> => {
      if (state.status !== 'loaded') return;
      if (!bffBaseUrl || !state.sessionId) return;

      const editor = editorRef.current;
      if (!editor || typeof editor.materializeComposeDraft !== 'function') {
        // Defensive: the editor handle always exposes materializeComposeDraft (HOOK #2), but
        // guard against an older mounted build. Fail visibly-but-soft; do not crash.
        setComposeDraftError(
          'This build cannot insert AI drafts into the editor yet (editor materialize handle missing).'
        );
        return;
      }

      try {
        // Session-ledger read projection of the session's compose outputs (task-016 report
        // INTEGRATION HOOK #1). `@spaarke/auth` per ADR-028.
        const url = `${bffBaseUrl}/api/ai/chat/sessions/${encodeURIComponent(state.sessionId)}/compose-outputs`;
        const response = await authenticatedFetch(url, { method: 'GET' });
        // Defensive: authenticatedFetch throws ApiError on non-2xx (it never returns a
        // non-ok Response), so a 404 lands in the catch below — not here. This guard is a
        // safety net should that behaviour ever change.
        if (!response.ok) {
          if (response.status === 404) return; // no compose outputs yet — nothing to materialize
          setComposeDraftError(`Failed to load the drafted content (HTTP ${response.status}).`);
          return;
        }

        const outputs = (await response.json()) as ComposeLedgerOutput[];
        const composeOutputs = Array.isArray(outputs)
          ? outputs.filter(o => o.disposition === 'compose' && o.payload)
          : [];

        if (targetLedgerRef) {
          // TARGETED — a specific known stored output (Flow-5 signal, or a duplicate-signal test).
          // Empty-list bail is scoped to THIS branch only: nothing to look up.
          if (composeOutputs.length === 0) return;
          const target = composeOutputs.find(o => o.key === targetLedgerRef);
          if (!target) return;
          materializeSingleOutput(editor, target);
          return;
        }

        // UNTARGETED must NOT bail on an empty list (task 032 128KB budget, Leg B): an over-cap
        // findings payload is truncated at the LEDGER WRITE seam and the read projection
        // (`ChatEndpoints.ProjectComposeOutputs`) SKIPS the truncation-marker entry entirely — so a
        // truncated review can leave `composeOutputs` COMPLETELY EMPTY. The degraded-restore check
        // below (comparing against the sessionStorage marker) needs to run in exactly that case, so
        // it cannot sit behind an early return on emptiness.

        // UNTARGETED (reopen / refresh-durability, task 032 gap #3 coexistence): replay ALL findings
        // outputs (never evicted by a later edit — the ORIGINAL bug: `composeOutputs.reduce(...)` over
        // the WHOLE set picked only the single globally-highest-turn output, silently dropping an
        // earlier review's findings once ANY later edit output existed) PLUS the latest edit-shaped
        // output (unchanged highest-turn-among-edits semantics — undo/redo still works the same way).
        if (reviewSummarySessionRef.current !== state.sessionId) {
          // A genuinely different session's findings are about to materialize — reset the accumulator
          // so a document switch never leaves a PRIOR document's stale rows visible.
          setReviewSummaryFindings([]);
          setReviewSummaryFailedCount(0);
          setReviewSummaryOverallRisk(undefined);
          setReviewFindingsDegraded(null);
          reviewSummarySessionRef.current = state.sessionId;
        }

        const findingsOutputs = composeOutputs.filter(isFindingsShapedComposeOutput);
        const editOutputs = composeOutputs.filter(o => !isFindingsShapedComposeOutput(o));

        for (const output of findingsOutputs) {
          const reviewPayload = output.payload as ComposeReviewPayload;
          materializeFindingsOutput(editor, output, reviewPayload, reviewPayload.flaggedSections ?? []);
        }

        if (editOutputs.length > 0) {
          const latestEdit = editOutputs.reduce((a, b) => (b.turn > a.turn ? b : a));
          if (latestEdit.key !== lastMaterializedKey) {
            materializeEditOutput(editor, latestEdit);
          }
        }

        // Task 032 (128KB budget, Leg B) — degraded-restore detection: the read-projection shows ZERO
        // findings-shaped outputs for this session. If a same-tab marker says a prior review placed
        // N>0 findings, the entry clearly existed once and is now silently gone (the ADR-040
        // truncation-marker skip, `ChatEndpoints.ProjectComposeOutputs`) — surface a visible notice
        // rather than a silent empty panel.
        if (findingsOutputs.length === 0) {
          const marker = readReviewFindingsMarker(state.sessionId);
          if (marker && marker.count > 0) {
            setReviewFindingsDegraded({ expectedCount: marker.count, reason: 'skipped' });
          }
        }
      } catch (err) {
        // A 404 means the session has no compose outputs yet — a fresh document/upload mount
        // with nothing drafted. authenticatedFetch throws ApiError on non-2xx (never returns a
        // non-ok Response), so this "nothing to materialize" case lands here. It is a silent
        // no-op: this callback also runs UNCONDITIONALLY on every 'loaded' transition
        // (refresh-durability effect), where a plain file load legitimately has no draft. Only
        // genuine failures (500, network) surface an error card.
        if (err instanceof ApiError && err.status === 404) return;
        const message = err instanceof Error ? err.message : String(err);
        setComposeDraftError(`Failed to insert drafted content: ${message}`);
      }
    },
    [
      state.status,
      state.sessionId,
      bffBaseUrl,
      lastMaterializedKey,
      materializeSingleOutput,
      materializeFindingsOutput,
      materializeEditOutput,
    ]
  );

  // -------------------------------------------------------------------------
  // PaneEventBus receivers — Compose three-pane coordination, WORKSPACE leg
  // (task 104 / E2E-R5; supersedes/absorbs task 070). The Workspace pane OWNS
  // only the two flows whose reaction is an editor mutation:
  //   Flow 3 `compose_context_insert`  — insert a precedent clause at cursor.
  //   Flow 5 `compose_assistant_insert` — materialize an AI draft (FROM the
  //     stored ledger entry when `ledgerRef` present — ADR-040 render-follows-
  //     store; else the legacy R1 manual-confirm staging path).
  // Flows 1/2/6 react on the Context / Assistant panes (ContextPaneController /
  // ComposeAssistantCoordination) — ComposeWorkspace emits/relays but is NEVER
  // the terminal handler for those. Reception is via the typed
  // `useComposeWorkspaceReceivers` hook (zero `as any`; discriminants enumerated
  // on the shared-lib bus union).
  // -------------------------------------------------------------------------
  // UAT round-5 #2 — the "Overall risk" banner was removed from the summary, so the host no longer
  // tracks `overallRisk` (the event still carries it; we simply don't render it anymore).

  useComposeWorkspaceReceivers({
    // Flow 3 — Context → Workspace: insert the precedent/library clause into the
    // editor at cursor as a pending insertion (the editor's materialize seam).
    onContextInsert: event => {
      const html = event.contentHtml ?? '';
      if (!html) return;
      const clauseId = event.sourceClauseId ?? 'context-insert';
      editorRef.current?.materializeComposeDraft(
        { new_text: html },
        { ledgerRef: `context-insert:${clauseId}`, bindingId: clauseId, turn: 0 }
      );
    },
    // Flow 5 — Assistant → Workspace: materialize FROM the stored ledger entry
    // when a `ledgerRef` is present (ADR-040); else keep the R1 manual-confirm
    // staging path. Runtime contract preserved verbatim from the prior inline
    // receiver — only the (now-typed) event access + call site changed.
    onAssistantInsert: event => {
      if (event.ledgerRef) {
        void materializeComposeDraftFromLedger(event.ledgerRef);
        return;
      }
      dispatch({ kind: 'pendingAssistantInsert', payload: toAssistantInsertPayload(event) });
    },
    // task 072 (FR-35 Doc Q&A stretch) — ephemeral highlight only; no document
    // mutation, no ledger entry, no-op if the editor isn't mounted yet.
    onQaHighlight: event => {
      if (!event.qaSourceText) return;
      editorRef.current?.highlightCitedSpan(event.qaSourceText, event.qaSectionLabel);
    },
    // ai-advanced-capabilities-nda-r1 task 031 — NDA-REVIEW advisory comments. A
    // client-derived projection of the SAME ledgered NDA-REVIEW result the review-summary
    // panel renders (ADR-040); materializes one PERSISTENT comment thread per flagged
    // clause via ComposeEditorHandle.placeAdvisoryComments (createThread +
    // resolveTargetSpans('strict') reused, not reimplemented). Ranges that fail strict
    // resolution are reported via console.warn — never silently dropped (FR-19 "do not
    // guess"); a user-visible count is a natural addition once the review-summary panel
    // exists to host it.
    onAdvisoryComments: event => {
      const items = event.advisoryComments ?? [];
      if (items.length === 0) return;
      // task 032 (right-gutter comment layout, gate022031 follow-on): thread sectionRef/riskLevel/
      // standardRef through to placeAdvisoryComments so the created threads carry the metadata the
      // right-rail gutter card renders as a risk badge + citation. Previously only targetText/
      // explanation crossed this call, dropping the rest even though the event already carried them.
      const result = editorRef.current?.placeAdvisoryComments(
        items.map(item => ({
          targetText: item.targetText,
          explanation: item.explanation,
          sectionRef: item.sectionRef,
          riskLevel: item.riskLevel,
          standardRef: item.standardRef,
          // agreements-r1 task-002 schema split: discrete grounded-fact / judgment fields, so the
          // created threads render/export the structured "Flagged clause / Assessment says" form
          // (task 052) with no string-parsing. Undefined for legacy (pre-split) payloads.
          flaggedClause: item.flaggedClause,
          assessment: item.assessment,
        }))
      );
      if (result && result.failed.length > 0) {
        // eslint-disable-next-line no-console
        console.warn(
          `[ComposeWorkspace] ${result.failed.length} of ${items.length} advisory comment(s) could not be anchored ` +
            '(strict resolution failed):',
          result.failed
        );
      }
      // ai-advanced-capabilities-nda-r1 task 030 — additive capture for the review-summary docked
      // panel (does not alter the placement logic above). Field rename: the event's `targetText`
      // (031's own field, matching ComposeEditorHandle.placeAdvisoryComments' input shape) becomes
      // the panel's `quotedText` (matching the NDA-REVIEW schema's own field name — see
      // AgreementReviewSummaryPanel.tsx's file header for why the panel keeps the schema's vocabulary).
      setReviewSummaryFindings(
        items.map(item => ({
          sectionRef: item.sectionRef,
          quotedText: item.targetText,
          riskLevel: item.riskLevel,
          explanation: item.explanation,
          standardRef: item.standardRef,
        }))
      );
      setReviewSummaryFailedCount(result?.failed.length ?? 0);
      // Task 032 — thread the event's `overallRisk` (typed on the wire, previously dropped — see
      // AgreementReviewSummaryPanel.tsx's file header "OVERALL RISK") into the SAME state the ledger-
      // restore path populates, so live vs. reopen carry parity.
      setReviewSummaryOverallRisk(event.overallRisk);
      // A fresh LIVE review supersedes any earlier degraded-restore notice for this session.
      setReviewFindingsDegraded(null);
      // Task 032 (031-residual dedupe guard) — record WHICH session this live placement belongs to
      // (so the untargeted ledger-restore pass below never wrongly resets it as "a different
      // session's data") AND the content signature (so a same-mount ledger re-run — e.g. an
      // `externalChange` status-cycle racing this live placement — recognizes the SAME clause set and
      // skips re-placing it; `placeAdvisoryComments` has no idempotency of its own — see
      // notes/031-execution-notes.md "Residual risk").
      const liveSessionScope = event.sessionId ?? state.sessionId;
      reviewSummarySessionRef.current = liveSessionScope;
      materializedFindingsKeysRef.current.add(
        `${liveSessionScope}::sig:${computeAdvisorySignature(items.map(item => ({ targetText: item.targetText })))}`
      );
      // Task 032 (128KB budget, Leg B) — record the durability marker so a LATER reopen (same tab)
      // can detect a truncated/skipped ledger entry for this review.
      writeReviewFindingsMarker(liveSessionScope, items.length);
      // UAT round-7 #4 — the Review Summary now DEFAULTS COLLAPSED; a completed review no longer
      // auto-opens it. The reviewer opens it on demand via the toolbar's Review Summary toggle. (The
      // right-gutter Review Notes still show by default — ComposeEditor's `reviewNotesVisible`.)
    },
  });

  // DEF-12 — publish the editor's redline-accept into the cross-pane bridge so the Assistant
  // confirmation message's "Accept" control (the AI↔user interaction surface) commits the redline
  // through the EXISTING `usePendingRedline.accept` (via the editor handle). No-op outside the bridge
  // provider (standalone LegalWorkspace mount). Reject/Try-another do NOT route here — they are the
  // Assistant's durable ledger supersessions.
  //
  // spaarkeai-compose-r2 (multi-Compose-tab): the bridge Accept slot is single-writer (last-mounted
  // instance wins). Gate so an INACTIVE tab's instance holding the slot does NOT commit a redline into
  // a HIDDEN document when the chat Accept is issued while a DIFFERENT Compose tab is active — only the
  // ACTIVE tab services it (standalone / single-instance mounts default isActiveTab=true, so unaffected).
  const handleBridgeAcceptRedline = React.useCallback((ledgerRef: string): void => {
    if (isActiveTabRef.current === false) return;
    editorRef.current?.acceptPendingRedline(ledgerRef);
  }, []);
  useRegisterComposeRedlineAcceptHandler(handleBridgeAcceptRedline);

  // FR-04 refresh-durability (task 016): on (re)load of a session, re-materialize the CURRENT
  // compose draft from the ledger so a page refresh restores the drafted content — materialized
  // from durable storage (ADR-040), not a client buffer. Idempotent via `lastMaterializedKey`.
  React.useEffect(() => {
    if (state.status !== 'loaded') return;
    void materializeComposeDraftFromLedger();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [state.status, state.sessionId]);

  // -------------------------------------------------------------------------
  // FR-02 (task 011) — Search for Document -> reuse the 1c BFF Load path
  // -------------------------------------------------------------------------
  // Opens the standard Dataverse lookup dialog (Xrm.Utility.lookupObjects) scoped to
  // `sprk_document`, resolves the picked record's SPE pointer (Xrm.WebApi.retrieveRecord
  // — same fields as the 1c ribbon launcher, `DocumentComposeLaunch.ts`), then dispatches
  // `requestLoad` so the EXISTING `GET /api/compose/documents/{speId}` effect above mounts
  // it — identical to 1c (refresh-surviving, carries `documentRecordId`, reaches the
  // 'loaded' stage). No new load path, no new endpoint (ADR-039).
  const performDocumentSearch = React.useCallback(async (): Promise<void> => {
    let lookupResults: LookupResult[];
    try {
      const navigationService = createXrmNavigationService();
      lookupResults = await navigationService.openLookup({ entityType: 'sprk_document' });
    } catch (err) {
      // No Dataverse-hosted context (e.g. a standalone non-MDA host) — the standard
      // lookup dialog is unavailable. Fail soft: leave the empty state as-is.
      // eslint-disable-next-line no-console
      console.warn('[ComposeWorkspace] Document lookup unavailable in this host context:', err);
      return;
    }

    if (lookupResults.length === 0) return; // user dismissed the lookup — empty state unchanged
    const picked = lookupResults[0];

    // DEF-02: a new Search pick supersedes any prior Search-resolved drive id. Clear it up
    // front so a resolution that FAILS below (retrieveRecord throws, or a half-provisioned
    // record with no SPE drive pointer) leaves NO stale override — otherwise `effectiveDriveId`
    // would keep the PREVIOUS pick's drive and a later Save/Load could key off the WRONG drive.
    // A successful resolution re-sets the correct drive at the bottom of this handler.
    setSearchResolvedDriveId(null);

    let record: Record<string, unknown>;
    try {
      const dataService = createXrmDataService();
      record = await dataService.retrieveRecord('sprk_document', picked.id, SEARCH_DOCUMENT_SELECT);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      dispatch({ kind: 'loadFailed', errorMessage: `Failed to resolve the selected document: ${message}` });
      return;
    }

    const speDriveItemId = record[SEARCH_FIELD_GRAPH_ITEM_ID] as string | undefined;
    const speDriveId = record[SEARCH_FIELD_DRIVE_ID] as string | undefined;
    const fileName = (record[SEARCH_FIELD_DISPLAY_NAME] as string | undefined) ?? picked.name;

    if (!speDriveItemId || !speDriveId) {
      // Half-provisioned document (missing SPE drive pointer) — mirrors the 1c ribbon
      // launcher's same guard (issue #572 Defect 1).
      dispatch({
        kind: 'loadFailed',
        errorMessage: `"${picked.name}" isn't fully provisioned in SharePoint Embedded (missing drive pointer). Pick another document.`,
      });
      return;
    }

    setSearchResolvedDriveId(speDriveId);
    dispatch({
      kind: 'requestLoad',
      documentRef: { speDriveItemId, sprkDocumentId: picked.id, fileName },
      sessionId: initialSessionId ?? '',
    });
  }, [initialSessionId]);

  const handleSearchRequested = React.useCallback((): void => {
    onSearchRequested?.();
    void performDocumentSearch();
  }, [onSearchRequested, performDocumentSearch]);

  // -------------------------------------------------------------------------
  // Editor-side callbacks
  // -------------------------------------------------------------------------

  // Dirty flag is UI-only (drives the Save button's enabled/disabled state
  // in ComposeToolbar). The reducer's status enum doesn't distinguish clean
  // vs dirty inside `loaded`; a local flag is the least-invasive surface.
  const [isDirty, setIsDirty] = React.useState<boolean>(false);
  const handleDirtyChange = React.useCallback((dirty: boolean): void => {
    setIsDirty(dirty);
  }, []);

  const handleImportWarnings = React.useCallback((warnings: Array<{ type: string; message: string }>): void => {
    dispatch({ kind: 'importWarnings', warnings });
  }, []);

  // -------------------------------------------------------------------------
  // FR-01 (task 010) — Browse local-file transient mount
  // -------------------------------------------------------------------------
  // Browse opens a local `.docx` picker and mounts the picked file's bytes into the
  // editor as a TRANSIENT working draft via the same `docxBytes` seam FR-03/task 012
  // uses for Assistant-uploaded files (`mountTransient` reducer action). No
  // `sprk_document` is created and no BFF round-trip occurs (ADR-040) — persistence
  // happens on first Save (create-on-save, FR-05/task 013).
  // task 113 (UAT defect 4): host-injected active-document registrar (null on standalone mounts).
  // Held in a ref so the stable-`[]` Browse callback below can read the latest value without churn.
  const registerActiveDocument = useComposeActiveDocumentRegistration();
  const registerActiveDocumentRef = React.useRef(registerActiveDocument);
  registerActiveDocumentRef.current = registerActiveDocument;

  const handleBrowseRequested = React.useCallback((): void => {
    onBrowseRequested?.();
    browseFileInputRef.current?.click();
  }, [onBrowseRequested]);

  const handleBrowseFileSelected = React.useCallback(
    (event: React.ChangeEvent<HTMLInputElement>): void => {
      const file = event.target.files?.[0] ?? null;
      // Reset the input value so re-selecting the same file still fires a change event.
      event.target.value = '';
      if (!file) return; // user cancelled the picker — empty state unchanged, nothing mounts

      const reader = new FileReader();
      reader.onload = () => {
        const result = reader.result;
        if (!(result instanceof ArrayBuffer)) return;
        // A Browse mount has no SPE drive — clear any stale Search-resolved drive id
        // (FR-02/task 011) so a later Save doesn't key off the WRONG drive.
        setSearchResolvedDriveId(null);
        // Wave 2 (UAT-R3 Test #3 fix): mint the tab's DOCUMENT session id here. Unlike the
        // assistant-upload path (which gets a server sessionId via requestUploadMount), a Browse
        // mount never hits the server for its identity, so its `mountTransient` reducer previously
        // left state.sessionId ''. That empty id caused the AI toolbar to thread `documentSessionId:
        // ''`, which ConversationPane reclassified as INFORMATIONAL (prose card) instead of a compose
        // EDIT (redline). A minted, tab-lifetime id restores EDIT routing (DEF-09/DEF-11) and the
        // redline-from-ledger materialization (which aborts on empty state.sessionId).
        const browseDocumentSessionId = mintDocumentSessionId();

        // FR-03 (task 011, spaarkeai-compose-fidelity-r4.5, T-2 path-A resolution): project the
        // browsed bytes through the SAME stateless server reader Load/Upload use (POST
        // /api/compose/project) so Browse renders via the one-reader projection branch (F-2). This is a
        // READ-only round-trip: the server returns a projection and persists NOTHING (no ITenantCache
        // write, no SPE write, no sprk_document row) — it does NOT violate ADR-040 / R4 I-2 (the client
        // still authors no .docx bytes; it merely asks the server to render bytes it already holds
        // locally, and the server hands back a render without storing or echoing them as an authored
        // artifact).
        //
        // Task 013 (F-2 "one reader") RECONCILIATION: the client mammoth fallback reader has been
        // DELETED, so Browse now HARD-REQUIRES this round-trip to produce an editable surface. The
        // fetch below stays best-effort at the NETWORK layer (unconfigured `bffBaseUrl` / thrown fetch
        // still fall through with `projection: null`, and `mountTransient` still dispatches so the tab
        // navigates and the file is registered with the active chat session) — but a null projection no
        // longer degrades to a lossy mammoth render. `ComposeEditor` now renders an explicit "couldn't
        // prepare this document for editing" error/unavailable state for a docx mount with no
        // projection (see `ComposeEditor.tsx`'s `projectionUnavailable` state) — never a silent blank or
        // degraded editor.
        void (async () => {
          let projection: ComposeServerProjection | null = null;
          if (bffBaseUrl) {
            try {
              const response = await authenticatedFetch(`${bffBaseUrl}/api/compose/project`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ content: arrayBufferToBase64(result), fileName: file.name }),
              });
              if (response.ok) {
                const payload = (await response.json()) as { projection?: RawComposeProjectionPayload };
                projection = normalizeProjection(payload.projection);
              }
            } catch {
              // Network/parse failure — fall through with projection: null. The MOUNT itself still
              // proceeds (dispatch below), but per task 013 (F-2) the editor renders the explicit
              // error/unavailable state rather than a degraded render — there is no fallback reader.
              projection = null;
            }
          }

          // FR-05 (task 100): carry the host-resolved BU container so the first Save (create-on-save)
          // knows which SPE container to mint the new sprk_document's drive-item in.
          dispatch({
            kind: 'mountTransient',
            docxBytes: result,
            fileName: file.name,
            containerId: containerIdRef.current,
            sessionId: browseDocumentSessionId,
            projection,
            // G7 (task 022): mint the transient dedup key once for this Browse mount → every create-on-save
            // sends it so repeated saves target ONE record (no duplicate mint).
            transientKey: mintTransientKey(),
          });
          // A freshly Browse-mounted file is unsaved by definition — mark dirty so Save
          // (create-on-save, task 013) is enabled immediately.
          setIsDirty(true);
          // task 113 (UAT defect 4): register this Browse/direct mount with the active chat session so
          // chat "summarize this document" + a later "edit in Compose" resolve THIS file. Fire-and-
          // forget; the host lands the bytes as a ChatSessionFile + marks the active document. Null
          // (no-op) on a standalone LegalWorkspace mount. Bytes travel by a direct function call — never
          // the PaneEventBus (ADR-015 keeps the bus content-free).
          // Wave 3 Part 2: thread the tab's minted document session id so the server sets
          // ActiveDocument.DocumentSessionId → a typed revise/draft (TEXT path) routes into THIS doc session.
          void registerActiveDocumentRef.current?.({
            docxBytes: result,
            fileName: file.name,
            documentSessionId: browseDocumentSessionId,
          });
        })();
      };
      reader.onerror = () => {
        dispatch({
          kind: 'loadFailed',
          errorMessage: `Failed to read "${file.name}". The file may be corrupted or unreadable.`,
        });
      };
      reader.readAsArrayBuffer(file);
    },
    [bffBaseUrl]
  );

  // -------------------------------------------------------------------------
  // Item 7 (UAT round-4) — Blank page / Open template born-in-editor mounts
  // -------------------------------------------------------------------------
  // Both mount a born-in-editor working draft via `mountDraftHtml` (create-on-save on first Save, the
  // same lifecycle as an inline AI draft). Each mints a document session id (item 6) so a subsequent
  // "Draft alternative" / AI edit routes into a real session and materializes as a redline. "Open
  // template" mounts a single generic starter scaffold today; the handler is the seam for a future
  // template picker (the empty-state CTA already exists).
  const mountBornInEditor = React.useCallback((html: string, fileName: string): void => {
    setSearchResolvedDriveId(null);
    dispatch({
      kind: 'mountDraftHtml',
      html,
      fileName,
      containerId: containerIdRef.current,
      sessionId: mintDocumentSessionId(),
      // G7 (task 022): transient dedup key for this born-in-editor mount.
      transientKey: mintTransientKey(),
    });
    // A freshly-created born-in-editor doc is unsaved by definition — enable Save (create-on-save).
    setIsDirty(true);
  }, []);

  const handleBlankRequested = React.useCallback((): void => {
    mountBornInEditor('<p></p>', 'Untitled document.docx');
  }, [mountBornInEditor]);

  const handleTemplateRequested = React.useCallback((): void => {
    mountBornInEditor(COMPOSE_BLANK_TEMPLATE_HTML, 'Untitled document.docx');
  }, [mountBornInEditor]);

  // -------------------------------------------------------------------------
  // R3 — "Visible to assistant" toggle → active-document register/withdraw
  // -------------------------------------------------------------------------
  // The toggle lives on the WORKSPACE tab strip (WorkspacePane.handleToggleVisibility), but the
  // loaded document BYTES live HERE. When the user toggles it ON we register the doc's identity +
  // extracted text into the chat context via the SAME conduit the Browse / stored-doc (DEF-10) /
  // upload auto-registers use (`registerActiveDocument` → chat upload → ChatSessionFile RAG +
  // ActiveDocument); OFF withdraws it (`visible: false` in the POST body, mapped server-side). No new
  // conduit machinery (§11) — this handler just parameterizes the existing one with `visible`.
  const handleComposeVisibilityChange = React.useCallback(
    (visible: boolean): void => {
      if (state.status !== 'loaded' && state.status !== 'saving') return;
      if (!state.docxBytes) return;
      // spaarkeai-compose-r2 (multi-Compose-tab): the visibility conduit is single-slot (the bridge
      // holds ONE editor handler; the last-mounted instance wins the slot). Guard the REGISTER
      // (visible=true) so an INACTIVE instance holding the slot cannot claim the session's active
      // document when WorkspacePane drives visible=true for a DIFFERENT active Compose tab — the
      // active tab registers via the tab-scoped tab_change effect below. A WITHDRAW (visible=false,
      // switch to a non-document tab) is session-level and safe from any instance, so it is allowed
      // through regardless of active state.
      if (visible && isActiveTabRef.current === false) return;
      void registerActiveDocumentRef.current?.({
        docxBytes: state.docxBytes,
        fileName: state.documentRef?.fileName,
        documentSessionId: state.sessionId,
        visible,
      });
    },
    [state.status, state.docxBytes, state.documentRef?.fileName, state.sessionId]
  );
  useRegisterComposeVisibilityHandler(handleComposeVisibilityChange);

  // -------------------------------------------------------------------------
  // R4 — "Insert into document": Assistant suggestion → tracked change at selection
  // -------------------------------------------------------------------------
  // Track the editor's latest selection (Flow-1 `compose_selection_changed`, dispatched by
  // ComposeEditor on the `context` channel) so the insert handler can decide strike+replace (a live
  // selection) vs insert-at-cursor. Read-only cache; no editor changes (the engine already anchors).
  const lastSelectionRef = React.useRef<{ from: number; to: number; selectionText: string } | null>(null);
  usePaneEvent('context', (event): void => {
    if (event.type !== 'compose_selection_changed') return;
    lastSelectionRef.current = event.selection
      ? { from: event.selection.from, to: event.selection.to, selectionText: event.selection.selectionText }
      : null;
  });

  // Materialize an Assistant suggestion as a PENDING redline via the EXISTING engine
  // (`materializeComposeDraft` → usePendingRedline.materialize) — no engine changes (§11). A live
  // selection → `target_text` (strike+replace at selection); no selection → omit (insert at cursor).
  // Each insert gets a UNIQUE ledgerRef/bindingId so multiple chat inserts coexist as independent
  // redlines (a shared bindingId would make a later insert SUPERSEDE — strip — the earlier one). The
  // per-change Accept/Reject popover + `usePendingRedline.accept/reject` handle it by ledgerRef.
  const chatInsertSeqRef = React.useRef(0);
  const handleInsertSuggestion = React.useCallback((content: string, messageId?: string): void => {
    // spaarkeai-compose-r2 (multi-Compose-tab): the bridge Insert slot is single-writer (last-mounted
    // instance wins). Gate so an INACTIVE tab's instance holding the slot does NOT materialize the
    // Assistant suggestion into a HIDDEN document when "Insert into document" is issued while a
    // DIFFERENT Compose tab is active — only the ACTIVE tab services it (standalone / single-instance
    // mounts default isActiveTab=true, so unaffected).
    if (isActiveTabRef.current === false) return;
    const editor = editorRef.current;
    if (!editor || typeof editor.materializeComposeDraft !== 'function') return;
    if (!content || content.trim().length === 0) return;
    const sel = lastSelectionRef.current;
    const targetText = sel && sel.to > sel.from && sel.selectionText.trim().length > 0 ? sel.selectionText : undefined;
    const id = messageId && messageId.length > 0 ? messageId : String((chatInsertSeqRef.current += 1));
    const ledgerRef = `chat-insert:${id}`;
    editor.materializeComposeDraft(
      targetText ? { new_text: content, target_text: targetText } : { new_text: content },
      { ledgerRef, bindingId: ledgerRef, turn: 0 }
    );
    setIsDirty(true);
  }, []);
  useRegisterComposeInsertSuggestionHandler(handleInsertSuggestion);

  // -------------------------------------------------------------------------
  // DEF-10 (DEF-UAT-1 part 2, 2026-07-12) — share the loaded HOST document's
  // TEXT with the Assistant's chat session
  // -------------------------------------------------------------------------
  // A host `sprk_document` opened in Compose ("Open in Compose") loads its DOCX
  // bytes via the stored-document Load effect above — but, unlike a Browse mount
  // (handleBrowseFileSelected) or a chat upload, those bytes never reached the
  // CHAT session. So the Assistant answered "no document uploaded in this session"
  // to "summarize this document": the two-session split (document session ≠ chat
  // session). This effect closes that gap by REUSING the EXACT same host registrar
  // the Browse path uses (ConversationPane.registerComposeActiveDocument → the
  // EXISTING chat upload endpoint → a ChatSessionFile carrying ExtractedText →
  // SessionFileTextSource / the session-files RAG index). NO new BFF endpoint and
  // NO parallel context path (§11): the loaded bytes we already hold are handed to
  // the same conduit a Browse mount uses.
  //
  // "Visible to assistant" is DEFAULT ON for a host document (owner decision
  // 2026-07-12): it is auto-registered on load — no toggle required, and toggling
  // the workspace-tab flag is no longer the (inert) path to doc visibility.
  //
  // Fires ONCE per stored SPE document (ref-guarded by speDriveItemId). Transient
  // Browse / upload / draft mounts are EXCLUDED: a Browse mount already registers
  // itself (above), and upload / draft mounts are chat-native (an upload IS a
  // ChatSessionFile; a draft lives in the session ledger). No-op (null registrar)
  // on a standalone LegalWorkspace mount with no bridge provider — Save is unaffected.
  const sharedActiveDocumentKeyRef = React.useRef<string | null>(null);
  React.useEffect(() => {
    if (state.status !== 'loaded') return;
    const speDriveItemId = state.documentRef?.speDriveItemId;
    // Only a STORED host document has a real SPE drive-item id; transient mounts set it to ''.
    if (!speDriveItemId) return;
    if (!state.docxBytes) return;
    if (sharedActiveDocumentKeyRef.current === speDriveItemId) return; // once per stored document
    // spaarkeai-compose-r2 (multi-Compose-tab): only the ACTIVE tab's instance auto-claims the
    // session's active document on load. An INACTIVE instance finishing its load (e.g. a second
    // Compose tab opened over this one, or a background load race) must NOT steal active-doc from
    // the tab the user is viewing. When this tab later becomes active the tab-scoped tab_change
    // effect below re-registers it. (Guard skips WITHOUT setting the once-ref so a later activation
    // still registers.)
    if (isActiveTabRef.current === false) return;
    sharedActiveDocumentKeyRef.current = speDriveItemId;
    void registerActiveDocumentRef.current?.({
      docxBytes: state.docxBytes,
      fileName: state.documentRef?.fileName,
      // Wave 3 Part 2: the stored document's loaded session id IS its document session (keyed
      // DocumentId+MatterId) — register it so a typed revise/draft routes into THIS doc session.
      documentSessionId: state.sessionId,
    });
    // registerActiveDocumentRef is a stable ref; excluded from deps intentionally.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [state.status, state.documentRef?.speDriveItemId, state.docxBytes]);

  // -------------------------------------------------------------------------
  // Wave 3 Part 3 — tab-scoped ActiveDocument (multi-tab correctness)
  // -------------------------------------------------------------------------
  // With multiple Compose tabs the Assistant-typed edit must target the tab the user is VIEWING, not
  // the last one that mounted. When a Compose tab becomes the ACTIVE workspace tab (WorkspacePane
  // dispatches `tab_change` / `active_widget_changed` on the `workspace` channel), re-register THIS
  // tab's document as the session's active document — most-recent-active-wins — so
  // ChatSession.ActiveDocument.DocumentSessionId re-points to the viewed tab and BindingCapabilityTool
  // routes a typed revise/draft here. Reuses the Part-2 registration conduit (the host dedups the
  // upload → pointer-only re-assert; no duplicate ChatSessionFile). NO new bus event (ADR-030): the
  // existing tab_change discriminant is the signal. Gated on a loaded document with a real document
  // session id, and only when the newly-active tab is a Compose tab.
  usePaneEvent('workspace', (event: WorkspacePaneEvent): void => {
    if (event.type !== 'tab_change' && event.type !== 'active_widget_changed') return;
    if (state.status !== 'loaded' && state.status !== 'saving') return;
    if (!state.sessionId || !state.docxBytes) return;
    // spaarkeai-compose-r2 (multi-Compose-tab): when this instance knows its OWN workspace tab id
    // (SpaarkeAi keep-alive mount), re-register ONLY when the newly-active tab is THIS tab. Multiple
    // Compose editors are mounted at once (each hidden except the active one); without this scope
    // EVERY mounted editor would re-register on ANY compose tab activation and fight over the
    // session's active document (most-recent-active-wins would resolve to whichever effect ran last,
    // not the tab the user is viewing). Comparing the event's target tab id is deterministic — it
    // does not depend on the `isActiveTab` prop having re-rendered before the synchronous dispatch.
    if (workspaceTabId) {
      if (event.tabId !== workspaceTabId) return;
    } else {
      // Standalone / layout-door single-instance mount (no tab id): recognize a Compose tab by its
      // DIRECT widgetType regardless of seed shape, or the legacy LAYOUT discriminant (compose seed
      // / layoutName === 'Compose') for the ribbon compose-launch path (widgetType 'workspace').
      const wd = event.widgetData as { compose?: unknown; layoutName?: string } | null | undefined;
      const activeTabIsCompose =
        event.widgetType === 'compose' || (wd != null && (wd.compose != null || wd.layoutName === 'Compose'));
      if (!activeTabIsCompose) return;
    }
    void registerActiveDocumentRef.current?.({
      docxBytes: state.docxBytes,
      fileName: state.documentRef?.fileName,
      documentSessionId: state.sessionId,
    });
  });

  // -------------------------------------------------------------------------
  // FR-03 (task 012) — transient upload-mount
  // -------------------------------------------------------------------------
  // When launched from a chat "open in Compose" on an Assistant-UPLOADED file,
  // fetch the retained bytes from POST /api/compose/upload and route them into
  // the editor's `docxBytes` seam as a transient working draft (create-on-save;
  // NO sprk_document until first Save). Mutually exclusive with the
  // initialDocumentRef (stored-document) path. `@spaarke/auth` per ADR-028.
  React.useEffect(() => {
    if (!initialUploadRef) return;
    if (initialDocumentRef) return; // stored-document path wins; upload is mutually exclusive
    if (state.status !== 'empty' && state.status !== 'error') return;
    if (!initialUploadRef.sessionId || !initialUploadRef.sessionFileId) return;
    if (!bffBaseUrl) {
      dispatch({
        kind: 'loadFailed',
        errorMessage: 'BFF base URL is not configured. Cannot open the uploaded file.',
      });
      return;
    }

    const ac = new AbortController();
    const uploadRef = initialUploadRef;
    dispatch({ kind: 'requestUploadMount', sessionId: uploadRef.sessionId });

    (async () => {
      try {
        const url = `${bffBaseUrl}/api/compose/upload`;
        const response = await authenticatedFetch(url, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ sessionId: uploadRef.sessionId, documentId: uploadRef.sessionFileId }),
          signal: ac.signal,
        });

        if (!response.ok) {
          const msg =
            response.status === 404
              ? 'The uploaded file is no longer available (the session may have expired). Re-upload it in the Assistant and try again.'
              : `Failed to open the uploaded file (HTTP ${response.status}).`;
          dispatch({ kind: 'loadFailed', errorMessage: msg });
          return;
        }

        const payload = (await response.json()) as {
          content: string;
          fileName?: string;
          size?: number;
          // FR-01 (task 010, spaarkeai-compose-fidelity-r4.5): the server DOCX→editor projection,
          // built from these SAME uploaded bytes via ComposeDocxProjectionBuilder — the identical
          // shape the stored-doc Load response carries. Optional so an older BFF (no projection
          // field) still mounts — task 013 (F-2): the editor renders an error/unavailable state, not
          // a mammoth fallback (which no longer exists client-side).
          projection?: {
            status?: 'success' | 'partial' | 'failed';
            canEdit?: boolean;
            html?: string;
            warnings?: { code: string; count: number }[];
            schemaVersion?: string;
          };
        };

        // ASP.NET Core serializes byte[] as a base64 string (NOT a JSON number
        // array) — decode with atob(), mirroring the Load effect above.
        const binary = atob(payload.content ?? '');
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) {
          bytes[i] = binary.charCodeAt(i);
        }
        if (ac.signal.aborted) return;

        // Normalize the server projection defensively via the shared `normalizeProjection` helper —
        // same shape/defaults as the Load effect above. An older BFF (no projection field) → null →
        // (task 013, F-2) the editor renders an explicit error/unavailable state.
        const hydratedUploadProjection = normalizeProjection(payload.projection);

        // An upload mount has no SPE drive — clear any stale Search-resolved drive id
        // (FR-02/task 011) so a later Save doesn't key off the WRONG drive.
        setSearchResolvedDriveId(null);
        dispatch({
          kind: 'mountTransient',
          docxBytes: bytes.buffer,
          fileName: payload.fileName ?? uploadRef.fileName,
          // FR-05 (task 100): thread the host-resolved BU container for create-on-save.
          containerId: containerIdRef.current,
          projection: hydratedUploadProjection,
          // G7 (task 022): transient dedup key for this assistant-upload mount.
          transientKey: mintTransientKey(),
        });
        // A freshly-mounted upload is unsaved by definition — mark dirty so Save
        // (create-on-save, task 013) is enabled immediately.
        setIsDirty(true);
        // Wave 4 (spaarkeai-compose-r2, end-to-end revise): register THIS upload mount's document
        // session with the host so `ChatSession.ActiveDocument.DocumentSessionId` back-fills — the
        // Browse (line ~1383) and stored-doc (line ~1432) paths already do this, but the assistant
        // upload-mount door did NOT, so a chat-uploaded doc auto-mounted for "revise this document"
        // never established the routing target and a subsequent typed/chip revise fell back to the
        // chat session (narrated as prose instead of redlined). `requestUploadMount` already set
        // state.sessionId to `uploadRef.sessionId`; thread that same id. The host dedups the upload
        // (pointer-only re-assert; no duplicate ChatSessionFile). No-op on a standalone mount.
        // spaarkeai-compose-r2 (multi-Compose-tab): only the ACTIVE tab's instance auto-claims the
        // session's active document — an inactive instance finishing its upload load must not steal
        // active-doc from the viewed tab; it re-registers via the tab_change effect on activation.
        if (isActiveTabRef.current !== false) {
          void registerActiveDocumentRef.current?.({
            docxBytes: bytes.buffer,
            fileName: payload.fileName ?? uploadRef.fileName,
            documentSessionId: uploadRef.sessionId,
          });
        }
      } catch (err) {
        if (ac.signal.aborted) return;
        const message = err instanceof Error ? err.message : String(err);
        dispatch({ kind: 'loadFailed', errorMessage: `Failed to open the uploaded file: ${message}` });
      }
    })();

    return () => ac.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [initialUploadRef?.sessionFileId, initialUploadRef?.sessionId, bffBaseUrl]);

  // -------------------------------------------------------------------------
  // DEF-08: AI-drafted full-document seed mount
  // -------------------------------------------------------------------------
  // When launched with a draft seed (and no stored-doc / upload), materialize the drafted
  // document BODY into the editor as a TRANSIENT working draft (create-on-save on first Save — the
  // same lifecycle as an upload mount). Two provenance shapes:
  //   - Part B: `initialDraftRef.html` (inline, "Open in Compose" affordance) — mount directly.
  //   - Part A: `initialDraftRef.{ledgerRef,sessionId}` — resolve the body from the session ledger
  //     via GET /compose-outputs (ADR-040 render-follows-store; the content lives in the ledger,
  //     the seed carried only an identifier — ADR-015). Mutually exclusive with the stored-doc /
  //     upload paths.
  React.useEffect(() => {
    if (!initialDraftRef) return;
    if (initialDocumentRef || initialUploadRef) return; // stored-doc / upload win (mutually exclusive)
    if (state.status !== 'empty' && state.status !== 'error') return;

    // Part B: inline html — mount directly, no fetch.
    if (typeof initialDraftRef.html === 'string' && initialDraftRef.html.length > 0) {
      setSearchResolvedDriveId(null);
      // Item 6 (UAT round-4): mint a document session id so a later "Draft alternative" (or any
      // AI-edit) on this born-in-editor doc routes into a real session and materializes as a redline
      // instead of misrouting to informational prose. Mirrors the Browse path (mountTransient).
      const draftDocumentSessionId = mintDocumentSessionId();
      dispatch({
        kind: 'mountDraftHtml',
        html: initialDraftRef.html,
        fileName: initialDraftRef.fileName,
        containerId: containerIdRef.current,
        sessionId: draftDocumentSessionId,
        // G7 (task 022): transient dedup key for this inline (Part B) draft mount.
        transientKey: mintTransientKey(),
      });
      setIsDirty(true);
      return;
    }

    // Part A: resolve the drafted body from the session ledger by ledgerRef.
    const ledgerRef = initialDraftRef.ledgerRef;
    const draftSessionId = initialDraftRef.sessionId;
    if (!ledgerRef || !draftSessionId) return;
    if (!bffBaseUrl) {
      dispatch({
        kind: 'loadFailed',
        errorMessage: 'BFF base URL is not configured. Cannot open the drafted document.',
      });
      return;
    }

    const ac = new AbortController();
    // Enter the loading spinner WITHOUT a documentRef (reuses the upload-mount transition) so the
    // stored-document Load effect stays inert — this effect owns the mount.
    dispatch({ kind: 'requestUploadMount', sessionId: draftSessionId });

    (async () => {
      try {
        const url = `${bffBaseUrl}/api/ai/chat/sessions/${encodeURIComponent(draftSessionId)}/compose-outputs`;
        const response = await authenticatedFetch(url, { method: 'GET', signal: ac.signal });
        // Defensive: authenticatedFetch throws ApiError on non-2xx (it never returns a
        // non-ok Response), so a 404 lands in the catch below — not here. This guard is a
        // safety net should that behaviour ever change.
        if (!response.ok) {
          dispatch({
            kind: 'loadFailed',
            errorMessage:
              response.status === 404
                ? 'The drafted document is no longer available (the session may have expired). Try drafting it again.'
                : `Failed to load the drafted document (HTTP ${response.status}).`,
          });
          return;
        }

        // GET /compose-outputs → ComposeLedgerOutputDto[]: { key, bindingId, turn, disposition, payload }.
        // The nested `payload` is passed through opaquely (snake_case body_html preserved).
        const outputs = (await response.json()) as Array<{
          key?: string;
          payload?: { body_html?: string; title?: string };
        }>;
        const match = Array.isArray(outputs) ? outputs.find(o => o?.key === ledgerRef) : undefined;
        const bodyHtml = match?.payload?.body_html;
        if (ac.signal.aborted) return;
        if (typeof bodyHtml !== 'string' || bodyHtml.length === 0) {
          dispatch({
            kind: 'loadFailed',
            errorMessage: 'The drafted document could not be found in this session. Try drafting it again.',
          });
          return;
        }

        // A draft mount has no SPE drive — clear any stale Search-resolved drive id.
        setSearchResolvedDriveId(null);
        // FR-34 D-F3 (task 071): arm the render-ack signal BEFORE the state flips to 'loaded'
        // so the watcher effect below emits `compose_content_rendered` the instant the seeded
        // editor renders (never on a mere tab-shell open). Correlated to the waiting server frame
        // by ledgerRef. A failure path above already returned via loadFailed WITHOUT arming this —
        // so a seed that never renders never acks (honest timeout).
        pendingDraftRenderSignalRef.current = { ledgerRef, sessionId: draftSessionId };
        dispatch({
          kind: 'mountDraftHtml',
          html: bodyHtml,
          fileName: match?.payload?.title,
          containerId: containerIdRef.current,
          // G7 (task 022): transient dedup key for this ledger-resolved (Part A) draft mount.
          transientKey: mintTransientKey(),
        });
        // A freshly-seeded draft is unsaved by definition — mark dirty so Save (create-on-save) is
        // enabled immediately.
        setIsDirty(true);
      } catch (err) {
        if (ac.signal.aborted) return;
        // This is a DRAFT-SEED mount — a draft IS expected. authenticatedFetch throws ApiError
        // on non-2xx (never returns a non-ok Response), so a 404 (the drafted output isn't in
        // the session — expired or never written) lands here. Surface the same soft, non-scary
        // message the equivalent stored-doc path would; do NOT crash.
        if (err instanceof ApiError && err.status === 404) {
          dispatch({
            kind: 'loadFailed',
            errorMessage:
              'The drafted document is no longer available (the session may have expired). Try drafting it again.',
          });
          return;
        }
        const message = err instanceof Error ? err.message : String(err);
        dispatch({ kind: 'loadFailed', errorMessage: `Failed to load the drafted document: ${message}` });
      }
    })();

    return () => ac.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [initialDraftRef?.ledgerRef, initialDraftRef?.sessionId, initialDraftRef?.html, bffBaseUrl]);

  // -------------------------------------------------------------------------
  // FR-34 D-F3 (task 071) — emit the content-render ack signal once the seeded
  // draft actually renders in the editor
  // -------------------------------------------------------------------------
  // Fires ONLY when a Part-A draft seed reached the 'loaded' state with populated
  // `seedHtml` (the editor is rendered with the drafted body — see the render
  // branch: showEditor mounts <ComposeEditor initialHtml={state.seedHtml} />). This
  // is the honest "content is on screen" moment WorkspacePane's deferred ack waits
  // for — NOT the tab-shell open. One-shot per seed (the ref is cleared on emit);
  // a seed that failed to render left the ref null (loadFailed returned early) so
  // nothing is emitted and the server ack times out honestly.
  React.useEffect(() => {
    const signal = pendingDraftRenderSignalRef.current;
    if (!signal) return;
    if (state.status !== 'loaded' || state.seedHtml === null) return;
    pendingDraftRenderSignalRef.current = null;
    dispatchPaneEvent('workspace', {
      type: 'compose_content_rendered',
      ledgerRef: signal.ledgerRef,
      sessionId: signal.sessionId,
    });
  }, [state.status, state.seedHtml, dispatchPaneEvent]);

  // -------------------------------------------------------------------------
  // Editor doc-ref shape (shared lib has its own narrower interface)
  // -------------------------------------------------------------------------
  const editorDocRef: ComposeEditorDocumentRef | undefined = state.documentRef
    ? {
        speDriveItemId: state.documentRef.speDriveItemId,
        sprkDocumentId: state.documentRef.sprkDocumentId,
        fileName: state.documentRef.fileName,
        containerId: state.documentRef.containerId,
      }
    : undefined;

  // Toolbar documentId (Open-in-Word handoff) — accepts SPE id or sprk_documentid.
  const toolbarDocumentId = state.documentRef?.sprkDocumentId ?? state.documentRef?.speDriveItemId ?? '';

  // "Open Document" preview gating — the BFF preview-url endpoint resolves a document
  // by its sprk_document id, so the button appears only for a PROMOTED doc (one with a
  // sprk_document record) and a configured BFF base URL. Undefined handler → button hidden
  // (mirrors the onRefreshProfile gating pattern).
  const previewDocumentId = state.documentRef?.sprkDocumentId ?? '';
  const canPreviewDocument = previewDocumentId.length > 0 && bffBaseUrl.length > 0;

  // FR-05 (task 100, gap 1.5): a transient (Browse/Upload) draft has no SPE pointer yet — the Save
  // button must be enabled for it (create-on-save) even though its editor dirty flag is false for
  // an unedited mount (FR-06a keeps the original bytes). A draft exists whenever the workspace is
  // showing an editor for a documentRef that has no real speDriveItemId.
  const hasTransientDraft =
    (state.status === 'loaded' || state.status === 'saving') &&
    !!state.documentRef &&
    !state.documentRef.speDriveItemId;

  // FIX #5 (UAT): derived enable/disable state for the consolidated toolbar's Word
  // dropdown + Save button (was previously computed inside ComposeToolbar). Same
  // rules as before: Open-in-Word needs a real persisted document id + bffBaseUrl
  // and is suppressed while saving or while another doc action is in flight; Save
  // is available when there is unsaved work (an edit OR an unpersisted transient
  // draft) and not mid-save.
  const isSavingNow = state.status === 'saving';
  const hasWordDocument = toolbarDocumentId.length > 0 && bffBaseUrl.length > 0;
  const wordActionsDisabled = isSavingNow || !hasWordDocument || isWordActing;
  const canSaveNow = !isSavingNow && bffBaseUrl.length > 0 && (isDirty || hasTransientDraft);

  // C3 fix (UAT 2026-07-20): Open-in-Word FLUSHES a save first so Word opens the CURRENT bytes —
  // including pending AI redlines as native w:ins/w:del. Redlines (and settled edits) only reach SPE via
  // a save; Open-in-Word used to open the last-PERSISTED bytes, so a redline the user had on screen never
  // showed in Word (only comments, which a prior push/save had already landed). Gated on unsaved work OR
  // pending redlines (a clean doc opens immediately). The document id is stable across the flush because
  // Word-open is only enabled once the doc is already persisted (hasWordDocument), so a create-on-save id
  // change cannot strand this handler. With C1/C2 fixed, that redline-inclusive save now succeeds.
  const openInWordFlushed = async (mode: 'web' | 'desktop'): Promise<void> => {
    if (wordActionsDisabled) return;
    const hasRedlines = editorRef.current?.hasPendingRedlines?.() ?? false;
    if (isDirty || hasRedlines) {
      await triggerSaveRef.current();
    }
    if (mode === 'web') {
      await openInWeb(toolbarDocumentId);
    } else {
      await openInDesktop(toolbarDocumentId);
    }
  };

  // -------------------------------------------------------------------------
  // Render
  // -------------------------------------------------------------------------
  const showEditor = state.status === 'loaded' || state.status === 'saving';

  return (
    <div
      className={mergeClasses(styles.root, className)}
      role="region"
      aria-label={state.documentRef?.fileName ?? 'Compose workspace'}
      data-compose-workspace-status={state.status}
      data-compose-checkout-status={state.checkoutStatus}
      // FR-29/FR-33 (R2, tasks 060/102): rehydrated collection counts — a lightweight,
      // test-observable signal that the Load response's anchoredAnnotations/definedTermsTracking/
      // actionHistory reached this component (the resume restored prior state).
      data-compose-anchored-annotation-count={anchoredAnnotations.length}
      data-compose-defined-term-count={definedTermsTracking.length}
      data-compose-action-history-count={actionHistory.length}
      data-compose-pulled-annotation-count={pulledAnnotationCount}
      data-compose-reanchor-total={reanchorSummary?.total ?? 0}
      data-testid="compose-workspace"
    >
      {/*
        FR-01 (task 010): hidden native file input backing "Browse / open file".
        Always mounted (not gated on status === 'empty') so the ref stays stable
        across the empty -> loaded transition triggered by a successful pick.
      */}
      <input
        ref={browseFileInputRef}
        type="file"
        accept=".docx,application/vnd.openxmlformats-officedocument.wordprocessingml.document"
        className={styles.hiddenBrowseInput}
        onChange={handleBrowseFileSelected}
        data-testid="compose-workspace-browse-file-input"
        aria-hidden="true"
        tabIndex={-1}
      />

      {/* Empty state — Path A/B picker */}
      {state.status === 'empty' ? (
        <>
          {missingDrivePointer ? (
            <div
              className={styles.bannerStack}
              data-testid="compose-workspace-missing-drive-pointer"
              role="status"
              aria-live="polite"
            >
              <MessageBar intent="info">
                <MessageBarBody>
                  <MessageBarTitle>Document not fully provisioned</MessageBarTitle>
                  {"This document isn't fully provisioned in SharePoint Embedded (missing drive pointer) — " +
                    'pick another document or re-upload this one.'}
                </MessageBarBody>
              </MessageBar>
            </div>
          ) : null}
          <ComposeEmptyState
            onBlankRequested={handleBlankRequested}
            onTemplateRequested={handleTemplateRequested}
            onBrowseRequested={handleBrowseRequested}
            onSearchRequested={handleSearchRequested}
          />
        </>
      ) : null}

      {/* Loading spinner */}
      {state.status === 'loading' ? (
        <div className={styles.loadingState} role="status" aria-live="polite" data-testid="compose-workspace-loading">
          <Spinner size="medium" />
          <Text size={300}>Loading document…</Text>
        </div>
      ) : null}

      {/* Loaded / Saving — toolbar + editor + banners */}
      {showEditor ? (
        <>
          {/* FIX #5 (UAT): the separate ComposeToolbar command-bar row was removed — its
              Open-in-Word / Save / Push actions now live in the consolidated single-row
              ComposeFormatToolbar inside ComposeEditor (handlers threaded below). */}

          {/* gap 3.5 — return-from-Word re-anchor summary banner (task 054 component, mounted here). */}
          <ComposeReanchorBanner
            summary={reanchorSummary}
            onReview={() => setReanchorPanelOpen(true)}
            onDismiss={resetReanchor}
          />

          {/* G8 (FR-07, task 030) — external-change refresh banner. A CLEAN editor was already
              remounted transparently (this is the informational confirmation); a DIRTY editor shows
              the explicit Reload action so unsaved edits are never silently discarded (NFR-08). */}
          <ComposeExternalChangeBanner
            pending={state.externalChangePending}
            hasUnsavedEdits={isDirty}
            onReload={() => {
              if (state.documentRef) {
                dispatch({
                  kind: 'requestLoad',
                  documentRef: state.documentRef,
                  sessionId: state.sessionId,
                  externalChange: true,
                });
              }
            }}
            onDismiss={() => dispatch({ kind: 'externalChangeDismissed' })}
          />

          {/* Banner stack — errors / warnings / checkout status / assistant pending */}
          <ComposeBannerStack
            errorMessage={state.errorMessage}
            // UAT #10/#11 (task 052): when the save failed with a Word co-authoring lock (423), show the
            // honest "Open in Word" bar with Retry (re-run the save once Word is closed) + Reload-from-Word
            // (pull Word's latest version as the new baseline). No fake "Unlock" — none exists.
            saveErrorIsLock={state.saveErrorIsLock}
            onRetrySave={() => void triggerSave()}
            onReloadFromWord={
              state.documentRef?.speDriveItemId
                ? () => {
                    if (!state.documentRef) return;
                    dispatch({
                      kind: 'requestLoad',
                      documentRef: state.documentRef,
                      sessionId: state.sessionId,
                      externalChange: true,
                    });
                  }
                : undefined
            }
            checkoutStatus={state.checkoutStatus}
            checkoutLockedBy={state.checkoutLockedBy}
            checkoutFailureMessage={state.checkoutFailureMessage}
            importWarnings={state.importWarnings}
            // UAT round-7 #8 — suppress the "Some formatting was simplified" banner per reviewer request.
            hideImportWarnings
            pendingAssistantInsert={state.pendingAssistantInsert}
            saveSuccessToken={state.saveSuccessToken}
            // Prong 1 (task 055): when the last save could only anchor PART of the batch, show the honest
            // "N edits couldn't be saved — please redo them" warning (replaces the plain Saved ✓ bar).
            partialApply={state.partialApply}
            // FIX #7a: the transient "Open preview" link was REMOVED from the Saved ✓ banner — the
            // persistent affordance now lives in the Assistant chat (a "Saved to the DMS" message
            // with "Open preview", posted via the save-completed conduit). The banner keeps its
            // success signal (saveSuccessToken) but no longer carries the preview link.
            // Task 032 (FR-16 128KB budget, Leg B) — an honest notice when a prior review's findings
            // could not be fully restored on reopen (never silent absence).
            reviewFindingsDegraded={reviewFindingsDegraded}
          />

          {/* ai-advanced-capabilities-nda-r1 UAT round-5 #1 — the Review Summary panel MOVED from here
              INTO the editor's top region (ComposeEditor renders it below the toolbar, in-flow). The
              host still OWNS the review data (captured from the ledgered `compose_advisory_comments`
              event) + the open/toggle state, and threads it down via the `reviewSummary` prop; the
              editor renders the panel, derives each finding's location from the live doc, and wires
              navigation to its own cited-span primitive. */}

          {/* FR-04 (task 016): soft failure surfacing for draft materialization. */}
          {composeDraftError ? (
            <div
              className={styles.bannerStack}
              role="status"
              aria-live="polite"
              data-testid="compose-workspace-draft-error"
            >
              <MessageBar intent="warning">
                <MessageBarBody>
                  <MessageBarTitle>Could not insert AI draft</MessageBarTitle>
                  {composeDraftError}
                </MessageBarBody>
              </MessageBar>
            </div>
          ) : null}

          {/* FR-14 (task 051) — "Create Summary Memo" negative-path / failure surface. Covers BOTH the
              honest "no memo yet" 404 (never a silent empty export) and a transient generate/email
              network failure. Cleared at the start of the next Generate/Email attempt (mirrors the
              `composeDraftError` banner above — no separate dismiss affordance). */}
          {memoActionMessage ? (
            <div
              className={styles.bannerStack}
              role="status"
              aria-live="polite"
              data-testid="compose-workspace-memo-action-message"
            >
              <MessageBar intent="warning">
                <MessageBarBody>
                  <MessageBarTitle>Create Summary Memo</MessageBarTitle>
                  {memoActionMessage}
                </MessageBarBody>
              </MessageBar>
            </div>
          ) : null}

          <div className={styles.editorSlot}>
            <ComposeEditor
              ref={editorRef}
              docxBytes={state.docxBytes}
              initialHtml={state.seedHtml}
              // task 052 fast-follow (FR-08/FR-24/FR-25 wire gap): set atomically alongside docxBytes
              // per ComposeEditor's mount contract (JSDoc on these props) — sourced from the
              // stored-document Load response; `[]` for every other mount door.
              paraIdMap={state.paraIdMap}
              importedRevisions={state.importedRevisions}
              importedComments={state.importedComments}
              // The server DOCX→editor projection — every mount door hydrates it (tasks 010/011/012).
              // When present the editor mounts projection.html directly; null on a docx mount → (task
              // 013, F-2 "one reader") the editor renders an explicit error/unavailable state.
              projection={state.projection}
              documentRef={editorDocRef}
              bffBaseUrl={bffBaseUrl}
              sessionId={state.sessionId}
              // task 041 (FR-13): pass-through to getToolsForSurface via ComposeEditor's own
              // activeWorkType prop; ComposeEditor defaults to '*' when omitted.
              activeWorkType={activeWorkType}
              onDirtyChange={handleDirtyChange}
              onImportWarnings={handleImportWarnings}
              enqueueComposeAction={enqueueComposeAction}
              // FIX #5 (UAT): Word + Save actions folded into the consolidated toolbar.
              onOpenInWord={() => {
                void openInWordFlushed('web');
              }}
              onOpenInWordDesktop={() => {
                void openInWordFlushed('desktop');
              }}
              wordActionsDisabled={wordActionsDisabled}
              // G7 (task 022): the toolbar Save split-button threads its choice ('version' default /
              // 'new' fork) into triggerSave. Ctrl+S / the cross-pane bridge call triggerSave() → 'version'.
              onSave={mode => {
                void triggerSave(mode ?? 'version');
              }}
              canSave={canSaveNow}
              // G10 (task 040): the manual "Refresh Profile" button — only for a PROMOTED doc (there is a
              // sprk_document to re-profile). Undefined for a transient/unpromoted mount → the button hides.
              onRefreshProfile={state.documentRef?.sprkDocumentId ? () => void triggerRefreshProfile() : undefined}
              isRefreshingProfile={isRefreshingProfile}
              // "Open Document" — opens the source Dataverse Document in the shared preview modal
              // (RichFilePreviewDialog + BFF preview-url). Wired only for a doc with a preview
              // source (a promoted sprk_document); undefined → the toolbar button hides.
              onOpenDocument={canPreviewDocument ? () => setDocumentPreviewOpen(true) : undefined}
              // UAT #5 (task 053): always-available "Reload from source" — pulls the latest SPE bytes (e.g.
              // after an external Word-web edit that the change-check missed, or on demand). Gated on having an
              // SPE source (speDriveItemId); undefined for a born-in-editor doc with no source to reload from.
              // Honors the dirty-guard: confirm before discarding unsaved edits (no silent loss, NFR-08).
              onReloadFromSource={
                state.documentRef?.speDriveItemId
                  ? () => {
                      if (!state.documentRef) return;
                      if (
                        isDirty &&
                        !window.confirm('Reload from source? Your unsaved Compose changes will be discarded.')
                      ) {
                        return;
                      }
                      dispatch({
                        kind: 'requestLoad',
                        documentRef: state.documentRef,
                        sessionId: state.sessionId,
                        externalChange: true,
                      });
                    }
                  : undefined
              }
              isSaving={isSavingNow}
              // UAT round-2 items #1/#2 — the editor's "Review" toolbar dropdown toggles this docked
              // summary panel (owned here) alongside its own right-gutter "Review Notes". `open` mirrors
              // the panel's real render gate; `hasFindings` gates whether the "Review" control appears.
              reviewSummary={{
                open: reviewSummaryOpen && reviewSummaryFindings.length > 0,
                hasFindings: reviewSummaryFindings.length > 0,
                onToggle: () => setReviewSummaryOpen(o => !o),
                // UAT round-5 #1 — the editor now renders the panel; feed it the data + failure count.
                findings: reviewSummaryFindings,
                placementFailureCount: reviewSummaryFailedCount,
                // Task 032 gap #1 — complete the overallRisk data path (live event → state → panel)
                // that task 030 planted but nothing consumed. NOT re-introducing the round-5 #2
                // removed banner — AgreementReviewSummaryPanel's `overallRisk` prop stays deprecated
                // (ignored) by that standing UAT decision; this just stops the value from being
                // silently discarded.
                overallRisk: reviewSummaryOverallRisk,
                // FR-14 (task 051) — "Create Summary Memo" toolbar dropdown. Both actions READ the
                // persisted review-memo record server-side (render-from-persisted); the negative "no
                // memo yet" state surfaces via the memoActionMessage banner above, never a silent
                // empty export.
                onGenerateMemo: () => void handleGenerateMemo(),
                onEmailMemo: () => void handleEmailMemo(),
                isMemoActionInFlight: memoActionInFlight,
              }}
            />
          </div>
        </>
      ) : null}

      {/* FR-14 (task 051) — "Email memo" prefilled EmailComposer (ADR-045 canonical dialog wrapper).
          Opens ONLY after a successful memo read (handleEmailMemo); the user must act to send — this
          NEVER auto-sends. Body/subject are derived entirely from the persisted memo record. */}
      <SendEmailDialog
        open={memoEmailOpen}
        onClose={() => setMemoEmailOpen(false)}
        mode="compose"
        initialSubject={memoEmailSubject}
        initialBody={memoEmailBody}
        initialBodyFormat="HTML"
        authenticatedFetch={authenticatedFetch}
        bffBaseUrl={bffBaseUrl}
        onSent={() => setMemoEmailOpen(false)}
      />

      {/* "Open Document" preview modal — opened from the format toolbar's "Open Document"
          button. Reuses the shared RichFilePreviewDialog + the BFF
          GET /api/documents/{id}/preview-url endpoint (the SAME mechanism the ConversationPane
          "Open preview" chat affordance uses — no new component/endpoint, §11 reuse). Keyed off
          the current doc's sprk_document id; theme-aware via the dialog's semantic tokens (ADR-021).
          Rendered only once the doc is promoted (canPreviewDocument). */}
      {canPreviewDocument ? (
        <RichFilePreviewDialog
          open={documentPreviewOpen}
          documentId={previewDocumentId}
          documentName={state.documentRef?.fileName ?? 'Document'}
          onClose={() => setDocumentPreviewOpen(false)}
          fetchPreviewUrl={fetchDocumentPreviewUrl}
          onOpenFile={() => undefined}
          onEmailDocument={() => undefined}
          onCopyLink={() => undefined}
          onOpenRecord={() => {
            void previewNavigationService.openRecord('sprk_document', previewDocumentId);
          }}
        />
      ) : null}

      {/*
        Task 051: Multi-tab conflict dialog (FR-16 verbatim labels). Rendered
        when the /checkout-status probe revealed THIS user holds the lock from
        another session.
      */}
      <ComposeConflictDialog
        open={state.checkoutStatus === 'same-user-conflict'}
        documentDisplayName={state.documentRef?.fileName}
        conflictingSessionOpenedAt={state.sameUserConflictInfo?.checkedOutAt ?? null}
        onGoToOtherSession={handleGoToOtherSession}
        onForceCloseOtherSession={() => {
          void forceCloseAndAcquire();
        }}
        onCancel={discardAndCancel}
      />

      {/* gap 3.5 — return-from-Word conflict panel (task 054 component, mounted here). Opened from
          the reanchor banner's "Review changes" button; resolves flagged/orphaned anchors. */}
      <ComposeReanchorConflictPanel
        open={reanchorPanelOpen}
        summary={reanchorSummary}
        onResolve={handleReanchorResolve}
        onClose={() => setReanchorPanelOpen(false)}
      />

      {/* Error state — load failed; no document loaded */}
      {state.status === 'error' ? (
        <div className={styles.bannerStack} data-testid="compose-workspace-error-empty" role="alert">
          <MessageBar intent="error">
            <MessageBarBody>
              <MessageBarTitle>Cannot load document</MessageBarTitle>
              {state.errorMessage ?? 'An unknown error occurred.'}
            </MessageBarBody>
          </MessageBar>
        </div>
      ) : null}
    </div>
  );
}

export default ComposeWorkspace;
