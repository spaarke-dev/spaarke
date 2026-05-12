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
import type { ISprkChatDocumentStatusProps } from './types';
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
export declare const SprkChatDocumentStatus: React.FC<ISprkChatDocumentStatusProps>;
export default SprkChatDocumentStatus;
//# sourceMappingURL=SprkChatDocumentStatus.d.ts.map