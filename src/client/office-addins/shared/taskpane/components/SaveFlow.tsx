import React, { useCallback, useMemo, useEffect } from 'react';
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
  BrainCircuitRegular,
  OpenRegular,
  CopyRegular,
  PersonSearchRegular,
} from '@fluentui/react-icons';
import { EntityPicker } from './EntityPicker';
import { AttachmentSelector } from './AttachmentSelector';
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
import type { AttachmentInfo, HostType } from '@shared/adapters/types';

/**
 * Styles using Fluent UI v9 design tokens (ADR-021).
 */
const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
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
  processingDescription: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    marginLeft: tokens.spacingHorizontalXL,
  },
  actions: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
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
  /** Email sender (Outlook only) */
  emailSender?: string;
  /** Email received date (Outlook only) */
  emailReceivedDate?: Date;
  /** Document URL (Word only) */
  documentUrl?: string;
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
 *   getAccessToken={() => authService.getAccessToken(['user_impersonation'])}
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
    emailSender,
    emailReceivedDate,
    documentUrl,
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
  const saveFlowOptions: UseSaveFlowOptions = useMemo(() => ({
    apiBaseUrl,
    getAccessToken,
    onComplete: (docId, docUrl) => {
      announce('Document saved successfully', 'polite');
      onComplete?.(docId, docUrl);
    },
    onError: (error) => {
      announce(`Error: ${error.message}`, 'assertive');
    },
    onDuplicate: (docId, message) => {
      announce('Duplicate detected: ' + message, 'polite');
    },
  }), [apiBaseUrl, getAccessToken, onComplete, announce]);

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

  // Build save context
  const buildSaveContext = useCallback((): SaveFlowContext => ({
    hostType,
    itemId,
    itemName,
    attachments,
    documentUrl,
  }), [hostType, itemId, itemName, attachments, documentUrl]);

  // Handle save button click
  const handleSave = useCallback(() => {
    const context = buildSaveContext();
    startSave(context);
  }, [buildSaveContext, startSave]);

  // Handle entity selection
  const handleEntitySelect = useCallback((entity: EntitySearchResult | null) => {
    setSelectedEntity(entity);
    if (entity) {
      announce(`Selected ${entity.entityType}: ${entity.name}`, 'polite');
    }
  }, [setSelectedEntity, announce]);

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
              jobStatus.status === 'Completed' ? 'success' :
              jobStatus.status === 'Failed' ? 'danger' :
              'informative'
            }
          >
            {jobStatus.status}
          </Badge>
        </div>

        <ProgressBar value={progressPercentage / 100} />

        <div
          className={styles.stageList}
          role="list"
          aria-label="Processing stages"
        >
          {jobStatus.stages.map((stage) => (
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
      <Text size={500} weight="semibold">Document Saved</Text>
      <Body1 style={{ marginTop: tokens.spacingVerticalS }}>
        Your document has been saved to Spaarke and associated with{' '}
        <Text weight="semibold">{selectedEntity?.name}</Text>.
      </Body1>
      <div className={styles.successActions}>
        <Button
          appearance="primary"
          icon={<OpenRegular />}
          onClick={handleViewDocument}
          disabled={!savedDocumentUrl}
        >
          View Document
        </Button>
        <Button
          appearance="outline"
          icon={<CopyRegular />}
          onClick={handleCopyLink}
          disabled={!savedDocumentUrl}
        >
          Copy Link
        </Button>
        <Button
          appearance="subtle"
          icon={<ArrowResetRegular />}
          onClick={reset}
        >
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
          <Text className={styles.processingDescription}>
            Generate AI summary with key details and entities
          </Text>

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
              aria-label="Enable RAG indexing"
            />
          </div>
          <Text className={styles.processingDescription}>
            Index content for AI-powered search
          </Text>

          <Divider />

          <div className={styles.processingOption}>
            <div className={styles.processingLabel}>
              <BrainCircuitRegular />
              <Text>Deep Analysis</Text>
            </div>
            <Switch
              checked={processingOptions.deepAnalysis}
              onChange={() => toggleProcessingOption('deepAnalysis')}
              disabled={isSaving}
              aria-label="Enable deep AI analysis"
            />
          </div>
          <Text className={styles.processingDescription}>
            Run comprehensive AI analysis (slower)
          </Text>
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
            <Text weight="semibold">
              {hostType === 'outlook' ? 'Email' : 'Document'}
            </Text>
          </div>
          <div className={styles.documentInfo}>
            <div className={styles.documentInfoRow}>
              <Text weight="semibold">{itemName}</Text>
            </div>
            {hostType === 'outlook' && emailSender && (
              <div className={styles.documentInfoRow}>
                <Text size={200}>From: {emailSender}</Text>
              </div>
            )}
            {hostType === 'outlook' && emailReceivedDate && (
              <div className={styles.documentInfoRow}>
                <Text size={200}>
                  Received: {emailReceivedDate.toLocaleDateString()}
                </Text>
              </div>
            )}
          </div>
        </div>
      )}

      {/* Entity Picker */}
      <div className={styles.section}>
        <EntityPicker
          value={selectedEntity}
          onChange={handleEntitySelect}
          onQuickCreate={onQuickCreate}
          placeholder="Search for Matter, Project, Account..."
          allowedTypes={allowedEntityTypes}
          showTypeFilter
          showRecent
          showQuickCreate={!!onQuickCreate}
          disabled={isSaving}
          required
          label="Associate With"
          errorMessage={
            !selectedEntity && flowState !== 'idle' && flowState !== 'selecting'
              ? 'Please select an association target'
              : undefined
          }
        />
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

      {/* Processing Options */}
      {renderProcessingOptions()}

      {/* Actions */}
      <div className={styles.actions}>
        <Button
          appearance="primary"
          icon={isSaving ? <Spinner size="tiny" /> : <SaveRegular />}
          onClick={handleSave}
          disabled={isSaving || !isValid}
          size="large"
        >
          {isSaving ? 'Saving...' : 'Save to Spaarke'}
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
    <div
      className={mergeClasses(styles.container, className)}
      role="form"
      aria-label="Save to Spaarke"
    >
      {renderContent()}
    </div>
  );
}

export default SaveFlow;
