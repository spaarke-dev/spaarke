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
 *   - Each dismissed item has a restore button that calls onRestore(eventId)
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
import { IEvent } from "../../types/entities";
import { PriorityLevel, EffortLevel, TodoSource } from "../../types/enums";
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

function derivePriorityLevel(priority: number | undefined): PriorityLevel | null {
  switch (priority) {
    case 0: return "Low";
    case 1: return "Normal";
    case 2: return "High";
    case 3: return "Urgent";
    default: return null;
  }
}

function deriveEffortLevel(effortScore: number | undefined): EffortLevel | null {
  if (effortScore === undefined || effortScore === null) return null;
  if (effortScore >= 70) return "High";
  if (effortScore >= 35) return "Med";
  return "Low";
}

function deriveSourceType(source: number | undefined): TodoSource {
  switch (source) {
    case 100000000: return "System";
    case 100000001: return "User";
    case 100000002: return "AI";
    default:        return "User";
  }
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
  event: IEvent;
  onRestore: (eventId: string) => void;
  isRestoring: boolean;
}

const DismissedItem: React.FC<IDismissedItemProps> = React.memo(
  ({ event, onRestore, isRestoring }) => {
    const styles = useStyles();

    const priorityLevel = derivePriorityLevel(event.sprk_priority);
    const effortLevel = deriveEffortLevel(event.sprk_effortscore);
    const dueDate = parseDueDate(event.sprk_duedate);
    const dueLabel = computeDueLabel(dueDate);
    // source kept for aria label but icon not shown in dismissed view
    const sourceType = deriveSourceType(event.sprk_todosource);
    const sourceLabel =
      sourceType === "System"
        ? "System-generated"
        : sourceType === "AI"
        ? "AI-generated"
        : "Manually created";

    const rowAriaLabel = [
      "Dismissed.",
      event.sprk_eventname,
      sourceLabel,
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
            {event.sprk_eventname}
          </Text>
          {(event.sprk_priorityreason || event.sprk_effortreason) && (
            <Text as="span" size={200} className={styles.context}>
              {event.sprk_priorityreason ?? event.sprk_effortreason}
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
            onClick={() => onRestore(event.sprk_eventid)}
            disabled={isRestoring}
            aria-label={`Restore "${event.sprk_eventname}"`}
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
  /** Dismissed to-do events */
  items: IEvent[];
  /**
   * Called when the user clicks the restore button on a dismissed item.
   * Parent handles the Dataverse update + optimistic list move.
   */
  onRestore: (eventId: string) => void;
  /**
   * Set of event IDs currently being restored (to disable their restore buttons
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
                key={item.sprk_eventid}
                event={item}
                onRestore={onRestore}
                isRestoring={restoringIds.has(item.sprk_eventid)}
              />
            ))}
          </div>
        )}
      </div>
    );
  }
);

DismissedSection.displayName = "DismissedSection";
