/**
 * KanbanCard — card for the Kanban board (rich Smart To Do subtree).
 *
 * R5 FR-01 / task 002 — hoisted host-agnostic from
 * `src/solutions/LegalWorkspace/src/components/SmartToDo/KanbanCard.tsx`.
 * Cross-folder imports re-homed to package sources: `ITodo` from
 * `../../types/entities`; `computeDueLabel`/`parseDueDate`/`DueUrgency`/
 * `computeTodoScore` from the LOCKED `../../utils/todoScoring`. `RecordCardShell`
 * + `CardIcon` stay imported from the `@spaarke/ui-components` package barrel.
 *
 * NOTE (task 002 deviation): this is the LegalWorkspace *rich* card
 * (`RecordCardShell`-based, `ITodo`-typed). It is DISTINCT from the package's
 * pre-existing widget card at `components/KanbanCard/` (flexbox-based,
 * `IKanbanCardTodo`-generic), which the SmartTodo Code Page consumes under the
 * bare `KanbanCard` root export. To avoid clobbering that externally-consumed
 * export, this rich card is FOLDER-INTERNAL to `components/SmartToDo/` and is
 * re-exported at the package root under the ALIAS `SmartToDoKanbanCard`
 * (`ISmartToDoKanbanCardProps`).
 *
 * Thin wrapper around RecordCardShell from @spaarke/ui-components.
 * Shows a score circle (left), title + due date + assigned (center),
 * and a pin toggle (right tools slot). Accent border colour comes from the
 * parent Kanban column.
 */

import * as React from "react";
import {
  tokens,
  Text,
  Button,
  makeStyles,
  mergeClasses,
} from "@fluentui/react-components";
import { PinRegular, PinFilled, Flag16Filled } from "@fluentui/react-icons";
import type { ITodo } from "../../types/entities";
import {
  computeDueLabel,
  computeTodoScore,
  parseDueDate,
  type DueUrgency,
} from "../../utils/todoScoring";
import { RecordCardShell, CardIcon } from "@spaarke/ui-components";

// ---------------------------------------------------------------------------
// Due badge
// ---------------------------------------------------------------------------

const DUE_BADGE_STYLE: Record<Exclude<DueUrgency, "none">, React.CSSProperties> = {
  overdue: { backgroundColor: tokens.colorPaletteRedBackground3, color: tokens.colorNeutralForegroundOnBrand },
  "3d": { backgroundColor: tokens.colorPaletteDarkOrangeBackground3, color: tokens.colorNeutralForegroundOnBrand },
  "7d": { backgroundColor: tokens.colorPaletteYellowBackground3, color: tokens.colorNeutralForeground1 },
  "10d": { backgroundColor: tokens.colorNeutralBackground3, color: tokens.colorNeutralForeground2 },
};

const badgeBase: React.CSSProperties = {
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
};

// ---------------------------------------------------------------------------
// FR-02/FR-03 priority + effort indicators (task 012) — presentation-only
// surfacing of the `sprk_priority` / `sprk_effort` Choice fields (task 010).
// Distinct from the SCORE-derived score circle above (reads
// `sprk_priorityscore`/`sprk_effortscore` via the LOCKED `todoScoring.ts`).
//
// `ITodo` (`../../types/entities.ts`) does not (yet) declare these two
// fields — widening that shared contract is out of this task's edit scope
// (`src/components/**` only, per task 012 concurrent-agent boundary). Once
// Dataverse rows include them (task 010 schema, deployed), reading them via
// this local structural type is a safe, honest optional access. Kept as a
// local duplicate of the same helpers in `../KanbanCard/KanbanCard.tsx`
// (the widget card) rather than a new shared module — this file already
// duplicates its own `DUE_BADGE_STYLE` map independently of the widget
// card's identical map, so a local copy here matches the file's existing
// convention rather than introducing new shared surface (CLAUDE.md §11).
// ---------------------------------------------------------------------------

/** Local structural extension covering the two new Choice fields. */
interface IKanbanCardPriorityEffortFields {
  /** sprk_priority Choice: Urgent=100000000, High=100000001, Medium=100000002, Low=100000003. */
  sprk_priority?: number | null;
  /** sprk_effort Choice: None=100000000, Very High=100000001, High=100000002, Medium=100000003, Low=100000004. */
  sprk_effort?: number | null;
}

/** Priority glyph tone (icon colour + accessible label) per `sprk_priority` option. */
export function derivePriorityGlyph(
  value: number | null | undefined
): { label: string; color: string } | undefined {
  switch (value) {
    case 100000000:
      return { label: "Urgent", color: tokens.colorStatusDangerForeground1 };
    case 100000001:
      return { label: "High", color: tokens.colorStatusWarningForeground1 };
    case 100000002:
      return { label: "Medium", color: tokens.colorStatusSuccessForeground1 };
    case 100000003:
      return { label: "Low", color: tokens.colorNeutralForeground3 };
    default:
      // Unset or an unrecognised value — neutral no-op (no glyph rendered).
      return undefined;
  }
}

/** Effort badge (label + tone) per `sprk_effort` option. */
export function deriveEffortBadge(
  value: number | null | undefined
): { label: string; style: React.CSSProperties } | undefined {
  switch (value) {
    case 100000000:
      return { label: "None", style: { backgroundColor: tokens.colorNeutralBackground3, color: tokens.colorNeutralForeground2 } };
    case 100000001:
      return { label: "Very High", style: { backgroundColor: tokens.colorStatusDangerBackground1, color: tokens.colorStatusDangerForeground1 } };
    case 100000002:
      return { label: "High", style: { backgroundColor: tokens.colorPaletteDarkOrangeBackground1, color: tokens.colorPaletteDarkOrangeForeground1 } };
    case 100000003:
      return { label: "Medium", style: { backgroundColor: tokens.colorStatusWarningBackground1, color: tokens.colorStatusWarningForeground1 } };
    case 100000004:
      return { label: "Low", style: { backgroundColor: tokens.colorStatusSuccessBackground1, color: tokens.colorStatusSuccessForeground1 } };
    default:
      // Unset or an unrecognised value — neutral no-op (no badge rendered).
      return undefined;
  }
}

// ---------------------------------------------------------------------------
// Content-specific styles (layout handled by RecordCardShell)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  completed: { opacity: "0.6" },
  /**
   * Title row — wraps the title text + the FR-02 priority glyph so the
   * glyph sits inline with the title without breaking the title's
   * ellipsis-on-overflow behaviour (task 012).
   */
  titleRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalXXS,
    minWidth: 0,
  },
  title: {
    display: "block",
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    flex: "1 1 auto",
    minWidth: 0,
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
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function formatDueDate(date: Date): string {
  const now = new Date();
  const sameYear = date.getFullYear() === now.getFullYear();
  return date.toLocaleDateString("en-US", {
    month: "short",
    day: "numeric",
    ...(sameYear ? {} : { year: "numeric" }),
  });
}

function scoreCircleColors(score: number): { bg: string; fg: string } {
  if (score >= 60) return { bg: tokens.colorPaletteRedBackground3, fg: tokens.colorNeutralForegroundOnBrand };
  if (score >= 30) return { bg: tokens.colorPaletteYellowBackground3, fg: tokens.colorNeutralForeground1 };
  return { bg: tokens.colorPaletteGreenBackground3, fg: tokens.colorNeutralForegroundOnBrand };
}

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IKanbanCardProps {
  todo: ITodo;
  onPinToggle?: (todoId: string) => void;
  onClick?: (todoId: string) => void;
  accentColor?: string;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const KanbanCard: React.FC<IKanbanCardProps> = React.memo(
  ({ todo, onPinToggle, onClick, accentColor }) => {
    const styles = useStyles();

    const dueDate = parseDueDate(todo.sprk_duedate);
    const dueLabel = computeDueLabel(dueDate);
    const { todoScore } = computeTodoScore(todo);
    const roundedScore = Math.round(todoScore);
    // Completed = statuscode 2 (per task 009 mapping).
    const isCompleted = todo.statuscode === 2;
    const isPinned = todo.sprk_todopinned === true;
    const dueDateFormatted = dueDate ? formatDueDate(dueDate) : null;
    const colors = scoreCircleColors(roundedScore);

    // FR-02/FR-03 (task 012): priority glyph + effort badge, read from the raw
    // Choice fields (see the local structural type + helpers above).
    const { sprk_priority: priorityChoice, sprk_effort: effortChoice } = todo as unknown as IKanbanCardPriorityEffortFields;
    const priorityGlyph = derivePriorityGlyph(priorityChoice);
    const effortBadge = deriveEffortBadge(effortChoice);

    const handlePinClick = React.useCallback(() => {
      onPinToggle?.(todo.sprk_todoid);
    }, [onPinToggle, todo.sprk_todoid]);

    const handleCardClick = React.useCallback(() => {
      onClick?.(todo.sprk_todoid);
    }, [onClick, todo.sprk_todoid]);

    const ariaLabel = [
      todo.sprk_name,
      isCompleted ? "Completed." : "Open.",
      isPinned ? "Pinned." : "",
      dueDateFormatted ? `Due: ${dueDateFormatted}.` : "",
      dueLabel.label ? `${dueLabel.label}.` : "",
      priorityGlyph ? `Priority: ${priorityGlyph.label}.` : "",
      effortBadge ? `Effort: ${effortBadge.label}.` : "",
      `To Do Score: ${roundedScore}.`,
    ].filter(Boolean).join(" ");

    // Secondary content: due date row + assigned row
    const secondaryContent = (
      <>
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
                  <Text as="span" size={200} style={{ color: tokens.colorNeutralForeground3 }}>{"·"}</Text>
                )}
                <span role="img" aria-label={dueLabel.label} style={{ ...badgeBase, ...DUE_BADGE_STYLE[dueLabel.urgency] }}>
                  {dueLabel.label}
                </span>
              </>
            )}
          </div>
        )}
        {todo.assignedToName && (
          <div className={styles.metadataRow}>
            <span className={styles.fieldLabel}>Assigned:</span>
            <span className={styles.fieldValue}>{todo.assignedToName}</span>
          </div>
        )}
        {effortBadge && (
          <div className={styles.metadataRow}>
            <span className={styles.fieldLabel}>Effort:</span>
            <span role="img" aria-label={`Effort: ${effortBadge.label}`} style={{ ...badgeBase, ...effortBadge.style }}>
              {effortBadge.label}
            </span>
          </div>
        )}
      </>
    );

    return (
      <RecordCardShell
        icon={
          <CardIcon
            size={40}
            backgroundColor={colors.bg}
            iconColor={colors.fg}
          >
            <span style={{ fontWeight: tokens.fontWeightSemibold, fontSize: tokens.fontSizeBase300, lineHeight: "1" }}>
              {roundedScore}
            </span>
          </CardIcon>
        }
        accentColor={accentColor ?? "none"}
        primaryContent={
          <div className={styles.titleRow}>
            <Text as="span" size={300} className={mergeClasses(styles.title, isCompleted && styles.titleCompleted)}>
              {todo.sprk_name}
            </Text>
            {priorityGlyph && (
              <Flag16Filled
                style={{ color: priorityGlyph.color, flexShrink: 0 }}
                role="img"
                aria-label={`Priority: ${priorityGlyph.label}`}
                title={`Priority: ${priorityGlyph.label}`}
              />
            )}
          </div>
        }
        secondaryContent={secondaryContent}
        tools={
          <Button
            appearance="subtle"
            size="small"
            icon={isPinned ? <PinFilled /> : <PinRegular />}
            onClick={handlePinClick}
            aria-label={isPinned ? `Unpin "${todo.sprk_name}"` : `Pin "${todo.sprk_name}"`}
            title={isPinned ? "Unpin from column" : "Pin to column"}
          />
        }
        onClick={handleCardClick}
        ariaLabel={ariaLabel}
        className={isCompleted ? styles.completed : undefined}
      />
    );
  }
);

KanbanCard.displayName = "KanbanCard";
