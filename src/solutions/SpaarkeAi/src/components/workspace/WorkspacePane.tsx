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
// Those symbols are no longer needed at this site; consumers
// (executeSummarizeIntent.ts + FilePreviewContextWidget.dispatchSummarizeOnly)
// own them now.
import { buildBffApiUrl } from "@spaarke/auth";
import { usePaneCollapseContext } from "../shell/ThreePaneShell";
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
  // ---------------------------------------------------------------------------

  React.useEffect(() => {
    if (!chatSessionId || !bffBaseUrl || !isAuthenticated) return;

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

  const autoInstalledDefaultRef = React.useRef<boolean>(false);
  React.useEffect(() => {
    if (!isAuthenticated) return;
    if (autoInstalledDefaultRef.current) return; // run once per mount
    if (!activeLayout) return; // wait for the BFF default to resolve, or stay empty if null

    // Defer the guard arming until after we actually have a default to
    // process so a transient `activeLayout === null` (cold load before fetch
    // resolves) doesn't lock the effect out.
    autoInstalledDefaultRef.current = true;

    const manager = managerRef.current;

    // Skip if this layout is already open (e.g. NFR-09 tab restore brought
    // it back from the last session). Match by widgetData.layoutId.
    const alreadyOpen = manager
      .getSnapshot()
      .tabs.some((t) => {
        if (t.widgetType !== "workspace") return false;
        const data = t.widgetData as { layoutId?: string } | null;
        return data?.layoutId === activeLayout.id;
      });
    if (alreadyOpen) return;

    // Skip if the default is in the pinned list — the pin auto-open effect
    // below will open it; we don't want to double-dispatch.
    const isPinned = getPinnedWorkspaces().some(
      (p) => p.layoutId === activeLayout.id,
    );
    if (isPinned) return;

    // Defer to a macrotask so usePaneEvent's subscription effect (declared
    // later in this component) has registered. Identical pattern to the pin
    // auto-open effect below — see that effect's block comment for the
    // subscription-race rationale.
    const timerId = window.setTimeout(() => {
      // eslint-disable-next-line no-console
      console.info(
        `[WorkspacePane] Auto-installing default workspace: ${activeLayout.name} (${activeLayout.id})`,
      );
      dispatch("workspace", {
        type: "widget_load",
        widgetType: "workspace",
        widgetData: {
          layoutId: activeLayout.id,
          layoutName: activeLayout.name,
        },
        displayName: activeLayout.name,
      });
    }, 0);

    return () => {
      window.clearTimeout(timerId);
    };
    // Run once when both auth AND activeLayout are ready; the ref guard
    // prevents re-runs on subsequent dependency changes (e.g. refetch).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isAuthenticated, activeLayout]);

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
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isAuthenticated, layouts]);

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
  // The fix shifts BOTH responsibilities to `executeSummarizeIntent` (in
  // ConversationPane's summarize-intent dispatcher):
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
      // override flag so subsequent `field_delta` / `streaming_complete`
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

  const header = (
    <PaneHeader
      title="Workspace"
      icon={<AppsListRegular />}
      onCollapse={paneCollapse ? handleHeaderCollapse : undefined}
      expanded={isWorkspaceExpanded}
      rightSlot={
        <WorkspacePaneMenu
          tabs={tabs}
          activeTabId={activeTabId}
          onTabSelect={handleTabChange}
          onTabClose={handleTabClose}
        />
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
      />
    </div>
  );
}
