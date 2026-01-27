import { useState, useCallback, useRef, useEffect, useMemo } from 'react';
import type { EntitySearchResult } from './useEntitySearch';
import type { AttachmentInfo, HostType } from '@shared/adapters/types';
import {
  type ProblemDetails,
  mapProblemDetailsToMessage,
  isProblemDetails,
  createErrorFromException,
  type ErrorMessage,
} from '../utils/errorMessages';
import { createSseConnection, type SseConnection, type SseEvent } from '../services/SseClient';

/**
 * Source types for save operations.
 */
export type SourceType = 'OutlookEmail' | 'OutlookAttachment' | 'WordDocument';

/**
 * Save flow states based on spec.md Task Pane State Diagram.
 */
export type SaveFlowState =
  | 'idle'
  | 'selecting'
  | 'uploading'
  | 'processing'
  | 'complete'
  | 'error'
  | 'duplicate';

/**
 * Processing options for the save operation.
 */
export interface ProcessingOptions {
  /** Generate profile summary */
  profileSummary: boolean;
  /** Index for RAG search */
  ragIndex: boolean;
  /** Run deep AI analysis */
  deepAnalysis: boolean;
}

/**
 * Stage status for job processing.
 */
export interface StageStatus {
  name: string;
  status: 'Pending' | 'Running' | 'Completed' | 'Failed' | 'Skipped';
  completedAt?: string;
}

/**
 * Completed phase from server response.
 */
export interface CompletedPhase {
  name: string;
  completedAt: string;
  durationMs?: number;
}

/**
 * Job result artifact from server response.
 */
export interface JobResultArtifact {
  type: string;
  id: string;
  webUrl?: string;
  speFileId?: string;
  containerId?: string;
}

/**
 * Job result from server response.
 */
export interface JobResult {
  artifact?: JobResultArtifact;
}

/**
 * Job error from server response.
 */
export interface JobErrorResponse {
  code: string;
  message: string;
  retryable?: boolean;
}

/**
 * Job status response from API.
 * Matches server's JobStatusResponse model.
 */
export interface JobStatus {
  jobId: string;
  status: 'Queued' | 'Running' | 'Completed' | 'Failed' | 'PartialSuccess' | 'Cancelled';
  jobType: string;
  progress: number;
  currentPhase?: string;
  completedPhases?: CompletedPhase[];
  createdAt: string;
  createdBy?: string;
  startedAt?: string;
  completedAt?: string;
  result?: JobResult;
  error?: JobErrorResponse;
  // Legacy fields for UI compatibility
  stages?: StageStatus[];
  documentId?: string;
  documentUrl?: string;
}

/**
 * Save response from API.
 */
export interface SaveResponse {
  jobId: string;
  documentId?: string;
  statusUrl: string;
  streamUrl: string;
  status: string;
  duplicate: boolean;
  message?: string;
  correlationId: string;
}

/**
 * Content data for save request.
 */
export interface SaveContentData {
  emailId?: string;
  includeBody?: boolean;
  attachmentIds?: string[];
  documentUrl?: string;
  documentName?: string;
}

/**
 * Metadata for save request.
 */
export interface SaveMetadata {
  description?: string;
  tags?: string[];
}

/**
 * Save request payload.
 */
export interface SaveRequest {
  sourceType: SourceType;
  associationType: string;
  associationId: string;
  content: SaveContentData;
  processing: ProcessingOptions;
  metadata?: SaveMetadata;
}

/**
 * Email recipient for save context.
 */
export interface EmailRecipient {
  email: string;
  displayName?: string;
  type: 'to' | 'cc' | 'bcc';
}

/**
 * Save flow context data.
 */
export interface SaveFlowContext {
  /** Host type (outlook or word) */
  hostType: HostType;
  /** Current item ID (email or document) */
  itemId?: string;
  /** Display name of the current item */
  itemName?: string;
  /** Available attachments (Outlook only) */
  attachments: AttachmentInfo[];
  /** Email body content (Outlook only) */
  emailBody?: string;
  /** Sender email address (Outlook only) */
  senderEmail?: string;
  /** Sender display name (Outlook only) */
  senderDisplayName?: string;
  /** Email recipients (Outlook only) */
  recipients?: EmailRecipient[];
  /** Email sent date (Outlook only) */
  sentDate?: Date;
  /** Document content URL (Word only) */
  documentUrl?: string;
}

/**
 * Hook options.
 */
export interface UseSaveFlowOptions {
  /** API base URL */
  apiBaseUrl?: string;
  /** Access token getter for authenticated requests */
  getAccessToken: () => Promise<string>;
  /** Polling interval for job status (ms, default: 3000) */
  pollingIntervalMs?: number;
  /** SSE timeout (ms, default: 30000) */
  sseTimeoutMs?: number;
  /** Callback when save is complete */
  onComplete?: (documentId: string, documentUrl: string) => void;
  /** Callback on error */
  onError?: (error: ErrorMessage) => void;
  /** Callback on duplicate detected */
  onDuplicate?: (documentId: string, message: string) => void;
}

/**
 * Hook result.
 */
export interface UseSaveFlowResult {
  /** Current flow state */
  flowState: SaveFlowState;
  /** Selected association entity */
  selectedEntity: EntitySearchResult | null;
  /** Set selected entity */
  setSelectedEntity: (entity: EntitySearchResult | null) => void;
  /** Selected attachment IDs */
  selectedAttachmentIds: Set<string>;
  /** Set selected attachment IDs */
  setSelectedAttachmentIds: (ids: Set<string>) => void;
  /** Whether to include email body */
  includeBody: boolean;
  /** Set include email body */
  setIncludeBody: (include: boolean) => void;
  /** Processing options */
  processingOptions: ProcessingOptions;
  /** Set processing options */
  setProcessingOptions: (options: ProcessingOptions) => void;
  /** Toggle a processing option */
  toggleProcessingOption: (key: keyof ProcessingOptions) => void;
  /** Current job status (during processing) */
  jobStatus: JobStatus | null;
  /** Current error message */
  error: ErrorMessage | null;
  /** Clear error */
  clearError: () => void;
  /** Duplicate detection info */
  duplicateInfo: { documentId: string; message: string } | null;
  /** Whether save is in progress */
  isSaving: boolean;
  /** Whether form is valid (association selected) */
  isValid: boolean;
  /** Start the save flow */
  startSave: (context: SaveFlowContext) => Promise<void>;
  /** Reset the flow to idle state */
  reset: () => void;
  /** Retry after error */
  retry: () => void;
  /** Document ID after successful save */
  savedDocumentId: string | null;
  /** Document URL after successful save */
  savedDocumentUrl: string | null;
}

/**
 * Default processing options.
 */
const DEFAULT_PROCESSING_OPTIONS: ProcessingOptions = {
  profileSummary: true,
  ragIndex: true,
  deepAnalysis: false,
};

/**
 * Storage key for last-used association.
 */
const LAST_ASSOCIATION_KEY = 'spaarke-last-association';

/**
 * Get last-used association from sessionStorage.
 */
function getLastAssociation(): EntitySearchResult | null {
  if (typeof sessionStorage === 'undefined') return null;

  try {
    const stored = sessionStorage.getItem(LAST_ASSOCIATION_KEY);
    return stored ? JSON.parse(stored) : null;
  } catch {
    return null;
  }
}

/**
 * Save last-used association to sessionStorage.
 */
function saveLastAssociation(entity: EntitySearchResult): void {
  if (typeof sessionStorage === 'undefined') return;

  try {
    sessionStorage.setItem(LAST_ASSOCIATION_KEY, JSON.stringify(entity));
  } catch {
    // Ignore storage errors
  }
}

/**
 * Compute idempotency key (SHA-256 of canonical payload).
 */
async function computeIdempotencyKey(request: SaveRequest): Promise<string> {
  const canonical = JSON.stringify({
    sourceType: request.sourceType,
    associationType: request.associationType,
    associationId: request.associationId,
    emailId: request.content.emailId,
    attachmentIds: request.content.attachmentIds?.sort(),
    includeBody: request.content.includeBody,
    documentUrl: request.content.documentUrl,
  });

  const encoder = new TextEncoder();
  const data = encoder.encode(canonical);
  const hashBuffer = await crypto.subtle.digest('SHA-256', data);
  const hashArray = Array.from(new Uint8Array(hashBuffer));
  return hashArray.map(b => b.toString(16).padStart(2, '0')).join('');
}

/**
 * React hook for managing the save flow state and operations.
 *
 * Handles:
 * - Entity selection with validation
 * - Attachment selection (Outlook only)
 * - Processing options
 * - Save submission with idempotency
 * - Job status tracking via SSE/polling
 * - Duplicate detection
 * - Error handling
 *
 * @example
 * ```tsx
 * const {
 *   flowState,
 *   selectedEntity,
 *   setSelectedEntity,
 *   processingOptions,
 *   toggleProcessingOption,
 *   startSave,
 *   isValid,
 *   isSaving,
 *   jobStatus,
 *   error,
 * } = useSaveFlow({
 *   getAccessToken: () => authService.getAccessToken(['user_impersonation']),
 *   onComplete: (docId, url) => navigateToDocument(url),
 *   onError: (error) => showNotification(error),
 * });
 * ```
 */
export function useSaveFlow(options: UseSaveFlowOptions): UseSaveFlowResult {
  const {
    apiBaseUrl = '',
    getAccessToken,
    pollingIntervalMs = 3000,
    sseTimeoutMs = 30000,
    onComplete,
    onError,
    onDuplicate,
  } = options;

  // State
  const [flowState, setFlowState] = useState<SaveFlowState>('idle');
  const [selectedEntity, setSelectedEntityState] = useState<EntitySearchResult | null>(
    () => getLastAssociation()
  );
  const [selectedAttachmentIds, setSelectedAttachmentIds] = useState<Set<string>>(new Set());
  const [includeBody, setIncludeBody] = useState(true);
  const [processingOptions, setProcessingOptions] = useState<ProcessingOptions>(
    DEFAULT_PROCESSING_OPTIONS
  );
  const [jobStatus, setJobStatus] = useState<JobStatus | null>(null);
  const [error, setError] = useState<ErrorMessage | null>(null);
  const [duplicateInfo, setDuplicateInfo] = useState<{
    documentId: string;
    message: string;
  } | null>(null);
  const [savedDocumentId, setSavedDocumentId] = useState<string | null>(null);
  const [savedDocumentUrl, setSavedDocumentUrl] = useState<string | null>(null);

  // Refs
  const sseConnectionRef = useRef<SseConnection | null>(null);
  const pollingIntervalRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const savedContextRef = useRef<SaveFlowContext | null>(null);
  const abortControllerRef = useRef<AbortController | null>(null);
  const pollingRetryCountRef = useRef<number>(0);
  const maxPollingRetries = 3;

  // Computed values
  const isSaving = flowState === 'uploading' || flowState === 'processing';
  // Association is optional - user can save without selecting an entity
  const isValid = true;

  // Set selected entity and save to storage
  const setSelectedEntity = useCallback((entity: EntitySearchResult | null) => {
    setSelectedEntityState(entity);
    if (entity) {
      saveLastAssociation(entity);
    }
  }, []);

  // Toggle processing option
  const toggleProcessingOption = useCallback((key: keyof ProcessingOptions) => {
    setProcessingOptions(prev => ({
      ...prev,
      [key]: !prev[key],
    }));
  }, []);

  // Clear error
  const clearError = useCallback(() => {
    setError(null);
    if (flowState === 'error') {
      setFlowState('selecting');
    }
  }, [flowState]);

  // Cleanup SSE/polling
  const cleanup = useCallback(() => {
    if (sseConnectionRef.current) {
      sseConnectionRef.current.close();
      sseConnectionRef.current = null;
    }
    if (pollingIntervalRef.current) {
      clearInterval(pollingIntervalRef.current);
      pollingIntervalRef.current = null;
    }
    if (abortControllerRef.current) {
      abortControllerRef.current.abort();
      abortControllerRef.current = null;
    }
    pollingRetryCountRef.current = 0;
  }, []);

  // Reset state
  const reset = useCallback(() => {
    cleanup();
    setFlowState('idle');
    setSelectedAttachmentIds(new Set());
    setIncludeBody(true);
    setProcessingOptions(DEFAULT_PROCESSING_OPTIONS);
    setJobStatus(null);
    setError(null);
    setDuplicateInfo(null);
    setSavedDocumentId(null);
    setSavedDocumentUrl(null);
    savedContextRef.current = null;
  }, [cleanup]);

  // Handle SSE event
  const handleSseEvent = useCallback((event: SseEvent) => {
    // SSE events have different structures based on event type
    // Server sends events like: progress, stage, complete, failed, heartbeat
    const eventType = event.event || 'message';
    const data = event.data as Record<string, unknown>;

    if (eventType === 'progress' || eventType === 'message') {
      // Progress update: { progress: number, currentPhase: string }
      setJobStatus(prev => {
        if (!prev) return null;
        const progress = typeof data.progress === 'number' ? data.progress : prev.progress;
        const currentPhase = typeof data.currentPhase === 'string' ? data.currentPhase : prev.currentPhase;

        // Update stages based on current phase
        const stages = prev.stages?.map(s => {
          if (s.name === currentPhase) {
            return { ...s, status: 'Running' as const };
          }
          return s;
        });

        return { ...prev, progress, currentPhase, stages };
      });
    } else if (eventType === 'stage') {
      // Stage update: { stage: string, status: string, timestamp: string }
      const stageName = data.stage as string;
      const stageStatus = data.status as string;
      const timestamp = data.timestamp as string;

      setJobStatus(prev => {
        if (!prev) return null;
        const stages = prev.stages?.map(s =>
          s.name === stageName
            ? { ...s, status: stageStatus as StageStatus['status'], completedAt: timestamp }
            : s
        );
        return { ...prev, stages };
      });
    } else if (eventType === 'complete') {
      // Completion: { jobId: string, documentId?: string, documentUrl?: string }
      const documentId = data.documentId as string | undefined;
      const documentUrl = data.documentUrl as string | undefined;

      if (documentId) {
        setSavedDocumentId(documentId);
        setSavedDocumentUrl(documentUrl || null);
        setFlowState('complete');
        onComplete?.(documentId, documentUrl || '');
      }
      cleanup();
    } else if (eventType === 'failed' || eventType === 'error') {
      // Error: { code: string, message: string, retryable?: boolean }
      const errorMsg: ErrorMessage = {
        title: 'Processing Failed',
        message: (data.message as string) || 'An error occurred during processing.',
        type: 'error',
        recoverable: (data.retryable as boolean) ?? true,
      };
      setError(errorMsg);
      setFlowState('error');
      onError?.(errorMsg);
      cleanup();
    }
    // Ignore heartbeat events
  }, [cleanup, onComplete, onError]);

  // Poll for job status
  const pollJobStatus = useCallback(async (jobId: string, accessToken: string) => {
    try {
      const response = await fetch(`${apiBaseUrl}/office/jobs/${jobId}`, {
        headers: {
          Authorization: `Bearer ${accessToken}`,
        },
        signal: abortControllerRef.current?.signal,
      });

      if (!response.ok) {
        // Increment retry counter on failure
        pollingRetryCountRef.current++;
        console.warn(
          `Polling failed (attempt ${pollingRetryCountRef.current}/${maxPollingRetries}): ${response.status}`
        );

        // Stop polling after max retries
        if (pollingRetryCountRef.current >= maxPollingRetries) {
          console.error(`Max polling retries (${maxPollingRetries}) exceeded, stopping`);
          const errorMsg: ErrorMessage = {
            title: 'Job Status Unavailable',
            message: `Unable to retrieve job status after ${maxPollingRetries} attempts. The save may still be processing in the background.`,
            type: 'error',
            recoverable: true,
          };
          setError(errorMsg);
          setFlowState('error');
          onError?.(errorMsg);
          cleanup();
          return;
        }

        const errorData = await response.json().catch(() => null);
        if (isProblemDetails(errorData)) {
          throw errorData;
        }
        throw new Error(`Failed to fetch job status: ${response.status}`);
      }

      // Reset retry counter on success
      pollingRetryCountRef.current = 0;

      const rawStatus = (await response.json()) as JobStatus;

      // Map server response to UI-compatible format
      const mappedStages: StageStatus[] = [];

      // Build stages from completedPhases and currentPhase
      const phaseOrder = ['RecordsCreated', 'FileUploaded', 'ProfileSummary', 'Indexed', 'DeepAnalysis'];
      const completedPhaseNames = new Set(rawStatus.completedPhases?.map(p => p.name) || []);

      for (const phaseName of phaseOrder) {
        if (completedPhaseNames.has(phaseName)) {
          const phase = rawStatus.completedPhases?.find(p => p.name === phaseName);
          mappedStages.push({
            name: phaseName,
            status: 'Completed',
            completedAt: phase?.completedAt,
          });
        } else if (rawStatus.currentPhase === phaseName) {
          mappedStages.push({
            name: phaseName,
            status: 'Running',
          });
        } else if (rawStatus.status !== 'Completed') {
          // Only show pending stages if job is not complete
          mappedStages.push({
            name: phaseName,
            status: 'Pending',
          });
        }
      }

      // Extract document info from result.artifact
      const documentId = rawStatus.result?.artifact?.id;
      const documentUrl = rawStatus.result?.artifact?.webUrl;

      const status: JobStatus = {
        ...rawStatus,
        stages: mappedStages,
        documentId,
        documentUrl,
      };

      setJobStatus(status);

      // Check for completion
      if (status.status === 'Completed' || status.status === 'Failed') {
        if (status.status === 'Completed' && documentId) {
          setSavedDocumentId(documentId);
          setSavedDocumentUrl(documentUrl || null);
          setFlowState('complete');
          onComplete?.(documentId, documentUrl || '');
        } else if (status.status === 'Failed') {
          const errorMsg: ErrorMessage = {
            title: 'Processing Failed',
            message: status.error?.message || 'An error occurred during processing.',
            type: 'error',
            recoverable: status.error?.retryable ?? true,
          };
          setError(errorMsg);
          setFlowState('error');
          onError?.(errorMsg);
        }
        cleanup();
      }
    } catch (err) {
      if (err instanceof Error && err.name === 'AbortError') {
        return; // Cancelled
      }
      console.error('Polling error:', err);

      // Increment retry counter on exception
      pollingRetryCountRef.current++;
      if (pollingRetryCountRef.current >= maxPollingRetries) {
        console.error(`Max polling retries (${maxPollingRetries}) exceeded after exception, stopping`);
        const errorMsg: ErrorMessage = {
          title: 'Job Status Unavailable',
          message: `Unable to retrieve job status after ${maxPollingRetries} attempts. Please try again later.`,
          type: 'error',
          recoverable: true,
        };
        setError(errorMsg);
        setFlowState('error');
        onError?.(errorMsg);
        cleanup();
      }
    }
  }, [apiBaseUrl, cleanup, onComplete, onError]);

  // Start SSE connection with polling fallback
  const startJobTracking = useCallback(async (
    jobId: string,
    streamUrl: string,
    accessToken: string
  ) => {
    setFlowState('processing');

    // Reset retry counter at start of tracking
    pollingRetryCountRef.current = 0;

    // Initialize job status with standard stages
    const initialStages: StageStatus[] = [
      { name: 'RecordsCreated', status: 'Pending' },
      { name: 'FileUploaded', status: 'Pending' },
    ];

    if (processingOptions.profileSummary) {
      initialStages.push({ name: 'ProfileSummary', status: 'Pending' });
    }
    if (processingOptions.ragIndex) {
      initialStages.push({ name: 'Indexed', status: 'Pending' });
    }
    if (processingOptions.deepAnalysis) {
      initialStages.push({ name: 'DeepAnalysis', status: 'Pending' });
    }

    setJobStatus({
      jobId,
      status: 'Queued',
      stages: initialStages,
    });

    // Always start polling as the primary mechanism
    // SSE is optional enhancement but may not receive events if Redis pub/sub isn't configured
    console.log('[SaveFlow] Starting job polling for', jobId);
    pollingIntervalRef.current = setInterval(() => {
      pollJobStatus(jobId, accessToken);
    }, pollingIntervalMs);

    // Also do an immediate poll to get current status
    pollJobStatus(jobId, accessToken);

    // Try SSE as enhancement (provides faster updates when available)
    try {
      sseConnectionRef.current = createSseConnection(`${apiBaseUrl}${streamUrl}`, {
        accessToken,
        onEvent: handleSseEvent,
        onError: (err) => {
          console.warn('SSE error (polling continues):', err.message);
        },
        onClose: () => {
          sseConnectionRef.current = null;
        },
        timeout: sseTimeoutMs,
      });
    } catch (err) {
      console.warn('Failed to create SSE connection (polling continues):', err);
    }
  }, [apiBaseUrl, handleSseEvent, pollJobStatus, pollingIntervalMs, processingOptions, sseTimeoutMs]);

  // Start save operation
  const startSave = useCallback(async (context: SaveFlowContext) => {
    // Association is optional - user can save without selecting an entity

    // Save context for retry
    savedContextRef.current = context;
    setFlowState('uploading');
    setError(null);
    setDuplicateInfo(null);

    // Cancel any existing operations
    cleanup();
    abortControllerRef.current = new AbortController();

    try {
      const accessToken = await getAccessToken();

      // Determine content type for server API
      let contentType: 'Email' | 'Attachment' | 'Document';
      if (context.hostType === 'outlook') {
        contentType = selectedAttachmentIds.size > 0 ? 'Attachment' : 'Email';
      } else {
        contentType = 'Document';
      }

      // Build request in server-expected format
      // Server requires: contentType, targetEntity, and type-specific metadata
      const serverRequest: Record<string, unknown> = {
        contentType,
        triggerAiProcessing: processingOptions.deepAnalysis || processingOptions.ragIndex,
      };

      // Add target entity if an association was selected
      if (selectedEntity?.id) {
        serverRequest.targetEntity = {
          entityType: selectedEntity.entityType,
          entityId: selectedEntity.id,
          displayName: selectedEntity.name,
        };
      }

      // Add content-type-specific metadata
      if (contentType === 'Email') {
        // Build recipients array for server (matches EmailMetadata.Recipients model)
        // Server expects: { type: 'To'|'Cc'|'Bcc', email: string, name?: string }
        const recipients = context.recipients?.map(r => ({
          type: r.type === 'to' ? 'To' : r.type === 'cc' ? 'Cc' : 'Bcc',
          email: r.email,
          name: r.displayName,
        })) || [];

        serverRequest.email = {
          subject: context.itemName || 'Untitled Email',
          senderEmail: context.senderEmail || 'unknown@placeholder.com',
          senderName: context.senderDisplayName,
          recipients,
          sentDate: context.sentDate?.toISOString(),
          body: includeBody ? context.emailBody : undefined,
          isBodyHtml: true,
          internetMessageId: context.itemId,
        };
      } else if (contentType === 'Attachment') {
        const attachmentId = Array.from(selectedAttachmentIds)[0];
        const attachment = context.attachments.find(a => a.id === attachmentId);
        serverRequest.attachment = {
          attachmentId: attachmentId,
          fileName: attachment?.name || 'attachment',
          contentType: attachment?.contentType,
          size: attachment?.size,
        };
      } else if (contentType === 'Document') {
        serverRequest.document = {
          fileName: context.itemName || 'document.docx',
          title: context.itemName,
          contentType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
        };
      }

      // Build legacy request for idempotency key computation (keep format stable)
      const request: SaveRequest = {
        sourceType: context.hostType === 'outlook'
          ? (selectedAttachmentIds.size > 0 ? 'OutlookAttachment' : 'OutlookEmail')
          : 'WordDocument',
        associationType: selectedEntity?.entityType || '',
        associationId: selectedEntity?.id || '',
        content: {
          emailId: context.itemId,
          includeBody: includeBody && context.hostType === 'outlook',
          attachmentIds: Array.from(selectedAttachmentIds),
          documentUrl: context.documentUrl,
          documentName: context.itemName,
        },
        processing: processingOptions,
      };

      // Compute idempotency key from legacy format for consistency
      const idempotencyKey = await computeIdempotencyKey(request);

      // Submit save request
      console.log('[SaveFlow] Sending request:', JSON.stringify(serverRequest, null, 2));
      const response = await fetch(`${apiBaseUrl}/office/save`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${accessToken}`,
          'X-Idempotency-Key': idempotencyKey,
        },
        body: JSON.stringify(serverRequest),
        signal: abortControllerRef.current.signal,
      });

      // Get raw response text first for debugging
      const responseText = await response.text();
      console.log('[SaveFlow] Response status:', response.status);
      console.log('[SaveFlow] Response text (raw):', responseText);

      // Parse as JSON if possible
      let responseData: unknown;
      try {
        responseData = JSON.parse(responseText);
        console.log('[SaveFlow] Response data (parsed):', JSON.stringify(responseData, null, 2));
      } catch (parseError) {
        console.error('[SaveFlow] Failed to parse response as JSON:', parseError);
        // If 400 with non-JSON response, show raw text as error
        if (!response.ok) {
          throw new Error(`Save failed (${response.status}): ${responseText.substring(0, 500)}`);
        }
      }

      // Handle different responses
      if (response.status === 200 && responseData.duplicate) {
        // Duplicate detected
        setDuplicateInfo({
          documentId: responseData.documentId,
          message: responseData.message || 'This item was previously saved.',
        });
        setFlowState('duplicate');
        onDuplicate?.(responseData.documentId, responseData.message);
        return;
      }

      if (!response.ok) {
        // Error response
        if (isProblemDetails(responseData)) {
          const errorMsg = mapProblemDetailsToMessage(responseData);
          setError(errorMsg);
          setFlowState('error');
          onError?.(errorMsg);
          return;
        }
        throw new Error(`Save failed: ${response.status}`);
      }

      // Success - start job tracking
      const saveResponse = responseData as SaveResponse;
      await startJobTracking(saveResponse.jobId, saveResponse.streamUrl, accessToken);
    } catch (err) {
      if (err instanceof Error && err.name === 'AbortError') {
        return; // Cancelled
      }

      const errorMsg = createErrorFromException(err, 'Failed to save. Please try again.');
      setError(errorMsg);
      setFlowState('error');
      onError?.(errorMsg);
    }
  }, [
    apiBaseUrl,
    cleanup,
    getAccessToken,
    includeBody,
    onDuplicate,
    onError,
    processingOptions,
    selectedAttachmentIds,
    selectedEntity,
    startJobTracking,
  ]);

  // Retry after error
  const retry = useCallback(() => {
    if (savedContextRef.current) {
      startSave(savedContextRef.current);
    }
  }, [startSave]);

  // Cleanup on unmount
  useEffect(() => {
    return () => {
      cleanup();
    };
  }, [cleanup]);

  return {
    flowState,
    selectedEntity,
    setSelectedEntity,
    selectedAttachmentIds,
    setSelectedAttachmentIds,
    includeBody,
    setIncludeBody,
    processingOptions,
    setProcessingOptions,
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
  };
}
