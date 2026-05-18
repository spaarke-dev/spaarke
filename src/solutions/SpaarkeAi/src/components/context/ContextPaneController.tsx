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
  usePaneEvent,
  resolveContextWidget,
  getContextWidgetForTab,
} from "@spaarke/ai-widgets";
import type { ContextWidgetComponent, ContextPaneEvent, WorkspacePaneEvent } from "@spaarke/ai-widgets";
import { useShellStage } from "../shell/ThreePaneShell";
import type { ShellStage } from "../shell/ThreePaneShell";

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

  // Header bar
  header: {
    flexShrink: 0,
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke1,
    backgroundColor: tokens.colorNeutralBackground1,
    minHeight: "40px",
  },
  headerIcon: {
    color: tokens.colorBrandForeground1,
    fontSize: "16px",
    flexShrink: 0,
  },
  headerTitle: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    flexGrow: 1,
  },
  headerStageLabel: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground4,
    flexShrink: 0,
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
  // Also auto-load the PlaybookGalleryWidget when entering Stage 1 (AIPU2-107).
  React.useEffect(() => {
    setContextStage(shellStageToContextStage(currentStage));

    // Auto-load PlaybookGalleryWidget in Stage 1 so the gallery is interactive
    // from the start (not just a static placeholder). The widget is resolved
    // lazily from the ContextWidgetRegistry — same path as context_update events.
    if (currentStage === "welcome") {
      setIsResolving(true);
      resolveContextWidget("playbook-gallery").then((Component) => {
        setIsResolving(false);
        if (Component !== null) {
          setActiveWidget({
            Component,
            widgetType: "playbook-gallery",
            data: null,
            isLoading: false,
          });
        }
      }).catch(() => {
        setIsResolving(false);
      });
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
  // Derive header stage label for debugging / accessibility
  // ---------------------------------------------------------------------------

  const stageLabelMap: Record<ContextStage, string> = {
    "playbook-gallery": "Gallery",
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
    // Stage 1 — welcome: no playbook selected, show gallery placeholder.
    // The PlaybookGalleryWidget (AIPU2-086) should ideally be loaded here via
    // a context_update event, but if no event has arrived yet we show the
    // static placeholder so the pane is never empty.
    if (currentStage === "welcome") {
      // If a widget has been loaded (e.g. PlaybookGalleryWidget via context_update),
      // prefer it over the static placeholder so the gallery renders interactively.
      if (activeWidget !== null) {
        return (
          <div className={styles.content} data-testid="context-pane-widget">
            <ResolvedContextWidget slot={activeWidget} highlightRef={highlightRef} />
          </div>
        );
      }
      return (
        <div className={styles.emptyState} data-testid="context-pane-welcome">
          <DocumentRegular className={styles.emptyIcon} />
          <Text className={styles.emptyTitle} size={400}>
            Select a Playbook
          </Text>
          <Text className={styles.emptySubtitle} size={200}>
            Choose a playbook from the right panel to configure the AI agent
            and get started. Context, sources, and citations will appear here.
          </Text>
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

  return (
    <div className={styles.root} data-testid="context-pane-controller">
      {/* Header */}
      <div className={styles.header}>
        <DocumentRegular className={styles.headerIcon} />
        <Text className={styles.headerTitle} size={300}>
          Context
        </Text>
        <Text className={styles.headerStageLabel} size={100}>
          {stageLabelMap[contextStage]}
        </Text>
      </div>

      {/* Content — adaptive based on stage and event state */}
      {renderContent()}
    </div>
  );
}
