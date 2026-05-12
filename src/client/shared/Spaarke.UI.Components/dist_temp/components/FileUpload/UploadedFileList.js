/**
 * UploadedFileList.tsx
 * Displays a list of accepted files with type-appropriate icons and remove buttons.
 *
 * Each row shows:
 *   - Type-appropriate icon (PDF / DOCX / XLSX)
 *   - File name (truncated with tooltip)
 *   - Formatted file size (KB / MB)
 *   - Remove button (DismissRegular)
 *
 * All colors via Fluent v9 semantic tokens — zero hardcoded values.
 */
import * as React from 'react';
import { makeStyles, mergeClasses, tokens, Text, Button, Tooltip } from '@fluentui/react-components';
import { DocumentPdfRegular, DocumentRegular, TableRegular, DismissRegular } from '@fluentui/react-icons';
// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------
const useStyles = makeStyles({
    list: {
        listStyle: 'none',
        margin: '0px',
        padding: '0px',
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalXS,
    },
    row: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalS,
        paddingTop: tokens.spacingVerticalXS,
        paddingBottom: tokens.spacingVerticalXS,
        paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalS,
        borderRadius: tokens.borderRadiusMedium,
        backgroundColor: tokens.colorNeutralBackground2,
        borderTopWidth: '1px',
        borderRightWidth: '1px',
        borderBottomWidth: '1px',
        borderLeftWidth: '1px',
        borderTopStyle: 'solid',
        borderRightStyle: 'solid',
        borderBottomStyle: 'solid',
        borderLeftStyle: 'solid',
        borderTopColor: tokens.colorNeutralStroke2,
        borderRightColor: tokens.colorNeutralStroke2,
        borderBottomColor: tokens.colorNeutralStroke2,
        borderLeftColor: tokens.colorNeutralStroke2,
        transition: 'background-color 0.15s ease',
        ':hover': {
            backgroundColor: tokens.colorNeutralBackground3,
        },
    },
    fileIcon: {
        flexShrink: 0,
        color: tokens.colorNeutralForeground3,
        fontSize: '20px',
    },
    fileIconPdf: {
        color: tokens.colorPaletteRedForeground1,
    },
    fileIconDocx: {
        color: tokens.colorPaletteBlueForeground2,
    },
    fileIconXlsx: {
        color: tokens.colorPaletteGreenForeground1,
    },
    fileInfo: {
        display: 'flex',
        flexDirection: 'column',
        flex: '1 1 auto',
        minWidth: 0,
        gap: '1px',
    },
    fileName: {
        color: tokens.colorNeutralForeground1,
        overflow: 'hidden',
        textOverflow: 'ellipsis',
        whiteSpace: 'nowrap',
    },
    fileSize: {
        color: tokens.colorNeutralForeground4,
    },
    removeButton: {
        flexShrink: 0,
        color: tokens.colorNeutralForeground3,
    },
});
// ---------------------------------------------------------------------------
// Utilities
// ---------------------------------------------------------------------------
/** Format bytes into a human-readable string (KB / MB). */
function formatBytes(bytes) {
    if (bytes < 1024)
        return `${bytes} B`;
    if (bytes < 1024 * 1024)
        return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
const FileTypeIcon = ({ fileType }) => {
    const styles = useStyles();
    if (fileType === 'pdf') {
        return React.createElement(DocumentPdfRegular, { className: mergeClasses(styles.fileIcon, styles.fileIconPdf), "aria-hidden": "true" });
    }
    if (fileType === 'xlsx') {
        return React.createElement(TableRegular, { className: mergeClasses(styles.fileIcon, styles.fileIconXlsx), "aria-hidden": "true" });
    }
    // docx (default)
    return React.createElement(DocumentRegular, { className: mergeClasses(styles.fileIcon, styles.fileIconDocx), "aria-hidden": "true" });
};
const FileRow = ({ file, onRemove, disabled }) => {
    const styles = useStyles();
    const handleRemove = React.useCallback(() => {
        onRemove(file.id);
    }, [file.id, onRemove]);
    return (React.createElement("li", { className: styles.row, role: "listitem" },
        React.createElement(FileTypeIcon, { fileType: file.fileType }),
        React.createElement("div", { className: styles.fileInfo },
            React.createElement(Tooltip, { content: file.name, relationship: "label", withArrow: true },
                React.createElement(Text, { size: 200, className: styles.fileName }, file.name)),
            React.createElement(Text, { size: 100, className: styles.fileSize }, formatBytes(file.sizeBytes))),
        React.createElement(Tooltip, { content: `Remove ${file.name}`, relationship: "label", withArrow: true },
            React.createElement(Button, { appearance: "subtle", size: "small", icon: React.createElement(DismissRegular, null), className: styles.removeButton, onClick: handleRemove, "aria-label": `Remove ${file.name}`, disabled: disabled }))));
};
// ---------------------------------------------------------------------------
// UploadedFileList (exported)
// ---------------------------------------------------------------------------
export const UploadedFileList = ({ files, onRemove, disabled = false }) => {
    const styles = useStyles();
    if (files.length === 0) {
        return null;
    }
    return (React.createElement("ol", { className: styles.list, "aria-label": `Uploaded files (${files.length})` }, files.map(file => (React.createElement(FileRow, { key: file.id, file: file, onRemove: onRemove, disabled: disabled })))));
};
//# sourceMappingURL=UploadedFileList.js.map