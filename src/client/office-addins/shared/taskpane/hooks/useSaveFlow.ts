import { useState, useCallback, useRef, useEffect, useContext } from 'react';
import type { EntitySearchResult, EntityType } from './useEntitySearch';
import type { AttachmentInfo, HostType } from '@shared/adapters/types';
import { authenticatedJsonFetch } from '@shared/services/authenticatedJsonFetch';
import {
  describeVersionSaveFailure,
  describeCollisionFailure,
  isProblemDetails,
  createErrorFromException,
  type ErrorMessage,
} from '../utils/errorMessages';
import { createSseConnection, type SseConnection, type SseEvent } from '../services/SseClient';
import { cleanGuid } from '@spaarke/ui-components/guid';
import { toDocxFileName } from '../utils/documentFileName';
import { EmailContentCaptureContext } from './emailContentCaptureContext';
import type { EmailContentCapture } from '../services/emailContentCapture';
import {
  readEmailFilingProblem,
  type EmailFilingOutcome,
  type EmailFilingProblem,
} from '../services/emailFilingNotice';
import {
  describeAttachmentsNotSaveable,
  describeAttachmentsNotStored,
  readAttachmentsAdded,
  readAttachmentsAddedCount,
  readAttachmentsNotSaveable,
  readAttachmentsNotStored,
} from '../services/attachmentsAddedNotice';

/** A canonical (bare-lowercase, ADR-044) Dataverse GUID. */
const CANONICAL_GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;

/**
 * Source types for save operations.
 */
export type SourceType = 'OutlookEmail' | 'OutlookAttachment' | 'WordDocument';

/**
 * Save flow states based on spec.md Task Pane State Diagram.
 */
export type SaveFlowState = 'idle' | 'selecting' | 'uploading' | 'processing' | 'complete' | 'error' | 'duplicate';

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
  /** Absent on the client-constructed initial "Queued" state; present once the first server poll response lands. */
  jobType?: string;
  /** Absent on the client-constructed initial "Queued" state; present once the first server poll response lands. */
  progress?: number;
  currentPhase?: string;
  completedPhases?: CompletedPhase[];
  /** Absent on the client-constructed initial "Queued" state; present once the first server poll response lands. */
  createdAt?: string;
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
  /**
   * Task 131 (#2171): what the save did about filing the email it reconciled to. OMITTED when there is nothing to report
   * (and on a duplicate replay). Only `NotPermitted` / `Failed` / `FiledUnconfirmed` (task 140) are shown to the user. A value this build does not know
   * is ignored — see {@link readEmailFilingProblem}.
   */
  emailFiling?: EmailFilingOutcome;
  /**
   * Task 138 (#2209): the file names of the attachments this save ADDED under an email already saved to the same record.
   * OMITTED on every other answer. Read through {@link readAttachmentsAddedCount}, so a malformed value counts as none.
   */
  attachmentsAdded?: string[];
  /**
   * Task 138 (#2209): the file names the save was asked to add that the stored email does not hold (not saved with it the
   * first time), so they cannot be added. OMITTED when there are none. Read through {@link readAttachmentsNotStored}.
   */
  attachmentsNotStored?: string[];
  /**
   * Task 141 review: the file names the save was asked to add that the stored email holds but no email save ever creates a
   * document for (a calendar file, a blocked type, an inline or signature image, no content). OMITTED when there are none.
   * Read through {@link readAttachmentsNotSaveable}.
   */
  attachmentsNotSaveable?: string[];
  /**
   * The saved document. Task 141: an add to a saved email that adds nothing (every name already saved, or only names the
   * stored email does not hold) answers 200 with `duplicate: true`, NO job, and this naming the saved `.eml` — the save
   * completes on it directly.
   */
  artifact?: JobResultArtifact;
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
 * Where a Word Document save goes (FR-11, spaarkeai-word-add-in-r1 task 024).
 *
 * - `create` — a new `sprk_document` (the only behavior before task 024).
 * - `version` — a new SPE version of the EXISTING `sprk_document` task 013 resolved, via task 023's
 *   server path (`document.existingDocumentId` + `document.isNewVersion: true`). One row before and after.
 *
 * - `addAttachments` — task 141: add attachments to an email ALREADY saved in Spaarke, as children of its `.eml`
 *   (`email.savedDocumentId`). Nothing else is saved: no `.eml`, no upload, no re-filing. `record` is the record the
 *   `.eml` is filed to (sent as `targetEntity`; `entityType` is the friendly type, e.g. `Matter`), and
 *   `attachmentFileNames` are exactly the names to add (the ticked, unsaved ones) — sent as
 *   `email.selectedAttachmentFileNames`. Only the Email branch reads it.
 *
 * Only the Document branch reads `version`; an Outlook (Email) save never carries version fields.
 */
export type SaveTarget =
  | { mode: 'create' }
  | { mode: 'version'; existingDocumentId: string }
  | {
      mode: 'addAttachments';
      savedDocumentId: string;
      record: { entityType: string; id: string; name: string };
      attachmentFileNames: string[];
    };

/**
 * Save flow context data.
 */
export interface SaveFlowContext {
  /**
   * Where a Word Document save goes (task 024). Absent → `create`, exactly as before. Chosen by
   * `SaveFlow` from the resolved identity + the user's explicit override (`resolveSaveMode`).
   */
  saveTarget?: SaveTarget;
  /** Host type (outlook or word) */
  hostType: HostType;
  /**
   * Current item ID. For an email this is the Exchange item id (`item.itemId`), sent as `email.exchangeItemId` — it is
   * NOT the RFC Message-ID (see {@link internetMessageId}). For a Word document, its URL.
   */
  itemId?: string;
  /**
   * Task 121: the email's RFC 5322 Message-ID (`Office.context.mailbox.item.internetMessageId`), sent as
   * `email.internetMessageId`. Outlook read mode only — a compose item has none until it is sent.
   */
  internetMessageId?: string;
  /** Display name of the current item */
  itemName?: string;
  /** Custom document name (overrides itemName if provided) */
  documentName?: string;
  /** Available attachments (Outlook only) */
  attachments: AttachmentInfo[];
  /**
   * Email body content (Outlook only). NOT what the save sends: since task 116a the body is read live at submit
   * through `EmailContentCaptureContext` (see `hooks/emailContentCaptureContext.ts`). Kept for `SaveFlow`'s prop.
   */
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
  /**
   * Document content as a base64 string (Word only) — a plain VALUE, already in hand. Kept for
   * callers (tests, or any future non-live caller) that already have bytes; production `SaveFlow`
   * supplies {@link captureDocumentContent} instead, which `startSave` prefers when present (task 045).
   */
  documentContentBase64?: string;
  /**
   * Task 045: reads the document's CURRENT bytes at the moment `startSave` actually submits — never
   * captured earlier and cached. A LIVE function (bound to the open document by the caller, e.g.
   * `SaveView`'s `hostAdapter.getDocumentContent()`), not a snapshot — so every attempt that reaches
   * `startSave` re-invokes it and reads whatever is on screen right now. That covers the first Save,
   * `retry()` (which resends this SAME context object, so the same live function runs again), and
   * "Save Another" (which rebuilds a fresh context on the next Save press). When present, `startSave`
   * calls this INSTEAD OF reading the plain {@link documentContentBase64} value. A rejection here
   * surfaces as an ordinary handled save error (the existing `catch` in `startSave`,
   * `createErrorFromException`) — nothing is uploaded.
   */
  captureDocumentContent?: () => Promise<string>;
  /**
   * Task 025: "Keep both" — retries a CREATE save that was just refused with a filename collision
   * (OFFICE_020), asking the server to upload under a Graph-chosen non-colliding name instead of
   * refusing again. Ignored on a version save (`saveTarget.mode === 'version'`): that path never
   * collides by name. Absent/false means "refuse and ask" (this save's normal, non-retry behavior).
   */
  allowRename?: boolean;
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
  /**
   * Callback when save is complete. The third argument is passed ONLY when the save succeeded but the email it reconciled
   * to could not be filed to the chosen record (task 131) — otherwise the call has exactly two arguments, as before.
   * The fourth is passed ONLY when the save added attachments to an email already saved to the record (task 138): how
   * many it is adding (the third is then the filing problem or `undefined`). The fifth is passed ONLY when the save was asked
   * to add attachments the stored email does not hold: their names (the fourth is then `undefined` unless some are added).
   */
  onComplete?: (
    documentId: string,
    documentUrl: string,
    emailFilingProblem?: EmailFilingProblem,
    attachmentsAdded?: number,
    attachmentsNotStored?: string[]
  ) => void;
  /** Callback on error */
  onError?: (error: ErrorMessage) => void;
  /** Callback on duplicate detected */
  onDuplicate?: (documentId: string, message: string) => void;
  /**
   * Task 141 review (F2): called once an add-attachments save's job has FINISHED creating documents — its status read
   * reports phase `AttachmentsAdded` or `AttachmentsIncomplete` (or anything but `AttachmentsQueued`), or it can no longer
   * be watched (the watch's cap, or repeated read failures). `names` are the names that add's answer said it queued. The
   * save itself completed earlier (`onComplete`), when the add was queued: only now can the saved names be re-read.
   */
  onAttachmentsAddFinished?: (documentId: string, names: string[]) => void;
}

/**
 * Task 141 review (F2): how many status reads an add's watch makes, at the polling interval, before it stops waiting and
 * reports the add finished anyway (the caller then re-reads what is saved, which is the truth whatever the job says).
 */
export const ATTACHMENTS_ADD_WATCH_MAX_POLLS = 40;

/** Task 141 review (F2): the phase an add's job reads while its worker has not finished. */
const ATTACHMENTS_QUEUED_PHASE = 'AttachmentsQueued';

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
  /**
   * Task 141 review: what the SERVER answered about the attachments of the last save that completed: the names it is adding
   * (`attachmentsAdded`) and the names it can never save (`attachmentsNotSaveable`), each empty when it said none. Never
   * what the pane sent: the server may dedup to an existing document or leave names out. A stable function, safe to call
   * from a callback.
   */
  getAttachmentsAnswer: () => SaveAttachmentsAnswer;
}

/** Task 141 review: the server's answer about a save's attachments (see {@link UseSaveFlowResult.getAttachmentsAnswer}). */
export interface SaveAttachmentsAnswer {
  added: string[];
  notSaveable: string[];
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
 * The record types a Save can file to, and that its "+ New" can create: the ONE list for both pickers in
 * `SaveFlow`, the restored last association and the select backstops.
 *
 * Task 133 slice C (owner go 2026-11-20): Invoice left Save and "+ New" in both hosts. An invoice is created only by
 * an invoice submission (the Outlook Invoice tab, ebilling's from-email route), never as a Save target or a blank
 * quick-created record. Invoices are still FOUND and still shown as the record a document is filed to (Find, the
 * Related-to card), because reading one is unchanged.
 */
export const SAVE_TARGET_TYPES: readonly EntityType[] = ['Matter', 'Project'];

/** True when Save can file to (and "+ New" can create) a record of this type. */
export function isSaveTargetType(entityType: string | undefined | null): boolean {
  return SAVE_TARGET_TYPES.includes(entityType as EntityType);
}

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
    const entity = stored ? (JSON.parse(stored) as EntitySearchResult) : null;
    // Task 084: this restore is a pre-selection path, and a record the caller cannot file to is never
    // pre-selected (the save would refuse it). Task 133 slice C: nor a record Save no longer files to (an invoice
    // remembered from before Invoice left Save would otherwise come back pre-selected).
    if (!entity || entity.canFile === false || !isSaveTargetType(entity.entityType)) return null;
    return entity;
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
 * The parts of a VERSION save (FR-11, task 024) that make its idempotency key distinct.
 *
 * Mirrors what task 023 made the SERVER's key discriminate on for a version save
 * (`OfficeService.GenerateIdempotencyKey`: `…|FileName|ExistingDocumentId|version-content:{sha256}`):
 * - `existingDocumentId` — a version save and a fresh save of the same file name never share a key;
 * - `contentSha256` — two different revisions of the same document are two operations, while an
 *   identical re-send (same bytes) still de-duplicates;
 * - `failedAttempts` — how many version-save attempts have FAILED in this pane session. A failed attempt
 *   leaves a failed `sprk_processingjob` row behind carrying its key, and the server's persistent
 *   idempotency lookup answers any later request with that key as a "duplicate" of the FAILED job — so a
 *   retry after, say, a 423 lock would loop on the old failure forever. The counter only grows, so a
 *   retry never reproduces a failed attempt's key, and a completed attempt's key is only ever re-sent
 *   for identical content (a true duplicate).
 */
export interface VersionIdempotencyParts {
  existingDocumentId: string;
  fileName: string;
  contentSha256: string;
  failedAttempts: number;
}

/**
 * Task 141: the parts of an ADD-ATTACHMENTS save that make its idempotency key distinct. The route's response cache
 * replays an answer for a repeated `X-Idempotency-Key`, so without these an add could share the key of an ordinary save
 * of the same email to the same record with the same ticks and be answered with THAT save's response. `failedAttempts`
 * plays the same part as in {@link VersionIdempotencyParts}: a retry after a failed add is a new attempt.
 */
export interface AddAttachmentsIdempotencyParts {
  savedDocumentId: string;
  attachmentFileNames: string[];
  failedAttempts: number;
}

/**
 * The canonical string an idempotency key is the SHA-256 of. Exported for its unit tests.
 *
 * For every save that is NOT a version save (or, task 141, an add-attachments save) the string is byte-for-byte what it
 * was before task 024 (the `version` / `addAttachments` members are absent, and `JSON.stringify` emits nothing for them),
 * matching task 023's rule that "the canonical string for every other request is unchanged".
 */
export function buildIdempotencyCanonical(
  request: SaveRequest,
  version?: VersionIdempotencyParts,
  addAttachments?: AddAttachmentsIdempotencyParts
): string {
  return JSON.stringify({
    sourceType: request.sourceType,
    associationType: request.associationType,
    associationId: request.associationId,
    emailId: request.content.emailId,
    attachmentIds: request.content.attachmentIds?.sort(),
    includeBody: request.content.includeBody,
    documentUrl: request.content.documentUrl,
    ...(version
      ? {
          version: {
            existingDocumentId: version.existingDocumentId,
            fileName: version.fileName,
            contentSha256: version.contentSha256,
            ...(version.failedAttempts > 0 ? { failedAttempts: version.failedAttempts } : {}),
          },
        }
      : {}),
    ...(addAttachments
      ? {
          addAttachments: {
            savedDocumentId: addAttachments.savedDocumentId,
            attachmentFileNames: [...addAttachments.attachmentFileNames].sort(),
            ...(addAttachments.failedAttempts > 0 ? { failedAttempts: addAttachments.failedAttempts } : {}),
          },
        }
      : {}),
  });
}

/**
 * `JSON.stringify` replacer for the request log: the email's text and every file's base64 are replaced by their
 * length. The log is for the request's SHAPE; it must not copy a whole email or attachment into the console
 * (task 116a — the email body and attachments now travel in the request).
 */
function redactContent(key: string, value: unknown): unknown {
  if ((key === 'body' || key === 'contentBase64') && typeof value === 'string') {
    return `[${value.length} chars]`;
  }
  return value;
}

/** Lowercase-hex SHA-256 of a string's UTF-8 bytes. */
async function sha256Hex(text: string): Promise<string> {
  const hashBuffer = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text));
  return Array.from(new Uint8Array(hashBuffer))
    .map(b => b.toString(16).padStart(2, '0'))
    .join('');
}

/**
 * Compute idempotency key (SHA-256 of the canonical payload — see {@link buildIdempotencyCanonical}).
 */
export async function computeIdempotencyKey(
  request: SaveRequest,
  version?: VersionIdempotencyParts,
  addAttachments?: AddAttachmentsIdempotencyParts
): Promise<string> {
  return sha256Hex(buildIdempotencyCanonical(request, version, addAttachments));
}

/**
 * Shape of the `/api/office/save` response body when the server reports a duplicate
 * (`response.status === 200 && duplicate === true`). Narrows the parsed JSON body — typed
 * `unknown` on purpose, since it comes straight off the wire — via a real runtime discriminant
 * rather than a cast.
 */
interface DuplicateSaveResponse {
  duplicate: true;
  documentId: string;
  message?: string;
  /** Task 138: names the re-save asked to add that the stored email does not hold (read through `readAttachmentsNotStored`). */
  attachmentsNotStored?: unknown;
  /** Task 141 review: names no email save ever creates (read through `readAttachmentsNotSaveable`). */
  attachmentsNotSaveable?: unknown;
}

function isDuplicateSaveResponse(value: unknown): value is DuplicateSaveResponse {
  return (
    typeof value === 'object' &&
    value !== null &&
    (value as Record<string, unknown>).duplicate === true &&
    typeof (value as Record<string, unknown>).documentId === 'string'
  );
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
 *   getAccessToken: () => authService.getAccessToken(),
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
    onAttachmentsAddFinished,
  } = options;

  // State
  const [flowState, setFlowState] = useState<SaveFlowState>('idle');
  const [selectedEntity, setSelectedEntityState] = useState<EntitySearchResult | null>(() => getLastAssociation());
  const [selectedAttachmentIds, setSelectedAttachmentIds] = useState<Set<string>>(new Set());
  const [includeBody, setIncludeBody] = useState(true);
  const [processingOptions, setProcessingOptions] = useState<ProcessingOptions>(DEFAULT_PROCESSING_OPTIONS);
  const [jobStatus, setJobStatus] = useState<JobStatus | null>(null);
  const [error, setError] = useState<ErrorMessage | null>(null);
  const [duplicateInfo, setDuplicateInfo] = useState<{
    documentId: string;
    message: string;
  } | null>(null);
  const [savedDocumentId, setSavedDocumentId] = useState<string | null>(null);
  const [savedDocumentUrl, setSavedDocumentUrl] = useState<string | null>(null);

  // Task 116a: reads the email's body + attachments at submit, when the pane supplies a reader (SaveView).
  const captureEmailContent = useContext(EmailContentCaptureContext);

  // Refs
  const sseConnectionRef = useRef<SseConnection | null>(null);
  const pollingIntervalRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const savedContextRef = useRef<SaveFlowContext | null>(null);
  const abortControllerRef = useRef<AbortController | null>(null);
  const pollingRetryCountRef = useRef<number>(0);
  const maxPollingRetries = 3;
  // Task 060: the job's terminal outcome is applied ONCE. Polling and SSE both track the job, and since task 060 the
  // SSE completion branch is live, so a stream event and an in-flight poll response can both reach a terminal branch.
  // Whichever arrives first wins; the other is ignored. Reset when tracking starts.
  const terminalOutcomeAppliedRef = useRef(false);
  const claimTerminalOutcome = useCallback((): boolean => {
    if (terminalOutcomeAppliedRef.current) return false;
    terminalOutcomeAppliedRef.current = true;
    return true;
  }, []);
  // FR-11 (task 024): whether the save in flight is a VERSION save, and how many version-save attempts have
  // FAILED in this pane session (see VersionIdempotencyParts.failedAttempts). Deliberately NOT reset by
  // reset(): the counter must only grow, or a later identical re-send could reproduce a failed attempt's
  // key and be answered with that failed job.
  const activeVersionSaveRef = useRef(false);
  const versionFailuresRef = useRef(0);
  // The existing document an in-flight VERSION save targets. By contract (task 023) a version save's resulting
  // document IS that document, so completion can name it even when the job record carries no artifact — which
  // the synchronous save path never sets (only the AI workers do, later, over SSE).
  // Task 141: the same fallback serves an ADD-ATTACHMENTS save — its resulting document is the saved .eml it named.
  const activeVersionDocumentIdRef = useRef<string | null>(null);
  // Task 141: whether the save in flight is an add-attachments save, and how many have FAILED in this pane session (its
  // key's failedAttempts, as for a version save; never reset).
  const activeAddSaveRef = useRef(false);
  const addFailuresRef = useRef(0);
  // Task 141 review: the server's answer about the current save's attachments (see getAttachmentsAnswer). Cleared with
  // emailFilingProblemRef.
  const attachmentsAnswerRef = useRef<SaveAttachmentsAnswer>({ added: [], notSaveable: [] });
  const getAttachmentsAnswer = useCallback(() => attachmentsAnswerRef.current, []);
  // Task 131 (#2171): the refused / failed email filing the CURRENT save's 202 reported, handed to onComplete when the job
  // completes. Null = nothing to tell the user. Cleared when a save starts and on reset.
  const emailFilingProblemRef = useRef<EmailFilingProblem | null>(null);
  // Task 138 (#2209): how many attachments the CURRENT save's 202 says it added to the already-saved email. 0 = none.
  // Cleared with emailFilingProblemRef.
  const attachmentsAddedRef = useRef(0);
  // Task 138 (#2209): the names the CURRENT save was asked to add that the stored email does not hold. Empty = none.
  const attachmentsNotStoredRef = useRef<string[]>([]);
  // Task 141 review (F2): the job of the CURRENT add-attachments save, when its answer queued names; read (and cleared)
  // when that save completes, to start watching the job until the add has finished. Cleared when a save starts.
  const pendingAddJobRef = useRef<{ jobId: string; names: string[] } | null>(null);
  // Task 141 review (F2): the add jobs being watched, by job id → the timer of the next read. Several adds can be watched at
  // once; a new save never stops an earlier add's watch. Stopped by reset() and on unmount.
  const addWatchesRef = useRef(new Map<string, ReturnType<typeof setTimeout>>());
  const onAttachmentsAddFinishedRef = useRef(onAttachmentsAddFinished);
  onAttachmentsAddFinishedRef.current = onAttachmentsAddFinished;
  const stopAddWatches = useCallback(() => {
    for (const timer of addWatchesRef.current.values()) clearTimeout(timer);
    addWatchesRef.current.clear();
  }, []);
  // Task 141 review (F2): reads an add's job until its worker has finished (any phase but `AttachmentsQueued`, or a failed
  // job), then reports it. The first read is immediate (the worker may be done already). It also reports after
  // ATTACHMENTS_ADD_WATCH_MAX_POLLS reads, or `maxPollingRetries` failed reads in a row: the caller's re-read of what is
  // saved is the answer either way, and an add must never show "Adding…" forever.
  const watchAttachmentsAdd = useCallback(
    (jobId: string, documentId: string, names: string[]) => {
      if (addWatchesRef.current.has(jobId)) return;
      let reads = 0;
      let failuresInARow = 0;
      const finish = () => {
        addWatchesRef.current.delete(jobId);
        onAttachmentsAddFinishedRef.current?.(documentId, names);
      };
      const read = async () => {
        reads += 1;
        let finished = false;
        try {
          const accessToken = await getAccessToken();
          const response = await authenticatedJsonFetch(`${apiBaseUrl}/api/office/jobs/${jobId}`, {}, accessToken, {
            getRetryToken: getAccessToken,
          });
          if (!addWatchesRef.current.has(jobId)) return; // stopped while reading
          if (response.ok) {
            failuresInARow = 0;
            const job = (await response.json()) as JobStatus;
            finished =
              job.status === 'Failed' || job.status === 'Cancelled' || job.currentPhase !== ATTACHMENTS_QUEUED_PHASE;
          } else {
            failuresInARow += 1;
          }
        } catch {
          if (!addWatchesRef.current.has(jobId)) return;
          failuresInARow += 1;
        }
        if (finished || failuresInARow >= maxPollingRetries || reads >= ATTACHMENTS_ADD_WATCH_MAX_POLLS) {
          finish();
          return;
        }
        addWatchesRef.current.set(
          jobId,
          setTimeout(() => void read(), pollingIntervalMs)
        );
      };
      addWatchesRef.current.set(
        jobId,
        setTimeout(() => void read(), 0)
      );
    },
    [apiBaseUrl, getAccessToken, pollingIntervalMs]
  );
  const notifyComplete = useCallback(
    (documentId: string, documentUrl: string) => {
      const addJob = pendingAddJobRef.current;
      pendingAddJobRef.current = null;
      if (addJob) watchAttachmentsAdd(addJob.jobId, documentId, addJob.names);
      const problem = emailFilingProblemRef.current;
      const added = attachmentsAddedRef.current;
      const notStored = attachmentsNotStoredRef.current;
      if (notStored.length > 0)
        onComplete?.(documentId, documentUrl, problem ?? undefined, added > 0 ? added : undefined, notStored);
      else if (added > 0) onComplete?.(documentId, documentUrl, problem ?? undefined, added);
      else if (problem) onComplete?.(documentId, documentUrl, problem);
      else onComplete?.(documentId, documentUrl);
    },
    [onComplete, watchAttachmentsAdd]
  );

  // Computed values
  const isSaving = flowState === 'uploading' || flowState === 'processing';
  // Association is optional - user can save without selecting an entity
  const isValid = true;

  // Set selected entity and save to storage
  const setSelectedEntity = useCallback((entity: EntitySearchResult | null) => {
    // Task 133 slice C: the save's record is only ever a Save target type, whatever path asks (the pickers offer
    // nothing else; this is the hook-level backstop, as the canFile check in SaveFlow is the pane-level one).
    if (entity && !isSaveTargetType(entity.entityType)) return;
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
    stopAddWatches();
    pendingAddJobRef.current = null;
    setFlowState('idle');
    setSelectedAttachmentIds(new Set());
    setIncludeBody(true);
    setProcessingOptions(DEFAULT_PROCESSING_OPTIONS);
    setJobStatus(null);
    setError(null);
    setDuplicateInfo(null);
    setSavedDocumentId(null);
    setSavedDocumentUrl(null);
    emailFilingProblemRef.current = null;
    attachmentsAddedRef.current = 0;
    attachmentsNotStoredRef.current = [];
    attachmentsAnswerRef.current = { added: [], notSaveable: [] };
    savedContextRef.current = null;
  }, [cleanup, stopAddWatches]);

  // Task 060 (#1084): a job reported Completed that names no document, on a save with no version target to fall back
  // on. It matched neither completion branch, so the pane sat on "processing" forever with no error. It is a terminal
  // outcome the user must see. Not recoverable from here: the save may well have written the document, and a blind
  // retry is exactly what the duplicate check exists to absorb.
  const failUnconfirmedCompletion = useCallback(() => {
    const errorMsg: ErrorMessage = {
      title: 'Save Not Confirmed',
      message:
        'The save finished, but Spaarke could not confirm which document it created. Check the record before saving again.',
      type: 'error',
      recoverable: false,
    };
    setError(errorMsg);
    setFlowState('error');
    onError?.(errorMsg);
  }, [onError]);

  // Handle SSE event
  const handleSseEvent = useCallback(
    (event: SseEvent) => {
      // The server's event names are SseHelper.EventTypes: connected, progress, stage-update, job-complete, job-failed,
      // heartbeat, error. Task 060: this handler listened for 'stage' / 'complete' / 'failed', which the server never
      // sends, so its completion branch could not fire. Both spellings are accepted.
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

          return {
            ...prev,
            ...(progress !== undefined ? { progress } : {}),
            ...(currentPhase !== undefined ? { currentPhase } : {}),
            ...(stages !== undefined ? { stages } : {}),
          };
        });
      } else if (eventType === 'stage' || eventType === 'stage-update') {
        // Stage update: { stage: string, status: string, timestamp: string }
        const stageName = data.stage as string;
        const stageStatus = data.status as string;
        const timestamp = data.timestamp as string;

        setJobStatus(prev => {
          if (!prev) return null;
          const stages = prev.stages?.map(s =>
            s.name === stageName
              ? {
                  ...s,
                  status: stageStatus as StageStatus['status'],
                  completedAt: timestamp,
                }
              : s
          );
          return { ...prev, ...(stages !== undefined ? { stages } : {}) };
        });
      } else if (eventType === 'complete' || eventType === 'job-complete') {
        if (!claimTerminalOutcome()) return;
        // Completion: { jobId: string, documentId?: string, documentUrl?: string }. A VERSION save falls back to
        // the document it named (task 024) — see activeVersionDocumentIdRef.
        const documentId = (data.documentId as string | undefined) ?? activeVersionDocumentIdRef.current ?? undefined;
        const documentUrl = data.documentUrl as string | undefined;

        if (documentId) {
          setSavedDocumentId(documentId);
          setSavedDocumentUrl(documentUrl || null);
          setFlowState('complete');
          notifyComplete(documentId, documentUrl || '');
        } else {
          failUnconfirmedCompletion();
        }
        cleanup();
      } else if (eventType === 'failed' || eventType === 'job-failed' || eventType === 'error') {
        // Error: { errorCode: string, errorMessage: string, retryable?: boolean } (SseHelper's JobFailedPayload and
        // ErrorPayload). `message` is the older spelling, kept.
        if (!claimTerminalOutcome()) return;
        if (activeVersionSaveRef.current) {
          versionFailuresRef.current += 1; // a retry must not reuse the failed job's key (task 024)
        }
        if (activeAddSaveRef.current) addFailuresRef.current += 1; // the same for an add (task 141)
        const errorMsg: ErrorMessage = {
          title: 'Processing Failed',
          message: (data.message as string) || (data.errorMessage as string) || 'An error occurred during processing.',
          type: 'error',
          recoverable: (data.retryable as boolean) ?? true,
        };
        setError(errorMsg);
        setFlowState('error');
        onError?.(errorMsg);
        cleanup();
      }
      // Ignore heartbeat events
    },
    [claimTerminalOutcome, cleanup, failUnconfirmedCompletion, notifyComplete, onError]
  );

  // Poll for job status
  //
  // Auth v2 (D-AUTH-7 / task 082): `getAccessToken` is invoked PER POLL — not
  // snapshotted at startSave time. Polling can run for minutes (long-running jobs);
  // the previous snapshot-once pattern would silently 401 once the cached token
  // expired. Each call delegates to the caller's provider cache (MSAL NAA / silent),
  // so this is cheap when the token is still warm.
  //
  // Task 040 / FR-B0: this is plain JSON (not SSE/XHR), so it is NOT a D-AUTH-7
  // exception site — it now routes through `authenticatedJsonFetch` for a
  // single-retry-on-401, same as every other JSON BFF call in this package. The
  // hook still takes an injected `getAccessToken` (no hard `@spaarke/auth`
  // import) — `authenticatedJsonFetch` re-invokes that same getter for the
  // retry attempt.
  const pollJobStatus = useCallback(
    async (jobId: string) => {
      try {
        const accessToken = await getAccessToken();
        const response = await authenticatedJsonFetch(
          `${apiBaseUrl}/api/office/jobs/${jobId}`,
          abortControllerRef.current?.signal ? { signal: abortControllerRef.current.signal } : {},
          accessToken,
          { getRetryToken: getAccessToken }
        );

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
              ...(phase?.completedAt !== undefined ? { completedAt: phase.completedAt } : {}),
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

        // Extract document info from result.artifact. For a VERSION save, fall back to the document it named
        // (task 024): the synchronous save path completes the job without an artifact, and without this the pane
        // would stop polling on "Completed" with no way forward.
        const documentId = rawStatus.result?.artifact?.id ?? activeVersionDocumentIdRef.current ?? undefined;
        const documentUrl = rawStatus.result?.artifact?.webUrl;

        const status: JobStatus = {
          ...rawStatus,
          stages: mappedStages,
          ...(documentId !== undefined ? { documentId } : {}),
          ...(documentUrl !== undefined ? { documentUrl } : {}),
        };

        setJobStatus(status);

        // Check for completion
        if (status.status === 'Completed' || status.status === 'Failed') {
          if (!claimTerminalOutcome()) return;
          if (status.status === 'Completed' && documentId) {
            setSavedDocumentId(documentId);
            setSavedDocumentUrl(documentUrl || null);
            setFlowState('complete');
            notifyComplete(documentId, documentUrl || '');
          } else if (status.status === 'Completed') {
            failUnconfirmedCompletion();
          } else if (status.status === 'Failed') {
            if (activeVersionSaveRef.current) {
              versionFailuresRef.current += 1; // a retry must not reuse the failed job's key (task 024)
            }
            if (activeAddSaveRef.current) addFailuresRef.current += 1; // the same for an add (task 141)
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
    },
    [apiBaseUrl, claimTerminalOutcome, cleanup, failUnconfirmedCompletion, getAccessToken, notifyComplete, onError]
  );

  // Start SSE connection with polling fallback.
  //
  // Auth v2 (D-AUTH-7 / task 082): no longer accepts an `accessToken` snapshot.
  // The downstream consumers (`pollJobStatus` + `createSseConnection`) each call
  // `getAccessToken()` per request, so the token used for any given poll or
  // reconnect is always the freshest one available from the provider cache.
  const startJobTracking = useCallback(
    async (jobId: string, streamUrl: string) => {
      setFlowState('processing');

      // Reset retry counter and the terminal-outcome claim at start of tracking
      pollingRetryCountRef.current = 0;
      terminalOutcomeAppliedRef.current = false;

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
        pollJobStatus(jobId);
      }, pollingIntervalMs);

      // Also do an immediate poll to get current status
      pollJobStatus(jobId);

      // Try SSE as enhancement (provides faster updates when available).
      // Auth v2 (D-AUTH-7): pass the `getAccessToken` getter (not a snapshot).
      // The SSE client invokes it immediately before each fetch — including any
      // 401-driven reconnects — so the token is always fresh for THIS stream.
      try {
        sseConnectionRef.current = createSseConnection(`${apiBaseUrl}${streamUrl}`, {
          getAccessToken,
          onEvent: handleSseEvent,
          onError: err => {
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
    },
    [apiBaseUrl, getAccessToken, handleSseEvent, pollJobStatus, pollingIntervalMs, processingOptions, sseTimeoutMs]
  );

  // Start save operation
  const startSave = useCallback(
    async (context: SaveFlowContext) => {
      // Association is optional - user can save without selecting an entity

      // Save context for retry
      savedContextRef.current = context;
      setFlowState('uploading');
      setError(null);
      setDuplicateInfo(null);

      // Cancel any existing operations
      cleanup();
      abortControllerRef.current = new AbortController();

      // FR-11 (task 024): whether THIS attempt is a version save. Declared before the request is built so every
      // failure path below — a ProblemDetails refusal, a thrown error — can count it.
      let isVersionAttempt = false;
      activeVersionSaveRef.current = false;
      activeVersionDocumentIdRef.current = null;
      activeAddSaveRef.current = false;
      attachmentsAnswerRef.current = { added: [], notSaveable: [] };
      pendingAddJobRef.current = null;
      emailFilingProblemRef.current = null;
      attachmentsAddedRef.current = 0;
      attachmentsNotStoredRef.current = [];

      try {
        // Auth v2 (D-AUTH-7 / task 082): no top-level token snapshot.
        // The token used for the POST below is acquired inline immediately
        // before the fetch (see `await getAccessToken()` at the POST call).
        // `startJobTracking` no longer receives an accessToken either — it
        // closes over `getAccessToken` so polls + SSE always pull a fresh token.

        // Determine content type for server API
        // Note: only 'Email' and 'Document' are ever assigned below — Outlook always saves as Email
        // (selectedAttachmentFileNames controls which attachments become Documents server-side); there
        // is no client-side 'Attachment' content type. The type reflects that (finding recorded in
        // notes/typecheck-fix-patterns.md — a stale 'Attachment'-only branch was removed as unreachable).
        //
        // task 040 / FR-19 audit: `context.hostType` here selects WHICH REQUEST SHAPE to build
        // (Email vs Document) — host-shaped DATA, not a feature on/off gate — so it is intentionally
        // NOT converted to a capability read (see the "which save content type to send" example in
        // the task's own scoping note). See notes/parity-checklist.md.
        let contentType: 'Email' | 'Document';
        if (context.hostType === 'outlook') {
          // Always save as Email when in Outlook
          // The selectedAttachmentFileNames array controls which attachments become Documents
          contentType = 'Email';
        } else {
          contentType = 'Document';
        }

        // Use custom documentName if provided, otherwise fall back to itemName
        const effectiveDocumentName = context.documentName || context.itemName;

        // FR-11 (task 024): a VERSION save is a Word Document save whose target is an existing sprk_document
        // (task 013's resolved identity, unless the user chose "a new document"). Only the Document branch can
        // carry it — an Outlook Email save never sends version fields, whatever the context says.
        const versionTarget =
          contentType === 'Document' && context.saveTarget?.mode === 'version' ? context.saveTarget : undefined;
        // ADR-044: canonical bare-lowercase via the shared cleanGuid BEFORE the id leaves the client (the server
        // binds it as a typed Guid and formats it "D" again at its own boundary).
        const existingDocumentId = versionTarget ? cleanGuid(versionTarget.existingDocumentId) : undefined;
        if (versionTarget && (!existingDocumentId || !CANONICAL_GUID.test(existingDocumentId))) {
          // Never fall back to creating: a version save that cannot name its document is refused here.
          throw new Error('The existing Spaarke document could not be identified, so no version was saved.');
        }
        isVersionAttempt = versionTarget !== undefined;
        activeVersionSaveRef.current = isVersionAttempt;
        activeVersionDocumentIdRef.current = existingDocumentId ?? null;

        // Task 141: an ADD-ATTACHMENTS save names the saved .eml and the record it is filed to. Only the Email branch
        // carries it. ADR-044: the id is canonicalized before it leaves the client; an id that is not a GUID, or an add with
        // no names, is refused here — it never falls back to saving the email again.
        const addTarget =
          contentType === 'Email' && context.saveTarget?.mode === 'addAttachments' ? context.saveTarget : undefined;
        const savedEmailDocumentId = addTarget ? cleanGuid(addTarget.savedDocumentId) : undefined;
        if (addTarget) {
          if (!savedEmailDocumentId || !CANONICAL_GUID.test(savedEmailDocumentId)) {
            throw new Error('The saved email could not be identified, so no attachments were added.');
          }
          if (addTarget.attachmentFileNames.length === 0) {
            throw new Error('Select at least one attachment to add.');
          }
          activeAddSaveRef.current = true;
          activeVersionDocumentIdRef.current = savedEmailDocumentId;
        }

        // The "Related to" record actually sent. NOT on a version save: that path writes to the existing
        // document's own item and never re-associates it (task 023 D-4/D-5), so a picker selection would be
        // inert — and would add an unrelated authorization check (EntityAccessFilter) that could refuse a
        // legitimate version save. Task 141: NOT the picker's either on an add — it is filed to the saved email's record.
        const sentEntity = versionTarget || addTarget ? null : selectedEntity;
        let documentFileName: string | undefined;
        // Task 045: the bytes actually sent for a Document save — resolved below, at submission time,
        // from `context.captureDocumentContent()` when supplied (the live path) or the plain
        // `context.documentContentBase64` value otherwise. Declared here so the idempotency-key section
        // further down (which hashes this same content) can read it without re-invoking the capture.
        let documentContentForRequest: string | undefined;

        // Build request in server-expected format
        // Server requires: contentType, targetEntity, and type-specific metadata
        //
        // Task 020 (FR-06): the top-level `documentMetadata: { name, description }` object this used to send
        // is REMOVED — `SaveRequest` (server) has no such member, so System.Text.Json silently dropped it on
        // every prior request (dead weight on the wire). The user-facing name already travels correctly per
        // content type: `document.title` (Document, mapped server-side to sprk_documentname as of task 020)
        // and `email.documentName` (Email, #2416; `email.subject` stays the email's own). No replacement field is needed.
        const serverRequest: Record<string, unknown> = {
          contentType,
          triggerAiProcessing:
            processingOptions.deepAnalysis || processingOptions.ragIndex || processingOptions.profileSummary,
          aiOptions: {
            profileSummary: processingOptions.profileSummary,
            ragIndex: processingOptions.ragIndex,
            deepAnalysis: processingOptions.deepAnalysis,
          },
        };

        // Add target entity if an association was selected (never on a version save — see `sentEntity`)
        if (sentEntity?.id) {
          serverRequest.targetEntity = {
            entityType: sentEntity.entityType,
            entityId: sentEntity.id,
            displayName: sentEntity.name,
          };
        } else if (addTarget) {
          // Task 141: the record the saved .eml is filed to. The route checks the caller may file to it, and the server
          // refuses (OFFICE_009) when the .eml is not filed to it.
          serverRequest.targetEntity = {
            entityType: addTarget.record.entityType,
            entityId: cleanGuid(addTarget.record.id),
            displayName: addTarget.record.name,
          };
        }

        // Add content-type-specific metadata
        if (contentType === 'Email') {
          // Build recipients array for server (matches EmailMetadata.Recipients model)
          // Server expects: { type: 'To'|'Cc'|'Bcc', email: string, name?: string }
          const recipients =
            context.recipients?.map(r => ({
              type: r.type === 'to' ? 'To' : r.type === 'cc' ? 'Cc' : 'Bcc',
              email: r.email,
              name: r.displayName,
            })) || [];

          // Task 116a: the body and the attachments (ALL of them — the .eml is the complete email; only the TICKED ones
          // become Documents, via selectedAttachmentFileNames) are read HERE, in the add-in, at the moment this save
          // submits (first Save and every retry). The server used to fetch them from the mailbox through Graph, which
          // cannot reach a B2B guest's home-tenant mailbox — a guest's save stored a header-only .eml and no
          // attachment documents. The server already prefers content the client sends and falls back to Graph only
          // when the body is absent, which is what still happens when no reader is provided (older hosts, tests).
          // A read failure THROWS here, inside the existing try/catch, so it surfaces as an ordinary save error and
          // nothing is sent. Attachments that cannot be carried (too large, cloud links) come back in
          // `captured.skipped`, which the provider (SaveView) shows; the rest of the save goes ahead.
          //
          // Task 141: an ADD reads the body only — no attachment bytes. The server creates the children from the STORED
          // .eml and never from request bytes; the body is sent so the server's own content check (it falls back to a
          // Graph read when the body is absent) takes the client-content path, as for every pane save.
          const captured: EmailContentCapture | undefined = captureEmailContent
            ? addTarget
              ? await captureEmailContent([], new Set())
              : await captureEmailContent(context.attachments, selectedAttachmentIds)
            : undefined;

          // Which attachments become Documents. Always sent when the email has attachments (an empty array means
          // "create no Documents"); undefined only when it has none at all. With a capture, these are the names
          // of the attachments actually sent (a skipped one is not in the .eml, so it cannot become a Document).
          // Task 141: an add sends exactly the names it was given (the ticked, unsaved ones).
          const selectedAttachmentFileNames = addTarget
            ? [...addTarget.attachmentFileNames]
            : captured
              ? captured.selectedAttachmentFileNames
              : context.attachments.length > 0
                ? Array.from(selectedAttachmentIds)
                    .map(id => context.attachments.find(a => a.id === id)?.name)
                    .filter((name): name is string => !!name)
                : undefined;

          const typedEmailName = context.documentName?.trim();
          serverRequest.email = {
            // #2416: always the email's own subject — it is the saved .eml's Subject header, sprk_emailsubject and the
            // communication's subject. Before #2416 the typed Document Name travelled here and replaced all three.
            subject: context.itemName || 'Untitled Email',
            // #2416: the Document Name the user typed names the .eml file only. Whitespace alone is no name.
            ...(typedEmailName ? { documentName: typedEmailName } : {}),
            // Task 046 (b): says whether the .eml name is the email's own subject (system-derived) or the Document
            // Name the user TYPED. The server stores a system-derived name with a short unique suffix, so two
            // same-subject, same-date emails never share a file. A typed name is stored exactly as typed.
            // #2416: "Keep both" after a typed-name collision asks for the suffix too. The server stores the file
            // under a unique name and sprk_documentname keeps the typed name: the email's form of a rename.
            isNameSystemDerived: !typedEmailName || context.allowRename === true,
            senderEmail: context.senderEmail || 'unknown@placeholder.com',
            senderName: context.senderDisplayName,
            recipients,
            sentDate: context.sentDate?.toISOString(),
            // Task 116a: read in the add-in. Undefined only without a reader — the server then fetches from Graph.
            body: captured?.body,
            isBodyHtml: true,
            // Task 141: an add carries no attachment bytes (see the capture above).
            ...(captured && !addTarget ? { attachments: captured.attachments } : {}),
            // Task 121: the RFC Message-ID, exactly as Quick Save sends it. The server stores it on the .eml
            // (Message-ID header, sprk_emailmessageid), keys the communication record and its idempotency on it, and
            // links the save to the communication that captured the same email. Absent when Office gives none (a
            // compose item). Before task 121 the pane sent the Exchange item id HERE.
            ...(context.internetMessageId ? { internetMessageId: context.internetMessageId } : {}),
            // Task 121: the Exchange item id, in its own field — the server's Graph fallback fetches the email by it
            // when no content was captured.
            ...(context.itemId ? { exchangeItemId: context.itemId } : {}),
            selectedAttachmentFileNames: selectedAttachmentFileNames, // Can be undefined, empty array, or array with names
            // Task 141: the saved .eml this save adds the attachments to. Absent on every other save.
            ...(savedEmailDocumentId ? { savedDocumentId: savedEmailDocumentId } : {}),
          };
        } else if (contentType === 'Document') {
          // Task 045: capture the document's bytes HERE — at the moment this save is actually
          // submitted — never earlier. `captureDocumentContent` (when supplied) is a LIVE function
          // bound to the open document (SaveView → hostAdapter.getDocumentContent()); calling it fresh
          // on every `startSave` is what makes the first Save, a `retry()` (which resends this same
          // context/callback), and "Save Another" (whose next Save press rebuilds the context) each
          // upload the document as it is right now, instead of a snapshot taken when the Save tab
          // mounted. Falls back to the plain `documentContentBase64` value for callers that already
          // have bytes in hand (tests). A capture failure THROWS here, inside the existing try/catch,
          // so it surfaces as an ordinary handled save error (`createErrorFromException` below) and
          // uploads nothing — never a silent old-bytes upload.
          documentContentForRequest = context.captureDocumentContent
            ? await context.captureDocumentContent()
            : context.documentContentBase64;
          if (!documentContentForRequest) {
            throw new Error('Document content is required. Please ensure the document is captured before saving.');
          }
          // Ensure a real .docx extension — SPE preview, Word open, Compose mount, and AI text
          // extraction all key off it. Without it the file is stored extensionless and treated as
          // an unknown/unsupported type (email-communication-intelligence-r2 UAT 2026-09-03).
          // Task 089: the rule lives in utils/documentFileName so the ribbon's Quick Save uses the same one.
          const docFileName = toDocxFileName(effectiveDocumentName);
          documentFileName = docFileName;
          serverRequest.document = {
            fileName: docFileName,
            title: effectiveDocumentName,
            contentType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
            contentBase64: documentContentForRequest,
            // FR-11 (task 024): the two version fields travel TOGETHER — the server refuses existingDocumentId
            // without isNewVersion:true (OFFICE_018). "Save as new document" sends neither. No versionComment:
            // the pane collects none (SharePoint Embedded keeps no readable version comment).
            ...(existingDocumentId ? { existingDocumentId, isNewVersion: true } : {}),
            // Task 025: "Keep both" only applies to a CREATE (never a version save, which cannot collide by
            // name) — the guard mirrors the existingDocumentId branch above rather than trusting the caller
            // not to set both.
            ...(!existingDocumentId && context.allowRename ? { allowRename: true } : {}),
          };
        }

        // Build legacy request for idempotency key computation (keep format stable)
        // task 040 / FR-19 audit: `context.hostType` below is the SAME host-shaped-data case as
        // `contentType` above — selecting which request shape's hash to compute, not gating a
        // feature — so it stays as-is (see notes/parity-checklist.md).
        const request: SaveRequest = {
          sourceType:
            context.hostType === 'outlook'
              ? selectedAttachmentIds.size > 0
                ? 'OutlookAttachment'
                : 'OutlookEmail'
              : 'WordDocument',
          associationType: sentEntity?.entityType || '',
          associationId: sentEntity?.id || '',
          content: {
            ...(context.itemId !== undefined ? { emailId: context.itemId } : {}),
            includeBody: includeBody && context.hostType === 'outlook',
            attachmentIds: Array.from(selectedAttachmentIds),
            ...(context.documentUrl !== undefined ? { documentUrl: context.documentUrl } : {}),
            ...(effectiveDocumentName !== undefined ? { documentName: effectiveDocumentName } : {}),
          },
          processing: processingOptions,
        };

        // Idempotency key. Every save except a version save computes it exactly as before (legacy format).
        // A VERSION save keys on its document + file name + content + failed-attempt count
        // (VersionIdempotencyParts), and ALSO sends that key in the body: the server's PERSISTENT job dedupe
        // uses `request.idempotencyKey` when present — the X-Idempotency-Key header only reaches the Redis
        // response cache (IdempotencyFilter) — so the body key is what stops a retry after a failure from being
        // answered with the failed job, and what makes two revisions of one document two operations.
        let idempotencyKey: string;
        if (existingDocumentId && documentFileName && documentContentForRequest) {
          idempotencyKey = await computeIdempotencyKey(request, {
            existingDocumentId,
            fileName: documentFileName,
            contentSha256: await sha256Hex(documentContentForRequest),
            failedAttempts: versionFailuresRef.current,
          });
          serverRequest.idempotencyKey = idempotencyKey;
        } else if (addTarget && savedEmailDocumentId) {
          // Task 141: an add keys on the saved .eml, its names and its failed attempts — never the key of an ordinary
          // save of the same email (see AddAttachmentsIdempotencyParts). Header only: the server needs no earlier job.
          idempotencyKey = await computeIdempotencyKey(request, undefined, {
            savedDocumentId: savedEmailDocumentId,
            attachmentFileNames: addTarget.attachmentFileNames,
            failedAttempts: addFailuresRef.current,
          });
        } else {
          idempotencyKey = await computeIdempotencyKey(request);
        }

        // Submit save request.
        // Auth v2 (D-AUTH-7, updated task 072): acquire token inline — no snapshot.
        // This hook stays decoupled from `@spaarke/auth` by design (injected
        // `getAccessToken`, not a hard import). Per-call `getAccessToken()`
        // eliminates staleness.
        // Task 040 / FR-B0: this is plain JSON, not SSE/XHR — routed through
        // `authenticatedJsonFetch` for a single-retry-on-401 (was a raw `fetch`
        // + Bearer literal with no retry).
        console.log('[SaveFlow] Sending request:', JSON.stringify(serverRequest, redactContent, 2));
        const saveToken = await getAccessToken();
        const response = await authenticatedJsonFetch(
          `${apiBaseUrl}/api/office/save`,
          {
            method: 'POST',
            headers: {
              'Content-Type': 'application/json',
              'X-Idempotency-Key': idempotencyKey,
            },
            body: JSON.stringify(serverRequest),
            signal: abortControllerRef.current.signal,
          },
          saveToken,
          { getRetryToken: getAccessToken }
        );

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
        if (response.status === 200 && isDuplicateSaveResponse(responseData)) {
          // Duplicate detected
          // Task 138 (#2209): a re-save that ticked attachments the stored email does not hold says so beside "already saved".
          const notStoredNotice = describeAttachmentsNotStored(
            readAttachmentsNotStored(responseData.attachmentsNotStored)
          );
          // Task 141 review: and names no save can ever create.
          const notSaveableNotice = describeAttachmentsNotSaveable(
            readAttachmentsNotSaveable(responseData.attachmentsNotSaveable)
          );
          const duplicateMessage = [
            responseData.message || 'This item was previously saved.',
            notStoredNotice,
            notSaveableNotice,
          ]
            .filter(Boolean)
            .join(' ');
          setDuplicateInfo({
            documentId: responseData.documentId,
            message: duplicateMessage,
          });
          setFlowState('duplicate');
          onDuplicate?.(responseData.documentId, duplicateMessage);
          return;
        }

        if (!response.ok) {
          // Error response
          if (isProblemDetails(responseData)) {
            if (isVersionAttempt) {
              versionFailuresRef.current += 1; // a retry must not reuse the failed job's key (task 024)
            }
            if (activeAddSaveRef.current) addFailuresRef.current += 1; // the same for an add (task 141)
            // A refused VERSION save is described in terms of the existing document and — when the refusal is
            // about that document — offers "Save as new document" as the way forward (task 024). A refused
            // CREATE save (task 025) is described in terms of the collision and offers the two-option choice
            // ("Keep both" always; "Save as new version" when the server resolved an existing document) — the
            // two describers are mutually exclusive by construction (isVersionAttempt), so there is no
            // ambiguity about which applies.
            const errorMsg = isVersionAttempt
              ? describeVersionSaveFailure(responseData)
              : describeCollisionFailure(responseData);
            setError(errorMsg);
            setFlowState('error');
            onError?.(errorMsg);
            return;
          }
          throw new Error(`Save failed: ${response.status}`);
        }

        // Success - start job tracking.
        // Auth v2 (D-AUTH-7 / task 082): no token passed; `startJobTracking`
        // closes over `getAccessToken` (function) so polls + SSE always pull
        // a fresh token from the provider cache.
        const saveResponse = responseData as SaveResponse;
        // Task 131 (#2171): the save worked, but its email may not have been filed to the record — kept for onComplete.
        emailFilingProblemRef.current = readEmailFilingProblem(saveResponse?.emailFiling);
        // Task 138 (#2209): a re-save that added attachments to the already-saved email — counted for onComplete.
        attachmentsAddedRef.current = readAttachmentsAddedCount(saveResponse?.attachmentsAdded);
        attachmentsNotStoredRef.current = readAttachmentsNotStored(saveResponse?.attachmentsNotStored);
        // Task 141 review: the names the server says it is adding, and the ones it can never save — the pane's only
        // source for marking attachments after an add.
        attachmentsAnswerRef.current = {
          added: readAttachmentsAdded(saveResponse?.attachmentsAdded),
          notSaveable: readAttachmentsNotSaveable(saveResponse?.attachmentsNotSaveable),
        };

        // Task 141: an answer with NO job that names the saved document completes here — nothing was queued to track
        // (an add whose names were all saved already, or none of which the stored email holds).
        const artifactId = saveResponse?.artifact?.id;
        if (!saveResponse?.jobId && typeof artifactId === 'string' && artifactId) {
          terminalOutcomeAppliedRef.current = true;
          setSavedDocumentId(artifactId);
          setSavedDocumentUrl(saveResponse.artifact?.webUrl || null);
          setFlowState('complete');
          notifyComplete(artifactId, saveResponse.artifact?.webUrl || '');
          return;
        }

        // Task 141 review (F2): an add that queued names is watched, once its save completes, until its worker finished.
        if (activeAddSaveRef.current && saveResponse?.jobId && attachmentsAnswerRef.current.added.length > 0) {
          pendingAddJobRef.current = { jobId: saveResponse.jobId, names: attachmentsAnswerRef.current.added };
        }

        await startJobTracking(saveResponse.jobId, saveResponse.streamUrl);
      } catch (err) {
        if (err instanceof Error && err.name === 'AbortError') {
          return; // Cancelled
        }

        if (isVersionAttempt) {
          // Failed, or its outcome is unknown (no response). Either way a retry is a NEW attempt: at worst it
          // writes one more identical SPE version of the same document — never a second row (task 023).
          versionFailuresRef.current += 1;
        }
        if (activeAddSaveRef.current) addFailuresRef.current += 1; // the same for an add (task 141)

        const errorMsg = createErrorFromException(err, 'Failed to save. Please try again.');
        setError(errorMsg);
        setFlowState('error');
        onError?.(errorMsg);
      }
    },
    [
      apiBaseUrl,
      captureEmailContent,
      cleanup,
      getAccessToken,
      includeBody,
      notifyComplete,
      onDuplicate,
      onError,
      processingOptions,
      selectedAttachmentIds,
      selectedEntity,
      startJobTracking,
    ]
  );

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
      stopAddWatches();
    };
  }, [cleanup, stopAddWatches]);

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
    getAttachmentsAnswer,
  };
}
