/**
 * FileUploadZone.tsx
 * Drag-and-drop file upload zone for Step 1 of the Create New Matter wizard.
 *
 * Accepted types: PDF (.pdf), DOCX (.docx), XLSX (.xlsx)
 * Maximum size:   10 MB per file
 *
 * Validation occurs on both drop and click-to-browse selection.
 * Provides visual feedback (border highlight) on dragover.
 * Zero hardcoded colors — all styling via Fluent v9 semantic tokens.
 */
import * as React from 'react';
import { makeStyles, tokens, Text, mergeClasses } from '@fluentui/react-components';
import { ArrowUploadRegular } from '@fluentui/react-icons';
import {
  IFileUploadZoneProps,
  IUploadedFile,
  IFileValidationError,
  UploadedFileType,
  AcceptedMimeType,
} from './wizardTypes';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const MAX_FILE_SIZE_BYTES = 10 * 1024 * 1024; // 10 MB

const ACCEPTED_EXTENSIONS: ReadonlySet<string> = new Set(['.pdf', '.docx', '.xlsx']);

const MIME_TO_FILE_TYPE: ReadonlyMap<AcceptedMimeType, UploadedFileType> = new Map([
  ['application/pdf', 'pdf'],
  [
    'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
    'docx',
  ],
  [
    'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    'xlsx',
  ],
]);

const EXTENSION_TO_FILE_TYPE: ReadonlyMap<string, UploadedFileType> = new Map([
  ['.pdf', 'pdf'],
  ['.docx', 'docx'],
  ['.xlsx', 'xlsx'],
]);

// HTML accept attribute value (used on <input type="file">)
const INPUT_ACCEPT =
  '.pdf,.docx,.xlsx,application/pdf,application/vnd.openxmlformats-officedocument.wordprocessingml.document,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';

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
    fontWeight: '600' as const,
  },
  helpText: {
    color: tokens.colorNeutralForeground4,
    textAlign: 'center',
    marginTop: tokens.spacingVerticalXS,
  },
  // Hidden file input
  hiddenInput: {
    display: 'none',
  },
});

// ---------------------------------------------------------------------------
// Utilities
// ---------------------------------------------------------------------------

/** Derive file extension (lower-cased, with dot) from a file name. */
function getExtension(fileName: string): string {
  const lastDot = fileName.lastIndexOf('.');
  if (lastDot === -1) return '';
  return fileName.slice(lastDot).toLowerCase();
}

/** Format byte count as a human-readable string (KB / MB). */
function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/** Generate a sufficiently unique id for a file entry. */
function generateFileId(): string {
  return `file-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
}

/**
 * Validates a single File object.
 * Returns an IUploadedFile on success, or an IFileValidationError on failure.
 */
function validateFile(
  file: File
): { valid: true; result: IUploadedFile } | { valid: false; error: IFileValidationError } {
  const ext = getExtension(file.name);
  const mimeType = file.type as AcceptedMimeType;

  // Validate by MIME type and extension (both must pass)
  const mimeValid = MIME_TO_FILE_TYPE.has(mimeType);
  const extValid = ACCEPTED_EXTENSIONS.has(ext);

  if (!mimeValid && !extValid) {
    return {
      valid: false,
      error: {
        fileName: file.name,
        reason: `File type not supported. Only PDF, DOCX, and XLSX files are accepted.`,
      },
    };
  }

  // Resolve file type (prefer MIME, fall back to extension)
  const fileType: UploadedFileType | undefined =
    MIME_TO_FILE_TYPE.get(mimeType) ?? EXTENSION_TO_FILE_TYPE.get(ext);

  if (!fileType) {
    return {
      valid: false,
      error: {
        fileName: file.name,
        reason: `File type not supported. Only PDF, DOCX, and XLSX files are accepted.`,
      },
    };
  }

  // Validate size
  if (file.size > MAX_FILE_SIZE_BYTES) {
    return {
      valid: false,
      error: {
        fileName: file.name,
        reason: `File exceeds the 10 MB limit (${formatBytes(file.size)}).`,
      },
    };
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
function processFileList(fileList: FileList | null): {
  accepted: IUploadedFile[];
  errors: IFileValidationError[];
} {
  const accepted: IUploadedFile[] = [];
  const errors: IFileValidationError[] = [];

  if (!fileList) return { accepted, errors };

  Array.from(fileList).forEach((file) => {
    const result = validateFile(file);
    if (result.valid) {
      accepted.push(result.result);
    } else {
      errors.push(result.error);
    }
  });

  return { accepted, errors };
}

// ---------------------------------------------------------------------------
// FileUploadZone component (exported)
// ---------------------------------------------------------------------------

export const FileUploadZone: React.FC<IFileUploadZoneProps> = ({
  onFilesAccepted,
  onValidationErrors,
}) => {
  const styles = useStyles();
  const [isDragOver, setIsDragOver] = React.useState(false);
  const fileInputRef = React.useRef<HTMLInputElement>(null);

  // -------------------------------------------------------------------------
  // Drag-and-drop handlers
  // -------------------------------------------------------------------------

  const handleDragEnter = React.useCallback(
    (e: React.DragEvent<HTMLDivElement>) => {
      e.preventDefault();
      e.stopPropagation();
      setIsDragOver(true);
    },
    []
  );

  const handleDragOver = React.useCallback(
    (e: React.DragEvent<HTMLDivElement>) => {
      e.preventDefault();
      e.stopPropagation();
      // Show "copy" cursor to signal that dropping is allowed
      e.dataTransfer.dropEffect = 'copy';
      setIsDragOver(true);
    },
    []
  );

  const handleDragLeave = React.useCallback(
    (e: React.DragEvent<HTMLDivElement>) => {
      e.preventDefault();
      e.stopPropagation();
      // Only clear if leaving the zone entirely (not entering a child element)
      if (!e.currentTarget.contains(e.relatedTarget as Node)) {
        setIsDragOver(false);
      }
    },
    []
  );

  const handleDrop = React.useCallback(
    (e: React.DragEvent<HTMLDivElement>) => {
      e.preventDefault();
      e.stopPropagation();
      setIsDragOver(false);

      const { accepted, errors } = processFileList(e.dataTransfer.files);

      if (errors.length > 0) {
        onValidationErrors(errors);
      }
      if (accepted.length > 0) {
        onFilesAccepted(accepted);
      }
    },
    [onFilesAccepted, onValidationErrors]
  );

  // -------------------------------------------------------------------------
  // Click-to-browse handler
  // -------------------------------------------------------------------------

  const handleClick = React.useCallback(() => {
    fileInputRef.current?.click();
  }, []);

  const handleKeyDown = React.useCallback(
    (e: React.KeyboardEvent<HTMLDivElement>) => {
      if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault();
        fileInputRef.current?.click();
      }
    },
    []
  );

  const handleInputChange = React.useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      const { accepted, errors } = processFileList(e.target.files);

      if (errors.length > 0) {
        onValidationErrors(errors);
      }
      if (accepted.length > 0) {
        onFilesAccepted(accepted);
      }

      // Reset input so the same file can be re-selected after removal
      e.target.value = '';
    },
    [onFilesAccepted, onValidationErrors]
  );

  // -------------------------------------------------------------------------
  // Render
  // -------------------------------------------------------------------------

  const zoneClass = mergeClasses(styles.zone, isDragOver && styles.zoneDragOver);
  const iconClass = mergeClasses(
    styles.uploadIcon,
    isDragOver && styles.uploadIconActive
  );

  return (
    <>
      {/* Hidden file input */}
      <input
        ref={fileInputRef}
        type="file"
        multiple
        accept={INPUT_ACCEPT}
        className={styles.hiddenInput}
        onChange={handleInputChange}
        aria-hidden="true"
        tabIndex={-1}
      />

      {/* Drop zone */}
      <div
        className={zoneClass}
        role="button"
        tabIndex={0}
        aria-label="Drop files here or click to browse. Accepted file types: PDF, DOCX, XLSX. Maximum 10 MB per file."
        onClick={handleClick}
        onKeyDown={handleKeyDown}
        onDragEnter={handleDragEnter}
        onDragOver={handleDragOver}
        onDragLeave={handleDragLeave}
        onDrop={handleDrop}
      >
        <ArrowUploadRegular className={iconClass} />

        <Text size={300} className={styles.primaryText}>
          Drop files here or{' '}
          <span className={styles.linkText}>click to browse</span>
        </Text>

        <Text size={200} className={styles.helpText}>
          Supported: PDF, DOCX, XLSX (max 10MB each)
        </Text>
      </div>
    </>
  );
};
