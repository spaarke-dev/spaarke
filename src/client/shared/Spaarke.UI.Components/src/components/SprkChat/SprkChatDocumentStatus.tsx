/**
 * SprkChatDocumentStatus - Upload processing status message for SprkChat
 *
 * Renders a system-type message showing document upload processing status:
 * - Processing: Spinner + "Processing [filename]..."
 * - Complete: Checkmark + "Document added to context -- [filename], [N] pages"
 *   + optional "Save to matter files" button (when containerId available)
 * - Error: Error icon + error description
 *
 * After 15 seconds without completion (NFR-02), shows an extended wait message.
 *
 * SPE persistence (FR-14):
 * - "Save to matter files" button appears on completed documents when hasContainerId=true
 * - Clicking save shows a spinner on the button (saving state)
 * - On success: button replaced with "Saved -- View in Files" link
 * - On failure: button restored (parent shows error toast)
 * - Save creates a COPY in SPE; session-scoped temp document remains (NFR-06)
 *
 * This component is rendered inline in the chat message stream as a visually
 * distinct system message -- not styled as user or assistant bubbles.
 *
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode required
 * @see ADR-022 - React 16 APIs only
 * @see ADR-012 - Shared Component Library; no Xrm/ComponentFramework imports
 * @see ADR-015 - MUST NOT display extracted document text
 * @see spec-FR-13 - Document upload via drag-and-drop
 * @see spec-FR-14 - Optional SPE persistence for uploaded documents
 * @see spec-NFR-02 - Processing must complete within 15 seconds for <50 pages
 * @see spec-NFR-06 - Save creates a COPY; session document remains
 */

import * as React from 'react';
import {
  makeStyles,
  shorthands,
  tokens,
  Text,
  Spinner,
  Badge,
  Button,
  Link,
} from '@fluentui/react-components';
import {
  CheckmarkCircleRegular,
  ErrorCircleRegular,
  SaveRegular,
} from '@fluentui/react-icons';
import type { ISprkChatDocumentStatusProps } from './types';
import { DOCUMENT_PROCESSING_TIMEOUT_MS } from './types';

// ---------------------------------------------------------------------------
// Styles (ADR-021: Fluent v9 semantic tokens only, dark mode compatible)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  /** Root container -- visually distinct from user/assistant message bubbles. */
  root: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.border('1px', 'solid', tokens.colorNeutralStroke2),
    alignSelf: 'stretch',
    maxWidth: '100%',
  },

  /** Icon container for fixed-width alignment. */
  iconContainer: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    flexShrink: 0,
    width: '20px',
    height: '20px',
  },

  /** Success checkmark icon. */
  successIcon: {
    color: tokens.colorStatusSuccessForeground1,
    fontSize: '20px',
  },

  /** Error icon. */
  errorIcon: {
    color: tokens.colorStatusDangerForeground1,
    fontSize: '20px',
  },

  /** Text content area. */
  content: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    minWidth: 0,
    flex: 1,
  },

  /** Primary status text line. */
  primaryText: {
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
    color: tokens.colorNeutralForeground1,
  },

  /** Secondary detail text (page count, extended wait hint). */
  secondaryText: {
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
    color: tokens.colorNeutralForeground3,
  },

  /** Error detail text. */
  errorText: {
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
    color: tokens.colorStatusDangerForeground1,
  },

  /** Page count badge. */
  pageCountBadge: {
    marginLeft: tokens.spacingHorizontalXS,
  },

  /** Save action row below the complete status text. */
  saveActionRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    marginTop: tokens.spacingVerticalXXS,
  },

  /** Saved confirmation row with link. */
  savedRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    marginTop: tokens.spacingVerticalXXS,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorStatusSuccessForeground1,
  },

  /** Saved checkmark icon (smaller inline variant). */
  savedIcon: {
    color: tokens.colorStatusSuccessForeground1,
    fontSize: '14px',
    flexShrink: 0,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * SprkChatDocumentStatus - Renders document upload processing status.
 *
 * Four states:
 * 1. **Processing** -- Spinner + "Processing [filename]..."
 *    After 15s timeout: adds "Still processing -- large documents may take longer"
 * 2. **Complete** -- CheckmarkCircle + "Document added to context" + page count badge
 *    + optional "Save to matter files" button (when hasContainerId=true)
 * 3. **Saved** -- CheckmarkCircle + "Saved -- View in Files" link (replaces save button)
 * 4. **Error** -- ErrorCircle + "Failed to process document" + error detail
 *
 * @example
 * ```tsx
 * <SprkChatDocumentStatus
 *   status={{
 *     documentId: "abc-123",
 *     fileName: "contract.pdf",
 *     status: "complete",
 *     pageCount: 12,
 *     startedAt: Date.now(),
 *     persistenceState: "idle",
 *   }}
 *   hasContainerId={true}
 *   onSaveToMatterFiles={(docId) => handleSave(docId)}
 * />
 * ```
 */
export const SprkChatDocumentStatus: React.FC<ISprkChatDocumentStatusProps> = ({
  status,
  onSaveToMatterFiles,
  hasContainerId = false,
}) => {
  const styles = useStyles();

  // Track whether the processing timeout has been exceeded (NFR-02: 15s)
  const [isExtendedWait, setIsExtendedWait] = React.useState<boolean>(false);

  React.useEffect(() => {
    // Only run the timer when actively processing
    if (status.status !== 'processing') {
      setIsExtendedWait(false);
      return;
    }

    // Calculate remaining time until timeout threshold
    const elapsed = Date.now() - status.startedAt;
    const remaining = DOCUMENT_PROCESSING_TIMEOUT_MS - elapsed;

    if (remaining <= 0) {
      // Already past the threshold
      setIsExtendedWait(true);
      return;
    }

    const timer = setTimeout(() => {
      setIsExtendedWait(true);
    }, remaining);

    return () => clearTimeout(timer);
  }, [status.status, status.startedAt]);

  /** Handle save button click -- delegates to parent callback. */
  const handleSaveClick = React.useCallback(() => {
    onSaveToMatterFiles?.(status.documentId);
  }, [onSaveToMatterFiles, status.documentId]);

  // Derive persistence state with a safe default
  const persistenceState = status.persistenceState ?? 'idle';
  const isSaving = persistenceState === 'saving';
  const isSaved = persistenceState === 'saved';

  // Show save button when: document is complete, containerId is available,
  // and document has not already been saved
  const showSaveButton = status.status === 'complete'
    && hasContainerId
    && !!onSaveToMatterFiles
    && !isSaved;

  // Show saved confirmation when persistence succeeded
  const showSavedConfirmation = status.status === 'complete' && isSaved;

  // -- Processing state -------------------------------------------------------
  if (status.status === 'processing') {
    return (
      <div
        className={styles.root}
        role="status"
        aria-label={`Processing document: ${status.fileName}`}
        aria-live="polite"
        data-testid="sprkchat-document-status"
        data-document-id={status.documentId}
      >
        <div className={styles.iconContainer}>
          <Spinner size="tiny" />
        </div>
        <div className={styles.content}>
          <Text className={styles.primaryText}>
            Processing {status.fileName}...
          </Text>
          {isExtendedWait && (
            <Text className={styles.secondaryText}>
              Still processing — large documents may take longer
            </Text>
          )}
        </div>
      </div>
    );
  }

  // -- Complete state ---------------------------------------------------------
  if (status.status === 'complete') {
    return (
      <div
        className={styles.root}
        role="status"
        aria-label={`Document added to context: ${status.fileName}`}
        data-testid="sprkchat-document-status"
        data-document-id={status.documentId}
      >
        <div className={styles.iconContainer}>
          <CheckmarkCircleRegular className={styles.successIcon} />
        </div>
        <div className={styles.content}>
          <Text className={styles.primaryText}>
            Document added to context — {status.fileName}
            {status.pageCount != null && status.pageCount > 0 && (
              <Badge
                className={styles.pageCountBadge}
                appearance="tint"
                color="informative"
                size="small"
              >
                {status.pageCount} {status.pageCount === 1 ? 'page' : 'pages'}
              </Badge>
            )}
          </Text>

          {/* Save to matter files button (FR-14) -- only when containerId available */}
          {showSaveButton && (
            <div className={styles.saveActionRow}>
              <Button
                appearance="primary"
                size="small"
                icon={isSaving ? <Spinner size="extra-tiny" /> : <SaveRegular />}
                onClick={handleSaveClick}
                disabled={isSaving}
                data-testid="sprkchat-save-to-matter-files"
              >
                {isSaving ? 'Saving...' : 'Save to matter files'}
              </Button>
            </div>
          )}

          {/* Saved confirmation with link to SharePoint file */}
          {showSavedConfirmation && (
            <div className={styles.savedRow} data-testid="sprkchat-saved-confirmation">
              <CheckmarkCircleRegular className={styles.savedIcon} />
              <span>
                Saved
                {status.savedFileUrl && (
                  <>
                    {' — '}
                    <Link
                      href={status.savedFileUrl}
                      target="_blank"
                      rel="noopener noreferrer"
                      data-testid="sprkchat-view-in-files-link"
                    >
                      View in Files
                    </Link>
                  </>
                )}
              </span>
            </div>
          )}
        </div>
      </div>
    );
  }

  // -- Error state ------------------------------------------------------------
  return (
    <div
      className={styles.root}
      role="alert"
      aria-label={`Failed to process document: ${status.fileName}`}
      data-testid="sprkchat-document-status"
      data-document-id={status.documentId}
    >
      <div className={styles.iconContainer}>
        <ErrorCircleRegular className={styles.errorIcon} />
      </div>
      <div className={styles.content}>
        <Text className={styles.primaryText}>
          Failed to process document — {status.fileName}
        </Text>
        {status.error && (
          <Text className={styles.errorText}>{status.error}</Text>
        )}
      </div>
    </div>
  );
};

export default SprkChatDocumentStatus;
