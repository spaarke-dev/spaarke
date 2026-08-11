/**
 * WorkspacePane.tsx — Center pane for the SpaarkeAi three-pane shell (R2).
 *
 * Subscribes to the 'workspace' PaneEventBus channel via usePaneEvent and
 * delegates all tab lifecycle work to WorkspaceTabManager. Widget components
 * are resolved lazily from WorkspaceWidgetRegistry — no widget code is bundled
 * at shell startup.
 *
 * Handled PaneEventBus events:
 *   workspace / widget_load       — add new tab, resolve widget component, activate tab
 *   workspace / widget_update     — update existing tab's data payload
 *   workspace / widget_action     — forward action to the active tab's widget via ref
 *   conversation / playbook-selected — clear tabs (if exclusive) + seed defaultWidgets (AIPU2-102)
 *
 * Dispatched PaneEventBus events:
 *   workspace / tab_change       — emitted when the active tab changes so
 *                                  ContextPaneController can adapt its view
 *   workspace / tab_count_change — emitted when the number of open tabs changes
 *                                  so ShellStageManager can drive Stage 3↔4
 *
 * This component replaces R1's OutputPanel.tsx.
 *
 * @see WorkspaceTabManager    — tab state management (plain TS class)
 * @see WorkspaceTabManagerComponent — tab bar + active widget renderer
 * @see resolveWorkspaceWidget — lazy widget registry
 * @see ADR-021 — Fluent v9 tokens only, dark mode, no hardcoded colors
 * @see ADR-022 — React 19, functional components
 */

import * as React from 'react';
import { makeStyles, tokens, Spinner } from '@fluentui/react-components';
import { AppsListRegular } from '@fluentui/react-icons';
import {
  PaneHeader,
  createXrmDataService,
  createXrmNavigationService,
  searchUsersAndContacts,
  ANALYSIS_REGARDING_TARGETS,
  resolveAnalysisFilePreview,
} from "@spaarke/ui-components";
import type { AssociationResult } from "@spaarke/ui-components";
import {
  usePaneEvent,
  useDispatchPaneEvent,
  resolveWorkspaceWidget,
  getWorkspaceWidgetMetadata,
  useAiSession,
  CreateAnalysisWizardWidget,
} from '@spaarke/ai-widgets';
import type {
  WorkspacePaneEvent,
  ConversationPaneEvent,
  CreateAnalysisWizardData,
} from '@spaarke/ai-widgets';
// R6 Hotfix Wave B-G9c2 (2026-06-10): the previously-eager Summary tab
// auto-install (R5 task 038) was removed. Each summarize invocation now
// dispatches its own `workspace.widget_load` carrying the structured-
// output-stream widget type + SUMMARIZE_SCHEMA + a per-run correlationId.
// Those symbols are no longer needed at this site; capability dispatchers
// (the shared dispatchConsumer helper via its `workspaceTarget` arg +
// FilePreviewContextWidget.dispatchSummarizeOnly) own them now.
import { buildBffApiUrl } from '@spaarke/auth';
import { usePaneCollapseContext, useComposeLaunch, useAnalysisLaunch } from '../shell/ThreePaneShell';
// R3 ("Visible to assistant") — deep-import the cross-pane bridge hook (not the
// `@spaarke/compose-components` barrel) so this workspace-pane module does NOT transitively pull the
// TipTap editor widgets — mirrors ConversationPane's deep-import rationale. Resolves in Vite + jest.
import { useComposeVisibility } from '@spaarke/compose-components/context/composeActionBridge';
import { WorkspaceTabManager } from './WorkspaceTabManager';
import type {
  ActiveTabSnapshot,
  WorkspaceTabManagerState,
  WorkspaceTabPersistenceSnapshot,
} from './WorkspaceTabManager';
import { WorkspaceTabManagerComponent } from './WorkspaceTabManagerComponent';
import { WorkspacePaneMenu } from './WorkspacePaneMenu';
// spaarkeai-compose-r2 UNIFY — the DIRECT 'compose' widget's seed shape. Used to
// build the ribbon composeMode=editor launch seed (stored-doc pointer) that the
// workspace handler's 'compose' branch consumes.
import type { ComposeWidgetSeed } from './composeWidgetData';
// spaarkeai-assistant-enhancements-r2 Phase 0 Fix 2 — derive a Compose tab's
// short display label + full-filename tooltip from the loaded document, so
// multiple open Compose tabs are distinguishable in the tab strip instead of
// all reading "Compose".
import { deriveComposeTabLabel } from './composeTabLabel';
import {
  logTelemetryError,
  TELEMETRY_TAB_RESTORE_LOAD_FAILURE,
  TELEMETRY_TAB_RESTORE_SAVE_FAILURE,
  TELEMETRY_UI_ACTION_ACK_FAILURE,
} from '../../telemetry/errorTelemetry';
import { getPinnedWorkspaces, prunePinnedToKnown } from '../../services/pinnedWorkspaces';
// ai-advanced-capabilities-analysis-hub-r1: headless analysis open (grid row-click →
// openSpaarkeAi target:2). Owns the code-page modal primitive (ADR-039 code-routing).
import { openSpaarkeAi } from '../../utils/launch-resolver';
// Wave 2b (task 109): the cold-load default tab is now driven by
// useWorkspaceLayouts().activeLayout (the BFF's discovered default — Daily
// Briefing in dev) instead of a hard-coded Home tab. See the auto-install
// effect below for the dispatch path.
import { useWorkspaceLayouts } from '../../hooks/useWorkspaceLayouts';
// UAT round-5 item #13 (workspace-tab persistence + resume): durable localStorage
// persistence for the UNBOUND home-surface Compose tab(s) + their in-flight review
// runs — the one surface `tabAnchorKeyForContext` (entityContext-keyed) and the
// BFF `/tabs` store (chatSessionId-keyed) both decline to cover. See the module
// header for the exact gap + design.
import {
  readComposeRunState,
  writeComposeRunState,
  upsertPersistedComposeTab,
  removePersistedComposeTab,
  clearRunInFlight,
  clearRunInFlightBySession,
  isRunResumable,
  hasFindings,
  findLatestFindingsPayload,
  withComposeSessionId,
  COMPOSE_RUN_IN_FLIGHT_MAX_MS,
  type ComposeLedgerOutputLike,
} from './composeRunPersistence';
// UAT round-5 item #13 (resume poll): reuse the SHIPPED live-completion projection
// (conversation/**) so a polled compose-outputs ledger entry materializes findings
// through the EXACT same `compose_advisory_comments` receiver the live path uses —
// no re-implemented projection (§11 reuse).
import { projectFlaggedSectionsToAdvisoryComments } from '../conversation/useNdaReviewAdvisoryCommentsBridge';

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    width: '100%',
    overflow: 'hidden',
    backgroundColor: tokens.colorNeutralBackground2,
  },

  // ── First-paint / empty-state placeholder. Wave 2b (task 109) — used in
  //    two cases now: (a) the brief window between mount and the
  //    auto-install-default effect dispatching the default workspace tab,
  //    and (b) when the BFF returns NO default (cascade step 4) — the user
  //    sees an empty pane and can pick from the Workspaces dropdown.
  firstPaintPlaceholder: {
    flex: 1,
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
  },
});

// ---------------------------------------------------------------------------
// Analysis-anchored tab persistence (task 025 / spec FR-09)
// ---------------------------------------------------------------------------
//
// Tab persistence (NFR-09, task 065) was previously gated entirely on
// `chatSessionId`: the save effect no-op'd and the restore effect settled
// immediately whenever no chat session existed yet. A pre-session tab (e.g.
// the ribbon "compose" launch, which opens a workspace tab before the user
// has sent a first chat message) therefore had NO durable store to persist
// against and was silently lost on refresh — a data-loss bug (task 025
// negative acceptance criterion).
//
// `entityContext` (the Analysis record this ThreePaneShell/WorkspacePane
// instance is bound to) is known at mount — well before any chat session is
// minted. Anchoring a fast, always-available localStorage cache on it closes
// the gap without a new BFF endpoint or server contract change (§11
// Component Justification: extends the SAME per-host-context localStorage
// keying convention `AiSessionProvider.chatSessionKeyForContext` already
// established for `chatSessionId`/`playbookId`). The BFF per-session
// PATCH/GET remains the durable, cross-device store once a session exists;
// this is an additive client-only fallback layer, not a replacement.
const TAB_ANCHOR_KEY_PREFIX = 'sprk_ai2_workspaceTabs';

interface TabAnchorEntityContext {
  entityType?: string;
  entityId?: string;
}

/**
 * Derive the localStorage key anchoring this Analysis (or other host entity)
 * record's workspace-tab snapshot. Returns null when no entity context is
 * bound (e.g. the unbound home surface) — there is no stable anchor to key
 * on in that case, and the existing chatSessionId-gated BFF path is the only
 * persistence available.
 */
export function tabAnchorKeyForContext(entityContext: TabAnchorEntityContext | null | undefined): string | null {
  if (!entityContext?.entityType || !entityContext?.entityId) return null;
  return `${TAB_ANCHOR_KEY_PREFIX}__${entityContext.entityType.toLowerCase()}:${entityContext.entityId.toLowerCase()}`;
}

/** Safe localStorage read — returns null on parse failure or unavailable storage. */
function readLocalTabSnapshot(key: string): WorkspaceTabPersistenceSnapshot | null {
  try {
    const raw = localStorage.getItem(key);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as WorkspaceTabPersistenceSnapshot;
    if (!parsed || !Array.isArray(parsed.tabs)) return null;
    return parsed;
  } catch {
    return null;
  }
}

/** Safe localStorage write — silently swallows quota/security errors (best-effort). */
function writeLocalTabSnapshot(key: string, snapshot: WorkspaceTabPersistenceSnapshot): void {
  try {
    localStorage.setItem(key, JSON.stringify(snapshot));
  } catch {
    /* localStorage may be unavailable (quota / private mode) — best-effort only. */
  }
}

// ---------------------------------------------------------------------------
// Compose instance-key derivation (spaarkeai-compose-r2 — multi-Compose-tab)
// ---------------------------------------------------------------------------

/**
 * Derive a STABLE per-document instance key from a compose open's `widgetData`.
 *
 * Compose is no longer a hard singleton: a DIFFERENT document opens a NEW tab, while the SAME
 * document reuses its existing tab. This key is the identity used to decide reuse-vs-new. It
 * prefers a durable id over a filename per source door:
 *   - draft   → `draft:<bindingId>` — the ledgerRef's `@t<turn>` suffix is STRIPPED so successive
 *               turns of the SAME drafting binding (e.g. `b1@t1`, `b1@t2`) map to ONE tab
 *               (DEF-08 single-tab reuse across re-drafts).
 *   - upload  → `upload:<sessionFileId>`
 *   - stored  → `stored:<speDriveItemId>` (or `<sprkDocumentId>` fallback)
 *   - name    → `name:<fileName>` — only when no stable id exists.
 *
 * Returns `undefined` when there is no `compose` seed at all (a source-only re-activation), OR the
 * seed carries no identifiable document (a Part-B inline-html draft, or an empty `{ upload: {} }`
 * / blank open) — the caller distinguishes those cases (source-only reuse vs blank new tab).
 *
 * The key is RE-DERIVED from the existing tab's persisted `compose` seed on every reuse decision
 * (see {@link composeTabInstanceKey}) rather than stored on `widgetData` — that keeps the seed
 * clean (its shape is asserted verbatim by tests + consumed by `buildLaunchFromSeed`) and Just
 * Works for tabs restored from persistence, which never carried a stored key.
 */
export function deriveComposeInstanceKey(widgetData: unknown): string | undefined {
  if (widgetData === null || typeof widgetData !== 'object') return undefined;
  const compose = (widgetData as { compose?: unknown }).compose;
  if (compose === null || typeof compose !== 'object') return undefined;
  const c = compose as {
    draft?: { ledgerRef?: string; fileName?: string; html?: string };
    upload?: { sessionFileId?: string; fileName?: string };
    speDriveItemId?: string;
    sprkDocumentId?: string;
    fileName?: string;
  };

  if (typeof c.draft?.ledgerRef === 'string' && c.draft.ledgerRef.length > 0) {
    // `<bindingId>@t<turn>` → strip the per-turn suffix so re-drafts of the SAME binding reuse.
    return `draft:${c.draft.ledgerRef.replace(/@t\d+$/i, '')}`;
  }
  if (typeof c.upload?.sessionFileId === 'string' && c.upload.sessionFileId.length > 0) {
    return `upload:${c.upload.sessionFileId}`;
  }
  if (typeof c.speDriveItemId === 'string' && c.speDriveItemId.length > 0) {
    return `stored:${c.speDriveItemId}`;
  }
  if (typeof c.sprkDocumentId === 'string' && c.sprkDocumentId.length > 0) {
    return `stored:${c.sprkDocumentId}`;
  }
  const fn = c.upload?.fileName ?? c.draft?.fileName ?? c.fileName;
  if (typeof fn === 'string' && fn.length > 0) {
    return `name:${fn}`;
  }
  // A compose object with no durable identity (Part-B inline html, or an empty seed) → no key.
  return undefined;
}

/** The document instance key of an existing compose tab, re-derived from its persisted seed. */
function composeTabInstanceKey(tab: { widgetData?: unknown }): string | undefined {
  return deriveComposeInstanceKey(tab.widgetData);
}

// ---------------------------------------------------------------------------
// WorkspacePane
// ---------------------------------------------------------------------------

/**
 * WorkspacePane — center pane for the SpaarkeAi three-pane shell (R2).
 *
 * Owns the WorkspaceTabManager instance and drives React state from it.
 * Delegates tab bar rendering and active widget display to
 * WorkspaceTabManagerComponent.
 */
export function WorkspacePane(): React.JSX.Element {
  const styles = useStyles();
  const dispatch = useDispatchPaneEvent();

  // FIX #6 (spaarkeai-compose-r2) — the cross-pane conduit into the Compose editor's active-document
  // register/withdraw. Null when no Compose editor is registered (no Compose tab open / standalone).
  // Driven from the tab-activation effect below (visible=true when the Compose tab is active,
  // visible=false when a non-compose tab is active) — replacing the removed manual toggle.
  const composeVisibility = useComposeVisibility();

  // ---------------------------------------------------------------------------
  // Auth surface — NFR-09 tab persistence (task 065)
  //
  // Per ADR-028: `authenticatedFetch` is obtained from useAiSession() (never
  // snapshotted as a prop or token string). `bffBaseUrl` + `chatSessionId`
  // also come from the session provider so write-through targets the correct
  // session and we can no-op cleanly when no session id is set yet.
  // ---------------------------------------------------------------------------

  const { bffBaseUrl, authenticatedFetch, chatSessionId, isAuthenticated, entityContext } = useAiSession();

  // ---------------------------------------------------------------------------
  // Analysis entry-matrix launch (task 050 — spec §12 / FR-14)
  //
  // Published by ThreePaneShell from the URL params (openSpaarkeAi analysis
  // deep-link, ADR-039 CODE path — NOT surfaceLaunchRegistry). Null unless the
  // app was launched into an analysis entry mode. Drives the analysis
  // auto-install effect below (hub for 'new'; existing analysis for 'existing').
  // ---------------------------------------------------------------------------
  const analysisLaunch = useAnalysisLaunch();

  // ---------------------------------------------------------------------------
  // Host-coupled Dataverse services for the Analysis creation wizard (task 050
  // thread 1 — spec FR-12/FR-14).
  //
  // `create-analysis-wizard` (@spaarke/ai-widgets, task 040) is context-agnostic
  // by design (ADR-012): it accepts `dataService`/`navigationService`/`searchUsers`
  // as injected props and MUST NOT construct Xrm-coupled services itself. The
  // SpaarkeAi SOLUTION is the correct layer to resolve them from the Xrm host
  // context — the SAME `createXrmDataService()` / `createXrmNavigationService()` /
  // `searchUsersAndContacts()` factories ConversationPane already uses (per
  // DATA-ACCESS-DECISION-CRITERIA: host-context Xrm.WebApi, no BFF/OBO). Injected
  // into the wizard's `widgetData` in the workspace `widget_load` handler below, so
  // EVERY dispatcher of that type (the hub Agreement Review card in 2a, the
  // record-driven modal in 2b) gets a fully-wired wizard — closing the interim
  // "Connecting to workspace services…" gap task 030 deferred here.
  // ---------------------------------------------------------------------------
  const analysisWizardDataService = React.useMemo(() => createXrmDataService(), []);
  const analysisWizardNavigationService = React.useMemo(() => createXrmNavigationService(), []);
  const analysisWizardSearchUsers = React.useCallback(
    (query: string) => searchUsersAndContacts(analysisWizardDataService, query),
    [analysisWizardDataService]
  );

  // SPE container resolver for the Analysis wizard's file upload (UAT #7 fix,
  // 2026-07-30). The wizard's `onFinish` throws "No storage container is configured
  // for your business unit" when `speContainerId` is empty — the root cause was that
  // the modal host never supplied a `resolveSpeContainerId`. This is the SAME
  // current-user → business-unit → `sprk_containerid` chain every shipped Create
  // wizard uses (useWizardPageBootstrap.ts / the Create* code pages). Pure Dataverse
  // Web API via the Xrm host global (no BFF/OBO); the GUID-strip on userId is required.
  const resolveAnalysisSpeContainerId = React.useCallback(async (): Promise<string> => {
    /* eslint-disable @typescript-eslint/no-explicit-any */
    const xrm: any =
      (window as any).Xrm ?? (window.parent as any)?.Xrm ?? (window.top as any)?.Xrm;
    /* eslint-enable @typescript-eslint/no-explicit-any */
    if (!xrm?.WebApi?.retrieveRecord) throw new Error("Xrm.WebApi not available");
    const userId: string = xrm.Utility.getGlobalContext().userSettings.userId.replace(/[{}]/g, "");
    const user = await xrm.WebApi.retrieveRecord("systemuser", userId, "?$select=_businessunitid_value");
    const buId = user["_businessunitid_value"] as string;
    if (!buId) throw new Error("Could not resolve business unit");
    const bu = await xrm.WebApi.retrieveRecord("businessunit", buId, "?$select=sprk_containerid");
    return (bu["sprk_containerid"] as string) || "";
  }, []);

  // ---------------------------------------------------------------------------
  // Create Analysis wizard MODAL host (ai-advanced-capabilities-analysis-hub-r1
  // tabbed Quick Start). The Quick Start "Agreement Review" card dispatches
  // `open_create_analysis_wizard`; we host `CreateAnalysisWizardWidget` here as a
  // MODAL (`embedded: false`) — it does NOT take a workspace tab, and on finish it
  // opens its result tab exactly as today. WorkspacePane already injects the
  // wizard's Xrm services (above) and is where the result tab lands, so it is the
  // correct host.
  // ---------------------------------------------------------------------------
  const [createAnalysisModal, setCreateAnalysisModal] = React.useState<{
    open: boolean;
    workTypeValue?: number;
    workTypeLabel?: string;
  }>({ open: false });

  // Regarding pre-set: resolve from the host entityContext (the record the
  // SpaarkeAi surface was launched in — e.g. a Matter/Project modal). Mirrors the
  // logic the retired AnalysisHub cards used; only the supported ADR-024 targets
  // (Matter/Project/Document) force a regarding. Absent/unsupported host → no
  // forced regarding (the wizard opens with an empty, user-editable lookup).
  const analysisInitialAssociation = React.useMemo<AssociationResult | undefined>(() => {
    const entityType = entityContext?.entityType;
    const recordId = entityContext?.entityId;
    if (!entityType || !recordId) return undefined;
    const supported = ANALYSIS_REGARDING_TARGETS.some(t => t.entityType === entityType);
    if (!supported) return undefined;
    return { entityType, recordId, recordName: recordId };
  }, [entityContext?.entityType, entityContext?.entityId]);

  // Task 025 (FR-09): the Analysis-record anchor tab persistence keys on — see the
  // module-scope block above. Memoized on the entity identity fields (entityContext
  // itself is a fresh object per AiSessionProvider render).
  const tabAnchorKey = React.useMemo(
    () => tabAnchorKeyForContext(entityContext),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [entityContext?.entityType, entityContext?.entityId]
  );

  // ---------------------------------------------------------------------------
  // Tab manager — single instance per WorkspacePane mount
  // ---------------------------------------------------------------------------

  // Forwarding ref: the manager's onPersistChange callback dereferences this
  // on every mutation. The actual `persistTabs` function below is rebuilt with
  // useCallback (it captures sessionId/bffBaseUrl) and assigned into the ref
  // on each render — so the manager always calls the latest persistTabs.
  const persistTabsRef = React.useRef<((snapshot: WorkspaceTabPersistenceSnapshot) => void) | null>(null);

  // Round 4 Fix 4: Forwarding ref for the active-tab-change signal. Same
  // pattern as persistTabsRef — keeps the manager construction stable while
  // letting the dispatch closure capture the latest `dispatch` reference.
  const activeTabChangeRef = React.useRef<((snapshot: ActiveTabSnapshot) => void) | null>(null);

  // Stable manager reference — never recreated across re-renders.
  // The onPersistChange / onActiveTabChange callbacks are themselves stable;
  // they just dispatch through the current ref values (so updates to deps
  // refresh cleanly without re-instantiating the manager).
  const managerRef = React.useRef<WorkspaceTabManager>(
    new WorkspaceTabManager({
      onPersistChange: snapshot => {
        persistTabsRef.current?.(snapshot);
      },
      onActiveTabChange: snapshot => {
        activeTabChangeRef.current?.(snapshot);
      },
    })
  );

  // React state mirrors the manager's snapshot; triggers re-renders.
  const [tabState, setTabState] = React.useState<WorkspaceTabManagerState>(() => managerRef.current.getSnapshot());

  // UAT round-4 (item #10a): true while a review whose progress modal was dismissed ("Continue working
  // in background") is STILL running server-side. Fed purely by the additive
  // `nda_review_background_run` broadcast (useNdaReviewRunProgress, Assistant pane); drives the tiny
  // circular progress indicator on the running Compose tab header (WorkspaceTabManagerComponent). Goes
  // false when the run reaches a terminal state - completion then flows through the existing
  // ReviewCompleteToast rules, unchanged.
  const [composeReviewRunningInBackground, setComposeReviewRunningInBackground] = React.useState(false);

  /** Sync React state with the current manager snapshot. */
  const syncState = React.useCallback((): void => {
    setTabState(managerRef.current.getSnapshot());
  }, []);

  // ---------------------------------------------------------------------------
  // Debounced write-through — NFR-09 (task 065)
  //
  // The manager fires onPersistChange synchronously on every mutation. We
  // coalesce rapid bursts (e.g. FIFO eviction adding + removing) by buffering
  // the latest snapshot in a ref and flushing once per ~200ms tick. The
  // write-through is best-effort: on failure we log telemetry and continue
  // (in-memory state remains correct, restore on next mount may be stale).
  // ---------------------------------------------------------------------------

  const pendingSnapshotRef = React.useRef<WorkspaceTabPersistenceSnapshot | null>(null);
  const persistTimerRef = React.useRef<number | null>(null);

  const persistTabs = React.useCallback(
    (snapshot: WorkspaceTabPersistenceSnapshot): void => {
      pendingSnapshotRef.current = snapshot;
      if (persistTimerRef.current !== null) {
        window.clearTimeout(persistTimerRef.current);
      }
      persistTimerRef.current = window.setTimeout(async () => {
        persistTimerRef.current = null;
        const snap = pendingSnapshotRef.current;
        pendingSnapshotRef.current = null;
        if (!snap) return;

        // Task 025 (FR-09): anchor persistence on the Analysis record — NOT gated on
        // chatSessionId — so a pre-session/ribbon-compose tab survives a refresh. Always
        // write the local anchor-keyed cache; the BFF PATCH below remains additive once a
        // durable session exists (cross-device store-of-record for that session).
        if (tabAnchorKey) {
          writeLocalTabSnapshot(tabAnchorKey, snap);
        }

        if (!chatSessionId || !bffBaseUrl || !isAuthenticated) return;

        try {
          const url = buildBffApiUrl(bffBaseUrl, `/ai/chat/sessions/${encodeURIComponent(chatSessionId)}/tabs`);
          const response = await authenticatedFetch(url, {
            method: 'PATCH',
            headers: {
              'Content-Type': 'application/json',
              Accept: 'application/json',
            },
            body: JSON.stringify(snap),
          });
          // 404 = session not yet known to BFF — treat as benign (best-effort).
          if (!response.ok && response.status !== 404) {
            throw new Error(`HTTP ${response.status}`);
          }
        } catch (err) {
          logTelemetryError(TELEMETRY_TAB_RESTORE_SAVE_FAILURE, {
            sessionId: chatSessionId,
            message: err instanceof Error ? err.message : String(err),
          });
          // Continue — write-through is best-effort. In-memory state is the
          // source of truth until the next successful save.
        }
      }, 200);
    },
    [chatSessionId, tabAnchorKey, bffBaseUrl, isAuthenticated, authenticatedFetch]
  );

  // Update the forwarding ref every render so the manager calls the latest
  // persistTabs (which captures the latest sessionId/bffBaseUrl deps).
  React.useEffect(() => {
    persistTabsRef.current = persistTabs;
  }, [persistTabs]);

  // ---------------------------------------------------------------------------
  // D-F3 UI-action truthfulness — client-ack (FR-A1-08 / task AIR2-037)
  //
  // A server-initiated `widget_load` (no tabId) carrying a `frameId` means the
  // emitting tool call (e.g. SendWorkspaceArtifactHandler's workspace_open_tab
  // frame) is WAITING for this exact ack before its tool result can complete
  // truthfully. Fired ONLY after the tab is actually materialized
  // (manager.addTab below) — never before — so the ack is a genuine confirmation,
  // not a hopeful echo of the frame. Fire-and-forget: on failure the server-side
  // wait simply times out and the tool call reports an honest failure to the
  // model (never a fabricated success) — there is no user-facing retry needed.
  // ---------------------------------------------------------------------------

  const sendUiActionAck = React.useCallback(
    (frameId: string): void => {
      if (!chatSessionId || !bffBaseUrl || !isAuthenticated) return;

      const ackUrl = buildBffApiUrl(bffBaseUrl, `/ai/chat/sessions/${encodeURIComponent(chatSessionId)}/ack`);
      void authenticatedFetch(ackUrl, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Accept: 'application/json',
        },
        body: JSON.stringify({ frameId }),
      }).catch(err => {
        logTelemetryError(TELEMETRY_UI_ACTION_ACK_FAILURE, {
          sessionId: chatSessionId,
          message: err instanceof Error ? err.message : String(err),
        });
      });
    },
    [chatSessionId, bffBaseUrl, isAuthenticated, authenticatedFetch]
  );

  // ---------------------------------------------------------------------------
  // FR-34 D-F3 honest CONTENT-render ack — deferral map (task 071)
  //
  // The base D-F3 loop (above) acks a server `workspace_open_tab` frame the moment
  // the TAB SHELL is materialized (manager.addTab). That is correct for a plain
  // layout open, but WRONG for a CONTENT-bearing open — a chat "open as a document"
  // frame that carries a full-document draft SEED
  // (widgetData.compose.draft.{ledgerRef,sessionId}, DEF-08). For those, the tab
  // opening is NOT the draft rendering: a seed that fails to materialize would still
  // ack tab-open (false "the draft is in the editor" — the exact R2-D fabrication).
  //
  // So for a draft-seeded frame we DEFER: stash the server frame id keyed by the
  // draft's ledgerRef here (instead of acking on tab-open), and fire the ack only
  // when ComposeWorkspace emits `workspace.compose_content_rendered` for that
  // ledgerRef (the editor actually rendered the seeded body). If it never renders,
  // we never ack → the server's WaitForAckAsync times out → honest failure. Plain
  // (non-seeded) opens are UNCHANGED: they still ack on tab-open below.
  const pendingRenderAcksRef = React.useRef<Map<string, string>>(new Map());

  // ---------------------------------------------------------------------------
  // Active-tab signal — Round 4 Fix 4 (2026-05-21)
  //
  // Foundation signal for cross-pane coordination: when the active workspace
  // tab changes, broadcast `active_widget_changed` on the `workspace` channel
  // so future subscribers (Assistant + Context panes) can scope themselves to
  // the active workspace context. NO consumers are wired in this task — this
  // is the signal infrastructure only.
  //
  // The dispatch is mediated by activeTabChangeRef so the WorkspaceTabManager
  // ref stays stable across renders even as `dispatch` evolves.
  // ---------------------------------------------------------------------------

  const broadcastActiveTabChange = React.useCallback(
    (snapshot: ActiveTabSnapshot): void => {
      // Skip events that have no active tab — they're a "no active context"
      // state that subscribers can derive from a separate `session_reset` or
      // `tabs_clear` event when needed.
      if (!snapshot.tabId || !snapshot.widgetType) return;

      dispatch('workspace', {
        type: 'active_widget_changed',
        widgetType: snapshot.widgetType,
        // FR-B1/FR-C3 (task 020): resolve the widget's declared closed
        // contextType from the registry so subscribers (e.g. the FR-A1 focus
        // stamp) can scope to the active tab's surface kind. `undefined` for
        // widgets that declared none (registry lookup miss or omitted field).
        widgetContextType: getWorkspaceWidgetMetadata(snapshot.widgetType)?.contextType,
        widgetData: snapshot.widgetData,
        tabId: snapshot.tabId,
        displayName: snapshot.displayName ?? snapshot.widgetType,
      });
    },
    [dispatch]
  );

  React.useEffect(() => {
    activeTabChangeRef.current = broadcastActiveTabChange;
  }, [broadcastActiveTabChange]);

  // On unmount: cancel any pending timer to avoid late writes against a stale
  // session id. The in-memory snapshot is discarded; the most recent
  // successful write to BFF remains authoritative.
  React.useEffect(() => {
    return () => {
      if (persistTimerRef.current !== null) {
        window.clearTimeout(persistTimerRef.current);
        persistTimerRef.current = null;
      }
    };
  }, []);

  // ---------------------------------------------------------------------------
  // Restore on mount — NFR-09 (task 065)
  //
  // Fetches the persisted tab snapshot for the current chat session and
  // hydrates the manager. 404 is benign (no tabs to restore). Other failures
  // emit telemetry and leave the workspace in its default Home-only state.
  // Guard: restoreFromPersistence() itself no-ops if a non-Home tab is
  // already open, so an in-flight session won't be clobbered if the user
  // opens a tab during the restore window.
  //
  // G-P3 UAT round-3 R3-3 (2026-07-07): `tabRestoreSettled` sequences the
  // auto-install-default and pin auto-open effects AFTER this restore. Before
  // this gate existed, the auto-install effect's `addTab` routinely landed
  // FIRST on refresh (its layouts fetch resolves faster than restore's
  // GET + widget resolution), which (a) made restoreFromPersistence's
  // hasNonHomeTab guard silently no-op — dropping every persisted tab — and
  // (b) fired the debounced PATCH write-through, OVERWRITING the server
  // store with only the fresh default tab (App Insights: SaveTabs
  // tabCount=2 at 16:48:10Z → refresh GET 16:49:05Z → SaveTabs tabCount=1
  // at 16:49:07Z with a freshly-numbered wstab-1-workspace id). Chat-opened
  // (workspace_open_tab bridge) and menu-opened tabs were BOTH persisted
  // correctly — the loss happened entirely on the restore path.
  // ---------------------------------------------------------------------------

  const [tabRestoreSettled, setTabRestoreSettled] = React.useState(false);

  // FR-D1 (spaarkeai-assistant-enhancements-r2) — in-place SESSION SWITCH support.
  //
  // `lastRestoredSessionIdRef` holds the id of the session whose tabs this manager
  // currently reflects. It distinguishes the initial cold-load restore (null → first
  // session — nothing to clear) from a genuine in-place session SWITCH (session A →
  // session B, i.e. reopening a History entry). On a switch the manager still holds
  // the PRIOR session's tabs, so the restore effect below must clear them FIRST —
  // otherwise `restoreFromPersistence()` no-ops (its hasNonHomeTab guard), the prior
  // tabs stay visible on the reopened session AND get PATCHed onto the reopened
  // session's /tabs store (the overwrite hazard FR-D1 fixes).
  const lastRestoredSessionIdRef = React.useRef<string | null>(null);

  // FR-D1: set when a compose `widget_load` ADOPTS a session (the wizard→review
  // hand-off, or the cold-load compose re-adoption — both carry `composeSessionId`
  // and drive `ConversationPane.handleSelectHistorySession` to change chatSessionId
  // to that same session). Those flows intentionally show JUST the adopted document,
  // not the session's full stored tab set (mirroring the analysis-existing skip
  // above), so the session-switch clear below must NOT wipe the just-opened compose
  // tab. This is a precise causal marker written by WorkspacePane's own compose
  // widget_load handler — not a timing heuristic.
  const composeAdoptionSessionRef = React.useRef<string | null>(null);

  // Task 025 (FR-09): the BFF (chatSessionId-keyed) restore remains the durable,
  // cross-device path once a session exists — UNCHANGED below. It is no longer the
  // ONLY restore path: when there is no session yet (pre-session/ribbon-compose tab)
  // OR the BFF has no tabs for this session (404 — e.g. a session freshly promoted
  // from a pre-session tab whose saves only ever reached localStorage so far), this
  // effect falls back to the Analysis-anchored local snapshot (tabAnchorKey) instead
  // of leaving the workspace silently Home-only.
  React.useEffect(() => {
    if (!bffBaseUrl || !isAuthenticated) return;

    // Focused analysis open (headless modal / entry 2c–2d "open existing analysis"): show an
    // INDEPENDENT session with ONLY this analysis loaded — do NOT restore the accumulated
    // workspace tab set (neither the server-session tabs nor the per-Analysis localStorage
    // anchor). The existing-analysis effect below mounts JUST the Compose document; the
    // Assistant pane restores the bound session's conversation via session_switch. Settle
    // immediately so that (tabRestoreSettled-gated) effect can proceed. (Owner UAT 2026-07-31:
    // the headless analysis modal must open independent, not reopen the full workspace tabs.)
    if (analysisLaunch?.mode === 'existing') {
      setTabRestoreSettled(true);
      return;
    }

    // FR-D1 (spaarkeai-assistant-enhancements-r2) — clear-before-restore on an
    // in-place SESSION SWITCH (reopening a History entry via
    // ConversationPane.handleSelectHistorySession → setChatSessionId).
    //
    // A switch is chatSessionId changing FROM a previously-restored session TO a
    // different one. The manager still holds the prior session's tabs;
    // restoreFromPersistence() (below) is a no-op while any non-Home tab exists, so
    // without clearing first (a) the prior session's tabs remain visible on the
    // reopened session and (b) they get PATCHed onto the reopened session's /tabs
    // store — corrupting it (the overwrite hazard). Clear FIRST so the GET+restore
    // below repopulates from the reopened session's OWN store.
    //
    // EXCLUDED — compose adoption: when the switch's target session was just adopted
    // by a compose widget_load (composeAdoptionSessionRef), the just-opened compose
    // tab must survive (the wizard/re-adoption flows show the adopted document, not
    // the session's full stored set). Same intent as the analysis-existing early
    // return above.
    //
    // ESCALATION (POML <escalation>): compose tabs on a real History switch ARE
    // cleared. This does NOT silently discard unsaved work: the compose document is
    // server-authoritative (ADR-049 — OOXML byte-store on the server, TipTap is a
    // lossy view) and, on the home surface, additionally persisted in localStorage
    // (composeRunPersistence — removal is explicit-close only). Clearing the TAB
    // therefore never destroys the document; it is re-restored from the reopened
    // session's own store. No in-flight UNSAVED artifact is silently lost, so the
    // "risk losing an in-flight unsaved tab" trigger is evaluated-and-not-fired (the
    // residual editor-flush-cadence question is surfaced to the orchestrator).
    const isSessionSwitch =
      !!chatSessionId &&
      lastRestoredSessionIdRef.current !== null &&
      lastRestoredSessionIdRef.current !== chatSessionId;
    const isComposeAdoption =
      !!chatSessionId && composeAdoptionSessionRef.current === chatSessionId;
    if (isSessionSwitch) {
      const manager = managerRef.current;
      const priorTabs = manager.getSnapshot().tabs;
      if (!isComposeAdoption) {
        // History reopen — FULL clear (incl. any compose tab; the document survives
        // per the ADR-049 rationale above). Only touch the store if there is
        // something to clear.
        if (priorTabs.some((t) => t.kind === 'widget')) {
          manager.clearAllTabs();
          // clearAllTabs() just scheduled a debounced PATCH of the now-EMPTY tab set
          // against the NEW chatSessionId. Cancel it SYNCHRONOUSLY (before any await
          // below) so an empty set is never written over the reopened session's
          // stored tabs; the GET+restore below repopulates from that same store.
          if (persistTimerRef.current !== null) {
            window.clearTimeout(persistTimerRef.current);
            persistTimerRef.current = null;
          }
          pendingSnapshotRef.current = null;
          syncState();
        }
      } else if (priorTabs.some((t) => t.kind === 'widget' && t.widgetType !== 'compose')) {
        // Compose adoption (Finding 2 guard): the adoption skips the FULL clear so the
        // just-opened compose tab survives — but the manager may still hold NON-compose
        // tabs from the PRIOR session (e.g. the home default layout at a cold-load
        // re-adoption, or whatever the user had open before an in-session wizard→review
        // hand-off). Those do NOT belong to the adopted session and would otherwise
        // ride onto it on the next write-through. Clear them while PRESERVING compose
        // (the adopted document + any compose work-product) — the same preserve
        // semantics the exclusive-playbook path uses. The resulting snapshot
        // ([compose…]) is the correct state for the adopted session, so the debounced
        // write-through is left in place (NOT cancelled) to record it.
        manager.clearAllTabs({ preserveWidgetTypes: ['compose'] });
        syncState();
      }
    }

    let cancelled = false;
    (async () => {
      let restoredFromServer = false;

      if (chatSessionId) {
        try {
          const url = buildBffApiUrl(bffBaseUrl, `/ai/chat/sessions/${encodeURIComponent(chatSessionId)}/tabs`);
          const response = await authenticatedFetch(url, {
            method: 'GET',
            headers: { Accept: 'application/json' },
          });
          if (cancelled) return;

          if (response.ok) {
            const snapshot = (await response.json()) as WorkspaceTabPersistenceSnapshot;
            if (cancelled) return;

            await managerRef.current.restoreFromPersistence(snapshot, resolveWorkspaceWidget);
            if (cancelled) return;

            // restoreFromPersistence no-ops if a non-Home tab already exists (e.g.
            // the user opened a tab during the restore window) — treat that as a
            // successful server restore too, since a widget tab is present either way.
            restoredFromServer = managerRef.current.getSnapshot().tabs.some(t => t.kind === 'widget');
          } else if (response.status !== 404) {
            throw new Error(`HTTP ${response.status}`);
          }
          // 404 falls through to the local anchor-keyed fallback below (benign —
          // no tabs known to the BFF for this session yet).
        } catch (err) {
          if (cancelled) return;
          logTelemetryError(TELEMETRY_TAB_RESTORE_LOAD_FAILURE, {
            sessionId: chatSessionId,
            message: err instanceof Error ? err.message : String(err),
          });
          // Degrade gracefully — fall through to the local anchor-keyed fallback below.
        }
      }

      if (!cancelled && !restoredFromServer && tabAnchorKey) {
        const localSnapshot = readLocalTabSnapshot(tabAnchorKey);
        if (localSnapshot) {
          await managerRef.current.restoreFromPersistence(localSnapshot, resolveWorkspaceWidget);
        }
      }

      if (!cancelled) {
        syncState();

        // Notify ShellStageManager about the restored tab count so it can
        // advance to the appropriate stage (Stage 3 / Stage 4).
        const snap = managerRef.current.getSnapshot();
        dispatch('workspace', {
          type: 'tab_count_change',
          tabCount: snap.tabs.length,
        });

        // FR-D1: record the session we just settled on so a later chatSessionId
        // change is recognized as a switch (clear-before-restore above). Consume the
        // one-shot compose-adoption marker now that this session's restore settled.
        lastRestoredSessionIdRef.current = chatSessionId ?? null;
        if (composeAdoptionSessionRef.current === chatSessionId) {
          composeAdoptionSessionRef.current = null;
        }

        // R3-3: settle on EVERY terminal path (server success, local fallback,
        // no anchor at all, or error) so the auto-install-default + pin
        // auto-open effects below can proceed.
        setTabRestoreSettled(true);
      }
    })();

    return () => {
      cancelled = true;
      // FR-D1 (Finding 1 — leak-proof marker): the compose-adoption marker is a
      // one-shot consumed at settle (above). If THIS run was a compose adoption whose
      // restore is CANCELLED before it settles (the user switches again before the
      // async GET resolves), the settle-consume never runs and the marker would leak —
      // a later genuine History reopen of this same session would then see
      // isComposeAdoption===true, SKIP the overwrite clear, and let the prior session's
      // tabs corrupt it. Reset the marker here (guarded to THIS run's session so a
      // newer adoption's marker for a DIFFERENT session is never wiped) so a cancelled
      // adoption can't suppress a later clear.
      if (composeAdoptionSessionRef.current === chatSessionId) {
        composeAdoptionSessionRef.current = null;
      }
    };
    // authenticatedFetch is a stable module-level function from @spaarke/auth
    // (returned by useAiSession() but identical reference across renders).
    // Including it in deps would re-fire the effect needlessly.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [chatSessionId, tabAnchorKey, bffBaseUrl, isAuthenticated, analysisLaunch?.mode]);

  // ---------------------------------------------------------------------------
  // Auto-install default workspace tab — Wave 2b (task 109)
  //
  // The hard-coded "Home" tab (formerly installed via
  // WorkspaceTabManager.ensureHomeTab + WorkspaceHomeTab) is GONE. Round 8
  // operator decision Option B: architectural unity — every workspace
  // (Corporate Workspace, the 4 Wave 2a Dataverse-seeded system layouts,
  // and user-created layouts) flows through the same `widget_load →
  // WorkspaceLayoutWidget → LegalWorkspaceApp(embedded) → section factories`
  // pipeline. The cold-load tab is therefore the BFF's discovered default
  // (typically "Daily Briefing" in dev, per Wave 2a's seed) — NOT a code-
  // local Home tab.
  //
  // The BFF's GetDefaultLayoutAsync cascade (task 109 BFF changes):
  //   1. Per-user default (user's customized choice)
  //   2. Dataverse system default (sprk_issystem=true + sprk_isdefault=true)
  //   3. Hard-coded system flagged as global default (forward-compat path)
  //   4. null — no default; we render an empty pane.
  //
  // Coordination with task 101's pin auto-open (effect declared below):
  //   - If the resolved default is in the pinned list, this effect SKIPS the
  //     dispatch and lets the pin auto-open handle it (the pin loop opens
  //     pinned workspaces in their persisted order; the default IS opened
  //     because it's pinned).
  //   - If the default is NOT pinned, this effect dispatches it independently
  //     as the first tab.
  //
  // Subscription-race fix carried forward from task 101: defer the dispatch
  // to a macrotask via setTimeout(..., 0). The usePaneEvent('workspace', ...)
  // subscription below is registered in its own useEffect that runs AFTER
  // this one in React's commit order; without the macrotask deferral the
  // dispatch lands on a zero-subscriber channel and is silently dropped.
  //
  // Step 4 fallback: if activeLayout is null (BFF returned null OR no
  // layouts at all), do NOT install any tab. The user sees an empty workspace
  // pane and can pick from the Workspaces dropdown. This is acceptable — no
  // default tab is the correct UX when the system has no default to offer.
  // ---------------------------------------------------------------------------

  const { activeLayout, layouts } = useWorkspaceLayouts({
    bffBaseUrl,
    authenticatedFetch,
    isAuthenticated,
  });

  // spaarkeai-compose-r2 UNIFY (completes the R1 flip): when App.tsx was
  // launched with `composeMode=editor` (ribbon Open-in-Compose modal path), we
  // NO LONGER install the "Compose" workspace LAYOUT tab (widgetType
  // 'workspace'). Instead the compose-launch auto-install effect below opens a
  // DIRECT 'compose' widget tab (widgetType 'compose'), so EVERY Compose mount
  // is protected by the keep-mounted-hidden keep-alive
  // (WorkspaceTabManagerComponent) and never unmounts on a tab switch (the
  // transient/Browse doc survives). Non-compose default layouts (Daily
  // Briefing, dashboards, …) still flow through the 'workspace' LAYOUT door.
  const composeLaunch = useComposeLaunch();
  const isComposeLaunch = composeLaunch?.composeMode === 'editor';

  // Build the DIRECT-widget seed from the launch context's stored document.
  // main.tsx parses the ribbon URL params (sprkDocumentId / speDriveItemId /
  // speDriveId / speFileName) into `composeLaunch.document` + `.driveId`; we map
  // them onto the stored-document door of the compose seed. An empty seed (no
  // document — should not happen for the ribbon path, which always carries a
  // stored doc) opens the Compose empty state.
  const composeLaunchSeed = React.useMemo<ComposeWidgetSeed>(() => {
    if (!isComposeLaunch) return {};
    const doc = composeLaunch?.document ?? null;
    const driveId = composeLaunch?.driveId ?? '';
    const seed: ComposeWidgetSeed = {};
    if (doc?.speDriveItemId) seed.speDriveItemId = doc.speDriveItemId;
    if (doc?.sprkDocumentId) seed.sprkDocumentId = doc.sprkDocumentId;
    if (driveId) seed.speDriveId = driveId;
    if (doc?.fileName) seed.fileName = doc.fileName;
    return seed;
  }, [isComposeLaunch, composeLaunch]);

  // The NON-compose default layout still auto-installs through the 'workspace'
  // LAYOUT door (unchanged). In compose-launch mode the layout auto-install
  // effect early-returns so we don't open the BFF default BEHIND the Compose
  // tab — the compose-Direct effect owns the mount.
  const layoutForAutoInstall = activeLayout;

  const autoInstalledDefaultRef = React.useRef<boolean>(false);
  React.useEffect(() => {
    if (!isAuthenticated) return;
    // spaarkeai-compose-r2 UNIFY: in compose-launch mode the DIRECT 'compose'
    // tab is installed by the dedicated effect below — skip the layout
    // auto-install entirely so we don't ALSO open the BFF default layout behind
    // the Compose tab (the ribbon user must land ONLY on the editor).
    if (isComposeLaunch) return;
    // task 050 (spec §12 / FR-14): in an analysis entry mode the dedicated
    // analysis auto-install effect below owns the cold-load tab (the hub for
    // 'new', the existing analysis for 'existing') — skip the BFF default layout
    // so the analysis user lands ONLY on the analysis surface.
    if (analysisLaunch) return;
    // R3-3 (2026-07-07): wait for the NFR-09 tab restore to settle so the
    // `alreadyOpen` check below sees the RESTORED tabs. Without this gate the
    // default-tab addTab raced restore, no-op'd it (hasNonHomeTab guard) and
    // its write-through overwrote the persisted store — refresh lost every tab.
    if (!tabRestoreSettled) return;
    if (autoInstalledDefaultRef.current) return; // run once per mount
    if (!layoutForAutoInstall) return; // wait for the layout to resolve, or stay empty if null

    // Defer the guard arming until after we actually have a default to
    // process so a transient `layoutForAutoInstall === null` (cold load before
    // fetch resolves) doesn't lock the effect out.
    autoInstalledDefaultRef.current = true;

    const manager = managerRef.current;

    // Skip if this layout is already open (e.g. NFR-09 tab restore brought
    // it back from the last session). Match by widgetData.layoutId.
    const existingTab = manager.getSnapshot().tabs.find(t => {
      if (t.widgetType !== 'workspace') return false;
      const data = t.widgetData as { layoutId?: string } | null;
      return data?.layoutId === layoutForAutoInstall.id;
    });
    if (existingTab) return;

    // Skip if the default is in the pinned list — the pin auto-open effect
    // below will open it; we don't want to double-dispatch.
    const isPinned = getPinnedWorkspaces().some(p => p.layoutId === layoutForAutoInstall.id);
    if (isPinned) return;

    // Defer to a macrotask so usePaneEvent's subscription effect (declared
    // later in this component) has registered. Identical pattern to the pin
    // auto-open effect below — see that effect's block comment for the
    // subscription-race rationale.
    const timerId = window.setTimeout(() => {
      // eslint-disable-next-line no-console
      console.info(
        `[WorkspacePane] Auto-installing default workspace: ${layoutForAutoInstall.name} (${layoutForAutoInstall.id})`
      );
      dispatch('workspace', {
        type: 'widget_load',
        widgetType: 'workspace',
        widgetData: {
          layoutId: layoutForAutoInstall.id,
          layoutName: layoutForAutoInstall.name,
        },
        displayName: layoutForAutoInstall.name,
      });
    }, 0);

    return () => {
      window.clearTimeout(timerId);
    };
    // Run once when auth AND the tab restore AND layoutForAutoInstall are
    // ready; the ref guard prevents re-runs on subsequent dependency changes
    // (e.g. refetch).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isAuthenticated, isComposeLaunch, analysisLaunch, tabRestoreSettled, layoutForAutoInstall]);

  // ---------------------------------------------------------------------------
  // spaarkeai-compose-r2 UNIFY — compose-launch auto-install (DIRECT 'compose')
  //
  // The ribbon composeMode=editor launch now opens a widgetType:'compose' tab
  // (not the "Compose" workspace LAYOUT tab). We dispatch a single 'compose'
  // widget_load carrying the stored-document seed (composeLaunchSeed). The
  // workspace handler's 'compose' branch REUSES the single existing compose tab
  // (activating it — this covers the relaunch/restore case, issue #572 Defect
  // 1d, since a restored compose tab is reused-and-activated) or creates one.
  // Because every Compose mount is now widgetType 'compose', it is covered by
  // the keep-mounted-hidden keep-alive and survives switching to the Email (or
  // any) tab.
  //
  // Same macrotask deferral + tabRestoreSettled gate + run-once ref guard as
  // the layout auto-install effect above.
  // ---------------------------------------------------------------------------
  const autoInstalledComposeRef = React.useRef<boolean>(false);
  React.useEffect(() => {
    if (!isComposeLaunch) return;
    if (!isAuthenticated) return;
    if (!tabRestoreSettled) return;
    if (autoInstalledComposeRef.current) return; // run once per mount
    autoInstalledComposeRef.current = true;

    const timerId = window.setTimeout(() => {
      // eslint-disable-next-line no-console
      console.info('[WorkspacePane] Auto-installing compose (direct widget)');
      dispatch('workspace', {
        type: 'widget_load',
        widgetType: 'compose',
        widgetData: { compose: composeLaunchSeed },
        displayName: 'Compose',
      });
    }, 0);

    return () => {
      window.clearTimeout(timerId);
    };
    // composeLaunchSeed is intentionally omitted from deps — the ref guard runs
    // this once per mount; the seed is stable for the life of a compose launch.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isComposeLaunch, isAuthenticated, tabRestoreSettled]);

  // ===========================================================================
  // UAT round-5 item #13 — home-surface Compose-tab persistence + resume
  //
  // Extends (does NOT replace) the two shipped tab-persistence mechanisms for the
  // one surface they both decline to cover: the UNBOUND "home" surface (Daily
  // Briefing → direct-Compose door), where `tabAnchorKeyForContext` returns null
  // (no entityContext) AND the BFF `/tabs` store has no `chatSessionId` at
  // tab-open time. See `composeRunPersistence.ts` for the exact gap + the storage
  // choice (localStorage, following the existing `sprk_ai2_*` convention + the
  // owner's conditional; reliable across the code-page iframe teardown).
  //
  // "Home surface" = no bound host record (no local anchor), not an analysis
  // deep-link, and not a ribbon compose-launch (each of those owns its own cold
  // mount). When bound, the shipped anchors already restore the Compose tab.
  // ===========================================================================
  const isHomeSurface = !tabAnchorKey && !analysisLaunch && !isComposeLaunch;

  // Refs mirror the values the (inline) PaneEventBus handler + the callbacks below
  // read, so they never go stale regardless of the subscription's closure vintage.
  const isHomeSurfaceRef = React.useRef(isHomeSurface);
  const chatSessionIdRef = React.useRef<string | null>(chatSessionId ?? null);
  React.useEffect(() => {
    isHomeSurfaceRef.current = isHomeSurface;
  }, [isHomeSurface]);
  React.useEffect(() => {
    chatSessionIdRef.current = chatSessionId ?? null;
  }, [chatSessionId]);

  // Persist-on-open + capture-session-when-available. Runs whenever the open
  // Compose tab set OR chatSessionId changes: read-modify-writes the durable
  // localStorage snapshot (never in-memory-only, so it can't drop a tab it did
  // not itself open), upserting every currently-open Compose tab. Upsert MERGES
  // (preserves any prior `run`/`sessionId`), so this never wipes an in-flight
  // flag captured by the background-run handler. It NEVER removes — removal is
  // explicit-close only (agency; see handleTabClose). No-op off the home surface.
  React.useEffect(() => {
    if (!isHomeSurface) return;
    const composeTabs = tabState.tabs.filter(t => t.widgetType === 'compose');
    if (composeTabs.length === 0) return;
    const now = Date.now();
    let snap = readComposeRunState(now);
    let changed = false;
    for (const t of composeTabs) {
      const instanceKey = composeTabInstanceKey(t);
      if (!instanceKey) continue; // no durable document identity → cannot reliably restore; skip
      snap = upsertPersistedComposeTab(snap, {
        instanceKey,
        widgetType: 'compose',
        widgetData: t.widgetData,
        displayName: t.displayName,
        savedAt: now,
        sessionId: chatSessionId ?? undefined,
      });
      changed = true;
    }
    if (changed) writeComposeRunState(snap);
  }, [tabState.tabs, chatSessionId, isHomeSurface]);

  // Background-run capture (round-4 dismiss signal): `nda_review_background_run` fires true when a
  // review whose progress modal was dismissed ("Continue working in background") is still executing.
  //
  // UAT round-6 (item #15a): this signal is now PURELY a UI concern — it drives the in-page tab
  // spinner ONLY. It NO LONGER stamps the persisted run-in-flight flag. The owner's repro proved the
  // gap: they navigated away WITHOUT ever dismissing the modal, so this signal never fired and nothing
  // persisted. The persistence flag is now stamped at DISPATCH time instead (see the
  // `nda_review_dispatch_active` handler below), which fires for EVERY review path regardless of
  // whether the modal is later dismissed.
  const handleBackgroundRunChange = React.useCallback((active: boolean): void => {
    setComposeReviewRunningInBackground(active);
  }, []);

  // Dispatch-time run capture (UAT round-6 item #15a): `nda_review_dispatch_active` fires true the
  // instant a review binding is dispatched from the Assistant (the ONE `runBindingDispatch`
  // chokepoint — chip/typed/gate/wizard/rerun all funnel through it), and false when that dispatch
  // settles WITHOUT completing (failure). On the home surface we stamp the ACTIVE Compose tab's
  // persisted entry with `run:{inFlight,dispatchedAt}` + the current session so a navigate-away-and-
  // return trip resumes the spinner + poll — INDEPENDENT of the round-4 dismiss signal (the exact gap
  // the owner hit). Clearing on COMPLETION rides `compose_advisory_comments` (keyed by session — see
  // below); this false branch is the failure clear.
  const handleReviewDispatchActive = React.useCallback((active: boolean): void => {
    if (!isHomeSurfaceRef.current) return;
    const activeTab = managerRef.current.getActiveTab();
    if (!activeTab || activeTab.widgetType !== 'compose') return;
    const instanceKey = composeTabInstanceKey(activeTab);
    if (!instanceKey) return;
    const now = Date.now();
    const current = readComposeRunState(now);
    if (active) {
      // Upsert-with-run guarantees the entry exists even if persist-on-open has not
      // flushed yet (races the same tab-open), capturing the session to poll on.
      writeComposeRunState(
        upsertPersistedComposeTab(current, {
          instanceKey,
          widgetType: 'compose',
          widgetData: activeTab.widgetData,
          displayName: activeTab.displayName,
          savedAt: now,
          sessionId: chatSessionIdRef.current ?? undefined,
          run: { inFlight: true, dispatchedAt: now },
        })
      );
    } else {
      // Dispatch failed — clear the active tab's flag so a return trip doesn't resume a dead run.
      writeComposeRunState(clearRunInFlight(current, instanceKey));
    }
  }, []);

  // Completion clear (UAT round-6 item #15a): a review completion — including a zero-findings clean
  // review, which now dispatches unconditionally (see useNdaReviewAdvisoryCommentsBridge) — arrives as
  // `compose_advisory_comments` carrying the completing `sessionId`. Clear the persisted in-flight flag
  // for the tab bound to THAT session (keyed by session, not the active tab, because a dismissed run can
  // complete while the user has switched to a different workspace tab). Idempotent no-op when nothing
  // matches (e.g. the run was never stamped, or already cleared, or the completion is the resume poll's
  // own re-dispatch on restore, which already cleared via `finish()`).
  const handleReviewCompletionForSession = React.useCallback((sessionId: string | undefined): void => {
    if (!isHomeSurfaceRef.current) return;
    if (!sessionId) return;
    writeComposeRunState(clearRunInFlightBySession(readComposeRunState(Date.now()), sessionId));
  }, []);

  // Resume poll (still-running case): show the tab spinner (reusing round-4's
  // `composeReviewRunning` slot) and poll compose-outputs until the review's
  // findings land, then materialize them through the SAME `compose_advisory_comments`
  // receiver the live path uses (+ the completion-toast rules run for free). A
  // `sawEmpty` guard makes this fire ONLY when findings arrive AFTER we observed an
  // empty ledger — so it never double-places with the reopened tab's own FR-16
  // mount-materialize (which handles the already-complete case). Bounded by the
  // young-run ceiling; a single shared boolean drives the spinner (round-4 parity).
  const [composeReviewResuming, setComposeReviewResuming] = React.useState(false);
  const resumeActiveCountRef = React.useRef(0);
  const resumePollCleanupsRef = React.useRef<Array<() => void>>([]);
  React.useEffect(() => {
    return () => {
      resumePollCleanupsRef.current.forEach(fn => fn());
      resumePollCleanupsRef.current = [];
    };
  }, []);

  const runResumePoll = React.useCallback(
    (instanceKey: string, sessionId: string): void => {
      if (!sessionId || !bffBaseUrl || !isAuthenticated) return;
      resumeActiveCountRef.current += 1;
      setComposeReviewResuming(resumeActiveCountRef.current > 0);

      let sawEmpty = false;
      let cancelled = false;
      let timerId: number | null = null;
      const startedAt = Date.now();
      const POLL_INTERVAL_MS = 5000;

      const finish = (): void => {
        if (cancelled) return;
        cancelled = true;
        if (timerId !== null) {
          window.clearInterval(timerId);
          timerId = null;
        }
        resumeActiveCountRef.current = Math.max(0, resumeActiveCountRef.current - 1);
        setComposeReviewResuming(resumeActiveCountRef.current > 0);
        // Clear the in-flight flag durably (the run is now resolved/timed-out for us);
        // the seed + session stay persisted so a later open still recalls findings.
        writeComposeRunState(clearRunInFlight(readComposeRunState(Date.now()), instanceKey));
      };

      const poll = async (): Promise<void> => {
        if (cancelled) return;
        if (Date.now() - startedAt > COMPOSE_RUN_IN_FLIGHT_MAX_MS) {
          finish();
          return;
        }
        try {
          const url = buildBffApiUrl(
            bffBaseUrl,
            `/ai/chat/sessions/${encodeURIComponent(sessionId)}/compose-outputs`
          );
          const resp = await authenticatedFetch(url, {
            method: 'GET',
            headers: { Accept: 'application/json' },
          });
          if (cancelled) return;
          if (!resp.ok) return; // transient; keep polling until the deadline
          const outputs = (await resp.json()) as ComposeLedgerOutputLike[];
          if (cancelled) return;
          const present = hasFindings(outputs);
          if (present && sawEmpty) {
            // Findings arrived AFTER an empty observation → the reopened tab's own
            // mount-materialize already ran empty and will NOT re-fire. Place them
            // via the live event (dispatched ONCE), then stop.
            const payload = findLatestFindingsPayload(outputs);
            if (payload) {
              dispatch('workspace', {
                type: 'compose_advisory_comments',
                advisoryComments: projectFlaggedSectionsToAdvisoryComments(
                  payload.flaggedSections as Parameters<typeof projectFlaggedSectionsToAdvisoryComments>[0]
                ),
                overallRisk: payload.overallRisk,
                sessionId,
                timestamp: new Date().toISOString(),
              });
            }
            finish();
            return;
          }
          if (present && !sawEmpty) {
            // Findings already present on the FIRST check → the reopened tab's own
            // FR-16 mount-materialize is placing them from the SAME ledger; defer to
            // it (no dispatch → no double-placement).
            finish();
            return;
          }
          sawEmpty = true;
        } catch {
          /* network/parse transient — keep polling until the deadline */
        }
      };

      timerId = window.setInterval(() => {
        void poll();
      }, POLL_INTERVAL_MS);
      resumePollCleanupsRef.current.push(finish);
      void poll(); // immediate first check
    },
    [bffBaseUrl, isAuthenticated, authenticatedFetch, dispatch]
  );

  // Cold-load restore (home surface): once the shipped restore has settled, reopen
  // any FRESH persisted Compose tab that is not already open — via the EXACT same
  // `widget_load{ compose }` door a normal open uses (so ComposeDirectWidget →
  // ComposeWorkspace resumes the document via the threaded `composeSessionId` and
  // FR-16 recall re-materializes completed findings on mount). For a still-young
  // in-flight run, start the resume poll (spinner + findings-on-arrival).
  const composeRunRestoreRef = React.useRef(false);
  React.useEffect(() => {
    if (!isAuthenticated) return;
    if (!tabRestoreSettled) return;
    if (!isHomeSurface) return;
    if (composeRunRestoreRef.current) return;
    composeRunRestoreRef.current = true;

    const now = Date.now();
    const snapshot = readComposeRunState(now);
    if (!snapshot || snapshot.tabs.length === 0) return;

    const manager = managerRef.current;
    const openKeys = new Set(
      manager
        .getSnapshot()
        .tabs.filter(t => t.widgetType === 'compose')
        .map(t => composeTabInstanceKey(t))
        .filter((k): k is string => !!k)
    );
    const toRestore = snapshot.tabs.filter(t => !openKeys.has(t.instanceKey));
    if (toRestore.length === 0) return;

    // Macrotask deferral — same subscription-race guard as the auto-install effects:
    // usePaneEvent's 'workspace' subscription (declared later) must be live when we
    // dispatch, or the widget_load lands on a zero-subscriber channel.
    const timerId = window.setTimeout(() => {
      for (const t of toRestore) {
        const seed = withComposeSessionId(t.widgetData, t.sessionId);
        // eslint-disable-next-line no-console
        console.info(
          `[WorkspacePane] Restoring home-surface Compose tab: ${t.displayName} (${t.instanceKey})` +
            (isRunResumable(t.run, Date.now()) ? ' [review in-flight → resuming]' : '')
        );
        dispatch('workspace', {
          type: 'widget_load',
          widgetType: 'compose',
          widgetData: seed,
          displayName: t.displayName,
        });
        if (t.sessionId && isRunResumable(t.run, Date.now())) {
          runResumePoll(t.instanceKey, t.sessionId);
        }
      }
    }, 0);
    return () => window.clearTimeout(timerId);
  }, [isAuthenticated, tabRestoreSettled, isHomeSurface, dispatch, runResumePoll]);

  // ---------------------------------------------------------------------------
  // Analysis entry-matrix auto-install (task 050 — spec §12 / FR-14)
  //
  // When the app is launched into an analysis entry mode (analysisLaunch != null),
  // this effect owns the cold-load surface (the layout + pin auto-installs above
  // early-return on `analysisLaunch`):
  //   - mode='new'      → open the Analysis hub tab ('analysis-hub'). The hub
  //                       renders the "Create new" work-type cards; when a record
  //                       context is present (entityContext, threaded by the 052
  //                       ribbon's entityLogicalName/entityId) the hub's card
  //                       dispatch pre-sets regarding=parent (2b). With no record
  //                       context the hub opens unforced (2a).
  //   - mode='existing' → resolve the analysis's bound session via the task-031
  //                       `GET /ai/chat/sessions/by-analysis/{id}` endpoint and
  //                       dispatch `conversation.session_switch` so the modal
  //                       opens on the existing analysis's transcript (2d/2c),
  //                       with NO hub cards. A 404 (no session ever bound) is a
  //                       graceful no-op — never mints an empty session (mirrors
  //                       the hub's own reopen escalation contract, task 031).
  //
  // Same macrotask deferral + tabRestoreSettled gate + run-once ref guard as the
  // layout/compose auto-install effects above (subscription-race + restore
  // sequencing). ADR-039 / §13.3: this is the deterministic CODE routing path,
  // not the reactive in-chat surface-launch registry.
  // ---------------------------------------------------------------------------
  const autoInstalledAnalysisRef = React.useRef<boolean>(false);
  React.useEffect(() => {
    if (!analysisLaunch) return;
    if (!isAuthenticated) return;
    if (!tabRestoreSettled) return;
    if (autoInstalledAnalysisRef.current) return; // run once per mount
    autoInstalledAnalysisRef.current = true;

    if (analysisLaunch.mode === 'new') {
      const timerId = window.setTimeout(() => {
        // eslint-disable-next-line no-console
        console.info('[WorkspacePane] Auto-installing Analysis hub (entry matrix 2a/2b)');
        dispatch('workspace', {
          type: 'widget_load',
          widgetType: 'analysis-hub',
          widgetData: null,
          displayName: 'Analysis',
        });
      }, 0);
      return () => window.clearTimeout(timerId);
    }

    // mode === 'existing' — load the analysis's latest conversation history (if a
    // session is bound) AND surface the analysis's document. UAT round 4: for an
    // analysis with no bound session yet (e.g. freshly created), the modal must still
    // SHOW the analysis (its linked document) rather than an empty workspace.
    const analysisId = analysisLaunch.analysisId;
    if (!analysisId || !bffBaseUrl) return;
    let cancelled = false;
    const timerId = window.setTimeout(() => {
      void (async () => {
        try {
          const lookupUrl = buildBffApiUrl(
            bffBaseUrl,
            `/ai/chat/sessions/by-analysis/${encodeURIComponent(analysisId)}`
          );
          const response = await authenticatedFetch(lookupUrl, {
            headers: { Accept: 'application/json' },
          });
          if (cancelled) return;
          // 404 (no session ever bound) / any non-OK → fall through to the
          // document-only surface below; never mint an empty session (task 031).
          if (response.ok) {
            const session = (await response.json()) as { sessionId?: string };
            if (!cancelled && session.sessionId) {
              // Restores the transcript (Assistant history) + session-keyed
              // review/findings widgets. The Compose document is mounted separately
              // below so it survives a cross-browser reopen (no localStorage tab).
              dispatch("conversation", {
                type: "session_switch",
                sessionId: session.sessionId,
              });
            }
          }
        } catch {
          // Network/parse failure — fall through to the document-only surface.
        }

        // Surface the analysis's linked document in the EDITABLE Compose/TipTap
        // surface (Phase 1, UAT round 5): the analysis surface IS the review surface,
        // not a read-only preview. Resolve the SPE pointer from the linked
        // sprk_document (`sprk_graphitemid` = drive-item, `sprk_graphdriveid` = drive
        // — BOTH required by ComposeEditor; mirrors DocumentComposeLaunch). Falls back
        // to the read-only document-viewer if the pointer is incomplete.
        //
        // Cross-browser reopen MUST-HAVE (durable Compose + history together): mount
        // the document even when a session was restored above. `session_switch`
        // restores the CONVERSATION transcript (server-backed by-analysis lookup), but
        // the Compose WORKSPACE tab only restores from the per-Analysis localStorage
        // snapshot — absent in a fresh browser / cleared storage. Without this,
        // cross-browser reopen lands on chat history with NO editor. Guard against
        // double-mount in the same-browser case where restoreFromPersistence already
        // rehydrated a compose tab from localStorage (tabRestoreSettled gate above
        // guarantees restore has completed, so this snapshot is authoritative).
        if (cancelled) return;
        const hasComposeTab =
          managerRef.current?.getSnapshot().tabs.some((t) => t.widgetType === "compose") ?? false;
        if (hasComposeTab) return;
        try {
          // task 022 (spec FR-09; hub A3 deferred deep-threading leg — open-existing door):
          // $expand the persisted `sprk_agreementtype` lookup (attribute logical name confirmed
          // via Dataverse MCP describe against `sprk_analysis`, 2026-07-31 — NOT
          // `sprk_agreementtypeid`, which is the reference table's own PK) so a reopened
          // Analysis's sub-domain rides into the Compose envelope alongside `activeWorkType`.
          const rec = (await analysisWizardDataService.retrieveRecord(
            "sprk_analysis",
            analysisId,
            "?$select=sprk_name,sprk_worktype,_sprk_documentid_value,_sprk_agreementtype_value" +
              "&$expand=sprk_documentid($select=sprk_filename,sprk_graphitemid,sprk_graphdriveid)," +
              "sprk_agreementtype($select=sprk_key)",
          )) as Record<string, unknown>;
          if (cancelled) return;
          const doc = (rec["sprk_documentid"] ?? {}) as Record<string, unknown>;
          const speDriveItemId = doc["sprk_graphitemid"] as string | undefined;
          const speDriveId = doc["sprk_graphdriveid"] as string | undefined;
          const documentId = rec["_sprk_documentid_value"] as string | undefined;
          const fileName =
            (doc["sprk_filename"] as string | undefined) ?? (rec["sprk_name"] as string | undefined) ?? "Document";
          // Agreement/NDA work-type (100000000) scopes the Compose AI toolbar tools.
          const activeWorkType = rec["sprk_worktype"] === 100000000 ? "agreement-analysis" : undefined;
          // task 022: ONE envelope contract — an EXPLICIT subDomain from the cold-load deep-link
          // door (`analysisLaunch.subDomain`, task 022's other leg) is authoritative; the
          // open-existing derivation (the expanded lookup's `sprk_key`) fills it only when the
          // explicit value is absent. Neither present → field stays absent (no fabricated
          // default; the classifier, task 021, owns filling it later during the review dispatch).
          const agreementType = (rec["sprk_agreementtype"] ?? {}) as Record<string, unknown>;
          const derivedSubDomain = agreementType["sprk_key"] as string | undefined;
          const subDomain = analysisLaunch.subDomain ?? derivedSubDomain;

          if (speDriveItemId && speDriveId) {
            dispatch("workspace", {
              type: "widget_load",
              widgetType: "compose",
              widgetData: {
                compose: {
                  speDriveItemId,
                  speDriveId,
                  ...(documentId ? { sprkDocumentId: documentId } : {}),
                  fileName,
                  ...(activeWorkType ? { activeWorkType } : {}),
                  ...(subDomain ? { subDomain } : {}),
                },
              },
              displayName: fileName,
            });
            return;
          }

          // Incomplete SPE pointer → read-only preview fallback (ADR-007 hop).
          const preview = resolveAnalysisFilePreview(
            rec as Parameters<typeof resolveAnalysisFilePreview>[0],
            { bffBaseUrl, authenticatedFetch },
          );
          if (preview.status !== "resolved") return;
          dispatch("workspace", {
            type: "widget_load",
            widgetType: "document-viewer",
            widgetData: {
              filename: preview.documentName,
              contentType: "application/octet-stream",
              textContent: "",
              documentId: preview.documentId,
              fetchPreviewUrl: preview.fetchPreviewUrl,
            },
            displayName: preview.documentName,
          });
        } catch {
          // Could not resolve the document — leave the workspace to the default surface.
        }
      })();
    }, 0);
    return () => {
      cancelled = true;
      window.clearTimeout(timerId);
    };
    // Run once per mount when auth + restore are ready; the ref guard prevents
    // re-runs. authenticatedFetch + analysisWizardDataService are stable refs.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [analysisLaunch, isAuthenticated, tabRestoreSettled, bffBaseUrl, analysisWizardDataService]);

  // ---------------------------------------------------------------------------
  // Auto-open pinned workspaces — task 092 / round 5 / task 101 fix
  //
  // Reads the multi-pin list from `services/pinnedWorkspaces.ts` (backed by
  // `localStorage` key `spaarke:workspace:pinned-list`) and dispatches a
  // `widget_load` event for each pinned workspace. The existing event handler
  // below converts each into a tab via the WorkspaceTabManager pipeline —
  // identical machinery used by `WorkspacePaneMenu.handleLayoutSelect`. Home
  // tab remains the default and is NOT replaced (pinned tabs open IN ADDITION
  // to Home so the user can close Home if they don't want it).
  //
  // Deferral on auth: pinned workspaces resolve via LegalWorkspace's embedded
  // `useWorkspaceLayouts` which calls the BFF — we wait for `isAuthenticated`
  // before dispatching so the auto-opened tabs hydrate cleanly instead of
  // rendering a 401 or empty state.
  //
  // Duplicate guard: if a pinned workspace is already open (e.g. user
  // refreshes the page mid-session and tab restore via NFR-09 restored that
  // workspace), we skip the auto-open dispatch to avoid stacking duplicate
  // tabs. The match is by `widgetData.layoutId` since `widgetType` is the
  // generic `'workspace'` string for every workspace tab.
  //
  // Subscription-race fix (task 101 — operator feedback):
  //   The `usePaneEvent('workspace', ...)` subscription below is registered
  //   via its own internal `useEffect`. React runs effects in declaration
  //   order, so this auto-open effect (declared earlier in this component)
  //   ran BEFORE the workspace subscription was attached. When `widget_load`
  //   was dispatched, PaneEventBus had zero subscribers on the workspace
  //   channel → the event fell on the floor → pinned tabs never opened on
  //   refresh, even though the pin indicator persisted correctly.
  //
  //   Fix: defer the dispatches to a macrotask via `setTimeout(..., 0)`. By
  //   the time the macrotask runs, every useEffect in this render's commit
  //   phase has executed (including usePaneEvent's), so the subscription is
  //   live when the events fire. We cancel the timer on unmount to avoid a
  //   late dispatch into a torn-down tree.
  // ---------------------------------------------------------------------------

  const autoOpenedPinsRef = React.useRef<boolean>(false);
  React.useEffect(() => {
    if (!isAuthenticated) return;
    if (layouts.length === 0) return; // wait for layouts to load before pruning
    // R3-3 (2026-07-07): same sequencing gate as the auto-install-default
    // effect — the duplicate guard below must see the RESTORED tabs or the
    // pin auto-open re-stacks (and its write-through clobbers) them.
    if (!tabRestoreSettled) return;
    if (autoOpenedPinsRef.current) return; // run once per mount
    autoOpenedPinsRef.current = true;

    // Stale-pin cleanup: drop pinned entries whose layoutId is no longer in
    // the server-side layouts list (e.g. another device or the Manage
    // Workspaces drawer deleted the layout). Persists the cleaned list back
    // to localStorage in the same call. Returns the live (cleaned) list so we
    // do not dispatch widget_load for non-existent layouts.
    const knownLayoutIds = new Set(layouts.map(l => l.id));
    const pinned = prunePinnedToKnown(knownLayoutIds);
    if (pinned.length === 0) return;

    const manager = managerRef.current;
    const openLayoutIds = new Set<string>(
      manager
        .getSnapshot()
        .tabs.filter(t => t.widgetType === 'workspace')
        .map(t => {
          const data = t.widgetData as { layoutId?: string } | null;
          return data?.layoutId ?? '';
        })
        .filter((id): id is string => id.length > 0)
    );

    // Filter to the pins that actually need opening so we can log + skip
    // cleanly if there's nothing to do.
    const pinsToOpen = pinned.filter(pin => !openLayoutIds.has(pin.layoutId));
    if (pinsToOpen.length === 0) return;

    // Defer dispatch to a macrotask so usePaneEvent's subscription effect
    // (declared later in this component) has had a chance to register on the
    // workspace channel. Without this, dispatches land on a zero-subscriber
    // channel and are silently dropped — see block comment above.
    const timerId = window.setTimeout(() => {
      // eslint-disable-next-line no-console
      console.info(`[WorkspacePane] Auto-opening ${pinsToOpen.length} pinned workspace(s):`, pinsToOpen);
      for (const pin of pinsToOpen) {
        dispatch('workspace', {
          type: 'widget_load',
          widgetType: 'workspace',
          widgetData: { layoutId: pin.layoutId, layoutName: pin.layoutName },
          displayName: pin.layoutName,
        });
      }
    }, 0);

    return () => {
      window.clearTimeout(timerId);
    };
    // Auto-open is a one-shot per mount. `isAuthenticated` flipping false→true
    // is the trigger; subsequent state changes (re-auth on token refresh, or
    // layouts refetch) MUST NOT re-trigger or we'd re-stack tabs. The ref
    // guard above enforces this. `layouts` is in deps so the effect re-runs
    // once after the initial empty array is replaced with the loaded list
    // (the early-return guard at top blocks the first empty-array invocation).
    // `tabRestoreSettled` is in deps so the effect re-runs once restore
    // completes (R3-3 sequencing gate above).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isAuthenticated, layouts, tabRestoreSettled]);

  // ---------------------------------------------------------------------------
  // R6 Hotfix Wave B-G9c2 (B7 + B8) — DEFERRED Summary-tab install + per-run
  // tab (2026-06-10).
  //
  // Previously (R5 task 038): a single "Summary" tab was eagerly prepended on
  // WorkspacePane mount as a persistent event sink for `workspace.streaming_*`
  // events tagged with `streamId === chatSessionId`. This caused two bugs
  // surfaced during the Phase B walkthrough:
  //
  //   B7: An empty Summary tab appeared (and was default-active) BEFORE any
  //       summarize had run — confusing the user.
  //
  //   B8: Every subsequent `/summarize` run REPLACED the prior tab's content
  //       because all runs shared `streamId = chatSessionId`. File A's summary
  //       was lost when file B was summarized.
  //
  // The fix shifts BOTH responsibilities to the invoking dispatcher (today
  // the shared `dispatchConsumer` helper's `workspaceTarget` arg; formerly
  // the retired R5 per-capability summarize orchestrator):
  //
  //   - Each run generates a UNIQUE `streamId` (no reuse of `chatSessionId`).
  //   - The run synchronously emits `workspace.widget_load` with the structured-
  //     output-stream widget + `correlationId = streamId` + tab title
  //     `Summary: <fileName>` BEFORE the SSE stream opens. The existing
  //     `widget_load` handler below (~line 720+) installs a NEW tab via
  //     `addTab`, which honors `MAX_WORKSPACE_TABS` FIFO eviction.
  //   - The subsequent `streaming_started` event flows to the same tab via
  //     the widget's `correlationId === streamId` gate.
  //
  // Race handling: PaneEventBus dispatch is SYNCHRONOUS, so the `widget_load`
  // event installs the tab BEFORE any `streaming_started` event from the
  // same call site. The widget itself mounts asynchronously
  // (resolveWorkspaceWidget()), but the tab carries the `widgetData` payload
  // including correlationId from the moment it's installed; the widget's
  // initial effect picks up the early events via its own subscription.
  // ---------------------------------------------------------------------------

  // ---------------------------------------------------------------------------
  // R6 Hotfix Wave B-G9c2 — auto-focus is now NATURAL via `addTab`
  //
  // Each summarize run dispatches a `workspace.widget_load`; the existing
  // `widget_load` handler below calls `manager.addTab(...)` which
  // AUTO-ACTIVATES the new tab (see WorkspaceTabManager.addTab), so the new
  // Summary tab is focused as soon as the run starts — no separate
  // `streaming_started` focus handler required. (The R5 task 038 manual
  // Summary-override refs were removed here: each run owns its own tab, so the
  // override semantic was vestigial and its `handleTabChange` block was
  // unreachable — `summaryTabIdRef` was permanently null.)
  // ---------------------------------------------------------------------------

  // ---------------------------------------------------------------------------
  // PaneEventBus subscription — 'workspace' channel
  // ---------------------------------------------------------------------------

  usePaneEvent('workspace', (event: WorkspacePaneEvent): void => {
    const manager = managerRef.current;

    // ai-advanced-capabilities-analysis-hub-r1 (tabbed Quick Start): open the
    // Create Analysis wizard AS A MODAL (not a tab). Just flip state; the modal
    // render below injects the Xrm services + regarding and, on finish, the wizard
    // opens its own result tab (document-viewer) exactly as before.
    if (event.type === 'open_create_analysis_wizard') {
      setCreateAnalysisModal({
        open: true,
        workTypeValue: event.analysisWorkType,
        workTypeLabel: event.analysisWorkTypeLabel,
      });
      return;
    }

    // ai-advanced-capabilities-analysis-hub-r1 (headless analysis open): the Analysis
    // hub grid row-click opens the picked analysis as a HEADLESS SpaarkeAi code-page
    // modal (`openSpaarkeAi` target:2 — no OOB `sprk_analysis` form chrome), instead of
    // the OOB form. A fresh SpaarkeAi instance mounts on `analysisId` → the existing-
    // analysis effect restores Compose + history (cross-browser durable recall).
    if (event.type === 'open_analysis_headless') {
      if (event.analysisId) {
        openSpaarkeAi({ analysisId: event.analysisId }, 2);
      }
      return;
    }

    // UAT round-4 (item #10a): a backgrounded review run's liveness lives on the Compose tab now.
    // Track the latest state so the tab strip can show/hide the tiny circular progress indicator; the
    // dismissed progress card itself is fully unmounted (useNdaReviewRunProgress `visible=false` →
    // NdaReviewProgressModal returns null), so this signal is the ONLY remaining liveness surface.
    if (event.type === 'nda_review_background_run') {
      // Round-4 dismiss signal — drives the in-page tab spinner ONLY. UAT round-6 (item #15a): it no
      // longer stamps the persisted run flag; dispatch-time stamping does that (see below).
      handleBackgroundRunChange(event.backgroundRunActive === true);
      return;
    }

    // UAT round-6 (item #15a): stamp / clear the persisted run-in-flight flag at DISPATCH time (true)
    // and on dispatch failure (false). This fires for EVERY review path (chip/typed/gate/wizard/rerun)
    // because they all funnel through the ONE `runBindingDispatch` chokepoint — so the flag is now
    // captured whether or not the user ever dismisses the progress modal (the owner's navigate-away
    // repro never dismissed it).
    if (event.type === 'nda_review_dispatch_active') {
      handleReviewDispatchActive(event.dispatchActive === true);
      return;
    }

    // UAT round-6 (item #15a): a review COMPLETION (incl. a zero-findings clean review) arrives as
    // `compose_advisory_comments`. Clear the persisted in-flight flag for the completing session so a
    // later navigate-away doesn't resurrect a stale spinner. (This runs alongside ComposeWorkspace's
    // own advisory-comments receiver — the bus is broadcast; both subscribers act independently.)
    if (event.type === 'compose_advisory_comments') {
      handleReviewCompletionForSession(event.sessionId);
      return;
    }

    // FR-34 D-F3 (task 071): the deferred CONTENT-render ack. ComposeWorkspace emits
    // `compose_content_rendered` once a seeded draft actually renders in the editor.
    // If we deferred an ack for this ledgerRef on the originating `workspace_open_tab`
    // frame, fire it NOW (the genuine "content is on screen" confirmation) — mirroring
    // the tab-open ack, only later + honest. No pending entry ⇒ no-op (a client-
    // originated open, or a render we never gated on).
    if (event.type === 'compose_content_rendered') {
      const ledgerRef = event.ledgerRef;
      if (ledgerRef) {
        const frameId = pendingRenderAcksRef.current.get(ledgerRef);
        if (frameId) {
          pendingRenderAcksRef.current.delete(ledgerRef);
          sendUiActionAck(frameId);
        }
      }
      return;
    }

    if (event.type === 'widget_load' && !event.tabId) {
      // Guard: ignore our own re-dispatched widget_load confirmations (which carry tabId).
      // Only the server-initiated events (no tabId) should open a new tab.
      const widgetType = event.widgetType ?? 'unknown';
      const widgetData = event.widgetData ?? null;

      // NOTE: All 'email' widget_load events now fall through to the generic registry
      // path below (→ resolveWorkspaceWidget('email') → the REAL EmailWorkspaceWidget).
      // The former "Email — coming soon" stub intercept (FIX #10b) was removed once the
      // full email widget shipped (email-communication-solution-r5); nothing dispatches
      // the compose-draft hand-off payload (mode/bodyText/attachmentFileName) any more.

      // Resolve the tab display name with this precedence:
      //   1. Event payload `displayName` (Round 4 Fix 4: lets the menu set the
      //      tab title to a per-instance label such as "Corporate Workspace"
      //      rather than the generic registry label "Workspace").
      //   2. Registry metadata `displayName`.
      //   3. The raw widgetType string as last resort.
      const meta = getWorkspaceWidgetMetadata(widgetType);
      const displayName = event.displayName ?? meta?.displayName ?? widgetType;

      // ── spaarkeai-compose-r2 UNIFY — "Compose" layout → Direct 'compose' ────
      // A "Compose" workspace-LAYOUT load — the WorkspacePaneMenu "Compose"
      // menu selection (WorkspacePaneMenu.handleLayoutSelect dispatches
      // widgetType:'workspace' + layoutName:'Compose'), or any legacy
      // Compose-layout dispatch — is RE-ROUTED here to the DIRECT 'compose'
      // widget so it mounts as widgetType 'compose' and is protected by the
      // keep-mounted-hidden keep-alive. Detected by the layout NAME "Compose";
      // ALL other layouts (Daily Briefing, dashboards, …) keep the 'workspace'
      // LAYOUT door and flow through the generic addTab path below, unchanged.
      // The menu selection carries no document → empty Compose editor.
      const isComposeLayoutLoad =
        widgetType === 'workspace' &&
        ((widgetData as { layoutName?: string } | null)?.layoutName === 'Compose' || event.displayName === 'Compose');
      const effectiveWidgetType = isComposeLayoutLoad ? 'compose' : widgetType;

      // ── Compose DIRECT widget — single-tab reuse (spaarkeai-compose-r2) ─────
      // Compose is a first-class DIRECT workspace widget (widgetType 'compose' →
      // ComposeDirectWidget → ComposeWorkspace), NOT a LegalWorkspace LAYOUT tab.
      // Every Compose open (a chat "open as a document" draft, an "Open in
      // Compose" upload, a stored open, or an empty open) mounts through THIS
      // branch, so ComposeWorkspace renders UNCONDITIONALLY — never
      // LegalWorkspaceApp/dashboard, and with NO layout-row lookup or race. It
      // REUSES the single existing 'compose' tab (allowMultiple:false) instead
      // of stacking duplicates. Other layouts (Daily Briefing, Documents, …)
      // keep the 'workspace' LAYOUT door and flow through the generic addTab
      // path below, unchanged.
      if (effectiveWidgetType === 'compose') {
        // FR-D1 (spaarkeai-assistant-enhancements-r2): a compose open that ADOPTS a
        // session (wizard→review hand-off / cold-load compose re-adoption — seed
        // carries `composeSessionId`) will imminently change chatSessionId to that
        // session. Mark it so the restore effect's session-switch clear does NOT wipe
        // this just-opened compose tab (those flows show the adopted document, not the
        // session's full stored tab set — the analysis-existing skip, applied to the
        // in-place compose door).
        {
          const adoptSid = (widgetData as { compose?: { composeSessionId?: string } } | null)
            ?.compose?.composeSessionId;
          if (adoptSid) composeAdoptionSessionRef.current = adoptSid;
        }
        // FR-34 D-F3 (task 071): a CONTENT-bearing open carries a full-document
        // draft SEED (widgetData.compose.draft.ledgerRef — DEF-08 Part A). When
        // present, the ack is DEFERRED until ComposeWorkspace signals the draft
        // actually rendered (compose_content_rendered), keyed by this ledgerRef.
        // Absent for upload/stored/empty opens, which ack on tab-open as before.
        const composeData = widgetData as { compose?: { draft?: { ledgerRef?: string } } } | null;
        const composeDraftLedgerRef =
          composeData?.compose && typeof composeData.compose === 'object'
            ? composeData.compose.draft?.ledgerRef
            : undefined;

        // R3 filename contract — hoist the loaded document's filename to a TOP-LEVEL `filename` on
        // the compose tab's widgetData so it is readable server-side (a sibling server agent maps it
        // to a DocumentViewer visible-state). Sourced from the seed's known locations (upload /
        // draft) or an already-hoisted top-level value. When a re-seed carries none (e.g. an
        // add-to-DMS re-activation with only a `source` marker), the existing tab's filename is
        // preserved rather than clobbered with undefined.
        const composeSeed = widgetData as {
          filename?: string;
          compose?: {
            fileName?: string;
            upload?: { fileName?: string };
            draft?: { fileName?: string };
          };
        } | null;
        const seedFilename =
          composeSeed?.compose?.upload?.fileName ??
          composeSeed?.compose?.draft?.fileName ??
          // spaarkeai-compose-r2 UNIFY: the ribbon stored-doc seed carries its
          // name at `compose.fileName` (speFileName) — hoist it too so the
          // R3 server-readable top-level `filename` contract covers the ribbon
          // Open-in-Compose path, not just upload/draft opens.
          composeSeed?.compose?.fileName ??
          composeSeed?.filename;

        const ackComposeFrame = (): void => {
          if (!event.frameId) return;
          if (composeDraftLedgerRef) {
            pendingRenderAcksRef.current.set(composeDraftLedgerRef, event.frameId);
          } else {
            sendUiActionAck(event.frameId);
          }
        };

        // ── Instance-keyed reuse (spaarkeai-compose-r2 — multi-Compose-tab) ────
        // Compose is no longer a hard singleton. Decide reuse-vs-new by DOCUMENT
        // IDENTITY, not by widgetType:
        //   • the open carries an identity key AND an existing compose tab has the
        //     SAME key → REUSE that tab (relaunch/restore of the same doc; a
        //     re-draft of the same binding across turns);
        //   • a source-only / seedless re-activation (no new `compose` seed, no
        //     identity — e.g. the add-to-DMS `{source}` marker, issue #572 1d) →
        //     REUSE the ACTIVE compose tab (else any existing one) so add-to-DMS
        //     keeps working without minting a blank tab;
        //   • an identity key with NO match, OR an explicit blank open (the
        //     Workspaces-menu "Compose" layout load) → fall through to a NEW tab.
        const instanceKey = deriveComposeInstanceKey(widgetData);
        const hasComposeSeed =
          widgetData != null &&
          typeof widgetData === 'object' &&
          typeof (widgetData as { compose?: unknown }).compose === 'object' &&
          (widgetData as { compose?: unknown }).compose !== null;
        // UAT (tab independence): the seedless-reuse-active branch below must fire ONLY for the
        // KNOWN source-only re-activation flows (add-to-DMS / reporting-email / welcome-compose),
        // which carry an explicit `widgetData.source` marker and legitimately act on the CURRENT
        // compose doc's tab. WITHOUT this gate, ANY seedless compose open adopted whatever compose
        // tab was active and OVERWROTE it — e.g. opening a new file clobbered an open NDA analysis.
        // A seedless open with no `source` marker is an ambiguous/new open → mint a NEW tab (below),
        // keeping every analysis tab independent.
        const hasSourceReactivationMarker =
          widgetData != null &&
          typeof widgetData === 'object' &&
          typeof (widgetData as { source?: unknown }).source === 'string' &&
          ((widgetData as { source?: string }).source ?? '').length > 0;

        const snapshot0 = manager.getSnapshot();
        const composeTabs = snapshot0.tabs.filter(t => t.widgetType === 'compose');
        let reuseTab: (typeof composeTabs)[number] | undefined;
        if (instanceKey) {
          reuseTab = composeTabs.find(t => composeTabInstanceKey(t) === instanceKey);
        } else if (!hasComposeSeed && !isComposeLayoutLoad && hasSourceReactivationMarker) {
          // Source-only re-activation (explicit `widgetData.source`) — reuse the ACTIVE compose tab,
          // else the first open one. Never mints a duplicate (source-only opens carry no new
          // document + intentionally target the current doc). A blank menu "Compose" open
          // (isComposeLayoutLoad) and any UN-marked seedless open both fall through to a NEW tab.
          reuseTab = composeTabs.find(t => t.id === snapshot0.activeTabId) ?? composeTabs[0];
        }

        if (reuseTab) {
          const existingComposeTab = reuseTab;
          const existingData = (existingComposeTab.widgetData ?? {}) as {
            filename?: string;
            compose?: Record<string, unknown>;
          };
          const newData = (widgetData ?? {}) as { compose?: Record<string, unknown> };
          const existingFilename = existingData.filename;
          const mergedFilename = seedFilename ?? existingFilename;
          // UAT round-7 #1 seed-merge: a seedless re-activation (e.g. an
          // add-to-DMS `source`-only marker) carries NO new `compose` seed.
          // The prior implementation spread ONLY the new event's widgetData,
          // OVERWRITING the tab's reloadable `compose` seed with nothing — so a
          // later remount had nothing to reload. Preserve the existing seed when
          // the re-activation brings none (merge, don't overwrite); a genuine
          // new seed (fresh draft/upload) still wins. The tab's stable identity
          // is re-derived from this seed on the next reuse decision, so nothing
          // extra is stamped onto it (keeps the seed shape clean).
          const mergedCompose = newData.compose ?? existingData.compose;
          const reuseWidgetData: Record<string, unknown> = {
            ...(widgetData ?? {}),
            ...(mergedCompose !== undefined ? { compose: mergedCompose } : {}),
            ...(mergedFilename ? { filename: mergedFilename } : {}),
          };
          manager.updateTab(existingComposeTab.id, reuseWidgetData);
          manager.setActiveTab(existingComposeTab.id);
          syncState();
          ackComposeFrame();
          window.setTimeout(() => {
            dispatch('workspace', {
              type: 'tab_change',
              tabId: existingComposeTab.id,
              widgetType: existingComposeTab.widgetType,
              widgetData: reuseWidgetData,
            });
          }, 0);
          return;
        }

        // No matching Compose tab — add a NEW one and lazily resolve the DIRECT
        // 'compose' widget (ComposeDirectWidget) from the registry. Hoist the
        // seed's filename to a top-level `filename` (R3 server-readable contract).
        // The tab's stable identity is re-derived from its seed on later reuse
        // decisions, so nothing extra is stamped onto widgetData.
        const composeWidgetData = seedFilename ? { ...(widgetData ?? {}), filename: seedFilename } : widgetData;
        // Fix 2 (spaarkeai-assistant-enhancements-r2): every compose entry path
        // (upload / draft / stored-doc / ribbon-launch) already resolves
        // `seedFilename` above — derive the short tab label + full-filename
        // tooltip from it HERE, the single funnel point, instead of touching
        // each dispatch call site. A seedless/blank open (no filename known —
        // e.g. the Workspaces-menu "Compose" selection or the welcome-card
        // blank open) falls back to the plain `displayName` ("Compose"),
        // identical to the pre-fix behavior.
        const composeLabel = deriveComposeTabLabel(seedFilename, displayName);
        const composeTabId = manager.addTab('compose', composeWidgetData, composeLabel.displayName, composeLabel.tooltip);
        syncState();
        // UC-5 truthfulness (task 020 / FR-C1): a NEW compose tab's widget has
        // NOT resolved yet — only the empty shell exists. Acking here would
        // claim "opened the draft in Compose" before anything materialized (the
        // R2-D fabrication). Register a DRAFT's deferred render-ack now
        // (stashing a pending id is not itself a claim); a NON-draft open's ack
        // is withheld until the compose widget actually resolves + attaches, in
        // the .then() below. Never ack at bare shell creation.
        if (event.frameId && composeDraftLedgerRef) {
          pendingRenderAcksRef.current.set(composeDraftLedgerRef, event.frameId);
        }
        resolveWorkspaceWidget('compose').then(Component => {
          const resolvedMeta = getWorkspaceWidgetMetadata('compose');
          manager.resolveTabComponent(
            composeTabId,
            Component,
            event.displayName ? undefined : resolvedMeta?.displayName
          );
          syncState();
          // UC-5 (task 020): the compose widget has now resolved + attached — a
          // NON-draft open (upload/stored/empty) is genuinely materialized, so
          // ack it here. Draft opens still defer to compose_content_rendered
          // (registered above); if that render never fires, no ack is sent and
          // the server's WaitForAckAsync times out into an honest failure.
          if (event.frameId && !composeDraftLedgerRef) {
            sendUiActionAck(event.frameId);
          }
          const snapshot = manager.getSnapshot();
          const currentTabCount = snapshot.tabs.length;
          dispatch('workspace', {
            type: 'widget_load',
            widgetType: 'compose',
            tabId: composeTabId,
            ...(currentTabCount > 0 ? { tabCount: currentTabCount } : {}),
          });
          dispatch('workspace', {
            type: 'tab_count_change',
            tabCount: currentTabCount,
          });
        });
        return;
      }

      // ── Analysis wizard host-service injection (task 050 thread 1) ──────────
      // The context-agnostic `create-analysis-wizard` needs Xrm-coupled Dataverse
      // services the shared-lib widget MUST NOT construct itself (ADR-012). The
      // SpaarkeAi solution injects them here from the Xrm host context, merging
      // OVER the dispatcher-supplied widgetData (e.g. the hub card's workTypeValue
      // + 2b initialAssociation) so pre-set context is preserved. Every other
      // widgetType flows through unchanged.
      const effectiveWidgetData =
        widgetType === 'create-analysis-wizard'
          ? {
              ...((widgetData as Record<string, unknown> | null) ?? {}),
              dataService: analysisWizardDataService,
              navigationService: analysisWizardNavigationService,
              searchUsers: analysisWizardSearchUsers,
              searchAssignees: analysisWizardSearchUsers,
              resolveSpeContainerId: resolveAnalysisSpeContainerId,
              authenticatedFetch,
              bffBaseUrl,
              ...(chatSessionId ? { sessionId: chatSessionId } : {}),
            }
          : widgetData;

      // ── Fix 1 (spaarkeai-assistant-enhancements-r2, UAT: duplicate tabs) ────
      // This generic path had NO de-dup guard, unlike the compose branch's
      // instance-keyed reuse above and the startup-default-layout effect's
      // layoutId match (~line 960). A second `widget_load` for an already-open
      // workspace LAYOUT or singleton widget type stacked a duplicate tab
      // instead of focusing the existing one — e.g. asking "do you see the
      // daily briefing tab?" opened a SECOND Daily Briefing tab.
      //
      // Reuse rule (mirrors the two existing guards' style):
      //   - widgetType === 'workspace' (a LAYOUT tab) → match an existing
      //     'workspace' tab by `widgetData.layoutId`. The 'workspace' registry
      //     entry is itself allowMultiple:true (different LAYOUTS may coexist
      //     side-by-side — Corporate Workspace + Litigation Workspace), but the
      //     SAME layoutId must not stack a second tab.
      //   - any other widgetType → match an existing tab by widgetType alone,
      //     but ONLY when the registry metadata's `allowMultiple` is falsy (a
      //     true singleton, e.g. a global dashboard). Widgets registered
      //     allowMultiple:true (email, document-viewer, analysis, …) keep
      //     stacking as before — untouched by this guard.
      const incomingLayoutId =
        widgetType === 'workspace' ? (widgetData as { layoutId?: string } | null)?.layoutId : undefined;
      const dedupeSnapshot = manager.getSnapshot();
      const existingSingletonTab =
        widgetType === 'workspace'
          ? incomingLayoutId
            ? dedupeSnapshot.tabs.find(t => {
                if (t.widgetType !== 'workspace') return false;
                const data = t.widgetData as { layoutId?: string } | null;
                return data?.layoutId === incomingLayoutId;
              })
            : undefined
          : !meta?.allowMultiple
            ? dedupeSnapshot.tabs.find(t => t.kind === 'widget' && t.widgetType === widgetType)
            : undefined;

      if (existingSingletonTab) {
        // Reuse: update the existing tab's data + focus it — no new tab, no
        // FIFO eviction slot consumed. Mirrors the compose branch's reuse
        // handling (update then activate) rather than a bare setActiveTab, so
        // a re-dispatch carrying fresher widgetData (e.g. a changed
        // workTypeValue) still reaches the mounted widget.
        manager.updateTab(existingSingletonTab.id, effectiveWidgetData);
        manager.setActiveTab(existingSingletonTab.id);
        syncState();

        // Same truthfulness contract as a normal open (UC-5 / FR-C1): the
        // widget is already resolved+mounted (it was reused, not freshly
        // created), so acking + the confirmation dispatches are safe here —
        // no need to wait on resolveWorkspaceWidget() again.
        if (event.frameId) {
          sendUiActionAck(event.frameId);
        }

        const snapshotAfterReuse = manager.getSnapshot();
        const reuseTabCount = snapshotAfterReuse.tabs.length;
        dispatch('workspace', {
          type: 'widget_load',
          widgetType,
          tabId: existingSingletonTab.id,
          ...(reuseTabCount > 0 ? { tabCount: reuseTabCount } : {}),
        });
        dispatch('workspace', {
          type: 'tab_count_change',
          tabCount: reuseTabCount,
        });
        return;
      }

      // Add the tab — this enforces MAX_WORKSPACE_TABS eviction internally.
      const tabId = manager.addTab(widgetType, effectiveWidgetData, displayName);
      syncState();

      // Lazy-resolve the widget component; update the tab once resolved.
      resolveWorkspaceWidget(widgetType).then(Component => {
        const resolvedMeta = getWorkspaceWidgetMetadata(widgetType);
        // Round 4 Fix 4: preserve a per-instance displayName from the event
        // payload (e.g. "Corporate Workspace") over the registry's generic
        // label (e.g. "Workspace"). Pass `undefined` for displayName when the
        // event carried one so resolveTabComponent does not overwrite it.
        manager.resolveTabComponent(tabId, Component, event.displayName ? undefined : resolvedMeta?.displayName);
        syncState();

        // UC-5 truthfulness (task 020 / FR-C1) — MOVED here from tab-shell
        // creation (was AIR2-037 acking at addTab). The tab's widget component
        // has now genuinely resolved + attached; only NOW is "opened X" a true
        // claim. If the server frame carried a frameId (an ack-gated tool call
        // is waiting), ack it referencing that EXACT frame id. Client-originated
        // widget_load events (menu-opened tabs) never carry a frameId, so this
        // is a no-op for them. If resolution never completes, no ack is sent →
        // the server's WaitForAckAsync times out into an honest failure, never a
        // fabricated success. (This is the LAYOUT/other-widget path — Daily
        // Briefing, Documents, …; a content-bearing Compose draft open defers
        // its ack to compose_content_rendered in the 'compose' branch above.)
        if (event.frameId) {
          sendUiActionAck(event.frameId);
        }

        // Snapshot the current tab count after resolution so ShellStageManager
        // can advance stage (Stage 2 → Stage 3 / Stage 4).
        const snapshot = manager.getSnapshot();
        const currentTabCount = snapshot.tabs.length;

        // Dispatch widget_load WITH tabId so ShellStageManager reacts to it
        // (server-initiated events carry no tabId; this is the confirmation).
        // tabCount is included so ShellStageManager can also derive Stage 4.
        dispatch('workspace', {
          type: 'widget_load',
          widgetType,
          tabId,
          ...(currentTabCount > 0 ? { tabCount: currentTabCount } : {}),
        });

        // Dispatch tab_count_change so ShellStageManager can drive Stage 3↔4.
        dispatch('workspace', {
          type: 'tab_count_change',
          tabCount: currentTabCount,
        });
      });
    } else if (event.type === 'widget_update') {
      if (event.tabId) {
        manager.updateTab(event.tabId, event.widgetData ?? null);
        syncState();
        // task 042c-fr-c4 (FR-C4) companion focus-stamp fix. `WorkspaceTabManager.updateTab`
        // fires `_notifyPersistChange` but NOT `_notifyActiveTabChange` (:515-523), so when the
        // user browses emails WITHIN the active email tab (each pick re-fires `widget_update` with
        // a fresh `emlDocumentId`), the ConversationPane focus stamp goes stale — the deterministic
        // "Summarize this email" chip would target the previously-viewed email. When the updated tab
        // IS the active tab AND its data is an Email payload, re-broadcast `active_widget_changed`
        // (the EXISTING ADR-030 event — no new type) so the stamp refreshes to the current email.
        // Both subscribers tolerate the extra broadcast idempotently: `ReviewCompleteToast` only sets
        // `activeWidgetTypeRef`; ConversationPane's `fireProactiveSuggestion` is once-per-tabId
        // guarded (so this never causes a proactive-suggest re-fire).
        const updated = manager.getSnapshot();
        if (updated.activeTabId === event.tabId) {
          const activeTab = updated.tabs.find((t) => t.id === event.tabId);
          const widgetData = activeTab?.widgetData as { kind?: unknown } | null | undefined;
          if (activeTab && widgetData != null && widgetData.kind === 'Email') {
            broadcastActiveTabChange({
              tabId: activeTab.id,
              widgetType: activeTab.widgetType,
              widgetData: activeTab.widgetData,
              displayName: activeTab.displayName,
              kind: activeTab.kind,
            });
          }
        }
      }
    } else if (event.type === 'widget_action') {
      // Forward widget_action events are handled by the widget itself via
      // the bus — WorkspacePane is a transparent router here.
      // No tab-manager state change needed.
    }
  });

  // ---------------------------------------------------------------------------
  // PaneEventBus subscription — 'conversation' channel (AIPU2-102)
  //
  // Receives `playbook-selected` events dispatched by PlaybookGalleryWidget
  // when the user picks a playbook from the gallery in the Context pane.
  //
  // Behaviour:
  //   isExclusive === true  → clear all existing tabs, then seed defaultWidgets
  //   isExclusive === false → keep existing tabs, then seed defaultWidgets (additive)
  //   defaultWidgets empty  → no tab seeding (workspace retains current state)
  //
  // Each defaultWidget follows the same addTab → resolveWorkspaceWidget path
  // used by server-initiated widget_load events, ensuring identical tab lifecycle.
  // ---------------------------------------------------------------------------

  usePaneEvent('conversation', (event: ConversationPaneEvent): void => {
    if (event.type !== 'playbook-selected') return;

    const manager = managerRef.current;
    const defaultWidgets = event.defaultWidgets ?? [];
    const isExclusive = event.isExclusive ?? false;

    // Clear existing tabs when the playbook is exclusive (guardrail mode).
    //
    // No-collateral-teardown (task 020 / FR-C2 / UC-4): this teardown is an
    // ORCHESTRATED action's side-effect. It must stay scoped to the workspace
    // "stage" it owns and MUST NOT tear down an unrelated live Compose tab
    // (the editor holds an unsaved draft/document — losing it was the UC-4
    // regression). Preserve 'compose' work-product tabs across the clear.
    if (isExclusive && manager.getSnapshot().tabs.length > 0) {
      manager.clearAllTabs({ preserveWidgetTypes: ['compose'] });
      syncState();
      // Emit tabs_clear so subscribers (e.g. ContextPaneController) can reset.
      dispatch('workspace', { type: 'tabs_clear' });
    }

    // Seed each default widget as a new tab.
    // When defaultWidgets is empty the workspace retains its current state.
    for (const widgetConfig of defaultWidgets) {
      const widgetType = widgetConfig.widgetType;
      const widgetData = widgetConfig.widgetData ?? null;
      const meta = getWorkspaceWidgetMetadata(widgetType);
      const displayName = widgetConfig.displayName ?? meta?.displayName ?? widgetType;

      const tabId = manager.addTab(widgetType, widgetData, displayName);
      syncState();

      // Lazy-resolve the widget component — same pattern as workspace channel.
      resolveWorkspaceWidget(widgetType).then(Component => {
        const resolvedMeta = getWorkspaceWidgetMetadata(widgetType);
        manager.resolveTabComponent(tabId, Component, resolvedMeta?.displayName);
        syncState();

        // Dispatch widget_load (with tabId) so ShellStageManager can advance stage.
        dispatch('workspace', { type: 'widget_load', widgetType, tabId });
      });
    }
  });

  // ---------------------------------------------------------------------------
  // Tab change handler — called by WorkspaceTabManagerComponent
  // ---------------------------------------------------------------------------

  const handleTabChange = React.useCallback(
    (tabId: string): void => {
      const manager = managerRef.current;
      manager.setActiveTab(tabId);
      syncState();

      // Find the newly active tab to include widget info in the event.
      const activeTab = manager.getActiveTab();

      // Dispatch tab_change so ContextPaneController can adapt its view.
      dispatch('workspace', {
        type: 'tab_change',
        tabId,
        widgetType: activeTab?.widgetType,
        widgetData: activeTab?.widgetData,
      });
    },
    [dispatch, syncState]
  );

  // ---------------------------------------------------------------------------
  // Tab close handler — called by WorkspaceTabManagerComponent
  // ---------------------------------------------------------------------------

  // ---------------------------------------------------------------------------
  // FIX #6 (spaarkeai-compose-r2) — the Assistant's active document FOLLOWS the
  // active workspace tab. The manual "Visible to assistant" toggle + its
  // handleToggleVisibility handler were REMOVED: the SELECTED Compose tab IS
  // what the Assistant works with (implicit visibility).
  //
  // When the Compose tab is the active tab, register its document (identity +
  // extracted text) as the session's active document by driving the editor's
  // visibility conduit with visible=true (→ ComposeWorkspace.handleComposeVisibilityChange
  // → registerActiveDocument → ChatSessionFile RAG + ActiveDocument, so the
  // Assistant can answer "what file is loaded"). When ANY non-Compose tab is
  // active (Daily Briefing / dashboard / other), withdraw it with visible=false
  // so exactly one active doc = the selected Compose tab, and NONE when a
  // non-document tab is active. Switching between compose docs re-seeds the
  // single Compose tab, whose activation re-registers the new doc here.
  //
  // Reuses the SAME cross-pane conduit the removed toggle drove
  // (useComposeVisibility) — no new bus/service (§11). `composeVisibility` is
  // null until a Compose editor registers its handler (no Compose tab open /
  // standalone mount) → no-op then. Because the single Compose tab is kept
  // mounted-hidden across switches, the handler stays registered, so switching
  // AWAY reliably withdraws and switching BACK re-registers. Re-fires if the
  // handler registers while the Compose tab is already active (composeVisibility
  // null→non-null) so the doc still registers on first mount.
  // ---------------------------------------------------------------------------
  React.useEffect(() => {
    const activeTab = managerRef.current.getActiveTab();
    composeVisibility?.(activeTab?.widgetType === 'compose');
    // tabState.activeTabId drives every activation path (click, compose reuse,
    // close-restore, restore-from-persistence, auto-install); composeVisibility
    // re-runs the sync when the editor's handler registers/unregisters.
  }, [tabState.activeTabId, composeVisibility]);

  const handleTabClose = React.useCallback(
    (tabId: string): void => {
      const manager = managerRef.current;

      // UAT round-5 (item #13): an EXPLICIT close is an agency signal — do not
      // resurrect this Compose tab on return. Capture its instance key BEFORE the
      // close removes it, then drop it from the home-surface persisted snapshot.
      // (Navigation teardown never calls closeTab, so those tabs are retained.)
      if (isHomeSurfaceRef.current) {
        const closingTab = manager.getSnapshot().tabs.find(t => t.id === tabId);
        if (closingTab?.widgetType === 'compose') {
          const instanceKey = composeTabInstanceKey(closingTab);
          if (instanceKey) {
            writeComposeRunState(removePersistedComposeTab(readComposeRunState(Date.now()), instanceKey));
          }
        }
      }

      const newActiveId = manager.closeTab(tabId);
      syncState();

      const snapshot = manager.getSnapshot();
      const currentTabCount = snapshot.tabs.length;

      // Dispatch tab_count_change so ShellStageManager can revert Stage 4 → Stage 3
      // when the user closes tabs down to one, or Stage 3 → Stage 1 when all tabs close.
      dispatch('workspace', {
        type: 'tab_count_change',
        tabCount: currentTabCount,
      });

      // If closing the tab changed the active tab, dispatch a tab_change so
      // ContextPaneController can adapt its view to the new active widget.
      if (newActiveId !== null) {
        const newActive = manager.getActiveTab();
        dispatch('workspace', {
          type: 'tab_change',
          tabId: newActiveId,
          widgetType: newActive?.widgetType,
          widgetData: newActive?.widgetData,
        });
      }
    },
    [dispatch, syncState]
  );

  // ---------------------------------------------------------------------------
  // Tab data-change handler — task 025 (FR-09) live edit-state persistence
  //
  // A widget (e.g. AnalysisEditorWidget) reports a data patch via its `onDataChange`
  // prop when the user is mid-edit. The patch is MERGED onto the tab's current
  // widgetData (updateTab replaces wholesale, so the merge happens here — the widget
  // only needs to report what changed) and pushed through
  // WorkspaceTabManager.updateTab, which now fires the same persist-change signal
  // every other mutation does (see WorkspaceTabManager.updateTab fix). This is what
  // makes a live edit survive a tab close/reopen or page refresh instead of being
  // silently lost (task 025 negative acceptance criterion).
  // ---------------------------------------------------------------------------

  const handleTabDataChange = React.useCallback(
    (tabId: string, patch: unknown): void => {
      const manager = managerRef.current;
      const current = manager.getSnapshot().tabs.find(t => t.id === tabId);
      if (!current) return;

      const currentData =
        current.widgetData !== null && typeof current.widgetData === 'object'
          ? (current.widgetData as Record<string, unknown>)
          : {};
      const patchData = patch !== null && typeof patch === 'object' ? (patch as Record<string, unknown>) : {};

      manager.updateTab(tabId, { ...currentData, ...patchData });
      syncState();
    },
    [syncState]
  );

  // ---------------------------------------------------------------------------
  // Render
  //
  // FR-10: Render the shared <PaneHeader> at the top of every paint, with the
  // brand-colored AppsListRegular icon.
  //
  // FR-12 (task 032): `PaneHeader.rightSlot` hosts `WorkspacePaneMenu` — a
  // Fluent v9 Dropdown that surfaces workspace switching + "+ New Workspace"
  // wizard launch + Manage workspaces. The menu is fed tab state from
  // `WorkspaceTabManager` snapshots via the `tabs` / `activeTabId` props and
  // dispatches selection / close back through the existing `handleTabChange`
  // / `handleTabClose` callbacks.
  //
  // Wave 2b (task 109): `tabs.length === 0` is now a reachable steady state
  // (not just a single render window) — it occurs when the BFF returns no
  // default layout (cascade step 4) AND the user has no pinned workspaces.
  // We render a minimal Spinner placeholder while auth + the default-layout
  // fetch are still resolving; once they settle, the placeholder remains as
  // an empty-state hint that the user should pick from the Workspaces
  // dropdown. Operator UX rationale: no fake "Home" tab; an empty pane is
  // the honest signal that no default is configured.
  // ---------------------------------------------------------------------------

  const { tabs, activeTabId } = tabState;

  // ── Pane collapse (Task 094) ────────────────────────────────────────────
  //
  // The Workspace pane is the CENTER pane in the three-pane shell. Clicking
  // the PaneHeader (anywhere except the WorkspacePaneMenu dropdown trigger)
  // toggles collapse via `paneCollapse.toggle('workspace')`. The shared
  // PaneHeader applies stopPropagation on its rightSlot wrapper so the
  // dropdown menu doesn't bubble its click up to the header.
  const paneCollapse = usePaneCollapseContext();
  const handleHeaderCollapse = React.useCallback(() => {
    paneCollapse?.toggle('workspace');
  }, [paneCollapse]);
  const isWorkspaceExpanded = !(paneCollapse?.isCollapsed('workspace') ?? false);

  // spaarkeai-compose-r1 task 100 (Phase 10 polish, FR-S7):
  //
  // In compose-launch mode (`composeMode="editor"`) the user is locked to
  // the Compose surface — the workspace-layout picker (WorkspacePaneMenu:
  // "Select Workspace" list + "+ New Workspace" wizard + "Manage workspaces")
  // and the tab bar (WorkspaceTabManagerComponent tab strip) are suppressed
  // so the user cannot browse to Matters / Documents / Daily Briefing /
  // other layouts from inside a compose session.
  //
  // Widget-add extensibility is PRESERVED — future Compose-focused widgets
  // can still be dispatched via `widget_load` PaneEventBus events; they
  // will render normally (the tab manager still creates tabs; only the tab
  // strip UI is hidden). See `hideTabBar` prop on WorkspaceTabManagerComponent.
  const isComposeLaunchMode = isComposeLaunch;

  const header = (
    <PaneHeader
      title="Workspace"
      icon={<AppsListRegular />}
      onCollapse={paneCollapse ? handleHeaderCollapse : undefined}
      expanded={isWorkspaceExpanded}
      rightSlot={
        isComposeLaunchMode ? undefined : (
          <WorkspacePaneMenu
            tabs={tabs}
            activeTabId={activeTabId}
            onTabSelect={handleTabChange}
            onTabClose={handleTabClose}
          />
        )
      }
    />
  );

  if (tabs.length === 0) {
    // First-paint placeholder (the Home tab was removed, so `tabs.length === 0`
    // is genuinely reachable): rendered before any auto-install / restore /
    // dispatched `widget_load` has committed a tab, and whenever the user closes
    // every tab. Shows a spinner behind the header until a tab lands.
    return (
      <div className={styles.root} data-testid="workspace-first-paint">
        {header}
        <div className={styles.firstPaintPlaceholder}>
          <Spinner size="tiny" />
        </div>
      </div>
    );
  }

  return (
    <div className={styles.root}>
      {header}
      <WorkspaceTabManagerComponent
        tabs={tabs}
        activeTabId={activeTabId}
        onTabChange={handleTabChange}
        onTabClose={handleTabClose}
        onTabDataChange={handleTabDataChange}
        // UAT round-4 (item #10a): a dismissed-but-still-running review shows a tiny circular
        // progress indicator on the running Compose tab header until completion.
        // UAT round-5 (item #13): the SAME slot shows while a restored Compose tab's
        // in-flight review is being resumed (poll until findings land).
        composeReviewRunning={composeReviewRunningInBackground || composeReviewResuming}
        // spaarkeai-compose-r1 task 100 — suppress the tab strip in compose
        // mode; the Compose widget renders full-pane. See the block comment
        // above the header definition for rationale + widget-add contract.
        hideTabBar={isComposeLaunchMode}
      />

      {/* ai-advanced-capabilities-analysis-hub-r1 (tabbed Quick Start): the Create
          Analysis wizard hosted AS A MODAL. Mounted only while open; `embedded:false`
          makes CreateRecordWizard render its own Fluent Dialog (portal). On close/finish
          `onRequestClose` unmounts it; on finish the wizard also opens its result tab. */}
      {createAnalysisModal.open && (
        <CreateAnalysisWizardWidget
          widgetType="create-analysis-wizard"
          data={
            {
              embedded: false,
              workTypeValue: createAnalysisModal.workTypeValue,
              workTypeLabel: createAnalysisModal.workTypeLabel,
              dataService: analysisWizardDataService,
              navigationService: analysisWizardNavigationService,
              searchUsers: analysisWizardSearchUsers,
              searchAssignees: analysisWizardSearchUsers,
              resolveSpeContainerId: resolveAnalysisSpeContainerId,
              authenticatedFetch,
              bffBaseUrl,
              ...(chatSessionId ? { sessionId: chatSessionId } : {}),
              ...(analysisInitialAssociation ? { initialAssociation: analysisInitialAssociation } : {}),
              onRequestClose: () => setCreateAnalysisModal({ open: false }),
            } as CreateAnalysisWizardData
          }
        />
      )}
    </div>
  );
}
