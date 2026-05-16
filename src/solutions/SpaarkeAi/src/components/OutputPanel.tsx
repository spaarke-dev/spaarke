/**
 * OutputPanel.tsx — Center pane output widget renderer for the SpaarkeAi Code Page.
 *
 * Renders AI output widgets driven by SSE `output_pane` events received from the BFF
 * standalone context endpoint. Widget components are resolved lazily from the
 * @spaarke/ai-outputs output widget registry.
 *
 * Data flow:
 *   BFF SSE → SprkChat (decodes event) → dispatchCrossPaneLink / streaming callbacks
 *   → StandaloneAiContext.streamingState → OutputPanel re-renders with new widget items
 *
 * The panel maintains a local list of active output widget "slots" — each slot
 * maps an OutputWidgetType to its resolved component and data payload. Widget
 * components are loaded once and cached via React.lazy + React.Suspense.
 *
 * Cross-pane linking: citation clicks in output widgets use dispatchCrossPaneLink()
 * to notify the SourcePanel, which subscribes via useCrossPaneSubscription().
 *
 * @see ADR-021 — Fluent v9 semantic tokens only (no hardcoded colors)
 * @see ADR-022 — React 19, functional components, hooks
 * @see @spaarke/ai-outputs — output widget registry, resolveOutputWidget()
 * @see @spaarke/ai-outputs — cross-pane linking (dispatchCrossPaneLink)
 */

import * as React from "react";
import {
  makeStyles,
  shorthands,
  tokens,
  Spinner,
  Text,
  ProgressBar,
} from "@fluentui/react-components";
import { BrainCircuitRegular } from "@fluentui/react-icons";
import {
  resolveOutputWidget,
  OutputWidgetType,
  useDispatchCrossPaneLink,
} from "@spaarke/ai-outputs";
import { useStandaloneAi } from "@spaarke/ai-context";
import type { OutputWidgetProps } from "@spaarke/ai-outputs";

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
  streamingBar: {
    flexShrink: 0,
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
// Widget slot state
// ---------------------------------------------------------------------------

interface OutputWidgetSlot {
  id: string;
  widgetType: OutputWidgetType;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  data: any;
  isLoading?: boolean;
  error?: string;
}

// ---------------------------------------------------------------------------
// ResolvedWidget — lazy-loads and renders a single output widget
// ---------------------------------------------------------------------------

interface ResolvedWidgetProps {
  slot: OutputWidgetSlot;
}

function ResolvedWidget({ slot }: ResolvedWidgetProps): React.JSX.Element {
  const styles = useStyles();
  const dispatchLink = useDispatchCrossPaneLink();

  const [Component, setComponent] = React.useState<React.ComponentType<OutputWidgetProps<unknown>> | null>(null);
  const [resolveError, setResolveError] = React.useState<string | null>(null);

  React.useEffect(() => {
    let cancelled = false;

    resolveOutputWidget(slot.widgetType)
      .then((Comp) => {
        if (!cancelled) {
          setComponent(() => Comp);
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
          Widget unavailable: {resolveError}
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
      data-widget-id={slot.id}
      data-widget-type={slot.widgetType}
    >
      <Component
        data={slot.data}
        isLoading={slot.isLoading}
        error={slot.error}
        // Cross-pane linking: output widgets call onLink to activate source pane.
        // The dispatchLink helper broadcasts a CrossPaneLinkEvent via CustomEvent
        // on document. SourcePanel subscribes via useCrossPaneSubscription().
        // Widgets that support cross-pane linking accept an onLink prop.
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        {...({ onLink: (citationId: string, sourceWidgetId: string, highlightStart: number, highlightEnd: number) => {
          dispatchLink({
            citationId,
            sourceWidgetId: sourceWidgetId || `source-${slot.id}`,
            highlightStart: highlightStart ?? 0,
            highlightEnd: highlightEnd ?? 0,
          });
        }} as any)}
      />
    </div>
  );
}

// ---------------------------------------------------------------------------
// OutputPanel
// ---------------------------------------------------------------------------

/**
 * OutputPanel — center pane for the SpaarkeAi three-pane layout.
 *
 * Renders a list of output widgets from the @spaarke/ai-outputs registry.
 * In the current wave, widget slots are populated by listening to SSE events
 * via StandaloneAiContext streaming state changes. When no widgets are active,
 * renders an empty state with a prompt to start a conversation.
 *
 * SSE routing: When the BFF emits `output_pane` events, SprkChat forwards them
 * via the streaming callbacks registered with StandaloneAiContext. OutputPanel
 * listens to streamingState changes and renders the appropriate widgets.
 *
 * For this task (040), a representative set of slots is rendered based on
 * the current streamingState. Full SSE event-driven slot management will be
 * completed as part of SSE wire-up in the next wave.
 */
export function OutputPanel(): React.JSX.Element {
  const styles = useStyles();
  const { streamingState } = useStandaloneAi();

  // Widget slots — populated by SSE output_pane events
  // Initially empty; managed as local state driven by streaming callbacks
  const [widgetSlots, setWidgetSlots] = React.useState<OutputWidgetSlot[]>([]);

  // When streaming begins, add a loading slot for the expected output widget.
  // When streaming ends, finalize the slot (remove loading state).
  // This is the lightweight SSE-driven slot management for task 040.
  // Full SSE event parsing (widgetType discrimination) happens in task 041+.
  const prevIsStreamingRef = React.useRef(false);

  React.useEffect(() => {
    const wasStreaming = prevIsStreamingRef.current;
    const nowStreaming = streamingState.isStreaming;

    if (!wasStreaming && nowStreaming && streamingState.operationId) {
      // New streaming operation started — add a loading placeholder slot
      setWidgetSlots((prev) => [
        ...prev,
        {
          id: streamingState.operationId ?? `slot-${Date.now()}`,
          widgetType: OutputWidgetType.AnalysisEditor, // default widget until SSE type arrives
          data: null,
          isLoading: true,
        },
      ]);
    } else if (wasStreaming && !nowStreaming && streamingState.operationId) {
      // Streaming ended — finalize the slot (clear loading state)
      setWidgetSlots((prev) =>
        prev.map((slot) =>
          slot.id === streamingState.operationId
            ? { ...slot, isLoading: false }
            : slot
        )
      );
    }

    prevIsStreamingRef.current = nowStreaming;
  }, [streamingState]);

  const isStreaming = streamingState.isStreaming;
  const hasWidgets = widgetSlots.length > 0;

  return (
    <div className={styles.root}>
      {/* Header */}
      <div className={styles.header}>
        <BrainCircuitRegular className={styles.headerIcon} />
        <Text className={styles.headerTitle} size={300}>
          AI Output
        </Text>
        {isStreaming && (
          <Text size={100} style={{ color: tokens.colorNeutralForeground3, marginLeft: "auto" }}>
            AI is thinking...
          </Text>
        )}
      </div>

      {/* Streaming progress bar */}
      {isStreaming && (
        <ProgressBar
          className={styles.streamingBar}
          shape="square"
          thickness="medium"
        />
      )}

      {/* Widget list or empty state */}
      {hasWidgets ? (
        <div className={styles.content}>
          {widgetSlots.map((slot) => (
            <ResolvedWidget key={slot.id} slot={slot} />
          ))}
        </div>
      ) : (
        <div className={styles.emptyState}>
          <BrainCircuitRegular className={styles.emptyIcon} />
          <Text className={styles.emptyTitle} size={400}>
            AI Output Pane
          </Text>
          <Text className={styles.emptySubtitle} size={200}>
            AI analysis results, search outputs, and structured data
            will appear here when you send a message.
          </Text>
        </div>
      )}
    </div>
  );
}
