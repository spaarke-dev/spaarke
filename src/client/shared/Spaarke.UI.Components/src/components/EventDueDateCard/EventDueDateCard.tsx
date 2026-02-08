/**
 * EventDueDateCard - Displays an event due date card with color-coded urgency
 * Standards: ADR-012 (shared components), ADR-021 (Fluent v9 design tokens)
 */

import * as React from "react";
import {
  Card,
  makeStyles,
  mergeClasses,
  tokens,
  Text,
  Badge,
  Spinner,
} from "@fluentui/react-components";

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
    display: "flex",
    flexDirection: "row",
    alignItems: "stretch",
    cursor: "pointer",
    minHeight: "80px",
    overflow: "hidden",
    padding: "0",
    ":hover": {
      boxShadow: tokens.shadow8,
    },
  },
  cardDisabled: {
    cursor: "default",
    opacity: 0.7,
  },
  dateColumn: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    minWidth: "64px",
    padding: tokens.spacingVerticalS,
    color: tokens.colorNeutralForeground1,
  },
  dateDay: {
    fontSize: tokens.fontSizeHero700,
    fontWeight: tokens.fontWeightBold,
    lineHeight: tokens.lineHeightHero700,
  },
  dateMonth: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    textTransform: "uppercase" as const,
  },
  content: {
    display: "flex",
    flexDirection: "column",
    flex: 1,
    padding: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    gap: tokens.spacingVerticalXS,
    overflow: "hidden",
    justifyContent: "center",
  },
  title: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
  },
  description: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    overflow: "hidden",
    textOverflow: "ellipsis",
    display: "-webkit-box",
    WebkitLineClamp: 2,
    WebkitBoxOrient: "vertical" as const,
  },
  assignedTo: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },
  badgeColumn: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    padding: tokens.spacingHorizontalM,
    gap: tokens.spacingVerticalXXS,
  },
  badgeLabel: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground3,
    whiteSpace: "nowrap",
  },
  spinnerOverlay: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    padding: tokens.spacingHorizontalM,
  },
});

const MONTH_ABBREVS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

function getDueBadgeAppearance(daysUntilDue: number, isOverdue: boolean): "danger" | "warning" | "important" | "informative" {
  if (isOverdue) return "danger";
  if (daysUntilDue === 0) return "warning";
  if (daysUntilDue <= 3) return "important";
  return "informative";
}

function getDueBadgeText(daysUntilDue: number, isOverdue: boolean): string {
  if (isOverdue) return String(Math.abs(daysUntilDue));
  if (daysUntilDue === 0) return "Today";
  return String(daysUntilDue);
}

export const EventDueDateCard: React.FC<IEventDueDateCardProps> = (props) => {
  const styles = useStyles();

  const handleClick = React.useCallback(() => {
    if (props.onClick && !props.isNavigating) {
      props.onClick(props.eventId);
    }
  }, [props.onClick, props.isNavigating, props.eventId]);

  const handleKeyDown = React.useCallback(
    (e: React.KeyboardEvent) => {
      if (e.key === "Enter" || e.key === " ") {
        e.preventDefault();
        handleClick();
      }
    },
    [handleClick]
  );

  const dateColumnStyle: React.CSSProperties = props.eventTypeColor
    ? { backgroundColor: props.eventTypeColor }
    : {};

  const day = props.dueDate.getDate();
  const month = MONTH_ABBREVS[props.dueDate.getMonth()];

  return (
    <Card
      className={mergeClasses(styles.card, props.isNavigating && styles.cardDisabled)}
      onClick={handleClick}
      onKeyDown={handleKeyDown}
      role="button"
      tabIndex={0}
      aria-label={`${props.eventTypeName}: ${props.eventName}, due ${month} ${day}`}
    >
      <div className={styles.dateColumn} style={dateColumnStyle}>
        <span className={styles.dateDay}>{day}</span>
        <span className={styles.dateMonth}>{month}</span>
      </div>

      <div className={styles.content}>
        <Text className={styles.title} truncate>
          {props.eventTypeName}: {props.eventName}
        </Text>
        {props.description && (
          <Text className={styles.description}>{props.description}</Text>
        )}
        {props.assignedTo && (
          <Text className={styles.assignedTo}>Assigned To: {props.assignedTo}</Text>
        )}
      </div>

      {props.isNavigating ? (
        <div className={styles.spinnerOverlay}>
          <Spinner size="tiny" />
        </div>
      ) : (
        <div className={styles.badgeColumn}>
          <Text className={styles.badgeLabel}>
            {props.isOverdue ? "Overdue" : "Days Left"}
          </Text>
          <Badge
            appearance="filled"
            color={getDueBadgeAppearance(props.daysUntilDue, props.isOverdue)}
            size="large"
          >
            {getDueBadgeText(props.daysUntilDue, props.isOverdue)}
          </Badge>
        </div>
      )}
    </Card>
  );
};
