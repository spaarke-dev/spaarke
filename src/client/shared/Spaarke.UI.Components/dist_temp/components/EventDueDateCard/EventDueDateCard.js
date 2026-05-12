/**
 * EventDueDateCard - Displays an event due date card with color-coded urgency
 * Standards: ADR-012 (shared components), ADR-021 (Fluent v9 design tokens)
 */
import * as React from 'react';
import { Card, makeStyles, mergeClasses, tokens, Text, Badge, Spinner } from '@fluentui/react-components';
const useStyles = makeStyles({
    card: {
        display: 'flex',
        flexDirection: 'row',
        alignItems: 'stretch',
        cursor: 'pointer',
        minHeight: '80px',
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
        minWidth: '64px',
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
        textTransform: 'uppercase',
    },
    content: {
        display: 'flex',
        flexDirection: 'column',
        flex: 1,
        padding: tokens.spacingVerticalS,
        paddingLeft: tokens.spacingHorizontalM,
        gap: tokens.spacingVerticalXS,
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
        WebkitLineClamp: 2,
        WebkitBoxOrient: 'vertical',
    },
    assignedTo: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground3,
    },
    badgeColumn: {
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        padding: tokens.spacingHorizontalM,
        gap: tokens.spacingVerticalXXS,
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
function getDueBadgeAppearance(daysUntilDue, isOverdue) {
    if (isOverdue || daysUntilDue < 3)
        return 'danger'; // red: overdue or <3 days
    if (daysUntilDue <= 5)
        return 'warning'; // yellow: 3-5 days
    return 'success'; // green: 6+ days
}
/**
 * Get urgency-based background color for the date column.
 * Uses CSS custom properties from Fluent v9 theme for dark mode support.
 */
function getUrgencyDateStyle(daysUntilDue, isOverdue) {
    if (isOverdue || daysUntilDue < 3) {
        return { backgroundColor: 'var(--colorStatusDangerBackground2)' };
    }
    if (daysUntilDue <= 5) {
        return { backgroundColor: 'var(--colorStatusWarningBackground2)' };
    }
    return { backgroundColor: 'var(--colorStatusSuccessBackground2)' };
}
function getDueBadgeText(daysUntilDue, isOverdue) {
    if (isOverdue)
        return String(Math.abs(daysUntilDue));
    if (daysUntilDue === 0)
        return 'Today';
    return String(daysUntilDue);
}
export const EventDueDateCard = props => {
    const styles = useStyles();
    const handleClick = React.useCallback(() => {
        if (props.onClick && !props.isNavigating) {
            props.onClick(props.eventId);
        }
    }, [props.onClick, props.isNavigating, props.eventId]);
    const handleKeyDown = React.useCallback((e) => {
        if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault();
            handleClick();
        }
    }, [handleClick]);
    // Urgency-based date column coloring: <3d red, 3-5d yellow, 6+d green
    const dateColumnStyle = getUrgencyDateStyle(props.daysUntilDue, props.isOverdue);
    const day = props.dueDate.getDate();
    const month = MONTH_ABBREVS[props.dueDate.getMonth()];
    return (React.createElement(Card, { className: mergeClasses(styles.card, props.isNavigating && styles.cardDisabled), onClick: handleClick, onKeyDown: handleKeyDown, role: "button", tabIndex: 0, "aria-label": `${props.eventTypeName}: ${props.eventName}, due ${month} ${day}` },
        React.createElement("div", { className: styles.dateColumn, style: dateColumnStyle },
            React.createElement("span", { className: styles.dateDay }, day),
            React.createElement("span", { className: styles.dateMonth }, month)),
        React.createElement("div", { className: styles.content },
            React.createElement(Text, { className: styles.title, truncate: true },
                props.eventTypeName,
                ": ",
                props.eventName),
            props.description && React.createElement(Text, { className: styles.description }, props.description),
            props.assignedTo && React.createElement(Text, { className: styles.assignedTo },
                "Assigned To: ",
                props.assignedTo)),
        props.isNavigating ? (React.createElement("div", { className: styles.spinnerOverlay },
            React.createElement(Spinner, { size: "tiny" }))) : (React.createElement("div", { className: styles.badgeColumn },
            React.createElement(Text, { className: styles.badgeLabel }, props.isOverdue ? 'Overdue' : 'Days Left'),
            React.createElement(Badge, { appearance: "filled", color: getDueBadgeAppearance(props.daysUntilDue, props.isOverdue), size: "large" }, getDueBadgeText(props.daysUntilDue, props.isOverdue))))));
};
//# sourceMappingURL=EventDueDateCard.js.map