import * as React from 'react';
import { Link, tokens, mergeClasses, makeStyles } from '@fluentui/react-components';
import { openEventDetailPane } from '../utils/sidePaneUtils';
import { logger } from '../utils/logger';

/**
 * Props for HyperlinkCell component
 */
export interface HyperlinkCellProps {
    /** The display text for the hyperlink */
    displayText: string;
    /** The unique ID of the record (Event GUID) */
    recordId: string;
    /** The Event Type GUID (optional) */
    eventType?: string;
    /** Whether the cell is disabled (read-only mode) */
    disabled?: boolean;
    /** Optional callback after side pane opens */
    onSidePaneOpened?: (recordId: string) => void;
}

/**
 * Styles for HyperlinkCell
 */
const useStyles = makeStyles({
    link: {
        fontWeight: tokens.fontWeightRegular,
        textDecoration: 'none',
        cursor: 'pointer',
        ':hover': {
            textDecoration: 'underline',
        },
    },
    disabledText: {
        color: tokens.colorNeutralForeground3,
    },
});

/**
 * HyperlinkCell Component
 *
 * Renders a clickable hyperlink that opens the EventDetailSidePane
 * instead of navigating away from the current page.
 *
 * Used in DatasetGrid for the Event Name column (sprk_eventname).
 *
 * @example
 * ```tsx
 * <HyperlinkCell
 *     displayText="Filing Deadline"
 *     recordId="12345678-1234-1234-1234-123456789012"
 *     eventType="87654321-4321-4321-4321-210987654321"
 * />
 * ```
 *
 * @see tasks/013-add-hyperlink-sidepane.poml
 */
export const HyperlinkCell: React.FC<HyperlinkCellProps> = ({
    displayText,
    recordId,
    eventType,
    disabled = false,
    onSidePaneOpened,
}) => {
    const styles = useStyles();

    /**
     * Handle hyperlink click
     * - Prevents event propagation (so row selection doesn't trigger)
     * - Opens the Event Detail Side Pane with the event data
     */
    const handleClick = React.useCallback(
        async (event: React.MouseEvent<HTMLAnchorElement>) => {
            // Stop propagation to prevent row selection
            event.preventDefault();
            event.stopPropagation();

            if (disabled) {
                logger.debug('HyperlinkCell', 'Click ignored - cell is disabled');
                return;
            }

            logger.info('HyperlinkCell', 'Opening side pane for event', {
                recordId,
                eventType,
            });

            try {
                const result = await openEventDetailPane({
                    eventId: recordId,
                    eventType,
                });

                if (result.success) {
                    logger.info('HyperlinkCell', 'Side pane opened successfully');
                    onSidePaneOpened?.(recordId);
                } else {
                    logger.error('HyperlinkCell', 'Failed to open side pane', {
                        error: result.error,
                    });
                }
            } catch (error) {
                logger.error('HyperlinkCell', 'Exception opening side pane', {
                    error: error instanceof Error ? error.message : String(error),
                });
            }
        },
        [recordId, eventType, disabled, onSidePaneOpened]
    );

    /**
     * Handle keyboard navigation (Enter/Space to activate)
     */
    const handleKeyDown = React.useCallback(
        (event: React.KeyboardEvent<HTMLAnchorElement>) => {
            if (event.key === 'Enter' || event.key === ' ') {
                event.preventDefault();
                event.stopPropagation();
                handleClick(event as unknown as React.MouseEvent<HTMLAnchorElement>);
            }
        },
        [handleClick]
    );

    // If no display text, show empty dash
    if (!displayText || displayText.trim() === '') {
        return <span className={styles.disabledText}>-</span>;
    }

    // If disabled, render as plain text
    if (disabled) {
        return <span className={styles.disabledText}>{displayText}</span>;
    }

    return (
        <Link
            as="a"
            href="#"
            onClick={handleClick}
            onKeyDown={handleKeyDown}
            className={styles.link}
            role="button"
            tabIndex={0}
            aria-label={`Open details for ${displayText}`}
        >
            {displayText}
        </Link>
    );
};

/**
 * Default export for convenient importing
 */
export default HyperlinkCell;
