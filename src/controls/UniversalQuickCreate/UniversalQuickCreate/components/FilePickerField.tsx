import * as React from 'react';
import { Field, makeStyles } from '@fluentui/react-components';
import { logger } from '../utils/logger';

const useStyles = makeStyles({
    fileInfo: {
        marginTop: '8px',
        fontSize: '12px',
        color: '#666'
    }
});

export interface FilePickerFieldProps {
    value?: File;
    onChange: (file: File | undefined) => void;
    required?: boolean;
}

/**
 * File Picker Field Component
 *
 * Single file selection for Task 1 baseline.
 * Multi-file support will be added in Task 2A.
 *
 * @param value - Selected file
 * @param onChange - File change callback
 * @param required - Is file selection required
 */
export const FilePickerField: React.FC<FilePickerFieldProps> = ({
    value,
    onChange,
    required = false
}) => {
    const styles = useStyles();

    const handleFileChange = React.useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
        const file = e.target.files?.[0];
        onChange(file);

        if (file) {
            logger.info('FilePickerField', 'File selected', {
                name: file.name,
                size: file.size,
                type: file.type
            });
        }
    }, [onChange]);

    const formatFileSize = (bytes: number): string => {
        if (bytes < 1024) return `${bytes} B`;
        if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
        return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
    };

    return (
        <Field label="Select File" required={required}>
            <input
                type="file"
                onChange={handleFileChange}
                style={{
                    padding: '8px',
                    border: '1px solid #d1d1d1',
                    borderRadius: '4px',
                    width: '100%',
                    fontFamily: "'Segoe UI', Tahoma, Geneva, Verdana, sans-serif",
                    fontSize: '14px'
                }}
            />
            {value && (
                <div className={styles.fileInfo}>
                    {value.name} ({formatFileSize(value.size)})
                </div>
            )}
        </Field>
    );
};
