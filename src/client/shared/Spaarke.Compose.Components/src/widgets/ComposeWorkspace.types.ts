/**
 * ComposeWorkspace.types.ts — shared types for the Compose workspace orchestrator
 * and its extracted hooks (`useComposeBroadcastChannel`, `useComposeCheckoutLifecycle`,
 * `useComposeHeartbeatGate`).
 *
 * Project: spaarkeai-compose-r1
 * Phase:   Phase 4 / Phase 5
 * Extracted: R2 refactor (ComposeWorkspace.tsx 1795 → ~400 LOC) — types lifted
 *            here so the hooks can import them without forming a circular
 *            dependency on ComposeWorkspace.tsx.
 *
 * Re-exported from ComposeWorkspace.tsx for backwards-compatible imports.
 *
 * @see src/solutions/SpaarkeAi/src/components/compose/ComposeWorkspace.tsx
 */

import type {
  ComposeAssistantToWorkspaceFlow,
  ComposeDocumentRef,
  ParaIdMapEntry,
  ImportedRevision,
  ImportedComment,
  ComposeServerProjection,
  ComposeDocumentOrigin,
  ComposeContentModel,
} from '../types/compose-contracts';

// ---------------------------------------------------------------------------
// Document-context state machine
// ---------------------------------------------------------------------------

/**
 * Reducer state. The discriminated `status` field is the source of truth for
 * which child renders.
 *
 *   - `'empty'`   → no document selected; render ComposeEmptyState
 *   - `'loading'` → fetching DOCX bytes from BFF; render spinner
 *   - `'loaded'`  → editor + toolbar; document bytes available
 *   - `'saving'`  → preserving editor + toolbar but disable actions
 *   - `'error'`   → render error MessageBar with retry affordance
 */
export type ComposeWorkspaceStatus = 'empty' | 'loading' | 'loaded' | 'saving' | 'error';

/**
 * SPE check-out lifecycle status (Task 050 / Spike #3 §9; Task 051 multi-tab UX).
 *
 *   - `'idle'`               → no checkout attempted yet (initial state, or pre-promotion)
 *   - `'skipped'`            → no `sprkDocumentId` present (Path B ephemeral)
 *   - `'probing'`            → GET /api/documents/{id}/checkout-status in flight
 *   - `'acquiring'`          → POST /api/documents/{id}/checkout in flight
 *   - `'acquired'`           → 200 OK; lock held by current user (own or idempotent re-checkout)
 *   - `'conflict'`           → 409; doc locked by ANOTHER user (cross-user only)
 *   - `'same-user-conflict'` → probe revealed THIS user holds the lock from
 *                              another session (CheckoutStatusInfo.IsCurrentUser === true).
 *                              User must resolve via the ComposeConflictDialog.
 *   - `'discarding'`         → POST /api/documents/{id}/discard in flight as the
 *                              "force-close" step before re-checkout.
 *   - `'failed'`             → non-2xx / network error (non-fatal — editor remains usable)
 *   - `'cancelled'`          → user clicked "Cancel — close this tab" in the conflict
 *                              dialog, OR another tab posted a `force-closed` message.
 */
export type ComposeCheckoutStatus =
  | 'idle'
  | 'skipped'
  | 'probing'
  | 'acquiring'
  | 'acquired'
  | 'conflict'
  | 'same-user-conflict'
  | 'discarding'
  | 'failed'
  | 'cancelled';

/**
 * Subset of the BFF `CheckoutUserInfo` response shape we surface in the UI.
 * Mirrors `src/server/api/Sprk.Bff.Api/Models/CheckoutModels.cs`.
 */
export interface ComposeCheckoutLockedByInfo {
  id: string;
  name: string;
  /** ISO timestamp of when the conflicting lock was acquired. */
  checkedOutAt: string | null;
}

/**
 * Prong 1 (task 055) — the client-side mirror of the BFF `partialApply` save-response summary. Counts
 * only; the per-op details stay server-side (telemetry) — the user just needs "N of M edits couldn't be
 * saved, please redo them." All op-centric (comments are handled fail-soft separately server-side).
 */
export interface ComposePartialApplyInfo {
  /** Total ops in the batch the save attempted. */
  total: number;
  /** Ops that were successfully anchored + applied. */
  appliedCount: number;
  /** Ops that could NOT be anchored and were surfaced (not applied) — what the user must redo. */
  unresolvedCount: number;
}

/**
 * ai-advanced-capabilities-agreements-r1 task 032 (FR-16 128KB budget, Leg B — visible notice, not
 * chunking). ADR-040's `InlinePayloadCapBytes` (128 KB) truncates an over-cap findings payload at the
 * LEDGER WRITE seam — the full `flaggedSections[]` is gone before the client ever sees it, and the
 * read projection (`ChatEndpoints.ProjectComposeOutputs`) SKIPS a truncation-marker entry entirely,
 * so a truncated review is INDISTINGUISHABLE from "no review ran" on the GET response alone.
 * Chunking would need a SERVER write-seam change (splitting one Action turn's output into multiple
 * ledger entries before the cap applies) — out of this task's read-only `src/server/**` boundary.
 * This is the client-only fallback signal: never silently show nothing when a prior review is known
 * (via the sessionStorage marker or an unusable-but-present payload) to have produced findings.
 * Placed in this neutral types module (not `ComposeWorkspace.tsx`) so `ComposeBannerStack.tsx` can
 * import it without a circular `ComposeWorkspace.tsx` ⇄ `ComposeBannerStack.tsx` dependency.
 */
export interface ComposeReviewFindingsDegraded {
  /** Best-known count of findings that should have restored (0 for the 'malformed' case). */
  expectedCount: number;
  /**
   * 'skipped' — the read-projection shows ZERO findings-shaped outputs but a same-tab sessionStorage
   * marker says a prior review placed N findings (a truncated payload silently dropped server-side).
   * 'malformed' — a findings-shaped output IS present in the read-projection but every entry failed
   * the `projectLedgerFindingsToAdvisoryComments` guard (corrupted/partial payload).
   */
  reason: 'skipped' | 'malformed';
}

export interface ComposeWorkspaceState {
  status: ComposeWorkspaceStatus;
  documentRef: ComposeDocumentRef | null;
  sessionId: string;
  docxBytes: ArrayBuffer | null;
  /**
   * DEF-08: AI-drafted full-document seed HTML. Populated by `mountDraftHtml` (mutually exclusive
   * with `docxBytes`) — the editor sets its content directly from this HTML. Null on every non-draft
   * path.
   */
  seedHtml: string | null;
  /** ETag from last load — used as if-match on save (per ComposeEndpoints contract). */
  etag: string | null;
  /**
   * R3 FR-06 (task 027): the LOAD-TIME SPE version id from the Load response. Sent as
   * `baselineVersionId` on a dirty-loaded save so the server can re-fetch the load-time baseline even if
   * the client no longer holds the retained bytes (e.g. after a page refresh). Null for a transient/
   * born-in-editor mount (no server version yet) or when the Load did not surface one.
   */
  versionId: string | null;
  /** Mammoth import warnings (Tier 1 safe). */
  importWarnings: Array<{ type: string; message: string }>;
  /**
   * task 052 fast-follow (FR-08/FR-24/FR-25 wire gap): the server pre-parse `w14:paraId` map
   * from a STORED-DOCUMENT Load response (task 010), in document order. Set ATOMICALLY with
   * `docxBytes` on `loadSucceeded` (the ComposeEditor mount contract) so the editor stamps
   * paraIds immediately after import. Empty for every other mount door (Browse upload / assistant
   * upload / AI-draft seed) — those never carry a server pre-parse.
   */
  paraIdMap: readonly ParaIdMapEntry[];
  /**
   * task 052 fast-follow — existing Word revisions (`w:ins`/`w:del`) recovered server-side on a
   * STORED-DOCUMENT Load (task 050) and projected for the editor render (FR-24). Set atomically
   * with `docxBytes` + `paraIdMap`. Empty for every other mount door.
   */
  importedRevisions: readonly ImportedRevision[];
  /**
   * task 052 fast-follow — existing Word comments (`w:comment`) recovered server-side on a
   * STORED-DOCUMENT Load (task 051) and projected for the editor render (FR-25). Set atomically
   * with `docxBytes` + `paraIdMap`. Empty for every other mount door.
   */
  importedComments: readonly ImportedComment[];
  /**
   * The server-side DOCX→editor projection from a STORED-DOCUMENT Load response. When present, the
   * editor mounts `projection.html` directly (the paraId extension parses `data-paraid`). Fail-closed:
   * `canEdit === false` ⇒ the editor renders a read-only / "Open in Word" state, NEVER a blank editable
   * doc. Null for an older BFF that predates the projection field, or (task 013, F-2 "one reader") a
   * transient (Browse / assistant-upload / AI-draft) mount whose projection round-trip failed/was
   * unreachable — the client-side mammoth fallback reader is DELETED, so a docx mount with a null
   * projection now renders an explicit error/unavailable state instead.
   */
  projection: ComposeServerProjection | null;
  /**
   * task 012 (spaarkeai-compose-r6, render-on-save cutover): the retained CANONICAL content model
   * from the mount door's server response (Load / Upload / Browse->project all carry the additive
   * `contentModel` field since commit 70be80006) — the imported save mapper's MERGE BASE
   * (`ComposeEditorHandle.buildImportedContentModel(loadedContentModel, …)` folds the editor's edits
   * + comment threads onto it). Set ATOMICALLY wherever `projection` is set (same clear-rather-than-
   * inherit discipline). `null` = legacy session / older BFF / failed canonical projection → the save
   * falls back to the transitional op-log request shape (unchanged behavior). Replaced by the
   * post-save model on a successful model-path save (`saveSucceeded.contentModel`).
   */
  loadedContentModel: ComposeContentModel | null;
  /**
   * task 013 (r6, review F7): the canonical-model projection's FLATTEN warnings from the SAME mount
   * response that carried `loadedContentModel` (`contentModelWarnings` on the load / upload /
   * project payloads — e.g. `text-box-flattened`, `complex-object-dropped`; previously
   * server-log-only). The loss they describe MATERIALIZES on the FIRST model-path save (the
   * byte-identical passthrough / op-log shapes never render from the flatten-tier model), so
   * `triggerSave` folds them into the `saveDegradationWarnings` it dispatches there — and they are
   * CLEARED via the `saveSucceeded.contentModel` adoption (the post-save adopted model already
   * reflects the loss) so subsequent saves do not repeat them. Same set/clear lifecycle as
   * `loadedContentModel`; null = none surfaced / older BFF.
   */
  loadedContentModelWarnings: Array<{ code: string; count: number }> | null;
  /**
   * Task 041 (spaarkeai-compose-r6, FR-06 — PDF intake): the load's source format. `'pdf'` = the
   * mounted docx was SYNTHESIZED server-side from a PDF's canonical-model projection (task 040;
   * the original PDF is untouched in SPE). Drives (a) the honest-lossiness notice banner and (b)
   * the save routing: while `'pdf'`, EVERY save takes the create-on-save path with a `.docx`
   * display name — a new Word document, never docx bytes replaced onto the `.pdf` item. Cleared
   * to null by `saveSucceeded` (the doc is re-targeted to its new docx identity) and by every
   * fresh mount. Null = native docx (the overwhelmingly common case — behavior unchanged).
   */
  sourceFormat: string | null;
  /**
   * 026-F5 (task 012, r6): SAVE-time degradation warnings — content the server (and/or the client
   * imported-model mapper) simplified/dropped while authoring the LAST successful save. A SEPARATE
   * warning family from `importWarnings` (load-time import fidelity): the workspace suppresses the
   * import banner (`hideImportWarnings`, UAT round-7 #8) but save warnings MUST still render.
   * REPLACED wholesale on every successful save — `null` (a clean save) clears any stale banner
   * (026-F5's second half). Never merged into `importWarnings` (that was the bug).
   */
  saveDegradationWarnings: Array<{ code: string; count: number }> | null;
  /** User-facing error message (NOT a Tier 3 sink). */
  errorMessage: string | null;
  /** UAT #10/#11 (task 052): true when the last save failed with HTTP 423 (the doc is open in Word — a
   *  co-authoring lock). Flips the error banner to the honest "Open in Word" bar with Retry + Reload-from-Word
   *  actions (there is no programmatic unlock). Reset on save-start / success / load. */
  saveErrorIsLock: boolean;
  /**
   * Prong 1 (task 055 — keep-edits graceful degradation): populated when the LAST save applied only PART
   * of the edit batch — some ops couldn't be anchored server-side, so the save best-effort-applied the
   * rest and surfaced the unresolvable ones (never silently applied, never silently dropped). Drives an
   * honest warning banner prompting the user to redo just those edits. `null` when the whole batch applied
   * cleanly (the common case). Reset on save-start / load.
   */
  partialApply: ComposePartialApplyInfo | null;
  /** Last assistant-inserted draft staged for confirm (Flow 5 R1 manual-confirm gate). */
  pendingAssistantInsert: ComposeAssistantToWorkspaceFlow | null;
  /** SPE check-out lifecycle (Task 050 / Spike #3 §9; Task 051 multi-tab UX). */
  checkoutStatus: ComposeCheckoutStatus;
  /** Populated only when `checkoutStatus === 'conflict'` (lock holder is a DIFFERENT user). */
  checkoutLockedBy: ComposeCheckoutLockedByInfo | null;
  /** Populated only when `checkoutStatus === 'same-user-conflict'`. Task 051. */
  sameUserConflictInfo: { checkedOutAt: string | null } | null;
  /** User-facing checkout failure message (set only when status is `'failed'`). */
  checkoutFailureMessage: string | null;
  /**
   * UAT #7 (compose-r2): monotonically-incrementing token bumped on every successful Save. A
   * change in value (not the value itself) is the signal the banner stack uses to surface a
   * transient "Saved ✓" MessageBar — so a second identical Save still re-triggers the confirmation.
   * 0 = no save has succeeded yet this mount.
   */
  saveSuccessToken: number;
  /**
   * G1 (FR-01, task 020): the persisted cross-session origin marker for THIS document —
   * `'authored'` | `'imported'` | `null`. `null` covers a transient/not-yet-promoted mount (no
   * `sprk_document` record) OR a legacy pre-existing record (no backfill) — treated the SAME as
   * `'imported'` by every consumer (BINDING null-handling contract; see `ComposeDocumentOrigin`).
   * Set on `loadSucceeded` (Path A reopen) and refreshed on `saveSucceeded` (so a same-session
   * create-on-save's resolved origin is known without a follow-up Load). Drives `triggerSave`'s
   * clean-vs-tracked routing on a REOPENED document — see the `bornInEditor` discriminant.
   */
  origin: ComposeDocumentOrigin | null;
  /**
   * G8 (FR-07, task 030): an external change to the underlying document (a new SPE version landed
   * from the document-management system — e.g. an Office edit) was detected and not yet dismissed/
   * reloaded. Drives the non-blocking "Document updated from document management system version"
   * banner. For a CLEAN editor the parent transparently remounts (requestLoad carries the flag
   * forward so the banner still shows post-reload); for a DIRTY editor the parent sets this WITHOUT
   * remounting (NFR-08 — the banner offers an explicit Reload so unsaved edits are never silently
   * dropped). Cleared on dismiss or on a fresh mount.
   */
  externalChangePending: boolean;
}

export type ComposeWorkspaceAction =
  // G8 (FR-07, task 030): `externalChange` (default false) carries the external-change flag THROUGH a
  // remount — a clean-editor auto-remount dispatches requestLoad with externalChange:true so the
  // banner still renders after loadSucceeded. Every other requestLoad (initial open / Search) omits it.
  | {
      kind: 'requestLoad';
      documentRef: ComposeDocumentRef;
      sessionId: string;
      externalChange?: boolean;
      carryDegradationWarnings?: Array<{ code: string; count: number }> | null;
    }
  | {
      kind: 'loadSucceeded';
      docxBytes: ArrayBuffer;
      etag: string | null;
      versionId: string | null;
      sessionId: string;
      sprkDocumentId?: string;
      fileName?: string;
      // task 052 fast-follow (FR-08/FR-24/FR-25 wire gap): parsed defensively by the caller from an
      // optional Load response field — undefined (older BFF) is normalized to `[]` in the reducer.
      paraIdMap?: readonly ParaIdMapEntry[];
      importedRevisions?: readonly ImportedRevision[];
      importedComments?: readonly ImportedComment[];
      // The server DOCX→editor projection. Undefined (older BFF) → null in the reducer → (task 013,
      // F-2) the editor renders an explicit error/unavailable state — no client fallback reader remains.
      projection?: ComposeServerProjection | null;
      // G1 (FR-01, task 020): the persisted origin marker (Path A only — Path B continuation and an
      // older BFF both omit it). Normalized to `null` in the reducer (BINDING null-handling contract —
      // treated the same as 'imported').
      origin?: ComposeDocumentOrigin | null;
      // task 012 (r6): the canonical content model from the Load response (additive since commit
      // 70be80006). Undefined/null (older BFF, or projection failure) → null → the save falls back
      // to the transitional op-log shape. Set atomically with `projection`.
      contentModel?: ComposeContentModel | null;
      // task 013 (r6, review F7): the canonical-model projection's flatten warnings from the same
      // response — folded into saveDegradationWarnings on the first model-path save. Undefined/null
      // (older BFF / no loss) → null.
      contentModelWarnings?: Array<{ code: string; count: number }> | null;
      // Task 041 (FR-06, PDF intake): the Load response's sourceFormat marker ('pdf' = the mounted
      // docx was synthesized from a PDF). Undefined/null (older BFF / native docx) → null.
      sourceFormat?: string | null;
      // Task 041 (FR-06): a client-minted transient dedup key, supplied ONLY on a PDF-sourced load —
      // carried onto documentRef.transientKey so the PDF's repeated create-on-saves dedup to ONE new
      // docx record (the G7 mechanism, reused).
      transientKey?: string;
      // FR-07(b) (task 010): the non-rotating logical id for a PDF-sourced (transient) load — carried
      // onto documentRef.composeLogicalId so the recovered identity survives re-mount. Undefined for a
      // native stored-doc load (identity comes from speDriveItemId/sprkDocumentId).
      composeLogicalId?: string;
      // FR-09 (task 071): the AUTHORITATIVE drive the doc was loaded from (the BFF Load response's
      // required `driveId`). Stamped onto documentRef so a subsequent Reload-from-source (requestLoad)
      // targets the drive the doc actually LIVES in — a doc in a BU-container drive the host `driveId`
      // prop doesn't identify would otherwise lose its drive on reload and hit the `!loadDriveId → reset`
      // blank branch (the R6 D4 "Reload from source blanks + asks for re-upload" root cause). Mirrors the
      // saveSucceeded create-on-save re-target stamp (UAT-2026-07-19 P2).
      driveId?: string;
    }
  | { kind: 'loadFailed'; errorMessage: string }
  // ── FR-03 (task 012): transient upload-mount (no SPE pointer, create-on-save) ──
  | { kind: 'requestUploadMount'; sessionId: string }
  // FR-05 (task 100): `containerId` is the client-resolved BU container the first Save
  // (create-on-save) persists into; carried on the transient documentRef so triggerSave
  // can send it. Undefined until the host resolves it (see ComposeWorkspaceProps.containerId).
  //
  // Wave 2 (UAT-R3 Test #3 fix): `sessionId` establishes the tab's DOCUMENT session id for a
  // door that has NO server round-trip (the Browse-direct-upload path). The assistant-upload
  // path sets sessionId earlier via `requestUploadMount`; Browse has no server-assigned id, so
  // the caller MINTS a client-generated document session id and threads it here. When omitted
  // (the upload path's `mountTransient`, which follows `requestUploadMount`) the reducer
  // preserves the sessionId already on state — INVARIANT: no mount leaves state.sessionId empty.
  //
  // FR-01/FR-03 (tasks 010/011, spaarkeai-compose-fidelity-r4.5): `projection`, when supplied, is
  // the SAME ComposeServerProjection shape the stored-doc Load path hydrates (see the
  // `loadSucceeded` action above) — the assistant-upload door (POST /api/compose/upload) AND the
  // Browse-direct-upload door (POST /api/compose/project, task 011, T-2 path-A) both run their
  // bytes through the server projection builder now. Undefined/omitted (an older BFF, or a failed/
  // unreachable projection call) normalizes to `null` in the reducer. Task 013 (F-2 "one reader"):
  // the client mammoth fallback is DELETED, so a null projection on a docx mount now renders an
  // explicit error/unavailable state (never a silent blank or degraded editor).
  | {
      kind: 'mountTransient';
      docxBytes: ArrayBuffer;
      fileName?: string;
      containerId?: string;
      sessionId?: string;
      projection?: ComposeServerProjection | null;
      // task 012 (r6): the canonical content model from the Upload / Browse->project response (same
      // additive field as `loadSucceeded.contentModel`). Undefined/null normalizes to null → op-log
      // fallback save shape. Set atomically with `projection`.
      contentModel?: ComposeContentModel | null;
      // task 013 (r6, review F7): the projection's flatten warnings from the same response — same
      // lifecycle as `contentModel` (see the loadSucceeded field above).
      contentModelWarnings?: Array<{ code: string; count: number }> | null;
      // G7 (FR-06, task 022): the client-minted stable transient-draft dedup key
      // (mintTransientKey). Carried onto documentRef.transientKey so every create-on-save sends it →
      // repeated saves dedup to ONE record. Omitted by an older caller → no dedup (unchanged behavior).
      transientKey?: string;
      // FR-07(b) (task 010): the non-rotating logical document id (startNewComposeLogicalId /
      // recovered). Carried onto documentRef.composeLogicalId — the SHARED key for FR-03 draft
      // recovery (040) + FR-07 client dedup (011). Persisted client-side; survives re-mount/reload.
      composeLogicalId?: string;
      // Task 051 (spaarkeai-compose-r7, FR-06 — PDF import parity): 'pdf' when the Browse-project /
      // Assistant-upload door forked a PDF into a server-SYNTHESIZED docx (task 050). Drives the editor's
      // editable admission (despite a .pdf display name) + the PDF create-on-save routing, exactly as
      // loadSucceeded.sourceFormat does for the Load door. Omitted for a native docx mount → null.
      sourceFormat?: string | null;
    }
  // ── DEF-08: AI-drafted full-document seed mount (create-on-save, like mountTransient) ──
  // Item 6 (UAT round-4): `sessionId` carries a MINTED document session id for born-in-editor mounts
  // (inline draft / Blank page / Open template). Without it state.sessionId stays '', which makes the
  // AI toolbar thread `documentSessionId: ''` → a "Draft alternative" is reclassified as informational
  // prose instead of an in-editor redline, and `materializeComposeDraftFromLedger` aborts. Same
  // NEVER-'' invariant as `mountTransient`.
  | {
      kind: 'mountDraftHtml';
      html: string;
      fileName?: string;
      containerId?: string;
      sessionId?: string;
      transientKey?: string;
      // FR-07(b) (task 010): non-rotating logical id for this born-in-editor draft — see mountTransient.
      composeLogicalId?: string;
    }
  | { kind: 'requestSave' }
  // FR-05 (task 100): create-on-save mints a NEW SPE drive-item; `documentSpeId` carries the
  // server-minted id back so a second Save targets the real item (the replace path), not the
  // empty transient pointer (gap 1.7).
  | {
      kind: 'saveSucceeded';
      sprkDocumentId?: string;
      documentSpeId?: string;
      etag: string | null;
      // UAT 2026-07-19 P2: the save response's driveId + versionId. Retained so a SUBSEQUENT
      // replace-path save of a BORN-IN-EDITOR doc (which holds no retained bytes) can resolve
      // its baseline by re-fetching the just-saved version (server ResolveSaveBaselineAsync
      // path b needs BaselineVersionId + DriveId + DocumentSpeId). Without these, the second
      // save sent content=undefined + baselineVersionId=undefined and the server threw
      // "no baseline could be resolved — supply the retained original bytes (Content)".
      driveId?: string;
      versionId?: string | null;
      // G1 (FR-01, task 020): the ComposeOrigin THIS save resolved (server-side, from ContentModel
      // presence). Populated on every save so a same-session create-on-save's resolved origin is
      // known without a follow-up Load — refreshed into state.origin below (never regressed to null
      // by an older BFF response that omits the field; see the reducer).
      origin?: ComposeDocumentOrigin | null;
      // Prong 1 (task 055): best-effort partial-apply summary when some ops couldn't be anchored server-side
      // (null/omitted on the common clean-batch path). Drives the honest "N edits couldn't be saved" banner.
      partialApply?: ComposePartialApplyInfo | null;
      // task 012 (r6): the post-save content model adopted as the NEW merge base after a successful
      // MODEL-PATH save (the server's returned model, or — when the server omitted it — the model
      // the client POSTed; the caller resolves that fallback). Omitted on op-log / born-in-editor
      // saves → the reducer keeps the existing `loadedContentModel` (never regresses to null).
      contentModel?: ComposeContentModel | null;
      // FR-07(a) (task 012): on a Save-New fork, the uniquified fork filename + the fresh task-010
      // logical id, adopted onto the forked documentRef so it reflects the NEW document (a real fork),
      // not the original. Undefined on every non-fork save (the reducer keeps the existing values).
      fileName?: string;
      composeLogicalId?: string;
    }
  | { kind: 'saveFailed'; errorMessage: string; isLock?: boolean }
  | { kind: 'reset' }
  | { kind: 'importWarnings'; warnings: Array<{ type: string; message: string }> }
  // 026-F5 (task 012, r6): REPLACE the save-time degradation-warning set. Dispatched on EVERY
  // successful save: warnings present → set; none → null (a clean save clears the stale banner).
  | { kind: 'saveDegradationWarnings'; warnings: Array<{ code: string; count: number }> | null }
  | { kind: 'pendingAssistantInsert'; payload: ComposeAssistantToWorkspaceFlow }
  | { kind: 'clearPendingAssistantInsert' }
  // ── Task 050 (Spike #3 §9): SPE check-out lifecycle actions ───────────────
  | { kind: 'checkoutSkipped' }
  | { kind: 'checkoutRequested' }
  | { kind: 'checkoutAcquired' }
  | { kind: 'checkoutConflict'; lockedBy: ComposeCheckoutLockedByInfo }
  | { kind: 'checkoutFailed'; failureMessage: string }
  // ── Task 051 (Spike #3 §1 multi-tab UX): probe + same-user conflict ─────
  | { kind: 'checkoutProbeRequested' }
  | { kind: 'checkoutSameUserConflict'; checkedOutAt: string | null }
  | { kind: 'checkoutDiscarding' }
  | { kind: 'checkoutCancelled' }
  // ── G8 (FR-07, task 030): external-change detection → banner ──────────────
  // Set when check-changes/webhook reports the underlying document changed AND the editor is DIRTY
  // (the parent defers the remount to protect unsaved edits). A CLEAN editor uses requestLoad
  // (externalChange:true) instead, which remounts + carries the flag forward.
  | { kind: 'externalChangeDetected' }
  | { kind: 'externalChangeDismissed' };

export const INITIAL_STATE: ComposeWorkspaceState = {
  status: 'empty',
  documentRef: null,
  sessionId: '',
  docxBytes: null,
  seedHtml: null,
  etag: null,
  versionId: null,
  importWarnings: [],
  paraIdMap: [],
  importedRevisions: [],
  importedComments: [],
  projection: null,
  loadedContentModel: null,
  loadedContentModelWarnings: null,
  sourceFormat: null,
  saveDegradationWarnings: null,
  errorMessage: null,
  saveErrorIsLock: false,
  partialApply: null,
  pendingAssistantInsert: null,
  checkoutStatus: 'idle',
  checkoutLockedBy: null,
  sameUserConflictInfo: null,
  checkoutFailureMessage: null,
  saveSuccessToken: 0,
  origin: null,
  externalChangePending: false,
};

export function composeWorkspaceReducer(
  state: ComposeWorkspaceState,
  action: ComposeWorkspaceAction
): ComposeWorkspaceState {
  switch (action.kind) {
    case 'requestLoad':
      return {
        ...INITIAL_STATE,
        status: 'loading',
        documentRef: action.documentRef,
        sessionId: action.sessionId,
        // G8 (task 030): carry the external-change flag through a clean-editor auto-remount so the
        // banner still renders after loadSucceeded (which spreads ...state). Defaults false for every
        // normal load (initial open / Search).
        externalChangePending: action.externalChange ?? false,
        // 032 Step-9.5 F2: requestLoad resets to INITIAL_STATE, which WIPES a just-dispatched
        // degradation banner. Apply-template's merge warnings exist ONLY in the apply response —
        // carry them through the remount or they are silently lost (loud-not-silent principle).
        saveDegradationWarnings: action.carryDegradationWarnings ?? null,
      };
    case 'loadSucceeded':
      return {
        ...state,
        status: 'loaded',
        docxBytes: action.docxBytes,
        seedHtml: null,
        etag: action.etag,
        versionId: action.versionId,
        sessionId: action.sessionId,
        // task 052 fast-follow (FR-08/FR-24/FR-25 wire gap): set ATOMICALLY with docxBytes (the
        // ComposeEditor mount contract) — normalize an omitted field (older BFF) to `[]`.
        paraIdMap: action.paraIdMap ?? [],
        importedRevisions: action.importedRevisions ?? [],
        importedComments: action.importedComments ?? [],
        // The server projection (null for an older BFF → task 013/F-2 error-unavailable state).
        projection: action.projection ?? null,
        // task 012 (r6): the canonical content model — set ATOMICALLY with projection (same source
        // response). Omitted (older BFF / failed canonical projection) → null → op-log fallback save.
        loadedContentModel: action.contentModel ?? null,
        // task 013 (r6, F7): the projection's flatten warnings — same atomic set/clear as the model.
        loadedContentModelWarnings: action.contentModelWarnings ?? null,
        // G1 (FR-01, task 020): normalize an omitted/undefined field (Path B continuation, or an
        // older BFF) to `null` — the BINDING null-handling contract treats null as 'imported'.
        origin: action.origin ?? null,
        // Task 041 (FR-06, PDF intake): 'pdf' = the mounted docx was synthesized from a PDF —
        // drives the honest-lossiness notice + the create-on-save routing. Omitted → null.
        sourceFormat: action.sourceFormat ?? null,
        documentRef: state.documentRef
          ? {
              ...state.documentRef,
              sprkDocumentId: action.sprkDocumentId ?? state.documentRef.sprkDocumentId,
              fileName: action.fileName ?? state.documentRef.fileName,
              // Task 041: the PDF dedup key (supplied only on PDF-sourced loads) — repeated saves
              // of the same PDF session create-on-save onto ONE new docx record (G7 mechanism).
              transientKey: action.transientKey ?? state.documentRef.transientKey,
              // FR-07(b) (task 010): the non-rotating logical id — supplied on a PDF-sourced
              // (transient) load; preserved from state for a native stored-doc load (where identity
              // comes from speDriveItemId/sprkDocumentId and this stays undefined).
              composeLogicalId: action.composeLogicalId ?? state.documentRef.composeLogicalId,
              // FR-09 (task 071): stamp the AUTHORITATIVE load-time drive so a later Reload-from-source
              // targets where the doc lives (never the `!loadDriveId → reset` blank). Mirrors the
              // saveSucceeded stamp below; a defensive empty/undefined falls back to the existing value.
              driveId:
                action.driveId && action.driveId.length > 0 ? action.driveId : state.documentRef.driveId,
            }
          : state.documentRef,
        errorMessage: null,
      };
    case 'loadFailed':
      return {
        ...state,
        status: 'error',
        errorMessage: action.errorMessage,
      };
    // ── FR-03 (task 012): transient upload-mount ────────────────────────────
    case 'requestUploadMount':
      // Enter the loading spinner WITHOUT a documentRef so the BFF Load effect
      // (which gates on `state.documentRef`) stays inert — the upload-mount effect
      // owns this transition and fetches from POST /api/compose/upload instead.
      return {
        ...INITIAL_STATE,
        status: 'loading',
        sessionId: action.sessionId,
        documentRef: null,
      };
    case 'mountTransient':
      // Pointer-less transient working draft: docxBytes populated, documentRef carries
      // ONLY a display fileName (empty speDriveItemId = "no SPE pointer yet"). No
      // sprk_document, no checkout (skipped) — first Save runs create-on-save (task 013).
      //
      // Wave 2 (UAT-R3 Test #3 fix): a Browse-direct-upload has no server round-trip, so it
      // supplies a MINTED document session id via `action.sessionId`. The upload path reaches
      // here AFTER `requestUploadMount` already set state.sessionId and omits `action.sessionId`,
      // so fall back to the existing state.sessionId in that case. INVARIANT: state.sessionId is
      // NEVER left '' after a mount — downstream AI-edit routing threads it as `documentSessionId`
      // (ComposeAiToolbar), and `documentSessionId.length > 0` is what classifies a compose EDIT
      // (redline) vs a misrouted INFORMATIONAL prose card (ConversationPane.dispatchComposeAction).
      return {
        ...state,
        status: 'loaded',
        docxBytes: action.docxBytes,
        seedHtml: null,
        etag: null,
        versionId: null,
        sessionId: action.sessionId ?? state.sessionId,
        // G7 (FR-06, task 022): stamp the client-minted transient dedup key onto documentRef so every
        // create-on-save sends it (triggerSave) → repeated transient saves dedup to ONE record.
        documentRef: {
          speDriveItemId: '',
          fileName: action.fileName,
          containerId: action.containerId,
          transientKey: action.transientKey,
          // FR-07(b) (task 010): the non-rotating logical id for this transient mount — the shared
          // key for FR-03 draft recovery + FR-07 dedup. Read identity via getComposeLogicalIdentity.
          composeLogicalId: action.composeLogicalId,
        },
        checkoutStatus: 'skipped',
        // A transient (Browse / assistant-upload) mount has no server pre-parse — there is no
        // stored-document Load response to source imports from. Explicitly clear rather than
        // inheriting whatever a PRIOR loaded document may have populated (e.g. Browse-mounting a
        // new local file after an earlier stored-document load in the same tab).
        paraIdMap: [],
        importedRevisions: [],
        importedComments: [],
        // FR-01/FR-03 (tasks 010/011): both the assistant-upload door (POST /api/compose/upload)
        // AND the Browse-direct-upload door (POST /api/compose/project, T-2 path-A) now supply a
        // server projection built from the SAME mounted bytes (ComposeDocxProjectionBuilder) —
        // hydrate it so the editor mounts via the projection branch, identical to stored-doc Load.
        // `action.projection` normalizes to `null` here when omitted (an older BFF) or when the
        // caller's projection round-trip failed/was unreachable — task 013 (F-2): the editor now
        // renders an explicit error/unavailable state for that case (no client fallback reader).
        projection: action.projection ?? null,
        // task 012 (r6): set atomically with projection — SAME clear-rather-than-inherit discipline
        // (a new transient mount over a prior loaded doc must never keep the prior doc's model).
        loadedContentModel: action.contentModel ?? null,
        // task 013 (r6, F7): same lifecycle as the model — set from this mount's response or cleared.
        loadedContentModelWarnings: action.contentModelWarnings ?? null,
        // Task 051 (FR-06 — PDF import parity): a Browse-project / Assistant-upload mount CAN now be
        // PDF-sourced (task 050 gave ProjectForMount the intake fork), so carry the marker when the door
        // supplies it; a native docx mount omits it → null (clear-rather-than-inherit still holds — a fresh
        // mount over a prior PDF session must not keep the prior 'pdf').
        sourceFormat: action.sourceFormat ?? null,
        // 026-F5 (task 012, r6): a fresh mount has no save history — clear any stale save-warning
        // banner from a prior document mounted in this same tab.
        saveDegradationWarnings: null,
        // G1 (FR-01, task 020): a fresh transient mount has no persisted origin yet (this doc has
        // never been saved) — explicitly clear rather than inheriting a PRIOR document's origin from
        // an earlier mount in the same tab (same "clear rather than inherit" rationale as
        // paraIdMap/importedRevisions/importedComments above).
        origin: null,
        errorMessage: null,
      };
    // ── DEF-08: AI-drafted full-document seed. Like mountTransient (create-on-save, no SPE
    // pointer, checkout skipped) but the editor content comes from seedHtml, not docxBytes.
    case 'mountDraftHtml':
      return {
        ...state,
        status: 'loaded',
        docxBytes: null,
        seedHtml: action.html,
        etag: null,
        versionId: null,
        // Item 6 (UAT round-4): adopt a minted document session id (born-in-editor mounts have no
        // server round-trip to supply one). Fall back to the existing state.sessionId for the Part-A
        // ledger draft path, which already set it via `requestUploadMount` before this action.
        sessionId: action.sessionId ?? state.sessionId,
        // G7 (FR-06, task 022): same transient dedup key stamp as mountTransient (born-in-editor drafts
        // create-on-save on first Save, so they need the same repeat-save dedup identity).
        documentRef: {
          speDriveItemId: '',
          fileName: action.fileName,
          containerId: action.containerId,
          transientKey: action.transientKey,
          // FR-07(b) (task 010): non-rotating logical id for this born-in-editor draft.
          composeLogicalId: action.composeLogicalId,
        },
        checkoutStatus: 'skipped',
        // An AI-drafted seed has no server pre-parse either — same rationale as `mountTransient`.
        paraIdMap: [],
        importedRevisions: [],
        importedComments: [],
        // No server round-trip → no projection. `docxBytes` is also null for this mount kind (the
        // editor seeds directly from `initialHtml`), so the editor's docx-mount branch (projection /
        // error-unavailable) is never reached here — this was never a mammoth consumer (task 012 audit).
        projection: null,
        // task 012 (r6): a born-in-editor seed has no loaded/imported baseline model — its saves
        // author the whole document via `buildContentModel()`, never the imported-model merge path.
        loadedContentModel: null,
        // task 013 (r6, F7): no projection ran for this mount — nothing was flattened.
        loadedContentModelWarnings: null,
        // Task 041 (FR-06): a born-in-editor seed is not a PDF-sourced load — clear rather than inherit.
        sourceFormat: null,
        // 026-F5 (task 012, r6): clear any stale save-warning banner from a prior mount (same
        // clear-rather-than-inherit rationale as mountTransient).
        saveDegradationWarnings: null,
        // G1 (FR-01, task 020): same rationale as mountTransient — a fresh AI-drafted seed has no
        // persisted origin yet.
        origin: null,
        errorMessage: null,
      };
    case 'requestSave':
      if (state.status !== 'loaded') return state;
      return { ...state, status: 'saving', errorMessage: null, saveErrorIsLock: false, partialApply: null };
    case 'saveSucceeded':
      return {
        ...state,
        status: 'loaded',
        etag: action.etag,
        // Prong 1 (task 055): a save may succeed WITH a partial-apply summary (some ops couldn't be
        // anchored). Carry it so the banner stack prompts the user to redo just those edits; null on a
        // clean batch (the common path) clears any prior partial-apply banner.
        partialApply: action.partialApply ?? null,
        // UAT #7: bump the token so the banner stack surfaces a fresh transient "Saved ✓".
        saveSuccessToken: state.saveSuccessToken + 1,
        // UAT 2026-07-19 P2: adopt the just-saved SPE version id as the baseline ONLY on the
        // first persist (when we had none). A born-in-editor doc holds no retained bytes
        // (docxBytes stays null) and had no load-time versionId; its create-on-save minted this
        // first version, which becomes the FIXED baseline its replace-path saves delta onto.
        //   CRITICAL — adopt-only-when-null (`state.versionId ??`): a STORED doc's versionId is
        //   the LOAD-TIME original (set on loadSucceeded) and MUST stay fixed across saves so
        //   every save is a delta vs the load-time original (FR-01). Advancing it each save would
        //   re-baseline onto the just-saved version and corrupt the tracked-change accumulation.
        //   Task 041 (FR-06, PDF intake) EXCEPTION: a PDF-sourced doc's load-time versionId is the
        //   PDF ITEM's version — meaningless as a baseline for the NEW docx its create-on-save just
        //   minted. Re-baseline onto the save response's versionId (the new doc's first version)
        //   instead of keeping the PDF's.
        versionId:
          state.sourceFormat === 'pdf'
            ? action.versionId && action.versionId.length > 0
              ? action.versionId
              : null
            : (state.versionId ?? (action.versionId && action.versionId.length > 0 ? action.versionId : null)),
        // G1 (FR-01, task 020): refresh from this save's resolved origin when the response carries
        // one; otherwise keep whatever state already had (never regress a known origin to null just
        // because an older BFF response omitted the field).
        origin: action.origin ?? state.origin,
        // task 012 (r6): a successful MODEL-PATH save adopts the post-save model as the new merge
        // base. Omitted (op-log / born-in-editor save, or a caller without one) → keep the existing
        // base — NEVER regress a known model to null on success.
        loadedContentModel: action.contentModel ?? state.loadedContentModel,
        // task 013 (r6, F7): model adoption ⇔ a model-path save just materialized the projection's
        // flatten loss (triggerSave folded these into that save's saveDegradationWarnings dispatch)
        // — CLEAR them so subsequent saves do not repeat them (the adopted post-save model already
        // reflects the loss). An op-log / born-in-editor save (no `action.contentModel`) keeps them:
        // the loss has NOT materialized on the byte-identical path yet.
        loadedContentModelWarnings: action.contentModel ? null : state.loadedContentModelWarnings,
        // Task 041 (FR-06, PDF intake): the create-on-save re-targeted the doc to its NEW docx
        // identity (documentSpeId/driveId below) — it is a native docx from here on. Clearing the
        // marker routes subsequent saves onto the normal replace path and retires the PDF notice.
        sourceFormat: null,
        documentRef: state.documentRef
          ? {
              ...state.documentRef,
              sprkDocumentId: action.sprkDocumentId ?? state.documentRef.sprkDocumentId,
              // Task 041 (FR-06): a PDF-sourced save just created a NEW Word document — reflect the
              // .docx name locally (triggerSave sent it as the create displayName) so subsequent
              // replace-path saves and the toolbar show the document's real identity. Review
              // B-LOW-4: the undefined-name fallback MIRRORS triggerSave's ('document.pdf' →
              // 'document.docx') so local state never diverges from the server record.
              // FR-07(a) (task 012): a Save-New fork adopts the uniquified fork name (action.fileName);
              // otherwise the PDF→docx rename or the existing name, unchanged.
              fileName:
                action.fileName
                ?? (state.sourceFormat === 'pdf'
                  ? (state.documentRef.fileName ?? 'document.pdf').replace(/\.pdf$/i, '') + '.docx'
                  : state.documentRef.fileName),
              // FR-07(a/b) (task 012/010): a fork adopts a NEW logical id (action.composeLogicalId);
              // a non-fork save preserves the existing one (the accessor prefers sprkDocumentId anyway).
              composeLogicalId: action.composeLogicalId ?? state.documentRef.composeLogicalId,
              // gap 1.7: carry the server-minted SPE id back so the mount is no longer transient
              // (empty speDriveItemId) — a second Save now targets the real drive-item.
              speDriveItemId:
                action.documentSpeId && action.documentSpeId.length > 0
                  ? action.documentSpeId
                  : state.documentRef.speDriveItemId,
              // UAT 2026-07-19 P2: carry the server-resolved drive id (a create-on-save doc lands
              // in the BU container's drive, which the host's `driveId` prop does NOT identify) so
              // the replace-path save + baseline re-fetch target the correct drive.
              driveId: action.driveId && action.driveId.length > 0 ? action.driveId : state.documentRef.driveId,
            }
          : state.documentRef,
      };
    case 'saveFailed':
      return { ...state, status: 'loaded', errorMessage: action.errorMessage, saveErrorIsLock: action.isLock ?? false };
    case 'reset':
      return INITIAL_STATE;
    case 'importWarnings':
      return { ...state, importWarnings: action.warnings };
    // 026-F5 (task 012, r6): wholesale REPLACE of the save-warning family — every successful save
    // dispatches this; null (a clean save) clears the stale banner.
    case 'saveDegradationWarnings':
      return { ...state, saveDegradationWarnings: action.warnings };
    case 'pendingAssistantInsert':
      return { ...state, pendingAssistantInsert: action.payload };
    case 'clearPendingAssistantInsert':
      return { ...state, pendingAssistantInsert: null };
    // ── Task 050: SPE check-out lifecycle ───────────────────────────────────
    case 'checkoutSkipped':
      return {
        ...state,
        checkoutStatus: 'skipped',
        checkoutLockedBy: null,
        sameUserConflictInfo: null,
        checkoutFailureMessage: null,
      };
    case 'checkoutRequested':
      return {
        ...state,
        checkoutStatus: 'acquiring',
        checkoutLockedBy: null,
        sameUserConflictInfo: null,
        checkoutFailureMessage: null,
      };
    case 'checkoutAcquired':
      return {
        ...state,
        checkoutStatus: 'acquired',
        checkoutLockedBy: null,
        sameUserConflictInfo: null,
        checkoutFailureMessage: null,
      };
    case 'checkoutConflict':
      return {
        ...state,
        checkoutStatus: 'conflict',
        checkoutLockedBy: action.lockedBy,
        sameUserConflictInfo: null,
        checkoutFailureMessage: null,
      };
    case 'checkoutFailed':
      return {
        ...state,
        checkoutStatus: 'failed',
        checkoutLockedBy: null,
        sameUserConflictInfo: null,
        checkoutFailureMessage: action.failureMessage,
      };
    // ── Task 051: probe + same-user conflict ────────────────────────────────
    case 'checkoutProbeRequested':
      return {
        ...state,
        checkoutStatus: 'probing',
        checkoutLockedBy: null,
        sameUserConflictInfo: null,
        checkoutFailureMessage: null,
      };
    case 'checkoutSameUserConflict':
      return {
        ...state,
        checkoutStatus: 'same-user-conflict',
        checkoutLockedBy: null,
        sameUserConflictInfo: { checkedOutAt: action.checkedOutAt },
        checkoutFailureMessage: null,
      };
    case 'checkoutDiscarding':
      return {
        ...state,
        checkoutStatus: 'discarding',
        checkoutLockedBy: null,
        sameUserConflictInfo: null,
        checkoutFailureMessage: null,
      };
    case 'checkoutCancelled':
      return {
        ...state,
        checkoutStatus: 'cancelled',
        checkoutLockedBy: null,
        sameUserConflictInfo: null,
        checkoutFailureMessage: null,
      };
    // ── G8 (task 030): external-change banner ────────────────────────────────
    case 'externalChangeDetected':
      return { ...state, externalChangePending: true };
    case 'externalChangeDismissed':
      return { ...state, externalChangePending: false };
    default:
      return state;
  }
}
