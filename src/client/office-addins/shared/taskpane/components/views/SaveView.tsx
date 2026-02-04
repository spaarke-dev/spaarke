import React, { useState, useEffect, useCallback } from 'react';
import {
  makeStyles,
  tokens,
  Spinner,
  Text,
} from '@fluentui/react-components';
import { SaveFlow } from '../SaveFlow';
import type { IHostAdapter } from '@shared/adapters/IHostAdapter';
import type { AttachmentInfo, HostType } from '@shared/adapters/types';
import type { EntityType, EntitySearchResult } from '../../hooks/useEntitySearch';

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingVerticalM,
    height: '100%',
    overflow: 'auto',
  },
  loadingContainer: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    padding: tokens.spacingVerticalXXL,
    gap: tokens.spacingVerticalM,
  },
  errorContainer: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    padding: tokens.spacingVerticalXXL,
    color: tokens.colorPaletteRedForeground1,
    textAlign: 'center',
    gap: tokens.spacingVerticalM,
  },
});

/**
 * Props for the SaveView component.
 */
export interface SaveViewProps {
  /** Host adapter for accessing Office.js functionality */
  hostAdapter?: IHostAdapter | null;
  /** Access token getter for API calls */
  getAccessToken?: () => Promise<string>;
  /** API base URL */
  apiBaseUrl?: string;
  /** Callback when save is complete */
  onComplete?: (documentId: string, documentUrl: string) => void;
  /** Callback when Quick Create is triggered */
  onQuickCreate?: (entityType: EntityType, searchQuery: string) => void;
  /** Callback when view document is clicked */
  onViewDocument?: (documentUrl: string) => void;
  /** Callback to navigate to different view */
  onNavigate?: (view: 'save' | 'status') => void;
  /** Entity types allowed for association */
  allowedEntityTypes?: EntityType[];
}

/**
 * SaveView component - Container view for the save workflow.
 *
 * This view component:
 * - Initializes the host adapter and retrieves item metadata (subject, sender, recipients, attachments list)
 * - Fetches attachment metadata for Outlook emails (content retrieved server-side via Graph API)
 * - Renders the SaveFlow component with proper context
 * - Handles loading and error states
 *
 * Note: Email body and attachment content are retrieved server-side via Microsoft Graph API
 * using OBO authentication. This provides more reliable retrieval than Office.js client-side APIs.
 *
 * @example
 * ```tsx
 * <SaveView
 *   hostAdapter={adapter}
 *   getAccessToken={() => authService.getAccessToken(['user_impersonation'])}
 *   onComplete={(docId, url) => navigateToDocument(url)}
 *   onQuickCreate={(type, query) => openQuickCreateDialog(type, query)}
 * />
 * ```
 */
export const SaveView: React.FC<SaveViewProps> = ({
  hostAdapter,
  getAccessToken,
  apiBaseUrl = '',
  onComplete,
  onQuickCreate,
  onViewDocument,
  onNavigate,
  allowedEntityTypes,
}) => {
  const styles = useStyles();

  // State for item context
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [hostType, setHostType] = useState<HostType>('outlook');
  const [itemId, setItemId] = useState<string | undefined>();
  const [itemName, setItemName] = useState<string | undefined>();
  const [attachments, setAttachments] = useState<AttachmentInfo[]>([]);
  const [senderEmail, setSenderEmail] = useState<string | undefined>();
  const [senderDisplayName, setSenderDisplayName] = useState<string | undefined>();
  const [recipients, setRecipients] = useState<Array<{ email: string; displayName?: string; type: 'to' | 'cc' | 'bcc' }>>([]);
  const [sentDate, setSentDate] = useState<Date | undefined>();
  const [documentUrl, setDocumentUrl] = useState<string | undefined>();
  const [documentContentBase64, setDocumentContentBase64] = useState<string | undefined>();
  // Note: emailBody removed - now retrieved server-side via Graph API

  // Load item context from host adapter
  useEffect(() => {
    async function loadContext() {
      if (!hostAdapter) {
        setError('Host adapter not available');
        setIsLoading(false);
        return;
      }

      try {
        setIsLoading(true);
        setError(null);

        // Get host type
        const type = hostAdapter.getHostType();
        setHostType(type);

        // Get item ID
        const id = await hostAdapter.getItemId();
        setItemId(id);

        // Get subject/title
        const subject = await hostAdapter.getSubject();
        setItemName(subject);

        // Get host-specific data
        if (type === 'outlook') {
          // Get attachments
          if (hostAdapter.getCapabilities().canGetAttachments) {
            const atts = await hostAdapter.getAttachments();
            setAttachments(atts);
          }

          // Get sender email and display name
          if (hostAdapter.getCapabilities().canGetSender) {
            const sender = await hostAdapter.getSenderEmail();
            setSenderEmail(sender);

            // Get sender display name if available (OutlookAdapter specific)
            if ('getSenderDisplayName' in hostAdapter && typeof hostAdapter.getSenderDisplayName === 'function') {
              const displayName = await hostAdapter.getSenderDisplayName();
              setSenderDisplayName(displayName);
            }
          }

          // Get recipients
          if (hostAdapter.getCapabilities().canGetRecipients) {
            const recipientList = await hostAdapter.getRecipients();
            setRecipients(recipientList.map(r => ({
              email: r.email,
              displayName: r.displayName,
              type: r.type,
            })));
          }

          // Get sent date if available (OutlookAdapter specific)
          if ('getSentDate' in hostAdapter && typeof hostAdapter.getSentDate === 'function') {
            const date = hostAdapter.getSentDate();
            setSentDate(date);
          }

          // Note: Email body and attachment content are now retrieved server-side via Graph API
          // Client only sends internetMessageId and metadata for reliable, consistent retrieval
        } else if (type === 'word') {
          // Word-specific context
          // Document URL is typically the current file path
          setDocumentUrl(id);

          // Capture document content as base64 for upload
          if (hostAdapter.getCapabilities().canGetDocumentContent) {
            try {
              const content = await hostAdapter.getDocumentContent({ format: 'ooxml' });
              // Convert ArrayBuffer to base64
              const uint8Array = new Uint8Array(content);
              let binary = '';
              for (let i = 0; i < uint8Array.length; i++) {
                binary += String.fromCharCode(uint8Array[i]);
              }
              const base64 = btoa(binary);
              setDocumentContentBase64(base64);
            } catch (err) {
              console.error('Failed to get document content:', err);
              // Don't fail completely - user can still attempt save
            }
          }
        }

        setIsLoading(false);
      } catch (err) {
        console.error('Failed to load context:', err);
        setError(err instanceof Error ? err.message : 'Failed to load document information');
        setIsLoading(false);
      }
    }

    loadContext();
  }, [hostAdapter]);

  // Default token getter if not provided
  const defaultGetAccessToken = useCallback(async (): Promise<string> => {
    // This should never be called in production - the parent component should provide this
    throw new Error('getAccessToken not provided');
  }, []);

  // Handle view document click
  const handleViewDocument = useCallback((url: string) => {
    if (onViewDocument) {
      onViewDocument(url);
    } else {
      // Default behavior: open in new tab
      window.open(url, '_blank');
    }
  }, [onViewDocument]);

  // Loading state
  if (isLoading) {
    return (
      <div className={styles.loadingContainer}>
        <Spinner size="medium" />
        <Text>Loading document information...</Text>
      </div>
    );
  }

  // Error state
  if (error) {
    return (
      <div className={styles.errorContainer}>
        <Text weight="semibold">Unable to load document</Text>
        <Text size={200}>{error}</Text>
      </div>
    );
  }

  // Render SaveFlow with context
  return (
    <div className={styles.container}>
      <SaveFlow
        hostType={hostType}
        itemId={itemId}
        itemName={itemName}
        attachments={attachments}
        senderEmail={senderEmail}
        senderDisplayName={senderDisplayName}
        recipients={recipients}
        sentDate={sentDate}
        documentUrl={documentUrl}
        documentContentBase64={documentContentBase64}
        getAccessToken={getAccessToken || defaultGetAccessToken}
        apiBaseUrl={apiBaseUrl}
        onComplete={onComplete}
        onQuickCreate={onQuickCreate}
        onViewDocument={handleViewDocument}
        onNavigate={onNavigate}
        allowedEntityTypes={allowedEntityTypes}
        showDocumentInfo
      />
    </div>
  );
};

export default SaveView;
