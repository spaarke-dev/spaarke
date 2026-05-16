/**
 * SourcePanel.tsx — Right pane source widget renderer for the SpaarkeAi Code Page.
 *
 * Renders reference material widgets driven by SSE `source_pane` events received
 * from the BFF standalone context endpoint. Widget components are resolved lazily
 * from the @spaarke/ai-outputs source widget registry.
 *
 * Cross-pane linking: this panel subscribes to cross-pane link events via
 * useCrossPaneSubscription(). When the OutputPanel dispatches a link (e.g. user
 * clicks a citation), SourcePanel receives the event and scrolls/highlights the
 * referenced range in the appropriate source widget.
 *
 * @see ADR-021 — Fluent v9 semantic tokens only (no hardcoded colors)
 * @see ADR-022 — React 19, functional components, hooks
 * @see @spaarke/ai-outputs — source widget registry, resolveSourceWidget()
 * @see @spaarke/ai-outputs — cross-pane linking (useCrossPaneSubscription)
 */

import * as React from "react";
import {
  makeStyles,
  shorthands,
  tokens,
  Spinner,
  Text,
} from "@fluentui/react-components";
import { DocumentRegular } from "@fluentui/react-icons";
import {
  resolveSourceWidget,
  SourceWidgetType,
  useCrossPaneSubscription,
} from "@spaarke/ai-outputs";
import { useStandaloneAi } from "@spaarke/ai-context";
import type { SourceWidgetProps } from "@spaarke/ai-outputs";

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
  },
  headerTitle: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  content: {
    flex: 1,
    overflowY: "auto",
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
  },
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
  widgetWrapper: {
    backgroundColor: tokens.colorNeutralBackground1,
    borderRadius: tokens.borderRadiusMedium,
    ...shorthands.border("1px", "solid", tokens.colorNeutralStroke2),
    overflow: "hidden",
    flex: "1 1 auto",
    minHeight: "200px",
  },
  widgetError: {
    padding: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    color: tokens.colorPaletteRedForeground1,
    fontSize: tokens.fontSizeBase200,
  },
});

// ---------------------------------------------------------------------------
// Source widget slot state
// ---------------------------------------------------------------------------

interface SourceWidgetSlot {
  id: string;
  widgetType: SourceWidgetType;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  data: any;
  sourceRef: string;
  isLoading?: boolean;
  error?: string;
}

// ---------------------------------------------------------------------------
// ResolvedSourceWidget — lazy-loads and renders a single source widget
// ---------------------------------------------------------------------------

interface ResolvedSourceWidgetProps {
  slot: SourceWidgetSlot;
  activeSelectionRef?: string;
}

function ResolvedSourceWidget({ slot, activeSelectionRef }: ResolvedSourceWidgetProps): React.JSX.Element {
  const styles = useStyles();

  const [Component, setComponent] = React.useState<React.ComponentType<SourceWidgetProps<unknown>> | null>(null);
  const [resolveError, setResolveError] = React.useState<string | null>(null);

  React.useEffect(() => {
    let cancelled = false;

    resolveSourceWidget(slot.widgetType)
      .then((mod) => {
        if (!cancelled && mod) {
          setComponent(() => mod.default);
        }
      })
      .catch((err: Error) => {
        if (!cancelled) {
          setResolveError(err.message);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [slot.widgetType]);

  if (resolveError) {
    return (
      <div className={styles.widgetWrapper}>
        <div className={styles.widgetError}>
          Source widget unavailable: {resolveError}
        </div>
      </div>
    );
  }

  if (!Component) {
    return (
      <div className={styles.widgetWrapper}>
        <Spinner size="small" label={`Loading ${slot.widgetType}...`} />
      </div>
    );
  }

  return (
    <div
      className={styles.widgetWrapper}
      data-source-widget-id={slot.id}
      data-source-widget-type={slot.widgetType}
      data-source-ref={slot.sourceRef}
    >
      <Component
        data={slot.data}
        isLoading={slot.isLoading}
        error={slot.error}
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        {...({ activeSelectionRef, onSourceSelect: undefined } as any)}
      />
    </div>
  );
}

// ---------------------------------------------------------------------------
// SourcePanel
// ---------------------------------------------------------------------------

/**
 * SourcePanel — right pane for the SpaarkeAi three-pane layout.
 *
 * Renders source reference widgets from the @spaarke/ai-outputs registry.
 * Subscribes to cross-pane link events via useCrossPaneSubscription() to
 * react when the output pane dispatches citation links.
 *
 * When no source widgets are active, renders an empty state guiding the user
 * to run an AI analysis to populate source materials.
 *
 * SSE routing: When the BFF emits `source_pane` events, SprkChat forwards them
 * via the streaming callbacks in StandaloneAiContext. Full SSE event-driven
 * slot management completes in the next wave (task 041+).
 *
 * Source highlight: When a `source_highlight` SSE event arrives, the panel
 * updates the activeSelectionRef for the relevant slot, triggering the widget
 * to scroll to and highlight the referenced section.
 */
export function SourcePanel(): React.JSX.Element {
  const styles = useStyles();
  const { streamingState } = useStandaloneAi();

  // Source widget slots — populated by SSE source_pane events
  const [widgetSlots, setWidgetSlots] = React.useState<SourceWidgetSlot[]>([]);

  // Active selection ref per widget — updated by source_highlight SSE events
  // and cross-pane link subscriptions
  const [activeSelectionRef, setActiveSelectionRef] = React.useState<string | undefined>();

  // Subscribe to cross-pane link events dispatched by OutputPanel widgets
  useCrossPaneSubscription((event) => {
    // When a cross-pane link fires, update the active selection ref so the
    // source widget scrolls to the referenced range.
    // CrossPaneLinkEvent carries citationId, sourceWidgetId, highlightStart, highlightEnd.
    // We use citationId as the selection ref for the source widget.
    setActiveSelectionRef(event.citationId);
  });

  // When streaming begins, add a loading placeholder slot for the expected source widget.
  // When streaming ends, finalize the slot. Full SSE type discrimination in task 041+.
  const prevIsStreamingRef = React.useRef(false);

  React.useEffect(() => {
    const wasStreaming = prevIsStreamingRef.current;
    const nowStreaming = streamingState.isStreaming;

    if (!wasStreaming && nowStreaming && streamingState.operationId) {
      setWidgetSlots((prev) => [
        ...prev,
        {
          id: `source-${streamingState.operationId ?? Date.now()}`,
          widgetType: SourceWidgetType.DocumentViewer, // default; SSE type arrives in payload
          data: null,
          sourceRef: "",
          isLoading: true,
        },
      ]);
    } else if (wasStreaming && !nowStreaming && streamingState.operationId) {
      setWidgetSlots((prev) =>
        prev.map((slot) =>
          slot.id === `source-${streamingState.operationId}`
            ? { ...slot, isLoading: false }
            : slot
        )
      );
    }

    prevIsStreamingRef.current = nowStreaming;
  }, [streamingState]);

  const hasWidgets = widgetSlots.length > 0;

  return (
    <div className={styles.root}>
      {/* Header */}
      <div className={styles.header}>
        <DocumentRegular className={styles.headerIcon} />
        <Text className={styles.headerTitle} size={300}>
          Sources
        </Text>
      </div>

      {/* Widget list or empty state */}
      {hasWidgets ? (
        <div className={styles.content}>
          {widgetSlots.map((slot) => (
            <ResolvedSourceWidget
              key={slot.id}
              slot={slot}
              activeSelectionRef={activeSelectionRef}
            />
          ))}
        </div>
      ) : (
        <div className={styles.emptyState}>
          <DocumentRegular className={styles.emptyIcon} />
          <Text className={styles.emptyTitle} size={400}>
            Source Materials
          </Text>
          <Text className={styles.emptySubtitle} size={200}>
            Documents, web sources, and references cited by the AI
            will appear here during analysis.
          </Text>
        </div>
      )}
    </div>
  );
}
