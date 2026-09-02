import React, { useCallback, useMemo, useEffect, useState, useRef } from 'react';
import {
  makeStyles,
  tokens,
  Button,
  Card,
  CardHeader,
  Text,
  Body1,
  Switch,
  Spinner,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  MessageBarActions,
  Badge,
  ProgressBar,
  Divider,
  Link,
  mergeClasses,
  Textarea,
  Label,
} from '@fluentui/react-components';
import {
  SaveRegular,
  DocumentRegular,
  ArrowResetRegular,
  CheckmarkCircleRegular,
  ErrorCircleRegular,
  InfoRegular,
  SparkleRegular,
  SearchRegular,
  OpenRegular,
  CopyRegular,
  PersonSearchRegular,
  EditRegular,
} from '@fluentui/react-icons';
import { RelatedToPicker } from './RelatedToPicker';
import { AttachmentSelector } from './AttachmentSelector';
import { ALL_ENTITY_TYPES } from '../hooks/useEntitySearch';
import type { EntitySearchResult, EntityType } from '../hooks/useEntitySearch';
import {
  useSaveFlow,
  type SaveFlowContext,
  type SaveFlowState,
  type ProcessingOptions,
  type JobStatus,
  type StageStatus,
  type UseSaveFlowOptions,
} from '../hooks/useSaveFlow';
import { useAnnounce } from '../hooks/useAnnounce';
import { fetchRelatedCandidates, type RelatedCandidate } from '../services/communicationSuggestionsService';
import { authenticatedJsonFetch } from '@shared/services/authenticatedJsonFetch';
import type { AttachmentInfo, HostType } from '@shared/adapters/types';

/** True inside the browser test harness (taskpane-test.html sets the flag). */
function isBrowserTestMode(): boolean {
  try {
    return (window as unknown as { __SPAARKE_TEST_MODE__?: boolean }).__SPAARKE_TEST_MODE__ === true;
  } catch {
    return false;
  }
}

/**
 * Demo "Related to" candidates for the browser test harness ONLY — the real
 * /suggestions endpoint 404s without a captured email. Mirrors the reconciliation
 * screenshots so the auto-match card UX is iterable.
 */
const DEMO_RELATED_CANDIDATES: RelatedCandidate[] = [
  {
    id: '11111111-1111-1111-1111-111111111111',
    entityType: 'Matter',
    logicalName: 'sprk_matter',
    name: 'Litigation matter',
    displayInfo: 'LITG-763955',
    confidence: 1.0,
    matchReason: 'Sender is on the matter team',
  },
  {
    id: '22222222-2222-2222-2222-222222222222',
    entityType: 'Matter',
    logicalName: 'sprk_matter',
    name: 'Monte Rosa Biotechnology v Spaarke Inc',
    displayInfo: 'LITG-119896',
    confidence: 0.97,
  },
  {
    id: '33333333-3333-3333-3333-333333333333',
    entityType: 'Matter',
    logicalName: 'sprk_matter',
    name: 'Meridian Corp v. Pinnacle Industries',
    displayInfo: 'LITG-226554',
    confidence: 0.92,
  },
  {
    id: '44444444-4444-4444-4444-444444444444',
    entityType: 'Project',
    logicalName: 'sprk_project',
    name: 'Q1 Patent Filing Project',
    displayInfo: 'PROJ-2025-014',
    confidence: 0.88,
  },
];

/**
 * Styles using Fluent UI v9 design tokens (ADR-021).
 */
const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
    width: '100%',
  },
  section: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  sectionTitle: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    color: tokens.colorNeutralForeground2,
    marginBottom: tokens.spacingVerticalXS,
  },
  documentInfo: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    padding: tokens.spacingVerticalS,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
  },
  documentInfoRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  processingOptions: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  processingOption: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    padding: tokens.spacingVerticalXS,
  },
  processingLabel: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  fieldContainer: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  fieldLabel: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
  },
  actions: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    marginTop: tokens.spacingVerticalM,
  },
  footer: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    marginTop: tokens.spacingVerticalM,
  },
  errorActions: {
    display: 'flex',
    gap: tokens.spacingHorizontalS,
    marginTop: tokens.spacingVerticalS,
  },
  jobStatus: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    padding: tokens.spacingVerticalM,
  },
  jobHeader: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  stageList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    marginTop: tokens.spacingVerticalS,
  },
  stageItem: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    padding: tokens.spacingVerticalXS,
  },
  stageIcon: {
    width: '20px',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
  },
  successCard: {
    textAlign: 'center',
    padding: tokens.spacingVerticalL,
  },
  successIcon: {
    fontSize: '48px',
    color: tokens.colorPaletteGreenForeground1,
    marginBottom: tokens.spacingVerticalM,
  },
  successActions: {
    display: 'flex',
    gap: tokens.spacingHorizontalS,
    justifyContent: 'center',
    marginTop: tokens.spacingVerticalM,
  },
  duplicateCard: {
    padding: tokens.spacingVerticalM,
  },
  duplicateActions: {
    display: 'flex',
    gap: tokens.spacingHorizontalS,
    marginTop: tokens.spacingVerticalM,
  },
});

/**
 * Stage display names.
 */
const STAGE_DISPLAY_NAMES: Record<string, string> = {
  RecordsCreated: 'Creating records',
  FileUploaded: 'Uploading file',
  ProfileSummary: 'Generating summary',
  Indexed: 'Indexing for search',
  DeepAnalysis: 'Running AI analysis',
};

/**
 * Stage icon component.
 */
function StageIcon({ status }: { status: StageStatus['status'] }): React.ReactElement {
  switch (status) {
    case 'Completed':
      return <CheckmarkCircleRegular style={{ color: tokens.colorPaletteGreenForeground1 }} />;
    case 'Running':
      return <Spinner size="tiny" />;
    case 'Failed':
      return <ErrorCircleRegular style={{ color: tokens.colorPaletteRedForeground1 }} />;
    case 'Skipped':
      return <span style={{ color: tokens.colorNeutralForeground3 }}>-</span>;
    default:
      return <span style={{ color: tokens.colorNeutralForeground3 }}>-</span>;
  }
}

/**
 * Props for the SaveFlow component.
 */
export interface SaveFlowProps {
  /** Host type (outlook or word) */
  hostType: HostType;
  /** Current item ID */
  itemId?: string;
  /** Display name of the current item */
  itemName?: string;
  /** Available attachments (Outlook only) */
  attachments?: AttachmentInfo[];
  /** Email sender email address (Outlook only) */
  senderEmail?: string;
  /** Email sender display name (Outlook only) */
  senderDisplayName?: string;
  /** Email recipients (Outlook only) */
  recipients?: Array<{
    email: string;
    displayName?: string;
    type: 'to' | 'cc' | 'bcc';
  }>;
  /** Email sent date (Outlook only) */
  sentDate?: Date;
  /** Email body content (Outlook only) */
  emailBody?: string;
  /** Document URL (Word only) */
  documentUrl?: string;
  /** Document content as base64 (Word only) */
  documentContentBase64?: string;
  /** Access token getter */
  getAccessToken: () => Promise<string>;
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
  /** Whether to show document info section */
  showDocumentInfo?: boolean;
  /** Class name for custom styling */
  className?: string;
}

/**
 * SaveFlow component - Complete save workflow UI.
 *
 * Orchestrates the entire save flow from entity selection through job completion:
 * - Entity picker for association selection (required)
 * - Attachment selector (Outlook only)
 * - Processing options toggles
 * - Submit button with validation
 * - Job status tracking with SSE/polling
 * - Duplicate detection handling
 * - Error handling with retry
 * - Success confirmation
 *
 * Implements ADR-021 (Fluent UI v9) and task 055 (accessibility).
 *
 * @example
 * ```tsx
 * <SaveFlow
 *   hostType="outlook"
 *   itemId={email.id}
 *   itemName={email.subject}
 *   attachments={email.attachments}
 *   emailSender={email.sender}
 *   getAccessToken={() => authService.getAccessToken()}
 *   onComplete={(docId, url) => navigateToDocument(url)}
 *   onQuickCreate={(type, query) => openQuickCreateDialog(type, query)}
 * />
 * ```
 */
export function SaveFlow(props: SaveFlowProps): React.ReactElement {
  const {
    hostType,
    itemId,
    itemName,
    attachments = [],
    senderEmail,
    senderDisplayName,
    recipients,
    sentDate,
    emailBody,
    documentUrl,
    documentContentBase64,
    getAccessToken,
    apiBaseUrl = '',
    onComplete,
    onQuickCreate,
    onViewDocument,
    onNavigate,
    allowedEntityTypes,
    showDocumentInfo = true,
    className,
  } = props;

  const styles = useStyles();
  const { announce } = useAnnounce();

  // Initialize save flow hook
  const saveFlowOptions: UseSaveFlowOptions = useMemo(
    () => ({
      apiBaseUrl,
      getAccessToken,
      onComplete: (docId, docUrl) => {
        announce('Document saved successfully', 'polite');
        onComplete?.(docId, docUrl);
      },
      onError: error => {
        announce(`Error: ${error.message}`, 'assertive');
      },
      onDuplicate: (docId, message) => {
        announce('Duplicate detected: ' + message, 'polite');
      },
    }),
    [apiBaseUrl, getAccessToken, onComplete, announce]
  );

  const {
    flowState,
    selectedEntity,
    setSelectedEntity,
    selectedAttachmentIds,
    setSelectedAttachmentIds,
    includeBody,
    setIncludeBody,
    processingOptions,
    toggleProcessingOption,
    jobStatus,
    error,
    clearError,
    duplicateInfo,
    isSaving,
    isValid,
    startSave,
    reset,
    retry,
    savedDocumentId,
    savedDocumentUrl,
  } = useSaveFlow(saveFlowOptions);

  // Local state for document metadata fields
  const [documentName, setDocumentName] = useState<string>('');
  const [documentDescription, setDocumentDescription] = useState<string>('');

  // Auto-match candidates for the "Related to" cards (engine suggestions, ranked
  // highest-first). Replaces the old single pre-selection with the reconciliation-
  // style card list (UI feedback 2026-09-02).
  const [relatedCandidates, setRelatedCandidates] = useState<RelatedCandidate[]>([]);
  const [candidatesLoading, setCandidatesLoading] = useState(false);

  // Build save context
  const buildSaveContext = useCallback(
    (): SaveFlowContext => ({
      hostType,
      itemId,
      itemName,
      documentName: documentName || undefined,
      documentDescription: documentDescription || undefined,
      attachments,
      senderEmail,
      senderDisplayName,
      recipients,
      sentDate,
      emailBody,
      documentUrl,
      documentContentBase64,
    }),
    [
      hostType,
      itemId,
      itemName,
      documentName,
      documentDescription,
      attachments,
      senderEmail,
      senderDisplayName,
      recipients,
      sentDate,
      emailBody,
      documentUrl,
      documentContentBase64,
    ]
  );

  // Handle save button click
  const handleSave = useCallback(() => {
    const context = buildSaveContext();
    startSave(context);
  }, [buildSaveContext, startSave]);

  // Cancel = clear the current selection + fields (wizard "Cancel" pattern).
  const handleCancel = useCallback(() => {
    setSelectedEntity(null);
    setDocumentName('');
    setDocumentDescription('');
    reset();
  }, [setSelectedEntity, reset]);

  // Handle entity selection (Confirm a card / select a search result / Change).
  const handleEntitySelect = useCallback(
    (entity: EntitySearchResult | null) => {
      setSelectedEntity(entity);
      if (entity) {
        announce(`Selected ${entity.entityType}: ${entity.name}`, 'polite');
      }
    },
    [setSelectedEntity, announce]
  );

  // Fetch the engine's ranked "Related to" candidates for the auto-match cards.
  // Reuses the SHARED derivePrimaryReview model (no fork; ADR-045). Outlook only,
  // best-effort: a 404 (email not captured) / failure → no cards (the user searches
  // instead — never an auto-filed guess). The browser test harness seeds demo
  // candidates so the card UX is iterable.
  useEffect(() => {
    if (hostType !== 'outlook' || !itemId) return;
    let cancelled = false;
    setCandidatesLoading(true);
    (async () => {
      try {
        let candidates = await fetchRelatedCandidates(itemId);
        if (candidates.length === 0 && isBrowserTestMode()) {
          candidates = DEMO_RELATED_CANDIDATES;
        }
        if (!cancelled) setRelatedCandidates(candidates);
      } catch {
        if (!cancelled && isBrowserTestMode()) setRelatedCandidates(DEMO_RELATED_CANDIDATES);
      } finally {
        if (!cancelled) setCandidatesLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [hostType, itemId]);

  // "Look up another record" search — scoped to the selected chip type.
  const relatedSearch = useCallback(
    async (query: string, type: EntityType): Promise<EntitySearchResult[]> => {
      if (!apiBaseUrl || !getAccessToken) return [];
      try {
        const token = await getAccessToken();
        const res = await authenticatedJsonFetch(
          `${apiBaseUrl}/api/office/search/entities?q=${encodeURIComponent(query)}&type=${type}&top=10`,
          { headers: { 'Content-Type': 'application/json' } },
          token,
          { getRetryToken: getAccessToken }
        );
        if (!res.ok) return [];
        const data = await res.json();
        const rows = (data.results ?? []) as Array<{
          id: string;
          entityType: string;
          logicalName: string;
          name: string;
          displayInfo?: string;
        }>;
        return rows.map(item => ({
          id: item.id,
          entityType: item.entityType as EntityType,
          logicalName: item.logicalName,
          name: item.name,
          ...(item.displayInfo ? { displayInfo: item.displayInfo } : {}),
        }));
      } catch {
        return [];
      }
    },
    [apiBaseUrl, getAccessToken]
  );

  // "New record" — Slice 3 will wire the BFF-backed create; for now route to the
  // host's Quick Create seam (opens a Dataverse create form) when available.
  const handleCreateNew = useCallback(
    (type: EntityType) => {
      onQuickCreate?.(type, '');
    },
    [onQuickCreate]
  );

  // Handle view document
  const handleViewDocument = useCallback(() => {
    if (savedDocumentUrl) {
      onViewDocument?.(savedDocumentUrl);
    }
  }, [savedDocumentUrl, onViewDocument]);

  // Handle copy link
  const handleCopyLink = useCallback(async () => {
    if (savedDocumentUrl) {
      try {
        await navigator.clipboard.writeText(savedDocumentUrl);
        announce('Link copied to clipboard', 'polite');
      } catch {
        announce('Failed to copy link', 'assertive');
      }
    }
  }, [savedDocumentUrl, announce]);

  // Announce state changes
  useEffect(() => {
    if (flowState === 'uploading') {
      announce('Uploading document...', 'polite');
    } else if (flowState === 'processing') {
      announce('Processing document...', 'polite');
    }
  }, [flowState, announce]);

  // Calculate progress percentage for job status
  const progressPercentage = useMemo(() => {
    if (!jobStatus?.stages) return 0;
    const completed = jobStatus.stages.filter(s => s.status === 'Completed' || s.status === 'Skipped').length;
    return (completed / jobStatus.stages.length) * 100;
  }, [jobStatus]);

  // Render job status section
  const renderJobStatus = () => {
    if (!jobStatus) return null;

    return (
      <Card className={styles.jobStatus}>
        <div className={styles.jobHeader}>
          <Text weight="semibold">Processing</Text>
          <Badge
            appearance="filled"
            color={
              jobStatus.status === 'Completed' ? 'success' : jobStatus.status === 'Failed' ? 'danger' : 'informative'
            }
          >
            {jobStatus.status}
          </Badge>
        </div>

        <ProgressBar value={progressPercentage / 100} />

        <div className={styles.stageList} role="list" aria-label="Processing stages">
          {jobStatus.stages.map(stage => (
            <div
              key={stage.name}
              className={styles.stageItem}
              role="listitem"
              aria-label={`${STAGE_DISPLAY_NAMES[stage.name] || stage.name}: ${stage.status}`}
            >
              <div className={styles.stageIcon}>
                <StageIcon status={stage.status} />
              </div>
              <Text size={200}>{STAGE_DISPLAY_NAMES[stage.name] || stage.name}</Text>
            </div>
          ))}
        </div>
      </Card>
    );
  };

  // Render success state
  const renderSuccessState = () => (
    <Card className={styles.successCard}>
      <CheckmarkCircleRegular className={styles.successIcon} aria-hidden="true" />
      <Text size={500} weight="semibold">
        Document Saved
      </Text>
      <Body1 style={{ marginTop: tokens.spacingVerticalS }}>
        Your document has been saved to Spaarke and associated with{' '}
        <Text weight="semibold">{selectedEntity?.name}</Text>.
      </Body1>
      <div className={styles.successActions}>
        <Button appearance="primary" icon={<OpenRegular />} onClick={handleViewDocument} disabled={!savedDocumentUrl}>
          View Document
        </Button>
        <Button appearance="outline" icon={<CopyRegular />} onClick={handleCopyLink} disabled={!savedDocumentUrl}>
          Copy Link
        </Button>
        <Button appearance="subtle" icon={<ArrowResetRegular />} onClick={reset}>
          Save Another
        </Button>
      </div>
    </Card>
  );

  // Render duplicate state
  const renderDuplicateState = () => (
    <Card className={styles.duplicateCard}>
      <MessageBar intent="info">
        <MessageBarBody>
          <MessageBarTitle>Document Already Saved</MessageBarTitle>
          {duplicateInfo?.message || 'This item was previously saved to this association.'}
        </MessageBarBody>
      </MessageBar>
      <div className={styles.duplicateActions}>
        <Button
          appearance="primary"
          icon={<OpenRegular />}
          onClick={() => duplicateInfo && onViewDocument?.(duplicateInfo.documentId)}
        >
          View Existing Document
        </Button>
        <Button
          appearance="outline"
          onClick={() => {
            setSelectedEntity(null);
            reset();
          }}
        >
          Select Different Entity
        </Button>
      </div>
    </Card>
  );

  // Render error state
  const renderErrorState = () => (
    <MessageBar intent="error">
      <MessageBarBody>
        <MessageBarTitle>{error?.title || 'Error'}</MessageBarTitle>
        {error?.message}
        {error?.action && (
          <Text size={200} style={{ display: 'block', marginTop: tokens.spacingVerticalXS }}>
            {error.action}
          </Text>
        )}
      </MessageBarBody>
      <MessageBarActions>
        {error?.recoverable && (
          <Button appearance="outline" size="small" onClick={retry}>
            Retry
          </Button>
        )}
        <Button appearance="subtle" size="small" onClick={clearError}>
          Dismiss
        </Button>
      </MessageBarActions>
    </MessageBar>
  );

  // Render processing options
  const renderProcessingOptions = () => (
    <div className={styles.section}>
      <div className={styles.sectionTitle}>
        <SparkleRegular />
        <Text weight="semibold">AI Processing</Text>
      </div>
      <Card>
        <div className={styles.processingOptions}>
          <div className={styles.processingOption}>
            <div className={styles.processingLabel}>
              <PersonSearchRegular />
              <Text>Profile Summary</Text>
            </div>
            <Switch
              checked={processingOptions.profileSummary}
              onChange={() => toggleProcessingOption('profileSummary')}
              disabled={isSaving}
              aria-label="Enable profile summary generation"
            />
          </div>

          <Divider />

          <div className={styles.processingOption}>
            <div className={styles.processingLabel}>
              <SearchRegular />
              <Text>Search Index</Text>
            </div>
            <Switch
              checked={processingOptions.ragIndex}
              onChange={() => toggleProcessingOption('ragIndex')}
              disabled={isSaving}
              aria-label="Enable search indexing"
            />
          </div>
        </div>
      </Card>
    </div>
  );

  // Render main form
  const renderForm = () => (
    <>
      {/* Document Info */}
      {showDocumentInfo && itemName && (
        <div className={styles.section}>
          <div className={styles.sectionTitle}>
            <DocumentRegular />
            <Text weight="semibold">{hostType === 'outlook' ? 'Email' : 'Document'}</Text>
          </div>
          <div className={styles.documentInfo}>
            <div className={styles.documentInfoRow}>
              <Text weight="semibold">{itemName}</Text>
            </div>
            {hostType === 'outlook' && (senderDisplayName || senderEmail) && (
              <div className={styles.documentInfoRow}>
                <Text size={200}>From: {senderDisplayName || senderEmail}</Text>
              </div>
            )}
            {hostType === 'outlook' && sentDate && (
              <div className={styles.documentInfoRow}>
                <Text size={200}>Sent: {sentDate.toLocaleDateString()}</Text>
              </div>
            )}
          </div>
        </div>
      )}

      {/* Related to — the RelatedToPicker renders its own header + type chips
          (UI feedback 2026-09-02); reconciliation-style auto-match cards. */}
      <div className={styles.section}>
        <RelatedToPicker
          value={selectedEntity}
          onChange={handleEntitySelect}
          candidates={relatedCandidates}
          candidatesLoading={candidatesLoading}
          onSearch={relatedSearch}
          onCreateNew={handleCreateNew}
          allowedTypes={allowedEntityTypes ?? ALL_ENTITY_TYPES}
          defaultType="Matter"
          disabled={isSaving}
        />
      </div>

      {/* Document Metadata Fields */}
      <div className={styles.section}>
        <div className={styles.sectionTitle}>
          <EditRegular />
          <Text weight="semibold">Document Details</Text>
        </div>
        <Card>
          <div className={styles.fieldContainer}>
            <Label htmlFor="document-name" className={styles.fieldLabel}>
              Document Name
            </Label>
            <Textarea
              id="document-name"
              value={documentName}
              onChange={(e, data) => setDocumentName(data.value)}
              placeholder="Enter document name"
              disabled={isSaving}
              aria-label="Document name"
              rows={2}
            />
          </div>
          <div className={styles.fieldContainer} style={{ marginTop: tokens.spacingVerticalM }}>
            <Label htmlFor="document-description" className={styles.fieldLabel}>
              Description
            </Label>
            <Textarea
              id="document-description"
              value={documentDescription}
              onChange={(e, data) => setDocumentDescription(data.value)}
              placeholder="Enter document description (optional)"
              disabled={isSaving}
              aria-label="Document description"
              rows={6}
            />
          </div>
        </Card>
      </div>

      {/* Attachment Selector (Outlook only) */}
      {hostType === 'outlook' && attachments.length > 0 && (
        <AttachmentSelector
          attachments={attachments}
          selectedIds={selectedAttachmentIds}
          onSelectionChange={setSelectedAttachmentIds}
          disabled={isSaving}
          showHeader
          label="Attachments"
        />
      )}

      {/* AI processing (Profile Summary + Search Index) is mandatory for all content
          saved to Spaarke — always on (DEFAULT_PROCESSING_OPTIONS), no toggles
          (UI feedback 2026-09-02). */}

      {/* Footer actions — wizard pattern: Cancel (left), Save (right). */}
      <div className={styles.footer}>
        <Button appearance="secondary" onClick={handleCancel} disabled={isSaving}>
          Cancel
        </Button>
        <Button
          appearance="primary"
          icon={isSaving ? <Spinner size="tiny" /> : <SaveRegular />}
          onClick={handleSave}
          disabled={isSaving || !isValid}
        >
          {isSaving ? 'Saving...' : 'Save'}
        </Button>
      </div>
    </>
  );

  // Determine what to render based on flow state
  const renderContent = () => {
    switch (flowState) {
      case 'complete':
        return renderSuccessState();
      case 'duplicate':
        return renderDuplicateState();
      case 'uploading':
      case 'processing':
        return (
          <>
            {renderJobStatus()}
            {flowState === 'uploading' && (
              <Text size={200} style={{ textAlign: 'center' }}>
                Uploading to Spaarke...
              </Text>
            )}
          </>
        );
      case 'error':
        return (
          <>
            {renderErrorState()}
            {renderForm()}
          </>
        );
      case 'idle':
      case 'selecting':
      default:
        return renderForm();
    }
  };

  return (
    <div className={mergeClasses(styles.container, className)} role="form" aria-label="Save to Spaarke">
      {renderContent()}
    </div>
  );
}

export default SaveFlow;
