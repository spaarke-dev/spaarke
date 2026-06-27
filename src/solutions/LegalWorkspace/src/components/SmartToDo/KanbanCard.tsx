/**
 * KanbanCard — card for the Kanban board (Smart To Do).
 *
 * Thin wrapper around RecordCardShell from @spaarke/ui-components.
 * Shows a score circle (left), title + due date + assigned (center),
 * and a pin toggle (right tools slot).
 *
 * Accent border colour comes from the parent Kanban column.
 */

import * as React from "react";
import {
  tokens,
  Text,
  Button,
  makeStyles,
  mergeClasses,
} from "@fluentui/react-components";
import { PinRegular, PinFilled } from "@fluentui/react-icons";
import { ITodo } from "../../types/entities";
import { computeDueLabel, parseDueDate, DueUrgency } from "../../utils/dueLabelUtils";
import { computeTodoScore } from "../../utils/todoScoreUtils";
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
// Content-specific styles (layout handled by RecordCardShell)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  completed: { opacity: "0.6" },
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
                  <Text as="span" size={200} style={{ color: tokens.colorNeutralForeground3 }}>{"\u00B7"}</Text>
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
          <Text as="span" size={300} className={mergeClasses(styles.title, isCompleted && styles.titleCompleted)}>
            {todo.sprk_name}
          </Text>
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
