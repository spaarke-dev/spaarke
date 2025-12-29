/**
 * CheckoutStatusBadge Component
 *
 * Displays checkout status indicator when a document is locked by a user.
 * Shows lock icon and user name who has the document checked out.
 */

import * as React from 'react';
import {
    makeStyles,
    shorthands,
    tokens,
    Tooltip,
    Text
} from '@fluentui/react-components';
import { LockClosed16Regular } from '@fluentui/react-icons';
import { CheckoutStatus } from '../types';

const useStyles = makeStyles({
    badge: {
        display: 'flex',
        alignItems: 'center',
        ...shorthands.gap('4px'),
        ...shorthands.padding('4px', '8px'),
        backgroundColor: tokens.colorPaletteYellowBackground2,
        ...shorthands.borderRadius('4px'),
        fontSize: '12px',
        color: tokens.colorPaletteYellowForeground2,
        flexShrink: 0
    },
    badgeDark: {
        backgroundColor: tokens.colorPaletteYellowBackground1,
        color: tokens.colorPaletteYellowForeground1
    },
    icon: {
        flexShrink: 0
    },
    text: {
        overflow: 'hidden',
        textOverflow: 'ellipsis',
        whiteSpace: 'nowrap',
        maxWidth: '150px'
    }
});

export interface CheckoutStatusBadgeProps {
    /** Checkout status from BFF API */
    checkoutStatus: CheckoutStatus | null;
    /** Dark theme mode */
    isDarkTheme?: boolean;
}

/**
 * Format relative time (simple implementation)
 */
function formatTimeAgo(dateString: string | null): string {
    if (!dateString) return '';

    try {
        const date = new Date(dateString);
        const now = new Date();
        const diffMs = now.getTime() - date.getTime();
        const diffMins = Math.floor(diffMs / 60000);
        const diffHours = Math.floor(diffMins / 60);
        const diffDays = Math.floor(diffHours / 24);

        if (diffMins < 1) return 'just now';
        if (diffMins < 60) return `${diffMins}m ago`;
        if (diffHours < 24) return `${diffHours}h ago`;
        if (diffDays < 7) return `${diffDays}d ago`;

        return date.toLocaleDateString();
    } catch {
        return '';
    }
}

export const CheckoutStatusBadge: React.FC<CheckoutStatusBadgeProps> = ({
    checkoutStatus,
    isDarkTheme = false
}) => {
    const styles = useStyles();

    // Don't render if not checked out
    if (!checkoutStatus?.isCheckedOut) {
        return null;
    }

    const userName = checkoutStatus.checkedOutBy?.name || 'Unknown user';
    const timeAgo = formatTimeAgo(checkoutStatus.checkedOutAt);
    const isCurrentUser = checkoutStatus.isCurrentUser;

    const displayText = isCurrentUser ? 'Checked out by you' : `Checked out by ${userName}`;
    const tooltipText = isCurrentUser
        ? `You checked out this document${timeAgo ? ` ${timeAgo}` : ''}`
        : `${userName} has this document locked${timeAgo ? ` (${timeAgo})` : ''}`;

    return (
        <Tooltip content={tooltipText} relationship="description">
            <div className={`${styles.badge} ${isDarkTheme ? styles.badgeDark : ''}`}>
                <LockClosed16Regular className={styles.icon} />
                <Text size={200} className={styles.text}>
                    {displayText}
                </Text>
            </div>
        </Tooltip>
    );
};

export default CheckoutStatusBadge;
