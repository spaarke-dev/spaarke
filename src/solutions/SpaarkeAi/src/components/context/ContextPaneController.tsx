/**
 * ContextPaneController.tsx — Stage-aware context pane for SpaarkeAi R2.
 *
 * Replaces R1's SourcePanel.tsx with an adaptive context pane that changes
 * its content based on the current shell lifecycle stage and server-driven
 * context_update events arriving on the 'context' PaneEventBus channel.
 *
 * Stage-to-widget mapping:
 *   welcome      — empty state: "Select a playbook to begin"
 *   loading      — Fluent Spinner: "Gathering context..."
 *   active-chat  — resolved context widget from ContextWidgetRegistry
 *   review       — resolved context widget (same as active-chat, may differ via event)
 *
 * PaneEventBus subscriptions (channel: 'context'):
 *   context_update    — server sends a new widget type + data payload → resolves
 *                       the widget from ContextWidgetRegistry, renders it.
 *                       Unknown type → Spinner (null from registry), not crash.
 *   context_highlight — forwards citationId + selectionRef to the active widget's
 *                       onHighlight() method, if implemented.
 *   stage_change      — clears the active widget so the stage-default UI renders.
 *
 * @see ADR-021 — Fluent v9 semantic tokens only; dark mode required
 * @see ADR-022 — React 19 for Code Pages (NOT PCF-safe)
 * @see ContextWidgetRegistry — resolveContextWidget(), registerContextWidget()
 * @see usePaneEvent — subscribe to 'context' PaneEventBus channel
 * @see ThreePaneShell — shell stage context, ShellStageContext
 * @see SourcePanel.tsx — R1 component being replaced
 */

import * as React from "react";
import {
  makeStyles,
  shorthands,
  tokens,
  Spinner,
  Text,
  mergeClasses,
} from "@fluentui/react-components";
import { DocumentRegular } from "@fluentui/react-icons";
import {
  PaneHeader,
  launchCreateMatterWizard,
  launchCreateProjectWizard,
  launchSummarizeFilesWizard,
  launchFindSimilarWizard,
  launchAssignWorkWizard,
  launchPlaybookIntent,
} from "@spaarke/ui-components";
import {
  usePaneEvent,
  resolveContextWidget,
  getContextWidgetForTab,
  GetStartedCardsWidget,
} from "@spaarke/ai-widgets";
import type {
  ContextWidgetComponent,
  ContextPaneEvent,
  WorkspacePaneEvent,
  GetStartedCardId,
} from "@spaarke/ai-widgets";
import { useShellStage, usePaneCollapseContext } from "../shell/ThreePaneShell";
import type { ShellStage } from "../shell/ThreePaneShell";
import { getBffBaseUrl } from "../../config/runtimeConfig";
import { useContextTool } from "../../hooks/useContextTool";
import { ContextPaneMenu } from "./ContextPaneMenu";
import { SemanticSearchCriteriaTool } from "./SemanticSearchCriteriaTool";

// ---------------------------------------------------------------------------
// ContextStage — maps to each of the five named context rendering modes.
//
// These align with the task spec's step 3: the five distinct content states
// the Context pane can occupy. They derive from ShellStage but are independent
// — a context_update event can drive from 'playbook-gallery' to
// 'sources-citations' without waiting for ShellStage to advance.
// ---------------------------------------------------------------------------

export type ContextStage =
  | "playbook-gallery"   // welcome — no playbook selected
  | "entity-info"        // context_update with contextType='entity-info'
  | "sources-citations"  // active-chat — citations/source document widget
  | "progress"           // long-running multi-step operations
  | "related-items";     // review — related items / supplementary list

// ---------------------------------------------------------------------------
// Stage-to-widget-type mapping for built-in registered widget types.
//
// The server sends contextType on context_update. We map known values to
// ContextStage so stage-level UI decisions remain separate from widget type.
// ---------------------------------------------------------------------------

const CONTEXT_TYPE_TO_STAGE: Record<string, ContextStage> = {
  "entity-info": "entity-info",
  "sources-citations": "sources-citations",
  "source": "sources-citations",
  "citation": "sources-citations",
  "findings": "sources-citations",      // FindingsWidget — citations/sources stage
  "progress": "progress",
  "progress-tracker": "progress",       // ProgressTrackerWidget
  "related-items": "related-items",
  "related": "related-items",
};

// ---------------------------------------------------------------------------
// ShellStage → default ContextStage (used when no context_update has arrived)
// ---------------------------------------------------------------------------

// Mapping per design.md Section 2.3 + task AIPU2-105:
//   Stage 1 welcome      → playbook-gallery  (gallery of available playbooks)
//   Stage 2 loading      → entity-info       (document info / entity waiting)
//   Stage 3 active-chat  → sources-citations (findings, citations, sources)
//   Stage 4 review       → related-items     (tab-adaptive: adapts via tab_change)
function shellStageToContextStage(stage: ShellStage): ContextStage {
  switch (stage) {
    case "welcome":     return "playbook-gallery";
    case "loading":     return "entity-info";    // Stage 2: awaiting document/entity selection
    case "active-chat": return "sources-citations";
    case "review":      return "related-items";  // Stage 4: adapts to active tab via tab_change
  }
}

// ---------------------------------------------------------------------------
// Active widget slot — holds the resolved component and its imperative ref.
// The ref is used to call onHighlight() on context_highlight events.
// ---------------------------------------------------------------------------

interface ActiveWidgetSlot {
  /** Resolved component from ContextWidgetRegistry. */
  Component: ContextWidgetComponent;
  /** Widget type string from the server. */
  widgetType: string;
  /** Data payload from the context_update event. */
  data: unknown;
  /** Whether the widget is still loading its data. */
  isLoading?: boolean;
}

// ---------------------------------------------------------------------------
// onHighlight ref interface — widgets that implement cross-pane citation
// highlighting expose this via a React ref.
// ---------------------------------------------------------------------------

export interface ContextWidgetImperativeHandle {
  onHighlight: (citationId: string, selectionRef?: string) => void;
}

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

  // Header style classes removed — header is now rendered by the shared
  // <PaneHeader> primitive from @spaarke/ui-components (ADR-012 / FR-17).
  //
  // Task 099 — the previous `headerStageLabel` rendering ("Sources" /
  // "Get Started" / etc.) was removed from the rightSlot per operator
  // request, leaving the Tools dropdown alone right-justified to mirror
  // the Workspace pane's PaneHeader. The `stageLabelMap` is kept for
  // potential ARIA / debug use but is no longer visually rendered, so the
  // CSS class for it is gone with this task.

  // Task 099 — "Quick Start" section title shown above the
  // GetStartedCardsWidget body. Uses Text size 400 semibold (one Fluent v9
  // step below the PaneHeader title from Wave 1's size 400 bump) so the
  // section title is clearly secondary to the pane title but still bold.
  quickStartHeader: {
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
  },
  quickStartTitle: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },

  // Content area — scrollable
  content: {
    flex: 1,
    overflowY: "auto",
    display: "flex",
    flexDirection: "column",
  },

  // Loading state — spinner centred in the pane
  loadingState: {
    flex: 1,
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    gap: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalXXL,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
  },

  // Empty / welcome state
  emptyState: {
    flex: 1,
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    gap: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalXXL,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    textAlign: "center",
    color: tokens.colorNeutralForeground3,
  },
  emptyIcon: {
    fontSize: "40px",
    color: tokens.colorNeutralForeground4,
    marginBottom: tokens.spacingVerticalS,
  },
  emptyTitle: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
  },
  emptySubtitle: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    maxWidth: "260px",
  },

  // Widget wrapper — fills content area
  widgetWrapper: {
    flex: 1,
    display: "flex",
    flexDirection: "column",
    overflow: "hidden",
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    margin: tokens.spacingHorizontalS,
    ...shorthands.border("1px", "solid", tokens.colorNeutralStroke2),
  },

  // Unknown widget fallback (null from registry) — spinner while resolving
  unknownWidgetState: {
    flex: 1,
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    gap: tokens.spacingVerticalS,
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalXXL,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
  },
  unknownWidgetLabel: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },
});

// ---------------------------------------------------------------------------
// ResolvedContextWidget — async-resolves and renders a single widget slot.
//
// Exposes onHighlight() via an imperative handle ref so the parent controller
// can forward context_highlight events without prop-drilling through state.
// ---------------------------------------------------------------------------

interface ResolvedContextWidgetProps {
  slot: ActiveWidgetSlot;
  highlightRef: React.MutableRefObject<ContextWidgetImperativeHandle | null>;
  className?: string;
}

function ResolvedContextWidget({
  slot,
  highlightRef,
  className,
}: ResolvedContextWidgetProps): React.JSX.Element {
  const styles = useStyles();

  // The slot already holds a resolved component — no async needed here.
  // Resolution happens in the event handler before setting slot state.
  const { Component, widgetType, data, isLoading } = slot;

  // Expose a stable onHighlight handle so the parent can call it on events.
  // We wire up to the widget's own onHighlight prop if the component exposes
  // it through a ref (imperative handle pattern). For function components that
  // don't use forwardRef, we provide a no-op so the shape is always present.
  React.useEffect(() => {
    highlightRef.current = {
      onHighlight: (_citationId: string, _selectionRef?: string): void => {
        // Default no-op — widgets that support highlighting override this
        // by setting highlightRef.current themselves via useImperativeHandle.
        // Since React function components can't be imperatively controlled
        // without forwardRef, we use a mutable ref pattern: the widget
        // component itself can update highlightRef.current if it chooses.
      },
    };

    return () => {
      highlightRef.current = null;
    };
  }, [highlightRef]);

  return (
    <div
      className={mergeClasses(styles.widgetWrapper, className)}
      data-context-widget-type={widgetType}
    >
      <Component
        data={data}
        widgetType={widgetType}
        isLoading={isLoading}
      />
    </div>
  );
}

// ---------------------------------------------------------------------------
// Wizard launchers
//
// Round 4 Fix 2 (task 085): all seven Get Started card click handlers now use
// the shared `launch*Wizard` helpers from `@spaarke/ui-components`. The shared
// helpers REUSE LegalWorkspace's exact Xrm.Navigation.navigateTo call shape
// (verbatim from `src/solutions/LegalWorkspace/src/components/Shell/WorkspaceGrid.tsx`
// and `ActionCardHandlers.ts`). The previous local `launchCodePagePopup` and
// `launchAssignWorkWizard` package-local helpers were divergent (and the
// widget_load dispatch paths added a tab-mount round-trip), which broke the
// six non-Create-Project wizard launches from this pane.
//
// See `src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/wizardLaunchers.ts`
// for the launcher implementations + provenance comments.
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// ContextPaneController — primary export
// ---------------------------------------------------------------------------

/**
 * ContextPaneController — adaptive, stage-based right pane for SpaarkeAi R2.
 *
 * Replaces R1's SourcePanel. Subscribes to the 'context' PaneEventBus channel
 * and adapts its rendering based on both the shell lifecycle stage and incoming
 * context_update events.
 *
 * Must be rendered inside:
 *   - PaneEventBusProvider (usePaneEvent requires bus context)
 *   - ShellStageContext.Provider (useShellStage requires stage context)
 */
export function ContextPaneController(): React.JSX.Element {
  const styles = useStyles();
  const { currentStage } = useShellStage();

  // ── Pane collapse (Task 094) ────────────────────────────────────────────
  //
  // The Context pane participates in the three-pane collapse/expand feature
  // owned by the shell. Clicking the PaneHeader (anywhere except the stage
  // label, which is non-interactive Text) toggles collapse via
  // `paneCollapse.toggle('context')`. The shared PaneHeader applies
  // stopPropagation on its rightSlot wrapper as a defensive guard.
  const paneCollapse = usePaneCollapseContext();
  const handleHeaderCollapse = React.useCallback(() => {
    paneCollapse?.toggle("context");
  }, [paneCollapse]);
  const isContextExpanded = !(paneCollapse?.isCollapsed("context") ?? false);

  // ── Tool selection (Task 095) ───────────────────────────────────────────
  //
  // The Context pane has a dropdown selector in its PaneHeader rightSlot
  // (mirrors WorkspacePaneMenu). The user-selected tool is the SOURCE OF
  // TRUTH for what the pane renders — it overrides ShellStage and server-
  // driven context_update events. Persisted in localStorage via the
  // `useContextTool` hook so a modal close (wizard, Semantic Search results)
  // returns the pane to the chosen tool instead of going blank (the
  // pre-task-095 bug for the welcome-stage GetStartedCards path).
  const { selectedTool, setSelectedTool } = useContextTool();

  // Active widget slot — null until a context_update event arrives with a
  // successfully resolved widget component.
  const [activeWidget, setActiveWidget] = React.useState<ActiveWidgetSlot | null>(null);

  // Whether we're currently resolving a widget from the registry asynchronously.
  const [isResolving, setIsResolving] = React.useState(false);

  // Derived context stage — tracks what mode the pane is in.
  // Updated by context_update (widgetType-driven) and stage_change events.
  const [contextStage, setContextStage] = React.useState<ContextStage>(
    shellStageToContextStage(currentStage)
  );

  // Imperative handle ref — holds the active widget's onHighlight() callback
  // so context_highlight events can be forwarded without re-render.
  const highlightRef = React.useRef<ContextWidgetImperativeHandle | null>(null);

  // Keep contextStage in sync with shell stage changes (fallback when no
  // context_update has set a specific stage for the new shell stage).
  //
  // Welcome stage (FR-18 + FR-21, task 042): the welcome stage now renders
  // the static <GetStartedCardsWidget /> directly in renderContent() rather
  // than auto-resolving a widget from the registry. We clear `activeWidget`
  // on entry to welcome so any prior widget (e.g. a PlaybookGalleryWidget
  // loaded by a context_update on a previous stage) does not bleed through.
  // PlaybookGalleryWidget remains REGISTERED in ContextWidgetRegistry — see
  // index.ts of @spaarke/ai-widgets — so non-welcome stages or future
  // context_update events that request it will still resolve correctly.
  React.useEffect(() => {
    setContextStage(shellStageToContextStage(currentStage));

    if (currentStage === "welcome") {
      // Clear any previously-resolved widget so the welcome state shows the
      // GetStartedCards (rendered directly in renderContent below) instead of
      // a stale widget left over from a previous shell stage.
      setActiveWidget(null);
      setIsResolving(false);
    }
  }, [currentStage]);

  // ---------------------------------------------------------------------------
  // PaneEventBus subscription — 'context' channel
  // ---------------------------------------------------------------------------

  usePaneEvent("context", React.useCallback((event: ContextPaneEvent): void => {
    switch (event.type) {
      case "context_update": {
        const widgetType = event.contextType ?? "unknown";
        const data = event.contextData ?? null;

        // Update stage from known contextType mapping
        const mappedStage = CONTEXT_TYPE_TO_STAGE[widgetType];
        if (mappedStage) {
          setContextStage(mappedStage);
        }

        // Resolve widget asynchronously from registry
        setIsResolving(true);
        resolveContextWidget(widgetType).then((Component) => {
          setIsResolving(false);
          if (Component !== null) {
            setActiveWidget({
              Component,
              widgetType,
              data,
              isLoading: false,
            });
          } else {
            // Unknown type — clear the active widget so the pane shows
            // the stage-appropriate default (spinner or empty state).
            // This is the null-return contract from ContextWidgetRegistry:
            // version mismatch → safe fallback, not a crash.
            setActiveWidget(null);
          }
        });
        break;
      }

      case "context_highlight": {
        // Forward to the active widget's highlight handler if it's implemented.
        const handle = highlightRef.current;
        if (handle && event.citationId) {
          handle.onHighlight(event.citationId, event.selectionRef);
        }
        break;
      }

      case "stage_change": {
        // Server signalled a stage advance — clear active widget so the pane
        // reverts to the stage-default UI until a new context_update arrives.
        setActiveWidget(null);
        setIsResolving(false);
        // stage transitions are handled by the ShellStageManager via the
        // same event; contextStage will update via the useEffect above.
        break;
      }

      default:
        break;
    }
  }, []));

  // ---------------------------------------------------------------------------
  // PaneEventBus subscription — 'workspace' channel
  //
  // Listens for tab_change events so the Context pane can automatically adapt
  // its widget to match the newly active workspace tab. This is the cross-pane
  // interaction defined in task AIPU2-103.
  //
  // The event MUST NOT affect ConversationPane state — we only update local
  // context widget state here, and we do not re-dispatch the event.
  // ---------------------------------------------------------------------------

  usePaneEvent("workspace", React.useCallback((event: WorkspacePaneEvent): void => {
    if (event.type !== "tab_change") {
      // Only handle tab_change events; all other workspace events are ignored
      // by the Context pane — they are handled exclusively by WorkspacePane.
      return;
    }

    const workspaceWidgetType = event.widgetType ?? "";
    if (!workspaceWidgetType) {
      // No widget type on the event — tab may be empty/loading; keep current context.
      return;
    }

    // Look up the recommended context widget type for this workspace widget.
    const recommendedContextType = getContextWidgetForTab(workspaceWidgetType);

    if (recommendedContextType === null) {
      // Explicit null mapping (or unknown type) — keep the current context widget.
      // This is the correct behaviour for widget types like ActionPlan that have
      // no meaningful context pairing.
      return;
    }

    // Resolve and activate the recommended context widget.
    // Use the workspace tab's widgetData as the context data so the widget
    // receives relevant metadata (documentId, searchQuery, etc.).
    const contextData = event.widgetData ?? null;

    // Update the context stage to match the recommended context type.
    const mappedStage = CONTEXT_TYPE_TO_STAGE[recommendedContextType];
    if (mappedStage) {
      setContextStage(mappedStage);
    }

    setIsResolving(true);
    resolveContextWidget(recommendedContextType).then((Component) => {
      setIsResolving(false);
      if (Component !== null) {
        setActiveWidget({
          Component,
          widgetType: recommendedContextType,
          data: contextData,
          isLoading: false,
        });
      } else {
        // Registry returned null for the recommended type — this indicates a
        // version mismatch or unregistered widget. Show the stage-default
        // empty state rather than crashing or showing stale content.
        setActiveWidget(null);
      }
    });
  }, []));

  // ---------------------------------------------------------------------------
  // PaneEventBus dispatcher — kept for non-card workspace events.
  //
  // Round 4 Fix 2 (task 085): all 7 Get Started cards now route through the
  // shared `launch*Wizard` helpers from `@spaarke/ui-components`. The previous
  // split into "Group A" (direct Xrm.Navigation via local `launchCodePagePopup`)
  // and "Group B" (`widget_load` → tab-mount → widget calls navigateTo on mount)
  // is REMOVED — that split was the parallel-implementation bug. Now every card
  // calls the SAME launcher pattern that LegalWorkspace's WorkspaceGrid uses.
  //
  // The corresponding workspace-widgets (CreateProjectWizardWidget,
  // FindSimilarWizardWidget, EmailComposeWidget, MeetingScheduleWidget,
  // CreateMatterWizardWidget, DocumentUploadWizardWidget) REMAIN REGISTERED in
  // WorkspaceWidgetRegistry per operator's "keep embedded structure for future
  // use cases" directive — server-initiated `widget_load` events from a future
  // playbook orchestrator can still mount them as workspace tabs. Only the
  // welcome-state CARD CLICK routing changes here.
  // ---------------------------------------------------------------------------

  /**
   * onCardClick handler for {@link GetStartedCardsWidget} (FR-19 mapping).
   *
   * All seven cards route directly to the shared wizard launchers — the exact
   * Xrm.Navigation.navigateTo shape used by LegalWorkspace's WorkspaceGrid. No
   * tab-mount intermediate, no widget_load dispatch, no parallel implementation.
   */
  const handleGetStartedCardClick = React.useCallback(
    (cardId: GetStartedCardId): void => {
      const bffBaseUrl = getBffBaseUrl();

      switch (cardId) {
        case "create-matter-wizard":
          launchCreateMatterWizard({ bffBaseUrl });
          return;

        case "create-project-wizard":
          launchCreateProjectWizard({ bffBaseUrl });
          return;

        case "assign-work":
          launchAssignWorkWizard({ bffBaseUrl });
          return;

        case "document-upload-wizard":
          // GetStartedCardsWidget labels this "Summarize Files" — route to the
          // same Summarize Files wizard LegalWorkspace uses.
          launchSummarizeFilesWizard({ bffBaseUrl });
          return;

        case "find-similar-wizard":
          launchFindSimilarWizard({ bffBaseUrl });
          return;

        case "email-compose":
          launchPlaybookIntent({ bffBaseUrl, intent: "email-compose" });
          return;

        case "meeting-schedule":
          launchPlaybookIntent({ bffBaseUrl, intent: "meeting-schedule" });
          return;

        default: {
          // Exhaustiveness check — TypeScript will flag this if a new
          // GetStartedCardId is added without a matching case.
          const _exhaustive: never = cardId;
          void _exhaustive;
          return;
        }
      }
    },
    []
  );

  // ---------------------------------------------------------------------------
  // Derive header stage label for debugging / accessibility
  // ---------------------------------------------------------------------------

  // FR-22 — Welcome-stage label renamed: "Gallery" → "Get Started".
  // The welcome ShellStage maps to ContextStage 'playbook-gallery' via
  // shellStageToContextStage(), so we change that entry's label.
  const stageLabelMap: Record<ContextStage, string> = {
    "playbook-gallery": "Get Started",
    "entity-info": "Entity",
    "sources-citations": "Sources",
    "progress": "Progress",
    "related-items": "Related",
  };

  // ---------------------------------------------------------------------------
  // Render helpers
  // ---------------------------------------------------------------------------

  /**
   * Renders the appropriate content for the current combination of shell
   * stage and active widget state.
   *
   * Per design.md Section 2.3 + task AIPU2-105:
   *   Stage 1 'welcome':     Playbook gallery (select a playbook to begin).
   *   Stage 2 'loading':     Entity info / document waiting spinner.
   *   Stage 3 'active-chat': Findings / sources / citations widget.
   *   Stage 4 'review':      Context adapts to active workspace tab via tab_change.
   */
  function renderContent(): React.ReactNode {
    // Task 095 — `selectedTool` is the SOURCE OF TRUTH for what the pane
    // renders. It overrides ShellStage and server-driven context_update events.
    //
    //   - 'semantic-search' → SemanticSearchCriteriaTool (always, regardless
    //                         of ShellStage). Modal close behavior: selectedTool
    //                         is persisted in localStorage, so the pane
    //                         re-renders with this tool still selected — no
    //                         blank pane.
    //   - 'quick-start'     → fall through to the existing stage-aware
    //                         rendering (welcome stage shows GetStartedCardsWidget,
    //                         other stages show server-driven widgets). This
    //                         path ALSO benefits from the persisted selectedTool:
    //                         when a wizard popup closes, the pane re-renders
    //                         with this branch selected, so GetStartedCardsWidget
    //                         shows back up instead of the empty stage default.
    if (selectedTool === "semantic-search") {
      return (
        <div className={styles.content} data-testid="context-pane-semantic-search">
          <SemanticSearchCriteriaTool />
        </div>
      );
    }

    // selectedTool === "quick-start" — fall through to the existing logic.

    // Stage 1 — welcome (or Quick Start tool selected on any stage): render the
    // GetStartedCardsWidget (FR-18, FR-19, FR-21).
    //
    // The widget shows 7 action cards in a 2-column grid. Each card click
    // routes through `handleGetStartedCardClick` (defined above):
    //   - 'assign-work'    → launchAssignWorkWizard({ bffBaseUrl }) (task 045)
    //   - any other cardId → dispatch widget_load on the `workspace` channel,
    //                        which opens the corresponding widget in the
    //                        Workspace pane as a new top-tab.
    //
    // PlaybookGalleryWidget remains REGISTERED in ContextWidgetRegistry (see
    // @spaarke/ai-widgets/src/index.ts) so any future server-driven
    // context_update that requests 'playbook-gallery' on a non-welcome stage
    // resolves correctly. We do NOT auto-load it here anymore — the welcome
    // stage is now the GetStarted entry point per FR-18 / the R3 design.
    //
    // Task 095: on the welcome stage, Quick Start always wins. On non-welcome
    // stages, Quick Start ALSO wins UNLESS the server has dispatched a
    // context_update event that resolved to a real widget (activeWidget !==
    // null) — that path is preserved so the existing Stage 3 (active-chat) /
    // Stage 4 (review) widgets (FindingsWidget, ProgressTrackerWidget, etc.)
    // continue to surface when the AI orchestrator drives them. When no
    // server-driven widget is loaded, Quick Start's GetStartedCardsWidget
    // appears as the default — which is the uniform fix for the "pane goes
    // blank after modal close" bug (a wizard launch doesn't change
    // selectedTool, so the pane returns to GetStartedCardsWidget when the
    // modal closes).
    if (currentStage === "welcome" || (selectedTool === "quick-start" && activeWidget === null && !isResolving)) {
      // Task 099 — wrap the GetStartedCardsWidget in a small section header
      // showing "Quick Start" so the user can SEE which Context tool is
      // currently active inside the pane body. We do NOT modify
      // GetStartedCardsWidget itself (it's in `@spaarke/ai-widgets` shared
      // lib — out of scope for this SpaarkeAi-local revision).
      return (
        <div className={styles.content} data-testid="context-pane-welcome">
          <div className={styles.quickStartHeader}>
            <Text
              className={styles.quickStartTitle}
              size={400}
              data-testid="context-quick-start-title"
            >
              Quick Start
            </Text>
          </div>
          <GetStartedCardsWidget onCardClick={handleGetStartedCardClick} />
        </div>
      );
    }

    // Stage 2 — loading: playbook selected, awaiting document/entity selection.
    // Show entity-info waiting state (per design.md Stage 2 diagram).
    if (currentStage === "loading") {
      // If a widget arrived via context_update (e.g. entity-info widget), render it.
      if (activeWidget !== null) {
        return (
          <div className={styles.content} data-testid="context-pane-widget">
            <ResolvedContextWidget slot={activeWidget} highlightRef={highlightRef} />
          </div>
        );
      }
      return (
        <div className={styles.loadingState} data-testid="context-pane-loading">
          <Spinner size="medium" label="Gathering context..." />
          <Text
            size={200}
            style={{ color: tokens.colorNeutralForeground3, textAlign: "center" }}
          >
            Select or upload a document to begin your analysis.
          </Text>
        </div>
      );
    }

    // Async resolution in progress — show spinner while registry loads widget.
    // Applies in Stage 3 and Stage 4 after a context_update arrives.
    if (isResolving) {
      return (
        <div
          className={styles.unknownWidgetState}
          data-testid="context-pane-resolving"
        >
          <Spinner size="small" label="Loading context widget..." />
        </div>
      );
    }

    // Active widget ready — render it.
    // Applies in Stage 3 (active-chat) and Stage 4 (review/multi-task).
    // In Stage 4 the widget is updated by tab_change events (ContextPaneController
    // subscribes to the workspace channel and resolves the recommended context
    // widget for the new active workspace widget type).
    if (activeWidget !== null) {
      return (
        <div className={styles.content} data-testid="context-pane-widget">
          <ResolvedContextWidget
            slot={activeWidget}
            highlightRef={highlightRef}
          />
        </div>
      );
    }

    // No active widget yet, but shell is in active-chat or review —
    // render stage-specific empty state (before first context_update arrives).
    return renderStageDefaultContent();
  }

  /**
   * Stage-default empty states for active-chat and review when no widget is
   * currently loaded (e.g. before first context_update arrives).
   */
  function renderStageDefaultContent(): React.ReactNode {
    switch (contextStage) {
      case "sources-citations":
        return (
          <div className={styles.emptyState} data-testid="context-pane-sources-empty">
            <DocumentRegular className={styles.emptyIcon} />
            <Text className={styles.emptyTitle} size={400}>
              Source Materials
            </Text>
            <Text className={styles.emptySubtitle} size={200}>
              Documents, sources, and citations cited by the AI will appear here
              during analysis.
            </Text>
          </div>
        );

      case "progress":
        return (
          <div className={styles.loadingState} data-testid="context-pane-progress-empty">
            <Spinner size="medium" label="Processing..." />
          </div>
        );

      case "related-items":
        return (
          <div className={styles.emptyState} data-testid="context-pane-related-empty">
            <DocumentRegular className={styles.emptyIcon} />
            <Text className={styles.emptyTitle} size={400}>
              Related Items
            </Text>
            <Text className={styles.emptySubtitle} size={200}>
              Related documents and items from your analysis will appear here.
            </Text>
          </div>
        );

      case "entity-info":
        return (
          <div className={styles.emptyState} data-testid="context-pane-entity-empty">
            <DocumentRegular className={styles.emptyIcon} />
            <Text className={styles.emptyTitle} size={400}>
              Entity Context
            </Text>
            <Text className={styles.emptySubtitle} size={200}>
              Entity details and structured information will appear here.
            </Text>
          </div>
        );

      case "playbook-gallery":
      default:
        return (
          <div className={styles.emptyState} data-testid="context-pane-empty">
            <DocumentRegular className={styles.emptyIcon} />
            <Text className={styles.emptyTitle} size={400}>
              Context
            </Text>
            <Text className={styles.emptySubtitle} size={200}>
              Contextual information from the AI analysis will appear here.
            </Text>
          </div>
        );
    }
  }

  // ---------------------------------------------------------------------------
  // Render
  // ---------------------------------------------------------------------------

  // Task 099 — `stageLabelMap` is retained for ARIA / debug only (no longer
  // rendered visually after the rightSlot stage-label removal). We expose
  // the current stage label as a `data-context-stage` attribute on the root
  // so test harnesses (Playwright / smoke) can still assert the pane's
  // logical stage without reading any rendered text. Keeps the mapping
  // referenced so `noUnusedLocals` is satisfied.
  const debugStageLabel = stageLabelMap[contextStage];

  return (
    <div
      className={styles.root}
      data-testid="context-pane-controller"
      data-context-stage={debugStageLabel}
    >
      {/*
        Header — migrated to shared <PaneHeader> primitive from
        @spaarke/ui-components (FR-17, task 010). The inline header JSX
        previously living here was the canonical visual model for PaneHeader,
        so visual parity is preserved by construction (matching styles in
        PaneHeader.tsx). Stage label remains in rightSlot.

        NOTE for task 042: rightSlot composition may need adjustment when the
        welcome-stage widget swap (PlaybookGalleryWidget → GetStartedCardsWidget)
        lands — leave this single-label composition unless 042 explicitly
        requires more right-aligned controls.
      */}
      <PaneHeader
        title="Context"
        icon={<DocumentRegular />}
        onCollapse={paneCollapse ? handleHeaderCollapse : undefined}
        expanded={isContextExpanded}
        rightSlot={
          /*
            Task 099 — rightSlot is now the ContextPaneMenu (Tools dropdown)
            ALONE, right-justified by virtue of PaneHeader's rightSlot CSS.
            The previous stage label `<Text>` ("Sources" / "Get Started" /
            etc.) was removed per operator request so the Context pane's
            PaneHeader visually mirrors the Workspace pane's PaneHeader
            (Tools dropdown only on the right). The `stageLabelMap` and
            `contextStage` state are kept for potential ARIA / debug use
            but are no longer rendered.

            The PaneHeader's rightSlot wrapper applies stopPropagation on
            clicks (task 094) so the Menu trigger does NOT accidentally
            collapse the pane.
          */
          <ContextPaneMenu
            selectedTool={selectedTool}
            onSelectTool={setSelectedTool}
          />
        }
      />

      {/* Content — adaptive based on stage and event state */}
      {renderContent()}
    </div>
  );
}
