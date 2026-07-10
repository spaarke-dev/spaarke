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
import {
  ComposeEditor,
  type ComposeEditorHandle,
  type ComposeEditorDocumentRef,
  type ComposeDraftPayload,
} from './ComposeEditor';
import type { ComposeActionEnqueue } from './ComposeAiToolbar';
// spaarkeai-compose-r1 task 093: deep-import from `@spaarke/ai-widgets/events`
// rather than the barrel `@spaarke/ai-widgets` to skip the barrel's side-effect
// widget registration (`register-workspace-widgets.ts` transitively pulls in
// `@spaarke/ai-outputs` subpaths, which LegalWorkspace's standalone Rollup
// cannot resolve). Matches the SpaarkeAi `useWorkspaceLayouts` adapter pattern
// (documented in `src/solutions/SpaarkeAi/src/hooks/useWorkspaceLayouts.ts`).
import { type WorkspacePaneEvent } from '@spaarke/ai-widgets/events';
// Compose three-pane coordination — WORKSPACE leg (task 104 / E2E-R5). Typed
// receivers for Flow 3 (compose_context_insert) + Flow 5 (compose_assistant_insert).
import { useComposeWorkspaceReceivers } from './useComposeWorkspaceReceivers';
import { authenticatedFetch } from '@spaarke/auth';
// FR-02 (task 011): "Search for Document" opens the standard Dataverse lookup dialog
// (Xrm.Utility.lookupObjects) scoped to `sprk_document`, then resolves the picked
// record's SPE pointer via Xrm.WebApi.retrieveRecord — the SAME two Xrm primitives
// `DataverseLookupField` and the 1c ribbon launcher (`DocumentComposeLaunch.ts`)
// already use. No new lookup UI, no new BFF endpoint (ADR-039: this is a data query,
// not a dispatch).
import { createXrmNavigationService, createXrmDataService, type LookupResult } from '@spaarke/ui-components';

import { ComposeToolbar } from './ComposeToolbar';
import { ComposeEmptyState } from './ComposeEmptyState';
import { ComposeConflictDialog } from './ComposeConflictDialog';
// Return-from-Word re-anchor UX (task 054 — BUILT; mounted here by task 103, gap 3.5).
import { ComposeReanchorBanner } from './ComposeReanchorBanner';
import { ComposeReanchorConflictPanel } from './ComposeReanchorConflictPanel';
import { useComposeReanchor } from './useComposeReanchor';
import type { ReanchorResolutionDecision } from './ComposeReanchor.types';
// Word round-trip shuttle client callers (task 103 — gaps 3.1 / 3.4 / poll half of 3.5).
import {
  useComposePushAnnotations,
  useComposePullAnnotations,
  useComposeCheckChanges,
  anchoredAnnotationsToPriorAnchors,
  anchoredAnnotationsToDocxAnnotations,
} from './useComposeWordShuttle';
import { composeWorkspaceReducer, INITIAL_STATE } from './ComposeWorkspace.types';
import { useComposeBroadcastChannel, useComposeCheckoutLifecycle, useComposeHeartbeatGate } from './hooks';
import type {
  ComposeDocumentRef,
  ComposeUploadRef,
  ComposeAssistantToWorkspaceFlow,
  AnchoredAnnotation,
  DefinedTerm,
  ComposeActionHistoryEntry,
} from '../types/compose-contracts';

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

  /** Called when the user clicks Search for Document in the empty state. */
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
  } = props;

  const [state, dispatch] = React.useReducer(composeWorkspaceReducer, INITIAL_STATE);

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
          fileName?: string;
          size: number;
          correlationId?: string;
          // FR-29/FR-33 (R2, tasks 060/102, design.md §8): the three collections the BFF Load
          // response now projects from the resumed/created session (gaps 4.2/4.4). Parsed
          // defensively (optional) so an older BFF that predates the wiring still loads.
          anchoredAnnotations?: AnchoredAnnotation[];
          definedTermsTracking?: DefinedTerm[];
          actionHistory?: ComposeActionHistoryEntry[];
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
        dispatch({
          kind: 'loadSucceeded',
          docxBytes: bytes.buffer,
          etag: payload.eTag ?? null,
          sessionId: payload.sessionId ?? '',
          sprkDocumentId: payload.documentRecordId,
          fileName: payload.fileName,
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
  // Word round-trip shuttle (task 103) — push (3.1) / pull (3.4) / reanchor + poll (3.5)
  // -------------------------------------------------------------------------
  // The push-annotations (050), pull-annotations (051), check-changes (053), and reanchor (054)
  // endpoints + the 054 reanchor banner/panel were all BUILT but never CONNECTED. This block wires
  // them: a "Push to Word" toolbar action (3.1), and a return-from-Word poll-on-focus that pulls the
  // current native annotations (3.4) + re-anchors prior anchors (3.5) into the mounted banner/panel.
  const { summary: reanchorSummary, reanchor: runReanchor, reset: resetReanchor } = useComposeReanchor({ bffBaseUrl });
  const { push: pushAnnotations, pushing: isPushingToWord } = useComposePushAnnotations({ bffBaseUrl });
  const { pull: pullAnnotations } = useComposePullAnnotations({ bffBaseUrl });
  const { checkChanges } = useComposeCheckChanges({ bffBaseUrl });

  const [reanchorPanelOpen, setReanchorPanelOpen] = React.useState(false);
  const [pulledAnnotationCount, setPulledAnnotationCount] = React.useState(0);

  // gap 3.1 — the accepted annotations rendered as native Word track-changes + comments.
  const composeDocxAnnotations = React.useMemo(
    () => anchoredAnnotationsToDocxAnnotations(anchoredAnnotations),
    [anchoredAnnotations]
  );
  const canPushToWord =
    (state.status === 'loaded' || state.status === 'saving') &&
    !!state.documentRef?.speDriveItemId &&
    !!effectiveDriveId &&
    !!state.etag &&
    composeDocxAnnotations.length > 0;

  const handlePushToWord = React.useCallback(async (): Promise<void> => {
    if (!state.documentRef?.speDriveItemId || !effectiveDriveId || !state.etag) return;
    if (composeDocxAnnotations.length === 0) return;
    try {
      await pushAnnotations({
        documentSpeId: state.documentRef.speDriveItemId,
        driveId: effectiveDriveId,
        tenantId,
        ifMatch: state.etag,
        annotations: composeDocxAnnotations,
      });
    } catch {
      // The hook surfaces a user-safe error; a push failure must not crash the editing session.
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [
    state.documentRef?.speDriveItemId,
    state.etag,
    effectiveDriveId,
    tenantId,
    composeDocxAnnotations,
    pushAnnotations,
  ]);

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
    } catch {
      // Poll/reanchor failures are non-fatal — the editing session continues.
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [
    state.status,
    state.documentRef?.speDriveItemId,
    effectiveDriveId,
    bffBaseUrl,
    tenantId,
    checkChanges,
    pullAnnotations,
    runReanchor,
    anchoredAnnotations,
  ]);

  React.useEffect(() => {
    if (state.status !== 'loaded') return;
    if (!state.documentRef?.speDriveItemId) return;
    const onFocus = (): void => {
      void runReturnFromWordCheck();
    };
    window.addEventListener('focus', onFocus);
    return () => window.removeEventListener('focus', onFocus);
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
  const triggerSave = React.useCallback(async (): Promise<void> => {
    if (state.status !== 'loaded') return;
    if (!state.documentRef || !editorRef.current) return;

    // FR-05 (task 100): a TRANSIENT (Browse/Upload) draft has NO SPE drive-item — it persists via
    // create-on-save into the client-resolved BU container, not the replace path. Branch on the
    // absence of a real speDriveItemId (mountTransient sets it to '').
    const isTransientCreate = !state.documentRef.speDriveItemId;
    const saveContainerId = state.documentRef.containerId ?? containerIdRef.current;

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
    } else if (!effectiveDriveId) {
      dispatch({
        kind: 'saveFailed',
        errorMessage: 'Cannot save — SPE drive configuration missing.',
      });
      return;
    }

    dispatch({ kind: 'requestSave' });
    try {
      // FR-06a (task 015) upload fidelity branch: an UNEDITED mount must persist the
      // pristine ORIGINAL bytes byte-identical — avoiding the lossy mammoth
      // `docxToTipTapHtml` -> `tipTapToDocxBytes` round-trip for a file the user never
      // touched. `editorRef.current.isDirty()` is the editor's OWN authoritative dirty
      // flag (ComposeEditor's internal `dirtyRef`), read fresh at Save time — this is
      // deliberately NOT the local `isDirty` React state above (which only gates the
      // toolbar button's enabled/disabled visual and can lag behind an in-flight
      // mammoth import; see DEF-01). `state.docxBytes` is the retained ORIGINAL mount
      // bytes: set once by the `loadSucceeded`/`mountTransient` reducer actions (see
      // ComposeWorkspace.types.ts) and never mutated by any other action for the life
      // of the mount, so it is still the pristine upload/load payload at Save time.
      // Only a genuinely dirty editor (or the defensive fallback when, unexpectedly,
      // no original bytes were retained) pays the regeneration cost via the EXISTING
      // `tipTapToDocxBytes` path — that path is otherwise unchanged.
      const editorIsDirty = editorRef.current.isDirty();
      const bytes = editorIsDirty || !state.docxBytes ? await editorRef.current.serialize() : state.docxBytes;

      // FR-05 (task 100): the create-on-save route carries no id in the path (the draft has no
      // drive-item yet) and sends `containerId`; the replace route carries the SPE id + driveId.
      // Both hit ComposeService.SaveAsync — the server branches on DocumentSpeId presence.
      const url = isTransientCreate
        ? `${bffBaseUrl}/api/compose/documents/create-on-save`
        : `${bffBaseUrl}/api/compose/documents/${encodeURIComponent(state.documentRef.speDriveItemId)}/save`;

      // Encode bytes -> base64. ASP.NET Core deserializes byte[] from
      // base64 strings, NOT from JSON number arrays. Iterate rather than
      // spread to avoid call-stack overflow on large documents.
      const view = new Uint8Array(bytes);
      let binary = '';
      for (let i = 0; i < view.length; i++) {
        binary += String.fromCharCode(view[i]);
      }
      const base64Content = btoa(binary);

      const requestBody = isTransientCreate
        ? {
            containerId: saveContainerId,
            tenantId,
            sessionId: state.sessionId,
            content: base64Content,
            displayName: state.documentRef.fileName ?? null,
          }
        : {
            driveId: effectiveDriveId,
            tenantId,
            sessionId: state.sessionId,
            content: base64Content,
            documentRecordId: state.documentRef.sprkDocumentId ?? null,
            displayName: state.documentRef.fileName ?? null,
          };

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
        eTag?: string;
        size: number;
        wasPromotedThisSave: boolean;
      };

      dispatch({
        kind: 'saveSucceeded',
        sprkDocumentId: payload.documentRecordId,
        // gap 1.7: carry the server-minted SPE id back into documentRef.speDriveItemId so a second
        // Save on this mount takes the replace path (no longer transient).
        documentSpeId: payload.documentSpeId,
        etag: payload.eTag ?? null,
      });
      // Clear the local dirty flag so the Save button disables until the
      // next edit. ComposeEditor's internal dirtyRef also resets on the
      // next load; here we mirror that for post-save.
      setIsDirty(false);

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
  }, [
    state.status,
    state.documentRef,
    state.docxBytes,
    state.sessionId,
    bffBaseUrl,
    effectiveDriveId,
    tenantId,
    onCreateOnSaveComplete,
  ]);

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
        if (!response.ok) {
          if (response.status === 404) return; // no compose outputs yet — nothing to materialize
          setComposeDraftError(`Failed to load the drafted content (HTTP ${response.status}).`);
          return;
        }

        const outputs = (await response.json()) as ComposeLedgerOutput[];
        const composeOutputs = Array.isArray(outputs)
          ? outputs.filter(o => o.disposition === 'compose' && o.payload)
          : [];
        if (composeOutputs.length === 0) return;

        const target = targetLedgerRef
          ? composeOutputs.find(o => o.key === targetLedgerRef)
          : composeOutputs.reduce((a, b) => (b.turn > a.turn ? b : a));
        if (!target) return;

        // Idempotent — never double-apply the same stored draft (refresh / duplicate signal).
        if (target.key === lastMaterializedKey) return;

        editor.materializeComposeDraft(target.payload, {
          ledgerRef: target.key, // {bindingId}@t{n} provenance
          bindingId: target.bindingId,
          turn: target.turn,
        });
        setLastMaterializedKey(target.key);
        setComposeDraftError(null);
      } catch (err) {
        const message = err instanceof Error ? err.message : String(err);
        setComposeDraftError(`Failed to insert drafted content: ${message}`);
      }
    },
    [state.status, state.sessionId, bffBaseUrl, lastMaterializedKey]
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
  });

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
  const handleBrowseRequested = React.useCallback((): void => {
    onBrowseRequested?.();
    browseFileInputRef.current?.click();
  }, [onBrowseRequested]);

  const handleBrowseFileSelected = React.useCallback((event: React.ChangeEvent<HTMLInputElement>): void => {
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
      // FR-05 (task 100): carry the host-resolved BU container so the first Save (create-on-save)
      // knows which SPE container to mint the new sprk_document's drive-item in.
      dispatch({ kind: 'mountTransient', docxBytes: result, fileName: file.name, containerId: containerIdRef.current });
      // A freshly Browse-mounted file is unsaved by definition — mark dirty so Save
      // (create-on-save, task 013) is enabled immediately.
      setIsDirty(true);
    };
    reader.onerror = () => {
      dispatch({
        kind: 'loadFailed',
        errorMessage: `Failed to read "${file.name}". The file may be corrupted or unreadable.`,
      });
    };
    reader.readAsArrayBuffer(file);
  }, []);

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
        };

        // ASP.NET Core serializes byte[] as a base64 string (NOT a JSON number
        // array) — decode with atob(), mirroring the Load effect above.
        const binary = atob(payload.content ?? '');
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) {
          bytes[i] = binary.charCodeAt(i);
        }
        if (ac.signal.aborted) return;

        // An upload mount has no SPE drive — clear any stale Search-resolved drive id
        // (FR-02/task 011) so a later Save doesn't key off the WRONG drive.
        setSearchResolvedDriveId(null);
        dispatch({
          kind: 'mountTransient',
          docxBytes: bytes.buffer,
          fileName: payload.fileName ?? uploadRef.fileName,
          // FR-05 (task 100): thread the host-resolved BU container for create-on-save.
          containerId: containerIdRef.current,
        });
        // A freshly-mounted upload is unsaved by definition — mark dirty so Save
        // (create-on-save, task 013) is enabled immediately.
        setIsDirty(true);
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

  // FR-05 (task 100, gap 1.5): a transient (Browse/Upload) draft has no SPE pointer yet — the Save
  // button must be enabled for it (create-on-save) even though its editor dirty flag is false for
  // an unedited mount (FR-06a keeps the original bytes). A draft exists whenever the workspace is
  // showing an editor for a documentRef that has no real speDriveItemId.
  const hasTransientDraft =
    (state.status === 'loaded' || state.status === 'saving') &&
    !!state.documentRef &&
    !state.documentRef.speDriveItemId;

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
          <ComposeEmptyState onBrowseRequested={handleBrowseRequested} onSearchRequested={handleSearchRequested} />
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
          <div className={styles.toolbarSlot}>
            <ComposeToolbar
              documentId={toolbarDocumentId}
              bffBaseUrl={bffBaseUrl}
              disabled={state.status === 'saving'}
              onSaveRequested={() => {
                void triggerSave();
              }}
              isDirty={isDirty}
              hasTransientDraft={hasTransientDraft}
              isSaving={state.status === 'saving'}
              // gap 3.1 — only offer Push-to-Word for a persisted document (a transient draft has
              // no SPE drive-item to write native annotations into yet).
              onPushToWordRequested={
                state.documentRef?.speDriveItemId
                  ? () => {
                      void handlePushToWord();
                    }
                  : undefined
              }
              canPushToWord={canPushToWord}
              isPushingToWord={isPushingToWord}
            />
          </div>

          {/* gap 3.5 — return-from-Word re-anchor summary banner (task 054 component, mounted here). */}
          <ComposeReanchorBanner
            summary={reanchorSummary}
            onReview={() => setReanchorPanelOpen(true)}
            onDismiss={resetReanchor}
          />

          {/* Banner stack — errors / warnings / checkout status / assistant pending */}
          <ComposeBannerStack
            errorMessage={state.errorMessage}
            checkoutStatus={state.checkoutStatus}
            checkoutLockedBy={state.checkoutLockedBy}
            checkoutFailureMessage={state.checkoutFailureMessage}
            importWarnings={state.importWarnings}
            pendingAssistantInsert={state.pendingAssistantInsert}
          />

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

          <div className={styles.editorSlot}>
            <ComposeEditor
              ref={editorRef}
              docxBytes={state.docxBytes}
              documentRef={editorDocRef}
              bffBaseUrl={bffBaseUrl}
              sessionId={state.sessionId}
              onDirtyChange={handleDirtyChange}
              onImportWarnings={handleImportWarnings}
              enqueueComposeAction={enqueueComposeAction}
            />
          </div>
        </>
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
