/**
 * Type definitions for the AnalysisWorkspace Code Page
 *
 * Defines API response types, error types, and shared interfaces used across
 * the AnalysisWorkspace services, hooks, and components.
 *
 * @see ADR-019 - ProblemDetails error responses from BFF API
 * @see ADR-007 - Document access through SpeFileStore facade (BFF API)
 */

// ---------------------------------------------------------------------------
// Analysis Record
// ---------------------------------------------------------------------------

/**
 * Analysis record returned from BFF API: GET /api/analyses/{analysisId}
 *
 * Represents a single analysis session including its HTML content,
 * metadata, and relationship to the source document.
 */
export interface AnalysisRecord {
    /** Unique identifier (GUID) of the analysis record */
    id: string;
    /** Display title of the analysis */
    title: string;
    /** HTML content of the analysis output (for RichTextEditor) */
    content: string;
    /** Current analysis status */
    status: AnalysisStatus;
    /** ID of the source document this analysis was generated from */
    sourceDocumentId: string;
    /** ISO 8601 timestamp of creation */
    createdOn: string;
    /** ISO 8601 timestamp of last modification */
    modifiedOn: string;
    /** ID of the action used to generate this analysis (if any) */
    actionId?: string;
    /** ID of the playbook used to generate this analysis (if any) */
    playbookId?: string;
    /** Raw Dataverse statuscode value (1=Draft, 2=Completed, etc.) */
    statusCode?: number;
    /** Display name of the user who created the analysis */
    createdBy?: string;
}

/** Possible statuses for an analysis record */
export type AnalysisStatus =
    | "draft"
    | "in_progress"
    | "completed"
    | "error"
    | "archived";

// ---------------------------------------------------------------------------
// Document Metadata
// ---------------------------------------------------------------------------

/**
 * Document metadata returned from BFF API: GET /api/documents/{documentId}
 *
 * Contains information needed to display the source document in the
 * SourceViewerPanel and show document details in the header.
 */
export interface DocumentMetadata {
    /** Unique identifier (GUID) of the document */
    id: string;
    /** Display name of the document (e.g., "Contract_v2.pdf") */
    name: string;
    /** MIME type of the document (e.g., "application/pdf") */
    mimeType: string;
    /** File size in bytes */
    size: number;
    /** URL for viewing/previewing the document in an iframe */
    viewUrl: string;
    /** File extension without dot (e.g., "pdf", "docx") */
    fileExtension?: string;
    /** SharePoint container ID */
    containerId?: string;
}

// ---------------------------------------------------------------------------
// API Error Types
// ---------------------------------------------------------------------------

/**
 * Structured error from BFF API (RFC 9457 ProblemDetails format).
 *
 * All BFF API error responses use this structure, enabling consistent
 * error handling across the Code Page.
 *
 * @see ADR-019 - ProblemDetails for all errors
 */
export interface AnalysisError {
    /** Machine-readable error code (e.g., "ANALYSIS_NOT_FOUND") */
    errorCode: string;
    /** Human-readable error message */
    message: string;
    /** Detailed error description (may contain technical info) */
    detail?: string;
    /** Correlation ID for tracing through the backend */
    correlationId?: string;
    /** HTTP status code */
    status?: number;
}

/**
 * ProblemDetails response shape from the BFF API.
 * Maps to RFC 9457 standard fields.
 */
export interface ProblemDetails {
    /** URI reference identifying the problem type */
    type?: string;
    /** Short, human-readable summary */
    title?: string;
    /** HTTP status code */
    status?: number;
    /** Human-readable explanation */
    detail?: string;
    /** URI reference identifying the specific occurrence */
    instance?: string;
    /** Machine-readable error code (Spaarke extension) */
    errorCode?: string;
    /** Correlation ID for backend tracing (Spaarke extension) */
    correlationId?: string;
}

// ---------------------------------------------------------------------------
// Save / Export Types
// ---------------------------------------------------------------------------

/** State of an auto-save operation */
export type SaveState = "idle" | "saving" | "saved" | "error";

/** State of an export operation */
export type ExportState = "idle" | "exporting" | "completed" | "error";

/** Supported export formats */
export type ExportFormat = "docx" | "pdf";

// ---------------------------------------------------------------------------
// Host Context
// ---------------------------------------------------------------------------

/**
 * Parameters parsed from the URL when the Code Page is opened.
 * The Code Page is opened via Xrm.Navigation.navigateTo with a data parameter
 * containing URL-encoded key-value pairs.
 */
export interface HostContext {
    /** Analysis session ID to load or resume (required) */
    analysisId: string;
    /** Document ID for the source document (required for viewer) */
    documentId: string;
    /** SharePoint Embedded tenant/container ID */
    tenantId: string;
    /** Theme override: light | dark | highcontrast */
    theme?: string;
}

// ---------------------------------------------------------------------------
// Selection Event Types (for SprkChatBridge)
// ---------------------------------------------------------------------------

/**
 * Payload for selection_changed events emitted to SprkChat.
 * Used by useSelectionBroadcast to communicate editor selections.
 */
export interface SelectionEventPayload {
    /** Plain text of the selected content */
    selectedText: string;
    /** HTML content of the selected content */
    selectedHtml: string;
    /** Viewport-relative bounding rectangle of the selection */
    boundingRect: SelectionBoundingRect;
    /** Identifies the originating panel */
    source: "analysis-editor";
}

/** Viewport-relative bounding rectangle for a text selection */
export interface SelectionBoundingRect {
    top: number;
    left: number;
    width: number;
    height: number;
}

// ---------------------------------------------------------------------------
// Diff Review Types (Task 103)
// ---------------------------------------------------------------------------

/**
 * State for the DiffReviewPanel overlay.
 *
 * When the AI proposes a revision in diff mode, the streaming tokens are
 * collected into a buffer. On stream_end, the DiffReviewPanel opens showing
 * a diff between originalText (current editor content) and proposedText
 * (the buffered AI output). The user can Accept, Reject, or Edit.
 *
 * @see DiffReviewPanel
 * @see useDiffReview hook
 */
export interface DiffReviewState {
    /** Whether the diff review panel is currently open */
    isOpen: boolean;
    /** Current editor content at the time the diff was triggered */
    originalText: string;
    /** AI-proposed revision content (collected from stream tokens) */
    proposedText: string;
    /** Operation ID of the stream that produced this diff */
    operationId: string | null;
}
