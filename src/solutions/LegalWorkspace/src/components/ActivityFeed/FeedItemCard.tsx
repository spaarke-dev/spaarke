/**
 * FeedItemCard — interactive card component for a single Updates Feed event.
 *
 * Layout (per wireframe):
 *   [type icon circle] [eventTypeName : eventName]         [flag] [AI sparkle]
 *                       [description (2 lines max)]
 *                       [priority badge] [record type] [regarding name]
 *                       [modifiedon]  [duedate]
 *
 * Flag behaviour (task 012):
 *   - Reads flag state from FeedTodoSyncContext (not from event.sprk_todoflag directly).
 *   - Optimistic UI: flag icon flips immediately on click; context issues the
 *     Dataverse write with 300 ms debounce. On failure the icon reverts.
 *   - Shows a pending spinner overlay on the flag button while the write is in-flight.
 *   - Error state: tooltip shows error text if the last write failed.
 *
 * Design constraints (task 011):
 *   - ALL colours from Fluent UI v9 semantic tokens — zero hardcoded hex/rgb
 *   - makeStyles (Griffel) only for custom styles
 *   - Icon-only buttons MUST have aria-label
 *   - Hover: colorNeutralBackground1Hover
 *   - Dark mode + high-contrast supported automatically via token system
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Button,
  Tooltip,
  Spinner,
  mergeClasses,
} from "@fluentui/react-components";
import {
  FlagRegular,
  FlagFilled,
  SparkleRegular,
} from "@fluentui/react-icons";
import { IEvent } from "../../types/entities";
import { PriorityLevel } from "../../types/enums";
import { formatRelativeTime } from "../../utils/formatRelativeTime";
import { getTypeIcon, getTypeIconLabel } from "../../utils/typeIconMap";
import { useFeedTodoSync } from "../../hooks/useFeedTodoSync";

// ---------------------------------------------------------------------------
// Priority badge token mapping
// ---------------------------------------------------------------------------

/**
 * Maps a PriorityLevel to the Fluent v9 semantic background token for the
 * badge colour. Dataverse: 0=Low, 1=Normal, 2=High, 3=Urgent.
 */
const PRIORITY_BADGE_STYLES: Record<PriorityLevel, React.CSSProperties> = {
  Urgent: {
    backgroundColor: tokens.colorPaletteRedBackground3,
    color: tokens.colorNeutralForegroundOnBrand,
  },
  High: {
    backgroundColor: tokens.colorPaletteYellowBackground3,
    color: tokens.colorNeutralForeground1,
  },
  Normal: {
    backgroundColor: tokens.colorPaletteBlueBorderActive,
    color: tokens.colorNeutralForegroundOnBrand,
  },
  Low: {
    backgroundColor: tokens.colorNeutralBackground3,
    color: tokens.colorNeutralForeground1,
  },
};

/** Map the raw sprk_priority option-set number to a PriorityLevel label */
function derivePriorityLevel(priority: number | undefined): PriorityLevel | null {
  if (priority === undefined || priority === null) return null;
  // Dataverse option-set: 0=Low, 1=Normal, 2=High, 3=Urgent
  switch (priority) {
    case 0: return "Low";
    case 1: return "Normal";
    case 2: return "High";
    case 3: return "Urgent";
    default: return null;
  }
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  card: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke3,
    backgroundColor: tokens.colorNeutralBackground1,
    cursor: "default",
    transitionProperty: "background-color",
    transitionDuration: tokens.durationFaster,
    transitionTimingFunction: tokens.curveEasyEase,
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
    ":focus-visible": {
      outlineStyle: "solid",
      outlineWidth: "2px",
      outlineColor: tokens.colorBrandStroke1,
      outlineOffset: "-2px",
    },
  },

  // ── Main row: icon + content + actions ─────────────────────────────────
  mainRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "flex-start",
    gap: tokens.spacingHorizontalS,
  },

  // ── Left: type icon circle ─────────────────────────────────────────────
  typeIconCircle: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    flexShrink: 0,
    width: "32px",
    height: "32px",
    borderRadius: "50%",
    backgroundColor: tokens.colorBrandBackground2,
    color: tokens.colorBrandForeground1,
    marginTop: "2px",
  },

  // ── Centre: content column ─────────────────────────────────────────────
  contentColumn: {
    flex: "1 1 0",
    minWidth: 0,
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXXS,
  },
  title: {
    display: "block",
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },
  description: {
    display: "-webkit-box",
    WebkitLineClamp: "2",
    WebkitBoxOrient: "vertical",
    overflow: "hidden",
    color: tokens.colorNeutralForeground2,
  },

  // ── Metadata row: priority badge + record type + regarding name ────────
  metaRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    flexWrap: "wrap",
  },
  metaText: {
    color: tokens.colorNeutralForeground3,
    whiteSpace: "nowrap",
    overflow: "hidden",
    textOverflow: "ellipsis",
  },
  metaDivider: {
    color: tokens.colorNeutralForeground4,
  },

  // ── Timestamp row: modifiedon + duedate ────────────────────────────────
  timestampRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
  },
  timestamp: {
    color: tokens.colorNeutralForeground3,
    whiteSpace: "nowrap",
  },
  dueDateOverdue: {
    color: tokens.colorPaletteRedForeground3,
    whiteSpace: "nowrap",
  },

  // ── Right: action buttons ──────────────────────────────────────────────
  actionsColumn: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalXXS,
    flexShrink: 0,
    paddingTop: "2px",
  },

  // Flag active state
  flagButtonActive: {
    color: tokens.colorBrandForeground1,
  },
  flagButtonWrapper: {
    position: "relative",
    display: "inline-flex",
    alignItems: "center",
    justifyContent: "center",
  },
  flagPendingSpinner: {
    position: "absolute",
    pointerEvents: "none",
  },
  flagButtonError: {
    color: tokens.colorPaletteRedForeground3,
  },
});

// ---------------------------------------------------------------------------
// Priority Badge sub-component
// ---------------------------------------------------------------------------

interface IPriorityBadgeProps {
  level: PriorityLevel;
}

const PriorityBadge: React.FC<IPriorityBadgeProps> = ({ level }) => {
  const badgeStyle = PRIORITY_BADGE_STYLES[level];
  return (
    <span
      role="img"
      aria-label={`Priority: ${level}`}
      style={{
        display: "inline-flex",
        alignItems: "center",
        justifyContent: "center",
        borderRadius: tokens.borderRadiusSmall,
        paddingTop: "1px",
        paddingBottom: "1px",
        paddingLeft: tokens.spacingHorizontalXS,
        paddingRight: tokens.spacingHorizontalXS,
        fontSize: tokens.fontSizeBase100,
        fontWeight: tokens.fontWeightSemibold,
        lineHeight: tokens.lineHeightBase100,
        whiteSpace: "nowrap",
        ...badgeStyle,
      }}
    >
      {level}
    </span>
  );
};

// ---------------------------------------------------------------------------
// Record Type Badge sub-component
// ---------------------------------------------------------------------------

interface IRecordTypeBadgeProps {
  typeName: string;
}

const RecordTypeBadge: React.FC<IRecordTypeBadgeProps> = ({ typeName }) => (
  <span
    role="img"
    aria-label={`Record type: ${typeName}`}
    style={{
      display: "inline-flex",
      alignItems: "center",
      justifyContent: "center",
      borderRadius: tokens.borderRadiusSmall,
      paddingTop: "1px",
      paddingBottom: "1px",
      paddingLeft: tokens.spacingHorizontalXS,
      paddingRight: tokens.spacingHorizontalXS,
      fontSize: tokens.fontSizeBase100,
      fontWeight: tokens.fontWeightSemibold,
      lineHeight: tokens.lineHeightBase100,
      whiteSpace: "nowrap",
      backgroundColor: tokens.colorNeutralBackground3,
      color: tokens.colorNeutralForeground2,
    }}
  >
    {typeName}
  </span>
);

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IFeedItemCardProps {
  /** The event to display */
  event: IEvent;
  /**
   * Called when the user clicks the AI Summary button.
   * The parent opens the AI Summary dialog (task 013).
   */
  onAISummary: (eventId: string) => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const FeedItemCard: React.FC<IFeedItemCardProps> = React.memo(
  ({ event, onAISummary }) => {
    const styles = useStyles();

    // Read flag state from shared context (optimistic updates included)
    const { isFlagged, toggleFlag, isPending, getError } = useFeedTodoSync();
    const flagged = isFlagged(event.sprk_eventid);
    const pending = isPending(event.sprk_eventid);
    const flagError = getError(event.sprk_eventid);

    const priorityLevel = derivePriorityLevel(event.sprk_priority);
    const relativeTime = formatRelativeTime(event.modifiedon);

    // Resolve type icon component
    const TypeIconComponent = getTypeIcon(event.eventTypeName);
    const typeIconLabel = getTypeIconLabel(event.eventTypeName);

    // Build title: "EventType : EventName" or just "EventName" if no type
    const titleText = event.eventTypeName
      ? `${event.eventTypeName} : ${event.sprk_eventname}`
      : event.sprk_eventname;

    // Due date formatting
    const dueDateText = event.sprk_duedate
      ? `Due: ${formatRelativeTime(event.sprk_duedate)}`
      : null;
    const isDueOverdue = event.sprk_duedate
      ? new Date(event.sprk_duedate) < new Date()
      : false;

    // Flag toggle handler
    const handleFlagToggle = React.useCallback(() => {
      void toggleFlag(event.sprk_eventid);
    }, [toggleFlag, event.sprk_eventid]);

    const handleAISummary = React.useCallback(() => {
      onAISummary(event.sprk_eventid);
    }, [onAISummary, event.sprk_eventid]);

    // Build aria label for the card
    const cardAriaLabel = [
      titleText,
      typeIconLabel,
      priorityLevel ? `Priority: ${priorityLevel}.` : "",
      event.regardingRecordTypeName || "",
      event.sprk_regardingrecordname || "",
      relativeTime,
      dueDateText || "",
      flagged ? "Flagged as to-do." : "",
    ]
      .filter(Boolean)
      .join(" ");

    // Flag button tooltip
    const flagTooltip = flagError
      ? `Error: ${flagError} — click to retry`
      : flagged
      ? "Remove flag"
      : "Flag as to-do";

    const flagAriaLabel = flagError
      ? `Flag error: ${flagError}`
      : flagged
      ? "Remove flag"
      : "Flag as to-do";

    const flagButtonClass = flagError
      ? mergeClasses(styles.flagButtonError)
      : flagged
      ? styles.flagButtonActive
      : undefined;

    return (
      <div
        className={styles.card}
        role="listitem"
        tabIndex={0}
        aria-label={cardAriaLabel}
      >
        {/* ── Main row: icon + content + actions ──────────────────── */}
        <div className={styles.mainRow}>
          {/* Type icon in circle */}
          <div
            className={styles.typeIconCircle}
            aria-label={typeIconLabel}
            role="img"
          >
            <TypeIconComponent fontSize={16} />
          </div>

          {/* Content: title, description, metadata, timestamps */}
          <div className={styles.contentColumn}>
            {/* Title: EventType : EventName */}
            <Text
              as="span"
              size={300}
              className={styles.title}
            >
              {titleText}
            </Text>

            {/* Description (sprk_description) */}
            {event.sprk_description && (
              <Text
                as="span"
                size={200}
                className={styles.description}
              >
                {event.sprk_description}
              </Text>
            )}

            {/* Metadata row: priority + record type + regarding name */}
            <div className={styles.metaRow}>
              {priorityLevel && <PriorityBadge level={priorityLevel} />}
              {event.regardingRecordTypeName && (
                <RecordTypeBadge typeName={event.regardingRecordTypeName} />
              )}
              {event.sprk_regardingrecordname && (
                <Text size={100} className={styles.metaText}>
                  {event.sprk_regardingrecordname}
                </Text>
              )}
            </div>

            {/* Timestamp row: modifiedon + duedate */}
            <div className={styles.timestampRow}>
              <Text size={100} className={styles.timestamp}>
                {relativeTime}
              </Text>
              {dueDateText && (
                <>
                  <Text size={100} className={styles.metaDivider} aria-hidden="true">
                    |
                  </Text>
                  <Text
                    size={100}
                    className={isDueOverdue ? styles.dueDateOverdue : styles.timestamp}
                  >
                    {dueDateText}
                  </Text>
                </>
              )}
            </div>
          </div>

          {/* Action buttons (right side) */}
          <div className={styles.actionsColumn}>
            {/* Flag toggle */}
            <Tooltip
              content={flagTooltip}
              relationship="label"
            >
              <div className={styles.flagButtonWrapper}>
                <Button
                  appearance="subtle"
                  size="small"
                  icon={
                    flagged
                      ? <FlagFilled aria-hidden="true" />
                      : <FlagRegular aria-hidden="true" />
                  }
                  aria-label={flagAriaLabel}
                  aria-pressed={flagged}
                  aria-busy={pending}
                  className={flagButtonClass}
                  onClick={handleFlagToggle}
                  disabled={pending}
                />
                {pending && (
                  <span className={styles.flagPendingSpinner} aria-hidden="true">
                    <Spinner size="extra-tiny" />
                  </span>
                )}
              </div>
            </Tooltip>

            {/* AI Summary */}
            <Tooltip content="Generate AI summary" relationship="label">
              <Button
                appearance="subtle"
                size="small"
                icon={<SparkleRegular aria-hidden="true" />}
                aria-label="Generate AI summary"
                onClick={handleAISummary}
              />
            </Tooltip>
          </div>
        </div>
      </div>
    );
  }
);

FeedItemCard.displayName = "FeedItemCard";
