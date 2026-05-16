/**
 * TimelineWidget
 *
 * Renders a vertical timeline of dated events with milestone markers.
 * Each event shows a left-aligned date column and a right column for
 * label and optional description. Milestone events are visually distinct
 * using Fluent v9 brand color tokens.
 *
 * NOT PCF-safe — requires React 19 and Fluent UI v9.
 *
 * Data shape injected via the AI streaming response (already parsed by the
 * calling code page). No direct API calls inside this widget.
 *
 * @see ADR-021 — Fluent UI v9 design system (no hard-coded colors)
 * @see ADR-012 — Shared component library
 */

import * as React from "react";
import {
  makeStyles,
  mergeClasses,
  shorthands,
  tokens,
  Text,
  Spinner,
} from "@fluentui/react-components";
import type { OutputWidgetProps } from "../types";

// ---------------------------------------------------------------------------
// Data types
// ---------------------------------------------------------------------------

export interface TimelineEvent {
  /** Unique identifier for this event. */
  id: string;
  /** ISO 8601 date string or human-readable date label (e.g. "2024-03-15"). */
  date: string;
  /** Short label / title for this event. */
  label: string;
  /** Optional longer description rendered below the label. */
  description?: string;
  /** When true, renders with a brand-colored milestone marker. */
  isMilestone?: boolean;
}

export interface TimelineData {
  /** List of timeline events in chronological order. */
  events: TimelineEvent[];
}

export type TimelineWidgetProps = OutputWidgetProps<TimelineData>;

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
    padding: tokens.spacingHorizontalL,
    overflowY: "auto",
  },
  errorText: {
    color: tokens.colorStatusDangerForeground1,
  },
  eventRow: {
    display: "grid",
    gridTemplateColumns: "96px 20px 1fr",
    gap: `0 ${tokens.spacingHorizontalS}`,
    alignItems: "start",
    position: "relative",
  },
  dateColumn: {
    textAlign: "right",
    paddingTop: tokens.spacingVerticalXS,
    color: tokens.colorNeutralForeground3,
    whiteSpace: "nowrap",
  },
  markerColumn: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    position: "relative",
  },
  dot: {
    width: "12px",
    height: "12px",
    borderRadius: "50%",
    backgroundColor: tokens.colorNeutralForeground3,
    flexShrink: 0,
    marginTop: "4px",
    zIndex: 1,
  },
  dotMilestone: {
    backgroundColor: tokens.colorBrandBackground,
    width: "14px",
    height: "14px",
    marginTop: "3px",
    boxShadow: `0 0 0 2px ${tokens.colorBrandBackgroundHover}`,
  },
  line: {
    width: "2px",
    flexGrow: 1,
    backgroundColor: tokens.colorNeutralStroke2,
    marginTop: tokens.spacingVerticalXXS,
    minHeight: tokens.spacingVerticalL,
  },
  contentColumn: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXXS,
    paddingBottom: tokens.spacingVerticalM,
  },
  labelMilestone: {
    color: tokens.colorBrandForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },
  description: {
    color: tokens.colorNeutralForeground2,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * TimelineWidget renders a vertical timeline with dated events. Events are
 * displayed in order with a left-aligned date, a visual marker/line, and
 * right-side label and description. Milestone events use brand color tokens
 * for the dot and label text to make them visually prominent.
 */
export default function TimelineWidget({
  data,
  isLoading,
  error,
  className,
}: TimelineWidgetProps): React.ReactElement {
  const styles = useStyles();

  if (isLoading) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <Spinner size="medium" label="Loading timeline..." />
      </div>
    );
  }

  if (error) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <Text className={styles.errorText}>{error}</Text>
      </div>
    );
  }

  const { events } = data;

  return (
    <div className={mergeClasses(styles.root, className)}>
      {events.map((event, index) => {
        const isMilestone = event.isMilestone === true;
        const isLast = index === events.length - 1;

        return (
          <div key={event.id} className={styles.eventRow}>
            {/* Date column */}
            <Text size={200} className={styles.dateColumn}>
              {event.date}
            </Text>

            {/* Marker column: dot + connecting line */}
            <div className={styles.markerColumn}>
              <div
                className={mergeClasses(
                  styles.dot,
                  isMilestone && styles.dotMilestone
                )}
              />
              {!isLast && <div className={styles.line} />}
            </div>

            {/* Content column */}
            <div className={styles.contentColumn}>
              <Text
                size={300}
                className={isMilestone ? styles.labelMilestone : undefined}
              >
                {event.label}
              </Text>
              {event.description && (
                <Text size={200} className={styles.description}>
                  {event.description}
                </Text>
              )}
            </div>
          </div>
        );
      })}
    </div>
  );
}
