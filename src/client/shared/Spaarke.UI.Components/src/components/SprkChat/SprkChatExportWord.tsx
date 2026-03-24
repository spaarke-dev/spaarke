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
import {
  makeStyles,
  Button,
  Spinner,
  Tooltip,
  tokens,
} from '@fluentui/react-components';
import { DocumentArrowUp20Regular } from '@fluentui/react-icons';
import type { IChatMessage } from './types';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

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

/** Shape of the BFF Word export response (matches WordExportResponse.cs). */
interface IExportWordResponse {
  /** SPE file ID of the uploaded DOCX. */
  speFileId: string;
  /** Filename of the generated document. */
  filename: string;
  /** Word Online URL to open in a new tab. */
  wordOnlineUrl: string;
  /** Size of the generated DOCX in bytes. */
  sizeBytes: number;
  /** UTC timestamp when the document was generated. */
  generatedAt: string;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  button: {
    minWidth: 'auto',
  },
  spinnerIcon: {
    width: '20px',
    height: '20px',
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

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
export const SprkChatExportWord: React.FC<ISprkChatExportWordProps> = ({
  sessionId,
  messages,
  apiBaseUrl,
  accessToken,
  onError,
  onSuccess,
}) => {
  const styles = useStyles();
  const [isExporting, setIsExporting] = React.useState(false);

  // Button is disabled when: no sessionId, no assistant content, or currently exporting
  const hasContent = messages.length > 0 && messages.some((m) => m.role === 'Assistant' && m.content.trim().length > 0);
  const hasSession = Boolean(sessionId);
  const isDisabled = isExporting || !hasSession || !hasContent;

  // Determine tooltip text for the disabled state
  const tooltipText = React.useMemo((): string => {
    if (isExporting) return 'Generating document...';
    if (!hasSession) return 'Start a conversation first';
    if (!hasContent) return 'No AI response to export';
    return 'Export conversation to Word Online';
  }, [isExporting, hasSession, hasContent]);

  /**
   * Assemble markdown content from assistant messages for DOCX generation.
   *
   * Concatenates all assistant responses with section breaks so the BFF's
   * DocxExportService can produce a well-structured Word document with
   * headings and paragraphs (not raw chat bubbles).
   */
  const assembleExportContent = React.useCallback((): string => {
    return messages
      .filter((m) => m.role === 'Assistant' && m.content.trim().length > 0)
      .map((m) => m.content.trim())
      .join('\n\n---\n\n');
  }, [messages]);

  /**
   * Generate a timestamped filename for the exported document.
   * Format: "Chat-Export-YYYY-MM-DD.docx"
   */
  const generateFilename = (): string => {
    const now = new Date();
    const dateStr = now.toISOString().slice(0, 10); // YYYY-MM-DD
    return `Chat-Export-${dateStr}.docx`;
  };

  /**
   * Handle the export-to-Word action.
   *
   * POST /api/ai/chat/export/word with { sessionId, content, filename }.
   * BFF resolves the SPE container from the session's HostContext (ADR-013),
   * generates a DOCX, uploads it, and returns a Word Online URL.
   */
  const handleExportToWord = React.useCallback(async () => {
    if (isDisabled || !sessionId) return;

    setIsExporting(true);
    try {
      const content = assembleExportContent();
      if (!content) {
        throw new Error('No content to export');
      }

      const baseUrl = apiBaseUrl.replace(/\/+$/, '');
      const url = `${baseUrl}/api/ai/chat/export/word`;
      const response = await fetch(url, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${accessToken}`,
        },
        body: JSON.stringify({
          sessionId,
          content,
          filename: generateFilename(),
        }),
      });

      if (!response.ok) {
        const errorText = await response.text().catch(() => 'Unknown error');
        throw new Error(`Export failed (${response.status}): ${errorText}`);
      }

      const data: IExportWordResponse = await response.json();

      if (!data.wordOnlineUrl) {
        throw new Error('Export succeeded but no Word Online URL was returned');
      }

      // Open Word Online in a new tab (spec-FR-15: MUST open in new tab)
      window.open(data.wordOnlineUrl, '_blank', 'noopener,noreferrer');
      onSuccess?.(data.wordOnlineUrl);
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message : 'Failed to export to Word';
      onError?.(errorMessage);
    } finally {
      setIsExporting(false);
    }
  }, [isDisabled, sessionId, apiBaseUrl, accessToken, assembleExportContent, onError, onSuccess]);

  // Render the button icon: Spinner when exporting, DocumentArrowUp otherwise
  const buttonIcon = isExporting
    ? React.createElement(Spinner, { size: 'tiny', className: styles.spinnerIcon })
    : React.createElement(DocumentArrowUp20Regular);

  return React.createElement(
    Tooltip,
    {
      content: tooltipText,
      relationship: 'description',
    },
    React.createElement(
      Button,
      {
        className: styles.button,
        appearance: 'subtle',
        icon: buttonIcon,
        disabled: isDisabled,
        onClick: handleExportToWord,
        'aria-label': 'Open in Word',
        size: 'small',
      },
      'Open in Word'
    )
  );
};
