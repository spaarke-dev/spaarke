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
 * Job status response from API.
 */
export interface JobStatus {
  jobId: string;
  status: 'Queued' | 'Running' | 'Completed' | 'Failed' | 'PartialSuccess';
  stages: StageStatus[];
  documentId?: string;
  documentUrl?: string;
  associationUrl?: string;
  errorCode?: string;
  errorMessage?: string;
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
    const data = event.data as Partial<JobStatus & { stage?: string; timestamp?: string }>;

    // Update job status
    if (data.stage && data.status) {
      // Stage update
      setJobStatus(prev => {
        if (!prev) return null;
        const stages = prev.stages.map(s =>
          s.name === data.stage
            ? { ...s, status: data.status as StageStatus['status'], completedAt: data.timestamp }
            : s
        );
        return { ...prev, stages };
      });
    }

    // Check for completion
    if (data.status === 'Completed' || data.status === 'Failed') {
      if (data.status === 'Completed' && data.documentId) {
        setSavedDocumentId(data.documentId);
        setSavedDocumentUrl(data.documentUrl || null);
        setFlowState('complete');
        onComplete?.(data.documentId, data.documentUrl || '');
      } else if (data.status === 'Failed') {
        const errorMsg: ErrorMessage = {
          title: 'Processing Failed',
          message: data.errorMessage || 'An error occurred during processing.',
          type: 'error',
          recoverable: true,
        };
        setError(errorMsg);
        setFlowState('error');
        onError?.(errorMsg);
      }
      cleanup();
    }
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
        const errorData = await response.json().catch(() => null);
        if (isProblemDetails(errorData)) {
          throw errorData;
        }
        throw new Error(`Failed to fetch job status: ${response.status}`);
      }

      const status = (await response.json()) as JobStatus;
      setJobStatus(status);

      // Check for completion
      if (status.status === 'Completed' || status.status === 'Failed') {
        if (status.status === 'Completed' && status.documentId) {
          setSavedDocumentId(status.documentId);
          setSavedDocumentUrl(status.documentUrl || null);
          setFlowState('complete');
          onComplete?.(status.documentId, status.documentUrl || '');
        } else if (status.status === 'Failed') {
          const errorMsg: ErrorMessage = {
            title: 'Processing Failed',
            message: status.errorMessage || 'An error occurred during processing.',
            type: 'error',
            recoverable: true,
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
    }
  }, [apiBaseUrl, cleanup, onComplete, onError]);

  // Start SSE connection with polling fallback
  const startJobTracking = useCallback(async (
    jobId: string,
    streamUrl: string,
    accessToken: string
  ) => {
    setFlowState('processing');

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

    // Try SSE first
    try {
      sseConnectionRef.current = createSseConnection(`${apiBaseUrl}${streamUrl}`, {
        accessToken,
        onEvent: handleSseEvent,
        onError: (err) => {
          console.warn('SSE error, falling back to polling:', err.message);
          // Fall back to polling
          if (!pollingIntervalRef.current) {
            pollingIntervalRef.current = setInterval(() => {
              pollJobStatus(jobId, accessToken);
            }, pollingIntervalMs);
          }
        },
        onClose: () => {
          sseConnectionRef.current = null;
        },
        timeout: sseTimeoutMs,
      });
    } catch (err) {
      console.warn('Failed to create SSE connection, using polling:', err);
      // Start polling
      pollingIntervalRef.current = setInterval(() => {
        pollJobStatus(jobId, accessToken);
      }, pollingIntervalMs);
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

      // Determine source type
      let sourceType: SourceType;
      if (context.hostType === 'outlook') {
        sourceType = selectedAttachmentIds.size > 0 ? 'OutlookAttachment' : 'OutlookEmail';
      } else {
        sourceType = 'WordDocument';
      }

      // Build request - association is optional
      const request: SaveRequest = {
        sourceType,
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

      // Compute idempotency key
      const idempotencyKey = await computeIdempotencyKey(request);

      // Submit save request
      const response = await fetch(`${apiBaseUrl}/office/save`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${accessToken}`,
          'X-Idempotency-Key': idempotencyKey,
        },
        body: JSON.stringify(request),
        signal: abortControllerRef.current.signal,
      });

      const responseData = await response.json();

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
