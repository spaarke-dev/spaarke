/**
 * KanbanCard — A domain-specific card for the Kanban board (Smart To Do).
 *
 * Rendered inside the generic KanbanBoard via the renderCard prop.
 *
 * Layout (flexbox row):
 *   [Score circle 40px]  [Event Name (truncated)]  [Pin toggle]
 *                         [Due: Feb 4 · Overdue]
 *                         [Assigned: Jane Smith]
 *
 * Features:
 *   - Left accent border (3px) coloured by the parent column via prop
 *   - Score displayed as a prominent 40px circle (brand colour)
 *   - Pin toggle locks item in its Kanban column
 *   - Card body click opens detail pane (pin clicks do not bubble)
 *   - Completed state: opacity 0.6, title strikethrough
 *   - Due date shows actual date + urgency badge
 *   - Field labels: "Due:", "Assigned:" for clarity
 *
 * Design constraints:
 *   - ALL colours from Fluent UI v9 semantic tokens (ADR-021)
 *   - makeStyles (Griffel) only for custom styles
 *   - Dark mode + high-contrast supported automatically via token system
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Button,
} from "@fluentui/react-components";
import { PinRegular, PinFilled } from "@fluentui/react-icons";
import { IEvent } from "../../types/entities";
import { computeDueLabel, parseDueDate, DueUrgency } from "../../utils/dueLabelUtils";
import { computeTodoScore } from "../../utils/todoScoreUtils";

// ---------------------------------------------------------------------------
// InlineBadge (shared pattern from TodoItem.tsx)
// ---------------------------------------------------------------------------

interface IBadgeProps {
  style: React.CSSProperties;
  ariaLabel: string;
  children: React.ReactNode;
}

const InlineBadge: React.FC<IBadgeProps> = ({ style, ariaLabel, children }) => (
  <span
    role="img"
    aria-label={ariaLabel}
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
      ...style,
    }}
  >
    {children}
  </span>
);

// ---------------------------------------------------------------------------
// Due badge style map (copied from TodoItem.tsx for visual consistency)
// ---------------------------------------------------------------------------

const DUE_BADGE_STYLE: Record<Exclude<DueUrgency, 'none'>, React.CSSProperties> = {
  overdue: {
    backgroundColor: tokens.colorPaletteRedBackground3,
    color: tokens.colorNeutralForegroundOnBrand,
  },
  '3d': {
    backgroundColor: tokens.colorPaletteDarkOrangeBackground3,
    color: tokens.colorNeutralForegroundOnBrand,
  },
  '7d': {
    backgroundColor: tokens.colorPaletteYellowBackground3,
    color: tokens.colorNeutralForeground1,
  },
  '10d': {
    backgroundColor: tokens.colorNeutralBackground3,
    color: tokens.colorNeutralForeground2,
  },
};

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  card: {
    display: "flex",
    flexDirection: "row",
    alignItems: "flex-start",
    gap: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderRadius: tokens.borderRadiusMedium,
    boxShadow: tokens.shadow2,
    backgroundColor: tokens.colorNeutralBackground1,
    cursor: "pointer",
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

  cardCompleted: {
    opacity: "0.6",
  },

  scoreCircle: {
    flexShrink: 0,
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    width: "40px",
    height: "40px",
    borderRadius: "50%",
    backgroundColor: tokens.colorBrandBackground2,
    color: tokens.colorBrandForeground1,
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    lineHeight: "1",
  },

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
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },

  titleCompleted: {
    textDecorationLine: "line-through",
    textDecorationColor: tokens.colorNeutralForeground3,
    color: tokens.colorNeutralForeground3,
  },

  metadataRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    flexWrap: "wrap",
  },

  fieldLabel: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },

  fieldValue: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
  },

  actionsColumn: {
    display: "flex",
    flexDirection: "column",
    alignItems: "flex-end",
    flexShrink: 0,
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Format a Date to a short display string like "Feb 4" or "Feb 4, 2027". */
function formatDueDate(date: Date): string {
  const now = new Date();
  const sameYear = date.getFullYear() === now.getFullYear();
  return date.toLocaleDateString("en-US", {
    month: "short",
    day: "numeric",
    ...(sameYear ? {} : { year: "numeric" }),
  });
}

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IKanbanCardProps {
  event: IEvent;
  /** Called when pin button is clicked. */
  onPinToggle?: (eventId: string) => void;
  /** Called when card body is clicked (not pin). Opens detail pane. */
  onClick?: (eventId: string) => void;
  /** Left border accent colour from parent column. */
  accentColor?: string;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const KanbanCard: React.FC<IKanbanCardProps> = React.memo(
  ({ event, onPinToggle, onClick, accentColor }) => {
    const styles = useStyles();

    // Derived display values
    const dueDate = parseDueDate(event.sprk_duedate);
    const dueLabel = computeDueLabel(dueDate);
    const { todoScore } = computeTodoScore(event);
    const roundedScore = Math.round(todoScore);
    const isCompleted = event.sprk_todostatus === 100000001;
    const isPinned = event.sprk_todopinned === true;
    const dueDateFormatted = dueDate ? formatDueDate(dueDate) : null;

    // -----------------------------------------------------------------------
    // Handlers
    // -----------------------------------------------------------------------

    const handlePinClick = React.useCallback(
      (ev: React.MouseEvent<HTMLButtonElement>) => {
        ev.stopPropagation();
        if (onPinToggle) {
          onPinToggle(event.sprk_eventid);
        }
      },
      [onPinToggle, event.sprk_eventid]
    );

    const handleCardClick = React.useCallback(() => {
      if (onClick) {
        onClick(event.sprk_eventid);
      }
    }, [onClick, event.sprk_eventid]);

    const handleCardKeyDown = React.useCallback(
      (ev: React.KeyboardEvent<HTMLDivElement>) => {
        if (ev.key === "Enter" || ev.key === " ") {
          ev.preventDefault();
          handleCardClick();
        }
      },
      [handleCardClick]
    );

    // -----------------------------------------------------------------------
    // Accessible label
    // -----------------------------------------------------------------------

    const cardAriaLabel = [
      event.sprk_eventname,
      isCompleted ? "Completed." : "Open.",
      isPinned ? "Pinned." : "",
      dueDateFormatted ? `Due: ${dueDateFormatted}.` : "",
      dueLabel.label ? `${dueLabel.label}.` : "",
      `To Do Score: ${roundedScore}.`,
    ]
      .filter(Boolean)
      .join(" ");

    // -----------------------------------------------------------------------
    // Class composition
    // -----------------------------------------------------------------------

    const cardClassName = [styles.card, isCompleted ? styles.cardCompleted : ""]
      .filter(Boolean)
      .join(" ");

    const titleClassName = [styles.title, isCompleted ? styles.titleCompleted : ""]
      .filter(Boolean)
      .join(" ");

    // Left accent border via inline style (colour is a runtime prop)
    const accentStyle: React.CSSProperties | undefined = accentColor
      ? {
          borderLeftWidth: "3px",
          borderLeftStyle: "solid",
          borderLeftColor: accentColor,
        }
      : undefined;

    // -----------------------------------------------------------------------
    // Render
    // -----------------------------------------------------------------------

    return (
      <div
        className={cardClassName}
        style={accentStyle}
        role="listitem"
        tabIndex={0}
        aria-label={cardAriaLabel}
        onClick={handleCardClick}
        onKeyDown={handleCardKeyDown}
      >
        {/* Score circle — prominent left visual anchor */}
        <div
          className={styles.scoreCircle}
          title={`To Do Score: ${roundedScore}`}
          aria-hidden="true"
        >
          {roundedScore}
        </div>

        {/* Content: title + metadata rows */}
        <div className={styles.contentColumn}>
          {/* Row 1: Title */}
          <Text as="span" size={300} weight="semibold" className={titleClassName}>
            {event.sprk_eventname}
          </Text>

          {/* Row 2: Due date + urgency badge */}
          {(dueDateFormatted || dueLabel.urgency !== "none") && (
            <div className={styles.metadataRow}>
              {dueDateFormatted && (
                <>
                  <span className={styles.fieldLabel}>Due:</span>
                  <span className={styles.fieldValue}>{dueDateFormatted}</span>
                </>
              )}
              {dueLabel.urgency !== "none" && (
                <>
                  {dueDateFormatted && (
                    <Text as="span" size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                      {"\u00B7"}
                    </Text>
                  )}
                  <InlineBadge
                    style={DUE_BADGE_STYLE[dueLabel.urgency]}
                    ariaLabel={dueLabel.label}
                  >
                    {dueLabel.label}
                  </InlineBadge>
                </>
              )}
            </div>
          )}

          {/* Row 3: Assigned to */}
          {event.assignedToName && (
            <div className={styles.metadataRow}>
              <span className={styles.fieldLabel}>Assigned:</span>
              <span className={styles.fieldValue}>{event.assignedToName}</span>
            </div>
          )}
        </div>

        {/* Actions column: pin button */}
        <div className={styles.actionsColumn}>
          <Button
            appearance="subtle"
            size="small"
            icon={isPinned ? <PinFilled /> : <PinRegular />}
            onClick={handlePinClick}
            aria-label={isPinned ? `Unpin "${event.sprk_eventname}"` : `Pin "${event.sprk_eventname}"`}
            title={isPinned ? "Unpin from column" : "Pin to column"}
          />
        </div>
      </div>
    );
  }
);

KanbanCard.displayName = "KanbanCard";
