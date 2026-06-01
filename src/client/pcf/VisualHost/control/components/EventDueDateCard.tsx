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

export interface IEventDueDateCardProps {
  eventId: string;
  eventName: string;
  eventTypeName: string;
  dueDate: Date;
  daysUntilDue: number;
  isOverdue: boolean;
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
  // v1.4.8 — horizontal layout: "Days Left" label LEFT of the pill (was
  // stacked vertically). Tighter padding to keep the card compact.
  badgeColumn: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    gap: tokens.spacingHorizontalXS,
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

function getDueBadgeAppearance(daysUntilDue: number, isOverdue: boolean): 'danger' | 'warning' | 'success' {
  if (isOverdue || daysUntilDue < 3) return 'danger'; // red: overdue or <3 days
  if (daysUntilDue <= 5) return 'warning'; // yellow: 3-5 days
  return 'success'; // green: 6+ days
}

/**
 * Get urgency-based background color for the date column.
 * v1.4.7 — switched from `colorStatusXxxBackground2` (pastel tints) to
 * `colorPaletteXxxBackground2` so the date column tints align with the
 * donut/HSBar palette (same `colorPalette*` family the rest of Matter UI
 * uses). Reads cleanly in both light and dark mode.
 */
function getUrgencyDateStyle(daysUntilDue: number, isOverdue: boolean): React.CSSProperties {
  if (isOverdue || daysUntilDue < 3) {
    return { backgroundColor: tokens.colorPaletteRedBackground2 };
  }
  if (daysUntilDue <= 5) {
    return { backgroundColor: tokens.colorPaletteYellowBackground2 };
  }
  return { backgroundColor: tokens.colorPaletteGreenBackground2 };
}

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

  // Urgency-based date column coloring: <3d red, 3-5d yellow, 6+d green
  const dateColumnStyle = getUrgencyDateStyle(props.daysUntilDue, props.isOverdue);

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
          <Text className={styles.badgeLabel}>{props.isOverdue ? 'Overdue' : 'Days Left'}</Text>
          <Badge appearance="filled" color={getDueBadgeAppearance(props.daysUntilDue, props.isOverdue)} size="large">
            {getDueBadgeText(props.daysUntilDue, props.isOverdue)}
          </Badge>
        </div>
      )}
    </Card>
  );
};
