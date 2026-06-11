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
  Checkbox,
} from "@fluentui/react-components";
import {
  PinRegular,
  PinFilled,
  Open20Regular,
} from "@fluentui/react-icons";
import { ITodo } from "../types/entities";
import { computeDueLabel, parseDueDate, DueUrgency } from "../utils/dueLabelUtils";
import { computeTodoScore } from "../utils/todoScoreUtils";

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

  cardSelected: {
    backgroundColor: tokens.colorNeutralBackground1Selected,
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground1Selected,
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
    gap: tokens.spacingVerticalXXS,
  },

  /**
   * Selection checkbox column — leading edge (upper-left per FR-27).
   * Always rendered to keep layout stable; visually hidden until hover or
   * when `isMultiSelected` is true (R4 task 060).
   */
  selectionColumn: {
    display: "flex",
    flexShrink: 0,
    alignItems: "flex-start",
    paddingTop: "2px",
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
  todo: ITodo;
  /** Called when pin button is clicked. */
  onPinToggle?: (todoId: string) => void;
  /** Called when card body is clicked (not pin / open / checkbox). Opens detail pane. */
  onClick?: (todoId: string) => void;
  /** Left border accent colour from parent column. */
  accentColor?: string;
  /** Whether this card is currently selected (detail panel open). */
  isSelected?: boolean;
  /**
   * Called when the user requests to OPEN the card in the modal
   * (R4 task 060 / spec FR-25, FR-26).
   *
   * Fires from:
   *   - Single click on the trailing-edge Open icon button
   *   - Double-click anywhere on the card body (except checkbox / open / pin)
   *   - Enter key while the Open icon button has focus (browser default)
   *
   * The callback is expected to dispatch the canonical
   * `OPEN_TODOS_EVENT` (sprk-smarttodo:open-todos) on `window` with
   * `{ selectedIds: [todoId], firstId: todoId }`. The single subscriber is
   * `<SmartTodoLayout>` (App.tsx) which routes to `<SmartTodoModal>` — see
   * Wave A task 040.
   */
  onOpen?: (todoId: string) => void;
  /**
   * Whether the card is in the multi-select set (R4 task 060 / spec FR-27).
   * Independent of `isSelected` (which tracks the single detail-pane focus).
   * Drives the checkbox state + ARIA labelling.
   */
  isMultiSelected?: boolean;
  /**
   * Called when the user toggles the per-card selection checkbox. Parent
   * mutates a `Set<string>` lifted in `SmartTodoLayout` (Wave A R4-030);
   * the same Set drives the selection-aware toolbar (FR-08).
   *
   * If undefined, the checkbox is NOT rendered (back-compat for surfaces
   * that don't yet plumb selection state).
   */
  onToggleSelect?: (todoId: string) => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const KanbanCard: React.FC<IKanbanCardProps> = React.memo(
  ({
    todo,
    onPinToggle,
    onClick,
    accentColor,
    isSelected = false,
    onOpen,
    isMultiSelected = false,
    onToggleSelect,
  }) => {
    const styles = useStyles();

    // Derived display values
    const dueDate = parseDueDate(todo.sprk_duedate);
    const dueLabel = computeDueLabel(dueDate);
    const { todoScore } = computeTodoScore(todo);
    const roundedScore = Math.round(todoScore);
    // statuscode 2 = Completed (task 009 mapping). statecode 1 (Inactive) is
    // also a valid signal that the item is no longer in the active pipeline.
    const isCompleted = todo.statuscode === 2 || todo.statecode === 1;
    const isPinned = todo.sprk_todopinned === true;
    const dueDateFormatted = dueDate ? formatDueDate(dueDate) : null;

    // -----------------------------------------------------------------------
    // Handlers
    // -----------------------------------------------------------------------

    const handlePinClick = React.useCallback(
      (ev: React.MouseEvent<HTMLButtonElement>) => {
        ev.stopPropagation();
        if (onPinToggle) {
          onPinToggle(todo.sprk_todoid);
        }
      },
      [onPinToggle, todo.sprk_todoid]
    );

    const handleCardClick = React.useCallback(() => {
      if (onClick) {
        onClick(todo.sprk_todoid);
      }
    }, [onClick, todo.sprk_todoid]);

    const handleCardKeyDown = React.useCallback(
      (ev: React.KeyboardEvent<HTMLDivElement>) => {
        if (ev.key === "Enter" || ev.key === " ") {
          ev.preventDefault();
          handleCardClick();
        }
      },
      [handleCardClick]
    );

    // R4 task 060 — Open icon click (trailing edge, single click opens modal).
    // Stops propagation so the card body's onClick does NOT also fire (which
    // would otherwise open the detail panel concurrently).
    const handleOpenClick = React.useCallback(
      (ev: React.MouseEvent<HTMLButtonElement>) => {
        ev.stopPropagation();
        if (onOpen) {
          onOpen(todo.sprk_todoid);
        }
      },
      [onOpen, todo.sprk_todoid]
    );

    // R4 task 060 — Double-click on card body opens the modal (FR-26).
    // The single-click semantics (detail panel) are preserved by the existing
    // onClick handler; the platform fires onClick *and* onDoubleClick for a
    // double-click, but since they have different targets (modal vs panel),
    // both firing is the correct UX behaviour: detail panel opens on first
    // click as preview, modal opens on confirmation double-click.
    const handleCardDoubleClick = React.useCallback(
      (ev: React.MouseEvent<HTMLDivElement>) => {
        // Defensive: ignore double-clicks that originate on interactive
        // children (checkbox / pin / open icon) — they handle their own
        // semantics and shouldn't double as modal openers.
        const target = ev.target as HTMLElement | null;
        if (target?.closest('input[type="checkbox"], button')) {
          return;
        }
        if (onOpen) {
          onOpen(todo.sprk_todoid);
        }
      },
      [onOpen, todo.sprk_todoid]
    );

    // R4 task 060 — Selection checkbox toggle (FR-27).
    // Stops propagation so the click on the checkbox does NOT bubble to the
    // card body click handler (which would open the detail panel).
    const handleSelectChange = React.useCallback(
      (ev: React.SyntheticEvent<HTMLInputElement>) => {
        ev.stopPropagation();
        if (onToggleSelect) {
          onToggleSelect(todo.sprk_todoid);
        }
      },
      [onToggleSelect, todo.sprk_todoid]
    );

    // Prevent click on the checkbox container from bubbling to the card body
    // click handler (Fluent v9 Checkbox wraps its input in a label; the click
    // event from the label still bubbles).
    const handleSelectContainerClick = React.useCallback(
      (ev: React.MouseEvent<HTMLDivElement>) => {
        ev.stopPropagation();
      },
      []
    );

    // -----------------------------------------------------------------------
    // Accessible label
    // -----------------------------------------------------------------------

    const cardAriaLabel = [
      todo.sprk_name,
      isSelected ? "Selected." : "",
      isMultiSelected ? "In multi-select." : "",
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

    const cardClassName = [
      styles.card,
      isSelected ? styles.cardSelected : "",
      isCompleted ? styles.cardCompleted : "",
    ]
      .filter(Boolean)
      .join(" ");

    const titleClassName = [styles.title, isCompleted ? styles.titleCompleted : ""]
      .filter(Boolean)
      .join(" ");

    // Score circle colour: red (>=60), yellow (30-59), green (<30)
    const scoreCircleStyle: React.CSSProperties = roundedScore >= 60
      ? { backgroundColor: tokens.colorPaletteRedBackground3, color: tokens.colorNeutralForegroundOnBrand }
      : roundedScore >= 30
        ? { backgroundColor: tokens.colorPaletteYellowBackground3, color: tokens.colorNeutralForeground1 }
        : { backgroundColor: tokens.colorPaletteGreenBackground3, color: tokens.colorNeutralForegroundOnBrand };

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
        aria-selected={isSelected}
        onClick={handleCardClick}
        onDoubleClick={handleCardDoubleClick}
        onKeyDown={handleCardKeyDown}
      >
        {/* R4 task 060 — Selection checkbox (upper-left, FR-27). Only rendered
            when the parent plumbs an `onToggleSelect` callback so legacy /
            embedded surfaces without selection wiring stay unchanged. */}
        {onToggleSelect && (
          <div
            className={styles.selectionColumn}
            onClick={handleSelectContainerClick}
          >
            <Checkbox
              checked={isMultiSelected}
              onChange={handleSelectChange}
              aria-label={
                isMultiSelected
                  ? `Deselect "${todo.sprk_name}"`
                  : `Select "${todo.sprk_name}"`
              }
            />
          </div>
        )}

        {/* Score circle — prominent left visual anchor */}
        <div
          className={styles.scoreCircle}
          style={scoreCircleStyle}
          title={`To Do Score: ${roundedScore}`}
          aria-hidden="true"
        >
          {roundedScore}
        </div>

        {/* Content: title + metadata rows */}
        <div className={styles.contentColumn}>
          {/* Row 1: Title */}
          <Text as="span" size={300} weight="semibold" className={titleClassName}>
            {todo.sprk_name}
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
          {todo.assignedToName && (
            <div className={styles.metadataRow}>
              <span className={styles.fieldLabel}>Assigned:</span>
              <span className={styles.fieldValue}>{todo.assignedToName}</span>
            </div>
          )}
        </div>

        {/* Actions column: open + pin buttons */}
        <div className={styles.actionsColumn}>
          {/* R4 task 060 — Open icon (upper-right, FR-25). Single click
              dispatches OPEN_TODOS_EVENT (parent wires this). Enter / Space
              activation comes for free from the underlying <button>. */}
          {onOpen && (
            <Button
              appearance="subtle"
              size="small"
              icon={<Open20Regular />}
              onClick={handleOpenClick}
              aria-label={`Open "${todo.sprk_name}" in modal`}
              title="Open in modal"
            />
          )}
          <Button
            appearance="subtle"
            size="small"
            icon={isPinned ? <PinFilled /> : <PinRegular />}
            onClick={handlePinClick}
            aria-label={isPinned ? `Unpin "${todo.sprk_name}"` : `Pin "${todo.sprk_name}"`}
            title={isPinned ? "Unpin from column" : "Pin to column"}
          />
        </div>
      </div>
    );
  }
);

KanbanCard.displayName = "KanbanCard";
