/**
 * FileUploadZone.tsx
 * Generic drag-and-drop file upload zone for the Spaarke shared component library.
 *
 * Default accepted types: PDF (.pdf), DOCX (.docx), XLSX (.xlsx)
 * Default maximum size:   10 MB per file
 *
 * Consumers can override defaults via the `validationConfig` prop, including
 * accepted extensions, max file size, and a custom validator callback.
 *
 * Provides visual feedback (border highlight) on dragover.
 * Zero hardcoded colors — all styling via Fluent v9 semantic tokens.
 */
import * as React from 'react';
import { makeStyles, tokens, Text, mergeClasses } from '@fluentui/react-components';
import { ArrowUploadRegular } from '@fluentui/react-icons';
// ---------------------------------------------------------------------------
// Default constants
// ---------------------------------------------------------------------------
const DEFAULT_MAX_FILE_SIZE_BYTES = 10 * 1024 * 1024; // 10 MB
const DEFAULT_ACCEPTED_EXTENSIONS = new Set(['.pdf', '.docx', '.xlsx']);
const MIME_TO_FILE_TYPE = new Map([
    ['application/pdf', 'pdf'],
    ['application/vnd.openxmlformats-officedocument.wordprocessingml.document', 'docx'],
    ['application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', 'xlsx'],
]);
const EXTENSION_TO_FILE_TYPE = new Map([
    ['.pdf', 'pdf'],
    ['.docx', 'docx'],
    ['.xlsx', 'xlsx'],
]);
const DEFAULT_INPUT_ACCEPT = '.pdf,.docx,.xlsx,application/pdf,application/vnd.openxmlformats-officedocument.wordprocessingml.document,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';
// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------
const useStyles = makeStyles({
    zone: {
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        gap: tokens.spacingVerticalS,
        borderTopWidth: '2px',
        borderRightWidth: '2px',
        borderBottomWidth: '2px',
        borderLeftWidth: '2px',
        borderTopStyle: 'dashed',
        borderRightStyle: 'dashed',
        borderBottomStyle: 'dashed',
        borderLeftStyle: 'dashed',
        borderTopColor: tokens.colorNeutralStroke1,
        borderRightColor: tokens.colorNeutralStroke1,
        borderBottomColor: tokens.colorNeutralStroke1,
        borderLeftColor: tokens.colorNeutralStroke1,
        borderRadius: tokens.borderRadiusMedium,
        paddingTop: tokens.spacingVerticalXXL,
        paddingBottom: tokens.spacingVerticalXXL,
        paddingLeft: tokens.spacingHorizontalXL,
        paddingRight: tokens.spacingHorizontalXL,
        cursor: 'pointer',
        transition: 'border-color 0.15s ease, background-color 0.15s ease',
        backgroundColor: tokens.colorNeutralBackground2,
        outline: 'none',
        ':focus-visible': {
            outlineWidth: '2px',
            outlineStyle: 'solid',
            outlineColor: tokens.colorBrandStroke1,
            outlineOffset: '2px',
        },
    },
    zoneDisabled: {
        cursor: 'not-allowed',
        opacity: 0.5,
    },
    zoneDragOver: {
        borderTopColor: tokens.colorBrandStroke1,
        borderRightColor: tokens.colorBrandStroke1,
        borderBottomColor: tokens.colorBrandStroke1,
        borderLeftColor: tokens.colorBrandStroke1,
        backgroundColor: tokens.colorBrandBackground2,
    },
    uploadIcon: {
        color: tokens.colorNeutralForeground3,
        fontSize: '32px',
    },
    uploadIconActive: {
        color: tokens.colorBrandForeground1,
    },
    primaryText: {
        color: tokens.colorNeutralForeground1,
        textAlign: 'center',
    },
    linkText: {
        color: tokens.colorBrandForeground1,
        fontWeight: '600',
    },
    helpText: {
        color: tokens.colorNeutralForeground4,
        textAlign: 'center',
        marginTop: tokens.spacingVerticalXS,
    },
    hiddenInput: {
        display: 'none',
    },
});
// ---------------------------------------------------------------------------
// Utilities
// ---------------------------------------------------------------------------
/** Derive file extension (lower-cased, with dot) from a file name. */
function getExtension(fileName) {
    const lastDot = fileName.lastIndexOf('.');
    if (lastDot === -1)
        return '';
    return fileName.slice(lastDot).toLowerCase();
}
/** Format byte count as a human-readable string (KB / MB). */
function formatBytes(bytes) {
    if (bytes < 1024)
        return `${bytes} B`;
    if (bytes < 1024 * 1024)
        return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
/** Generate a sufficiently unique id for a file entry. */
function generateFileId() {
    return `file-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
}
/**
 * Resolve the UploadedFileType from MIME type or extension.
 * Returns undefined if the file type is not recognized.
 */
function resolveFileType(mimeType, ext) {
    return MIME_TO_FILE_TYPE.get(mimeType) ?? EXTENSION_TO_FILE_TYPE.get(ext);
}
/**
 * Build the set of accepted extensions from config, or use defaults.
 */
function getAcceptedExtensions(config) {
    if (config?.acceptedExtensions && config.acceptedExtensions.length > 0) {
        return new Set(config.acceptedExtensions.map(e => e.toLowerCase()));
    }
    return DEFAULT_ACCEPTED_EXTENSIONS;
}
/**
 * Validates a single File object against the provided configuration.
 * Returns an IUploadedFile on success, or an IFileValidationError on failure.
 */
function validateFile(file, config) {
    const ext = getExtension(file.name);
    const mimeType = file.type;
    const acceptedExtensions = getAcceptedExtensions(config);
    const maxSize = config?.maxFileSizeBytes ?? DEFAULT_MAX_FILE_SIZE_BYTES;
    // Validate by MIME type and extension
    const mimeValid = MIME_TO_FILE_TYPE.has(mimeType);
    const extValid = acceptedExtensions.has(ext);
    if (!mimeValid && !extValid) {
        const extList = Array.from(acceptedExtensions)
            .map(e => e.replace('.', '').toUpperCase())
            .join(', ');
        return {
            valid: false,
            error: {
                fileName: file.name,
                reason: `File type not supported. Accepted types: ${extList}.`,
            },
        };
    }
    // Resolve file type (prefer MIME, fall back to extension)
    const fileType = resolveFileType(mimeType, ext);
    if (!fileType) {
        return {
            valid: false,
            error: {
                fileName: file.name,
                reason: `File type not supported.`,
            },
        };
    }
    // Validate size
    if (file.size > maxSize) {
        return {
            valid: false,
            error: {
                fileName: file.name,
                reason: `File exceeds the ${formatBytes(maxSize)} limit (${formatBytes(file.size)}).`,
            },
        };
    }
    // Custom validator
    if (config?.customValidator) {
        const customError = config.customValidator(file);
        if (customError) {
            return {
                valid: false,
                error: {
                    fileName: file.name,
                    reason: customError,
                },
            };
        }
    }
    return {
        valid: true,
        result: {
            id: generateFileId(),
            name: file.name,
            sizeBytes: file.size,
            fileType,
            file,
        },
    };
}
/**
 * Process a FileList (from drop or input change), separating valid files
 * from validation errors.
 */
function processFileList(fileList, config) {
    const accepted = [];
    const errors = [];
    if (!fileList)
        return { accepted, errors };
    Array.from(fileList).forEach(file => {
        const result = validateFile(file, config);
        if (result.valid) {
            accepted.push(result.result);
        }
        else {
            errors.push(result.error);
        }
    });
    return { accepted, errors };
}
/**
 * Build a human-readable description of accepted file types for the help text.
 */
function buildHelpText(config) {
    const extensions = getAcceptedExtensions(config);
    const extList = Array.from(extensions)
        .map(e => e.replace('.', '').toUpperCase())
        .join(', ');
    const maxSize = config?.maxFileSizeBytes ?? DEFAULT_MAX_FILE_SIZE_BYTES;
    return `Supported: ${extList} (max ${formatBytes(maxSize)} each)`;
}
// ---------------------------------------------------------------------------
// FileUploadZone component (exported)
// ---------------------------------------------------------------------------
export const FileUploadZone = ({ onFilesAccepted, onValidationErrors, validationConfig, disabled = false, }) => {
    const styles = useStyles();
    const [isDragOver, setIsDragOver] = React.useState(false);
    const fileInputRef = React.useRef(null);
    // Key forces React to recreate the input element after each selection,
    // avoiding browser quirks where the change event is swallowed on first click.
    const [inputKey, setInputKey] = React.useState(0);
    const inputAccept = validationConfig?.inputAccept ?? DEFAULT_INPUT_ACCEPT;
    const helpText = React.useMemo(() => buildHelpText(validationConfig), [validationConfig]);
    // -------------------------------------------------------------------------
    // Drag-and-drop handlers
    // -------------------------------------------------------------------------
    const handleDragEnter = React.useCallback((e) => {
        e.preventDefault();
        e.stopPropagation();
        if (!disabled)
            setIsDragOver(true);
    }, [disabled]);
    const handleDragOver = React.useCallback((e) => {
        e.preventDefault();
        e.stopPropagation();
        e.dataTransfer.dropEffect = disabled ? 'none' : 'copy';
        if (!disabled)
            setIsDragOver(true);
    }, [disabled]);
    const handleDragLeave = React.useCallback((e) => {
        e.preventDefault();
        e.stopPropagation();
        if (!e.currentTarget.contains(e.relatedTarget)) {
            setIsDragOver(false);
        }
    }, []);
    const handleDrop = React.useCallback((e) => {
        e.preventDefault();
        e.stopPropagation();
        setIsDragOver(false);
        if (disabled)
            return;
        const { accepted, errors } = processFileList(e.dataTransfer.files, validationConfig);
        if (errors.length > 0) {
            onValidationErrors(errors);
        }
        if (accepted.length > 0) {
            onFilesAccepted(accepted);
        }
    }, [onFilesAccepted, onValidationErrors, validationConfig, disabled]);
    // -------------------------------------------------------------------------
    // Click-to-browse handler
    // -------------------------------------------------------------------------
    const handleClick = React.useCallback(() => {
        if (!disabled)
            fileInputRef.current?.click();
    }, [disabled]);
    const handleKeyDown = React.useCallback((e) => {
        if (disabled)
            return;
        if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault();
            fileInputRef.current?.click();
        }
    }, [disabled]);
    const handleInputChange = React.useCallback((e) => {
        const files = e.target.files;
        if (!files || files.length === 0)
            return;
        const { accepted, errors } = processFileList(files, validationConfig);
        if (errors.length > 0) {
            onValidationErrors(errors);
        }
        if (accepted.length > 0) {
            onFilesAccepted(accepted);
        }
        // Force a fresh input element so the same file can be re-selected.
        setInputKey(k => k + 1);
    }, [onFilesAccepted, onValidationErrors, validationConfig]);
    // -------------------------------------------------------------------------
    // Render
    // -------------------------------------------------------------------------
    const zoneClass = mergeClasses(styles.zone, isDragOver && styles.zoneDragOver, disabled && styles.zoneDisabled);
    const iconClass = mergeClasses(styles.uploadIcon, isDragOver && styles.uploadIconActive);
    return (React.createElement(React.Fragment, null,
        React.createElement("input", { key: inputKey, ref: fileInputRef, type: "file", multiple: true, accept: inputAccept, className: styles.hiddenInput, onChange: handleInputChange, "aria-hidden": "true", tabIndex: -1, disabled: disabled }),
        React.createElement("div", { className: zoneClass, role: "button", tabIndex: disabled ? -1 : 0, "aria-label": `Drop files here or click to browse. ${helpText}.`, "aria-disabled": disabled, onClick: handleClick, onKeyDown: handleKeyDown, onDragEnter: handleDragEnter, onDragOver: handleDragOver, onDragLeave: handleDragLeave, onDrop: handleDrop },
            React.createElement(ArrowUploadRegular, { className: iconClass }),
            React.createElement(Text, { size: 300, className: styles.primaryText },
                "Drop files here or ",
                React.createElement("span", { className: styles.linkText }, "click to browse")),
            React.createElement(Text, { size: 200, className: styles.helpText }, helpText))));
};
//# sourceMappingURL=FileUploadZone.js.map