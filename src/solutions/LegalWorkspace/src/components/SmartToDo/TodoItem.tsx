/**
 * TodoItem — A single row in the Smart To Do list (Block 4).
 *
 * Layout (flexbox row, left-to-right):
 *   [drag handle] [checkbox] [source icon] [title + context] [badges] [due label]
 *
 * Sub-elements:
 *   - ReOrderDotsVerticalRegular icon: visual drag handle placeholder (non-functional, R1 scope)
 *   - Fluent v9 Checkbox: visual only for now (check/dismiss handled in task 015)
 *   - Source icon: BotRegular (System), FlagRegular (User/Flagged), EditRegular (Manual)
 *   - Title text (primary, semibold, single-line ellipsis)
 *   - Context/description text (secondary, 2-line clamp)
 *   - Priority badge: colour-coded by Critical/High/Medium/Low
 *   - Effort badge: colour-coded by High/Med/Low
 *   - Due label badge: colour-coded by urgency (overdue/3d/7d/10d/none)
 *
 * Design constraints:
 *   - ALL colours from Fluent UI v9 semantic tokens — zero hardcoded hex/rgb
 *   - makeStyles (Griffel) only for custom styles
 *   - Dark mode + high-contrast supported automatically via token system
 *   - All icon-only elements have aria-label or are aria-hidden
 *
 * Note on drag-and-drop:
 *   The drag handle icon is rendered visually but carries no drag event handlers.
 *   Full DnD reordering is deferred to a post-R1 iteration.
 *
 * Task 015 additions:
 *   - Checkbox handler: toggles sprk_todostatus Open ↔ Completed via onToggleComplete
 *   - Completed visual state: strikethrough title + opacity 0.6 on entire row
 *   - Dismiss button: DismissRegular icon button, calls onDismiss(eventId)
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Checkbox,
  Button,
} from "@fluentui/react-components";
import {
  ReOrderDotsVerticalRegular,
  BotRegular,
  SparkleRegular,
  EditRegular,
  DismissRegular,
} from "@fluentui/react-icons";
import { IEvent } from "../../types/entities";
import { PriorityLevel, EffortLevel, TodoSource } from "../../types/enums";
import { computeDueLabel, parseDueDate, DueUrgency } from "../../utils/dueLabelUtils";

// ---------------------------------------------------------------------------
// Badge style maps (using tokens for all colours — zero hardcoded hex)
// ---------------------------------------------------------------------------

/** Priority badge colours matching FeedItemCard.tsx for visual consistency */
const PRIORITY_BADGE_STYLE: Record<PriorityLevel, React.CSSProperties> = {
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

const EFFORT_BADGE_STYLE: Record<EffortLevel, React.CSSProperties> = {
  High: {
    backgroundColor: tokens.colorPaletteRedBackground3,
    color: tokens.colorNeutralForegroundOnBrand,
  },
  Med: {
    backgroundColor: tokens.colorPaletteYellowBackground3,
    color: tokens.colorNeutralForeground1,
  },
  Low: {
    backgroundColor: tokens.colorPaletteGreenBackground3,
    color: tokens.colorNeutralForeground1,
  },
};

const DUE_BADGE_STYLE: Record<Exclude<DueUrgency, 'none'>, React.CSSProperties> = {
  overdue: {
    backgroundColor: tokens.colorPaletteRedBackground3,
    color: tokens.colorNeutralForegroundOnBrand,
  },
  '3d': {
    // Orange — closest semantic token that reads "warning-red adjacent"
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
// Derivation helpers
// ---------------------------------------------------------------------------

/**
 * Map sprk_priority option-set integer to a PriorityLevel label.
 *   0 = Low, 1 = Normal, 2 = High, 3 = Urgent
 */
function derivePriorityLevel(priority: number | undefined): PriorityLevel | null {
  switch (priority) {
    case 0: return "Low";
    case 1: return "Normal";
    case 2: return "High";
    case 3: return "Urgent";
    default: return null;
  }
}

/**
 * Map sprk_effortscore (0-100) to an EffortLevel label.
 *   ≥70 = High, ≥35 = Med, <35 = Low
 */
function deriveEffortLevel(effortScore: number | undefined): EffortLevel | null {
  if (effortScore === undefined || effortScore === null) return null;
  if (effortScore >= 70) return "High";
  if (effortScore >= 35) return "Med";
  return "Low";
}

/**
 * Map sprk_todosource choice value to the source category for icon selection.
 * Dataverse choice: 100000000=System, 100000001=User, 100000002=AI.
 * Defaults to 'User' when unknown.
 */
function deriveSourceType(source: number | undefined): TodoSource {
  switch (source) {
    case 100000000: return "System";
    case 100000001: return "User";
    case 100000002: return "AI";
    default:        return "User";
  }
}

// ---------------------------------------------------------------------------
// Shared badge element (inline styles only — Griffel cannot access runtime
// token values in switch expressions, so we use React.CSSProperties directly)
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
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  row: {
    display: "flex",
    flexDirection: "row",
    alignItems: "flex-start",
    gap: tokens.spacingHorizontalXS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke3,
    backgroundColor: tokens.colorNeutralBackground1,
    // Smooth hover transition
    transitionProperty: "background-color, opacity",
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

  // Applied to the row when the item is completed
  rowCompleted: {
    opacity: "0.6",
  },

  // Drag handle — visual only, cursor is grab to hint at future DnD
  dragHandle: {
    display: "flex",
    alignItems: "center",
    paddingTop: "3px",
    flexShrink: 0,
    color: tokens.colorNeutralForeground4,
    cursor: "grab",
    // Make it slightly faded by default, more visible on row hover
    opacity: "0.4",
  },

  // Checkbox wrapper — aligns top with first text line
  checkboxWrapper: {
    display: "flex",
    alignItems: "flex-start",
    paddingTop: "2px",
    flexShrink: 0,
  },

  // Source indicator icon
  sourceIcon: {
    display: "flex",
    alignItems: "center",
    paddingTop: "3px",
    flexShrink: 0,
    color: tokens.colorNeutralForeground3,
    fontSize: "14px",
  },

  // Content column: title + context text
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
  // Applied to title text when the item is completed
  titleCompleted: {
    textDecorationLine: "line-through",
    textDecorationColor: tokens.colorNeutralForeground3,
    color: tokens.colorNeutralForeground3,
  },
  context: {
    display: "-webkit-box",
    WebkitLineClamp: "2",
    WebkitBoxOrient: "vertical",
    overflow: "hidden",
    color: tokens.colorNeutralForeground2,
  },

  // Badges column: priority + effort + due — stacked vertically, right-aligned
  badgesColumn: {
    display: "flex",
    flexDirection: "column",
    alignItems: "flex-end",
    gap: tokens.spacingVerticalXXS,
    flexShrink: 0,
    paddingTop: "2px",
  },

  badgeRow: {
    display: "flex",
    flexDirection: "row",
    gap: tokens.spacingHorizontalXXS,
    alignItems: "center",
  },

  // Dismiss button wrapper — aligns to top of row, shown on hover via group
  actionsColumn: {
    display: "flex",
    alignItems: "flex-start",
    paddingTop: "2px",
    flexShrink: 0,
  },
});

// ---------------------------------------------------------------------------
// Source icon selector
// ---------------------------------------------------------------------------

interface ISourceIconProps {
  source: TodoSource;
}

const SourceIcon: React.FC<ISourceIconProps> = ({ source }) => {
  switch (source) {
    case "System":
      return <BotRegular fontSize={14} aria-hidden="true" />;
    case "AI":
      return <SparkleRegular fontSize={14} aria-hidden="true" />;
    case "User":
    default:
      return <EditRegular fontSize={14} aria-hidden="true" />;
  }
};

function getSourceLabel(source: TodoSource): string {
  switch (source) {
    case "System":  return "System-generated";
    case "AI":      return "AI-generated";
    case "User":
    default:        return "Manually created";
  }
}

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ITodoItemProps {
  /** The to-do event to display */
  event: IEvent;
  /**
   * Called when the user toggles the checkbox.
   * Parent handles optimistic UI + Dataverse update.
   * When undefined the checkbox renders but does nothing.
   */
  onToggleComplete?: (eventId: string, completed: boolean) => void;
  /**
   * Called when the user clicks the dismiss button.
   * Parent handles the Dataverse status update (→ Dismissed) + list move.
   */
  onDismiss?: (eventId: string) => void;
  /**
   * True while a dismiss operation is in-flight for this item.
   * Disables the dismiss button to prevent double-clicks.
   */
  isDismissing?: boolean;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const TodoItem: React.FC<ITodoItemProps> = React.memo(
  ({ event, onToggleComplete, onDismiss, isDismissing = false }) => {
    const styles = useStyles();

    // Derived display values
    const priorityLevel = derivePriorityLevel(event.sprk_priority);
    const effortLevel = deriveEffortLevel(event.sprk_effortscore);
    const sourceType = deriveSourceType(event.sprk_todosource);
    const dueDate = parseDueDate(event.sprk_duedate);
    const dueLabel = computeDueLabel(dueDate);

    const sourceLabel = getSourceLabel(sourceType);
    const isCompleted = event.sprk_todostatus === 100000001; // Completed

    // Checkbox change handler — delegates to parent if provided
    const handleCheckboxChange = React.useCallback(
      (_ev: React.ChangeEvent<HTMLInputElement>, data: { checked: boolean | "mixed" }) => {
        if (onToggleComplete) {
          onToggleComplete(event.sprk_eventid, data.checked === true);
        }
      },
      [onToggleComplete, event.sprk_eventid]
    );

    // Dismiss button handler
    const handleDismiss = React.useCallback(() => {
      if (onDismiss) {
        onDismiss(event.sprk_eventid);
      }
    }, [onDismiss, event.sprk_eventid]);

    // Accessible row label
    const rowAriaLabel = [
      isCompleted ? "Completed." : "Open.",
      event.sprk_eventname,
      sourceLabel,
      priorityLevel ? `Priority: ${priorityLevel}.` : "",
      effortLevel ? `Effort: ${effortLevel}.` : "",
      dueLabel.label ? `Due: ${dueLabel.label}.` : "",
    ]
      .filter(Boolean)
      .join(" ");

    // Compose row class: base + optional completed class
    const rowClassName = [styles.row, isCompleted ? styles.rowCompleted : ""]
      .filter(Boolean)
      .join(" ");

    return (
      <div
        className={rowClassName}
        role="listitem"
        tabIndex={0}
        aria-label={rowAriaLabel}
      >
        {/* Drag handle — visual placeholder (non-functional in R1) */}
        <div
          className={styles.dragHandle}
          aria-hidden="true"
          title="Drag to reorder (coming soon)"
        >
          <ReOrderDotsVerticalRegular fontSize={16} />
        </div>

        {/* Checkbox */}
        <div className={styles.checkboxWrapper}>
          <Checkbox
            checked={isCompleted}
            onChange={handleCheckboxChange}
            aria-label={`Mark "${event.sprk_eventname}" as ${isCompleted ? "open" : "complete"}`}
          />
        </div>

        {/* Source indicator icon */}
        <div
          className={styles.sourceIcon}
          role="img"
          aria-label={sourceLabel}
          title={sourceLabel}
        >
          <SourceIcon source={sourceType} />
        </div>

        {/* Title + context */}
        <div className={styles.contentColumn}>
          <Text
            as="span"
            size={300}
            className={[styles.title, isCompleted ? styles.titleCompleted : ""]
              .filter(Boolean)
              .join(" ")}
          >
            {event.sprk_eventname}
          </Text>
          {(event.sprk_priorityreason || event.sprk_effortreason) && (
            <Text as="span" size={200} className={styles.context}>
              {event.sprk_priorityreason ?? event.sprk_effortreason}
            </Text>
          )}
        </div>

        {/* Badges column: priority row + effort row + due row */}
        <div className={styles.badgesColumn}>
          {/* Priority + effort on the same row */}
          <div className={styles.badgeRow}>
            {priorityLevel && (
              <InlineBadge
                style={PRIORITY_BADGE_STYLE[priorityLevel]}
                ariaLabel={`Priority: ${priorityLevel}`}
              >
                {priorityLevel}
              </InlineBadge>
            )}
            {effortLevel && (
              <InlineBadge
                style={EFFORT_BADGE_STYLE[effortLevel]}
                ariaLabel={`Effort: ${effortLevel}`}
              >
                {effortLevel}
              </InlineBadge>
            )}
          </div>

          {/* Due date label on its own row */}
          {dueLabel.urgency !== "none" && (
            <InlineBadge
              style={DUE_BADGE_STYLE[dueLabel.urgency]}
              ariaLabel={`Due: ${dueLabel.label}`}
            >
              {dueLabel.label}
            </InlineBadge>
          )}
        </div>

        {/* Dismiss button — only shown when onDismiss handler is provided */}
        {onDismiss && (
          <div className={styles.actionsColumn}>
            <Button
              appearance="subtle"
              size="small"
              icon={<DismissRegular />}
              onClick={handleDismiss}
              disabled={isDismissing}
              aria-label={`Dismiss "${event.sprk_eventname}"`}
              title="Dismiss from to-do list"
            />
          </div>
        )}
      </div>
    );
  }
);

TodoItem.displayName = "TodoItem";
