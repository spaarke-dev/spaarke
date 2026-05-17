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
import type { AiPaneEvent } from "@spaarke/ai-context";
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
 * Map an SSE widgetType string to the SourceWidgetType enum value.
 * Falls back to DocumentViewer for unknown values.
 */
function resolveSourceWidgetTypeFromString(raw: string | undefined): SourceWidgetType {
  if (!raw) return SourceWidgetType.DocumentViewer;
  const match = Object.values(SourceWidgetType).find((v) => v === raw);
  return match ?? SourceWidgetType.DocumentViewer;
}

/**
 * SourcePanel — right pane for the SpaarkeAi three-pane layout.
 *
 * Renders source reference widgets from the @spaarke/ai-outputs registry.
 * Subscribes to:
 *   - `source_pane` SSE events forwarded by OutputPanel via DOM CustomEvent
 *     ('sprk-ai-pane-event') to populate slots with the correct SourceWidgetType.
 *   - `source_highlight` SSE events (same DOM bus) to update the active selection ref.
 *   - Cross-pane link events via useCrossPaneSubscription() when the OutputPanel
 *     dispatches a citation link from an output widget.
 *
 * When no source widgets are active, renders an empty state.
 */
export function SourcePanel(): React.JSX.Element {
  const styles = useStyles();
  const { streamingState } = useStandaloneAi();

  // Source widget slots — populated by SSE source_pane events
  const [widgetSlots, setWidgetSlots] = React.useState<SourceWidgetSlot[]>([]);

  // Active selection ref — updated by source_highlight SSE events and cross-pane links
  const [activeSelectionRef, setActiveSelectionRef] = React.useState<string | undefined>();

  // Subscribe to cross-pane link events dispatched by OutputPanel widgets
  useCrossPaneSubscription((event) => {
    setActiveSelectionRef(event.citationId);
  });

  // Receive source_pane and source_highlight events forwarded by OutputPanel.
  // OutputPanel owns the single subscribePaneEvents slot and fans out non-output_pane
  // events via a DOM CustomEvent ('sprk-ai-pane-event'). This avoids the last-write-wins
  // conflict that would occur if both panels called subscribePaneEvents independently.
  React.useEffect(() => {
    const handleSourcePaneEvent = (e: Event) => {
      const customEvent = e as CustomEvent<AiPaneEvent>;
      const paneEvent = customEvent.detail;

      if (paneEvent.event === 'source_pane') {
        const widgetType = resolveSourceWidgetTypeFromString(paneEvent.widgetType);
        const slotId = `source-${paneEvent.widgetType ?? 'unknown'}-${Date.now()}`;

        setWidgetSlots((prev) => {
          // Upgrade existing loading placeholder if present
          const loadingIdx = prev.findIndex((s) => s.isLoading);
          if (loadingIdx !== -1) {
            const upgraded = [...prev];
            upgraded[loadingIdx] = {
              ...upgraded[loadingIdx],
              widgetType,
              data: paneEvent.payload ?? null,
              sourceRef: paneEvent.sourceRef ?? '',
              isLoading: false,
            };
            return upgraded;
          }
          return [
            ...prev,
            {
              id: slotId,
              widgetType,
              data: paneEvent.payload ?? null,
              sourceRef: paneEvent.sourceRef ?? '',
              isLoading: false,
            },
          ];
        });
      } else if (paneEvent.event === 'source_highlight') {
        if (paneEvent.selectionRef) {
          setActiveSelectionRef(paneEvent.selectionRef);
        }
      }
    };

    document.addEventListener('sprk-ai-pane-event', handleSourcePaneEvent);
    return () => {
      document.removeEventListener('sprk-ai-pane-event', handleSourcePaneEvent);
    };
  }, []);

  // Track streaming transitions to manage loading placeholder slots.
  const prevIsStreamingRef = React.useRef(false);

  React.useEffect(() => {
    const wasStreaming = prevIsStreamingRef.current;
    const nowStreaming = streamingState.isStreaming;

    if (!wasStreaming && nowStreaming && streamingState.operationId) {
      setWidgetSlots((prev) => [
        ...prev,
        {
          id: `source-${streamingState.operationId ?? Date.now()}`,
          widgetType: SourceWidgetType.DocumentViewer, // placeholder; overwritten by source_pane event
          data: null,
          sourceRef: '',
          isLoading: true,
        },
      ]);
    } else if (wasStreaming && !nowStreaming) {
      // Remove loading placeholders not upgraded by a source_pane event
      setWidgetSlots((prev) => prev.filter((s) => !s.isLoading));
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
