/**
 * Upload Progress Bar
 *
 * Progress indicator for file uploads.
 *
 * ADR Compliance:
 * - ADR-001: Fluent UI v9 Components
 *
 * @version 2.0.0.0
 */

import * as React from 'react';
import {
    ProgressBar,
    Field,
    Text,
    makeStyles,
    tokens
} from '@fluentui/react-components';

/**
 * Component Props
 */
export interface UploadProgressBarProps {
    /** Current file index (1-based) */
    current: number;

    /** Total files */
    total: number;
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
    progressText: {
        fontSize: tokens.fontSizeBase300,
        color: tokens.colorNeutralForeground2,
        textAlign: 'center'
    }
});

/**
 * Upload Progress Bar Component
 */
export const UploadProgressBar: React.FC<UploadProgressBarProps> = ({
    current,
    total
}) => {
    const styles = useStyles();

    // Calculate progress percentage
    const percentage = total > 0 ? (current / total) * 100 : 0;

    // Progress text
    const progressText = `Uploading files... (${current} of ${total} complete)`;

    return (
        <div className={styles.container}>
            <Field>
                <ProgressBar
                    value={percentage / 100}
                    max={1}
                    shape="rounded"
                />
            </Field>
            <Text className={styles.progressText}>
                {progressText}
            </Text>
        </div>
    );
};
