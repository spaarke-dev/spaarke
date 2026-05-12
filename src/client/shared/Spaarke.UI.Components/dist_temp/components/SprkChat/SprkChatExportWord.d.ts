/**
 * SprkChatExportWord - "Open in Word" export action button for SprkChat
 *
 * Standalone component that triggers a Word document export via the BFF,
 * then opens the returned Word Online URL in a new browser tab.
 *
 * Lifecycle:
 *   1. User clicks "Open in Word" button
 *   2. POST /api/ai/chat/export/word with { sessionId, content, filename }
 *   3. BFF generates .docx via DocxExportService, uploads to SPE (container resolved from session HostContext)
 *   4. BFF returns { wordOnlineUrl, speFileId, filename, sizeBytes, generatedAt }
 *   5. Component opens wordOnlineUrl in a new tab
 *
 * Wired into SprkChat.tsx toolbar area (task 057).
 *
 * @see ADR-007 - SpeFileStore facade; BFF resolves container from session HostContext
 * @see ADR-012 - Shared Component Library; callback-based props
 * @see ADR-013 - BFF export endpoint; ChatHostContext flow
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode
 * @see ADR-022 - React 16 APIs only
 * @see spec-FR-15 - Open in Word; Word Online redirect
 */
import * as React from 'react';
import type { IChatMessage } from './types';
/** Props for the SprkChatExportWord component. */
export interface ISprkChatExportWordProps {
    /** Active chat session ID (required for the export API call). */
    sessionId: string | null;
    /** Chat messages used to assemble export content (assistant responses only). */
    messages: IChatMessage[];
    /** Base URL for the BFF API. */
    apiBaseUrl: string;
    /** Bearer token for API authentication. */
    accessToken: string;
    /**
     * Callback fired when export fails.
     * The parent can use this to show a toast or error notification.
     */
    onError?: (error: string) => void;
    /**
     * Callback fired when export succeeds and Word Online URL opens.
     * The parent can use this for analytics or a success notification.
     */
    onSuccess?: (wordOnlineUrl: string) => void;
}
/**
 * "Open in Word" export button for SprkChat toolbar.
 *
 * Calls the BFF Word export endpoint, shows loading state during generation,
 * and opens the Word Online URL in a new tab on success.
 *
 * The BFF resolves the SPE container from the session's ChatHostContext
 * (ADR-007, ADR-013), so the frontend only needs to send sessionId + content.
 *
 * @example
 * ```tsx
 * <SprkChatExportWord
 *   sessionId={session?.sessionId ?? null}
 *   messages={messages}
 *   apiBaseUrl="https://spe-api-dev-67e2xz.azurewebsites.net"
 *   accessToken={token}
 *   onError={(msg) => setExportError(msg)}
 *   onSuccess={(url) => console.log("Opened:", url)}
 * />
 * ```
 */
export declare const SprkChatExportWord: React.FC<ISprkChatExportWordProps>;
//# sourceMappingURL=SprkChatExportWord.d.ts.map