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
 *   - spaarkeai-assistant-enhancements-r2 (DI-02 fix): flush-on-unmount. Every
 *     compose-tab close path (`WorkspaceTabManager.closeTab`, `clearAllTabs` on a
 *     History switch or exclusive-playbook reset) unmounted this component with no
 *     dirty-check. The unmount cleanup now best-effort flushes through the SAME
 *     `triggerSave` path when unsaved work is present (see `hasUnsavedWorkRef` below).
 *   - spaarkeai-compose-r7 (FR-03, tasks 040/041): draft-safe autosave. There is now a
 *     CLIENT-ONLY local draft autosave (a ~15s dirty-only localStorage snapshot via
 *     `composeDraftStore` — see the autosave effect), a `beforeunload` guard, and a
 *     toolbar save-state indicator. This DELIBERATELY reverses the prior "no autosave"
 *     invariant (spec ADR-Tensions path A) — but ONLY for local drafts: NO automatic
 *     SERVER save / SPE version is ever created. A write to the BFF still happens ONLY
 *     on an explicit Ctrl+S / toolbar-Save / bridge-chip save (plus the best-effort
 *     flush-on-unmount above); the autosave path never calls `triggerSave` (NFR-03).
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
  type ComposeDraftComment,
  type AdvisoryCommentInput,
} from './ComposeEditor';
// r8 task 055 — the paraId-vs-citation precedence shared with the AI-edit path and the
// advisory-comment path, so the whole-document review-flag path cannot drift from either.
import { resolveAnchorParaIds } from './composeAnchorResolution';
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
  useRegisterComposeAnchoredDocumentTextProvider,
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
  // FR-C05 (r8 task 052) — the stale-target "apply anyway?" question. ADR-050 canonical shell +
  // ADR-021 semantic tokens; no bespoke chrome (assessment §4.4 O-6).
  ConfirmModal,
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
// FR-05 (task 032, spaarkeai-compose-r6): "Apply firm template" dialog — 030 part-merge wiring.
import { ComposeApplyTemplateDialog } from './ComposeApplyTemplateDialog';
import { ComposeSaveNameDialog } from './ComposeSaveNameDialog';
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
// FR-07(b) (task 010): the non-rotating logical document id — minted once per logical document,
// persisted client-side, and rehydrated on recovery. Shared key for FR-03 (040) + FR-07 dedup (011).
import {
  startNewComposeLogicalId,
  clearActiveComposeLogicalId,
  uniquifyForkFileName,
  recoverActiveComposeLogicalId,
} from './composeIdentity';
// FR-03 (task 040): CLIENT-ONLY local draft store for draft-safe autosave (localStorage; no BFF).
import { saveComposeDraft, getComposeDraft, clearComposeDraft } from './composeDraftStore';
// FR-03/FR-07 (task 010): the canonical identity accessor = the draft-store key. Value import (the
// sibling `import type` block below is type-only).
import { getComposeLogicalIdentity } from '../types/compose-contracts';
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
  // task 012 (r6, render-on-save cutover): the canonical content model retained from the mount
  // door's response (state.loadedContentModel) and sent on the imported model-path save shape.
  ComposeContentModel,
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
function toAssistantInsertPayload(
  event: WorkspacePaneEvent,
  // FR-07(c) (task 011): the dedup-identity fallback when the event omits a documentRef. The call
  // site supplies the currently-mounted document's ref (which carries the task-010 composeLogicalId)
  // — or a freshly-minted-id ref when nothing is mounted — so a legacy assistant-insert NEVER enters
  // the staging/save path with the empty `{ speDriveItemId: '' }` sentinel (the id-less dedup hole).
  fallbackDocumentRef?: ComposeDocumentRef
): ComposeAssistantToWorkspaceFlow {
  return {
    type: 'compose_assistant_insert',
    documentRef: event.documentRef ?? fallbackDocumentRef ?? { speDriveItemId: '' },
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
 * FR-02 (task 030): the placeholder name a NEW born-in-editor / blank / template document carries
 * BEFORE it is named. The first-save name modal (UC-3) always fires before this reaches the server on
 * a user-initiated Save, so it never LANDS on a kept SPE record. It is still the tab-strip label for
 * an unnamed draft (the "Document1" convention). A background best-effort flush (beforeunload) that
 * bypasses the modal substitutes {@link autoNameForUnnamedDraft} so no path persists the literal
 * "Untitled document.docx".
 */
const UNTITLED_DOC_NAME = 'Untitled document.docx';

/**
 * FR-03 (task 040): dirty-autosave tick interval for the CLIENT-ONLY local draft store. ~15s per
 * spec §8 phase 4 — tunable, not a hard contract. Each tick writes localStorage ONLY when the editor
 * is dirty; it never calls the BFF (NFR-03).
 */
const COMPOSE_DRAFT_AUTOSAVE_INTERVAL_MS = 15000;

/**
 * FR-S05 (r8 task 012): the save request's own deadline. Before this, a save had none — a hung
 * request stranded `status === 'saving'` forever, and the only escape was a page reload, which
 * discarded the document. The `AbortSignal` this drives makes a hung save terminate as a FAILED
 * save (dirty flag intact, retry available) instead of a dead end.
 *
 * 120s is deliberately generous rather than snappy: a save can carry the full base64-encoded
 * retained original (documents up to the body ceiling task 015 addresses) over an arbitrary link,
 * and a timeout that fires on a save that WOULD have completed is itself a way to lose work. The
 * value bounds the pathological case; it is not a latency target.
 */
const COMPOSE_SAVE_TIMEOUT_MS = 120000;

/** True when a name is still the unnamed-draft placeholder (never a user-chosen name). */
function isUntitledDraftName(name?: string): boolean {
  return !name || name.trim().length === 0 || name === UNTITLED_DOC_NAME;
}

/**
 * FR-02 (task 030): a non-colliding fallback name for the residual case where an UNNAMED draft is
 * persisted WITHOUT going through the first-save modal (only the best-effort beforeunload flush, which
 * cannot show UI during unload). Guarantees the negative criterion "no code path lands
 * 'Untitled document.docx'". Phase 4 (040) client draft supersedes this flush for unnamed docs.
 */
function autoNameForUnnamedDraft(): string {
  const stamp = new Date().toISOString().slice(0, 16).replace('T', ' ').replace(/:/g, '-');
  return `Compose draft ${stamp}.docx`;
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

/** Inverse of {@link arrayBufferToBase64} — decodes an ASP.NET Core base64 byte[] response field.
 * task 012 (r6): the Browse `/project` response echoes `content` back ONLY when server-side paraId
 * minting mutated the caller's bytes; the client must adopt that echo as the retained mount bytes
 * so editor/model/carrier share ONE paraId universe. */
function base64ToArrayBuffer(b64: string): ArrayBuffer {
  const binary = atob(b64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return bytes.buffer;
}

/**
 * task 012 (r6) — the editor-handle surface the sibling ComposeEditor task is adding for the
 * render-on-save cutover. Typed STRUCTURALLY here (intersected at the call sites) so this file
 * compiles and its tests run before the handle lands; once `ComposeEditorHandle` declares these
 * members this local type is satisfied by it and adds nothing. Both members are optional-called
 * (typeof / `?.()` guards) — an older editor build without them falls back to the transitional
 * op-log save shape, mirroring the existing `getAnchoredComments` guard convention.
 */
interface ComposeEditorImportedModelHandle {
  /** Folds the editor's edits + session/advisory comment threads onto the loaded canonical model;
   * resets the editor dirty flag. Null when the editor is unavailable → op-log fallback.
   * `snapshot` (sibling F4) is the BUILD-TIME `{paraId → rejectText}` baseline map the posted model
   * was derived from — passed back verbatim to {@link adoptBaselineSnapshot} on save-200 so edits
   * typed DURING the in-flight save are never masked by a live-doc recapture. Typed opaque here
   * (`unknown`): this host never inspects it, only round-trips it. */
  buildImportedContentModel?: (
    loadedModel: ComposeContentModel,
    opts: { trackChanges: boolean }
  ) => { model: ComposeContentModel; warnings: Array<{ code: string; count: number }>; snapshot?: unknown } | null;
  /** Sibling F4: adopt the BUILD-TIME snapshot the posted model was built from as the editor's new
   * baseline (NOT a live-doc recapture — that would mask mid-flight edits). Preferred on save-200. */
  adoptBaselineSnapshot?: (snapshot: unknown) => void;
  /** Recaptures the editor's baseline snapshot from the LIVE doc after a confirmed model-path save.
   * FALLBACK ONLY (older editor build without {@link adoptBaselineSnapshot}) — a live recapture can
   * mask edits typed during the in-flight save. */
  recaptureBaselineSnapshot?: () => void;
}

/**
 * 026-F5 (task 012, r6) / F7 (task 013): merge the save's degradation-warning SOURCES into ONE
 * save-warning set, summing counts on duplicate codes. Sources (variadic): the SERVER's render-side
 * `degradationWarnings`, the client mapper's own warnings (`buildImportedContentModel(...).warnings`),
 * and — on the FIRST model-path save only — the mount-time canonical-model projection flatten
 * warnings (`state.loadedContentModelWarnings`, task 013 F7). Defensive against malformed entries
 * (skipped, never thrown); a missing/non-finite count contributes 1.
 */
function mergeDegradationWarnings(
  ...sources: ReadonlyArray<ReadonlyArray<{ code: string; count: number }>>
): Array<{ code: string; count: number }> {
  const byCode = new Map<string, number>();
  for (const source of sources) {
    for (const w of source) {
      if (!w || typeof w.code !== 'string' || w.code.length === 0) continue;
      const count = typeof w.count === 'number' && Number.isFinite(w.count) && w.count > 0 ? w.count : 1;
      byCode.set(w.code, (byCode.get(w.code) ?? 0) + count);
    }
  }
  return Array.from(byCode.entries()).map(([code, count]) => ({ code, count }));
}

/**
 * FR-S06 (r8 task 013) — the closed set of terminal save outcomes the server reports on the wire.
 * Mirrors `ComposeSaveOutcome` / `ComposeSaveOutcomes` in `IComposeService.cs`; the strings ARE the
 * contract. There is deliberately no "unknown" member: an unrecognized value is handled where it is
 * read (treated as not-a-success), not by widening the set.
 */
type ComposeSaveOutcome =
  | 'persisted'
  | 'persisted-with-warnings'
  | 'refused-stale'
  | 'refused-locked'
  | 'refused-invalid'
  | 'storage-failed'
  | 'partially-recorded';

/**
 * FR-S06 (r8 task 013): did this save actually store the document?
 *
 * `persisted` and `persisted-with-warnings` are the only outcomes where the bytes are durable and
 * complete. `partially-recorded` is deliberately NOT here: the document is stored but does not contain
 * everything submitted, so the user has work to redo and must not be told an unqualified "Saved".
 *
 * An ABSENT outcome means an older BFF that predates the field, whose 200 always meant a completed
 * write — so absent is a success. An UNRECOGNIZED value means a newer BFF added a member this client
 * does not know: treat it as not-a-success, because the safe failure direction is to under-claim.
 */
function isSuccessfulSaveOutcome(outcome: ComposeSaveOutcome | undefined): boolean {
  return outcome === undefined || outcome === 'persisted' || outcome === 'persisted-with-warnings';
}

/**
 * FR-S01 (r8 task 010) — how a save failure is classified before it is routed to an outcome.
 *
 * `authenticatedFetch` (ADR-028) RETURNS only when `response.ok`; every non-2xx is THROWN as an
 * `ApiError` carrying `.status` + ProblemDetails. Two failure classes never reach an HTTP status at
 * all, and both used to render as the same dead-end "Save failed: …" string:
 *   - `AuthError` — thrown when the 401 retry budget is exhausted, or no response was received.
 *     It carries `code`, never `status` (see `Spaarke.Auth/src/authenticatedFetch.ts`).
 *   - a transport rejection — `fetch` itself rejected (offline, DNS, CORS, abort), so no HTTP
 *     exchange happened. A malformed success body (`response.json()` throwing) lands here too.
 */
type SaveFailureClass =
  | { kind: 'http'; status: number; detail: string }
  | { kind: 'auth'; detail: string }
  // FR-S05 (r8 task 012): the save's own timeout fired (or the request was otherwise aborted). A
  // subclass of `transport` by mechanism — `fetch` rejects and no HTTP exchange completed — but it
  // is the ONE failure class we caused ourselves, and the only one whose honest advice is "it took
  // too long" rather than "check your connection". Kept a distinct member so the message can say so.
  | { kind: 'aborted'; detail: string }
  | { kind: 'transport'; detail: string };

/**
 * FR-S01 (r8 task 010): classify a thrown save failure. Never throws.
 *
 * The `status` read is deliberately STRUCTURAL rather than `err instanceof ApiError`: `instanceof`
 * fails silently when `@spaarke/auth` resolves to two copies across a bundle boundary (the host page
 * and this library), and a silent fall-through to the generic message is precisely the defect FR-S01
 * exists to remove. `.status` is the only field the routing needs, so it is read directly.
 */
function classifySaveFailure(err: unknown): SaveFailureClass {
  const detail = err instanceof Error ? err.message : String(err);
  const status = (err as { status?: unknown } | null | undefined)?.status;
  if (typeof status === 'number' && status >= 100 && status <= 599) {
    return { kind: 'http', status, detail };
  }
  // FR-S05 (r8 task 012): an aborted `fetch` rejects with a DOMException named `AbortError`
  // (`TimeoutError` where `AbortSignal.timeout` is used). Read `.name` STRUCTURALLY for the same
  // reason `.status` is: `instanceof DOMException` is not reliable across realms, and jsdom/Node
  // differ on whether the rejection is a DOMException or a plain Error at all.
  const name = (err as { name?: unknown } | null | undefined)?.name;
  if (name === 'AbortError' || name === 'TimeoutError') {
    return { kind: 'aborted', detail };
  }
  if (err instanceof Error && err.name === 'AuthError') {
    return { kind: 'auth', detail };
  }
  return { kind: 'transport', detail };
}

/**
 * FR-S01 (r8 task 010): the honest save-failure sentence for a classified failure.
 *
 * Every message states (a) that nothing was saved and (b) that the pending edits survive — which is
 * TRUE on every path here: `commitSaved()` (which drops the op-log batch) fires only after a
 * confirmed 200, so a refused save leaves the document dirty with its edits intact for a retry.
 *
 * `ApiError.message` is already `problemDetails.detail ?? title ?? "HTTP {status}"`, so the
 * synthesized `HTTP {status}` fallback is stripped rather than echoed back after our own status text.
 *
 * 423 is NOT handled here — it routes to the lock banner (which owns its own copy + Retry).
 */
const SIGN_IN_EXPIRED_MESSAGE =
  'Not saved — your sign-in expired. Refresh the page to sign in again, then Save. Your changes are still here.';

function saveFailureMessage(failure: SaveFailureClass): string {
  // A 401 that exhausted the retry budget arrives as an AuthError (no status); one that did not can
  // still arrive as an ApiError with status 401. Same cause, same recovery, same sentence.
  if (failure.kind === 'auth') {
    return SIGN_IN_EXPIRED_MESSAGE;
  }
  // FR-S05 (r8 task 012): the save's own timeout stopped a request that never came back. "Not saved"
  // is the honest claim: nothing here confirmed a write, and the edits are intact for a retry. The
  // server may still have completed it, which is why the sentence points at a reload rather than
  // promising the document is unchanged.
  if (failure.kind === 'aborted') {
    return (
      'Not saved — the save took too long and was stopped. Your changes are still here — try again. ' +
      'If it keeps timing out, reload the document first to check whether an earlier attempt landed.'
    );
  }
  if (failure.kind === 'transport') {
    return "Not saved — we couldn't complete the request (network or connection problem). Your changes are still here — try again.";
  }
  const { status } = failure;
  const serverDetail = (failure.detail && failure.detail !== `HTTP ${status}` ? failure.detail : '')
    .trim()
    .replace(/\.$/, '');
  const suffix = serverDetail ? `: ${serverDetail}.` : '.';
  switch (status) {
    case 401:
      return SIGN_IN_EXPIRED_MESSAGE;
    case 403:
      return `You do not have permission to save this document${suffix} Your changes are still here.`;
    case 404:
      return (
        'Not saved — this document no longer exists at its saved location (it may have been moved or ' +
        'deleted). Your changes are still here — use Save As to store them as a new document.'
      );
    default:
      return status >= 500
        ? `Not saved — the server hit an error (HTTP ${status})${suffix} Your changes are still here — try again.`
        : `Not saved — the server rejected this save (HTTP ${status})${suffix} Your changes are still here.`;
  }
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
 * FR-C05 (r8 task 052) — Tier-3 safe excerpt for the stale-target confirmation. The two clause texts
 * are shown so the user can SEE what changed rather than take our word for it; they are truncated
 * because a 40-line clause in an `xs` modal is unreadable, and they are never logged.
 */
const STALE_CLAUSE_EXCERPT_CHARS = 160;
function truncateClause(text: string): string {
  const collapsed = text.replace(/\s+/g, ' ').trim();
  return collapsed.length > STALE_CLAUSE_EXCERPT_CHARS
    ? `${collapsed.slice(0, STALE_CLAUSE_EXCERPT_CHARS)}…`
    : collapsed;
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
   * UAT-11 (2026-08-18, honest/safe): a RETRY resolver the host supplies so a transient-create Save
   * can RE-RESOLVE the BU container at save time instead of relying solely on the one-shot mount-time
   * `containerId`. The mount resolver runs once in a `useEffect([])`; if Xrm wasn't ready, a transient
   * 401, or a Dataverse query fault made it fail, `containerId` stays undefined and the save gate used
   * to emit a DISHONEST "your BU has no storage container configured" — telling the admin to fix a
   * correctly-configured BU. This callback lets the save path (a) retry the resolution and (b) learn
   * WHY it's still missing so the banner is honest:
   *   - `resolved`     → a container id (use it — the retry recovered a transient mount failure)
   *   - `no-container` → the query succeeded but the BU genuinely has no `sprk_containerid`
   *   - `unavailable`  → the resolution couldn't run/complete (no Xrm host, threw, transient fault)
   * Optional — a host that omits it keeps the pre-UAT-11 one-shot behavior + generic message.
   */
  resolveContainer?: () => Promise<{ containerId?: string; outcome: 'resolved' | 'no-container' | 'unavailable' }>;

  /**
   * FR-05 create-on-save (task 100): invoked once a transient draft is persisted as a NEW
   * `sprk_document` on first Save, with the server-minted `sprk_documentid`. The host wires this
   * to `useCreateOnSaveAssociation.associate(newDocumentId)` so a chosen parent association is
   * written (a no-op when the user chose "none"). Non-fatal — the document already exists.
   */
  onCreateOnSaveComplete?: (newSprkDocumentId: string) => void | Promise<void>;

  /**
   * UAT (2026-08-18, owner): the Document + Analysis are created on SAVE. Invoked ONCE, on the FIRST
   * save (create-on-save) of a NEW document, WHEN a review/analysis actually ran on it (there are
   * review findings) — with the server-minted `sprk_documentid`. The host wires this to create + bind
   * the `sprk_analysis` for the review session (so the Summary Memo works and the Analysis is
   * reopenable from history). NOT called for a plain drafting doc (no review), nor on subsequent saves
   * / a reopened Analysis (those are the replace/version path and the Analysis already exists). Fired
   * after the parent-association write so both land on the freshly-created document.
   */
  onReviewedDocumentCreated?: (
    newSprkDocumentId: string,
    sessionId: string,
    documentName: string
  ) => void | Promise<void>;

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
  // UAT-04 (2026-08-18): compact top-area operation-in-flight indicator. Semantic tokens only
  // (ADR-021 dark-mode-correct); an unobtrusive subtle-background strip above the banner stack.
  operationIndicator: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalS,
    paddingInline: tokens.spacingHorizontalM,
    paddingBlock: tokens.spacingVerticalXS,
    backgroundColor: tokens.colorNeutralBackground2,
    color: tokens.colorNeutralForeground2,
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
    resolveContainer,
    onCreateOnSaveComplete,
    onReviewedDocumentCreated,
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

  // UAT (2026-08-18, save-driven Analysis): refs the save closure reads for the first-save Analysis
  // create. `onReviewedDocumentCreatedRef` mirrors the host callback; `hasReviewFindingsRef` mirrors
  // "a review actually ran on this doc" (reviewSummaryFindings.length > 0, defined further down — the
  // ref lets the earlier-declared save callback read it without a stale closure). Updated via effects.
  const onReviewedDocumentCreatedRef = React.useRef(onReviewedDocumentCreated);
  React.useEffect(() => {
    onReviewedDocumentCreatedRef.current = onReviewedDocumentCreated;
  }, [onReviewedDocumentCreated]);
  const hasReviewFindingsRef = React.useRef<boolean>(false);

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

  // UAT-11 (honest/safe): keep the host's save-time container RETRY resolver in a ref so the save
  // callback can re-resolve without re-subscribing (mirrors containerIdRef). See the prop docs.
  const resolveContainerRef = React.useRef(resolveContainer);
  React.useEffect(() => {
    resolveContainerRef.current = resolveContainer;
  }, [resolveContainer]);

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

  // Banner consolidation (2026-08-19): the pending-redline anchor-failure notice, lifted OUT of
  // ComposeEditor so it renders in the single ComposeBannerStack rail (above the toolbar) instead of a
  // hand-rolled bar below the toolbar. The editor pushes changes via onRedlineErrorChange; dismissal
  // routes back through editorRef.current.clearRedlineError().
  const [pendingRedlineError, setPendingRedlineError] = React.useState<
    import('./hooks/usePendingRedline').PendingRedlineError | null
  >(null);

  // FR-C05 (r8 task 052) — the stale-target question the editor raises when an anchored suggestion's
  // clause no longer reads the way it did when the model wrote it. The editor DETECTS and holds the
  // suggestion back (placing nothing); this host ASKS (ConfirmModal below) and — the load-bearing
  // half — writes the DURABLE resolution through the shipped FR-17 supersession seam so the reopen
  // pass cannot re-raise it after a refresh (task-050 assessment §4.4 O-2/O-4/O-5).
  const [redlineStaleTarget, setRedlineStaleTarget] = React.useState<
    import('./hooks/usePendingRedline').PendingRedlineStaleTarget | null
  >(null);
  const [staleResolutionBusy, setStaleResolutionBusy] = React.useState(false);

  // FR-C06 (r8 task 053) — the anchorless-replay PROPOSAL. Raised only for a `compose` ledger entry
  // written BEFORE task 052's catalog change and replayed afterwards: it carries prose and no anchor,
  // so the bounded fallback located a candidate paragraph and is asking whether that is the right
  // place. NOTHING is in the document while this is non-null. Same host contract as the stale question
  // above — ask with a ConfirmModal, then write the durable FR-17 supersession either way (O-2/O-5).
  const [redlineLegacyProposal, setRedlineLegacyProposal] = React.useState<
    import('./hooks/usePendingRedline').PendingRedlineLegacyProposal | null
  >(null);
  const [proposalResolutionBusy, setProposalResolutionBusy] = React.useState(false);

  // FR-01/FR-03 (task 020): Auto Save state, surfaced as the Save-dropdown toggle. ON by default per
  // spec (draft-safe autosave). Task 020 wires the CONTROL to this state; the actual draft-safe autosave
  // behavior (client-only local draft, beforeunload guard, recovery) is Phase 4 (tasks 040/041), which
  // consumes this same state. Kept here (the workspace) so 040 can drive autosave off it without moving it.
  const [autoSaveEnabled, setAutoSaveEnabled] = React.useState(true);

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
    // 090 close-out review (HIGH): prefer the DOCUMENT'S OWN drive (documentRef.driveId — stamped
    // by the create-on-save re-target, UAT P2) over the host/search drive, mirroring triggerSave's
    // saveDriveId. A doc this workspace minted (born-in-editor fork, PDF-sourced) lives in the BU
    // container's drive, which the host `driveId` prop does not identify — a remount (external-
    // change reload / post-apply-template requestLoad) must fetch from where the doc lives.
    const loadDriveId = state.documentRef?.driveId ?? effectiveDriveId;
    if (!loadDriveId) {
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
        const qs = new URLSearchParams({ driveId: loadDriveId, tenantId });
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
          // UAT-12 (2026-08-18): true when the server's annotation read FAILED, so the empty
          // revisions/comments above are a fallback — NOT proof the document is clean. Parsed
          // defensively (older BFF omits it → falsy → no banner).
          annotationReadFailed?: boolean;
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
          // task 012 (r6): the canonical ComposeContentModel (additive since commit 70be80006).
          // Typed loosely + parsed defensively — undefined/null (older BFF, or a failed canonical
          // projection) → null → the save falls back to the transitional op-log shape.
          contentModel?: ComposeContentModel | null;
          // task 013 (r6, review F7): the canonical-model projection's flatten warnings (previously
          // server-log-only) — retained and folded into saveDegradationWarnings on the FIRST
          // model-path save, where the loss they describe materializes.
          contentModelWarnings?: Array<{ code: string; count: number }> | null;
          // Task 041 (FR-06, PDF intake): 'pdf' = `content` is the docx SYNTHESIZED server-side from
          // the PDF's canonical-model projection (task 040). Parsed defensively (older BFF omits it).
          sourceFormat?: string | null;
          // FR-S08 (r8 task 015): the server-advertised save size limit, in bytes. Optional — an
          // older BFF omits it, which the reader below normalizes to null (no numeric pre-flight).
          maxDocumentBytes?: number | null;
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
          // FR-A09 (task 044): the drive-item the server SERVED — see the reducer for why this must
          // come from the response rather than from `docRef.speDriveItemId` we requested with.
          speDriveItemId: payload.documentSpeId,
          fileName: payload.fileName,
          // Set ATOMICALLY with docxBytes (the ComposeEditor mount contract).
          paraIdMap: hydratedParaIdMap,
          importedRevisions: hydratedImportedRevisions,
          importedComments: hydratedImportedComments,
          // UAT-12: carry the honest annotation-read-failed signal into state so the banner stack can
          // warn the user not to treat a doc-with-unreadable-annotations as clean.
          annotationReadFailed: payload.annotationReadFailed === true,
          projection: hydratedProjection,
          // task 012 (r6): retain the canonical model atomically with the projection (same response).
          contentModel: payload.contentModel ?? null,
          // task 013 (r6, F7): the projection's flatten warnings — same defensive-parse convention
          // as the collections above (omitted/malformed → null).
          contentModelWarnings: Array.isArray(payload.contentModelWarnings) ? payload.contentModelWarnings : null,
          // FR-S08 (r8 task 015): the server-advertised save size limit. Read defensively — an older
          // BFF omits it, and `null` there means "do no numeric pre-flight", never "unlimited".
          maxDocumentBytes: typeof payload.maxDocumentBytes === 'number' ? payload.maxDocumentBytes : null,
          // G1 (FR-01, task 020): normalize undefined (older BFF / Path B continuation) to `null`.
          origin: payload.origin ?? null,
          // Task 041 (FR-06, PDF intake): the source-format marker + a client-minted transient dedup
          // key for the PDF's create-on-save routing (repeated saves of this session dedup to ONE new
          // docx record — the G7 mechanism, reused). Only PDF-sourced loads carry either.
          sourceFormat: payload.sourceFormat === 'pdf' ? 'pdf' : null,
          transientKey: payload.sourceFormat === 'pdf' ? mintTransientKey() : undefined,
          // FR-07(b) (task 010): a PDF-sourced load is a transient (create-on-save) document — give it
          // a persisted non-rotating logical id so draft recovery + dedup key off a stable value.
          composeLogicalId: payload.sourceFormat === 'pdf' ? startNewComposeLogicalId() : undefined,
          // FR-09 (task 071): stamp the AUTHORITATIVE drive this doc was loaded from so a later
          // Reload-from-source (requestLoad) fetches from where the doc LIVES, never falling into the
          // `!loadDriveId → reset` blank branch (the R6 D4 root cause). `payload.driveId` is required
          // on the Load response.
          driveId: payload.driveId,
        });
      } catch (err) {
        if (ac.signal.aborted) return;
        // FR-S09 sweep (r8 task 016): status routing lives HERE, because this is where every non-2xx
        // arrives. `authenticatedFetch` (ADR-028) RETURNS only when `response.ok` and THROWS a typed
        // `ApiError` otherwise — so the `if (!response.ok)` block that used to sit above the parse,
        // carrying "Document not found. It may have been deleted or moved." and "You do not have
        // permission to open this document.", could never execute. Every failed load rendered the
        // generic `Failed to load document: HTTP 404` instead. Same defect as FR-S01 removed from the
        // save path and FR-S09 item 4 removed from the checkout path; this is the load path's copy.
        const status = (err as { status?: unknown } | null | undefined)?.status;
        const httpStatus = typeof status === 'number' && status >= 100 && status <= 599 ? status : null;
        const message = err instanceof Error ? err.message : String(err);
        dispatch({
          kind: 'loadFailed',
          errorMessage:
            httpStatus === 404
              ? 'Document not found. It may have been deleted or moved.'
              : httpStatus === 403
                ? 'You do not have permission to open this document.'
                : httpStatus !== null
                  ? `Failed to load document (HTTP ${httpStatus}).`
                  : `Failed to load document: ${message}`,
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
        // FR-S09 sweep (r8 task 016): `response.ok` is necessarily TRUE here — a non-2xx threw into
        // the catch below. The condition was not wrong, it was unfalsifiable, which is the same defect
        // as a dead branch wearing the opposite sign. The abort check is the real guard.
        if (!ac.signal.aborted) {
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
  // FR-S05 (r8 task 012): the in-flight guard. `state.status === 'saving'` is NOT sufficient on its
  // own — `triggerSave` closes over `state`, so two calls dispatched in the same tick (Ctrl+S held
  // down, the toolbar button plus the cross-pane bridge chip, an unmount flush landing on top of a
  // manual save) both read the pre-dispatch `'loaded'` and both POST. A ref is read at call time,
  // not at render time, so it closes that window. Two concurrent saves of the same document race
  // each other's write and each other's `commitSaved`, which is how an edit gets acknowledged by a
  // save that never carried it.
  const saveInFlightRef = React.useRef(false);
  const triggerSave = React.useCallback(
    async (
      saveMode: ComposeSaveMode = 'version',
      // FR-02 (task 030): the name captured by the first-save / Save As modal (UC-3). When present it
      // OVERRIDES the create-on-save displayName (→ server ResolveFileName + sprk_documentname), so a
      // newly-named document persists under the entered name instead of the 'Untitled document.docx'
      // placeholder. Undefined for every already-named / replace-path save (unchanged behavior).
      opts?: { displayNameOverride?: string }
    ): Promise<void> => {
      // FR-S09 item 1 (r8 task 016): these were two bare `return`s. The user pressed Save and
      // NOTHING happened — no banner, no console line, no state change, no way to tell a refusal
      // apart from a broken button. Each case is now decided explicitly, and the two that are
      // reachable-and-invisible now say so.
      if (state.status === 'saving' || saveInFlightRef.current) {
        // A save is already running. Deliberately NOT a banner: "Saving…" and a disabled Save button
        // are already on screen saying exactly this, and a second, contradictory signal ("your save
        // was refused") would be worse than the silence. Documented as non-silent rather than assumed
        // to be — the distinction this whole task turns on.
        return;
      }
      if (state.status !== 'loaded') {
        // 'loading' / 'error' / 'empty'. The toolbar and Ctrl+S do not exist in these states, so the
        // only caller that can arrive here is programmatic — the Assistant's "Add the document to the
        // DMS" chip, or an unmount flush. It used to drop them on the floor. The message is worded to
        // stay true when it surfaces (the banner renders once the editor is up), and the reducer
        // deliberately does NOT move `status` for a refusal — see the `saveFailed` case.
        dispatch({
          kind: 'saveFailed',
          errorMessage: 'That save did not run — the document was still opening. Nothing was lost; press Save again.',
        });
        return;
      }
      if (!state.documentRef || !editorRef.current) {
        // THE silent one. Status is 'loaded', so the editor and an ENABLED Save button are both on
        // screen, and pressing Save did nothing at all — repeatedly, with no explanation. It happens
        // when the editor's imperative handle has not attached yet (a render race) or the document
        // reference was lost, and it is indistinguishable from a dead button.
        dispatch({
          kind: 'saveFailed',
          errorMessage:
            'That save did not run — the editor was not ready. Your changes are still here; press Save again.',
        });
        return;
      }

      // FR-02 (task 030): trimmed user-entered name from the save-name modal, if any.
      const nameOverride = opts?.displayNameOverride?.trim() || undefined;

      // G7 (FR-06, task 022): "Save New Document" (fork) forces the create-on-save path — a brand-new
      // sprk_document record — EVEN when the doc already has a real SPE item. A fresh transient key is
      // minted so the fork gets its OWN dedup identity, and `forkNew` tells the server to SKIP the
      // transient-key dedup lookup for this call (a deliberate new document, not a new version).
      const forkNew = saveMode === 'new';
      // Task 041 (FR-06, PDF intake): a PDF-sourced doc (the mounted docx was SYNTHESIZED from a PDF,
      // task 040) must NEVER take the replace path — that would write docx bytes onto the `.pdf`
      // drive-item. EVERY save while sourceFormat==='pdf' routes create-on-save (a NEW Word document;
      // the original PDF stays untouched — the honest "saves as a docx version" contract), with the
      // load-minted documentRef.transientKey deduping repeated saves onto ONE new record (G7). On
      // success, saveSucceeded re-targets documentRef to the new docx identity + clears sourceFormat,
      // so subsequent saves take the normal replace path.
      const pdfSourced = state.sourceFormat === 'pdf';
      // FR-05 (task 100): a TRANSIENT (Browse/Upload) draft has NO SPE drive-item — it persists via
      // create-on-save into the client-resolved BU container, not the replace path. Branch on the
      // absence of a real speDriveItemId (mountTransient sets it to ''), OR on a deliberate Save-New fork.
      const isTransientCreate = forkNew || !state.documentRef.speDriveItemId || pdfSourced;
      // G7: the dedup key to send on a create-on-save. A fork mints a fresh key (its own identity going
      // forward); a normal transient save reuses the mount-time key so repeated saves dedup to ONE record.
      const effectiveTransientKey = forkNew ? mintTransientKey() : state.documentRef.transientKey;
      // FR-07(a) (task 012): a Save-New fork must be a REAL fork — a distinct file + record, never a
      // silent re-version of the original. Two parts, both keyed off the fork's fresh transient key:
      //  (1) uniquify the create-on-save displayName so the SPE PUT-by-path lands a DISTINCT drive-item
      //      (a same-name PUT would re-version the original — the FR-07a coalesce bug);
      //  (2) mint a fresh task-010 composeLogicalId so the fork carries a NEW logical id, not the
      //      original's (adopted onto the forked documentRef by saveSucceeded below).
      // FR-02 (task 030): a Save As now carries a user-entered name (the modal). Honor it directly when
      // it is DISTINCT from the source file name — a distinct name already lands a distinct SPE
      // drive-item, so the FR-07(a) coalesce guard is unneeded and appending a "(copy …)" token would
      // mangle the user's deliberate name. Fall back to the machine uniquify only when there is NO
      // override or the entered name equals the source (same-name Save As still must not re-version).
      const forkDisplayName = forkNew
        ? nameOverride && nameOverride !== state.documentRef.fileName
          ? nameOverride
          : uniquifyForkFileName(
              nameOverride ?? state.documentRef.fileName,
              effectiveTransientKey ?? mintTransientKey()
            )
        : null;
      const forkLogicalId = forkNew ? startNewComposeLogicalId() : undefined;
      // `let` (UAT-11): the transient-create gate below may REPLACE this with a save-time retry result.
      let saveContainerId = state.documentRef.containerId ?? containerIdRef.current;
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

      // FR-S08 (r8 task 015): the size PRE-FLIGHT. Measured here, before the request is built, so an
      // oversize document costs the user nothing — no base64 encode of 25+ MB, no upload they wait out
      // only to have it rejected at the far end.
      //
      // The limit is the one the SERVER advertised on the response that mounted this document, never a
      // compiled-in copy: a second constant is precisely how "your file is fine" becomes a rejection,
      // and the server is the side that actually enforces it. When no limit was advertised — an older
      // BFF, or a mount door that never called the server (a local Browse pick, a born-in-editor seed)
      // — we do NO numeric check and let the server refuse honestly with its own number. Guessing here
      // would reintroduce the divergence this requirement exists to remove.
      //
      // Only the retained ORIGINAL bytes are measured: they are the only bytes the client ever sends,
      // and the ContentModel shapes are small structured JSON the server renders (a born-in-editor doc
      // has no retained bytes at all).
      const advertisedLimit = state.maxDocumentBytes;
      const outgoingBytes = state.docxBytes?.byteLength ?? 0;
      if (advertisedLimit !== null && outgoingBytes > advertisedLimit) {
        const asMb = (n: number) => Math.round((n / (1024 * 1024)) * 10) / 10;
        dispatch({
          kind: 'saveFailed',
          errorMessage:
            `Not saved — this document is ${asMb(outgoingBytes)} MB and the limit is ${asMb(advertisedLimit)} MB. ` +
            'Your changes are still here. Remove or compress large embedded images, or split the document, then save again.',
        });
        return;
      }

      // FR-S05 (r8 task 012): claim the in-flight guard. Sited HERE — after all the synchronous
      // setup above, immediately before the first `await` (the transient-create container
      // resolution). Two reasons, both deliberate:
      //   • Sync code cannot interleave, so nothing above this line needs guarding; the window a
      //     second save can slip through opens at the first suspension point.
      //   • A synchronous throw in that setup would otherwise latch the guard forever, silently
      //     killing saving for the rest of the session — a worse failure than the double-POST the
      //     guard exists to prevent. Below this line every exit path releases it.
      // The TEST moved to the entry guards above (FR-S09 item 1) so a refused second save can say so;
      // the CLAIM stays here, at the last moment before the first `await`, for the reasons above.
      saveInFlightRef.current = true;
      /** The single release point: the request's `finally`, and the two early returns below. */
      const finishSaveAttempt = (): void => {
        saveInFlightRef.current = false;
      };
      const failEarly = (errorMessage: string): void => {
        finishSaveAttempt();
        dispatch({ kind: 'saveFailed', errorMessage });
      };

      if (isTransientCreate) {
        let resolvedContainerId = saveContainerId;
        // UAT-11 (2026-08-18, honest/safe): the mount-time container resolver is a one-shot
        // useEffect([]) — if Xrm wasn't ready, a transient 401, or a Dataverse fault made it fail,
        // `containerId` stays undefined and the OLD gate emitted a DISHONEST "your BU has no storage
        // container configured" for what may be a correctly-configured BU. RETRY here (if the host
        // supplied a resolver) and only claim "no container configured" when the query actually
        // confirms the BU has none — otherwise say honestly that we couldn't determine it.
        let containerOutcome: 'resolved' | 'no-container' | 'unavailable' | 'unknown' = resolvedContainerId
          ? 'resolved'
          : 'unknown';
        if (!resolvedContainerId && resolveContainerRef.current) {
          try {
            const retry = await resolveContainerRef.current();
            containerOutcome = retry.outcome;
            if (retry.containerId) {
              resolvedContainerId = retry.containerId;
              containerIdRef.current = retry.containerId; // cache for subsequent saves this mount
            }
          } catch {
            containerOutcome = 'unavailable';
          }
        }
        if (!resolvedContainerId) {
          const errorMessage =
            containerOutcome === 'no-container'
              ? 'Cannot save this new document — your Business Unit has no storage container configured. ' +
                'Contact an administrator to set the container on your Business Unit.'
              : // unavailable / unknown: do NOT blame the BU config — the resolution didn't complete.
                "Cannot save this new document yet — we couldn't determine your storage container " +
                '(the Dataverse context may still be loading). Please try again in a moment.';
          failEarly(errorMessage);
          return;
        }
        saveContainerId = resolvedContainerId;
      } else if (!saveDriveId) {
        failEarly('Cannot save — SPE drive configuration missing.');
        return;
      }

      dispatch({ kind: 'requestSave' });
      // FR-S01 (r8 task 010): did the POST reach a 2xx? Everything after the fetch — response parsing,
      // dispatches, draft cleanup, the editor's `commitSaved()` — runs inside the same `try`, so a throw
      // there would otherwise be reported as "Not saved" for a document the server ALREADY wrote. That is
      // the same class of dishonest outcome this task removes, pointing the other way.
      let savePersisted = false;
      // FR-S06 (r8 task 013): a 2xx arrived, but we have NOT yet read the outcome that says whether
      // anything was written. Before 013 a 200 was taken to mean "written" — that assumption is exactly
      // what let a total write failure render as "Saved ✓". So the window between the response arriving
      // and the outcome being read is genuinely INDETERMINATE, and claiming either result there would be
      // a guess. Tracked separately from `savePersisted` so the catch can say so honestly.
      let saveReachedServer = false;
      // FR-S05 (r8 task 012): the save's deadline. `AbortController` (not `AbortSignal.timeout`) so
      // the timer can be cleared the moment the exchange finishes — an un-cleared timeout would fire
      // later and abort a signal nobody is reading, and, more importantly, keeps a 2-minute timer
      // alive per save. The abort surfaces as a rejected `fetch` → the catch below classifies it
      // `aborted` → a FAILED save with the dirty flag intact, which is what an unfinished save is.
      const saveAbort = new AbortController();
      const saveTimeoutId = window.setTimeout(() => saveAbort.abort(), COMPOSE_SAVE_TIMEOUT_MS);
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
        // UAT-23 (2026-08-18, honest/safe): the filter above excludes `deletedContentFlag` ops from
        // what we apply. A GENUINE later-deletion is expected to drop silently; but the
        // `anchorLostFlag` subset (an edit whose anchor drifted so it can't be re-anchored) is a
        // still-valid edit being lost — count it so the save surfaces an honest degradation warning
        // instead of dropping it in silence. Only meaningful on the op-log apply path; the model path
        // (buildImportedContentModel) captures the current text whole, so a drifted op is moot there.
        const anchorLostOpCount = opLogSnapshot
          ? opLogSnapshot.orderedOps.filter(entry => entry.anchorLostFlag).length
          : 0;

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
        // UAT-22 (2026-08-18, honest/safe): collect any session/advisory comment threads that resolve
        // NO anchored comment (their live anchor is gone / non-paragraph / drifted across a paragraph)
        // — a comment the user still sees in the gutter that would otherwise be silently dropped from
        // the save. Counted below into an honest "N comment(s) couldn't be saved" degradation warning.
        let droppedCommentCount = 0;
        const anchoredComments: ComposeAnchoredComment[] =
          typeof editorRef.current.getAnchoredComments === 'function'
            ? editorRef.current.getAnchoredComments(() => {
                droppedCommentCount += 1;
              })
            : [];

        // Base64-encode the RETAINED ORIGINAL bytes via the shared module-level encoder (see
        // `arrayBufferToBase64` above — also used by the FR-03/task 011 browse->project round-trip).
        const encodeRetained = arrayBufferToBase64;

        // ── task 012 (r6, render-on-save cutover): the IMPORTED model-path probe ──
        // A DIRTY imported/loaded doc (retained bytes present) whose mount captured the canonical
        // model (`state.loadedContentModel` — Load/Upload/Project responses, commit 70be80006) now
        // saves by sending the MERGED content model: `buildImportedContentModel` folds the editor's
        // edits AND the session+advisory comment threads onto the loaded model (so the model shape
        // sends NO separate `comments` field) and resets the editor dirty flag. Guards, in order:
        //   • `editorIsDirty` (review F3, CRITICAL) — a CLEAN imported save MUST keep the pre-012
        //     byte-identical passthrough (FR-06a): re-rendering an unedited doc from the flatten-tier
        //     model would silently drop content the render path degrades (e.g. an NDA's signature
        //     text-boxes) on a zero-edit Ctrl+S. Clean saves fall through to the unchanged
        //     content-only shapes below (operationLog is undefined there → byte-identical persist).
        //   • `state.docxBytes` — both imported branches hold retained bytes; born-in-editor stays out.
        //   • `state.loadedContentModel` — legacy session / older BFF / failed canonical projection
        //     → null → the transitional op-log shape below runs completely unchanged.
        //   • typeof check — an older editor build without the handle method → same op-log fallback
        //     (mirrors the `getAnchoredComments` guard convention).
        // `trackChanges`: BINDING null-handling — null/undefined origin → imported → tracked (true);
        // only a durable 'authored' marker saves clean.
        // Op-log discipline: `serializeOperationLog()` is called FIRST (result discarded) so the
        // high-water mark is recorded and `commitSaved()` after a 200 drops the batch — prevents
        // unbounded op accumulation on the model path. A dirty imported save always recorded the mark
        // via the `opLogSnapshot` read above; the extra call is belt-and-braces for any path that
        // reaches here without one.
        const importedModelHandle = editorRef.current as ComposeEditorHandle & ComposeEditorImportedModelHandle;
        let importedBuilt: {
          model: ComposeContentModel;
          warnings: Array<{ code: string; count: number }>;
          snapshot?: unknown;
        } | null = null;
        if (
          editorIsDirty &&
          state.docxBytes &&
          state.loadedContentModel &&
          typeof importedModelHandle.buildImportedContentModel === 'function'
        ) {
          const trackChanges = state.origin !== 'authored';
          if (!opLogSnapshot) importedModelHandle.serializeOperationLog();
          importedBuilt = importedModelHandle.buildImportedContentModel(state.loadedContentModel, { trackChanges });
        }
        // Non-null ⇔ THIS save posts the imported model shape (a null return = editor unavailable →
        // the existing op-log shape below, unchanged).
        const usedModelPath = importedBuilt !== null;

        // FR-05 (task 100): the create-on-save route carries no id in the path (the draft has no drive-item
        // yet) and sends `containerId`; the replace route carries the SPE id + driveId. Both hit
        // ComposeService.SaveAsync — the server branches on DocumentSpeId presence.
        const url = isTransientCreate
          ? `${bffBaseUrl}/api/compose/documents/create-on-save`
          : `${bffBaseUrl}/api/compose/documents/${encodeURIComponent(state.documentRef.speDriveItemId)}/save`;

        // Save shapes (the client authors NO bytes) — task 012 (r6) render-on-save cutover:
        //  1. Born-in-editor (create OR re-save)     → { contentModel } (buildContentModel — the editor
        //     folds session+advisory comments INTO the model now; no separate `comments` field, and
        //     NEVER a baselineVersionId — a born-in-editor doc's stored versionId is the drive-ITEM id
        //     and would 404 the server's version fetch).
        //  2. Imported + canonical model retained    → { contentModel: built.model, content (retained
        //     bytes) OR baselineVersionId } — the MODEL shape. No operationLog (ignored server-side on
        //     this shape), no paraIdMap (the model carries the id universe), no `comments` (folded in).
        //  3. Imported, NO canonical model (legacy session / older BFF / editor build without the
        //     mapper / mapper returned null) → the TRANSITIONAL op-log shape, completely unchanged:
        //     { content/baselineVersionId, operationLog?, comments, paraIdMap }.
        let requestBody: Record<string, unknown>;
        // FR-S03 (r8 task 012): did THIS save capture a born-in-editor content model? That capture
        // now watermarks the editor instead of clearing its dirty flag, so the post-success
        // `commitSaved()` must fire for it — see the commit gate below. Kept as a plain flag set at
        // the call sites rather than a closure, so `editorRef.current`'s narrowing is not disturbed.
        let sentEditorContentModel = false;
        if (isTransientCreate) {
          // UAT #1A fix (task 050): the born-in-editor discriminant is retained-bytes presence ONLY — the SAME
          // signal the replace path uses (`bornInEditor = !state.docxBytes`). Only a TRUE born-in-editor doc
          // (no retained bytes) renders from the authored content model; an imported transient (has bytes)
          // takes the model shape (task 012) or the transitional op-log shape.
          const bornInEditorRender = !state.docxBytes;
          // G7 (task 022): `transientKey` = the transient dedup key (repeated create-on-save → ONE record);
          // `forkNew` = the Save-New fork flag (skips dedup → a deliberately new record).
          const createCommon = {
            containerId: saveContainerId,
            tenantId,
            sessionId: state.sessionId,
            // FR-07(a) (task 012): a Save-New fork sends the uniquified name so the SPE PUT-by-path
            // creates a DISTINCT drive-item (a real fork), never a silent re-version of the original.
            // Task 041 (FR-06): a PDF-sourced create-on-save names the NEW document as Word — swap
            // the .pdf extension for .docx (the saved bytes ARE docx; a ".pdf"-named docx would
            // mislead every downstream consumer). Non-PDF, non-fork creates keep the existing name verbatim.
            // FR-02 (task 030): precedence for the create-on-save name —
            //  1. forkDisplayName (Save As, above);
            //  2. the modal's nameOverride (first create-on-save of a newly-named doc);
            //  3. PDF-sourced: the source PDF name with .pdf→.docx (task 041);
            //  4. the current file name IF it is a real user name (imported .docx keeps its name);
            //  5. an auto-name fallback — NEVER the literal 'Untitled document.docx' placeholder, so
            //     no path (incl. a modal-bypassing background flush) lands an "Untitled" record.
            displayName:
              forkDisplayName ??
              nameOverride ??
              (pdfSourced
                ? (state.documentRef.fileName ?? 'document.pdf').replace(/\.pdf$/i, '') + '.docx'
                : isUntitledDraftName(state.documentRef.fileName)
                  ? autoNameForUnnamedDraft()
                  : (state.documentRef.fileName ?? null)),
            transientKey: effectiveTransientKey,
            forkNew,
            // Task 041 B-MED-3 (operator resolution 2026-08-07, option C): on a PDF-sourced create,
            // send the SOURCE PDF's sprk_document id so the server files the new Word document
            // ALONGSIDE it (inherits the record's matter/project/… links). Undefined for a Path-B
            // PDF (no record — nothing to inherit) and for every non-PDF create (unchanged).
            sourceDocumentRecordId: pdfSourced ? (state.documentRef.sprkDocumentId ?? undefined) : undefined,
          };
          if (bornInEditorRender) {
            // Shape 1 — born-in-editor create-on-save. task 012 amendment: `buildContentModel()` now folds
            // session+advisory comment threads into the model itself (the server removed the engine
            // comment-bake for ALL ContentModel saves), so the separate `comments` field is GONE here.
            // paraIdMap stays (existing field; empty for a born-in-editor doc anyway).
            requestBody = { ...createCommon, paraIdMap, contentModel: editorRef.current.buildContentModel() };
            sentEditorContentModel = true;
          } else if (usedModelPath && importedBuilt) {
            // Shape 2 — imported transient create-on-save, MODEL shape: the merged model + the retained
            // ORIGINAL bytes as the render carrier.
            requestBody = {
              ...createCommon,
              contentModel: importedBuilt.model,
              content: encodeRetained(state.docxBytes!),
            };
          } else {
            // Shape 3 — transitional op-log create-on-save (unchanged): retained ORIGINAL bytes as the
            // baseline + the tracked op-log so the server applies redlines via ComposeShadowPatchEngine —
            // NOT the renderer. operationLog is undefined on a clean mount → the server persists the
            // retained bytes byte-identical (FR-06a). C2: paraIdMap lets the server stamp client-minted
            // ids onto the retained baseline before the engine resolves anchors.
            requestBody = {
              ...createCommon,
              comments: anchoredComments,
              paraIdMap,
              content: encodeRetained(state.docxBytes!),
              operationLog,
            };
          }
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
          // UAT 2026-07-19 P2: `driveId` = the drive the doc lives in (documentRef.driveId after a
          // create-on-save), falling back to the host default — so a born-in-editor doc's second save +
          // baseline re-fetch target the correct drive. G2: `documentRecordId` MUST ride every
          // reopened-doc save — the server reads the durable sprk_composeorigin marker for THIS record.
          const replaceCommon = {
            driveId: saveDriveId,
            tenantId,
            sessionId: state.sessionId,
            documentRecordId: state.documentRef.sprkDocumentId ?? null,
            displayName: state.documentRef.fileName ?? null,
            // UAT-25/26 (2026-08-18): the load-time SPE ETag this save's edits are based on, for honest
            // stale-base detection server-side. On the whole-body ContentModel re-author path a stale base
            // is refused (412 reload-and-reapply) instead of silently overwriting an external writer; on
            // the op-log path it re-anchors. The server prefers its own save-stamp when this session has
            // already saved — this covers the first-save-of-a-pre-existing-item gap.
            baselineETag: state.etag ?? undefined,
          };
          if (bornInEditor) {
            // Shape 1 — in-session born-in-editor re-save: re-author from the content model (no retained
            // baseline to delta onto — task 039). NEVER sends baselineVersionId (the stored versionId is
            // the drive-ITEM id — a real version fetch would 404). task 012 amendment: no separate
            // `comments` field — `buildContentModel()` folds the threads into the model.
            requestBody = { ...replaceCommon, paraIdMap, contentModel: editorRef.current.buildContentModel() };
            sentEditorContentModel = true;
          } else if (usedModelPath && importedBuilt) {
            // Shape 2 — loaded/imported OR reopened-authored replace save, MODEL shape: the merged model
            // + the baseline source (retained bytes when still held — the same-session case — else the
            // load-time versionId so the server re-fetches the baseline).
            requestBody = {
              ...replaceCommon,
              contentModel: importedBuilt.model,
              ...(state.docxBytes
                ? { content: encodeRetained(state.docxBytes) }
                : { baselineVersionId: state.versionId ?? undefined }),
            };
          } else {
            // Shape 3 — transitional op-log replace save (unchanged). The load-time version id = the
            // op-log's BASE VERSION; lets the server re-fetch the baseline even without the client bytes.
            // The server applies the op-log tracked (imported) or CLEAN (authored, per the durable
            // marker) — REQ-1/REQ-2. C2: paraIdMap lets the server stamp minted ids before the engine
            // resolves anchors. operationLog undefined on a clean save → baseline persists byte-identical.
            requestBody = {
              ...replaceCommon,
              comments: anchoredComments,
              paraIdMap,
              baselineVersionId: state.versionId ?? undefined,
              content: state.docxBytes ? encodeRetained(state.docxBytes) : undefined,
              operationLog,
            };
          }
        }

        // ADR-028: `authenticatedFetch` stays the transport (never a raw `fetch`) — the signal rides
        // its existing `RequestInit`, which it spreads onto every attempt, so the deadline also
        // bounds the 401 retry loop rather than restarting with it.
        const response = await authenticatedFetch(url, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(requestBody),
          signal: saveAbort.signal,
        });

        // The request completed with a 2xx. Whether anything was WRITTEN is the outcome field's job.
        saveReachedServer = true;

        // FR-S01 (r8 task 010): there is NO `if (!response.ok)` branch here, and there must never be one
        // again. `authenticatedFetch` returns ONLY when `response.ok` — every non-2xx is thrown as a typed
        // `ApiError` (ADR-028 / ADR-019 ProblemDetails). The block that used to sit here (423 lock banner,
        // 412 reload flow, 403 copy) was therefore unreachable from R5 onward, which is why every server
        // refusal rendered as one undifferentiated "Save failed: …" with no recovery. Status routing now
        // lives in the `catch` below, on `ApiError.status` — the ONE save-error path.
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
          // FR-S06 (r8 task 013): the server's TERMINAL OUTCOME for this save, from a closed set. This —
          // not the HTTP status — is what says whether anything was written: the create-on-save
          // container-failure path returns `storage-failed` on a 200, which is exactly how a total write
          // failure used to render as "Saved ✓". Optional so an older BFF (no field) still works; absent
          // is treated as `persisted`, which is what that older BFF's 200 always meant.
          outcome?: ComposeSaveOutcome;
          // Prong 1 (task 055): best-effort partial-apply summary — present only when some ops couldn't be
          // anchored server-side (the save still succeeded with the resolvable edits). Absent on the common
          // clean-batch path. Drives the honest "N edits couldn't be saved — please redo them" banner.
          partialApply?: {
            total: number;
            appliedCount: number;
            unresolvedCount: number;
          } | null;
          // Task 026 (FR-04 graceful degradation): render-side degradation warnings — content the server
          // simplified/dropped while authoring this save (success-with-warnings, never a 422). task 012
          // (r6, 026-F5): routed into the SEPARATE `saveDegradationWarnings` state/banner family below —
          // NO longer merged into `importWarnings` (which the workspace suppresses via hideImportWarnings).
          degradationWarnings?: Array<{ code: string; count: number }> | null;
          // task 012 (r6): the POST-SAVE content model on render-path saves — adopted as the new merge
          // base for the next model-path save. Optional/null (older BFF, or a non-render-path save).
          contentModel?: ComposeContentModel | null;
        };

        // FR-S06 (r8 task 013): THE honesty gate. A 200 does NOT mean the document was stored — the
        // create-on-save container-failure path returns a result (it does not throw), so the endpoint
        // wraps it in a 200 carrying `storage-failed`. Before this branch, that rendered as "Saved ✓"
        // over a write that never happened.
        //
        // Deliberately BEFORE the Assistant notification and the `saveSucceeded` dispatch: announcing a
        // save to chat, clearing the dirty flag, and committing the op-log are all success side effects,
        // and every one of them would be wrong here. Returning leaves the document dirty with its edits
        // intact, exactly as a thrown failure does — so a retry re-sends the same work.
        if (!isSuccessfulSaveOutcome(payload.outcome)) {
          dispatch({
            kind: 'saveFailed',
            errorMessage:
              payload.outcome === 'partially-recorded'
                ? 'Partly saved — the document was stored, but not everything was recorded. Reload the ' +
                  'document to see what landed, then redo anything missing.'
                : 'Not saved — the server accepted the request but could not store the document. Your ' +
                  'changes are still here — try again, and contact an administrator if it keeps failing.',
          });
          return;
        }

        // FR-S01 (task 010) + FR-S06 (task 013): the document is now CONFIRMED persisted — a 2xx AND a
        // success outcome. Set here rather than at the fetch so the catch below can honestly say "it was
        // saved" without that claim resting on the status alone; a throw before this point is reported
        // as a failure, which is correct, because at that point we did not yet know the write landed.
        savePersisted = true;

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
          // task 012 (r6): on a MODEL-PATH save, adopt the post-save model as the new merge base —
          // prefer the server's returned model; when the server omitted it, keep the model we POSTED
          // (never regress to null on success). Omitted on op-log / born-in-editor saves → the reducer
          // keeps whatever base it had.
          contentModel: usedModelPath && importedBuilt ? (payload.contentModel ?? importedBuilt.model) : undefined,
          // FR-07(a) (task 012): on a Save-New fork, adopt the uniquified fork name + the fresh
          // task-010 logical id so the forked documentRef reflects the NEW document's identity (a real
          // fork), not the original's. Undefined on every non-fork save (the reducer keeps existing).
          fileName: forkDisplayName ?? undefined,
          composeLogicalId: forkLogicalId,
        });
        // 026-F5 (task 012, r6): save-time degradation warnings are their OWN warning family — the old
        // dispatch into `importWarnings` both clobbered the load-time import warnings AND never rendered
        // (the workspace passes `hideImportWarnings` to the banner stack per UAT round-7 #8). Merge the
        // server's render-side warnings with the imported-model mapper's own (summing counts on duplicate
        // codes) and REPLACE; a clean save dispatches null so a stale banner CLEARS (026-F5 second half).
        //
        // task 013 (r6, review F7): a MODEL-PATH save additionally folds in the mount-time canonical-model
        // projection flatten warnings (`state.loadedContentModelWarnings` — e.g. text-box-flattened,
        // complex-object-dropped): the loss they describe MATERIALIZES exactly here, on the first save that
        // renders from the flatten-tier model. The `saveSucceeded` dispatch above (which carried
        // `contentModel`) already CLEARED them in the reducer, so a subsequent model save does not repeat
        // them — the adopted post-save model reflects the loss. An op-log / byte-identical save does NOT
        // fold them (nothing was flattened on that path) and keeps them retained for a later model save.
        // Task 042 / A-LOW-2 (041 review): the pdf-intake-* facts materialized at LOAD (the intake
        // already reflowed the PDF into the synthesized carrier), so they surface on the first save
        // EVEN when the save took the op-log/byte-passthrough path (usedModelPath false) — the docx
        // flatten warnings keep their model-path-only fold (their loss materializes only when a
        // model render runs). ACCEPTED-HONEST (042-review LOW-2): on repeated op-log-path saves the
        // pdf-intake facts re-surface with each save (nothing on that path clears
        // loadedContentModelWarnings) — deliberate: every persisted artifact embodies the intake
        // loss, and the banner is dismissible; a model-path save clears them via the reducer as before.
        const heldWarnings = state.loadedContentModelWarnings ?? [];
        // UAT-22 / UAT-23 (2026-08-18, honest/safe): on the OP-LOG apply path, a dropped comment
        // (its live anchor gone) and an anchor-lost edit (its anchor drifted so it can't be
        // re-anchored) would otherwise vanish from the save with no signal. Surface them as their own
        // degradation codes so the user is told "a comment / an edit couldn't be saved" instead of
        // silently losing it. Gated to `!usedModelPath` — the model path captures the current text +
        // comments whole (buildImportedContentModel), so neither loss occurs there.
        const clientSurfacedLossWarnings: Array<{ code: string; count: number }> = !usedModelPath
          ? [
              ...(droppedCommentCount > 0 ? [{ code: 'comment-anchor-unresolved', count: droppedCommentCount }] : []),
              ...(anchorLostOpCount > 0 ? [{ code: 'edit-anchor-lost', count: anchorLostOpCount }] : []),
            ]
          : [];
        // ══════════════════════════════════════════════════════════════════════════════════════
        // TASK 044 (r8) — the docx flatten warnings are NO LONGER folded into the save banner.
        //
        // The R6-era reasoning above ("the loss they describe MATERIALIZES exactly here, on the first
        // save that renders from the flatten-tier model") was correct WHEN EVERY SAVE REBUILT THE WHOLE
        // BODY. Since task 040 it is false: a text box, field or content control on a block the user did
        // not touch is CLONED VERBATIM and nothing about it is simplified. Folding the load-time flatten
        // warnings in anyway produced a "Some formatting was simplified when saving" banner on documents
        // that lost nothing — the false signal that trains a reader to ignore the true ones.
        //
        // The server is now authoritative and precise: `payload.degradationWarnings` carries one warning
        // per construct ACTUALLY lost, counted on the block that was re-rendered (ComposeBlockMerge's
        // shortfall report). Nothing is suppressed — the true warnings arrive from the party that knows.
        //
        // `pdf-intake-*` facts are the exception and are STILL folded on both paths: that reflow already
        // happened at LOAD, before any save, so the loss is real regardless of what the save does.
        const pdfIntakeFacts = heldWarnings.filter(
          w => typeof w?.code === 'string' && w.code.startsWith('pdf-intake-')
        );
        const mergedSaveWarnings = mergeDegradationWarnings(
          payload.degradationWarnings ?? [],
          usedModelPath && importedBuilt ? importedBuilt.warnings : [],
          pdfIntakeFacts,
          clientSurfacedLossWarnings
        );
        dispatch({
          kind: 'saveDegradationWarnings',
          warnings: mergedSaveWarnings.length > 0 ? mergedSaveWarnings : null,
        });

        // Clear the local dirty flag so the Save button disables until the next edit. This is the
        // workspace's MIRROR of the editor's authoritative `dirtyRef`, not a second source of truth:
        // the `commitSaved()` below fires on every successful save and its `onDirtyChange` is the
        // last writer, correctly re-arming Save when the user typed mid-flight. This assignment
        // covers only the case where the editor handle is already gone (unmount race). FR-S03 (r8
        // task 012): it sits on the success branch, AFTER the outcome-honesty gate — a failed save
        // never reaches it.
        setIsDirty(false);

        // FR-07(b) (task 010): a transient draft that just persisted (create-on-save promotion)
        // now has a real sprkDocumentId/speDriveItemId — it is no longer an UNSAVED draft to
        // recover. Clear the active-draft slot so a later reload does not resurrect it as a blank
        // draft. Guarded on the mounted doc having carried a transient logical id (a stored-doc
        // replace-path save has none, so it leaves any other slot untouched).
        if (state.documentRef?.composeLogicalId) {
          clearActiveComposeLogicalId();
          // FR-03 (task 040): drop the CLIENT-ONLY local draft for the PRE-save logical id — the doc
          // just persisted (an SPE version now exists), so a later reload must NOT resurrect it as an
          // unsaved draft. Keyed by the same accessor the autosave tick used; scoped so an unrelated
          // document's draft is left intact.
          clearComposeDraft(getComposeLogicalIdentity(state.documentRef));
        }

        // task 038 (zero-error guardrails): NOW that the save is confirmed (200), commit the persisted
        // op-log batch + recompute the editor's dirty flag. `serializeOperationLog()` no longer resets on
        // read (that was the data-loss bug — a 422 emptied the log BEFORE the POST, so a retry re-sent an
        // empty log and lost every valid text edit in the batch); this post-200 commit is what finally drops
        // the batch, and ONLY on success. FR-S01 (r8 task 010): a rejected save THROWS out of
        // `authenticatedFetch` straight to the catch below, so it never reaches here — the op-log + dirty
        // flag survive for a retry that re-sends the same edits.
        // Called AFTER setIsDirty(false) so `commitSaved`'s onDirtyChange (true iff concurrent edits arrived
        // during the in-flight save) is the last writer and leaves the Save state correct. Gated on having
        // actually SENT an op-log: the born-in-editor create-on-save path re-derives its whole content model
        // each save (buildContentModel), so it needs no op-log commit.
        //
        // task 012 (r6): the MODEL path sent NO operationLog (so the pre-existing `if (operationLog)`
        // commit cannot double-fire on it) but DID record the op-log high-water mark via the
        // serializeOperationLog call in the probe above — commit it now so the superseded batch drops
        // (prevents unbounded op accumulation across model-path saves). Baseline hand-off (sibling
        // F4, mid-flight edit race): PREFER `adoptBaselineSnapshot(built.snapshot)` — the BUILD-TIME
        // `{paraId → rejectText}` map the POSTED model was derived from — over a live-doc
        // `recaptureBaselineSnapshot()`, which would silently absorb (mask) any edits typed during
        // the in-flight save. The live recapture remains only as the older-editor-build fallback.
        // Exactly ONE commitSaved fires per successful save on every path.
        //
        //
        // FR-S03 (r8 task 012): the commit gate gains the BORN-IN-EDITOR case. `buildContentModel()`
        // no longer clears the dirty flag at build time — it watermarks — so a born-in-editor save
        // that reached here without committing would leave the document permanently dirty after a
        // save that actually succeeded (the mirror image of the bug this task removes). The gate
        // stays a gate rather than becoming unconditional: a CLEAN byte-identical passthrough save
        // captures nothing and must touch no editor state at all (review F3).
        //
        // The invariant across all three arms: exactly ONE `commitSaved()` per successful save that
        // captured anything, and none for one that captured nothing.
        if (usedModelPath) {
          const postSaveHandle = editorRef.current as (ComposeEditorHandle & ComposeEditorImportedModelHandle) | null;
          if (postSaveHandle && typeof postSaveHandle.adoptBaselineSnapshot === 'function' && importedBuilt) {
            postSaveHandle.adoptBaselineSnapshot(importedBuilt.snapshot);
          } else {
            postSaveHandle?.recaptureBaselineSnapshot?.();
          }
        }
        if (usedModelPath || operationLog || sentEditorContentModel) {
          editorRef.current?.commitSaved?.();
        }

        // FR-05 (task 100, gap 1.8): once a transient draft is persisted as a NEW sprk_document,
        // let the host write any chosen parent association (associate() no-ops on "none"). The
        // document already exists, so an association failure is NOT a save failure.
        // UAT-13 (2026-08-18, honest/safe): but it is NOT nothing either — a failed association leaves
        // the document ORPHANED (saved but not filed under its matter). The old code only console.warn'd
        // it, so the user saw an unqualified "Saved ✓" while the doc was silently unfiled. Surface an
        // honest, dismissible, RETRYABLE banner instead of swallowing it.
        if (isTransientCreate && onCreateOnSaveComplete && payload.documentRecordId) {
          try {
            await onCreateOnSaveComplete(payload.documentRecordId);
          } catch (assocErr) {
            // eslint-disable-next-line no-console
            console.warn('[ComposeWorkspace] create-on-save association write failed (surfaced):', assocErr);
            dispatch({ kind: 'associationWarning', documentRecordId: payload.documentRecordId });
          }
        }
        // UAT (2026-08-18, owner): SAVE-driven Analysis. On the FIRST save of a NEW document that had a
        // review/analysis run on it, tell the host to create + bind the sprk_analysis (so the Summary
        // Memo works and the Analysis is reopenable). Gated on `hasReviewFindingsRef` — a plain drafting
        // doc creates NO Analysis. Only on the transient-create (first) save; subsequent saves and a
        // reopened Analysis are the replace/version path (no create-on-save → the Analysis already
        // exists). Fire-and-forget — a failure never fails the save (the host handles it honestly).
        if (isTransientCreate && payload.documentRecordId && hasReviewFindingsRef.current && state.sessionId) {
          try {
            await onReviewedDocumentCreatedRef.current?.(
              payload.documentRecordId,
              state.sessionId,
              forkDisplayName ?? state.documentRef?.fileName ?? 'Document'
            );
          } catch (analysisErr) {
            // eslint-disable-next-line no-console
            console.warn('[ComposeWorkspace] reviewed-document Analysis create failed (non-fatal):', analysisErr);
          }
        }
      } catch (err) {
        // FR-S01 (r8 task 010): THE save-error path. Every server refusal arrives here as a thrown
        // `ApiError` (ADR-028), so each status gets its own outcome + recovery affordance instead of one
        // dead-end string. The op-log and dirty flag are untouched on every branch — `commitSaved()` fires
        // only after a confirmed 200 — so each message can honestly promise the edits survive.
        const failure = classifySaveFailure(err);

        // The POST already returned 2xx — the document IS written. Something in the post-save bookkeeping
        // threw. Every message below promises "Not saved", which would be a lie here; say what actually
        // happened instead. The `saveSucceeded` dispatch (if it got that far) stands.
        if (savePersisted) {
          dispatch({
            kind: 'saveFailed',
            errorMessage:
              'Your document was saved, but something went wrong immediately afterwards ' +
              `(${failure.detail}). Reload the document to see the saved version.`,
          });
          return;
        }

        // FR-S06: a 2xx arrived but we never read the outcome (e.g. an unreadable body), so we do not
        // know whether the write landed. Say that, rather than picking a side — "Not saved" would risk
        // a duplicate save, and "Saved" would risk silent data loss. Reloading is the one action that
        // resolves the ambiguity.
        if (saveReachedServer) {
          dispatch({
            kind: 'saveFailed',
            errorMessage:
              'We could not confirm whether your document was saved — the server replied, but the reply ' +
              `could not be read (${failure.detail}). Reload the document to check before saving again; ` +
              'your changes are still here either way.',
          });
          return;
        }

        // UAT #10/#11 (task 052): 423 = a Word-for-the-web CO-AUTHORING lock (Spaarke never does a formal
        // checkout, so a 423 is always co-authoring). No programmatic unlock exists — `isLock` routes to the
        // honest "Open in Word" bar with Retry Save + Reload from Word, not a fake Unlock. The server detail
        // already carries the honest message; the fallback covers a detail-less 423.
        if (failure.kind === 'http' && failure.status === 423) {
          dispatch({
            kind: 'saveFailed',
            errorMessage:
              failure.detail && failure.detail !== 'HTTP 423'
                ? failure.detail
                : 'This document is open in Word — close it there, then Retry. It also releases automatically ' +
                  'within a few minutes. Your Compose changes are safe and still pending.',
            isLock: true,
          });
          return;
        }

        // FR-S09 item 6 (r8 task 016): the service asked us to wait. Distinct from every other status
        // here because nothing is wrong — not with the document, not with the request, not with the
        // server — and the only useful instruction is a duration. Before task 016 a Graph throttle
        // reached the client as a 500 whose body read "Save failed: InvalidOperationException: Service
        // temporarily unavailable due to Graph rate limiting", which reads as a fault and invites a
        // support ticket instead of a coffee. The server states the wait in its ProblemDetails detail
        // (and in Retry-After); prefer it, and fall back to a conservative sentence rather than
        // inventing a number.
        if (failure.kind === 'http' && failure.status === 429) {
          dispatch({
            kind: 'saveFailed',
            errorMessage:
              failure.detail && failure.detail !== 'HTTP 429'
                ? failure.detail
                : 'Not saved — the document service is busy right now. Nothing was overwritten and your ' +
                  'changes are still here — try again in a moment.',
          });
          return;
        }

        // FR-S02 (r8 task 011): there is deliberately NO 412 branch here. Concurrency is now
        // LAST-WRITER-WINS with a warning — the server never refuses a save because the stored version
        // moved, so a save-path 412 is unreachable. The concurrent-writer case arrives on the SUCCESS
        // path instead, as a `concurrent-external-change` degradation warning naming version history as
        // the recovery. Task 010 routed 412 here transitionally; this task removed the reason.
        // Re-adding a 412 branch would mean a refusal loop came back — check the server first.
        dispatch({ kind: 'saveFailed', errorMessage: saveFailureMessage(failure) });
      } finally {
        // FR-S05 (r8 task 012): both cleanups belong here and nowhere else. The `saving` status is
        // terminated by the dispatches above (`saveSucceeded` / `saveFailed` both return the reducer
        // to `'loaded'`) — every path through the try and the catch performs one, including the
        // outcome-honesty gate's early return, so the editor can no longer be stranded mid-save.
        window.clearTimeout(saveTimeoutId);
        finishSaveAttempt();
      }
    },
    [
      state.status,
      state.documentRef,
      state.docxBytes,
      state.sessionId,
      // task 012 (r6): the model-path probe reads the retained canonical model, the origin marker
      // (trackChanges), and the load-time versionId (the model shape's baseline fallback).
      state.loadedContentModel,
      // task 013 (r6, F7): folded into the model-path save's degradation-warning dispatch.
      state.loadedContentModelWarnings,
      state.origin,
      state.versionId,
      // Task 041 review B-LOW-1: the PDF create-on-save routing reads this — listed explicitly
      // (previously masked by documentRef replacing on every sourceFormat transition).
      state.sourceFormat,
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

  // -------------------------------------------------------------------------
  // FR-05 (task 032, spaarkeai-compose-r6) — "Apply firm template" (030 engine + 031 resolver,
  // wired end-to-end). POST /api/compose/documents/{speId}/apply-template merges the PERSISTED
  // bytes into the resolved firm/matter template's chrome server-side and persists a NEW SPE
  // version; on 200 this host surfaces the merge degradation warnings through the EXISTING
  // saveDegradationWarnings banner family and re-mounts from the server-authoritative merged
  // bytes via the EXISTING requestLoad remount (the same path reload-from-source / the external-
  // change refresh use — bytes + projection + contentModel + paraIdMap adopt atomically). The
  // affordance is GUARDED to a saved (non-dirty, non-transient) document — the server merges
  // persisted bytes, never unsaved editor state (see `applyTemplateDisabledReason` below).
  // -------------------------------------------------------------------------
  const [applyTemplateOpen, setApplyTemplateOpen] = React.useState(false);
  const [isApplyingTemplate, setIsApplyingTemplate] = React.useState(false);
  const [applyTemplateError, setApplyTemplateError] = React.useState<string | null>(null);

  // (hoisted above handleApplyTemplate — its apply-time dirty re-check reads this binding; 032 F3)
  const [isDirty, setIsDirty] = React.useState<boolean>(false);

  const handleApplyTemplate = React.useCallback(
    async (templateIdOrName: string): Promise<void> => {
      const speId = state.documentRef?.speDriveItemId;
      // 090 close-out review (HIGH): the doc's own drive wins (create-on-save re-target lands the
      // doc in the BU container's drive, not the host's) — mirrors triggerSave's saveDriveId.
      const applyDriveId = state.documentRef?.driveId ?? effectiveDriveId;
      if (!speId || !applyDriveId || !bffBaseUrl) return;
      // 032 Step-9.5 F3: re-check dirtiness at APPLY time, not just toolbar-render time — a
      // programmatic edit (Assistant redline via the bridge) landing while the dialog is open would
      // otherwise be silently discarded by the post-merge remount.
      if (isDirty) {
        setApplyTemplateError('The document has unsaved changes. Save first, then apply the template.');
        return;
      }
      setIsApplyingTemplate(true);
      setApplyTemplateError(null);
      try {
        // FR-12 (task 074): `authenticatedFetch` THROWS a typed `ApiError` (status + ProblemDetails) on
        // any non-2xx — it never RETURNS a non-ok Response (see authenticatedFetch.ts + the same note at
        // the memo/draft handlers below). So the old `if (!response.ok)` branch here was DEAD code; the
        // 404 (and every other) failure is now handled as a typed ApiError in the catch below (ADR-019).
        const response = await authenticatedFetch(
          `${bffBaseUrl}/api/compose/documents/${encodeURIComponent(speId)}/apply-template`,
          {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ driveId: applyDriveId, templateIdOrName }),
          }
        );

        const payload = (await response.json()) as {
          templateName?: string;
          versionId?: string;
          // The 030 engine's template-merge-* degradation warnings + the post-merge canonical
          // projection's flatten warnings — folded into the EXISTING save-degradation banner
          // family (loud, never silent — operator principle).
          mergeWarnings?: Array<{ code: string; count: number }> | null;
          contentModelWarnings?: Array<{ code: string; count: number }> | null;
        };

        const warnings = mergeDegradationWarnings(payload.mergeWarnings ?? [], payload.contentModelWarnings ?? []);
        setApplyTemplateOpen(false);

        // Re-mount from the server-authoritative merged bytes — the SAME requestLoad remount the
        // reload-from-source path uses. 032 Step-9.5 F2: the merge warnings ride INSIDE requestLoad
        // (carryDegradationWarnings) — a separate pre-dispatch would be wiped by the reducer's
        // INITIAL_STATE reset before ever painting. docxBridge.ts remains the docx↔editor
        // round-trip beneath the remount.
        if (state.documentRef) {
          dispatch({
            kind: 'requestLoad',
            documentRef: state.documentRef,
            sessionId: state.sessionId,
            carryDegradationWarnings: warnings.length > 0 ? warnings : null,
          });
        }
      } catch (err) {
        // FR-12 (task 074): the failure path is a TYPED ApiError (ADR-019 ProblemDetails), thrown by
        // authenticatedFetch — branch on `err.status` (404 = template not found) and surface the
        // server-side `detail`/`title`, replacing the dead response.ok idiom removed above. A non-ApiError
        // (e.g. a genuine network/parse throw) keeps the generic fallback.
        if (err instanceof ApiError) {
          const detail = err.problemDetails?.detail ?? err.problemDetails?.title ?? '';
          setApplyTemplateError(
            err.status === 404
              ? detail || `Template "${templateIdOrName}" was not found. Check the template name or ID.`
              : detail || `Failed to apply the template (HTTP ${err.status}).`
          );
        } else {
          const message = err instanceof Error ? err.message : String(err);
          setApplyTemplateError(`Failed to apply the template: ${message}`);
        }
      } finally {
        setIsApplyingTemplate(false);
      }
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [state.documentRef, state.sessionId, effectiveDriveId, bffBaseUrl, isDirty]
  );

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

  // -------------------------------------------------------------------------
  // FR-02 (task 030, UC-3): name-on-first-save / Save As modal
  // -------------------------------------------------------------------------
  // The name-capture modal state. Null = closed. `requestSave` (below) opens it for an EXPLICIT save
  // that needs a name; its onSubmit re-enters triggerSave with the entered name (threaded into the
  // create-on-save displayName → server ResolveFileName + sprk_documentname).
  const [saveNameModal, setSaveNameModal] = React.useState<{
    mode: 'first-save' | 'save-as';
    defaultName: string;
  } | null>(null);

  // Does this save need a name first? Save As ALWAYS prompts (a deliberate fork the user names); a
  // normal Save prompts only on the FIRST create-on-save of a never-persisted, still-unnamed draft
  // (born-in-editor / blank / template). An imported .docx or PDF already carries a real name and an
  // already-persisted doc has an SPE id — neither prompts.
  const saveNeedsName = React.useCallback(
    (mode: ComposeSaveMode): boolean => {
      if (mode === 'new') return true;
      const ref = state.documentRef;
      if (!ref) return false;
      // UAT-03 (owner 2026-08-18): prompt for a name on the FIRST save of ANY new-to-system document,
      // not only born-in-editor "Untitled" drafts. A never-persisted doc (no speDriveItemId, no
      // sprkDocumentId) has no sprk_document row yet — this save CREATES it, so the user names it.
      // Previously an imported/uploaded file (which carries a real filename) skipped the prompt; FR-02's
      // intent is to prompt on every create-on-save. The modal is seeded with the current filename (see
      // requestSave) so the user confirms or renames rather than being blocked.
      return !ref.speDriveItemId && !ref.sprkDocumentId;
    },
    [state.documentRef]
  );

  // The EXPLICIT-save entry point (toolbar Save / Save As + Ctrl+S). Opens the name modal when a name
  // is required; otherwise saves directly. Background/best-effort paths (beforeunload flush, cross-pane
  // bridge) call triggerSave DIRECTLY — they cannot show UI, and triggerSave's auto-name fallback keeps
  // them from persisting the 'Untitled document.docx' placeholder.
  const requestSave = React.useCallback(
    (mode: ComposeSaveMode = 'version'): void => {
      if (saveNeedsName(mode)) {
        const current = state.documentRef?.fileName;
        // Seed the modal with the current filename whenever it is a real name (Save As, OR a first save of
        // an imported/uploaded file — UAT-03) so the user confirms/renames; a born-in-editor "Untitled"
        // draft starts blank.
        const defaultName = !isUntitledDraftName(current) ? (current ?? '') : '';
        setSaveNameModal({ mode: mode === 'new' ? 'save-as' : 'first-save', defaultName });
        return;
      }
      void triggerSave(mode);
    },
    [saveNeedsName, state.documentRef, triggerSave]
  );

  // Keyboard shortcut: Ctrl/Cmd+S → save (through the name-modal gate on a first/unnamed save).
  React.useEffect(() => {
    if (state.status !== 'loaded') return;
    const handler = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') {
        e.preventDefault();
        requestSave();
      }
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [state.status, requestSave]);

  // -------------------------------------------------------------------------
  // FR-04 draft-into-editor — render-follows-store materialization (task 016)
  // -------------------------------------------------------------------------
  // Materializes the drafted content into the editor FROM the stored ledger entry (ADR-040:
  // storage precedes rendering; the client re-reads the durable ledger, never a client buffer).
  // `targetLedgerRef` selects a specific stored output ({bindingId}@t{n}); when omitted, the
  // CURRENT (highest-turn) compose output for the session is materialized — the refresh-durable
  // and supersession/undo-replace resolution (FR-17 foundation).
  // r8 task 055 — the load-time reference map, read through a REF by `registerAiReviewComments`
  // below. A ref (rather than a dependency) keeps that callback's identity stable, which matters:
  // it feeds `materializeEditOutput`, which several materialize effects depend on, and a new
  // identity on every reload would re-arm them. The map only ever changes on a document load, and
  // flags are always registered after one.
  const paraIdMapRef = React.useRef<readonly ParaIdMapEntry[]>(state.paraIdMap);
  paraIdMapRef.current = state.paraIdMap;

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
  //
  // r8 task 055 (FR-C03) — THE DETERMINISTIC ANCHOR IS NOW PRODUCED HERE.
  //
  // `AnchoredAnnotationAnchor.paraId` shipped in R3 FR-11 as the documented PRIMARY anchor
  // ("Resolution order is paraId-FIRST, then the textPattern/paragraphHint fuzzy fallback"), and its
  // CONSUMER has been live ever since: the return-from-Word re-anchor path
  // (`anchoredAnnotationsToPriorAnchors` -> `AnnotationReanchorService`) resolves by paraId first and
  // only falls back to the fuzzy scorer when it is absent. This function was the missing PRODUCER —
  // it wrote `paragraphHint: -1` (the "no structural hint" sentinel) and no paraId, so every
  // `flag-risks` flag went through the fuzzy scorer even when the model had named its paragraph
  // exactly. Precedence comes from `resolveAnchorParaIds`, the SAME module the AI-edit path
  // (`usePendingRedline.resolveAnchoredSpans`) and the advisory-comment path
  // (`ComposeEditor.placeAdvisoryComments`) use, so the three cannot drift.
  //
  // Never fabricates: an anchor that does not resolve — an unknown citation, or a paraId and a
  // citation that disagree — leaves `paraId` unset and the flag keeps its prose fallback. That is
  // the honest outcome here, and it is NOT the UAT-21 "refuse" case: nothing is being PLACED in the
  // document at this moment, so there is no wrong position to land on. The anchor is a hint the
  // re-anchor service resolves later, and it degrades to exactly the pre-055 behaviour.
  const registerAiReviewComments = React.useCallback(
    (comments: readonly ComposeDraftComment[], provenance: { ledgerRef: string; bindingId: string }): void => {
      const flags = comments
        .map((c, i) => {
          // A bare paraId needs no map: it IS the address (same contract as the server
          // `ComposeAnchorResolver` and the edit path). A citation needs the load-time reference map.
          const resolution = resolveAnchorParaIds(
            { paraId: c?.target_para_id, ref: c?.target_ref },
            paraIdMapRef.current
          );
          // A RANGE citation names several paragraphs; an annotation anchor holds ONE, so the range's
          // FIRST clause is where the flag hangs (document order — `resolveCitation` sorts by index).
          const paraId = resolution.kind === 'resolved' ? resolution.paraIds[0] : undefined;
          return { i, target: c?.target_text?.trim() ?? '', body: c?.comment?.trim() ?? '', paraId };
        })
        // The gate is "somewhere to hang it AND something to say". Task 054 lets a flag carry a
        // deterministic anchor with weak or absent prose (L-1: hard breaks collapse in
        // `collectBlocks().text`, so a quoted excerpt may not exist verbatim), and the pre-055 gate
        // — `target.length > 0 && body.length > 0` — silently dropped exactly those BEST-anchored
        // flags. A flag with neither an anchor nor prose genuinely has nothing to attach to.
        .filter(c => c.body.length > 0 && (c.paraId !== undefined || c.target.length > 0));
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
            anchor: {
              textPattern: flag.target,
              paragraphHint: -1,
              spanId: provenance.ledgerRef,
              // Omitted rather than set to undefined, so a flag with no resolvable anchor serializes
              // to exactly the pre-055 shape on the FR-29 session-annotations write.
              ...(flag.paraId ? { paraId: flag.paraId } : {}),
            },
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
  // UAT (2026-08-18): mirror "a review ran on this doc" into a ref the (earlier-declared) save closure
  // reads to gate the first-save Analysis create.
  React.useEffect(() => {
    hasReviewFindingsRef.current = reviewSummaryFindings.length > 0;
  }, [reviewSummaryFindings]);
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
   * FR-S09 sweep (r8 task 016): translate a THROWN memo failure into FR-14's negative message, if it is
   * one — "session not bound to an Analysis" (promote first — the review is NOT lost) vs. "no memo
   * persisted yet" (generate first).
   *
   * This REPLACES `readMemoProblemCode`, which read the ProblemDetails `code` extension off a non-OK
   * `Response` — a shape `authenticatedFetch` never returns. Both of its call sites therefore sat
   * inside unreachable `if (!response.ok)` blocks, and BOTH of FR-14's negative messages were dead:
   * a user with no memo yet, and a user on the direct-Compose door, got the same generic failure. The
   * same `code` is already parsed onto `ApiError.problemDetails`, so no second body read is needed.
   *
   * Returns null for a genuine transport/server error, which keeps its own generic handling.
   */
  const memoNegativeFromError = React.useCallback(async (err: unknown): Promise<string | null> => {
    const status = (err as { status?: unknown } | null | undefined)?.status;
    if (typeof status !== 'number') return null;
    const details = (err as { problemDetails?: Record<string, unknown> | null } | null | undefined)?.problemDetails;
    const code = typeof details?.['code'] === 'string' ? (details['code'] as string) : null;
    return selectMemoNegativeMessage(status, code);
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
    let response: Response;
    try {
      response = await authenticatedFetch(
        `${bffBaseUrl}/api/ai/chat/sessions/${encodeURIComponent(state.sessionId)}/review-memo`,
        { method: 'GET' }
      );
    } catch (err) {
      // FR-S09 sweep (r8 task 016): the negative split happens on the THROWN ApiError. The
      // `if (response.ok)` / fallthrough that used to follow the call was unreachable.
      const negative = await memoNegativeFromError(err);
      if (negative) return { kind: 'negative', message: negative };
      throw err;
    }
    return { kind: 'ok', memo: (await response.json()) as ReviewMemoReadResponse };
  }, [bffBaseUrl, state.sessionId, memoNegativeFromError]);

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
      // FR-S09 sweep (r8 task 016): the `if (!response.ok)` split-negatives block that used to sit
      // here was unreachable. The split now happens in the catch, on the thrown ApiError.
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
      // Split negatives (agreements-r1 UAT round-1 #2): 404/no-memo → "generate first";
      // 400/session-not-bound → "promote to an Analysis first" (never a dead-end "Failed (400)").
      const negative = await memoNegativeFromError(err);
      setMemoActionMessage(negative ?? (err instanceof ApiError ? err.message : 'Could not generate the review memo.'));
    } finally {
      setMemoActionInFlight(false);
    }
  }, [bffBaseUrl, state.sessionId, memoActionInFlight, memoNegativeFromError]);

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
        // FR-S09 sweep (r8 task 016): the "defensive" guard that stood here is DELETED. It could not
        // execute — its own comment said so — and a safety net that cannot deploy is not a safety net,
        // it is a second description of the contract that can silently drift from the first. The catch
        // below already routes the 404 ("nothing drafted yet") as a silent no-op.

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

  /**
   * FR-C05 (r8 task 052) — the DURABLE half of the stale-target resolution.
   *
   * REUSE, NOT A NEW CARRIER (root §11 / assessment §4.4 O-3). This is the SHIPPED FR-17 supersession
   * seam — the same `POST /api/ai/chat/sessions/{id}/compose-outputs/supersede` endpoint
   * (`ChatEndpoints.SupersedeComposeOutputAsync`) that "undo that" / "try another approach" already
   * write through, with the same body and the same append-only ledger semantics (O-4: a NEW superseding
   * entry referencing `supersedesRef`; the original compose entry is never mutated or deleted).
   *
   * It is called from here rather than from `useEditSupersession` because that hook lives in the
   * SpaarkeAi Assistant pane (`src/solutions/SpaarkeAi/src/components/conversation/`), which DEPENDS on
   * this package — importing it here would invert the dependency. Same seam, second call site; not a
   * third carrier.
   *
   * WHY THIS SATISFIES O-2/O-5. Once the entry is superseded it is no longer the head, so the
   * untargeted reopen pass in {@link materializeComposeDraftFromLedger} does not re-materialize it and
   * the question cannot be asked a second time about a decision already made. `React.useState` and
   * `sessionStorage` would BOTH fail that test — `lastMaterializedKey` is the demonstrated
   * counter-example (assessment §4.3).
   *
   * Returns false on any failure, so the caller can be honest rather than silently pretend it stuck.
   */
  const supersedeComposeOutput = React.useCallback(
    async (supersedesRef: string): Promise<boolean> => {
      if (!bffBaseUrl || !state.sessionId || !supersedesRef) return false;
      try {
        const url = `${bffBaseUrl}/api/ai/chat/sessions/${encodeURIComponent(
          state.sessionId
        )}/compose-outputs/supersede`;
        const response = await authenticatedFetch(url, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ supersedesRef }),
        });
        const data = (await response.json()) as { key?: string; outcome?: string };
        return typeof data?.key === 'string';
      } catch {
        return false;
      }
    },
    [bffBaseUrl, state.sessionId]
  );

  /**
   * FR-C05 — the user's answer to "this clause changed since the suggestion — apply anyway?".
   *
   * BOTH answers write the supersession: whichever way the user went, this proposal has been CONSUMED
   * and must not be replayed. "Apply anyway" leaves the redline pending in the document (accept/reject
   * still apply normally); "skip" leaves the document untouched. Neither is an ADR-041 Gate — no
   * `PendingPlanManager`, no `SessionGate`, no `gateId` (assessment §4.2 / O-1).
   */
  const resolveRedlineStaleTarget = React.useCallback(
    async (answer: 'apply' | 'skip'): Promise<void> => {
      const target = redlineStaleTarget;
      if (!target || staleResolutionBusy) return;
      setStaleResolutionBusy(true);
      try {
        if (answer === 'apply') {
          editorRef.current?.applyStaleRedlineAnyway();
        } else {
          editorRef.current?.dismissStaleRedline();
        }
        const recorded = await supersedeComposeOutput(target.ledgerRef);
        if (!recorded) {
          // Honest, not silent (R7 charter): the in-editor outcome IS what the user asked for, but the
          // durable record did not land, so the question can legitimately return after a refresh.
          setComposeDraftError(
            'Your choice was applied to this document, but it could not be recorded — you may be asked ' +
              'about this clause again after a refresh.'
          );
        }
      } finally {
        setStaleResolutionBusy(false);
      }
    },
    [redlineStaleTarget, staleResolutionBusy, supersedeComposeOutput]
  );

  /**
   * FR-C06 — the user's answer to "we think this replayed suggestion belongs here — is that right?".
   *
   * Structurally identical to {@link resolveRedlineStaleTarget}, and deliberately so: BOTH answers
   * write the supersession, because either way this proposal has been CONSUMED and must not be
   * replayed (O-4 append-only, keyed by the edit's ledger key; O-5 the reopen pass finds it superseded
   * and does not ask again). "Place it" leaves a normal pending redline the user can still
   * accept/reject; "Skip" leaves the document untouched. Neither is an ADR-041 Gate (assessment §4.2 /
   * O-1) — the Action already ran, there is nothing to suspend, and the trigger is a runtime document
   * fact no catalog datum can declare.
   */
  const resolveRedlineLegacyProposal = React.useCallback(
    async (answer: 'place' | 'skip'): Promise<void> => {
      const proposal = redlineLegacyProposal;
      if (!proposal || proposalResolutionBusy) return;
      setProposalResolutionBusy(true);
      try {
        if (answer === 'place') {
          editorRef.current?.applyLegacyRedlineProposal();
        } else {
          editorRef.current?.dismissLegacyRedlineProposal();
        }
        const recorded = await supersedeComposeOutput(proposal.ledgerRef);
        if (!recorded) {
          // Honest, not silent (R7 charter): the in-editor outcome IS what the user asked for, but the
          // durable record did not land, so the question can legitimately return after a refresh.
          setComposeDraftError(
            'Your choice was applied to this document, but it could not be recorded — you may be asked ' +
              'about this suggestion again after a refresh.'
          );
        }
      } finally {
        setProposalResolutionBusy(false);
      }
    },
    [redlineLegacyProposal, proposalResolutionBusy, supersedeComposeOutput]
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
      // FR-07(c) (task 011): never stage a legacy assistant-insert with an empty dedup identity.
      // Inherit the currently-mounted document's ref (it already carries the task-010 composeLogicalId
      // after task 010's mint doors); when NOTHING is mounted, mint+persist a logical id so a
      // create-on-save from this staged insert still coalesces onto ONE identity instead of the
      // id-less `{ speDriveItemId: '' }` sentinel that historically skipped dedup.
      const fallbackRef: ComposeDocumentRef = state.documentRef ?? {
        speDriveItemId: '',
        composeLogicalId: startNewComposeLogicalId(),
      };
      dispatch({ kind: 'pendingAssistantInsert', payload: toAssistantInsertPayload(event, fallbackRef) });
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

  // Task 054 (FR-C03) — publish the editor's LIVE annotated document text + closed paraId set into the
  // bridge, so the Assistant's whole-document revise dispatch can send the model a set of identifiers
  // it can copy instead of asking it to quote prose back. Read on demand at dispatch time (never
  // cached) so it describes the document as it is NOW, including paragraphs typed since load.
  //
  // Same single-writer multi-tab gate as the Accept slot above, and for a sharper reason: supplying an
  // INACTIVE tab's document would hand the model identifiers from one document while the redline is
  // placed into another. Returning null degrades to the pre-054 dispatch (no annotated text, no closed
  // set) rather than to a wrong one.
  const handleReadAnchoredDocumentText = React.useCallback((): {
    text: string;
    paraIds: readonly string[];
  } | null => {
    if (isActiveTabRef.current === false) return null;
    const anchored = editorRef.current?.getAnchoredDocumentText?.();
    return anchored && anchored.paraIds.length > 0 ? { text: anchored.text, paraIds: anchored.paraIds } : null;
  }, []);
  useRegisterComposeAnchoredDocumentTextProvider(handleReadAnchoredDocumentText);

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
  const handleDirtyChange = React.useCallback((dirty: boolean): void => {
    setIsDirty(dirty);
  }, []);

  // -------------------------------------------------------------------------
  // DI-02 (spaarkeai-assistant-enhancements-r2, notes/defer-issues.md) — flush
  // unsaved compose work on unmount.
  // -------------------------------------------------------------------------
  // Investigation finding: EVERY path that removes this compose tab —
  // `WorkspaceTabManager.closeTab` (manual X), `clearAllTabs` (a History switch,
  // spaarkeai-assistant-enhancements-r2 task 035; or an exclusive-playbook reset) —
  // unmounts this component with NO dirty-check/flush gate anywhere in
  // `WorkspaceTabManager` (verified: both methods unconditionally filter the tab
  // out of the list; neither reads editor dirty state). The SERVER-save path stays
  // deliberately narrow: `triggerSave` fires ONLY on an explicit Ctrl+S, the toolbar
  // Save button, or the cross-pane "Add to DMS" bridge chip
  // (`useRegisterComposeSaveHandler` above) — plus this best-effort flush-on-unmount.
  // FR-03 (tasks 040/041) added a CLIENT-ONLY draft autosave (a ~15s dirty-only
  // localStorage snapshot) + a `beforeunload` guard, but that path NEVER calls
  // `triggerSave` and never creates an SPE version (NFR-03) — so this flush-on-unmount
  // remains the safety net for the un-persisted SERVER save. So a compose tab
  // closed via ANY of those paths while dirty would (without this flush) drop every
  // keystroke typed since the last explicit Save. The compose DOCUMENT itself is durable
  // server-side (ADR-049 — OOXML byte-store; TipTap is a lossy view) — only the
  // un-flushed in-memory delta is at risk.
  //
  // Fix: best-effort flush through the SAME `triggerSave` path a manual Save uses
  // (no new persistence mechanism — CLAUDE.md §11) when the tab unmounts while
  // dirty OR holding an un-persisted transient (create-on-save) draft. Chosen over
  // a flush call sited in WorkspacePane's History-switch handler because it is the
  // single choke point EVERY close path already funnels through (a WorkspacePane-
  // local fix would leave the manual-close and exclusive-playbook paths unflushed).
  //
  // `hasUnsavedWorkRef` mirrors the live "is there unsaved work" signal on every
  // render (the same ref-mirror convention `triggerSaveRef`/
  // `notifyComposeSaveCompletedRef` below use) so the unmount effect's cleanup —
  // registered once, deps `[]` — reads the CURRENT value instead of a stale
  // closure. `triggerSave` never rejects (it internally try/catches network errors
  // into a `saveFailed` dispatch), so no `.catch()` is needed; a dispatch that
  // lands after unmount is a documented React no-op, not a crash. Mirrors
  // `hasTransientDraft`'s condition below (kept LOCAL here — `hasTransientDraft`
  // itself is computed later in render order, so duplicating the two-line
  // predicate is cheaper than reordering a widely-referenced derived const).
  const hasUnsavedWorkRef = React.useRef(false);
  hasUnsavedWorkRef.current =
    state.status === 'loaded' && !!state.documentRef && (isDirty || !state.documentRef.speDriveItemId);

  // `useLayoutEffect`, NOT `useEffect`, is load-bearing here (verified empirically, not just in
  // theory): React detaches a forwardRef child's `ref.current` (here, `editorRef.current`, the
  // `ComposeEditorHandle`) during the SAME synchronous commit pass as `useLayoutEffect` cleanups —
  // but strictly BEFORE the separate, later passive-effect pass that runs `useEffect` cleanups. A
  // first implementation of this fix using `useEffect` measurably saw `editorRef.current === null`
  // by the time its cleanup ran (the child's ref was already detached), so `triggerSave`'s internal
  // `if (!editorRef.current) return;` guard silently no-opped — the flush never fired. Swapping to
  // `useLayoutEffect` fixed it: `editorRef.current` is still the live handle at cleanup time.
  React.useLayoutEffect(() => {
    return () => {
      if (hasUnsavedWorkRef.current) {
        void triggerSaveRef.current();
      }
    };
    // Intentionally fire-once (mount/unmount only) — reads the latest state via
    // `hasUnsavedWorkRef`/`triggerSaveRef`, not via this effect's own deps.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // FR-03 (task 041): warn before the BROWSER unloads (tab close / navigation / reload) while there is
  // unsaved work. Reads the same `hasUnsavedWorkRef` mirror the flush-on-unmount uses. The in-app
  // tab-close / History-switch path is already covered by the flush-on-unmount effect above (best-effort
  // `triggerSave`) plus the task-040 local draft; this guard covers the one path a React unmount cannot —
  // a real browser unload. Standard `preventDefault()` + `returnValue` contract; a clean/saved doc never
  // warns (the guard reads the live ref, so it never fires spuriously).
  React.useEffect(() => {
    const onBeforeUnload = (e: BeforeUnloadEvent): void => {
      if (!hasUnsavedWorkRef.current) return;
      e.preventDefault();
      // Legacy Chrome/Firefox still require a non-empty `returnValue` to show the native prompt.
      e.returnValue = '';
    };
    window.addEventListener('beforeunload', onBeforeUnload);
    return () => window.removeEventListener('beforeunload', onBeforeUnload);
  }, []);

  // -------------------------------------------------------------------------
  // FR-03 (task 040) — draft-safe autosave (CLIENT-ONLY local draft store)
  // -------------------------------------------------------------------------
  // A dirty-only ~15s tick snapshots the editor HTML to localStorage, keyed by the task-010 logical
  // id, so a crash / tab-close / navigation never loses unsaved work. NFR-03 (the task-040 escalation
  // trigger): this path is fully separate from the server save — it calls ONLY `saveComposeDraft`
  // (localStorage), never `authenticatedFetch`, so it can never create an SPE version per tick. The
  // SPE version is appended EXCLUSIVELY by an explicit Save (`triggerSave`).
  //
  // `draftAutosaveMirrorRef` mirrors the live inputs on every render (the same ref-mirror convention
  // as `hasUnsavedWorkRef`/`triggerSaveRef`) so the interval — registered once, deps `[state.status]`
  // — reads CURRENT values (Auto Save toggle, logical id, file name) instead of a stale closure, and
  // never needs to re-subscribe on each edit.
  const draftAutosaveMirrorRef = React.useRef<{
    enabled: boolean;
    logicalId: string | undefined;
    fileName: string | undefined;
  }>({ enabled: true, logicalId: undefined, fileName: undefined });
  draftAutosaveMirrorRef.current = {
    enabled: autoSaveEnabled,
    logicalId: getComposeLogicalIdentity(state.documentRef),
    fileName: state.documentRef?.fileName,
  };

  React.useEffect(() => {
    if (state.status !== 'loaded') return;
    const intervalId = window.setInterval(() => {
      const { enabled, logicalId, fileName } = draftAutosaveMirrorRef.current;
      // Auto Save off (task 020 toggle) OR no stable id yet → nothing to persist.
      if (!enabled || !logicalId) return;
      const handle = editorRef.current;
      // Dirty-only: `isDirty()` is the editor's OWN authoritative flag (dirtyRef) — read fresh.
      if (!handle || !handle.isDirty()) return;
      const html = handle.getDraftHtml?.();
      if (typeof html !== 'string') return;
      // CLIENT-ONLY write — localStorage, never the BFF (NFR-03).
      saveComposeDraft(logicalId, html, fileName);
    }, COMPOSE_DRAFT_AUTOSAVE_INTERVAL_MS);
    return () => window.clearInterval(intervalId);
    // Re-arm only when the loaded/unloaded status flips; live inputs ride the mirror ref.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [state.status]);

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
          let projectContentModel: ComposeContentModel | null = null;
          let projectContentModelWarnings: Array<{ code: string; count: number }> | null = null;
          // Task 051 (FR-06): the PDF-source marker from the /project response (task 050). 'pdf' → the
          // retainedBytes below are a server-synthesized docx the editor must admit as editable.
          let projectSourceFormat: 'pdf' | null = null;
          // task 012 (r6): the retained mount bytes. Default = the local file bytes; REPLACED by the
          // server's `content` byte echo when present — `/project` returns it ONLY when server-side
          // paraId minting mutated the caller's bytes, and adopting the echo keeps editor/model/
          // carrier in ONE paraId universe (the minted ids exist in all three).
          let retainedBytes: ArrayBuffer = result;
          if (bffBaseUrl) {
            try {
              const response = await authenticatedFetch(`${bffBaseUrl}/api/compose/project`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ content: arrayBufferToBase64(result), fileName: file.name }),
              });
              // FR-S09 sweep (r8 task 016): necessarily TRUE — a non-2xx threw into the catch below,
              // which already falls through to `mountTransient` with `projection: null`.
              {
                const payload = (await response.json()) as {
                  projection?: RawComposeProjectionPayload;
                  // task 012 (r6): canonical model + optional minted-byte echo (commit 70be80006).
                  contentModel?: ComposeContentModel | null;
                  content?: string | null;
                  // task 013 (r6, F7): the projection's flatten warnings.
                  contentModelWarnings?: Array<{ code: string; count: number }> | null;
                  // Task 051 (FR-06 — PDF import parity): 'pdf' when the browsed file was a PDF and
                  // `content` is the docx SYNTHESIZED by the task-050 mount fork. Parsed defensively.
                  sourceFormat?: string | null;
                };
                projection = normalizeProjection(payload.projection);
                projectContentModel = payload.contentModel ?? null;
                projectContentModelWarnings = Array.isArray(payload.contentModelWarnings)
                  ? payload.contentModelWarnings
                  : null;
                projectSourceFormat = payload.sourceFormat === 'pdf' ? 'pdf' : null;
                if (typeof payload.content === 'string' && payload.content.length > 0) {
                  retainedBytes = base64ToArrayBuffer(payload.content);
                }
              }
            } catch {
              // Network/parse failure — fall through with projection: null. The MOUNT itself still
              // proceeds (dispatch below), but per task 013 (F-2) the editor renders the explicit
              // error/unavailable state rather than a degraded render — there is no fallback reader.
              projection = null;
              projectContentModel = null;
              projectContentModelWarnings = null;
              projectSourceFormat = null;
              retainedBytes = result;
            }
          }

          // FR-05 (task 100): carry the host-resolved BU container so the first Save (create-on-save)
          // knows which SPE container to mint the new sprk_document's drive-item in.
          dispatch({
            kind: 'mountTransient',
            docxBytes: retainedBytes,
            fileName: file.name,
            containerId: containerIdRef.current,
            sessionId: browseDocumentSessionId,
            projection,
            // task 012 (r6): retain the canonical model atomically with the projection.
            contentModel: projectContentModel,
            // task 013 (r6, F7): the projection's flatten warnings — same lifecycle as the model.
            contentModelWarnings: projectContentModelWarnings,
            // Task 051 (FR-06 — PDF import parity): carry the PDF-source marker so the editor admits the
            // synthesized docx as editable (despite the .pdf display name) and Save routes create-on-save.
            sourceFormat: projectSourceFormat,
            // G7 (task 022): mint the transient dedup key once for this Browse mount → every create-on-save
            // sends it so repeated saves target ONE record (no duplicate mint).
            transientKey: mintTransientKey(),
            // FR-07(b) (task 010): mint+persist the non-rotating logical id for this new Browse document.
            composeLogicalId: startNewComposeLogicalId(),
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
          // task 012 (r6): register the RETAINED bytes (the server's minted-id echo when present) so
          // the chat-session copy shares the mount's paraId universe.
          void registerActiveDocumentRef.current?.({
            docxBytes: retainedBytes,
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
      // FR-07(b) (task 010): mint+persist the non-rotating logical id for this born-in-editor document.
      composeLogicalId: startNewComposeLogicalId(),
    });
    // A freshly-created born-in-editor doc is unsaved by definition — enable Save (create-on-save).
    setIsDirty(true);
  }, []);

  const handleBlankRequested = React.useCallback((): void => {
    mountBornInEditor('<p></p>', UNTITLED_DOC_NAME);
  }, [mountBornInEditor]);

  const handleTemplateRequested = React.useCallback((): void => {
    mountBornInEditor(COMPOSE_BLANK_TEMPLATE_HTML, UNTITLED_DOC_NAME);
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
          // task 012 (r6): the canonical ComposeContentModel (additive on the upload response since
          // commit 70be80006). Undefined/null → null → op-log fallback save shape.
          contentModel?: ComposeContentModel | null;
          // task 013 (r6, F7): the projection's flatten warnings.
          contentModelWarnings?: Array<{ code: string; count: number }> | null;
          // Task 051 (FR-06 — PDF import parity): 'pdf' when the uploaded file was a PDF and `content` is
          // the docx SYNTHESIZED by the task-050 mount fork. Parsed defensively (older BFF omits it).
          sourceFormat?: string | null;
          // FR-S08 (r8 task 015): the server-advertised save size limit, in bytes. Optional — an
          // older BFF omits it, which the reader below normalizes to null (no numeric pre-flight).
          maxDocumentBytes?: number | null;
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
          // task 012 (r6): retain the canonical model atomically with the projection (same response).
          contentModel: payload.contentModel ?? null,
          // task 013 (r6, F7): the projection's flatten warnings — same lifecycle as the model.
          contentModelWarnings: Array.isArray(payload.contentModelWarnings) ? payload.contentModelWarnings : null,
          // FR-S08 (r8 task 015): the server-advertised save size limit. Read defensively — an older
          // BFF omits it, and `null` there means "do no numeric pre-flight", never "unlimited".
          maxDocumentBytes: typeof payload.maxDocumentBytes === 'number' ? payload.maxDocumentBytes : null,
          // Task 051 (FR-06 — PDF import parity): carry the PDF-source marker so the editor admits the
          // synthesized docx as editable (despite the .pdf display name) and Save routes create-on-save.
          sourceFormat: payload.sourceFormat === 'pdf' ? 'pdf' : null,
          // G7 (task 022): transient dedup key for this assistant-upload mount.
          transientKey: mintTransientKey(),
          // FR-07(b) (task 010): mint+persist the non-rotating logical id for this uploaded document.
          composeLogicalId: startNewComposeLogicalId(),
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
        // FR-S09 sweep (r8 task 016): the 404 copy — "the session may have expired, re-upload it in
        // the Assistant" — used to live in an `if (!response.ok)` block above the parse, and could not
        // execute. An expired session rendered `Failed to open the uploaded file: HTTP 404`, which
        // tells the user nothing about the one action that fixes it.
        const status = (err as { status?: unknown } | null | undefined)?.status;
        const httpStatus = typeof status === 'number' && status >= 100 && status <= 599 ? status : null;
        const message = err instanceof Error ? err.message : String(err);
        dispatch({
          kind: 'loadFailed',
          errorMessage:
            httpStatus === 404
              ? 'The uploaded file is no longer available (the session may have expired). Re-upload it in the Assistant and try again.'
              : httpStatus !== null
                ? `Failed to open the uploaded file (HTTP ${httpStatus}).`
                : `Failed to open the uploaded file: ${message}`,
        });
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
        // FR-07(b) (task 010): mint+persist the non-rotating logical id for this inline draft document.
        composeLogicalId: startNewComposeLogicalId(),
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
        // FR-S09 sweep (r8 task 016): the unreachable "defensive" guard is DELETED — the catch below
        // already carries the 404 copy, and two descriptions of one contract drift apart.

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
          // FR-07(b) (task 010): mint+persist the non-rotating logical id for this ledger-resolved draft.
          composeLogicalId: startNewComposeLogicalId(),
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
  // FR-03 (task 040) — recover a CLIENT-ONLY local draft on reopen/crash
  // -------------------------------------------------------------------------
  // When the workspace opens with NO real mount door (no stored-doc / upload / draft-seed prop) and a
  // prior session left a persisted active draft, re-seed that draft into the editor via the SAME
  // `mountDraftHtml` born-in-editor path the blank/template/AI-draft mounts use — reusing the
  // RECOVERED logical id (never minting a fresh one) so identity + dedup stay stable across the reload.
  //
  // Scope kept minimal + non-destructive for task 040: recovery runs ONLY when no server document is
  // being mounted (the `initial*Ref` guard), so it can never clobber a loaded server doc. The
  // recover-vs-server-content PROMPT + save-state indicator are task 041. Fire-once on mount.
  React.useEffect(() => {
    if (state.status !== 'empty' && state.status !== 'error') return;
    // A real mount door (stored-doc / upload / draft-seed) owns the mount — defer to it, never recover.
    if (initialDocumentRef || initialUploadRef || initialDraftRef) return;
    const recoveredId = recoverActiveComposeLogicalId();
    if (!recoveredId) return;
    const draft = getComposeDraft(recoveredId);
    if (!draft || draft.html.length === 0) return;
    setSearchResolvedDriveId(null);
    dispatch({
      kind: 'mountDraftHtml',
      html: draft.html,
      fileName: draft.fileName,
      containerId: containerIdRef.current,
      sessionId: mintDocumentSessionId(),
      transientKey: mintTransientKey(),
      // Reuse the RECOVERED logical id so the rehydrated draft keeps its identity (do NOT mint fresh).
      composeLogicalId: recoveredId,
    });
    // A recovered draft is unsaved by definition — mark dirty so Save (create-on-save) is enabled.
    setIsDirty(true);
    // Fire-once on mount; the prop-guard (not deps) enforces "real mount doors win".
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

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
  // UAT-04 (2026-08-18, owner-requested): the user needs to know when Compose is performing an
  // operation — surface a progress indicator in the workspace's top notification area (NOT the
  // Assistant). Aggregate the existing per-action busy flags into ONE active-operation label; the
  // indicator renders above the banner stack whenever any operation is in flight. Save takes priority
  // (most frequent), then template apply, memo, Word-open, profile refresh.
  const activeOperationLabel: string | null = isSavingNow
    ? 'Saving…'
    : isApplyingTemplate
      ? 'Applying template…'
      : memoActionInFlight
        ? 'Creating summary memo…'
        : isWordActing
          ? 'Opening in Word…'
          : isRefreshingProfile
            ? 'Refreshing…'
            : null;
  const hasWordDocument = toolbarDocumentId.length > 0 && bffBaseUrl.length > 0;
  // Task 041 review B-MEDIUM-1: a PDF-sourced mount must NOT open in Word — the persisted item is
  // the .pdf (Word can't edit it), and the C3 "id stable across the flush" invariant breaks: the
  // flush IS a create-on-save that re-targets documentRef, so the pre-flush closure would open the
  // OLD PDF record while the just-flushed edits live in the new docx. Save first (which clears
  // sourceFormat and re-targets), then Word actions re-enable against the new docx identity.
  const isPdfSourced = state.sourceFormat === 'pdf';
  const wordActionsDisabled = isSavingNow || !hasWordDocument || isWordActing || isPdfSourced;
  // FR-S09 item 3 (r8 task 016): `tenantId` joins the precondition set.
  //
  // It was already required by `triggerSave` (which refuses without it) and by every save request
  // body — but not by the gate, so the Save button sat there ENABLED on a workspace that could not
  // possibly save, and only said so after the user pressed it. A precondition enforced one layer
  // deeper than it is advertised is a precondition the user discovers by failing.
  const hasSaveConfiguration = bffBaseUrl.length > 0 && !!tenantId;
  const hasUnsavedWorkToSave = isDirty || hasTransientDraft;
  const canSaveNow = !isSavingNow && hasSaveConfiguration && hasUnsavedWorkToSave;
  // ...and the disabled button explains itself rather than just being grey.
  const saveDisabledReason = isSavingNow
    ? undefined // the toolbar already renders "Saving…" for this case
    : !hasSaveConfiguration
      ? 'Saving is unavailable — this workspace is missing its Spaarke connection settings. Reload the page, and contact an administrator if it persists.'
      : !hasUnsavedWorkToSave
        ? 'No unsaved changes'
        : undefined;

  // FR-05 (task 032): "Apply firm template" gating. The button renders only for a PERSISTED doc
  // (an SPE drive-item exists — the server merges the SAVED bytes; a transient draft has nothing
  // persisted to merge onto). It is DISABLED-with-tooltip (not hidden) while there is unsaved work
  // or a save/apply in flight, so the user learns WHY instead of the affordance vanishing.
  const canShowApplyTemplate =
    (state.status === 'loaded' || state.status === 'saving') &&
    !!state.documentRef?.speDriveItemId &&
    // 090 close-out review (HIGH): a doc this workspace minted carries its own driveId (create-on-
    // save re-target) even when the host prop is empty (bare mount) — gate on either, not host-only.
    !!(state.documentRef?.driveId ?? effectiveDriveId) &&
    bffBaseUrl.length > 0;
  const applyTemplateDisabledReason = isApplyingTemplate
    ? 'Applying template…'
    : isSavingNow
      ? 'Saving…'
      : // Task 041 review B-MEDIUM-2: the persisted item behind a PDF-sourced mount IS the .pdf —
        // the server-side merge would receive %PDF- bytes (the server also refuses with a typed
        // 422). Disabled-with-reason, mirroring the server's honest copy.
        isPdfSourced
        ? 'Save as a Word document first (a PDF opened in Compose saves as a new Word document), then apply the template'
        : isDirty || hasTransientDraft
          ? 'Save your changes first — the firm template is applied to the saved document'
          : undefined;

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
        // Task 051 (spaarkeai-compose-r7, FR-06 — PDF import parity): admit .pdf at the Browse intake door.
        // A picked PDF round-trips through POST /api/compose/project (the task-050 mount fork) → a
        // synthesized docx the editor mounts editable. An un-intakeable PDF (DI gate off / parse failure)
        // still degrades gracefully (projection null / reference-only) — admission ≠ guaranteed editable.
        accept=".docx,.pdf,application/vnd.openxmlformats-officedocument.wordprocessingml.document,application/pdf"
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

          {/* UAT-04 (owner-requested): top-area progress indicator so the user knows Compose is
              performing an operation (save / template / memo / Word-open / refresh). Lives in the
              workspace's own notification area, NOT the Assistant. `aria-live="polite"` announces the
              operation to assistive tech; `role="status"` marks it non-alarming. */}
          {activeOperationLabel ? (
            <div
              className={styles.operationIndicator}
              role="status"
              aria-live="polite"
              data-testid="compose-workspace-operation-indicator"
            >
              <Spinner size="extra-tiny" />
              <Text size={200}>{activeOperationLabel}</Text>
            </div>
          ) : null}

          {/* Banner stack — errors / warnings / checkout status / assistant pending */}
          <ComposeBannerStack
            errorMessage={state.errorMessage}
            // Task 041 (FR-06, PDF intake): the honest-lossiness notice while the mounted doc is
            // PDF-sourced; cleared by the reducer after the first successful save (new docx identity).
            pdfSourceNotice={state.sourceFormat === 'pdf'}
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
            // 026-F5 (task 012, r6): SAVE-time degradation warnings — a SEPARATE banner family that is
            // NOT gated by hideImportWarnings (that suppression covers only the load-time import-
            // fidelity banner). null (a clean save) clears the banner.
            saveDegradationWarnings={state.saveDegradationWarnings}
            pendingAssistantInsert={state.pendingAssistantInsert}
            saveSuccessToken={state.saveSuccessToken}
            // Prong 1 (task 055): when the last save could only anchor PART of the batch, show the honest
            // "N edits couldn't be saved — please redo them" warning (replaces the plain Saved ✓ bar).
            partialApply={state.partialApply}
            // UAT-12 (2026-08-18): honest "tracked changes/comments couldn't be read" banner when the
            // server annotation read failed — so a doc that may CONTAIN redlines/comments is never
            // presented as clean.
            annotationReadFailed={state.annotationReadFailed}
            // UAT (2026-08-18, owner): SAVE-driven persistence — while the document has no SPE identity
            // yet (never persisted), show the "not saved yet — Save to create" notice. `reviewRan`
            // tailors the copy (and matches when Save will also create the Analysis). Clears
            // automatically once create-on-save gives the document its speDriveItemId.
            unsavedDocumentNotice={
              (state.status === 'loaded' || state.status === 'saving') &&
              !!state.documentRef &&
              !state.documentRef.speDriveItemId
                ? { reviewRan: reviewSummaryFindings.length > 0 }
                : null
            }
            // UAT-13 (2026-08-18): when a create-on-save persisted the document but its parent-
            // association write failed, show the honest "saved but not filed under its matter" banner
            // with a Retry that re-runs the host association write (clears on success).
            associationWarning={state.associationWarning}
            onRetryAssociation={
              state.associationWarning && onCreateOnSaveComplete
                ? () => {
                    const docId = state.associationWarning?.documentRecordId;
                    if (!docId) return;
                    void (async () => {
                      try {
                        await onCreateOnSaveComplete(docId);
                        dispatch({ kind: 'associationWarning', documentRecordId: null }); // succeeded → clear
                      } catch (retryErr) {
                        // eslint-disable-next-line no-console
                        console.warn('[ComposeWorkspace] association retry failed (banner stays):', retryErr);
                      }
                    })();
                  }
                : undefined
            }
            // FIX #7a: the transient "Open preview" link was REMOVED from the Saved ✓ banner — the
            // persistent affordance now lives in the Assistant chat (a "Saved to the DMS" message
            // with "Open preview", posted via the save-completed conduit). The banner keeps its
            // success signal (saveSuccessToken) but no longer carries the preview link.
            // Task 032 (FR-16 128KB budget, Leg B) — an honest notice when a prior review's findings
            // could not be fully restored on reopen (never silent absence).
            reviewFindingsDegraded={reviewFindingsDegraded}
            // Banner consolidation (2026-08-19): the pending-redline anchor-failure notice (hoisted out
            // of ComposeEditor's below-toolbar bar) + the two former stray host MessageBars
            // (draft-error, memo-message) now render in THIS single rail with consistent styling.
            pendingRedlineError={pendingRedlineError}
            onClearRedlineError={() => editorRef.current?.clearRedlineError()}
            composeDraftError={composeDraftError}
            memoActionMessage={memoActionMessage}
          />

          {/* ai-advanced-capabilities-nda-r1 UAT round-5 #1 — the Review Summary panel MOVED from here
              INTO the editor's top region (ComposeEditor renders it below the toolbar, in-flow). The
              host still OWNS the review data (captured from the ledgered `compose_advisory_comments`
              event) + the open/toggle state, and threads it down via the `reviewSummary` prop; the
              editor renders the panel, derives each finding's location from the live doc, and wires
              navigation to its own cited-span primitive. */}

          {/* Banner consolidation (2026-08-19): the FR-04 draft-error and FR-14 "Create Summary Memo"
              notices moved INTO ComposeBannerStack above (passed as composeDraftError / memoActionMessage)
              so every passive Compose notice shares one rail + Fluent MessageBar styling. */}

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
              // Task 051 (FR-06 — PDF import parity): 'pdf' when the mounted docx was synthesized from a
              // PDF (any intake door). The editor admits it as editable despite the .pdf display name.
              sourceFormat={state.sourceFormat}
              bffBaseUrl={bffBaseUrl}
              sessionId={state.sessionId}
              // task 041 (FR-13): pass-through to getToolsForSurface via ComposeEditor's own
              // activeWorkType prop; ComposeEditor defaults to '*' when omitted.
              activeWorkType={activeWorkType}
              onDirtyChange={handleDirtyChange}
              onRedlineErrorChange={setPendingRedlineError}
              onRedlineStaleTargetChange={setRedlineStaleTarget}
              onRedlineLegacyProposalChange={setRedlineLegacyProposal}
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
              // 'new' fork) into the save path. FR-02 (task 030): route through requestSave so a first
              // create-on-save / Save As opens the name modal (UC-3) before persisting. Ctrl+S also
              // goes through requestSave; the cross-pane bridge stays on triggerSave() → 'version'.
              onSave={mode => {
                requestSave(mode ?? 'version');
              }}
              canSave={canSaveNow}
              // FR-S09 item 3 (r8 task 016): a disabled Save states its reason (tooltip).
              saveDisabledReason={saveDisabledReason}
              // G10 (task 040): the manual "Refresh Profile" button — only for a PROMOTED doc (there is a
              // sprk_document to re-profile). Undefined for a transient/unpromoted mount → the button hides.
              onRefreshProfile={state.documentRef?.sprkDocumentId ? () => void triggerRefreshProfile() : undefined}
              isRefreshingProfile={isRefreshingProfile}
              // FR-05 (task 032): "Apply firm template" — opens the template-select dialog. Wired
              // only for a persisted doc (SPE source exists); disabled-with-tooltip while
              // dirty/transient/saving (the server merges the PERSISTED bytes).
              onApplyTemplate={
                canShowApplyTemplate
                  ? () => {
                      setApplyTemplateError(null);
                      setApplyTemplateOpen(true);
                    }
                  : undefined
              }
              applyTemplateDisabledReason={applyTemplateDisabledReason}
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
              // FR-01/FR-03 (task 020): the Save-dropdown Auto Save toggle. Phase 4 (040/041) connects
              // this state to the draft-safe autosave behavior; here it just renders + toggles.
              autoSaveEnabled={autoSaveEnabled}
              onAutoSaveToggle={setAutoSaveEnabled}
              // FR-03 (task 041): drive the toolbar save-state indicator. Unsaved = a dirty edit OR an
              // unpersisted transient (create-on-save) draft — the same signal the Save button gates on.
              hasUnsavedEdits={isDirty || hasTransientDraft}
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

      {/* FR-05 (task 032) — "Apply firm template" dialog. Presentation lives in
          ComposeApplyTemplateDialog (FormModal preset — ADR-021/ADR-050); this host owns the
          POST + post-apply remount (handleApplyTemplate above). onClose no-ops while the apply
          is in flight (FormModal busy contract — never abandon a mid-flight merge silently). */}
      <ComposeApplyTemplateDialog
        open={applyTemplateOpen}
        isApplying={isApplyingTemplate}
        errorMessage={applyTemplateError}
        onApply={templateIdOrName => void handleApplyTemplate(templateIdOrName)}
        onClose={() => {
          if (isApplyingTemplate) return;
          setApplyTemplateOpen(false);
          setApplyTemplateError(null);
        }}
      />

      {/* FR-02 (task 030 / UC-3) — name-on-first-save / Save As modal (FormModal preset, ADR-050 /
          ADR-021). Opened by requestSave for the FIRST create-on-save of an unnamed draft and for
          every Save As; onSubmit re-enters triggerSave with the entered name, which the create-on-save
          threads into displayName (→ server ResolveFileName + sprk_documentname). Removes the silent
          'Untitled document.docx' fallback. */}
      <ComposeSaveNameDialog
        open={saveNameModal !== null}
        mode={saveNameModal?.mode ?? 'first-save'}
        defaultName={saveNameModal?.defaultName ?? ''}
        onClose={() => {
          // FR-S09 item 2 (r8 task 016): the name modal is a HARD GATE on the save the user just
          // asked for — dismissing it (Cancel / Esc / backdrop) means that save does not happen.
          // It used to close in silence, leaving someone who pressed Ctrl+S and then Esc believing
          // their document was saved. The document stays dirty either way; now it also says so.
          setSaveNameModal(null);
          dispatch({
            kind: 'saveFailed',
            errorMessage: 'Not saved — this document needs a name. Press Save and enter one.',
          });
        }}
        onSubmit={name => {
          const mode: ComposeSaveMode = saveNameModal?.mode === 'save-as' ? 'new' : 'version';
          setSaveNameModal(null);
          void triggerSave(mode, { displayNameOverride: name });
        }}
      />

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

      {/* FR-C05 (r8 task 052) — the stale-target question. NOT an ADR-041 Gate (task-050 assessment
          §4.2): the Action already ran, there is nothing to suspend, and the trigger is a runtime
          document fact no catalog datum can declare. It IS a real confirmation, so it uses the
          canonical ConfirmModal shell (ADR-050) with semantic tokens only (ADR-021) — no bespoke
          chrome. `dismiss="alert"` (the preset's default) is deliberate: this must be answered, not
          Esc'd away, because "nothing happened" is not one of the outcomes.

          NOTHING has been placed while this is open. Confirm = apply anyway; Cancel = skip this
          suggestion. Either way the resolution is written to the ledger (see
          resolveRedlineStaleTarget) so a refresh cannot re-ask. */}
      <ConfirmModal
        open={redlineStaleTarget !== null}
        busy={staleResolutionBusy}
        title="This clause changed since the suggestion"
        message={
          redlineStaleTarget === null ? (
            ''
          ) : (
            <>
              {redlineStaleTarget.staleCount > 1
                ? `${redlineStaleTarget.staleCount} of ${redlineStaleTarget.totalCount} suggested edits target clauses that have changed since the suggestion was made. Applying them will replace the newer wording.`
                : 'This clause has changed since the suggestion was made. Applying it will replace the newer wording.'}
              <br />
              <br />
              {'Now: “'}
              {truncateClause(redlineStaleTarget.currentText)}
              {'”'}
              <br />
              {'When suggested: “'}
              {truncateClause(redlineStaleTarget.proposedAgainst)}
              {'”'}
              <br />
              <br />
              Apply anyway?
            </>
          )
        }
        confirmLabel="Apply anyway"
        cancelLabel="Skip this suggestion"
        onConfirm={() => {
          void resolveRedlineStaleTarget('apply');
        }}
        onClose={() => {
          void resolveRedlineStaleTarget('skip');
        }}
      />

      {/* FR-C06 (r8 task 053) — the anchorless-replay PROPOSAL.

          WHEN THIS CAN OPEN AT ALL: only for a `compose` ledger entry written before task 052 retired
          `target_text` from the four compose EDIT Actions, replayed afterwards (reopen, refresh, or an
          undo/try-another re-render of an older turn). No newly-produced edit can reach it — the model
          is not asked for prose any more, and an edit that HAS an anchor is refused rather than
          searched. The population is real, bounded, and shrinks with session retention.

          NOTHING is in the document while this is open — that is the whole point. The bounded fallback
          found where that wording occurs today; it cannot know that is the clause the model meant, so
          the user sees what would be struck and decides. Confirm = place it as a normal pending
          redline (still accept/rejectable); Cancel = skip it. Either way the answer is written to the
          ledger (resolveRedlineLegacyProposal) so a refresh cannot re-ask.

          Rendered only when the stale question is closed: two ConfirmModals at once would stack, and
          `dismiss="alert"` means neither can be Esc'd past. They are answered one at a time.

          Canonical ConfirmModal shell (ADR-050), semantic tokens only (ADR-021), no bespoke chrome —
          same contract as its FR-C05 sibling above. NOT an ADR-041 Gate (assessment §4.2 / O-1). */}
      <ConfirmModal
        open={redlineStaleTarget === null && redlineLegacyProposal !== null}
        busy={proposalResolutionBusy}
        title="Where should this suggestion go?"
        message={
          redlineLegacyProposal === null ? (
            ''
          ) : (
            <>
              {redlineLegacyProposal.proposedCount > 1
                ? `${redlineLegacyProposal.proposedCount} of ${redlineLegacyProposal.totalCount} suggestions in this set came from an earlier session, before suggestions carried a paragraph reference. We found the wording they quoted — check the first one below before placing them.`
                : 'This suggestion came from an earlier session, before suggestions carried a paragraph reference. We found the wording it quoted, but we cannot confirm it is the clause that was meant.'}
              <br />
              <br />
              {'It would replace: “'}
              {truncateClause(redlineLegacyProposal.matchedText)}
              {'”'}
              <br />
              {'The suggestion quoted: “'}
              {truncateClause(redlineLegacyProposal.quotedTarget)}
              {'”'}
              <br />
              <br />
              Place the suggestion here?
            </>
          )
        }
        confirmLabel="Place it here"
        cancelLabel="Skip this suggestion"
        onConfirm={() => {
          void resolveRedlineLegacyProposal('place');
        }}
        onClose={() => {
          void resolveRedlineLegacyProposal('skip');
        }}
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
