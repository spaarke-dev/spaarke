/**
 * FeedItemCard — interactive card component for a single Updates Feed event.
 *
 * Layout (redesigned for spacious 2-row cards):
 *   ┌──────────────────────────────────────────────────────────────────┐
 *   │▌                                                                 │
 *   │▌  [Icon 40px]  [Type badge]  Event Name          [Flag]        │
 *   │▌               [Priority] [Matter ref] · Due date    [Email]   │
 *   │▌                                                     [Teams]   │
 *   │▌                                                     [Edit]    │
 *   │▌                                                     [AI]      │
 *   │▌                                                                 │
 *   └──────────────────────────────────────────────────────────────────┘
 *     ↑ 3px left border (red=overdue, amber=soon, green=on track, neutral)
 *
 * Design constraints:
 *   - ALL colours from Fluent UI v9 semantic tokens — zero hardcoded hex/rgb
 *   - makeStyles (Griffel) only for custom styles
 *   - Icon-only buttons MUST have aria-label
 *   - Generous white space: L/XL padding tokens
 *   - Dark mode + high-contrast supported automatically via token system
 *   - Action buttons: medium size (24px+ touch targets), vertical stack
 *
 * Flag behaviour (task 012):
 *   - Reads flag state from FeedTodoSyncContext (optimistic updates).
 *   - Shows pending spinner overlay while Dataverse write is in-flight.
 *   - Error state: tooltip shows error text if last write failed.
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
  MailRegular,
  ChatRegular,
  EditRegular,
} from "@fluentui/react-icons";
import { IEvent } from "../../types/entities";
import { PriorityLevel } from "../../types/enums";
import { formatRelativeTime } from "../../utils/formatRelativeTime";
import { getTypeIcon, getTypeIconLabel } from "../../utils/typeIconMap";
import { useFeedTodoSync } from "../../hooks/useFeedTodoSync";
import { navigateToEntity } from "../../utils/navigation";

// ---------------------------------------------------------------------------
// Priority badge token mapping
// ---------------------------------------------------------------------------

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

function derivePriorityLevel(priority: number | undefined): PriorityLevel | null {
  if (priority === undefined || priority === null) return null;
  switch (priority) {
    case 0: return "Low";
    case 1: return "Normal";
    case 2: return "High";
    case 3: return "Urgent";
    default: return null;
  }
}

// ---------------------------------------------------------------------------
// Urgency derivation (for left border accent)
// ---------------------------------------------------------------------------

type UrgencyTier = 'overdue' | 'dueSoon' | 'onTrack' | 'neutral';

function deriveUrgencyTier(dueDate: string | undefined): UrgencyTier {
  if (!dueDate) return 'neutral';
  const now = new Date();
  const due = new Date(dueDate);
  if (isNaN(due.getTime())) return 'neutral';
  const diffDays = Math.ceil((due.getTime() - now.getTime()) / (1000 * 60 * 60 * 24));
  if (diffDays < 0) return 'overdue';
  if (diffDays <= 3) return 'dueSoon';
  if (diffDays <= 10) return 'onTrack';
  return 'neutral';
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  // ── Card container ────────────────────────────────────────────────────
  card: {
    display: "flex",
    flexDirection: "column",
    paddingTop: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalL,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
    backgroundColor: tokens.colorNeutralBackground1,
    borderRadius: tokens.borderRadiusMedium,
    boxShadow: tokens.shadow2,
    marginBottom: tokens.spacingVerticalS,
    borderLeftWidth: "3px",
    borderLeftStyle: "solid",
    borderLeftColor: tokens.colorNeutralStroke2, // default, overridden by urgency class
    cursor: "default",
    transitionProperty: "background-color, box-shadow",
    transitionDuration: tokens.durationFaster,
    transitionTimingFunction: tokens.curveEasyEase,
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground1Hover,
      boxShadow: tokens.shadow4,
    },
    ":focus-visible": {
      outlineStyle: "solid",
      outlineWidth: "2px",
      outlineColor: tokens.colorBrandStroke1,
      outlineOffset: "-2px",
    },
  },

  // ── Left border urgency variants ──────────────────────────────────────
  urgencyOverdue: {
    borderLeftColor: tokens.colorPaletteRedBorder2,
  },
  urgencyDueSoon: {
    borderLeftColor: tokens.colorPaletteDarkOrangeBorder2,
  },
  urgencyOnTrack: {
    borderLeftColor: tokens.colorPaletteGreenBorder2,
  },
  urgencyNeutral: {
    borderLeftColor: tokens.colorNeutralStroke2,
  },

  // ── Main row: icon + content + actions ────────────────────────────────
  mainRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "flex-start",
    gap: tokens.spacingHorizontalL,
  },

  // ── Left: type icon circle (40px) ────────────────────────────────────
  typeIconCircle: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    flexShrink: 0,
    width: "40px",
    height: "40px",
    borderRadius: "50%",
    backgroundColor: tokens.colorBrandBackground2,
    color: tokens.colorBrandForeground1,
    marginTop: "2px",
  },

  // ── Centre: content column (2 rows) ──────────────────────────────────
  contentColumn: {
    flex: "1 1 0",
    minWidth: 0,
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
  },

  // Row 1: type badge + event name
  primaryRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    flexWrap: "nowrap",
    minWidth: 0,
  },
  title: {
    display: "block",
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },

  // Row 2: priority badge + matter ref + due date
  secondaryRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
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
  timestamp: {
    color: tokens.colorNeutralForeground3,
    whiteSpace: "nowrap",
  },
  dueDateOverdue: {
    color: tokens.colorPaletteRedForeground3,
    fontWeight: tokens.fontWeightSemibold,
    whiteSpace: "nowrap",
  },

  // ── Right: action buttons (vertical stack) ────────────────────────────
  actionsColumn: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    gap: tokens.spacingVerticalXS,
    flexShrink: 0,
    paddingTop: "2px",
  },

  // Flag states
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
// Type Badge sub-component (event type label)
// ---------------------------------------------------------------------------

interface ITypeBadgeProps {
  typeName: string;
}

const TypeBadge: React.FC<ITypeBadgeProps> = ({ typeName }) => (
  <span
    role="img"
    aria-label={`Type: ${typeName}`}
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
      flexShrink: 0,
    }}
  >
    {typeName}
  </span>
);

// ---------------------------------------------------------------------------
// Record Type Badge sub-component (matter/project/etc.)
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
      backgroundColor: tokens.colorBrandBackground2,
      color: tokens.colorBrandForeground1,
      flexShrink: 0,
    }}
  >
    {typeName}
  </span>
);

// ---------------------------------------------------------------------------
// Urgency class selector
// ---------------------------------------------------------------------------

const URGENCY_CLASS_MAP: Record<UrgencyTier, keyof ReturnType<typeof useStyles>> = {
  overdue: 'urgencyOverdue',
  dueSoon: 'urgencyDueSoon',
  onTrack: 'urgencyOnTrack',
  neutral: 'urgencyNeutral',
};

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IFeedItemCardProps {
  /** The event to display */
  event: IEvent;
  /** Called when the user clicks the AI Summary button. */
  onAISummary: (eventId: string) => void;
  /** Called when the user clicks the Email action (stub). */
  onEmail?: (eventId: string) => void;
  /** Called when the user clicks the Teams action (stub). */
  onTeams?: (eventId: string) => void;
  /** Called when the user clicks the Edit action. */
  onEdit?: (eventId: string) => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const FeedItemCard: React.FC<IFeedItemCardProps> = React.memo(
  ({ event, onAISummary, onEmail, onTeams, onEdit }) => {
    const styles = useStyles();

    // Flag state from shared context (optimistic updates)
    const { isFlagged, toggleFlag, isPending, getError } = useFeedTodoSync();
    const flagged = isFlagged(event.sprk_eventid);
    const pending = isPending(event.sprk_eventid);
    const flagError = getError(event.sprk_eventid);

    const priorityLevel = derivePriorityLevel(event.sprk_priority);

    // Resolve type icon
    const TypeIconComponent = getTypeIcon(event.eventTypeName);
    const typeIconLabel = getTypeIconLabel(event.eventTypeName);

    // Due date
    const dueDateText = event.sprk_duedate
      ? `Due: ${formatRelativeTime(event.sprk_duedate)}`
      : null;
    const isDueOverdue = event.sprk_duedate
      ? new Date(event.sprk_duedate) < new Date()
      : false;

    // Urgency tier for left border
    const urgencyTier = deriveUrgencyTier(event.sprk_duedate);
    const urgencyClass = styles[URGENCY_CLASS_MAP[urgencyTier]];

    // ── Handlers ──────────────────────────────────────────────────────────

    const handleFlagToggle = React.useCallback(() => {
      void toggleFlag(event.sprk_eventid);
    }, [toggleFlag, event.sprk_eventid]);

    const handleAISummary = React.useCallback(() => {
      onAISummary(event.sprk_eventid);
    }, [onAISummary, event.sprk_eventid]);

    const handleEmail = React.useCallback(() => {
      if (onEmail) {
        onEmail(event.sprk_eventid);
      } else {
        console.info(`[FeedItemCard] Email action for event ${event.sprk_eventid} (stub)`);
      }
    }, [onEmail, event.sprk_eventid]);

    const handleTeams = React.useCallback(() => {
      if (onTeams) {
        onTeams(event.sprk_eventid);
      } else {
        console.info(`[FeedItemCard] Teams action for event ${event.sprk_eventid} (stub)`);
      }
    }, [onTeams, event.sprk_eventid]);

    const handleEdit = React.useCallback(() => {
      if (onEdit) {
        onEdit(event.sprk_eventid);
      } else {
        navigateToEntity({
          action: "openRecord",
          entityName: "sprk_event",
          entityId: event.sprk_eventid,
        });
      }
    }, [onEdit, event.sprk_eventid]);

    // ── Accessibility ─────────────────────────────────────────────────────

    const cardAriaLabel = [
      event.eventTypeName || "",
      event.sprk_eventname,
      priorityLevel ? `Priority: ${priorityLevel}.` : "",
      event.regardingRecordTypeName || "",
      event.sprk_regardingrecordname || "",
      dueDateText || "",
      flagged ? "Flagged as to-do." : "",
    ]
      .filter(Boolean)
      .join(" ");

    // Flag button tooltip / aria
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
        className={mergeClasses(styles.card, urgencyClass)}
        role="listitem"
        tabIndex={0}
        aria-label={cardAriaLabel}
      >
        <div className={styles.mainRow}>
          {/* Type icon in 40px circle */}
          <div
            className={styles.typeIconCircle}
            aria-label={typeIconLabel}
            role="img"
          >
            <TypeIconComponent fontSize={20} />
          </div>

          {/* Content: 2 rows */}
          <div className={styles.contentColumn}>
            {/* Row 1: Type badge + Event Name */}
            <div className={styles.primaryRow}>
              {event.eventTypeName && (
                <TypeBadge typeName={event.eventTypeName} />
              )}
              <Text
                as="span"
                size={400}
                className={styles.title}
              >
                {event.sprk_eventname}
              </Text>
            </div>

            {/* Row 2: Priority + record type + matter ref + due date */}
            <div className={styles.secondaryRow}>
              {priorityLevel && <PriorityBadge level={priorityLevel} />}
              {event.regardingRecordTypeName && (
                <RecordTypeBadge typeName={event.regardingRecordTypeName} />
              )}
              {event.sprk_regardingrecordname && (
                <Text size={200} className={styles.metaText}>
                  {event.sprk_regardingrecordname}
                </Text>
              )}
              {dueDateText && (
                <>
                  <Text size={200} className={styles.metaDivider} aria-hidden="true">
                    ·
                  </Text>
                  <Text
                    size={200}
                    className={isDueOverdue ? styles.dueDateOverdue : styles.timestamp}
                  >
                    {dueDateText}
                  </Text>
                </>
              )}
            </div>
          </div>

          {/* Action buttons — vertical stack, medium size */}
          <div className={styles.actionsColumn}>
            {/* Flag toggle */}
            <Tooltip content={flagTooltip} relationship="label">
              <div className={styles.flagButtonWrapper}>
                <Button
                  appearance="subtle"
                  size="medium"
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

            {/* Email (stub) */}
            <Tooltip content="Send email" relationship="label">
              <Button
                appearance="subtle"
                size="medium"
                icon={<MailRegular aria-hidden="true" />}
                aria-label="Send email about this event"
                onClick={handleEmail}
              />
            </Tooltip>

            {/* Teams (stub) */}
            <Tooltip content="Start Teams chat" relationship="label">
              <Button
                appearance="subtle"
                size="medium"
                icon={<ChatRegular aria-hidden="true" />}
                aria-label="Start Teams chat about this event"
                onClick={handleTeams}
              />
            </Tooltip>

            {/* Edit */}
            <Tooltip content="Edit event" relationship="label">
              <Button
                appearance="subtle"
                size="medium"
                icon={<EditRegular aria-hidden="true" />}
                aria-label="Edit this event"
                onClick={handleEdit}
              />
            </Tooltip>

            {/* AI Summary */}
            <Tooltip content="Generate AI summary" relationship="label">
              <Button
                appearance="subtle"
                size="medium"
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
