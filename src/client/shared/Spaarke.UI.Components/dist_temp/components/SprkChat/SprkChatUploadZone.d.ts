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
export declare const SprkChatUploadZone: React.FC<ISprkChatUploadZoneProps>;
export default SprkChatUploadZone;
//# sourceMappingURL=SprkChatUploadZone.d.ts.map