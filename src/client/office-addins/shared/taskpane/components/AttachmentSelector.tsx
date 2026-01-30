import React, {
  useState,
  useCallback,
  useMemo,
  forwardRef,
  type KeyboardEvent,
} from 'react';
import {
  makeStyles,
  tokens,
  Checkbox,
  Text,
  Button,
  Badge,
  Card,
  CardHeader,
  Spinner,
  mergeClasses,
} from '@fluentui/react-components';
import {
  AttachRegular,
  DocumentRegular,
  DocumentPdfRegular,
  ImageRegular,
  ArchiveRegular,
  NoteRegular,
  ErrorCircleRegular,
  CheckmarkCircleRegular,
} from '@fluentui/react-icons';
import type { AttachmentInfo } from '@shared/adapters/types';

/**
 * Styles using Fluent UI v9 design tokens (ADR-021).
 */
const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    width: '100%',
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalS,
  },
  headerLeft: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  attachmentCount: {
    marginLeft: 'auto',
  },
  attachmentList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  attachmentItem: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    padding: tokens.spacingVerticalS,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
    cursor: 'pointer',
    transition: 'background-color 0.1s ease-in-out',
    '&:hover': {
      backgroundColor: tokens.colorNeutralBackground2Hover,
    },
    '&:focus-visible': {
      outline: `2px solid ${tokens.colorBrandStroke1}`,
      outlineOffset: '2px',
    },
  },
  attachmentItemDisabled: {
    opacity: 0.6,
    cursor: 'not-allowed',
    '&:hover': {
      backgroundColor: tokens.colorNeutralBackground2,
    },
  },
  attachmentItemError: {
    borderLeft: `3px solid ${tokens.colorPaletteRedBorder2}`,
  },
  attachmentIcon: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: '24px',
    height: '24px',
    color: tokens.colorNeutralForeground2,
  },
  attachmentInfo: {
    display: 'flex',
    flexDirection: 'column',
    flex: 1,
    minWidth: 0,
    overflow: 'hidden',
  },
  attachmentName: {
    fontWeight: tokens.fontWeightSemibold,
    textOverflow: 'ellipsis',
    overflow: 'hidden',
    whiteSpace: 'nowrap',
  },
  attachmentMeta: {
    display: 'flex',
    gap: tokens.spacingHorizontalS,
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  attachmentSize: {
    color: tokens.colorNeutralForeground3,
  },
  attachmentError: {
    color: tokens.colorPaletteRedForeground1,
    fontSize: tokens.fontSizeBase200,
    marginTop: tokens.spacingVerticalXXS,
  },
  selectAllContainer: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginBottom: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  totalSize: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },
  totalSizeWarning: {
    color: tokens.colorPaletteYellowForeground1,
  },
  totalSizeError: {
    color: tokens.colorPaletteRedForeground1,
  },
  loadingContainer: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    padding: tokens.spacingVerticalL,
  },
  emptyState: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    padding: tokens.spacingVerticalL,
    color: tokens.colorNeutralForeground3,
  },
  errorMessage: {
    color: tokens.colorPaletteRedForeground1,
    fontSize: tokens.fontSizeBase200,
    marginTop: tokens.spacingVerticalXS,
  },
});

/**
 * Maximum file size per attachment (25MB per spec).
 */
const MAX_FILE_SIZE = 25 * 1024 * 1024;

/**
 * Maximum total size for all attachments (100MB per spec).
 */
const MAX_TOTAL_SIZE = 100 * 1024 * 1024;

/**
 * Blocked file extensions for security.
 */
const BLOCKED_EXTENSIONS = [
  '.exe', '.dll', '.bat', '.cmd', '.ps1', '.vbs', '.js',
  '.jar', '.msi', '.scr', '.com', '.pif', '.reg',
];

/**
 * Get icon for attachment based on content type.
 */
function getAttachmentIcon(contentType: string, fileName: string): React.ReactElement {
  const lowerName = fileName.toLowerCase();

  if (contentType.startsWith('image/')) {
    return <ImageRegular />;
  }
  if (contentType === 'application/pdf' || lowerName.endsWith('.pdf')) {
    return <DocumentPdfRegular />;
  }
  if (
    contentType.includes('zip') ||
    contentType.includes('rar') ||
    contentType.includes('tar') ||
    contentType.includes('gzip')
  ) {
    return <ArchiveRegular />;
  }
  if (
    contentType.includes('text/') ||
    lowerName.endsWith('.txt') ||
    lowerName.endsWith('.md')
  ) {
    return <NoteRegular />;
  }
  return <DocumentRegular />;
}

/**
 * Format file size for display.
 */
function formatFileSize(bytes: number): string {
  if (bytes === 0) return '0 B';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(1))} ${sizes[i]}`;
}

/**
 * Check if file type is blocked.
 */
function isBlockedFileType(fileName: string): boolean {
  const lowerName = fileName.toLowerCase();
  return BLOCKED_EXTENSIONS.some(ext => lowerName.endsWith(ext));
}

/**
 * Validation result for an attachment.
 */
interface AttachmentValidation {
  isValid: boolean;
  errorMessage?: string;
}

/**
 * Validate an attachment against size and type restrictions.
 */
function validateAttachment(attachment: AttachmentInfo): AttachmentValidation {
  if (isBlockedFileType(attachment.name)) {
    return {
      isValid: false,
      errorMessage: 'This file type is blocked for security reasons',
    };
  }

  if (attachment.size > MAX_FILE_SIZE) {
    return {
      isValid: false,
      errorMessage: `File exceeds ${formatFileSize(MAX_FILE_SIZE)} limit`,
    };
  }

  return { isValid: true };
}

/**
 * Props for the AttachmentSelector component.
 */
export interface AttachmentSelectorProps {
  /** List of available attachments */
  attachments: AttachmentInfo[];
  /** Currently selected attachment IDs */
  selectedIds: Set<string>;
  /** Callback when selection changes */
  onSelectionChange: (selectedIds: Set<string>) => void;
  /** Whether the selector is disabled */
  disabled?: boolean;
  /** Whether attachments are loading */
  isLoading?: boolean;
  /** Error message to display */
  errorMessage?: string;
  /** Optional label for the selector */
  label?: string;
  /** Whether to show the header with count */
  showHeader?: boolean;
  /** Accessible label for screen readers */
  'aria-label'?: string;
  /** Class name for custom styling */
  className?: string;
}

/**
 * AttachmentSelector component for selecting email attachments.
 *
 * Supports:
 * - Checkbox selection for individual attachments
 * - Select all/none functionality
 * - File size validation (25MB per file, 100MB total)
 * - Blocked file type detection
 * - File type icons
 * - Full keyboard navigation
 * - Dark mode and high-contrast support (ADR-021)
 * - WCAG 2.1 AA accessibility
 *
 * @example
 * ```tsx
 * const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
 *
 * <AttachmentSelector
 *   attachments={attachments}
 *   selectedIds={selectedIds}
 *   onSelectionChange={setSelectedIds}
 *   showHeader
 * />
 * ```
 */
export const AttachmentSelector = forwardRef<HTMLDivElement, AttachmentSelectorProps>(
  function AttachmentSelector(props, ref) {
    const {
      attachments,
      selectedIds,
      onSelectionChange,
      disabled = false,
      isLoading = false,
      errorMessage,
      label = 'Attachments',
      showHeader = true,
      'aria-label': ariaLabel,
      className,
    } = props;

    const styles = useStyles();

    // Validate all attachments
    const validationResults = useMemo(() => {
      const results = new Map<string, AttachmentValidation>();
      attachments.forEach(att => {
        results.set(att.id, validateAttachment(att));
      });
      return results;
    }, [attachments]);

    // Get selectable attachments (valid ones only)
    const selectableAttachments = useMemo(() => {
      return attachments.filter(att => validationResults.get(att.id)?.isValid);
    }, [attachments, validationResults]);

    // Calculate total selected size
    const totalSelectedSize = useMemo(() => {
      return attachments
        .filter(att => selectedIds.has(att.id))
        .reduce((sum, att) => sum + att.size, 0);
    }, [attachments, selectedIds]);

    // Check if total size exceeds limit
    const isTotalSizeExceeded = totalSelectedSize > MAX_TOTAL_SIZE;
    const isTotalSizeWarning = totalSelectedSize > MAX_TOTAL_SIZE * 0.8;

    // Check if all selectable are selected
    const allSelected = selectableAttachments.length > 0 &&
      selectableAttachments.every(att => selectedIds.has(att.id));
    const someSelected = selectableAttachments.some(att => selectedIds.has(att.id));

    // Handle individual attachment toggle
    const handleToggle = useCallback((attachmentId: string) => {
      if (disabled) return;

      const validation = validationResults.get(attachmentId);
      if (!validation?.isValid) return;

      const newSelected = new Set(selectedIds);
      if (newSelected.has(attachmentId)) {
        newSelected.delete(attachmentId);
      } else {
        newSelected.add(attachmentId);
      }
      onSelectionChange(newSelected);
    }, [disabled, validationResults, selectedIds, onSelectionChange]);

    // Handle select all toggle
    const handleSelectAll = useCallback(() => {
      if (disabled) return;

      const newSelected = new Set(selectedIds);

      if (allSelected) {
        // Deselect all
        selectableAttachments.forEach(att => {
          newSelected.delete(att.id);
        });
      } else {
        // Select all valid
        selectableAttachments.forEach(att => {
          newSelected.add(att.id);
        });
      }

      onSelectionChange(newSelected);
    }, [disabled, allSelected, selectableAttachments, selectedIds, onSelectionChange]);

    // Handle keyboard navigation
    const handleKeyDown = useCallback((
      event: KeyboardEvent<HTMLDivElement>,
      attachmentId: string
    ) => {
      if (event.key === 'Enter' || event.key === ' ') {
        event.preventDefault();
        handleToggle(attachmentId);
      }
    }, [handleToggle]);

    // Loading state
    if (isLoading) {
      return (
        <Card className={mergeClasses(styles.container, className)} ref={ref}>
          {showHeader && (
            <CardHeader
              image={<AttachRegular />}
              header={<Text weight="semibold">{label}</Text>}
            />
          )}
          <div className={styles.loadingContainer}>
            <Spinner size="small" label="Loading attachments..." />
          </div>
        </Card>
      );
    }

    // Empty state
    if (attachments.length === 0) {
      return (
        <Card className={mergeClasses(styles.container, className)} ref={ref}>
          {showHeader && (
            <CardHeader
              image={<AttachRegular />}
              header={<Text weight="semibold">{label}</Text>}
            />
          )}
          <div className={styles.emptyState}>
            <AttachRegular style={{ fontSize: '32px', marginBottom: tokens.spacingVerticalS }} />
            <Text>No attachments</Text>
          </div>
        </Card>
      );
    }

    return (
      <Card
        className={mergeClasses(styles.container, className)}
        ref={ref}
        role="group"
        aria-label={ariaLabel || label}
      >
        {/* Header with count */}
        {showHeader && (
          <CardHeader
            image={<AttachRegular />}
            header={
              <div className={styles.header}>
                <div className={styles.headerLeft}>
                  <Text weight="semibold">{label}</Text>
                </div>
                <Badge appearance="filled" color="informative" className={styles.attachmentCount}>
                  {selectedIds.size}/{attachments.length}
                </Badge>
              </div>
            }
          />
        )}

        {/* Attachment List */}
        <div
          className={styles.attachmentList}
          role="list"
          aria-label="Attachment list"
        >
          {attachments.map((attachment) => {
            const validation = validationResults.get(attachment.id);
            const isValid = validation?.isValid ?? true;
            const isSelected = selectedIds.has(attachment.id);
            const isDisabled = disabled || !isValid;

            return (
              <div
                key={attachment.id}
                className={mergeClasses(
                  styles.attachmentItem,
                  isDisabled && styles.attachmentItemDisabled,
                  !isValid && styles.attachmentItemError
                )}
                role="listitem"
                tabIndex={isDisabled ? -1 : 0}
                onClick={() => handleToggle(attachment.id)}
                onKeyDown={(e) => handleKeyDown(e, attachment.id)}
                aria-selected={isSelected}
                aria-disabled={isDisabled}
              >
                <Checkbox
                  checked={isSelected}
                  disabled={isDisabled}
                  onChange={() => handleToggle(attachment.id)}
                  aria-label={`Select ${attachment.name}`}
                  tabIndex={-1}
                />

                <div className={styles.attachmentIcon}>
                  {getAttachmentIcon(attachment.contentType, attachment.name)}
                </div>

                <div className={styles.attachmentInfo}>
                  <span className={styles.attachmentName} title={attachment.name}>
                    {attachment.name}
                  </span>
                  <div className={styles.attachmentMeta}>
                    <span className={styles.attachmentSize}>
                      {formatFileSize(attachment.size)}
                    </span>
                    {attachment.isInline && (
                      <Badge appearance="outline" size="small">Inline</Badge>
                    )}
                  </div>
                  {!isValid && validation?.errorMessage && (
                    <span className={styles.attachmentError}>
                      <ErrorCircleRegular style={{ marginRight: '4px' }} />
                      {validation.errorMessage}
                    </span>
                  )}
                </div>

                {isSelected && isValid && (
                  <CheckmarkCircleRegular
                    style={{ color: tokens.colorPaletteGreenForeground1 }}
                    aria-hidden="true"
                  />
                )}
              </div>
            );
          })}
        </div>

        {/* Error Message */}
        {errorMessage && (
          <span className={styles.errorMessage} role="alert">
            {errorMessage}
          </span>
        )}

        {/* Total size warning */}
        {isTotalSizeExceeded && (
          <span className={styles.errorMessage} role="alert">
            Total size exceeds {formatFileSize(MAX_TOTAL_SIZE)} limit. Please uncheck some attachments.
          </span>
        )}
      </Card>
    );
  }
);

export default AttachmentSelector;
