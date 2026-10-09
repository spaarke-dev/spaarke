import React, { useState, useEffect, useCallback } from 'react';
import {
  makeStyles,
  tokens,
  Spinner,
  Text,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
} from '@fluentui/react-components';
import { SaveFlow, type SavedDocumentPaneState } from '../SaveFlow';
import type { IHostAdapter } from '@shared/adapters/IHostAdapter';
import type { AttachmentInfo, HostType } from '@shared/adapters/types';
import type { EntityType, EntitySearchResult } from '../../hooks/useEntitySearch';
import {
  writeIdentityStampAfterSave,
  type DocumentIdentityState,
  type ResolvedRelatedRecord,
} from '../../services/documentIdentityService';
import { subscribeToDocumentChanges } from '../../services/documentChangeDetectionService';
import { canOpenSpaarkeRecords } from '../../services/openRecordLauncher';
import type { ContactOption } from './CreateTodoView';
import { EmailContentCaptureContext, type CaptureEmailContent } from '../../hooks/emailContentCaptureContext';
import {
  captureEmailContent as captureEmailContentFromReader,
  hostAdapterEmailReader,
  type SkippedAttachment,
} from '../../services/emailContentCapture';

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
  /** Callback fired once on a successful save with the selected "Related to" record (§C). */
  onSaved?: (entity: EntitySearchResult) => void;
  /** Callback when Quick Create is triggered */
  onQuickCreate?: (entityType: EntityType, searchQuery: string) => void;
  // Task 088 (UAT-1): `onViewDocument` (and its `window.open` fallback, which opened the stored file's Graph
  // webUrl — Word for the web) is REMOVED. SaveFlow's View Document now opens the Spaarke document record.
  /** Callback to navigate to different view */
  onNavigate?: (view: 'save' | 'status') => void;
  /** Entity types allowed for association */
  allowedEntityTypes?: EntityType[];
  /**
   * `sprk_document` id resolved by task 013's FR-01 identity resolution (task 021 / FR-07), from
   * `App.savedContext`. Threaded straight through to `SaveFlow`'s Profile section — this view does
   * not re-resolve identity itself.
   */
  resolvedDocumentId?: string;
  /**
   * The open document's identity state (task 024 / FR-11), from `App`. Threaded straight through to
   * `SaveFlow`, which decides from it whether Save defaults to a new version. `undefined` = identity does
   * not apply (Outlook) → a plain create save, as before.
   */
  documentIdentity?: DocumentIdentityState;
  /** Re-runs identity resolution for the "Check again" / "Try again" actions. */
  onRetryDocumentIdentity?: () => void;
  /** Task 111: the pane filed the open document to a record — `App` puts it into the identity state. */
  onDocumentFiled?: (documentId: string, record: ResolvedRelatedRecord) => void;
  /**
   * task 094: the saved-state bundle lifted to `App.tsx`, threaded straight through to `SaveFlow` so a
   * Save-tab remount (switching to To Do/Find and back) does not lose it. Omitted → `SaveFlow` keeps
   * its own uncontrolled copy, unchanged from before this task.
   */
  savedState?: SavedDocumentPaneState;
  /** The setter half of the lifted bundle above. */
  onSavedStateChange?: React.Dispatch<React.SetStateAction<SavedDocumentPaneState>>;
  /**
   * Task 100: the pane's one contact search (`App.handleSearchContacts`), threaded straight through to `SaveFlow` for
   * the "+ New" form's Assigned To field.
   */
  onSearchContacts?: (query: string) => Promise<ContactOption[]>;
}

/**
 * SaveView component - Container view for the save workflow.
 *
 * This view component:
 * - Initializes the host adapter and retrieves item metadata (subject, sender, recipients, attachments list)
 * - Provides the live email reader the save uses (task 116a) and shows the attachments a save left out
 * - Renders the SaveFlow component with proper context
 * - Handles loading and error states
 *
 * Task 116a: an email's body and its attachments are read here, through Office.js, when a save submits —
 * not fetched by the server from the mailbox through Graph, which cannot reach a B2B guest's home-tenant mailbox.
 * See `services/emailContentCapture.ts`.
 *
 * @example
 * ```tsx
 * <SaveView
 *   hostAdapter={adapter}
 *   getAccessToken={() => authService.getAccessToken()}
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
  onSaved,
  onQuickCreate,
  onNavigate,
  allowedEntityTypes,
  resolvedDocumentId,
  documentIdentity,
  onRetryDocumentIdentity,
  onDocumentFiled,
  savedState,
  onSavedStateChange,
  onSearchContacts,
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
  const [recipients, setRecipients] = useState<
    Array<{ email: string; displayName?: string; type: 'to' | 'cc' | 'bcc' }>
  >([]);
  const [sentDate, setSentDate] = useState<Date | undefined>();
  const [documentUrl, setDocumentUrl] = useState<string | undefined>();
  // Task 116a: the attachments the LAST email save left out (too large for one save, a cloud link, unreadable), each
  // with its reason. Replaced on every save attempt; shown above the form so a left-out file is never silent.
  const [skippedAttachments, setSkippedAttachments] = useState<SkippedAttachment[]>([]);
  // Task 045: document BYTES are deliberately NOT captured into state here. The old mount-time
  // capture (`getDocumentContent` called once in this effect, cached in a `documentContentBase64`
  // state variable) was the defect: every save after the first — an edit made after the tab
  // mounted, a retry, "Save Another" — silently re-uploaded that first snapshot while the pane
  // reported success. `captureDocumentContent` below is a live function instead, invoked by
  // `useSaveFlow.startSave` at the moment each save actually submits (see its doc comment).

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
        setSkippedAttachments([]);

        // Get host type
        const type = hostAdapter.getHostType();
        setHostType(type);

        // Get item ID
        const id = await hostAdapter.getItemId();
        setItemId(id);

        // Get subject/title
        const subject = await hostAdapter.getSubject();
        setItemName(subject);

        // Get host-specific data.
        // task 040 / FR-19 audit: this `type === 'outlook'` scaffold decides WHICH host-specific
        // IHostAdapter methods to call at all (getAttachments/getSenderEmail/getRecipients on
        // Outlook vs getDocumentContent on Word) — the actual value-producing calls inside each
        // branch are ALREADY capability-gated (canGetAttachments/canGetSender/canGetRecipients/
        // canGetDocumentContent). Left as-is (documented in notes/parity-checklist.md) rather than
        // converting the outer scaffold itself, which would only relabel this same branch.
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
            setRecipients(
              recipientList.map(r => ({
                email: r.email,
                type: r.type,
                ...(r.displayName !== undefined ? { displayName: r.displayName } : {}),
              }))
            );
          }

          // Get sent date if available (OutlookAdapter specific)
          if ('getSentDate' in hostAdapter && typeof hostAdapter.getSentDate === 'function') {
            const date = hostAdapter.getSentDate();
            setSentDate(date);
          }

          // Task 116a: the body and attachment CONTENT are not read here — `captureEmailContent` below reads them
          // when a save submits (every attachment; the ones ticked by then become documents).
        } else if (type === 'word') {
          // Word-specific context
          // Document URL is typically the current file path
          setDocumentUrl(id);

          // Task 045: document content is NO LONGER captured here. It used to be read once, on
          // mount, and cached — see the state-removal comment above for why that was the defect.
          // `captureDocumentContent` (below, outside this effect) reads it fresh at save time instead.
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

  // Task 045: reads the CURRENT document bytes, live — never cached. `useSaveFlow.startSave` calls
  // this at the moment a save attempt actually submits (first Save, "Keep both", "Save as new
  // version", a `retry()`, or the next Save after "Save Another"), so every attempt uploads the
  // document as it is right then rather than a snapshot taken when the Save tab mounted. A stable
  // callback (deps: [hostAdapter]) so `SaveFlow`'s `buildSaveContext` always forwards the SAME live
  // function rather than a new one each render.
  const captureDocumentContent = useCallback(async (): Promise<string> => {
    if (!hostAdapter) {
      throw new Error('Document content is required. Please ensure the document is captured before saving.');
    }
    try {
      const content = await hostAdapter.getDocumentContent({ format: 'ooxml' });
      // Convert ArrayBuffer to base64 — the same conversion this package has always done; only WHEN
      // it runs has changed (it used to run once, in the mount effect below — task 025 note M12).
      const uint8Array = new Uint8Array(content);
      let binary = '';
      for (let i = 0; i < uint8Array.length; i++) {
        binary += String.fromCharCode(uint8Array[i] ?? 0);
      }
      return btoa(binary);
    } catch (err) {
      // Normalize to a real Error so useSaveFlow's existing catch (createErrorFromException) surfaces
      // the actual cause (e.g. WordAdapter's typed HostAdapterError message) — a plain
      // `{ code, message }` HostAdapterError is not `instanceof Error` and would otherwise fall
      // through to a generic fallback string.
      const message = err instanceof Error ? err.message : (err as { message?: string } | undefined)?.message;
      throw new Error(message || "Couldn't read the document's current content. Please try again.");
    }
  }, [hostAdapter]);

  // Task 116a: reads the open email's body and its attachments for one save attempt, live — `useSaveFlow`
  // calls it at submit through `EmailContentCaptureContext` (SaveFlow sits between this view and the hook). Gated on
  // the adapter's capability, never a hostType check (NFR-10): `canGetAttachments` is Outlook read mode with Mailbox
  // 1.8 (`getAttachmentContentAsync`). Without it nothing is provided and the save falls back to the server's Graph
  // fetch, unchanged — sending a body there would switch that fetch off and lose the attachments.
  const canCaptureEmailContent = hostAdapter?.getCapabilities().canGetAttachments ?? false;
  const captureEmailContent = useCallback<CaptureEmailContent>(
    async (atts, selectedIds) => {
      if (!hostAdapter) {
        throw new Error("Couldn't read this email, so nothing was saved. Try again.");
      }
      setSkippedAttachments([]);
      const captured = await captureEmailContentFromReader(hostAdapterEmailReader(hostAdapter), atts, selectedIds);
      setSkippedAttachments(captured.skipped);
      return captured;
    },
    [hostAdapter]
  );

  // Task 089 (UAT-9): after EVERY successful pane save — create or version, first save or a later save — mark
  // the open document with the id it was saved as, so its next save (pane or ribbon, now or after reopening the
  // file) resolves it instead of colliding with its own record. The server stamps only the stored copy (task 014).
  // Capability-gated and non-fatal inside `writeIdentityStampAfterSave`; never delays the caller's onComplete.
  const handleComplete = useCallback(
    (documentId: string, documentUrl: string) => {
      if (hostAdapter) {
        void writeIdentityStampAfterSave(hostAdapter, documentId);
      }
      onComplete?.(documentId, documentUrl);
    },
    [hostAdapter, onComplete]
  );

  // Task 094 (owner, 2026-10-04 — "Re-enable on document edits"): registers a content-change handler
  // on the open document for the lifetime of the Save tab (this component), independent of whether a
  // document has been saved yet this session — harmless either way, since `SaveFlow`'s button logic
  // only consults `contentChangedSinceSave` once a `savedDocument` exists. Unmounting the Save tab
  // (switching to To Do/Find) removes the handler (AC3) — edits made while away are simply not
  // observed, which is fine: the AC only requires the SAVED STATE to survive the switch, not that
  // edits made during it are caught. Capability-gated (NFR-10) inside `subscribeToDocumentChanges` —
  // this effect runs unconditionally and no-ops when the adapter can't detect changes at all.
  useEffect(() => {
    if (!hostAdapter || !onSavedStateChange) {
      return undefined;
    }
    return subscribeToDocumentChanges(hostAdapter, () => {
      onSavedStateChange(prev => (prev.contentChangedSinceSave ? prev : { ...prev, contentChangedSinceSave: true }));
    });
  }, [hostAdapter, onSavedStateChange]);

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

  // task 027 / FR-10 (NFR-10): decided from the live adapter's capabilities, never a `hostType`
  // check — `false` (including while `hostAdapter` is absent/loading) renders SaveFlow's
  // related-record card and Document-record affordance without their open action.
  // Task 120: the pane's one open gate (`canOpenSpaarkeRecords`) — ORG_URL plus `openBrowserWindow` OR `window.open`,
  // so View Document / Open Document / the "Filed to" card also open on Office on the web.
  const canOpenRecord = hostAdapter ? canOpenSpaarkeRecords(hostAdapter.getCapabilities()) : false;

  // task 040 / FR-19 (NFR-10): same pattern as canOpenRecord above — decided from the live
  // adapter's capabilities, never a `hostType` check. `false` (including while `hostAdapter` is
  // absent/loading) skips SaveFlow's "Related to" auto-match candidates fetch entirely.
  const canSuggestRelatedRecords = hostAdapter?.getCapabilities().canSuggestRelatedRecords ?? false;

  // task 020 / FR-06 (NFR-10): same pattern — decided from the live adapter's capabilities, never a
  // `hostType` check. `false` (including while `hostAdapter` is absent/loading) leaves the Document
  // Name field as the plain, empty Textarea it was before task 020 — no default, no pencil.
  const canProvideDocumentName = hostAdapter?.getCapabilities().canProvideDocumentName ?? false;

  // task 045 (NFR-10): same pattern as canOpenRecord/canSuggestRelatedRecords/canProvideDocumentName
  // above — decided from the live adapter's capabilities, never a `hostType` check. `false` means
  // SaveFlow gets no capture function at all (Outlook, which never has document bytes; or an adapter
  // that doesn't support it) — it then falls back to whatever plain `documentContentBase64` value a
  // caller supplies, which is none in production.
  const canGetDocumentContent = hostAdapter?.getCapabilities().canGetDocumentContent ?? false;

  // task 094 (NFR-10): same pattern — decided from the live adapter's capabilities, never a `hostType`
  // check. `false` (including while `hostAdapter` is absent/loading) means `SaveFlow` never grays the
  // saved-state button (the owner's "never block a save" rule).
  const canDetectDocumentChanges = hostAdapter?.getCapabilities().canDetectDocumentChanges ?? false;

  // task 094 (NFR-10): PLATFORM, never hostType — gates the collision prompt's "Open in Word" trial.
  const canOpenDesktopWord = hostAdapter?.getCapabilities().canOpenDesktopWord ?? false;

  // Render SaveFlow with context
  const saveFlow = (
    <SaveFlow
      hostType={hostType}
      attachments={attachments}
      getAccessToken={getAccessToken || defaultGetAccessToken}
      showDocumentInfo
      canOpenRecord={canOpenRecord}
      canSuggestRelatedRecords={canSuggestRelatedRecords}
      canProvideDocumentName={canProvideDocumentName}
      canDetectDocumentChanges={canDetectDocumentChanges}
      canOpenDesktopWord={canOpenDesktopWord}
      {...(savedState !== undefined ? { savedState } : {})}
      {...(onSavedStateChange ? { onSavedStateChange } : {})}
      {...(itemId !== undefined ? { itemId } : {})}
      {...(itemName !== undefined ? { itemName } : {})}
      {...(senderEmail !== undefined ? { senderEmail } : {})}
      {...(senderDisplayName !== undefined ? { senderDisplayName } : {})}
      {...(recipients !== undefined ? { recipients } : {})}
      {...(sentDate !== undefined ? { sentDate } : {})}
      {...(documentUrl !== undefined ? { documentUrl } : {})}
      {...(canGetDocumentContent ? { captureDocumentContent } : {})}
      {...(apiBaseUrl !== undefined ? { apiBaseUrl } : {})}
      onComplete={handleComplete}
      {...(onSaved ? { onSaved } : {})}
      {...(onQuickCreate ? { onQuickCreate } : {})}
      {...(onNavigate ? { onNavigate } : {})}
      {...(allowedEntityTypes !== undefined ? { allowedEntityTypes } : {})}
      {...(resolvedDocumentId !== undefined ? { resolvedDocumentId } : {})}
      {...(documentIdentity !== undefined ? { documentIdentity } : {})}
      {...(onRetryDocumentIdentity ? { onRetryDocumentIdentity } : {})}
      {...(onDocumentFiled ? { onDocumentFiled } : {})}
      {...(onSearchContacts ? { onSearchContacts } : {})}
    />
  );

  return (
    <div className={styles.container}>
      {skippedAttachments.length > 0 && (
        <MessageBar intent="warning" layout="multiline" data-testid="save-skipped-attachments">
          <MessageBarBody>
            <MessageBarTitle>
              {skippedAttachments.length === 1
                ? 'One attachment was not saved'
                : `${skippedAttachments.length} attachments were not saved`}
            </MessageBarTitle>
            {skippedAttachments.map(s => (
              <div key={s.attachmentId}>{s.message}</div>
            ))}
          </MessageBarBody>
        </MessageBar>
      )}
      {/* Always rendered (a stable tree — SaveFlow never remounts); `undefined` = no reader, the server path. */}
      <EmailContentCaptureContext.Provider value={canCaptureEmailContent ? captureEmailContent : undefined}>
        {saveFlow}
      </EmailContentCaptureContext.Provider>
    </div>
  );
};

export default SaveView;
