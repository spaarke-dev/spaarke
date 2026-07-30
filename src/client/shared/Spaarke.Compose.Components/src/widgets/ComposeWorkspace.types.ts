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
  /** User-facing error message (NOT a Tier 3 sink). */
  errorMessage: string | null;
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
}

export type ComposeWorkspaceAction =
  | { kind: 'requestLoad'; documentRef: ComposeDocumentRef; sessionId: string }
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
      // G7 (FR-06, task 022): the client-minted stable transient-draft dedup key
      // (mintTransientKey). Carried onto documentRef.transientKey so every create-on-save sends it →
      // repeated saves dedup to ONE record. Omitted by an older caller → no dedup (unchanged behavior).
      transientKey?: string;
    }
  // ── DEF-08: AI-drafted full-document seed mount (create-on-save, like mountTransient) ──
  // Item 6 (UAT round-4): `sessionId` carries a MINTED document session id for born-in-editor mounts
  // (inline draft / Blank page / Open template). Without it state.sessionId stays '', which makes the
  // AI toolbar thread `documentSessionId: ''` → a "Draft alternative" is reclassified as informational
  // prose instead of an in-editor redline, and `materializeComposeDraftFromLedger` aborts. Same
  // NEVER-'' invariant as `mountTransient`.
  | { kind: 'mountDraftHtml'; html: string; fileName?: string; containerId?: string; sessionId?: string; transientKey?: string }
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
    }
  | { kind: 'saveFailed'; errorMessage: string }
  | { kind: 'reset' }
  | { kind: 'importWarnings'; warnings: Array<{ type: string; message: string }> }
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
  | { kind: 'checkoutCancelled' };

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
  errorMessage: null,
  pendingAssistantInsert: null,
  checkoutStatus: 'idle',
  checkoutLockedBy: null,
  sameUserConflictInfo: null,
  checkoutFailureMessage: null,
  saveSuccessToken: 0,
  origin: null,
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
        // G1 (FR-01, task 020): normalize an omitted/undefined field (Path B continuation, or an
        // older BFF) to `null` — the BINDING null-handling contract treats null as 'imported'.
        origin: action.origin ?? null,
        documentRef: state.documentRef
          ? {
              ...state.documentRef,
              sprkDocumentId: action.sprkDocumentId ?? state.documentRef.sprkDocumentId,
              fileName: action.fileName ?? state.documentRef.fileName,
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
        documentRef: { speDriveItemId: '', fileName: action.fileName, containerId: action.containerId, transientKey: action.transientKey },
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
        documentRef: { speDriveItemId: '', fileName: action.fileName, containerId: action.containerId, transientKey: action.transientKey },
        checkoutStatus: 'skipped',
        // An AI-drafted seed has no server pre-parse either — same rationale as `mountTransient`.
        paraIdMap: [],
        importedRevisions: [],
        importedComments: [],
        // No server round-trip → no projection. `docxBytes` is also null for this mount kind (the
        // editor seeds directly from `initialHtml`), so the editor's docx-mount branch (projection /
        // error-unavailable) is never reached here — this was never a mammoth consumer (task 012 audit).
        projection: null,
        // G1 (FR-01, task 020): same rationale as mountTransient — a fresh AI-drafted seed has no
        // persisted origin yet.
        origin: null,
        errorMessage: null,
      };
    case 'requestSave':
      if (state.status !== 'loaded') return state;
      return { ...state, status: 'saving', errorMessage: null };
    case 'saveSucceeded':
      return {
        ...state,
        status: 'loaded',
        etag: action.etag,
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
        versionId: state.versionId ?? (action.versionId && action.versionId.length > 0 ? action.versionId : null),
        // G1 (FR-01, task 020): refresh from this save's resolved origin when the response carries
        // one; otherwise keep whatever state already had (never regress a known origin to null just
        // because an older BFF response omitted the field).
        origin: action.origin ?? state.origin,
        documentRef: state.documentRef
          ? {
              ...state.documentRef,
              sprkDocumentId: action.sprkDocumentId ?? state.documentRef.sprkDocumentId,
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
      return { ...state, status: 'loaded', errorMessage: action.errorMessage };
    case 'reset':
      return INITIAL_STATE;
    case 'importWarnings':
      return { ...state, importWarnings: action.warnings };
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
    default:
      return state;
  }
}
