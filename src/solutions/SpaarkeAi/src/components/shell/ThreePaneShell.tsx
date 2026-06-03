/**
 * ThreePaneShell.tsx — R2 root shell component for SpaarkeAi.
 *
 * Replaces R1's AppShell (embedded in App.tsx) with a structured three-pane
 * layout that owns the PaneEventBus context and shell-level stage lifecycle.
 *
 * Provider tree (outermost → innermost):
 *   FluentProvider  (theme — owned by App.tsx, ThreePaneShell receives it via props)
 *     PaneEventBusProvider  (single bus instance for all three panes)
 *       AiSessionProvider   (session state + PaneEventBus routing)
 *         ShellStageManager (stage state machine — subscribes to bus events)
 *           ThreePaneLayout
 *             leftPane   = <ConversationPane />       (AIPU2-077)
 *             centerPane = <WorkspacePane />           (AIPU2-078)
 *             rightPane  = <ContextPaneController />   (AIPU2-079)
 *
 * Four-stage pane lifecycle (design.md Section 2.3):
 *
 *   Stage 1 — 'welcome'      Landing: no session or playbook.
 *     Conversation: welcome message + prompt buttons.
 *     Workspace:    "What would you like to work on?" + recent work cards.
 *     Context:      Playbook gallery.
 *
 *   Stage 2 — 'loading'      Playbook Selected: gathering context.
 *     Conversation: chat initialized with agent, awaiting entity/document.
 *     Workspace:    document/entity selection (Upload / Browse / Recent).
 *     Context:      entity info widget or loading spinner.
 *
 *   Stage 3 — 'active-chat'  Active Work: first document/widget loaded.
 *     Conversation: SprkChat with live exchange.
 *     Workspace:    single active widget (document viewer, report, etc.).
 *     Context:      findings, citations, sources.
 *
 *   Stage 4 — 'review'       Multi-Task: second workspace tab opened.
 *     Conversation: chat stays stable.
 *     Workspace:    tabbed widget view (tab bar visible).
 *     Context:      adapts to active workspace tab via tab_change events.
 *
 * Transitions (driven by PaneEventBus events + determineStage()):
 *   welcome → loading      playbook_change event OR first_message event
 *   loading → active-chat  widget_load (first resolved tab) OR entity resolved
 *   active-chat → review   tab_count_change with tabCount >= 2
 *   review → active-chat   tab_count_change with tabCount === 1
 *   any → welcome          session_reset event (session cleared/deleted)
 *
 * Stage determination is centralised in StageTransitionRules.determineStage().
 * ShellStageManager maintains a SessionState snapshot and recomputes the stage
 * after each bus event. This ensures all panes compute the same stage without
 * divergence or race conditions.
 *
 * @see ADR-021 - Fluent v9, dark mode required, semantic tokens only
 * @see ADR-022 - React 19 createRoot for Code Pages
 * @see StageTransitionRules — pure stage computation (determineStage)
 * @see ThreePaneLayout — layout primitive with draggable splitters
 * @see PaneEventBusProvider — cross-pane event bus context
 */

import * as React from "react";
import { makeStyles, Toaster, useToastController, useId, Toast, ToastTitle } from "@fluentui/react-components";
import { ChatRegular, AppsListRegular, DocumentRegular } from "@fluentui/react-icons";
import { ThreePaneLayout } from "@spaarke/ui-components";
import type { EntityContext, EntityType } from "@spaarke/ai-context";
import {
  PaneEventBusProvider,
  usePaneEvent,
  useDispatchPaneEvent,
  useAiSession,
  AiSessionProvider,
  determineStage,
  shouldReset,
} from "@spaarke/ai-widgets";
import type { SessionState } from "@spaarke/ai-widgets";
import { ConversationPane } from "../conversation/ConversationPane";
import { ContextPaneController } from "../context/ContextPaneController";
import { WorkspacePane } from "../workspace/WorkspacePane";
import { useSessionRestore } from "../../hooks/useSessionRestore";
import type { SessionRestoreSpec } from "../../hooks/useSessionRestore";
import { usePaneCollapse } from "../../hooks/usePaneCollapse";
import type { PaneId } from "../../hooks/usePaneCollapse";

// ---------------------------------------------------------------------------
// ShellStage — lifecycle state type (four-stage, design.md Section 2.3)
// ---------------------------------------------------------------------------

/**
 * Shell-level lifecycle stages for the SpaarkeAi three-pane experience.
 *
 * These four stages drive high-level layout decisions — which pane content to
 * surface — and are propagated to all child panes via ShellStageContext so
 * panes can adapt without prop-drilling.
 *
 * Identical to PaneStage from StageTransitionRules.ts so the two types are
 * interchangeable. Kept as a local alias to avoid forcing every import of
 * ThreePaneShell to also depend on @spaarke/ai-widgets.
 *
 * Stage summary (design.md Section 2.3):
 *   'welcome'     — Stage 1 Landing: no session, no playbook. Playbook gallery in context.
 *   'loading'     — Stage 2 Playbook Selected: awaiting first document/entity.
 *   'active-chat' — Stage 3 Active Work: first widget loaded, full working mode.
 *   'review'      — Stage 4 Multi-Task: two or more workspace tabs open.
 */
export type ShellStage = "welcome" | "loading" | "active-chat" | "review";

// ---------------------------------------------------------------------------
// ShellStageContext — propagates stage + transition handlers to panes
// ---------------------------------------------------------------------------

export interface ShellStageContextValue {
  /** Current lifecycle stage of the shell. */
  currentStage: ShellStage;

  // ── Forward transitions ──────────────────────────────────────────────────

  /** Stage 1 → Stage 2: playbook selected OR first message sent. */
  toLoading: () => void;
  /** Stage 2 → Stage 3: first workspace widget loaded OR entity context resolved. */
  toActiveChat: () => void;
  /** Stage 3 → Stage 4: second workspace tab opened (tabCount >= 2). */
  toReview: () => void;

  // ── Reverse transitions ──────────────────────────────────────────────────

  /** Stage 4 → Stage 3: all but one workspace tab closed (tabCount === 1). */
  toActiveWork: () => void;
  /** Any → Stage 1: session cleared / deleted. */
  reset: () => void;
}

export const ShellStageContext = React.createContext<ShellStageContextValue | null>(null);
ShellStageContext.displayName = "ShellStageContext";

/**
 * Consume the shell stage and transition handlers from within any pane.
 * Must be called inside a ThreePaneShell subtree.
 */
export function useShellStage(): ShellStageContextValue {
  const ctx = React.useContext(ShellStageContext);
  if (ctx === null) {
    throw new Error(
      "[ThreePaneShell] useShellStage() must be called inside <ThreePaneShell>. " +
        "Ensure the component tree is wrapped with ThreePaneShell."
    );
  }
  return ctx;
}

// ---------------------------------------------------------------------------
// PaneCollapseContext — exposes each pane's collapse-toggle handle to the
// pane subtrees so they can wire their own <PaneHeader onCollapse={...} />.
// Lives inside ThreePaneShell because the shell owns the layout + the
// `usePaneCollapse` state. Task 094.
// ---------------------------------------------------------------------------

export interface PaneCollapseContextValue {
  isCollapsed: (id: PaneId) => boolean;
  toggle: (id: PaneId) => void;
}

export const PaneCollapseContext = React.createContext<PaneCollapseContextValue | null>(
  null
);
PaneCollapseContext.displayName = "PaneCollapseContext";

/**
 * Consume the pane-collapse controller from inside any of the three panes.
 * Returns `null` if rendered outside `<ThreePaneShell>` so consumers can
 * detect the standalone case (e.g. when the pane is unit-tested directly).
 */
export function usePaneCollapseContext(): PaneCollapseContextValue | null {
  return React.useContext(PaneCollapseContext);
}

// ---------------------------------------------------------------------------
// ThreePaneShellProps
// ---------------------------------------------------------------------------

export interface ThreePaneShellProps {
  /** BFF API base URL resolved at bootstrap from Dataverse env vars. */
  bffBaseUrl: string;
  /** BFF access token acquired via @spaarke/auth. Null while acquiring.
   * Deprecated — auth state is now read via useAiSession() inside panes.
   * Task 021 will remove this prop entirely. */
  token?: string | null;
  /** Whether authentication has completed successfully.
   * Deprecated — see token. */
  isAuthenticated?: boolean;
  /** Dataverse entity logical name from URL (e.g. "sprk_matter"). Optional. */
  entityLogicalName?: string;
  /** Dataverse entity record GUID from URL. Optional. */
  entityId?: string;
  /** Matter ID shorthand from URL. Optional. */
  matterId?: string;
  /** Session ID for session restore flow (AIPU2-106). When present, triggers restore before first render. */
  sessionId?: string;
}

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  shell: {
    display: "flex",
    width: "100%",
    height: "100%",
    overflow: "hidden",
  },
});

// ---------------------------------------------------------------------------
// Placeholder slot components
// ConversationPaneSlot replaced by ConversationPane (task AIPU2-079).
// WorkspacePaneSlot replaced by WorkspacePane (task AIPU2-077).
// ContextPaneSlot replaced by ContextPaneController (task AIPU2-078).
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// ShellStageManager — subscribes to PaneEventBus, drives stage transitions
// ---------------------------------------------------------------------------

/**
 * Internal component that lives inside PaneEventBusProvider and manages
 * stage transitions via bus subscriptions.
 *
 * Separated from ThreePaneShell to keep the outer component free of bus
 * hooks — the bus context is only available after PaneEventBusProvider mounts.
 *
 * Stage determination strategy:
 *   Rather than hard-coding per-event if/else chains, ShellStageManager
 *   maintains a `SessionState` snapshot that mirrors workspace and session
 *   status. After each bus event it calls `determineStage(sessionState)` and
 *   sets the result as the current stage. This ensures all panes always agree
 *   on the current stage because they all read from a single computed value.
 *
 * Transitions (design.md Section 2.3 + task AIPU2-105):
 *   welcome → loading      conversation/playbook_change OR conversation/first_message
 *   loading → active-chat  workspace/widget_load (first tab resolved)
 *                          OR workspace/entity_resolved
 *   active-chat → review   workspace/tab_count_change with tabCount >= 2
 *   review → active-chat   workspace/tab_count_change with tabCount === 1
 *   any → welcome          workspace/session_reset  (session cleared/deleted)
 *
 * @see StageTransitionRules — determineStage(), shouldReset()
 */
interface ShellStageManagerProps {
  children: React.ReactNode;
}

function ShellStageManager({ children }: ShellStageManagerProps): React.JSX.Element {
  // ---------------------------------------------------------------------------
  // SessionState snapshot — source of truth for determineStage()
  // Each bus event handler mutates the relevant field(s) and calls recompute().
  // ---------------------------------------------------------------------------

  const sessionRef = React.useRef<SessionState>({
    hasSession: false,
    hasWidget: false,
    tabCount: 0,
    hasEntity: false,
  });

  const [currentStage, setCurrentStage] = React.useState<ShellStage>("welcome");

  /** Recompute stage from the current SessionState snapshot. */
  const recompute = React.useCallback((): void => {
    const next = determineStage(sessionRef.current) as ShellStage;
    setCurrentStage((prev) => (prev !== next ? next : prev));
  }, []);

  // ---------------------------------------------------------------------------
  // Explicit transition handlers — update sessionRef fields, then recompute.
  // Exposed via ShellStageContext so child panes can trigger transitions
  // directly when they have more context than the bus event alone provides
  // (e.g. ConversationPane advances to loading on first user message).
  // ---------------------------------------------------------------------------

  /** Stage 1 → Stage 2: playbook selected or first message sent. */
  const toLoading = React.useCallback((): void => {
    sessionRef.current = { ...sessionRef.current, hasSession: true };
    recompute();
  }, [recompute]);

  /** Stage 2 → Stage 3: first workspace widget loaded or entity resolved. */
  const toActiveChat = React.useCallback((): void => {
    sessionRef.current = { ...sessionRef.current, hasSession: true, hasWidget: true };
    recompute();
  }, [recompute]);

  /** Stage 3 → Stage 4: second workspace tab opened. */
  const toReview = React.useCallback((): void => {
    const prev = sessionRef.current;
    sessionRef.current = {
      ...prev,
      hasSession: true,
      hasWidget: true,
      tabCount: Math.max(prev.tabCount, 2),
    };
    recompute();
  }, [recompute]);

  /** Stage 4 → Stage 3: all but one workspace tab closed. */
  const toActiveWork = React.useCallback((): void => {
    sessionRef.current = { ...sessionRef.current, tabCount: 1 };
    recompute();
  }, [recompute]);

  /** Any → Stage 1: session cleared / deleted. */
  const reset = React.useCallback((): void => {
    sessionRef.current = { hasSession: false, hasWidget: false, tabCount: 0, hasEntity: false };
    setCurrentStage("welcome");
  }, []);

  // ---------------------------------------------------------------------------
  // PaneEventBus subscriptions — advance stage in response to bus events
  //
  // conversation channel:
  //   playbook-selected → marks hasSession=true (Stage 1 → Stage 2, gallery pick) [AIPU2-102]
  //   playbook_change   → marks hasSession=true (Stage 1 → Stage 2, legacy in-chat switch)
  //   first_message     → marks hasSession=true (Stage 1 → Stage 2, from typing)
  //
  // workspace channel:
  //   widget_load (with tabId — post-resolution confirmation) → marks
  //     hasWidget=true and updates tabCount (Stage 2 → Stage 3 / Stage 4)
  //   tab_count_change → updates tabCount for Stage 3 ↔ Stage 4 transitions
  //   entity_resolved  → marks hasEntity=true (Stage 2 → Stage 3 via entity)
  //   session_reset    → resets all state back to Stage 1
  //
  // NOTE: widget_load without tabId is the server-initiated event; WorkspacePane
  // re-dispatches widget_load WITH tabId after the registry promise resolves.
  // ShellStageManager only reacts to the post-resolution confirmation (tabId
  // present) to avoid advancing the stage before the widget is actually ready.
  // ---------------------------------------------------------------------------

  // Conversation channel — playbook selected (gallery) or first message sent
  //
  // `playbook-selected` (AIPU2-102): user picked a playbook from the gallery.
  //   Marks hasSession=true → Stage 1 → Stage 2 (loading).
  // `playbook_change`: legacy in-SprkChat playbook switch → same transition.
  // `first_message`: user typed first message without gallery → same transition.
  usePaneEvent("conversation", (event) => {
    if (
      event.type === "playbook-selected" ||
      event.type === "playbook_change" ||
      event.type === "first_message"
    ) {
      if (!sessionRef.current.hasSession) {
        sessionRef.current = { ...sessionRef.current, hasSession: true };
        recompute();
      }
    }
  });

  // Workspace channel — widget loaded, tab count changed, entity resolved, reset
  usePaneEvent("workspace", (event) => {
    const state = sessionRef.current;

    if (event.type === "widget_load" && event.tabId) {
      // Post-resolution confirmation: widget is now ready in a tab.
      // Update hasWidget and tabCount; recompute handles Stage 2→3 / 3→4.
      const tabCount = event.tabCount ?? state.tabCount;
      sessionRef.current = {
        ...state,
        hasSession: true,
        hasWidget: true,
        tabCount: Math.max(state.tabCount, tabCount > 0 ? tabCount : 1),
      };
      recompute();
    } else if (event.type === "tab_count_change") {
      // Explicit tab count update from WorkspacePane (on addTab / closeTab).
      const count = event.tabCount ?? state.tabCount;
      const wasReset = shouldReset({ ...state, tabCount: count });
      if (wasReset) {
        reset();
      } else {
        sessionRef.current = { ...state, tabCount: count };
        recompute();
      }
    } else if (event.type === "entity_resolved") {
      // Entity context resolved — advance from loading to active-chat.
      sessionRef.current = { ...state, hasSession: true, hasEntity: true };
      recompute();
    } else if (event.type === "session_reset") {
      // Session cleared — hard reset to welcome.
      reset();
    }
  });

  // ---------------------------------------------------------------------------
  // Context value — stable object when stage is unchanged
  // ---------------------------------------------------------------------------

  const stageContextValue = React.useMemo<ShellStageContextValue>(
    () => ({ currentStage, toLoading, toActiveChat, toReview, toActiveWork, reset }),
    [currentStage, toLoading, toActiveChat, toReview, toActiveWork, reset]
  );

  return (
    <ShellStageContext.Provider value={stageContextValue}>
      {children}
    </ShellStageContext.Provider>
  );
}

// ---------------------------------------------------------------------------
// RestoreContext — exposes restore state to panes (conversation summary, etc.)
// ---------------------------------------------------------------------------

export interface RestoreContextValue {
  /** Conversation summary from the restore spec (null if no summary or no restore). */
  conversationSummary: string | null;
  /** Recent messages from the restore spec (empty if no restore). */
  recentMessages: SessionRestoreSpec["recentMessages"];
  /** Whether entities have changed since the session was saved. */
  hasStaleEntities: boolean;
}

export const RestoreContext = React.createContext<RestoreContextValue | null>(null);
RestoreContext.displayName = "RestoreContext";

export function useRestoreContext(): RestoreContextValue | null {
  return React.useContext(RestoreContext);
}

// ---------------------------------------------------------------------------
// SessionRestoreManager — applies restore spec to session + dispatches events
// ---------------------------------------------------------------------------

interface SessionRestoreManagerProps {
  children: React.ReactNode;
  sessionId: string | undefined;
}

/**
 * Lives inside PaneEventBusProvider + AiSessionProvider + ShellStageManager.
 * When a sessionId URL param is present:
 *   1. Fetches restore spec from BFF via useSessionRestore
 *   2. Sets chatSessionId + playbookId on AiSessionProvider
 *   3. Dispatches widget_load events for each widget state
 *   4. Advances shell stage via toActiveChat()
 *   5. Provides conversation restore data via RestoreContext
 *   6. On 404: shows toast and lets Stage 1 render
 */
function SessionRestoreManager({ children, sessionId }: SessionRestoreManagerProps): React.JSX.Element {
  // Spaarke Auth v2 §H-4: function-based auth surface. SessionRestoreManager
  // uses `authenticatedFetch` from useAiSession() — never a snapshotted token.
  const { bffBaseUrl, authenticatedFetch, isAuthenticated, setChatSessionId, setPlaybookId } =
    useAiSession();
  const dispatch = useDispatchPaneEvent();
  const { toActiveChat } = useShellStage();

  const { restoreSpec, isRestoring: _isRestoring, restoreError, isNotFound } = useSessionRestore(
    sessionId,
    bffBaseUrl,
    authenticatedFetch,
    isAuthenticated
  );

  // Toast for restore failure
  const toasterId = useId("restore-toast");
  const { dispatchToast } = useToastController(toasterId);

  // Track whether we've already applied the restore spec (guard against double-apply).
  const appliedRef = React.useRef<string | null>(null);

  // Apply restore spec once it arrives
  React.useEffect(() => {
    if (!restoreSpec) return;
    if (appliedRef.current === restoreSpec.sessionId) return;
    appliedRef.current = restoreSpec.sessionId;

    // 1. Set session state on AiSessionProvider
    setChatSessionId(restoreSpec.sessionId);
    if (restoreSpec.playbookId) {
      setPlaybookId(restoreSpec.playbookId);
    }

    // 2. Dispatch widget_load events for each saved widget state
    const widgetEntries = Object.entries(restoreSpec.widgetStates);
    for (const [widgetType, serializedData] of widgetEntries) {
      let widgetData: unknown = null;
      try {
        widgetData = JSON.parse(serializedData);
      } catch {
        widgetData = { raw: serializedData };
      }

      dispatch("workspace", {
        type: "widget_load",
        widgetType,
        widgetData,
      });
    }

    // 3. Advance shell stage
    if (widgetEntries.length > 0) {
      toActiveChat();
    }

    console.info(
      `[SessionRestore] Applied restore spec: session=${restoreSpec.sessionId}, ` +
        `widgets=${widgetEntries.length}, summary=${restoreSpec.conversationSummary !== null}`
    );
  }, [restoreSpec, setChatSessionId, setPlaybookId, dispatch, toActiveChat]);

  // Show toast on 404 or error
  React.useEffect(() => {
    if (isNotFound) {
      dispatchToast(
        <Toast>
          <ToastTitle>Session not found. Starting a new session.</ToastTitle>
        </Toast>,
        { intent: "warning", timeout: 5000 }
      );
    } else if (restoreError && !isNotFound) {
      dispatchToast(
        <Toast>
          <ToastTitle>Failed to restore session: {restoreError}</ToastTitle>
        </Toast>,
        { intent: "error", timeout: 5000 }
      );
    }
  }, [isNotFound, restoreError, dispatchToast]);

  // Provide restore context to panes (conversation summary + recent messages)
  const restoreContextValue = React.useMemo<RestoreContextValue | null>(
    () =>
      restoreSpec
        ? {
            conversationSummary: restoreSpec.conversationSummary,
            recentMessages: restoreSpec.recentMessages,
            hasStaleEntities: restoreSpec.hasStaleEntities,
          }
        : null,
    [restoreSpec]
  );

  return (
    <RestoreContext.Provider value={restoreContextValue}>
      <Toaster toasterId={toasterId} position="top" />
      {children}
    </RestoreContext.Provider>
  );
}

// ---------------------------------------------------------------------------
// ThreePaneShell — public root component
// ---------------------------------------------------------------------------

/**
 * Root shell component for the SpaarkeAi Code Page (R2).
 *
 * Replaces R1's AppShell. Renders the full provider tree:
 *   PaneEventBusProvider → ShellStageManager → ThreePaneLayout
 *
 * FluentProvider is owned by App.tsx (theme detection lives there).
 * AiSessionProvider (AIPU2-076) will wrap ThreePaneLayout once implemented.
 *
 * @example
 * // From App.tsx (token prop retained for backwards compat until task 021 deletes it):
 * <FluentProvider theme={theme}>
 *   <ThreePaneShell
 *     bffBaseUrl={bffBaseUrl}
 *     entityLogicalName={entityLogicalName}
 *     entityId={entityId}
 *     matterId={matterId}
 *   />
 * </FluentProvider>
 */
export function ThreePaneShell(props: ThreePaneShellProps): React.JSX.Element {
  const styles = useStyles();
  // Spaarke Auth v2 §H-4: `token` and `isAuthenticated` props are no longer
  // consumed by this shell — auth state is read via useAiSession() (which
  // wraps useAuth()) inside the panes. The props on ThreePaneShellProps are
  // retained for now to avoid churning App.tsx; task 021 removes them entirely.
  const { bffBaseUrl, entityLogicalName, entityId, matterId, sessionId } = props;

  // Build the entity context for AiSessionProvider from URL params.
  // R2: entityContext is resolved at the shell level and passed down via
  // AiSessionProvider rather than being resolved inside StandaloneAiProvider.
  const entityContext = React.useMemo<EntityContext | null>(() => {
    if (!entityLogicalName || !entityId) return null;
    return {
      // entityLogicalName comes from URL (e.g. "sprk_matter") and may not
      // narrow to the EntityType literal union at compile time. Downstream
      // consumers normalize / map the value, so we widen via cast at the
      // source-of-truth (per task 075 minimum-viable nullability fix).
      entityType: entityLogicalName as EntityType,
      entityId,
      ...(matterId ? { matterId } : {}),
    };
  }, [entityLogicalName, entityId, matterId]);

  // ── Pane collapse state (Task 094) ──────────────────────────────────────
  //
  // Operator request: click any pane's HEADER to collapse it to a narrow
  // vertical strip (mirrors SmartToDo column collapse). All three panes
  // can be collapsed simultaneously; state persists across sessions via
  // localStorage so refresh restores the user's preference. See
  // `usePaneCollapse.ts` for the implementation + persistence shape.
  const paneCollapse = usePaneCollapse();
  const paneCollapseCtx = React.useMemo<PaneCollapseContextValue>(
    () => ({
      isCollapsed: paneCollapse.isCollapsed,
      toggle: paneCollapse.toggle,
    }),
    [paneCollapse.isCollapsed, paneCollapse.toggle]
  );

  const leftCollapsed = paneCollapse.isCollapsed("assistant");
  const centerCollapsed = paneCollapse.isCollapsed("workspace");
  const rightCollapsed = paneCollapse.isCollapsed("context");
  const toggleLeft = React.useCallback(() => paneCollapse.toggle("assistant"), [paneCollapse]);
  const toggleCenter = React.useCallback(() => paneCollapse.toggle("workspace"), [paneCollapse]);
  const toggleRight = React.useCallback(() => paneCollapse.toggle("context"), [paneCollapse]);

  return (
    <PaneEventBusProvider>
      <AiSessionProvider
        bffBaseUrl={bffBaseUrl}
        entityContext={entityContext}
      >
        <ShellStageManager>
          <SessionRestoreManager sessionId={sessionId}>
            <PaneCollapseContext.Provider value={paneCollapseCtx}>
              <div className={styles.shell}>
                {/*
                 * ThreePaneLayout dimensions match R1 App.tsx values (340/400/240/240/320).
                 * storageKey is namespaced to the R2 shell so sessionStorage is isolated
                 * from any residual R1 keys.
                 *
                 * Task 094: leftCollapsed / centerCollapsed / rightCollapsed +
                 * the matching toggle callbacks fully drive the layout's collapse
                 * state from `usePaneCollapse` (localStorage-backed). Each pane's
                 * PaneHeader also wires `onCollapse` so clicking the header
                 * collapses the pane. The collapsed-strip click in the layout
                 * re-expands.
                 */}
                <ThreePaneLayout
                  leftPane={<ConversationPane />}
                  centerPane={<WorkspacePane />}
                  rightPane={<ContextPaneController />}
                  storageKey="spaarke-ai-r2-shell"
                  /*
                   * Task 117 (Round 10, 2026-05-22) — Operator request: on a
                   * brand-new session with no saved pane widths, distribute
                   * the three panes as 25% / 50% / 25% of the viewport
                   * (Assistant / Workspace / Context) rather than the fixed
                   * 340/400 pixel defaults (which produced ~17/50/33 on a
                   * typical 2000px viewport — wildly off the desired layout).
                   *
                   * The fixed pixel defaults are retained as a fallback for
                   * SSR / non-browser environments and as a hard floor when
                   * the computed pixel value is below the minimum width.
                   *
                   * Precedence on each cold mount (see resolveInitialWidth):
                   *   1. sessionStorage stored pixel width (user-dragged value
                   *      persists — drag a splitter and the pixel value wins
                   *      on subsequent reloads)
                   *   2. defaultLeftWidthFrac × window.innerWidth (NEW)
                   *   3. defaultLeftWidthPx (legacy pixel default)
                   */
                  defaultLeftWidthPx={340}
                  defaultRightWidthPx={400}
                  defaultLeftWidthFrac={0.25}
                  defaultRightWidthFrac={0.25}
                  minLeftWidthPx={240}
                  minRightWidthPx={240}
                  minCenterWidthPx={320}
                  leftPaneCollapseLabel="Assistant"
                  centerPaneCollapseLabel="Workspace"
                  rightPaneCollapseLabel="Context"
                  /*
                   * Task 096 introduced the `*CollapsedIcon` props on
                   * ThreePaneLayout. Each collapsed strip now renders the
                   * same icon the pane's PaneHeader shows when expanded:
                   *   Assistant  → ChatRegular        (task 097)
                   *   Workspace  → AppsListRegular    (task 098 follow-up)
                   *   Context    → DocumentRegular    (task 099 follow-up)
                   * The rotated-text fallback is no longer used in SpaarkeAi.
                   * Operator feedback (2026-05-22): "in collapse mode use the
                   * icon only as the pane identifier, not the words."
                   */
                  leftCollapsedIcon={<ChatRegular />}
                  centerCollapsedIcon={<AppsListRegular />}
                  rightCollapsedIcon={<DocumentRegular />}
                  leftCollapsed={leftCollapsed}
                  centerCollapsed={centerCollapsed}
                  rightCollapsed={rightCollapsed}
                  onToggleLeft={toggleLeft}
                  onToggleCenter={toggleCenter}
                  onToggleRight={toggleRight}
                />
              </div>
            </PaneCollapseContext.Provider>
          </SessionRestoreManager>
        </ShellStageManager>
      </AiSessionProvider>
    </PaneEventBusProvider>
  );
}
