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
import type { WorkspacePaneEvent, ConversationPaneEvent } from "@spaarke/ai-widgets";
// R6 Hotfix Wave B-G9c2 (2026-06-10): the previously-eager Summary tab
// auto-install (R5 task 038) was removed. Each summarize invocation now
// dispatches its own `workspace.widget_load` carrying the structured-
// output-stream widget type + SUMMARIZE_SCHEMA + a per-run correlationId.
// Those symbols are no longer needed at this site; capability dispatchers
// (the shared dispatchConsumer helper via its `workspaceTarget` arg +
// FilePreviewContextWidget.dispatchSummarizeOnly) own them now.
import { buildBffApiUrl } from "@spaarke/auth";
import { usePaneCollapseContext, useComposeLaunch } from "../shell/ThreePaneShell";
import { WorkspaceTabManager } from "./WorkspaceTabManager";
import type {
  ActiveTabSnapshot,
  WorkspaceTabManagerState,
  WorkspaceTabPersistenceSnapshot,
} from "./WorkspaceTabManager";
import { WorkspaceTabManagerComponent } from "./WorkspaceTabManagerComponent";
import { WorkspacePaneMenu } from "./WorkspacePaneMenu";
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

  // spaarkeai-compose-r1 task 092: when App.tsx was launched with
  // `composeMode=editor` (ribbon Open-in-Compose modal path), override the
  // BFF-default active layout with the "Compose" workspace layout (system
  // row `sprk_workspacelayoutid=c09d26be-e173-f111-ab0e-7ced8ddc4a05` created
  // by W1a-010). Resolved by NAME here so no client-side GUID pinning is
  // required. Falls back to the BFF default when the Compose row is missing
  // from the returned layouts (defensive — should never happen post-deploy).
  const composeLaunch = useComposeLaunch();
  const layoutForAutoInstall = React.useMemo(() => {
    if (composeLaunch?.composeMode === "editor") {
      const composeRow = layouts.find((l) => l.name === "Compose");
      if (composeRow) return composeRow;
    }
    return activeLayout;
  }, [composeLaunch, layouts, activeLayout]);

  const autoInstalledDefaultRef = React.useRef<boolean>(false);
  React.useEffect(() => {
    if (!isAuthenticated) return;
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
    if (existingTab) {
      // Issue #572 Defect 1d: in compose-launch mode we must ACTIVATE the
      // restored Compose tab, not just skip the install. NFR-09 restore
      // honors the persisted activeTabId and never force-activates — so on
      // a compose relaunch the restored session could land on a different
      // tab, and with the tab strip hidden in compose-launch mode the user
      // had no way to reach the Compose surface (relaunch showed the normal
      // three-pane workspace instead of the editor). This mirrors the
      // "always want the Compose layout on top" rule the pin-skip guard
      // below already documents.
      if (composeLaunch?.composeMode === "editor") {
        manager.setActiveTab(existingTab.id);
        syncState();
        // Same macrotask deferral as the widget_load dispatch below — the
        // tab_change subscribers (ContextPaneController) register in effects
        // that may not have run yet on a fresh mount.
        const activateTimerId = window.setTimeout(() => {
          dispatch("workspace", {
            type: "tab_change",
            tabId: existingTab.id,
            widgetType: existingTab.widgetType,
            widgetData: existingTab.widgetData,
          });
        }, 0);
        return () => {
          window.clearTimeout(activateTimerId);
        };
      }
      return;
    }

    // Skip if the default is in the pinned list — the pin auto-open effect
    // below will open it; we don't want to double-dispatch. This check does
    // NOT apply in compose-launch mode: we always want the Compose layout on
    // top so the user lands in the editor regardless of their pin list.
    if (composeLaunch?.composeMode !== "editor") {
      const isPinned = getPinnedWorkspaces().some(
        (p) => p.layoutId === layoutForAutoInstall.id,
      );
      if (isPinned) return;
    }

    // Defer to a macrotask so usePaneEvent's subscription effect (declared
    // later in this component) has registered. Identical pattern to the pin
    // auto-open effect below — see that effect's block comment for the
    // subscription-race rationale.
    const timerId = window.setTimeout(() => {
      // eslint-disable-next-line no-console
      console.info(
        `[WorkspacePane] Auto-installing ${
          composeLaunch?.composeMode === "editor" ? "compose" : "default"
        } workspace: ${layoutForAutoInstall.name} (${layoutForAutoInstall.id})`,
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
  }, [isAuthenticated, tabRestoreSettled, layoutForAutoInstall]);

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

  // Retained as a no-op ref so `handleTabChange` (manual override semantics)
  // can continue to compare against the current Summary tab id when one is
  // present. With the deferred-install model, this is `null` until a
  // summarize run dispatches a `widget_load` AND we narrow the dispatched
  // tab into this ref. For the current B-G9c2 implementation we no longer
  // need the manual-override behavior to be Summary-specific (each run gets
  // its own tab; the user can freely click between tabs without affecting
  // future runs), so this ref stays `null` permanently. Removing the ref
  // entirely would force a wider refactor of `handleTabChange` — keeping
  // the variable as a benign null sentinel keeps the diff small.
  const summaryTabIdRef = React.useRef<string | null>(null);

  // ---------------------------------------------------------------------------
  // R6 Hotfix Wave B-G9c2 — auto-focus is now NATURAL via `addTab`
  //
  // The R5 task 038 streaming-started auto-focus block (removed here) is
  // unnecessary in the deferred-install model: each summarize run dispatches
  // a `workspace.widget_load`, the existing `widget_load` handler below
  // calls `manager.addTab(...)` which AUTO-ACTIVATES the new tab (see
  // WorkspaceTabManager.addTab line 378), so the new Summary tab is focused
  // as soon as the run starts — no separate `streaming_started` focus
  // handler required.
  //
  // The `streamFocusOverrideRef` is retained as a no-op sentinel so the
  // existing manual-override checks in `handleTabChange` don't have to
  // change. With each run owning its own tab, the override semantic is now
  // mostly vestigial — kept for compatibility with downstream consumers.
  // ---------------------------------------------------------------------------

  const streamFocusOverrideRef = React.useRef<boolean>(false);

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

      // Resolve the tab display name with this precedence:
      //   1. Event payload `displayName` (Round 4 Fix 4: lets the menu set the
      //      tab title to a per-instance label such as "Corporate Workspace"
      //      rather than the generic registry label "Workspace").
      //   2. Registry metadata `displayName`.
      //   3. The raw widgetType string as last resort.
      const meta = getWorkspaceWidgetMetadata(widgetType);
      const displayName =
        event.displayName ?? meta?.displayName ?? widgetType;

      // ── DEF-08 single-tab reuse ────────────────────────────────────────────
      // A Compose-editor open (a compose-SEEDED workspace layout tab — a chat
      // "open as a document" draft, an "Open in Compose" affordance, or a
      // stored/upload compose open) must REUSE the single existing Compose tab
      // rather than mint a NEW (often blank) one on every open. This fixes the
      // accumulated-blank-Compose-tabs side effect: repeated opens ACTIVATE the
      // one Compose tab (refreshing its seed) instead of stacking duplicates.
      // Match an existing "workspace" tab by layoutId. Non-compose widget opens
      // are unaffected (they still addTab as before).
      const composeOpen = widgetData as
        | { compose?: unknown; layoutId?: string; layoutName?: string }
        | null;
      const isComposeLayoutOpen =
        widgetType === "workspace" &&
        composeOpen != null &&
        (composeOpen.compose != null || composeOpen.layoutName === "Compose");
      // FR-34 D-F3 (task 071): a CONTENT-bearing open carries a full-document draft SEED
      // (widgetData.compose.draft.ledgerRef — DEF-08 Part A). When present, the ack for this
      // frame is DEFERRED (below) until ComposeWorkspace signals the draft actually rendered,
      // keyed by this ledgerRef. Typed narrowing — no `any` (ADR-030). Absent for plain layout
      // opens, upload mounts, and Part-B inline drafts, all of which ack on tab-open as before.
      const composeDraftLedgerRef =
        isComposeLayoutOpen && composeOpen?.compose && typeof composeOpen.compose === "object"
          ? (composeOpen.compose as { draft?: { ledgerRef?: string } }).draft?.ledgerRef
          : undefined;
      // Part B dispatches by layout NAME only (no fetch in the Assistant pane) — resolve the id
      // from the layouts list this pane already holds so the tab renders + reuse can match by id.
      let composeLayoutId = composeOpen?.layoutId;
      if (isComposeLayoutOpen && (!composeLayoutId || composeLayoutId.length === 0) && composeOpen?.layoutName) {
        composeLayoutId = layouts.find((l) => l.name === composeOpen.layoutName)?.id;
      }
      // Ensure the widgetData carries the resolved layoutId so the Compose layout renders.
      const composeWidgetData =
        isComposeLayoutOpen && composeLayoutId && composeOpen && composeOpen.layoutId !== composeLayoutId
          ? { ...(widgetData as Record<string, unknown>), layoutId: composeLayoutId }
          : widgetData;
      if (isComposeLayoutOpen && typeof composeLayoutId === "string" && composeLayoutId.length > 0) {
        const existingComposeTab = manager.getSnapshot().tabs.find((t) => {
          if (t.widgetType !== "workspace") return false;
          const d = t.widgetData as { layoutId?: string } | null;
          return d?.layoutId === composeLayoutId;
        });
        if (existingComposeTab) {
          // Refresh the seed (a fresh draft picks up if the editor hadn't loaded
          // yet; a loaded editor's draft effect status-gate prevents clobbering
          // unsaved edits) and activate the single tab.
          manager.updateTab(existingComposeTab.id, composeWidgetData);
          manager.setActiveTab(existingComposeTab.id);
          syncState();
          if (event.frameId) {
            // FR-34 D-F3 (task 071): a re-seed of the single Compose tab is still a CONTENT open —
            // defer the ack to the draft's actual render if this frame carries a draft seed;
            // otherwise ack the reuse now (unchanged).
            if (composeDraftLedgerRef) {
              pendingRenderAcksRef.current.set(composeDraftLedgerRef, event.frameId);
            } else {
              sendUiActionAck(event.frameId);
            }
          }
          window.setTimeout(() => {
            dispatch("workspace", {
              type: "tab_change",
              tabId: existingComposeTab.id,
              widgetType: existingComposeTab.widgetType,
              widgetData: composeWidgetData,
            });
          }, 0);
          return;
        }
      }

      // Add the tab — this enforces MAX_WORKSPACE_TABS eviction internally.
      // (Compose opens carry the resolved layoutId so the layout renders.)
      const tabId = manager.addTab(widgetType, composeWidgetData, displayName);
      syncState();

      // D-F3 UI-action truthfulness (FR-A1-08 / task AIR2-037): the tab is NOW
      // actually materialized in this client's tab state — if the server frame
      // carried a frameId (an ack-gated tool call is waiting), ack it referencing
      // that EXACT frame id. Client-originated widget_load events (menu-opened
      // tabs) never carry a frameId, so this is a no-op for them.
      //
      // FR-34 D-F3 CONTENT-render refinement (task 071): if this frame carries a
      // full-document draft SEED (composeDraftLedgerRef present — DEF-08), the tab
      // SHELL is open but the seeded content is NOT yet on screen. DEFER the ack —
      // stash the frame id keyed by ledgerRef and fire it only when
      // ComposeWorkspace emits `compose_content_rendered` for that ledgerRef (the
      // draft actually rendered). A seed that never renders never acks → honest
      // server timeout. Non-seeded opens ack on tab-open exactly as before.
      if (event.frameId) {
        if (composeDraftLedgerRef) {
          pendingRenderAcksRef.current.set(composeDraftLedgerRef, event.frameId);
        } else {
          sendUiActionAck(event.frameId);
        }
      }

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

    // Clear all existing tabs when the playbook is exclusive (guardrail mode).
    if (isExclusive && manager.getSnapshot().tabs.length > 0) {
      manager.clearAllTabs();
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

      // R5 task 038 — Manual override for the Summary tab auto-focus.
      //
      // When the user manually clicks a tab OTHER THAN Summary, set the
      // override flag so subsequent `section_*` / `streaming_complete`
      // events in the current stream cycle do NOT pull focus back to
      // Summary. The override is reset on the NEXT `streaming_started`
      // event (so the next stream can again auto-focus) AND on
      // `streaming_complete` (defensive double-reset — see the auto-focus
      // subscription above).
      const summaryTabId = summaryTabIdRef.current;
      if (summaryTabId && tabId !== summaryTabId) {
        streamFocusOverrideRef.current = true;
      } else if (tabId === summaryTabId) {
        // User clicked back to Summary themselves — clear the override
        // (no longer in "I want to be elsewhere" mode).
        streamFocusOverrideRef.current = false;
      }

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

  // R6 Pillar 9 / task 098 — per-tab visibility toggle handler.
  // Updates the local tab record so the next system-prompt snapshot reflects
  // the new flag, then PATCHes the BFF persistence layer. We don't roll back
  // on PATCH failure (the local view stays in sync with the user's intent);
  // background reconciliation on next workspace-state fetch corrects any
  // drift if the server rejects.
  const handleToggleVisibility = React.useCallback(
    (tabId: string, visibleToAssistant: boolean): void => {
      const manager = managerRef.current;
      manager.setTabVisibility(tabId, visibleToAssistant);
      syncState();

      // Best-effort BFF persistence. Server projection already wired per R6
      // Pillar 6a/9 — the endpoint accepts a partial PATCH on the tab record
      // with the visibleToAssistant field.
      if (chatSessionId && bffBaseUrl && isAuthenticated) {
        // Issue #572 aggravator: this URL was previously built raw as
        // `${bffBaseUrl}/ai/chat/...` — missing the `/api` prefix — so the
        // per-tab request 404'd in production (App Insights). buildBffApiUrl
        // adds the `/api` prefix idempotently, matching every other BFF call
        // in this file.
        void authenticatedFetch(
          buildBffApiUrl(
            bffBaseUrl,
            `/ai/chat/sessions/${encodeURIComponent(chatSessionId)}/tabs/${encodeURIComponent(tabId)}`,
          ),
          {
            method: "PATCH",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ visibleToAssistant }),
          },
        ).catch((err) => {
          // Best-effort persistence — local view stays in sync with user
          // intent. Background reconciliation on next workspace-state fetch
          // corrects any drift if the server rejects.
          // eslint-disable-next-line no-console
          console.warn(
            `[task-098] workspace visibility PATCH failed (tabId=${tabId}, sessionId=${chatSessionId}):`,
            err,
          );
        });
      }
    },
    [chatSessionId, bffBaseUrl, isAuthenticated, authenticatedFetch, syncState],
  );

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
  const isComposeLaunchMode = composeLaunch?.composeMode === "editor";

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
    // First-paint placeholder. With the Home tab installed in the mount
    // effect, this branch is reachable only for the single render before the
    // effect commits.
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
        // R6 Pillar 9 / task 098 — per-tab "Visible to assistant" toggle.
        chatSessionId={chatSessionId}
        onToggleVisibility={handleToggleVisibility}
        // spaarkeai-compose-r1 task 100 — suppress the tab strip in compose
        // mode; the Compose widget renders full-pane. See the block comment
        // above the header definition for rationale + widget-add contract.
        hideTabBar={isComposeLaunchMode}
      />
    </div>
  );
}
