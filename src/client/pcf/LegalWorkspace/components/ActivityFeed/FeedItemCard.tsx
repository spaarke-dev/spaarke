/**
 * FeedItemCard — interactive card component for a single Updates Feed event.
 *
 * Layout (flexbox row):
 *   [unread dot] [type icon] [title + summary] [priority badge + timestamp]
 *   [───────────────── bottom action bar ─────────────────────────────────]
 *                                       [Flag toggle]  [AI Summary button]
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
 * Maps a PriorityLevel to the Fluent v9 semantic background token name for the
 * badge colour.  We use CSS custom-property references directly so that
 * makeStyles can consume them as static strings (Griffel requirement).
 */
const PRIORITY_BADGE_STYLES: Record<PriorityLevel, React.CSSProperties> = {
  Critical: {
    backgroundColor: tokens.colorPaletteRedBackground3,
    color: tokens.colorNeutralForegroundOnBrand,
  },
  High: {
    backgroundColor: tokens.colorPaletteYellowBackground3,
    color: tokens.colorNeutralForeground1,
  },
  Medium: {
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
  // Dataverse option-set convention used in this project:
  //   1 = Critical, 2 = High, 3 = Medium, 4 = Low
  switch (priority) {
    case 1: return "Critical";
    case 2: return "High";
    case 3: return "Medium";
    case 4: return "Low";
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
    // Smooth background transition for hover
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

  // ── Main row ────────────────────────────────────────────────────────────
  mainRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "flex-start",
    gap: tokens.spacingHorizontalS,
  },

  // ── Left column: unread dot ──────────────────────────────────────────────
  unreadDotWrapper: {
    display: "flex",
    alignItems: "center",
    paddingTop: "6px", // Align with first line of title text
    flexShrink: 0,
    width: "8px",
  },
  unreadDot: {
    width: "8px",
    height: "8px",
    borderRadius: "50%",
    backgroundColor: tokens.colorBrandBackground,
    flexShrink: 0,
  },
  unreadDotHidden: {
    // Invisible placeholder so layout stays stable whether dot is shown or not
    visibility: "hidden",
  },

  // ── Type icon ────────────────────────────────────────────────────────────
  typeIconWrapper: {
    display: "flex",
    alignItems: "center",
    paddingTop: "3px", // Align icon optically with title text
    flexShrink: 0,
    color: tokens.colorNeutralForeground3,
    fontSize: "16px",
  },

  // ── Centre: title + description ──────────────────────────────────────────
  contentColumn: {
    flex: "1 1 0",
    minWidth: 0, // Required to allow text truncation in flex children
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
    // Max 2 lines with ellipsis — standard cross-browser technique
    // Griffel passes this through verbatim
    WebkitLineClamp: "2",
    WebkitBoxOrient: "vertical",
    overflow: "hidden",
    color: tokens.colorNeutralForeground2,
  },

  // ── Right column: priority badge + timestamp ─────────────────────────────
  metaColumn: {
    display: "flex",
    flexDirection: "column",
    alignItems: "flex-end",
    gap: tokens.spacingVerticalXXS,
    flexShrink: 0,
    paddingTop: "2px",
  },
  timestamp: {
    color: tokens.colorNeutralForeground3,
    whiteSpace: "nowrap",
  },

  // ── Bottom action bar ────────────────────────────────────────────────────
  actionBar: {
    display: "flex",
    flexDirection: "row",
    justifyContent: "flex-end",
    gap: tokens.spacingHorizontalXS,
    // Shift the action bar slightly inward from the right edge to align with
    // the meta column above
    paddingRight: "0px",
  },

  // Flag active state: icon button colour when toggled on
  flagButtonActive: {
    color: tokens.colorBrandForeground1,
  },

  // Flag button wrapper — relative positioning for spinner overlay
  flagButtonWrapper: {
    position: "relative",
    display: "inline-flex",
    alignItems: "center",
    justifyContent: "center",
  },

  // Spinner overlay on top of the flag button while write is in-flight
  flagPendingSpinner: {
    position: "absolute",
    // Centre over the button icon (16px icon, button is ~28px with padding)
    pointerEvents: "none",
  },

  // Error indication on the flag button when the last write failed
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
    // The unread dot is shown when the item is NOT yet flagged as to-do
    const isUnread = !flagged;
    const relativeTime = formatRelativeTime(event.modifiedon);

    // Resolve type icon component
    const TypeIconComponent = getTypeIcon(event.sprk_type);
    const typeIconLabel = getTypeIconLabel(event.sprk_type);

    // Flag toggle handler — delegates to context (optimistic + debounced Dataverse write)
    const handleFlagToggle = React.useCallback(() => {
      // Fire and forget — errors surface via getError / flagError state
      void toggleFlag(event.sprk_eventid);
    }, [toggleFlag, event.sprk_eventid]);

    const handleAISummary = React.useCallback(() => {
      onAISummary(event.sprk_eventid);
    }, [onAISummary, event.sprk_eventid]);

    // Build aria label for the card
    const cardAriaLabel = [
      isUnread ? "Unread." : "",
      event.sprk_subject,
      typeIconLabel,
      priorityLevel ? `Priority: ${priorityLevel}.` : "",
      relativeTime,
      flagged ? "Flagged as to-do." : "",
    ]
      .filter(Boolean)
      .join(" ");

    // Flag button tooltip content — error takes priority, then normal toggle label
    const flagTooltip = flagError
      ? `Error: ${flagError} — click to retry`
      : flagged
      ? "Remove flag"
      : "Flag as to-do";

    // Flag button aria label mirrors tooltip
    const flagAriaLabel = flagError
      ? `Flag error: ${flagError}`
      : flagged
      ? "Remove flag"
      : "Flag as to-do";

    // Determine flag button class: error > active > default
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
        {/* ── Main row ──────────────────────────────────────────────── */}
        <div className={styles.mainRow}>
          {/* Unread dot */}
          <div className={styles.unreadDotWrapper}>
            <div
              className={mergeClasses(
                styles.unreadDot,
                !isUnread && styles.unreadDotHidden
              )}
              aria-hidden="true"
            />
          </div>

          {/* Type icon */}
          <div
            className={styles.typeIconWrapper}
            aria-label={typeIconLabel}
            role="img"
          >
            <TypeIconComponent fontSize={16} />
          </div>

          {/* Title + description */}
          <div className={styles.contentColumn}>
            <Text
              as="span"
              size={300}
              className={styles.title}
            >
              {event.sprk_subject}
            </Text>
            {event.sprk_priorityreason && (
              <Text
                as="span"
                size={200}
                className={styles.description}
              >
                {event.sprk_priorityreason}
              </Text>
            )}
          </div>

          {/* Priority badge + timestamp */}
          <div className={styles.metaColumn}>
            {priorityLevel && <PriorityBadge level={priorityLevel} />}
            <Text size={100} className={styles.timestamp}>
              {relativeTime}
            </Text>
          </div>
        </div>

        {/* ── Action bar ────────────────────────────────────────────── */}
        <div className={styles.actionBar}>
          {/* Flag toggle — with optimistic state from FeedTodoSyncContext */}
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
              {/* In-flight spinner overlay — rendered only while Dataverse write is pending */}
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
    );
  }
);

FeedItemCard.displayName = "FeedItemCard";
