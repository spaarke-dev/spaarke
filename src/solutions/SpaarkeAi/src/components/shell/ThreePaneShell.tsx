/**
 * ThreePaneShell.tsx — R2 root shell component for SpaarkeAi.
 *
 * Replaces R1's AppShell (embedded in App.tsx) with a structured three-pane
 * layout that owns the PaneEventBus context and shell-level stage lifecycle.
 *
 * Provider tree (outermost → innermost):
 *   FluentProvider  (theme — owned by App.tsx, ThreePaneShell receives it via props)
 *     PaneEventBusProvider  (single bus instance for all three panes)
 *       ThreePaneLayout
 *         leftPane   = <ConversationPaneSlot />  (task AIPU2-077)
 *         centerPane = <WorkspacePaneSlot />      (task AIPU2-078)
 *         rightPane  = <ContextPaneSlot />        (task AIPU2-079)
 *
 * Stage lifecycle:
 *   ThreePaneShell manages `ShellStage` state and subscribes to PaneEventBus
 *   channels to advance the stage in response to user-driven events.
 *
 *   Stages:
 *     'welcome'      — initial landing, no playbook selected
 *     'loading'      — playbook selected, awaiting first AI response
 *     'active-chat'  — conversation in progress
 *     'review'       — AI output delivered, user reviewing results
 *
 *   Transitions:
 *     welcome → loading     via toLoading()    (playbook_change event)
 *     loading → active-chat via toActiveChat() (first AI turn arrives)
 *     active-chat → review  via toReview()     (AI output finalized)
 *     any → welcome         via reset()        (new session or clear)
 *
 * Pane slots are placeholder divs in this task. Tasks 077-079 replace them
 * with ConversationPane, WorkspacePane, and ContextPaneController.
 *
 * @see ADR-021 - Fluent v9, dark mode required, semantic tokens only
 * @see ADR-022 - React 19 createRoot for Code Pages
 * @see ThreePaneLayout — layout primitive with draggable splitters
 * @see PaneEventBusProvider — cross-pane event bus context
 */

import * as React from "react";
import { makeStyles, tokens } from "@fluentui/react-components";
import { ThreePaneLayout } from "@spaarke/ui-components";
import { PaneEventBusProvider, usePaneEvent } from "@spaarke/ai-widgets";

// ---------------------------------------------------------------------------
// ShellStage — lifecycle state enum
// ---------------------------------------------------------------------------

/**
 * Shell-level lifecycle stages.
 *
 * These stages drive high-level layout decisions (e.g. which placeholder or
 * pane content to surface) and are propagated down via ShellStageContext so
 * child panes can adapt without prop-drilling.
 */
export type ShellStage = "welcome" | "loading" | "active-chat" | "review";

// ---------------------------------------------------------------------------
// ShellStageContext — propagates stage + transition handlers to panes
// ---------------------------------------------------------------------------

export interface ShellStageContextValue {
  /** Current lifecycle stage of the shell. */
  currentStage: ShellStage;
  /** Transition: welcome → loading (playbook selected). */
  toLoading: () => void;
  /** Transition: loading → active-chat (first AI turn received). */
  toActiveChat: () => void;
  /** Transition: active-chat → review (AI output finalized). */
  toReview: () => void;
  /** Reset to welcome (new session or clear). */
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
// ThreePaneShellProps
// ---------------------------------------------------------------------------

export interface ThreePaneShellProps {
  /** BFF API base URL resolved at bootstrap from Dataverse env vars. */
  bffBaseUrl: string;
  /** BFF access token acquired via @spaarke/auth. Null while acquiring. */
  token: string | null;
  /** Whether authentication has completed successfully. */
  isAuthenticated: boolean;
  /** Dataverse entity logical name from URL (e.g. "sprk_matter"). Optional. */
  entityLogicalName?: string;
  /** Dataverse entity record GUID from URL. Optional. */
  entityId?: string;
  /** Matter ID shorthand from URL. Optional. */
  matterId?: string;
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

  // Placeholder pane styling — replaced by real components in tasks 077-079
  placeholderPane: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    width: "100%",
    height: "100%",
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    fontFamily: tokens.fontFamilyBase,
    gap: tokens.spacingVerticalS,
    backgroundColor: tokens.colorNeutralBackground2,
  },

  placeholderLabel: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
  },

  placeholderStage: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground4,
  },
});

// ---------------------------------------------------------------------------
// Placeholder slot components (replaced in tasks 077-079)
// ---------------------------------------------------------------------------

function ConversationPaneSlot(): React.JSX.Element {
  const styles = useStyles();
  const { currentStage } = useShellStage();
  return (
    <div className={styles.placeholderPane} data-testid="conversation-pane-slot">
      <span className={styles.placeholderLabel}>Conversation Pane</span>
      <span className={styles.placeholderStage}>stage: {currentStage}</span>
    </div>
  );
}

function WorkspacePaneSlot(): React.JSX.Element {
  const styles = useStyles();
  const { currentStage } = useShellStage();
  return (
    <div className={styles.placeholderPane} data-testid="workspace-pane-slot">
      <span className={styles.placeholderLabel}>Workspace Pane</span>
      <span className={styles.placeholderStage}>stage: {currentStage}</span>
    </div>
  );
}

function ContextPaneSlot(): React.JSX.Element {
  const styles = useStyles();
  const { currentStage } = useShellStage();
  return (
    <div className={styles.placeholderPane} data-testid="context-pane-slot">
      <span className={styles.placeholderLabel}>Context Pane</span>
      <span className={styles.placeholderStage}>stage: {currentStage}</span>
    </div>
  );
}

// ---------------------------------------------------------------------------
// ShellStageManager — subscribes to PaneEventBus, drives stage transitions
// ---------------------------------------------------------------------------

/**
 * Internal component that lives inside PaneEventBusProvider and manages
 * stage transitions via bus subscriptions.
 *
 * Separated from ThreePaneShell to keep the outer component free of bus
 * hooks — the bus context is only available after PaneEventBusProvider mounts.
 */
interface ShellStageManagerProps {
  children: React.ReactNode;
}

function ShellStageManager({ children }: ShellStageManagerProps): React.JSX.Element {
  const [currentStage, setCurrentStage] = React.useState<ShellStage>("welcome");

  // ---------------------------------------------------------------------------
  // Transition handlers (stable — no deps change between renders)
  // ---------------------------------------------------------------------------

  const toLoading = React.useCallback((): void => {
    setCurrentStage("loading");
  }, []);

  const toActiveChat = React.useCallback((): void => {
    setCurrentStage("active-chat");
  }, []);

  const toReview = React.useCallback((): void => {
    setCurrentStage("review");
  }, []);

  const reset = React.useCallback((): void => {
    setCurrentStage("welcome");
  }, []);

  // ---------------------------------------------------------------------------
  // PaneEventBus subscriptions — advance stage in response to bus events
  // ---------------------------------------------------------------------------

  // conversation channel: playbook_change → welcome → loading
  usePaneEvent("conversation", (event) => {
    if (event.type === "playbook_change" && currentStage === "welcome") {
      toLoading();
    }
  });

  // workspace channel: widget_load → loading → active-chat (first widget ready)
  usePaneEvent("workspace", (event) => {
    if (event.type === "widget_load" && currentStage === "loading") {
      toActiveChat();
    }
  });

  // context channel: stage_change → active-chat → review (analysis complete)
  usePaneEvent("context", (event) => {
    if (event.type === "stage_change" && currentStage === "active-chat") {
      toReview();
    }
  });

  // ---------------------------------------------------------------------------
  // Context value — stable object when stage is unchanged
  // ---------------------------------------------------------------------------

  const stageContextValue = React.useMemo<ShellStageContextValue>(
    () => ({ currentStage, toLoading, toActiveChat, toReview, reset }),
    [currentStage, toLoading, toActiveChat, toReview, reset]
  );

  return (
    <ShellStageContext.Provider value={stageContextValue}>
      {children}
    </ShellStageContext.Provider>
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
 * // From App.tsx:
 * <FluentProvider theme={theme}>
 *   <ThreePaneShell
 *     bffBaseUrl={bffBaseUrl}
 *     token={token}
 *     isAuthenticated={isAuthenticated}
 *     entityLogicalName={entityLogicalName}
 *     entityId={entityId}
 *     matterId={matterId}
 *   />
 * </FluentProvider>
 */
export function ThreePaneShell(_props: ThreePaneShellProps): React.JSX.Element {
  const styles = useStyles();

  return (
    <PaneEventBusProvider>
      <ShellStageManager>
        <div className={styles.shell}>
          {/*
           * ThreePaneLayout dimensions match R1 App.tsx values (340/400/240/240/320).
           * storageKey is namespaced to the R2 shell so sessionStorage is isolated
           * from any residual R1 keys.
           */}
          <ThreePaneLayout
            leftPane={<ConversationPaneSlot />}
            centerPane={<WorkspacePaneSlot />}
            rightPane={<ContextPaneSlot />}
            storageKey="spaarke-ai-r2-shell"
            defaultLeftWidthPx={340}
            defaultRightWidthPx={400}
            minLeftWidthPx={240}
            minRightWidthPx={240}
            minCenterWidthPx={320}
            leftPaneCollapseLabel="Show AI Chat"
            rightPaneCollapseLabel="Show Context"
          />
        </div>
      </ShellStageManager>
    </PaneEventBusProvider>
  );
}
