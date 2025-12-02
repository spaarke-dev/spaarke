/**
 * Error Message List
 *
 * Displays upload and record creation errors.
 *
 * ADR Compliance:
 * - ADR-001: Fluent UI v9 Components
 *
 * @version 2.0.0.0
 */

import * as React from 'react';
import {
    MessageBar,
    MessageBarBody,
    MessageBarTitle,
    makeStyles,
    tokens
} from '@fluentui/react-components';

/**
 * Component Props
 */
export interface ErrorMessageListProps {
    /** List of errors */
    errors: Array<{ fileName: string; error: string }>;
}

/**
 * Styles
 */
const useStyles = makeStyles({
    container: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalS
    },
    errorItem: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorPaletteRedForeground1
    }
});

/**
 * Error Message List Component
 */
export const ErrorMessageList: React.FC<ErrorMessageListProps> = ({ errors }) => {
    const styles = useStyles();

    if (errors.length === 0) {
        return null;
    }

    return (
        <MessageBar intent="error">
            <MessageBarBody>
                <MessageBarTitle>
                    {errors.length === 1 ? '1 error occurred' : `${errors.length} errors occurred`}
                </MessageBarTitle>
                <div className={styles.container}>
                    {errors.map((error, index) => (
                        <div key={index} className={styles.errorItem}>
                            • <strong>{error.fileName}:</strong> {error.error}
                        </div>
                    ))}
                </div>
            </MessageBarBody>
        </MessageBar>
    );
};
