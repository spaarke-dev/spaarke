/**
 * EventDueDateCard - Displays an event due date card with color-coded urgency
 *
 * Inlined from @spaarke/ui-components (the packaged tgz had a stub).
 * When the shared component library is properly rebuilt, this can be
 * replaced by re-importing from @spaarke/ui-components.
 *
 * Standards: ADR-012 (shared components), ADR-021 (Fluent v9 design tokens)
 */

import * as React from 'react';
import { Card, makeStyles, mergeClasses, tokens, Text, Badge, Spinner } from '@fluentui/react-components';

/**
 * Due-date urgency tier. Structurally identical to `DueUrgency` in
 * `@spaarke/ui-components` (`utils/dateLocal.ts`); declared here because this
 * package has no `@spaarke/*` dependency. The CALLER computes it with the
 * shared `dueUrgencyForDays` — this package holds no tier boundaries.
 */
export type EventDueUrgency = 'overdue' | '3d' | '7d' | '10d' | 'none';

export interface IEventDueDateCardProps {
  eventId: string;
  eventName: string;
  eventTypeName: string;
  dueDate: Date;
  daysUntilDue: number;
  isOverdue: boolean;
  /**
   * Due-date tier from the shared `dueUrgencyForDays` (task 081 / C-17). Drives
   * the badge and date-column colours; `daysUntilDue`/`isOverdue` drive only
   * the badge text.
   */
  urgency: EventDueUrgency;
  eventTypeColor?: string;
  description?: string;
  assignedTo?: string;
  onClick?: (eventId: string) => void;
  isNavigating?: boolean;
}

const useStyles = makeStyles({
  card: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'stretch',
    cursor: 'pointer',
    // v1.4.7 — reduced further (56 → 44) per UAT feedback ("reduce height of
    // the card"). v1.4.6 changed minHeight alone but the card content's
    // internal padding kept actual rendered height ~80px. This round also
    // tightens dateColumn + content paddings + font sizes so the floor is
    // actually visible.
    minHeight: '44px',
    overflow: 'hidden',
    padding: '0',
    ':hover': {
      boxShadow: tokens.shadow8,
    },
  },
  cardDisabled: {
    cursor: 'default',
    opacity: 0.7,
  },
  dateColumn: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    // v1.4.8 — wider column to fit single-line "DD-MMM-YYYY" format
    minWidth: '120px',
    paddingTop: tokens.spacingVerticalXXS,
    paddingBottom: tokens.spacingVerticalXXS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    color: tokens.colorNeutralForeground1,
  },
  // v1.4.8 — single-line "DD-MMM-YYYY" replaces the prior stacked
  // dateDay+dateMonth pattern. Larger, bolder so the date reads at a glance
  // (semibold + base300, tabular-nums to keep digits aligned across cards).
  dateLabel: {
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    fontVariantNumeric: 'tabular-nums',
    lineHeight: tokens.lineHeightBase300,
    whiteSpace: 'nowrap',
  },
  content: {
    display: 'flex',
    flexDirection: 'column',
    flex: 1,
    paddingTop: tokens.spacingVerticalXXS,
    paddingBottom: tokens.spacingVerticalXXS,
    paddingLeft: tokens.spacingHorizontalM,
    gap: tokens.spacingVerticalXXS,
    overflow: 'hidden',
    justifyContent: 'center',
  },
  title: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  description: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    display: '-webkit-box',
    WebkitLineClamp: 1,
    WebkitBoxOrient: 'vertical' as const,
  },
  assignedTo: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },
  // v1.4.8 — horizontal layout: "Days left" label LEFT of the pill (was
  // stacked vertically). Tighter padding to keep the card compact.
  // v1.4.12 — gap bumped to 5px per UAT ("add 5px space between label and pill").
  badgeColumn: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    gap: '5px',
  },
  badgeLabel: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground3,
    whiteSpace: 'nowrap',
  },
  spinnerOverlay: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    padding: tokens.spacingHorizontalM,
  },
});

const MONTH_ABBREVS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

type DueBadgeColor = 'danger' | 'severe' | 'warning' | 'informative';

/**
 * Tier → colour, following SmartTodo's due badge palette (owner decision
 * 2026-10-05, task 081 / H1): overdue red · 3d dark orange · 7d yellow ·
 * 10d and none neutral. The tier itself arrives as the `urgency` prop, computed
 * by the caller with the shared `dueUrgencyForDays` (3/7/10 calendar days), so
 * this package keeps NO boundary copy. Before task 081 this card computed its
 * own tiers (red < 3 days, yellow ≤ 5, green otherwise).
 */
export const EVENT_DUE_BADGE_COLOR: Record<EventDueUrgency, DueBadgeColor> = {
  overdue: 'danger',
  '3d': 'severe',
  '7d': 'warning',
  '10d': 'informative',
  none: 'informative',
};

/**
 * Date-column tint per tier (same palette as the badge). `Background2` for the
 * palette tiers so the column aligns with the donut/HSBar palette; neutral
 * `Background3` (as SmartTodo's 10d badge) for 10d and none.
 */
export const EVENT_DUE_DATE_COLUMN_BACKGROUND: Record<EventDueUrgency, string> = {
  overdue: tokens.colorPaletteRedBackground2,
  '3d': tokens.colorPaletteDarkOrangeBackground2,
  '7d': tokens.colorPaletteYellowBackground2,
  '10d': tokens.colorNeutralBackground3,
  none: tokens.colorNeutralBackground3,
};

function getDueBadgeText(daysUntilDue: number, isOverdue: boolean): string {
  if (isOverdue) return String(Math.abs(daysUntilDue));
  if (daysUntilDue === 0) return 'Today';
  return String(daysUntilDue);
}

export const EventDueDateCard: React.FC<IEventDueDateCardProps> = props => {
  const styles = useStyles();

  const handleClick = React.useCallback(() => {
    if (props.onClick && !props.isNavigating) {
      props.onClick(props.eventId);
    }
  }, [props.onClick, props.isNavigating, props.eventId]);

  const handleKeyDown = React.useCallback(
    (e: React.KeyboardEvent) => {
      if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault();
        handleClick();
      }
    },
    [handleClick]
  );

  // Tier-based date column colouring (see EVENT_DUE_DATE_COLUMN_BACKGROUND).
  const dateColumnStyle: React.CSSProperties = { backgroundColor: EVENT_DUE_DATE_COLUMN_BACKGROUND[props.urgency] };

  // v1.4.8 — single-line "DD-MMM-YYYY" format (e.g., "01-JUL-2026") replaces
  // the prior 2-line "DD" + "MMM" stacked layout. Day is zero-padded; month
  // is the 3-letter abbreviation in uppercase.
  const day = String(props.dueDate.getDate()).padStart(2, '0');
  const month = MONTH_ABBREVS[props.dueDate.getMonth()].toUpperCase();
  const year = props.dueDate.getFullYear();
  const dateLabel = `${day}-${month}-${year}`;

  return (
    <Card
      className={mergeClasses(styles.card, props.isNavigating && styles.cardDisabled)}
      onClick={handleClick}
      onKeyDown={handleKeyDown}
      role="button"
      tabIndex={0}
      aria-label={`${props.eventTypeName}: ${props.eventName}, due ${dateLabel}`}
    >
      <div className={styles.dateColumn} style={dateColumnStyle}>
        <span className={styles.dateLabel}>{dateLabel}</span>
      </div>

      <div className={styles.content}>
        <Text className={styles.title} truncate>
          {props.eventTypeName}: {props.eventName}
        </Text>
        {props.description && <Text className={styles.description}>{props.description}</Text>}
        {props.assignedTo && <Text className={styles.assignedTo}>Assigned To: {props.assignedTo}</Text>}
      </div>

      {props.isNavigating ? (
        <div className={styles.spinnerOverlay}>
          <Spinner size="tiny" />
        </div>
      ) : (
        <div className={styles.badgeColumn}>
          <Text className={styles.badgeLabel}>{props.isOverdue ? 'Overdue' : 'Days left'}</Text>
          <Badge appearance="filled" color={EVENT_DUE_BADGE_COLOR[props.urgency]} size="large">
            {getDueBadgeText(props.daysUntilDue, props.isOverdue)}
          </Badge>
        </div>
      )}
    </Card>
  );
};
