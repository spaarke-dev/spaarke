/**
 * KanbanCard — A domain-specific card for the Kanban board (Smart To Do).
 *
 * Rendered inside the generic KanbanBoard via the renderCard prop.
 *
 * Layout (flexbox row):
 *   [Checkbox] [Event Name (truncated)] ... [Pin toggle]
 *              [Due label] [separator] [Assigned to] ... [Score badge]
 *
 * Features:
 *   - Left accent border (3px) coloured by the parent column via prop
 *   - Checkbox toggles completion status (parent handles Dataverse write)
 *   - Pin toggle locks item in its Kanban column
 *   - Card body click opens detail pane (checkbox/pin clicks do not bubble)
 *   - Completed state: opacity 0.6, title strikethrough
 *   - Due badge colour-coded by urgency tier (overdue/3d/7d/10d)
 *   - To Do Score badge shows composite ranking score
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
  Checkbox,
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

  checkboxWrapper: {
    flexShrink: 0,
    display: "flex",
    alignItems: "flex-start",
    paddingTop: "2px",
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

  secondaryRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    flexWrap: "wrap",
  },

  actionsColumn: {
    display: "flex",
    flexDirection: "column",
    alignItems: "flex-end",
    gap: tokens.spacingVerticalXXS,
    flexShrink: 0,
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IKanbanCardProps {
  event: IEvent;
  /** Called when checkbox is toggled. Parent handles optimistic UI + Dataverse write. */
  onToggleComplete?: (eventId: string, completed: boolean) => void;
  /** Called when pin button is clicked. */
  onPinToggle?: (eventId: string) => void;
  /** Called when card body is clicked (not checkbox/pin). Opens detail pane. */
  onClick?: (eventId: string) => void;
  /** Left border accent colour from parent column. */
  accentColor?: string;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const KanbanCard: React.FC<IKanbanCardProps> = React.memo(
  ({ event, onToggleComplete, onPinToggle, onClick, accentColor }) => {
    const styles = useStyles();

    // Derived display values
    const dueDate = parseDueDate(event.sprk_duedate);
    const dueLabel = computeDueLabel(dueDate);
    const { todoScore } = computeTodoScore(event);
    const isCompleted = event.sprk_todostatus === 100000001;
    const isPinned = event.sprk_todopinned === true;

    // -----------------------------------------------------------------------
    // Handlers
    // -----------------------------------------------------------------------

    const handleCheckboxChange = React.useCallback(
      (ev: React.ChangeEvent<HTMLInputElement>, data: { checked: boolean | "mixed" }) => {
        ev.stopPropagation();
        if (onToggleComplete) {
          onToggleComplete(event.sprk_eventid, data.checked === true);
        }
      },
      [onToggleComplete, event.sprk_eventid]
    );

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
      dueLabel.label ? `Due: ${dueLabel.label}.` : "",
      `Score: ${Math.round(todoScore)}.`,
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
        {/* Checkbox */}
        <div className={styles.checkboxWrapper}>
          <Checkbox
            checked={isCompleted}
            onChange={handleCheckboxChange}
            aria-label={`Mark "${event.sprk_eventname}" as ${isCompleted ? "open" : "complete"}`}
          />
        </div>

        {/* Content: title row + secondary row */}
        <div className={styles.contentColumn}>
          {/* Row 1: Title */}
          <Text as="span" size={300} weight="semibold" className={titleClassName}>
            {event.sprk_eventname}
          </Text>

          {/* Row 2: Due badge + separator + assigned-to name */}
          <div className={styles.secondaryRow}>
            {dueLabel.urgency !== "none" && (
              <InlineBadge
                style={DUE_BADGE_STYLE[dueLabel.urgency]}
                ariaLabel={`Due: ${dueLabel.label}`}
              >
                {dueLabel.label}
              </InlineBadge>
            )}
            {dueLabel.urgency !== "none" && event.assignedToName && (
              <Text as="span" size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                {"\u00B7"}
              </Text>
            )}
            {event.assignedToName && (
              <Text as="span" size={200} style={{ color: tokens.colorNeutralForeground2 }}>
                {event.assignedToName}
              </Text>
            )}
          </div>
        </div>

        {/* Actions column: pin button + score badge */}
        <div className={styles.actionsColumn}>
          {/* Pin toggle */}
          <Button
            appearance="subtle"
            size="small"
            icon={isPinned ? <PinFilled /> : <PinRegular />}
            onClick={handlePinClick}
            aria-label={isPinned ? `Unpin "${event.sprk_eventname}"` : `Pin "${event.sprk_eventname}"`}
            title={isPinned ? "Unpin from column" : "Pin to column"}
          />

          {/* To Do Score badge */}
          <InlineBadge
            style={{
              backgroundColor: tokens.colorBrandBackground2,
              color: tokens.colorBrandForeground1,
            }}
            ariaLabel={`To Do Score: ${Math.round(todoScore)}`}
          >
            {Math.round(todoScore)}
          </InlineBadge>
        </div>
      </div>
    );
  }
);

KanbanCard.displayName = "KanbanCard";
