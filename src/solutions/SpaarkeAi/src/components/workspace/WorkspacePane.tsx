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

import * as React from "react";
import { makeStyles, tokens, Spinner } from "@fluentui/react-components";
import { AppsListRegular } from "@fluentui/react-icons";
import { PaneHeader } from "@spaarke/ui-components";
import {
  usePaneEvent,
  useDispatchPaneEvent,
  resolveWorkspaceWidget,
  getWorkspaceWidgetMetadata,
  useAiSession,
} from "@spaarke/ai-widgets";
import type {
  WorkspacePaneEvent,
  ConversationPaneEvent,
  WorkspaceWidgetComponent,
} from "@spaarke/ai-widgets";
// R6 Hotfix Wave B-G9c2 (2026-06-10): the previously-eager Summary tab
// auto-install (R5 task 038) was removed. Each summarize invocation now
// dispatches its own `workspace.widget_load` carrying the structured-
// output-stream widget type + SUMMARIZE_SCHEMA + a per-run correlationId.
// Those symbols are no longer needed at this site; capability dispatchers
// (the shared dispatchConsumer helper via its `workspaceTarget` arg +
// FilePreviewContextWidget.dispatchSummarizeOnly) own them now.
import { buildBffApiUrl } from "@spaarke/auth";
import { usePaneCollapseContext, useComposeLaunch } from "../shell/ThreePaneShell";
// R3 ("Visible to assistant") — deep-import the cross-pane bridge hook (not the
// `@spaarke/compose-components` barrel) so this workspace-pane module does NOT transitively pull the
// TipTap editor widgets — mirrors ConversationPane's deep-import rationale. Resolves in Vite + jest.
import { useComposeVisibility } from "@spaarke/compose-components/context/composeActionBridge";
import { WorkspaceTabManager } from "./WorkspaceTabManager";
import type {
  ActiveTabSnapshot,
  WorkspaceTabManagerState,
  WorkspaceTabPersistenceSnapshot,
} from "./WorkspaceTabManager";
import { WorkspaceTabManagerComponent } from "./WorkspaceTabManagerComponent";
import { WorkspacePaneMenu } from "./WorkspacePaneMenu";
// FIX #10b — STUB email widget rendered when the Compose "Email" affordance
// (or the chat "email" chip) dispatches a `widget_load` with widgetType 'email'.
// Statically imported so the email branch resolves the component synchronously
// (no WorkspaceWidgetRegistry round-trip).
import { EmailStubWidget } from "./EmailStubWidget";
// spaarkeai-compose-r2 UNIFY — the DIRECT 'compose' widget's seed shape. Used to
// build the ribbon composeMode=editor launch seed (stored-doc pointer) that the
// workspace handler's 'compose' branch consumes.
import type { ComposeWidgetSeed } from "./composeWidgetData";
import {
  logTelemetryError,
  TELEMETRY_TAB_RESTORE_LOAD_FAILURE,
  TELEMETRY_TAB_RESTORE_SAVE_FAILURE,
  TELEMETRY_UI_ACTION_ACK_FAILURE,
} from "../../telemetry/errorTelemetry";
import {
  getPinnedWorkspaces,
  prunePinnedToKnown,
} from "../../services/pinnedWorkspaces";
// Wave 2b (task 109): the cold-load default tab is now driven by
// useWorkspaceLayouts().activeLayout (the BFF's discovered default — Daily
// Briefing in dev) instead of a hard-coded Home tab. See the auto-install
// effect below for the dispatch path.
import { useWorkspaceLayouts } from "../../hooks/useWorkspaceLayouts";

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    width: "100%",
    overflow: "hidden",
    backgroundColor: tokens.colorNeutralBackground2,
  },

  // ── First-paint / empty-state placeholder. Wave 2b (task 109) — used in
  //    two cases now: (a) the brief window between mount and the
  //    auto-install-default effect dispatching the default workspace tab,
  //    and (b) when the BFF returns NO default (cascade step 4) — the user
  //    sees an empty pane and can pick from the Workspaces dropdown.
  firstPaintPlaceholder: {
    flex: 1,
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
  },
});

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
  if (widgetData === null || typeof widgetData !== "object") return undefined;
  const compose = (widgetData as { compose?: unknown }).compose;
  if (compose === null || typeof compose !== "object") return undefined;
  const c = compose as {
    draft?: { ledgerRef?: string; fileName?: string; html?: string };
    upload?: { sessionFileId?: string; fileName?: string };
    speDriveItemId?: string;
    sprkDocumentId?: string;
    fileName?: string;
  };

  if (typeof c.draft?.ledgerRef === "string" && c.draft.ledgerRef.length > 0) {
    // `<bindingId>@t<turn>` → strip the per-turn suffix so re-drafts of the SAME binding reuse.
    return `draft:${c.draft.ledgerRef.replace(/@t\d+$/i, "")}`;
  }
  if (typeof c.upload?.sessionFileId === "string" && c.upload.sessionFileId.length > 0) {
    return `upload:${c.upload.sessionFileId}`;
  }
  if (typeof c.speDriveItemId === "string" && c.speDriveItemId.length > 0) {
    return `stored:${c.speDriveItemId}`;
  }
  if (typeof c.sprkDocumentId === "string" && c.sprkDocumentId.length > 0) {
    return `stored:${c.sprkDocumentId}`;
  }
  const fn = c.upload?.fileName ?? c.draft?.fileName ?? c.fileName;
  if (typeof fn === "string" && fn.length > 0) {
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

  const { bffBaseUrl, authenticatedFetch, chatSessionId, isAuthenticated } =
    useAiSession();

  // ---------------------------------------------------------------------------
  // Tab manager — single instance per WorkspacePane mount
  // ---------------------------------------------------------------------------

  // Forwarding ref: the manager's onPersistChange callback dereferences this
  // on every mutation. The actual `persistTabs` function below is rebuilt with
  // useCallback (it captures sessionId/bffBaseUrl) and assigned into the ref
  // on each render — so the manager always calls the latest persistTabs.
  const persistTabsRef = React.useRef<
    ((snapshot: WorkspaceTabPersistenceSnapshot) => void) | null
  >(null);

  // Round 4 Fix 4: Forwarding ref for the active-tab-change signal. Same
  // pattern as persistTabsRef — keeps the manager construction stable while
  // letting the dispatch closure capture the latest `dispatch` reference.
  const activeTabChangeRef = React.useRef<
    ((snapshot: ActiveTabSnapshot) => void) | null
  >(null);

  // Stable manager reference — never recreated across re-renders.
  // The onPersistChange / onActiveTabChange callbacks are themselves stable;
  // they just dispatch through the current ref values (so updates to deps
  // refresh cleanly without re-instantiating the manager).
  const managerRef = React.useRef<WorkspaceTabManager>(
    new WorkspaceTabManager({
      onPersistChange: (snapshot) => {
        persistTabsRef.current?.(snapshot);
      },
      onActiveTabChange: (snapshot) => {
        activeTabChangeRef.current?.(snapshot);
      },
    }),
  );

  // React state mirrors the manager's snapshot; triggers re-renders.
  const [tabState, setTabState] = React.useState<WorkspaceTabManagerState>(() =>
    managerRef.current.getSnapshot()
  );

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

  const pendingSnapshotRef =
    React.useRef<WorkspaceTabPersistenceSnapshot | null>(null);
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
        if (!chatSessionId || !bffBaseUrl || !isAuthenticated) return;

        try {
          const url = buildBffApiUrl(
            bffBaseUrl,
            `/ai/chat/sessions/${encodeURIComponent(chatSessionId)}/tabs`,
          );
          const response = await authenticatedFetch(url, {
            method: "PATCH",
            headers: {
              "Content-Type": "application/json",
              Accept: "application/json",
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
    [chatSessionId, bffBaseUrl, isAuthenticated, authenticatedFetch],
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

      const ackUrl = buildBffApiUrl(
        bffBaseUrl,
        `/ai/chat/sessions/${encodeURIComponent(chatSessionId)}/ack`,
      );
      void authenticatedFetch(ackUrl, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Accept: "application/json",
        },
        body: JSON.stringify({ frameId }),
      }).catch((err) => {
        logTelemetryError(TELEMETRY_UI_ACTION_ACK_FAILURE, {
          sessionId: chatSessionId,
          message: err instanceof Error ? err.message : String(err),
        });
      });
    },
    [chatSessionId, bffBaseUrl, isAuthenticated, authenticatedFetch],
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

      dispatch("workspace", {
        type: "active_widget_changed",
        widgetType: snapshot.widgetType,
        widgetData: snapshot.widgetData,
        tabId: snapshot.tabId,
        displayName: snapshot.displayName ?? snapshot.widgetType,
      });
    },
    [dispatch],
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

  React.useEffect(() => {
    if (!bffBaseUrl || !isAuthenticated) return;
    if (!chatSessionId) {
      // No chat session ⇒ nothing was ever persisted for it — unblock the
      // auto-install/pin effects instead of deadlocking them. If a session id
      // appears later this effect re-runs and restore proceeds (its manager-
      // level guard still protects any tabs opened in the meantime).
      setTabRestoreSettled(true);
      return;
    }

    let cancelled = false;
    (async () => {
      try {
        const url = buildBffApiUrl(
          bffBaseUrl,
          `/ai/chat/sessions/${encodeURIComponent(chatSessionId)}/tabs`,
        );
        const response = await authenticatedFetch(url, {
          method: "GET",
          headers: { Accept: "application/json" },
        });
        if (cancelled) return;
        if (response.status === 404) return; // no tabs to restore — benign
        if (!response.ok) throw new Error(`HTTP ${response.status}`);

        const snapshot =
          (await response.json()) as WorkspaceTabPersistenceSnapshot;
        if (cancelled) return;

        await managerRef.current.restoreFromPersistence(
          snapshot,
          resolveWorkspaceWidget,
        );
        if (cancelled) return;
        syncState();

        // Notify ShellStageManager about the restored tab count so it can
        // advance to the appropriate stage (Stage 3 / Stage 4).
        const snap = managerRef.current.getSnapshot();
        dispatch("workspace", {
          type: "tab_count_change",
          tabCount: snap.tabs.length,
        });
      } catch (err) {
        if (cancelled) return;
        logTelemetryError(TELEMETRY_TAB_RESTORE_LOAD_FAILURE, {
          sessionId: chatSessionId,
          message: err instanceof Error ? err.message : String(err),
        });
        // Degrade gracefully — workspace continues with Home-only state.
      } finally {
        // R3-3: settle on EVERY terminal path (success, 404, error) so the
        // auto-install-default + pin auto-open effects below can proceed.
        if (!cancelled) setTabRestoreSettled(true);
      }
    })();

    return () => {
      cancelled = true;
    };
    // authenticatedFetch is a stable module-level function from @spaarke/auth
    // (returned by useAiSession() but identical reference across renders).
    // Including it in deps would re-fire the effect needlessly.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [chatSessionId, bffBaseUrl, isAuthenticated]);

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
  const isComposeLaunch = composeLaunch?.composeMode === "editor";

  // Build the DIRECT-widget seed from the launch context's stored document.
  // main.tsx parses the ribbon URL params (sprkDocumentId / speDriveItemId /
  // speDriveId / speFileName) into `composeLaunch.document` + `.driveId`; we map
  // them onto the stored-document door of the compose seed. An empty seed (no
  // document — should not happen for the ribbon path, which always carries a
  // stored doc) opens the Compose empty state.
  const composeLaunchSeed = React.useMemo<ComposeWidgetSeed>(() => {
    if (!isComposeLaunch) return {};
    const doc = composeLaunch?.document ?? null;
    const driveId = composeLaunch?.driveId ?? "";
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
    const existingTab = manager
      .getSnapshot()
      .tabs.find((t) => {
        if (t.widgetType !== "workspace") return false;
        const data = t.widgetData as { layoutId?: string } | null;
        return data?.layoutId === layoutForAutoInstall.id;
      });
    if (existingTab) return;

    // Skip if the default is in the pinned list — the pin auto-open effect
    // below will open it; we don't want to double-dispatch.
    const isPinned = getPinnedWorkspaces().some(
      (p) => p.layoutId === layoutForAutoInstall.id,
    );
    if (isPinned) return;

    // Defer to a macrotask so usePaneEvent's subscription effect (declared
    // later in this component) has registered. Identical pattern to the pin
    // auto-open effect below — see that effect's block comment for the
    // subscription-race rationale.
    const timerId = window.setTimeout(() => {
      // eslint-disable-next-line no-console
      console.info(
        `[WorkspacePane] Auto-installing default workspace: ${layoutForAutoInstall.name} (${layoutForAutoInstall.id})`,
      );
      dispatch("workspace", {
        type: "widget_load",
        widgetType: "workspace",
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
  }, [isAuthenticated, isComposeLaunch, tabRestoreSettled, layoutForAutoInstall]);

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
      console.info("[WorkspacePane] Auto-installing compose (direct widget)");
      dispatch("workspace", {
        type: "widget_load",
        widgetType: "compose",
        widgetData: { compose: composeLaunchSeed },
        displayName: "Compose",
      });
    }, 0);

    return () => {
      window.clearTimeout(timerId);
    };
    // composeLaunchSeed is intentionally omitted from deps — the ref guard runs
    // this once per mount; the seed is stable for the life of a compose launch.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isComposeLaunch, isAuthenticated, tabRestoreSettled]);

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
    const knownLayoutIds = new Set(layouts.map((l) => l.id));
    const pinned = prunePinnedToKnown(knownLayoutIds);
    if (pinned.length === 0) return;

    const manager = managerRef.current;
    const openLayoutIds = new Set<string>(
      manager
        .getSnapshot()
        .tabs.filter((t) => t.widgetType === "workspace")
        .map((t) => {
          const data = t.widgetData as { layoutId?: string } | null;
          return data?.layoutId ?? "";
        })
        .filter((id): id is string => id.length > 0),
    );

    // Filter to the pins that actually need opening so we can log + skip
    // cleanly if there's nothing to do.
    const pinsToOpen = pinned.filter(
      (pin) => !openLayoutIds.has(pin.layoutId),
    );
    if (pinsToOpen.length === 0) return;

    // Defer dispatch to a macrotask so usePaneEvent's subscription effect
    // (declared later in this component) has had a chance to register on the
    // workspace channel. Without this, dispatches land on a zero-subscriber
    // channel and are silently dropped — see block comment above.
    const timerId = window.setTimeout(() => {
      // eslint-disable-next-line no-console
      console.info(
        `[WorkspacePane] Auto-opening ${pinsToOpen.length} pinned workspace(s):`,
        pinsToOpen,
      );
      for (const pin of pinsToOpen) {
        dispatch("workspace", {
          type: "widget_load",
          widgetType: "workspace",
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

  usePaneEvent("workspace", (event: WorkspacePaneEvent): void => {
    const manager = managerRef.current;

    // FR-34 D-F3 (task 071): the deferred CONTENT-render ack. ComposeWorkspace emits
    // `compose_content_rendered` once a seeded draft actually renders in the editor.
    // If we deferred an ack for this ledgerRef on the originating `workspace_open_tab`
    // frame, fire it NOW (the genuine "content is on screen" confirmation) — mirroring
    // the tab-open ack, only later + honest. No pending entry ⇒ no-op (a client-
    // originated open, or a render we never gated on).
    if (event.type === "compose_content_rendered") {
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

    if (event.type === "widget_load" && !event.tabId) {
      // Guard: ignore our own re-dispatched widget_load confirmations (which carry tabId).
      // Only the server-initiated events (no tabId) should open a new tab.
      const widgetType = event.widgetType ?? "unknown";
      const widgetData = event.widgetData ?? null;

      // ── FIX #10b — STUB email tab ──────────────────────────────────────────
      // The Compose "Email" affordance (ComposeAiToolbar → handleEmailAction) and
      // the chat "email" chip dispatch widgetType 'email' (layoutName 'Email').
      // Resolve EmailStubWidget SYNCHRONOUSLY (statically imported — no registry
      // round-trip) and open a tab. Same addTab → confirm-dispatch pattern as the
      // generic path below; auto-activates via addTab.
      const emailData = widgetData as { layoutName?: string } | null;
      if (widgetType === "email" || emailData?.layoutName === "Email") {
        const emailDisplayName = event.displayName ?? "Email";
        const emailTabId = manager.addTab("email", widgetData, emailDisplayName);
        // Cast to the registry's WorkspaceWidgetComponent (matches the 'compose'
        // registry path) — EmailStubWidget takes WorkspaceWidgetProps<EmailWidgetData>.
        manager.resolveTabComponent(
          emailTabId,
          EmailStubWidget as unknown as WorkspaceWidgetComponent,
          emailDisplayName,
        );
        syncState();
        const emailSnapshot = manager.getSnapshot();
        dispatch("workspace", {
          type: "widget_load",
          widgetType: "email",
          tabId: emailTabId,
          ...(emailSnapshot.tabs.length > 0 ? { tabCount: emailSnapshot.tabs.length } : {}),
        });
        dispatch("workspace", {
          type: "tab_count_change",
          tabCount: emailSnapshot.tabs.length,
        });
        // D-F3 truthfulness parity with the generic path — ack a server frame if present.
        if (event.frameId) {
          sendUiActionAck(event.frameId);
        }
        return;
      }

      // Resolve the tab display name with this precedence:
      //   1. Event payload `displayName` (Round 4 Fix 4: lets the menu set the
      //      tab title to a per-instance label such as "Corporate Workspace"
      //      rather than the generic registry label "Workspace").
      //   2. Registry metadata `displayName`.
      //   3. The raw widgetType string as last resort.
      const meta = getWorkspaceWidgetMetadata(widgetType);
      const displayName =
        event.displayName ?? meta?.displayName ?? widgetType;

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
        widgetType === "workspace" &&
        ((widgetData as { layoutName?: string } | null)?.layoutName ===
          "Compose" ||
          event.displayName === "Compose");
      const effectiveWidgetType = isComposeLayoutLoad ? "compose" : widgetType;

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
      if (effectiveWidgetType === "compose") {
        // FR-34 D-F3 (task 071): a CONTENT-bearing open carries a full-document
        // draft SEED (widgetData.compose.draft.ledgerRef — DEF-08 Part A). When
        // present, the ack is DEFERRED until ComposeWorkspace signals the draft
        // actually rendered (compose_content_rendered), keyed by this ledgerRef.
        // Absent for upload/stored/empty opens, which ack on tab-open as before.
        const composeData = widgetData as
          | { compose?: { draft?: { ledgerRef?: string } } }
          | null;
        const composeDraftLedgerRef =
          composeData?.compose && typeof composeData.compose === "object"
            ? composeData.compose.draft?.ledgerRef
            : undefined;

        // R3 filename contract — hoist the loaded document's filename to a TOP-LEVEL `filename` on
        // the compose tab's widgetData so it is readable server-side (a sibling server agent maps it
        // to a DocumentViewer visible-state). Sourced from the seed's known locations (upload /
        // draft) or an already-hoisted top-level value. When a re-seed carries none (e.g. an
        // add-to-DMS re-activation with only a `source` marker), the existing tab's filename is
        // preserved rather than clobbered with undefined.
        const composeSeed = widgetData as
          | {
              filename?: string;
              compose?: {
                fileName?: string;
                upload?: { fileName?: string };
                draft?: { fileName?: string };
              };
            }
          | null;
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
          typeof widgetData === "object" &&
          typeof (widgetData as { compose?: unknown }).compose === "object" &&
          (widgetData as { compose?: unknown }).compose !== null;

        const snapshot0 = manager.getSnapshot();
        const composeTabs = snapshot0.tabs.filter((t) => t.widgetType === "compose");
        let reuseTab: (typeof composeTabs)[number] | undefined;
        if (instanceKey) {
          reuseTab = composeTabs.find((t) => composeTabInstanceKey(t) === instanceKey);
        } else if (!hasComposeSeed && !isComposeLayoutLoad) {
          // Seedless source-only re-activation — reuse the ACTIVE compose tab, else the first open
          // one. Never mints a duplicate (source-only opens carry no new document). A blank menu
          // "Compose" open (isComposeLayoutLoad) is EXCLUDED here so it mints a new tab below.
          reuseTab =
            composeTabs.find((t) => t.id === snapshot0.activeTabId) ?? composeTabs[0];
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
            dispatch("workspace", {
              type: "tab_change",
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
        const composeWidgetData = seedFilename
          ? { ...(widgetData ?? {}), filename: seedFilename }
          : widgetData;
        const composeTabId = manager.addTab("compose", composeWidgetData, displayName);
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
        resolveWorkspaceWidget("compose").then((Component) => {
          const resolvedMeta = getWorkspaceWidgetMetadata("compose");
          manager.resolveTabComponent(
            composeTabId,
            Component,
            event.displayName ? undefined : resolvedMeta?.displayName,
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
          dispatch("workspace", {
            type: "widget_load",
            widgetType: "compose",
            tabId: composeTabId,
            ...(currentTabCount > 0 ? { tabCount: currentTabCount } : {}),
          });
          dispatch("workspace", {
            type: "tab_count_change",
            tabCount: currentTabCount,
          });
        });
        return;
      }

      // Add the tab — this enforces MAX_WORKSPACE_TABS eviction internally.
      const tabId = manager.addTab(widgetType, widgetData, displayName);
      syncState();

      // Lazy-resolve the widget component; update the tab once resolved.
      resolveWorkspaceWidget(widgetType).then((Component) => {
        const resolvedMeta = getWorkspaceWidgetMetadata(widgetType);
        // Round 4 Fix 4: preserve a per-instance displayName from the event
        // payload (e.g. "Corporate Workspace") over the registry's generic
        // label (e.g. "Workspace"). Pass `undefined` for displayName when the
        // event carried one so resolveTabComponent does not overwrite it.
        manager.resolveTabComponent(
          tabId,
          Component,
          event.displayName ? undefined : resolvedMeta?.displayName,
        );
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
        dispatch("workspace", {
          type: "widget_load",
          widgetType,
          tabId,
          ...(currentTabCount > 0 ? { tabCount: currentTabCount } : {}),
        });

        // Dispatch tab_count_change so ShellStageManager can drive Stage 3↔4.
        dispatch("workspace", {
          type: "tab_count_change",
          tabCount: currentTabCount,
        });
      });
    } else if (event.type === "widget_update") {
      if (event.tabId) {
        manager.updateTab(event.tabId, event.widgetData ?? null);
        syncState();
      }
    } else if (event.type === "widget_action") {
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

  usePaneEvent("conversation", (event: ConversationPaneEvent): void => {
    if (event.type !== "playbook-selected") return;

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
      manager.clearAllTabs({ preserveWidgetTypes: ["compose"] });
      syncState();
      // Emit tabs_clear so subscribers (e.g. ContextPaneController) can reset.
      dispatch("workspace", { type: "tabs_clear" });
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
      resolveWorkspaceWidget(widgetType).then((Component) => {
        const resolvedMeta = getWorkspaceWidgetMetadata(widgetType);
        manager.resolveTabComponent(tabId, Component, resolvedMeta?.displayName);
        syncState();

        // Dispatch widget_load (with tabId) so ShellStageManager can advance stage.
        dispatch("workspace", { type: "widget_load", widgetType, tabId });
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
      dispatch("workspace", {
        type: "tab_change",
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
    composeVisibility?.(activeTab?.widgetType === "compose");
    // tabState.activeTabId drives every activation path (click, compose reuse,
    // close-restore, restore-from-persistence, auto-install); composeVisibility
    // re-runs the sync when the editor's handler registers/unregisters.
  }, [tabState.activeTabId, composeVisibility]);

  const handleTabClose = React.useCallback(
    (tabId: string): void => {
      const manager = managerRef.current;
      const newActiveId = manager.closeTab(tabId);
      syncState();

      const snapshot = manager.getSnapshot();
      const currentTabCount = snapshot.tabs.length;

      // Dispatch tab_count_change so ShellStageManager can revert Stage 4 → Stage 3
      // when the user closes tabs down to one, or Stage 3 → Stage 1 when all tabs close.
      dispatch("workspace", {
        type: "tab_count_change",
        tabCount: currentTabCount,
      });

      // If closing the tab changed the active tab, dispatch a tab_change so
      // ContextPaneController can adapt its view to the new active widget.
      if (newActiveId !== null) {
        const newActive = manager.getActiveTab();
        dispatch("workspace", {
          type: "tab_change",
          tabId: newActiveId,
          widgetType: newActive?.widgetType,
          widgetData: newActive?.widgetData,
        });
      }
    },
    [dispatch, syncState]
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
    paneCollapse?.toggle("workspace");
  }, [paneCollapse]);
  const isWorkspaceExpanded = !(paneCollapse?.isCollapsed("workspace") ?? false);

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
        // spaarkeai-compose-r1 task 100 — suppress the tab strip in compose
        // mode; the Compose widget renders full-pane. See the block comment
        // above the header definition for rationale + widget-add contract.
        hideTabBar={isComposeLaunchMode}
      />
    </div>
  );
}
