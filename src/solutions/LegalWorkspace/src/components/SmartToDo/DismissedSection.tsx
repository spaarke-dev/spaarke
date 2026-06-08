/**
 * DismissedSection — Collapsible section for dismissed to-do items (Task 015).
 *
 * Layout:
 *   [ChevronDown/Up icon] [Dismissed label] [count Badge]
 *   When expanded:
 *     List of dismissed TodoItems with an ArrowUndoRegular restore button on each row.
 *
 * Behaviour:
 *   - Collapsed by default (isExpanded starts false)
 *   - Clicking the header row toggles expanded / collapsed
 *   - Each dismissed item has a restore button that calls onRestore(todoId)
 *   - Does not render the section at all when there are zero dismissed items
 *
 * Design constraints:
 *   - ALL colours from Fluent UI v9 semantic tokens — zero hardcoded hex/rgb
 *   - makeStyles (Griffel) only for custom styles
 *   - Dark mode + high-contrast supported automatically via token system
 *   - Restore button uses ArrowUndoRegular icon
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Badge,
  Button,
} from "@fluentui/react-components";
import {
  ChevronDownRegular,
  ChevronUpRegular,
  ArrowUndoRegular,
  DismissRegular,
} from "@fluentui/react-icons";
import { ITodo } from "../../types/entities";
import { PriorityLevel, EffortLevel } from "../../types/enums";
import { computeDueLabel, parseDueDate, DueUrgency } from "../../utils/dueLabelUtils";

// ---------------------------------------------------------------------------
// Badge style maps — mirrors TodoItem.tsx for visual consistency
// ---------------------------------------------------------------------------

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

const DUE_BADGE_STYLE: Record<Exclude<DueUrgency, "none">, React.CSSProperties> = {
  overdue: {
    backgroundColor: tokens.colorPaletteRedBackground3,
    color: tokens.colorNeutralForegroundOnBrand,
  },
  "3d": {
    backgroundColor: tokens.colorPaletteDarkOrangeBackground3,
    color: tokens.colorNeutralForegroundOnBrand,
  },
  "7d": {
    backgroundColor: tokens.colorPaletteYellowBackground3,
    color: tokens.colorNeutralForeground1,
  },
  "10d": {
    backgroundColor: tokens.colorNeutralBackground3,
    color: tokens.colorNeutralForeground2,
  },
};

// ---------------------------------------------------------------------------
// Derivation helpers (mirrors TodoItem.tsx)
// ---------------------------------------------------------------------------

/**
 * Map sprk_priorityscore (0-100) to a display label string for the badge.
 *
 * Per R3 FR-09: sprk_todo carries a native 0-100 score (no separate option set).
 * Bucketing: >=75 Urgent, >=50 High, >=25 Normal, else Low.
 */
function derivePriorityLevel(priorityScore: number | undefined): PriorityLevel | null {
  if (priorityScore === undefined || priorityScore === null) return null;
  if (priorityScore >= 75) return "Urgent";
  if (priorityScore >= 50) return "High";
  if (priorityScore >= 25) return "Normal";
  return "Low";
}

function deriveEffortLevel(effortScore: number | undefined): EffortLevel | null {
  if (effortScore === undefined || effortScore === null) return null;
  if (effortScore >= 70) return "High";
  if (effortScore >= 35) return "Med";
  return "Low";
}

// ---------------------------------------------------------------------------
// Inline badge (same pattern as TodoItem.tsx)
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
  // ── Section container ──────────────────────────────────────────────────
  section: {
    display: "flex",
    flexDirection: "column",
    flexShrink: 0,
    borderTopWidth: "1px",
    borderTopStyle: "solid",
    borderTopColor: tokens.colorNeutralStroke2,
  },

  // ── Header row (clickable toggle) ──────────────────────────────────────
  headerButton: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground2,
    border: "none",
    cursor: "pointer",
    width: "100%",
    textAlign: "left",
    transitionProperty: "background-color",
    transitionDuration: tokens.durationFaster,
    transitionTimingFunction: tokens.curveEasyEase,
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground2Hover,
    },
    ":focus-visible": {
      outlineStyle: "solid",
      outlineWidth: "2px",
      outlineColor: tokens.colorBrandStroke1,
      outlineOffset: "-2px",
    },
  },

  chevron: {
    display: "flex",
    alignItems: "center",
    color: tokens.colorNeutralForeground3,
    flexShrink: 0,
  },

  headerLabel: {
    flex: "1 1 0",
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
  },

  // ── Dismissed item list ───────────────────────────────────────────────
  itemList: {
    display: "flex",
    flexDirection: "column",
  },

  // ── Individual dismissed item row ──────────────────────────────────────
  itemRow: {
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
    opacity: "0.7",
    transitionProperty: "background-color, opacity",
    transitionDuration: tokens.durationFaster,
    transitionTimingFunction: tokens.curveEasyEase,
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground1Hover,
      opacity: "1",
    },
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
    color: tokens.colorNeutralForeground3,
    fontWeight: tokens.fontWeightRegular,
    textDecorationLine: "line-through",
    textDecorationColor: tokens.colorNeutralForeground3,
  },

  context: {
    display: "-webkit-box",
    WebkitLineClamp: "1",
    WebkitBoxOrient: "vertical",
    overflow: "hidden",
    color: tokens.colorNeutralForeground4,
  },

  // Badges column
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

  // Actions column (restore button)
  actionsColumn: {
    display: "flex",
    alignItems: "flex-start",
    paddingTop: "2px",
    flexShrink: 0,
  },
});

// ---------------------------------------------------------------------------
// Dismissed item sub-component
// ---------------------------------------------------------------------------

interface IDismissedItemProps {
  todo: ITodo;
  onRestore: (todoId: string) => void;
  isRestoring: boolean;
}

const DismissedItem: React.FC<IDismissedItemProps> = React.memo(
  ({ todo, onRestore, isRestoring }) => {
    const styles = useStyles();

    const priorityLevel = derivePriorityLevel(todo.sprk_priorityscore);
    const effortLevel = deriveEffortLevel(todo.sprk_effortscore);
    const dueDate = parseDueDate(todo.sprk_duedate);
    const dueLabel = computeDueLabel(dueDate);

    const rowAriaLabel = [
      "Dismissed.",
      todo.sprk_name,
      priorityLevel ? `Priority: ${priorityLevel}.` : "",
      effortLevel ? `Effort: ${effortLevel}.` : "",
      dueLabel.label ? `Due: ${dueLabel.label}.` : "",
    ]
      .filter(Boolean)
      .join(" ");

    return (
      <div
        className={styles.itemRow}
        role="listitem"
        aria-label={rowAriaLabel}
      >
        {/* Title + context */}
        <div className={styles.contentColumn}>
          <Text as="span" size={300} className={styles.title}>
            {todo.sprk_name}
          </Text>
          {todo.sprk_notes && (
            <Text as="span" size={200} className={styles.context}>
              {todo.sprk_notes}
            </Text>
          )}
        </div>

        {/* Badges column */}
        <div className={styles.badgesColumn}>
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
          {dueLabel.urgency !== "none" && (
            <InlineBadge
              style={DUE_BADGE_STYLE[dueLabel.urgency]}
              ariaLabel={`Due: ${dueLabel.label}`}
            >
              {dueLabel.label}
            </InlineBadge>
          )}
        </div>

        {/* Restore button */}
        <div className={styles.actionsColumn}>
          <Button
            appearance="subtle"
            size="small"
            icon={<ArrowUndoRegular />}
            onClick={() => onRestore(todo.sprk_todoid)}
            disabled={isRestoring}
            aria-label={`Restore "${todo.sprk_name}"`}
            title="Restore to active list"
          />
        </div>
      </div>
    );
  }
);

DismissedItem.displayName = "DismissedItem";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IDismissedSectionProps {
  /** Dismissed to-do items */
  items: ITodo[];
  /**
   * Called when the user clicks the restore button on a dismissed item.
   * Parent handles the Dataverse update + optimistic list move.
   */
  onRestore: (todoId: string) => void;
  /**
   * Set of todo IDs currently being restored (to disable their restore buttons
   * while the async operation is in-flight).
   */
  restoringIds?: Set<string>;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const DismissedSection: React.FC<IDismissedSectionProps> = React.memo(
  ({ items, onRestore, restoringIds = new Set() }) => {
    const styles = useStyles();
    const [isExpanded, setIsExpanded] = React.useState<boolean>(false);

    // Don't render anything when there are no dismissed items
    if (items.length === 0) {
      return null;
    }

    const toggleExpanded = () => setIsExpanded((prev) => !prev);

    const countLabel = `${items.length} dismissed item${items.length === 1 ? "" : "s"}`;

    return (
      <div
        className={styles.section}
        role="region"
        aria-label="Dismissed to-do items"
      >
        {/* Toggle header */}
        <button
          className={styles.headerButton}
          onClick={toggleExpanded}
          aria-expanded={isExpanded}
          aria-controls="dismissed-todo-list"
        >
          <span className={styles.chevron} aria-hidden="true">
            {isExpanded ? (
              <ChevronUpRegular fontSize={14} />
            ) : (
              <ChevronDownRegular fontSize={14} />
            )}
          </span>
          <Text size={200} className={styles.headerLabel}>
            Dismissed
          </Text>
          <Badge
            appearance="filled"
            color="subtle"
            size="small"
            aria-label={countLabel}
          >
            {items.length}
          </Badge>
        </button>

        {/* Collapsible item list */}
        {isExpanded && (
          <div
            id="dismissed-todo-list"
            className={styles.itemList}
            role="list"
            aria-label={`Dismissed items, ${countLabel}`}
          >
            {items.map((item) => (
              <DismissedItem
                key={item.sprk_todoid}
                todo={item}
                onRestore={onRestore}
                isRestoring={restoringIds.has(item.sprk_todoid)}
              />
            ))}
          </div>
        )}
      </div>
    );
  }
);

DismissedSection.displayName = "DismissedSection";
