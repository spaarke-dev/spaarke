/**
 * KanbanCard — Domain-specific SmartTodo card for the Kanban board.
 *
 * R4 task 102 (E-1, 2026-06-18) — hoisted from
 * `src/solutions/SmartTodo/src/components/KanbanCard.tsx` into the
 * `@spaarke/smart-todo-components` peer package so the workspace widget
 * and the standalone Code Page share ONE source of truth for the Kanban
 * card surface (closes UAT issue 6: cards in the widget need parity with
 * the app's KanbanCard).
 *
 * Bit-for-bit visual + interaction parity with the pre-hoist Code Page
 * version is required — the Code Page swap is an import-source change
 * only (no UAT regressions).
 *
 * Layout (flexbox row):
 *   [Checkbox?] [Score 40px] [Title + Due + Assigned] [Open?] [Pin]
 *
 * Features preserved from the pre-hoist app version:
 *   - Left accent border (3px) coloured by parent column via prop
 *   - Score displayed as a 40px circle (red ≥60 / yellow ≥30 / green)
 *   - Pin toggle locks the item in its column (host wires persistence)
 *   - Card body click → host's `onClick` (Code Page opens detail pane)
 *   - Open icon → host's `onOpen` (dispatches OPEN_TODOS_EVENT modal)
 *   - Double-click on card body → same `onOpen` (FR-26)
 *   - Multi-select checkbox (FR-27) — only rendered when host plumbs
 *     `onToggleSelect`; legacy/embedded surfaces stay unchanged
 *   - Completed state: opacity 0.6, title strikethrough
 *   - Due-date badge with urgency colour (overdue / 3d / 7d / 10d)
 *
 * Standards:
 *   - ADR-021: Fluent v9 + Griffel + semantic tokens (no v8, no inline
 *     styles for static rules — runtime accent/score colours stay inline
 *     because Griffel cannot author per-instance dynamic colours).
 *   - ADR-012: Hoisted to peer package, structurally typed via
 *     `IKanbanCardTodo` so both the Code Page's `ITodo` and the widget's
 *     `ITodoRecord` work without transforms.
 *
 * @see ../../utils/todoScoring.ts — composite score + due label helpers
 * @see ./KanbanCard.styles.ts — Griffel styles (bit-for-bit copy)
 * @see src/solutions/SmartTodo/src/components/KanbanCard.tsx (pre-hoist source)
 */

import * as React from 'react';
import { tokens, Text, Button, Checkbox } from '@fluentui/react-components';
import { PinRegular, PinFilled, Open20Regular, Flag16Filled } from '@fluentui/react-icons';

import { useKanbanCardStyles } from './KanbanCard.styles';
import type { IKanbanCardTodo } from '../../types/kanban';
import { computeDueLabel, computeTodoScore, parseDueDate, type DueUrgency } from '../../utils/todoScoring';

// ---------------------------------------------------------------------------
// Inline badge — small pill used for the due-date urgency indicator. Inline
// because the badge colour is data-driven (per `DueUrgency`) and Griffel
// cannot author dynamic per-instance colours.
// ---------------------------------------------------------------------------

interface IInlineBadgeProps {
  style: React.CSSProperties;
  ariaLabel: string;
  children: React.ReactNode;
}

const InlineBadge: React.FC<IInlineBadgeProps> = ({ style, ariaLabel, children }) => (
  <span
    role="img"
    aria-label={ariaLabel}
    style={{
      display: 'inline-flex',
      alignItems: 'center',
      justifyContent: 'center',
      borderRadius: tokens.borderRadiusSmall,
      paddingTop: '1px',
      paddingBottom: '1px',
      paddingLeft: tokens.spacingHorizontalXS,
      paddingRight: tokens.spacingHorizontalXS,
      fontSize: tokens.fontSizeBase100,
      fontWeight: tokens.fontWeightSemibold,
      lineHeight: tokens.lineHeightBase100,
      whiteSpace: 'nowrap',
      ...style,
    }}
  >
    {children}
  </span>
);

// ---------------------------------------------------------------------------
// Due badge style map — colour palette matches the Code Page version exactly
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
// FR-02/FR-03 priority + effort indicators (task 012) — presentation-only
// surfacing of the `sprk_priority` / `sprk_effort` Choice fields (task 010).
// Distinct from the SCORE-derived `roundedScore` circle above (which reads
// `sprk_priorityscore`/`sprk_effortscore`, computed by `computeTodoScore` in
// the LOCKED `todoScoring.ts`) — these glyphs render the raw user-selected
// Choice label, not a derived score.
//
// `IKanbanCardTodo` (`../../types/kanban.ts`) does not (yet) declare these
// two fields — widening that shared contract is out of this task's edit
// scope (`src/components/**` only, per task 012 concurrent-agent boundary).
// Once Dataverse rows include them (task 010 schema, deployed), reading them
// via this local structural type is a safe, honest optional access.
// ---------------------------------------------------------------------------

/** Local structural extension covering the two new Choice fields. */
interface IKanbanCardPriorityEffortFields {
  /** sprk_priority Choice: Urgent=100000000, High=100000001, Medium=100000002, Low=100000003. */
  sprk_priority?: number | null;
  /** sprk_effort Choice: None=100000000, Very High=100000001, High=100000002, Medium=100000003, Low=100000004. */
  sprk_effort?: number | null;
}

/** Priority glyph tone (icon colour + accessible label) per `sprk_priority` option. */
export function derivePriorityGlyph(value: number | null | undefined): { label: string; color: string } | undefined {
  switch (value) {
    case 100000000:
      return { label: 'Urgent', color: tokens.colorStatusDangerForeground1 };
    case 100000001:
      return { label: 'High', color: tokens.colorStatusWarningForeground1 };
    case 100000002:
      return { label: 'Medium', color: tokens.colorStatusSuccessForeground1 };
    case 100000003:
      return { label: 'Low', color: tokens.colorNeutralForeground3 };
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
      return {
        label: 'None',
        style: { backgroundColor: tokens.colorNeutralBackground3, color: tokens.colorNeutralForeground2 },
      };
    case 100000001:
      return {
        label: 'Very High',
        style: { backgroundColor: tokens.colorStatusDangerBackground1, color: tokens.colorStatusDangerForeground1 },
      };
    case 100000002:
      return {
        label: 'High',
        style: {
          backgroundColor: tokens.colorPaletteDarkOrangeBackground1,
          color: tokens.colorPaletteDarkOrangeForeground1,
        },
      };
    case 100000003:
      return {
        label: 'Medium',
        style: { backgroundColor: tokens.colorStatusWarningBackground1, color: tokens.colorStatusWarningForeground1 },
      };
    case 100000004:
      return {
        label: 'Low',
        style: { backgroundColor: tokens.colorStatusSuccessBackground1, color: tokens.colorStatusSuccessForeground1 },
      };
    default:
      // Unset or an unrecognised value — neutral no-op (no badge rendered).
      return undefined;
  }
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Format a Date to a short display string like "Feb 4" or "Feb 4, 2027". */
function formatDueDate(date: Date): string {
  const now = new Date();
  const sameYear = date.getFullYear() === now.getFullYear();
  return date.toLocaleDateString('en-US', {
    month: 'short',
    day: 'numeric',
    ...(sameYear ? {} : { year: 'numeric' }),
  });
}

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IKanbanCardProps<T extends IKanbanCardTodo = IKanbanCardTodo> {
  todo: T;
  /** Called when the pin button is clicked. */
  onPinToggle?: (todoId: string) => void;
  /**
   * Called when the card body is clicked (not pin / open / checkbox). The
   * Code Page wires this to its detail-pane subscriber; the widget can omit
   * it (card click is a no-op in workspace context).
   */
  onClick?: (todoId: string) => void;
  /** Left border accent colour from the parent column. */
  accentColor?: string;
  /** Whether this card is currently selected (single-select detail pane). */
  isSelected?: boolean;
  /**
   * Called when the user requests to OPEN the card in a modal (single
   * click on the Open icon, double-click on the card body, or Enter key
   * when the Open icon has focus). When omitted, the Open icon is not
   * rendered and double-click is a no-op (back-compat for surfaces that
   * don't plumb modal routing).
   *
   * @see FR-25, FR-26 (R4 spec)
   */
  onOpen?: (todoId: string) => void;
  /**
   * Whether the card is in the multi-select set (FR-27). Independent of
   * `isSelected` (which tracks the single detail-pane focus). Drives the
   * checkbox state + ARIA labelling.
   */
  isMultiSelected?: boolean;
  /**
   * Called when the user toggles the per-card selection checkbox. If
   * undefined, the checkbox is NOT rendered (back-compat).
   */
  onToggleSelect?: (todoId: string) => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

function KanbanCardInner<T extends IKanbanCardTodo>({
  todo,
  onPinToggle,
  onClick,
  accentColor,
  isSelected = false,
  onOpen,
  isMultiSelected = false,
  onToggleSelect,
}: IKanbanCardProps<T>): React.ReactElement {
  const styles = useKanbanCardStyles();

  // Derived display values
  const dueDate = parseDueDate(todo.sprk_duedate);
  const dueLabel = computeDueLabel(dueDate);
  const { todoScore } = computeTodoScore(todo);
  const roundedScore = Math.round(todoScore);
  // statuscode 2 = Completed; statecode 1 (Inactive) also signals not active.
  const isCompleted = todo.statuscode === 2 || todo.statecode === 1;
  const isPinned = todo.sprk_todopinned === true;
  const dueDateFormatted = dueDate ? formatDueDate(dueDate) : null;

  // FR-02/FR-03 (task 012): priority glyph + effort badge, read from the raw
  // Choice fields (see the local structural type + helpers above `KanbanCardInner`).
  const { sprk_priority: priorityChoice, sprk_effort: effortChoice } =
    todo as unknown as IKanbanCardPriorityEffortFields;
  const priorityGlyph = derivePriorityGlyph(priorityChoice);
  const effortBadge = deriveEffortBadge(effortChoice);

  // -------------------------------------------------------------------------
  // Handlers
  // -------------------------------------------------------------------------

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
      if (ev.key === 'Enter' || ev.key === ' ') {
        ev.preventDefault();
        handleCardClick();
      }
    },
    [handleCardClick]
  );

  // Open icon click (trailing edge). Stops propagation so the card body's
  // onClick does NOT also fire (which would otherwise open the detail panel
  // concurrently with the modal).
  const handleOpenClick = React.useCallback(
    (ev: React.MouseEvent<HTMLButtonElement>) => {
      ev.stopPropagation();
      if (onOpen) {
        onOpen(todo.sprk_todoid);
      }
    },
    [onOpen, todo.sprk_todoid]
  );

  // Double-click anywhere on the card body opens the modal (FR-26). Defensive
  // guard: ignore double-clicks originating on interactive children
  // (checkbox / pin / open) so they retain single-click semantics.
  const handleCardDoubleClick = React.useCallback(
    (ev: React.MouseEvent<HTMLDivElement>) => {
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

  // Selection checkbox toggle (FR-27). Stops propagation so the click does
  // NOT bubble to the card body click handler (which would open the detail
  // pane in the Code Page).
  const handleSelectChange = React.useCallback(
    (ev: React.SyntheticEvent<HTMLInputElement>) => {
      ev.stopPropagation();
      if (onToggleSelect) {
        onToggleSelect(todo.sprk_todoid);
      }
    },
    [onToggleSelect, todo.sprk_todoid]
  );

  // The Fluent v9 Checkbox wraps its input in a label; the click on the
  // label still bubbles, so we also stop propagation at the container level.
  const handleSelectContainerClick = React.useCallback((ev: React.MouseEvent<HTMLDivElement>) => {
    ev.stopPropagation();
  }, []);

  // -------------------------------------------------------------------------
  // Accessible label
  // -------------------------------------------------------------------------

  const cardAriaLabel = [
    todo.sprk_name,
    isSelected ? 'Selected.' : '',
    isMultiSelected ? 'In multi-select.' : '',
    isCompleted ? 'Completed.' : 'Open.',
    isPinned ? 'Pinned.' : '',
    dueDateFormatted ? `Due: ${dueDateFormatted}.` : '',
    dueLabel.label ? `${dueLabel.label}.` : '',
    priorityGlyph ? `Priority: ${priorityGlyph.label}.` : '',
    effortBadge ? `Effort: ${effortBadge.label}.` : '',
    `To Do Score: ${roundedScore}.`,
  ]
    .filter(Boolean)
    .join(' ');

  // -------------------------------------------------------------------------
  // Class composition
  // -------------------------------------------------------------------------

  const cardClassName = [styles.card, isSelected ? styles.cardSelected : '', isCompleted ? styles.cardCompleted : '']
    .filter(Boolean)
    .join(' ');

  const titleClassName = [styles.title, isCompleted ? styles.titleCompleted : ''].filter(Boolean).join(' ');

  // Score circle colour: red (≥60), yellow (30–59), green (<30).
  const scoreCircleStyle: React.CSSProperties =
    roundedScore >= 60
      ? {
          backgroundColor: tokens.colorPaletteRedBackground3,
          color: tokens.colorNeutralForegroundOnBrand,
        }
      : roundedScore >= 30
        ? {
            backgroundColor: tokens.colorPaletteYellowBackground3,
            color: tokens.colorNeutralForeground1,
          }
        : {
            backgroundColor: tokens.colorPaletteGreenBackground3,
            color: tokens.colorNeutralForegroundOnBrand,
          };

  // Left accent border via inline style (colour is a runtime prop).
  const accentStyle: React.CSSProperties | undefined = accentColor
    ? {
        borderLeftWidth: '3px',
        borderLeftStyle: 'solid',
        borderLeftColor: accentColor,
      }
    : undefined;

  // -------------------------------------------------------------------------
  // Render
  // -------------------------------------------------------------------------

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
      {/* Selection checkbox — only rendered when host plumbs `onToggleSelect`. */}
      {onToggleSelect && (
        <div className={styles.selectionColumn} onClick={handleSelectContainerClick}>
          <Checkbox
            checked={isMultiSelected}
            onChange={handleSelectChange}
            aria-label={isMultiSelected ? `Deselect "${todo.sprk_name}"` : `Select "${todo.sprk_name}"`}
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
        {/* Row 1: Title + priority glyph (FR-02) */}
        <div className={styles.titleRow}>
          <Text as="span" size={300} weight="semibold" className={titleClassName}>
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

        {/* Row 2: Due date + urgency badge */}
        {(dueDateFormatted || dueLabel.urgency !== 'none') && (
          <div className={styles.metadataRow}>
            {dueDateFormatted && (
              <>
                <span className={styles.fieldLabel}>Due:</span>
                <span className={styles.fieldValue}>{dueDateFormatted}</span>
              </>
            )}
            {dueLabel.urgency !== 'none' && (
              <>
                {dueDateFormatted && (
                  <Text as="span" size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                    {'·'}
                  </Text>
                )}
                <InlineBadge style={DUE_BADGE_STYLE[dueLabel.urgency]} ariaLabel={dueLabel.label}>
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

        {/* Row 4: Effort indicator (FR-03) */}
        {effortBadge && (
          <div className={styles.metadataRow}>
            <span className={styles.fieldLabel}>Effort:</span>
            <InlineBadge style={effortBadge.style} ariaLabel={`Effort: ${effortBadge.label}`}>
              {effortBadge.label}
            </InlineBadge>
          </div>
        )}
      </div>

      {/* Actions column: open + pin buttons */}
      <div className={styles.actionsColumn}>
        {/* Open icon — only rendered when host plumbs `onOpen`. */}
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
          title={isPinned ? 'Unpin from column' : 'Pin to column'}
        />
      </div>
    </div>
  );
}

/**
 * Memoised SmartTodo Kanban card.
 *
 * Generic on `T extends IKanbanCardTodo` so both the Code Page's `ITodo`
 * and the widget's `ITodoRecord` flow through without a transform.
 */
export const KanbanCard = React.memo(KanbanCardInner) as <T extends IKanbanCardTodo = IKanbanCardTodo>(
  props: IKanbanCardProps<T>
) => React.ReactElement;

// Display-name set on the underlying inner function for React DevTools.
(KanbanCardInner as React.FC).displayName = 'KanbanCard';
