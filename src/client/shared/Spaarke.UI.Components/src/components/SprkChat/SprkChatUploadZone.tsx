/**
 * SprkChatUploadZone - Drag-and-drop document upload overlay for SprkChat
 *
 * Appears as a full-area overlay when the user drags files over the chat area.
 * Validates file type (PDF, DOCX, TXT, MD) and size (max 50MB), then uploads
 * to the BFF session documents endpoint with progress tracking.
 *
 * Design:
 * - Overlay renders only when `isDragging` is true (controlled by parent)
 * - File type validation uses both MIME type and extension fallback
 * - Upload progress via XMLHttpRequest progress events
 * - All colors via Fluent v9 semantic tokens (dark mode compatible)
 *
 * @see ADR-012 - Shared Component Library; callback-based props
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode
 * @see ADR-022 - React 16 APIs only
 * @see spec-FR-13 - Document upload via drag-and-drop
 */

import * as React from 'react';
import {
  makeStyles,
  shorthands,
  tokens,
  Text,
  Spinner,
  ProgressBar,
  mergeClasses,
} from '@fluentui/react-components';
import {
  DocumentAddRegular,
  DocumentDismissRegular,
  CheckmarkCircleRegular,
  ErrorCircleRegular,
} from '@fluentui/react-icons';

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/** Result of a successful document upload returned by the BFF. */
export interface UploadedDocument {
  /** Server-assigned document identifier. */
  documentId: string;
  /** Original file name. */
  fileName: string;
  /** MIME type of the uploaded file. */
  fileType: string;
  /** Number of pages (available after processing). */
  pageCount?: number;
  /** Processing status: 'processing' while being analyzed, 'ready' when done, 'error' on failure. */
  status: 'processing' | 'ready' | 'error';
}

/** Props for the SprkChatUploadZone component. */
export interface ISprkChatUploadZoneProps {
  /** Active chat session identifier (used in the upload endpoint URL). */
  sessionId: string;
  /** Base URL for the BFF API (e.g., "https://spe-api-dev-67e2xz.azurewebsites.net"). */
  apiBaseUrl: string;
  /** Bearer token for API authentication. */
  accessToken: string;
  /** Callback fired when a document upload completes successfully. */
  onUploadComplete?: (document: UploadedDocument) => void;
  /** Callback fired when an upload fails (validation error or network error). */
  onUploadError?: (error: string) => void;
  /** Whether the upload zone is disabled (e.g., no active session). */
  disabled?: boolean;
}

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

/** Maximum file size in bytes (50 MB). */
const MAX_FILE_SIZE = 52_428_800;

/** Accepted MIME types for upload validation. */
const ACCEPTED_MIME_TYPES = new Set([
  'application/pdf',
  'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  'text/plain',
  'text/markdown',
]);

/** Accepted file extensions (fallback when MIME type is empty, e.g., .md files). */
const ACCEPTED_EXTENSIONS = new Set(['.pdf', '.docx', '.txt', '.md']);

/** Human-readable list of accepted types for the UI. */
const ACCEPTED_TYPES_LABEL = 'PDF, DOCX, TXT, MD';

// ─────────────────────────────────────────────────────────────────────────────
// Styles (ADR-021: Fluent v9 tokens only, dark mode compatible)
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  overlay: {
    position: 'absolute',
    top: 0,
    left: 0,
    right: 0,
    bottom: 0,
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    zIndex: 100,
    backgroundColor: tokens.colorNeutralBackgroundAlpha2,
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    transitionProperty: 'background-color, border-color',
    transitionDuration: tokens.durationNormal,
    transitionTimingFunction: tokens.curveEasyEase,
    pointerEvents: 'auto',
  },

  /** Default drag state — dashed border inviting the drop. */
  overlayDefault: {
    ...shorthands.border('2px', 'dashed', tokens.colorNeutralStroke1),
  },

  /** Active drag state (valid file type hovering) — highlighted brand border. */
  overlayAccepted: {
    ...shorthands.border('2px', 'dashed', tokens.colorBrandStroke1),
    backgroundColor: tokens.colorBrandBackgroundInvertedSelected,
  },

  /** Rejected file type — red/error border. */
  overlayRejected: {
    ...shorthands.border('2px', 'dashed', tokens.colorPaletteRedBorder2),
    backgroundColor: tokens.colorPaletteRedBackground1,
  },

  /** Uploading state — solid brand border, subtle background. */
  overlayUploading: {
    ...shorthands.border('2px', 'solid', tokens.colorBrandStroke1),
    backgroundColor: tokens.colorNeutralBackgroundAlpha2,
  },

  content: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingVerticalM),
    ...shorthands.padding(tokens.spacingVerticalXXL),
    pointerEvents: 'none',
  },

  icon: {
    fontSize: '48px',
    color: tokens.colorBrandForeground1,
  },

  iconError: {
    fontSize: '48px',
    color: tokens.colorPaletteRedForeground1,
  },

  iconSuccess: {
    fontSize: '48px',
    color: tokens.colorPaletteGreenForeground1,
  },

  title: {
    fontSize: tokens.fontSizeBase400,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    textAlign: 'center',
  },

  subtitle: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    textAlign: 'center',
  },

  errorText: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorPaletteRedForeground1,
    textAlign: 'center',
  },

  progressContainer: {
    width: '200px',
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingVerticalXS),
  },

  progressBar: {
    width: '100%',
  },

  progressLabel: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Checks whether a file is an accepted type using MIME type + extension fallback.
 * Some .md files may have an empty MIME type, so extension is checked as backup.
 */
function isAcceptedFileType(file: File): boolean {
  if (ACCEPTED_MIME_TYPES.has(file.type)) {
    return true;
  }
  // Fallback: check extension (handles empty MIME for .md files)
  const dotIndex = file.name.lastIndexOf('.');
  if (dotIndex >= 0) {
    const ext = file.name.substring(dotIndex).toLowerCase();
    return ACCEPTED_EXTENSIONS.has(ext);
  }
  return false;
}

/** Returns the file extension (with dot) or empty string. */
function getExtension(fileName: string): string {
  const dotIndex = fileName.lastIndexOf('.');
  return dotIndex >= 0 ? fileName.substring(dotIndex).toLowerCase() : '';
}

/** Formats bytes into a human-readable string. */
function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

// ─────────────────────────────────────────────────────────────────────────────
// Upload state machine
// ─────────────────────────────────────────────────────────────────────────────

type UploadPhase = 'idle' | 'dragging' | 'rejected' | 'uploading' | 'success' | 'error';

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * SprkChatUploadZone - Drag-and-drop upload overlay for SprkChat.
 *
 * Attach drag event handlers on the parent container and render this component
 * as a child overlay. The component manages its own drag counter to correctly
 * handle nested element dragenter/dragleave events.
 *
 * @example
 * ```tsx
 * <div style={{ position: 'relative' }}
 *   onDragEnter={handleDragEnter}
 *   onDragOver={handleDragOver}
 *   onDragLeave={handleDragLeave}
 *   onDrop={handleDrop}
 * >
 *   {isDragging && (
 *     <SprkChatUploadZone
 *       sessionId={session.sessionId}
 *       apiBaseUrl={apiBaseUrl}
 *       accessToken={accessToken}
 *       onUploadComplete={handleUploadComplete}
 *       onUploadError={handleUploadError}
 *     />
 *   )}
 *   {children}
 * </div>
 * ```
 */
export const SprkChatUploadZone: React.FC<ISprkChatUploadZoneProps> = ({
  sessionId,
  apiBaseUrl,
  accessToken,
  onUploadComplete,
  onUploadError,
  disabled = false,
}) => {
  const styles = useStyles();

  // ── State ──────────────────────────────────────────────────────────────────

  const [phase, setPhase] = React.useState<UploadPhase>('dragging');
  const [uploadProgress, setUploadProgress] = React.useState<number>(0);
  const [errorMessage, setErrorMessage] = React.useState<string | null>(null);
  const [isDragOverValid, setIsDragOverValid] = React.useState<boolean | null>(null);

  /** Drag counter to handle nested element enter/leave correctly. */
  const dragCounterRef = React.useRef<number>(0);

  /** Abort controller ref for cancelling in-flight uploads on unmount. */
  const xhrRef = React.useRef<XMLHttpRequest | null>(null);

  // Clean up in-flight upload on unmount
  React.useEffect(() => {
    return () => {
      if (xhrRef.current) {
        xhrRef.current.abort();
        xhrRef.current = null;
      }
    };
  }, []);

  // ── Drag event handlers ────────────────────────────────────────────────────

  const handleDragEnter = React.useCallback(
    (e: React.DragEvent<HTMLDivElement>) => {
      e.preventDefault();
      e.stopPropagation();
      dragCounterRef.current += 1;

      // Check file type from dataTransfer items (available during dragenter)
      if (e.dataTransfer.items && e.dataTransfer.items.length > 0) {
        const item = e.dataTransfer.items[0];
        if (item.kind === 'file') {
          // During drag, MIME type may be available; extension is not
          const isValid = ACCEPTED_MIME_TYPES.has(item.type) || item.type === '';
          setIsDragOverValid(isValid);
        }
      }
    },
    [],
  );

  const handleDragOver = React.useCallback(
    (e: React.DragEvent<HTMLDivElement>) => {
      e.preventDefault();
      e.stopPropagation();
      // Required to allow drop
      e.dataTransfer.dropEffect = isDragOverValid === false ? 'none' : 'copy';
    },
    [isDragOverValid],
  );

  const handleDragLeave = React.useCallback(
    (e: React.DragEvent<HTMLDivElement>) => {
      e.preventDefault();
      e.stopPropagation();
      dragCounterRef.current -= 1;

      if (dragCounterRef.current <= 0) {
        dragCounterRef.current = 0;
        setIsDragOverValid(null);
        // Reset to default drag state
        if (phase === 'dragging' || phase === 'rejected') {
          setPhase('dragging');
        }
      }
    },
    [phase],
  );

  const handleDrop = React.useCallback(
    (e: React.DragEvent<HTMLDivElement>) => {
      e.preventDefault();
      e.stopPropagation();
      dragCounterRef.current = 0;

      if (disabled) return;

      const files = e.dataTransfer.files;
      if (!files || files.length === 0) return;

      // Take only the first file
      const file = files[0];

      // ── File type validation ────────────────────────────────────────────
      if (!isAcceptedFileType(file)) {
        const ext = getExtension(file.name) || file.type || 'unknown';
        const msg = `Unsupported file type (${ext}). Accepted types: ${ACCEPTED_TYPES_LABEL}`;
        setPhase('rejected');
        setErrorMessage(msg);
        onUploadError?.(msg);
        return;
      }

      // ── File size validation ────────────────────────────────────────────
      if (file.size > MAX_FILE_SIZE) {
        const msg = `File too large (${formatFileSize(file.size)}). Maximum size is 50 MB.`;
        setPhase('error');
        setErrorMessage(msg);
        onUploadError?.(msg);
        return;
      }

      // ── Begin upload ────────────────────────────────────────────────────
      setPhase('uploading');
      setUploadProgress(0);
      setErrorMessage(null);

      const formData = new FormData();
      formData.append('file', file);

      const xhr = new XMLHttpRequest();
      xhrRef.current = xhr;

      // Track upload progress
      xhr.upload.addEventListener('progress', (event) => {
        if (event.lengthComputable) {
          const pct = Math.round((event.loaded / event.total) * 100);
          setUploadProgress(pct);
        }
      });

      xhr.addEventListener('load', () => {
        xhrRef.current = null;
        if (xhr.status >= 200 && xhr.status < 300) {
          setPhase('success');
          try {
            const response = JSON.parse(xhr.responseText) as UploadedDocument;
            onUploadComplete?.(response);
          } catch {
            // If response isn't JSON, create a minimal result
            onUploadComplete?.({
              documentId: '',
              fileName: file.name,
              fileType: file.type,
              status: 'processing',
            });
          }
        } else {
          let msg = `Upload failed (${xhr.status})`;
          try {
            const errorBody = JSON.parse(xhr.responseText);
            if (errorBody?.detail) msg = errorBody.detail;
            else if (errorBody?.title) msg = errorBody.title;
          } catch {
            // Use default message
          }
          setPhase('error');
          setErrorMessage(msg);
          onUploadError?.(msg);
        }
      });

      xhr.addEventListener('error', () => {
        xhrRef.current = null;
        const msg = 'Network error — upload failed. Please check your connection and try again.';
        setPhase('error');
        setErrorMessage(msg);
        onUploadError?.(msg);
      });

      xhr.addEventListener('abort', () => {
        xhrRef.current = null;
        setPhase('dragging');
      });

      const baseUrl = apiBaseUrl.replace(/\/+$/, '').replace(/\/api\/?$/, '');
      const uploadUrl = `${baseUrl}/api/ai/chat/sessions/${encodeURIComponent(sessionId)}/documents`;
      xhr.open('POST', uploadUrl);
      xhr.setRequestHeader('Authorization', `Bearer ${accessToken}`);
      xhr.send(formData);
    },
    [sessionId, apiBaseUrl, accessToken, disabled, onUploadComplete, onUploadError],
  );

  // ── Overlay class based on phase ───────────────────────────────────────────

  const overlayClass = React.useMemo(() => {
    switch (phase) {
      case 'dragging':
        if (isDragOverValid === false) {
          return mergeClasses(styles.overlay, styles.overlayRejected);
        }
        if (isDragOverValid === true) {
          return mergeClasses(styles.overlay, styles.overlayAccepted);
        }
        return mergeClasses(styles.overlay, styles.overlayDefault);
      case 'rejected':
        return mergeClasses(styles.overlay, styles.overlayRejected);
      case 'uploading':
      case 'success':
        return mergeClasses(styles.overlay, styles.overlayUploading);
      case 'error':
        return mergeClasses(styles.overlay, styles.overlayRejected);
      default:
        return mergeClasses(styles.overlay, styles.overlayDefault);
    }
  }, [phase, isDragOverValid, styles]);

  // ── Render ─────────────────────────────────────────────────────────────────

  return (
    <div
      className={overlayClass}
      onDragEnter={handleDragEnter}
      onDragOver={handleDragOver}
      onDragLeave={handleDragLeave}
      onDrop={handleDrop}
      role="region"
      aria-label="File upload drop zone"
      data-testid="sprkchat-upload-zone"
    >
      <div className={styles.content}>
        {/* ── Dragging state ─────────────────────────────────────────── */}
        {phase === 'dragging' && isDragOverValid !== false && (
          <>
            <DocumentAddRegular className={styles.icon} />
            <Text className={styles.title}>Drop to analyze</Text>
            <Text className={styles.subtitle}>
              Accepted: {ACCEPTED_TYPES_LABEL} (max 50 MB)
            </Text>
          </>
        )}

        {/* ── Drag with invalid type ────────────────────────────────── */}
        {phase === 'dragging' && isDragOverValid === false && (
          <>
            <DocumentDismissRegular className={styles.iconError} />
            <Text className={styles.title}>Unsupported file type</Text>
            <Text className={styles.subtitle}>
              Accepted: {ACCEPTED_TYPES_LABEL}
            </Text>
          </>
        )}

        {/* ── Rejected after drop ───────────────────────────────────── */}
        {phase === 'rejected' && (
          <>
            <DocumentDismissRegular className={styles.iconError} />
            <Text className={styles.title}>File not accepted</Text>
            <Text className={styles.errorText}>{errorMessage}</Text>
          </>
        )}

        {/* ── Uploading ─────────────────────────────────────────────── */}
        {phase === 'uploading' && (
          <>
            <Spinner size="medium" label="Uploading..." />
            <div className={styles.progressContainer}>
              <ProgressBar
                className={styles.progressBar}
                value={uploadProgress / 100}
                max={1}
              />
              <Text className={styles.progressLabel}>{uploadProgress}%</Text>
            </div>
          </>
        )}

        {/* ── Upload success ────────────────────────────────────────── */}
        {phase === 'success' && (
          <>
            <CheckmarkCircleRegular className={styles.iconSuccess} />
            <Text className={styles.title}>Upload complete</Text>
            <Text className={styles.subtitle}>Document is being processed...</Text>
          </>
        )}

        {/* ── Upload error ──────────────────────────────────────────── */}
        {phase === 'error' && (
          <>
            <ErrorCircleRegular className={styles.iconError} />
            <Text className={styles.title}>Upload failed</Text>
            <Text className={styles.errorText}>{errorMessage}</Text>
          </>
        )}
      </div>
    </div>
  );
};

export default SprkChatUploadZone;
