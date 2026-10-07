import React, { useCallback, useMemo, useEffect, useState, useRef } from 'react';
import {
  makeStyles,
  tokens,
  Button,
  Card,
  Text,
  Spinner,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  MessageBarActions,
  Badge,
  ProgressBar,
  mergeClasses,
  Textarea,
  Input,
} from '@fluentui/react-components';
import {
  DocumentRegular,
  CheckmarkCircleRegular,
  ErrorCircleRegular,
  OpenRegular,
  CopyRegular,
  EditRegular,
  CheckmarkRegular,
} from '@fluentui/react-icons';
import { RelatedToPicker, type CreateRecordResult } from './RelatedToPicker';
import { RelatedRecordCard } from './RelatedRecordCard';
import { AttachmentSelector } from './AttachmentSelector';
import { DocumentProfileSection } from './DocumentProfileSection';
import { SaveModeSection, resolveSaveMode, type SaveModeChoice } from './SaveModeSection';
import type { EntitySearchResult, EntityType } from '../hooks/useEntitySearch';
import { useRelatedRecord, type RelatedRecordView } from '../hooks/useRelatedRecord';
import {
  useSaveFlow,
  type SaveFlowContext,
  type SaveTarget,
  type StageStatus,
  type UseSaveFlowOptions,
} from '../hooks/useSaveFlow';
import type { DocumentIdentityState, ResolvedRelatedRecord } from '../services/documentIdentityService';
import { fileDocumentToRecord } from '../services/documentFilingService';
import { useAnnounce } from '../hooks/useAnnounce';
import { fetchRelatedCandidates, type RelatedCandidate } from '../services/communicationSuggestionsService';
import {
  clearReferenceListCache,
  warningsIndicateReferenceNotFound,
  type ReferenceListName,
} from '../services/referenceListService';
import { useCreateRecordFormData } from '../hooks/useCreateRecordFormData';
import type { CreateRecordInput } from './CreateRecordForm';
import type { ContactOption } from './views/CreateTodoView';
import {
  openFileUrl,
  openDesktopUrl,
  openRecord,
  buildOpenRecordUrl,
  configuredSpaarkeAppName,
} from '../services/openRecordLauncher';
// Task 099 (ADR-012/ADR-044, amended 2026-10-05): the ONE shared `cleanGuid`, by exact-path alias — not the barrel.
import { cleanGuid } from '@spaarke/ui-components/guid';
import { describeFetchFailure } from '../utils/errorMessages';
import { authenticatedJsonFetch } from '@shared/services/authenticatedJsonFetch';
import type { AttachmentInfo, HostType } from '@shared/adapters/types';
// Task 020 (FR-06) default-name rule; task 089 moved it to utils so the ribbon's Quick Save names files the same way.
import { stripDocumentExtension } from '../utils/documentFileName';

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
/** Task 105: how long a button's confirmation ("Copied" / "Opened") shows before it reverts. */
const BUTTON_FEEDBACK_MS = 2000;

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
  // Task 020 (FR-06): the read-only Document Name row (value + pencil). ADR-021 / fluent-v9-host-visual-fit —
  // tokens only, sized for the narrow pane; the value truncates rather than wrapping/overflowing.
  documentNameDisplay: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalS,
    minHeight: '32px',
  },
  documentNameText: {
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    flexGrow: 1,
  },
  // Task 094: the hint under the LOCKED name box + the "Save as new document" unlock button. Readable
  // in both themes (ADR-021) via the semantic foreground2 token, same convention as SaveModeSection's hint.
  documentNameHint: {
    color: tokens.colorNeutralForeground2,
  },
  // Task 095: "Save as new document" is a quiet secondary link, not an outlined button.
  secondaryLink: {
    alignSelf: 'flex-start',
    minWidth: 'auto',
    paddingLeft: 0,
    paddingRight: 0,
    color: tokens.colorBrandForegroundLink,
    ':hover': { color: tokens.colorBrandForegroundLinkHover },
  },
  documentNameLockedRow: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
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
    flexWrap: 'wrap',
  },
  // Task 088 (UAT-7): the footer's right-hand group — Open Document immediately left of Save / Saved.
  footerEnd: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'flex-end',
    gap: tokens.spacingHorizontalS,
    flexWrap: 'wrap',
    marginLeft: 'auto',
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
  // Task 099: the confirmation takes programmatic focus after a save (tabIndex -1); no focus ring box needed.
  savedBarWrap: { outlineStyle: 'none' },
  // Task 105: stable width while the label swaps to "Copied" / "Opened" (no layout jump).
  savedBarButton: { minWidth: '8.5rem' },
  savedBarDetail: {
    display: 'block',
    marginTop: tokens.spacingVerticalXXS,
  },
  // Task 111: the "File to record" action under the picker, and its error line.
  filingActions: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  filingError: {
    color: tokens.colorStatusDangerForeground1,
  },
  collisionNote: {
    display: 'block',
    marginTop: tokens.spacingVerticalXS,
  },
  collisionError: {
    display: 'block',
    marginTop: tokens.spacingVerticalXS,
    color: tokens.colorStatusDangerForeground1,
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
 * Task 088: what the record a save was filed to looks like to the confirmation bar. A string names it; `null`
 * means the save was filed to NO record (a document-only save); `undefined` means the pane cannot know (a
 * version save of a document whose filing it never resolved) — the bar then says nothing about a record.
 */
type FiledTo = string | null | undefined;

/**
 * Task 111: what the green confirmation box shows. Built from the document this pane just saved (the post-save
 * state) OR from a document that was already in Spaarke when the pane opened (the resolved state) — one box, so the
 * two cannot drift. `filingUnknown` = the pane cannot tell which record the document is filed to (a stamp-only
 * identity): the box then says so instead of "filed" or "not filed".
 */
interface SavedBarModel {
  documentId: string;
  title: string;
  filedTo: FiledTo;
  filingUnknown?: boolean;
  /** The wording for `filedTo === null`. */
  notFiledText: string;
  /** What Copy Link copies; `null` = nothing to copy. */
  copyUrl: string | null;
  /** The resolved variant hides Copy Link when there is nothing to copy; the post-save one shows it disabled. */
  hideCopyWithoutUrl: boolean;
}

/** Task 088: taken when a save is SUBMITTED, committed to {@link SavedDocumentState} when it completes. */
interface PendingSaveSnapshot {
  /** The Document Name field's value at submission — "Saved" again once the field is reverted to it. */
  name: string;
  filedTo: FiledTo;
  mode: SaveTarget['mode'];
}

/** Task 088: the document this pane session last saved — the target of the next version save. */
export interface SavedDocumentState {
  /** Canonical (ADR-044) `sprk_document` id. */
  documentId: string;
  savedName: string;
  filedTo: FiledTo;
  /** Whether the save that produced this state created the document or added a version to it. */
  lastSave: SaveTarget['mode'];
}

/**
 * Task 094: the bundle of "what the Save tab remembers about a save it already made" — lifted so a
 * caller (normally `App.tsx`) can hold it ABOVE the Save tab's own mount lifecycle and hand it back
 * unchanged across a tab switch (owner, 2026-10-04: "yes save should survive tab switch"). `SaveFlow`
 * is uncontrolled when `savedState`/`onSavedStateChange` are omitted — every existing caller that
 * doesn't pass them keeps its own private copy, exactly as before this task.
 */
export interface SavedDocumentPaneState {
  /** `null` = nothing saved in this pane session (or the saved state was left via Cancel / Save as new document). */
  savedDocument: SavedDocumentState | null;
  /** Bumped on every save completion so `DocumentProfileSection` re-reads rather than showing the stale profile. */
  profileRefreshSignal: number;
  /**
   * A Word content-change event has fired since the last save completed (task 094 — "Re-enable on
   * document edits"; the ONLY thing that brings Save back after a save — task 099, owner decision A). Meaningless (never consulted) when the host can't detect changes at all — see
   * `canDetectDocumentChanges` below, which makes the button an enabled "Save" unconditionally then.
   */
  contentChangedSinceSave: boolean;
}

/** The pane's saved state before anything has been saved this session. */
export const DEFAULT_SAVED_DOCUMENT_PANE_STATE: SavedDocumentPaneState = {
  savedDocument: null,
  profileRefreshSignal: 0,
  contentChangedSinceSave: false,
};

/** Task 088: the confirmation bar's name for the record a resolved document is filed to. */
function relatedRecordLabel(view: RelatedRecordView): string {
  return view.displayName || view.number || view.type;
}

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
  /**
   * Document content as a base64 VALUE (Word only) — already in hand. Used only when
   * {@link captureDocumentContent} is absent (tests, or any future non-live caller); production
   * `SaveView` supplies the live capture function instead (task 045).
   */
  documentContentBase64?: string;
  /**
   * Task 045: reads the open document's CURRENT bytes, live, at the moment a save is submitted —
   * threaded from `SaveView` (`hostAdapter.getDocumentContent()`), gated on the adapter's
   * `canGetDocumentContent` capability (NFR-10, never a `hostType` check). Forwarded into every save
   * attempt's context by `buildSaveContext` below, so the first Save, "Keep both", "Save as new
   * version", the hook's `retry()` (which resends the same context/callback), and "Save Another"'s
   * next Save press all re-invoke it and upload the document as it is at THAT moment — never a
   * mount-time snapshot. `undefined` on Outlook (no document bytes) or while the capability is absent.
   */
  captureDocumentContent?: () => Promise<string>;
  /**
   * `sprk_document` id resolved by task 013's FR-01 identity resolution (task 021 / FR-07), threaded
   * down from `App.savedContext` via `SaveView`. `undefined` when unresolved (a new document, or
   * resolution hasn't completed) — the Profile section renders its no-identity state and makes no
   * profile read call.
   */
  resolvedDocumentId?: string;
  /**
   * The open document's identity state (task 024 / FR-11), threaded from `App` via `SaveView`. A resolved
   * identity makes Save DEFAULT to a new version of that record, with an explicit "a new document"
   * override; `undefined` means identity does not apply (Outlook) → a plain create save, as before.
   */
  documentIdentity?: DocumentIdentityState;
  /**
   * Task 111: called after the pane FILED the open (already-in-Spaarke, known-unfiled) document to a record, so the
   * host can put that record into its identity state — the card and the confirmation box then show it without a
   * reload. `documentId` is canonical (ADR-044).
   */
  onDocumentFiled?: (documentId: string, record: ResolvedRelatedRecord) => void;
  /** Re-runs identity resolution ("Check again" / "Try again"). Also the task 027 / FR-10
   * return-path re-read for the related-record card, fired on focus/visibility after the pane
   * regains focus following a record opened via the browser-tab escape hatch. */
  onRetryDocumentIdentity?: () => void;
  /**
   * task 027 / FR-10 (NFR-10): whether this host can open a browser tab
   * (`hostAdapter.getCapabilities().canOpenBrowserWindow`, decided by `SaveView` from the live
   * adapter — never a `hostType` check here). `false`/absent renders the related-record card and
   * the Document-record affordance WITHOUT their open action — the fallback surface, not an error.
   */
  canOpenRecord?: boolean;
  /**
   * task 040 / FR-19 (NFR-10): whether this host can fetch the Association Engine's "Related to"
   * auto-match candidates (`hostAdapter.getCapabilities().canSuggestRelatedRecords`, decided by
   * `SaveView` from the live adapter — never a `hostType` check here). `false`/absent skips the
   * fetch entirely — the "Related to" picker still works via manual search/create, just without
   * pre-ranked suggestion cards.
   */
  canSuggestRelatedRecords?: boolean;
  /**
   * task 020 / FR-06 (NFR-10): whether this host can supply the open item's own name as the
   * Document Name field's default (`hostAdapter.getCapabilities().canProvideDocumentName`, decided
   * by `SaveView` from the live adapter — never a `hostType` check here). `false`/absent leaves the
   * field as a plain, empty Textarea with no default and no pencil affordance — Outlook's existing,
   * pre-task-020 behavior (see the capability's doc comment in `shared/adapters/types.ts` for why
   * Outlook is false despite technically having a subject it could offer).
   */
  canProvideDocumentName?: boolean;
  /**
   * task 094 (NFR-10): whether this host can report document content changes
   * (`hostAdapter.getCapabilities().canDetectDocumentChanges`, decided by `SaveView` from the live
   * adapter — never a `hostType` check here). `false`/absent means the saved-state button is NEVER
   * shown gray: the owner's binding rule is "never block a save" — without detection, it stays an
   * enabled "Save" immediately after saving (see `hasUnsavedChanges` below).
   */
  canDetectDocumentChanges?: boolean;
  /**
   * task 094 (NFR-10): whether this PLATFORM can be offered "Open in Word" on the name-collision
   * prompt (`hostAdapter.getCapabilities().canOpenDesktopWord`, decided by `SaveView` from the live
   * adapter — never a `hostType` check here, and never `Office.context.platform` read directly by
   * this component). `false`/absent renders "Open in browser" only.
   */
  canOpenDesktopWord?: boolean;
  /**
   * task 094: lifts the saved-state bundle to the caller (normally `App.tsx`) so a Save-tab remount
   * (switching to To Do/Find and back) does not lose it. Uncontrolled — `SaveFlow` keeps its own copy
   * via `useState` — when either this or {@link onSavedStateChange} is omitted, matching every
   * existing caller/test unchanged.
   */
  savedState?: SavedDocumentPaneState;
  /**
   * task 094: the setter half of the lifted bundle above — exactly a `useState` setter's shape, so a
   * caller (`App.tsx`) can pass its own `setState` function straight through with no adapter.
   */
  onSavedStateChange?: React.Dispatch<React.SetStateAction<SavedDocumentPaneState>>;
  /** Access token getter */
  getAccessToken: () => Promise<string>;
  /** API base URL */
  apiBaseUrl?: string;
  /** Callback when save is complete */
  onComplete?: (documentId: string, documentUrl: string) => void;
  /**
   * Callback fired once when a save completes successfully, carrying the selected
   * "Related to" record. The host uses it to seed the Create To Do tab's regarding
   * (email-communication-intelligence-r2 §C — production save-context wiring).
   */
  onSaved?: (entity: EntitySearchResult) => void;
  /** Callback when Quick Create is triggered */
  onQuickCreate?: (entityType: EntityType, searchQuery: string) => void;
  /**
   * Task 100: contact search for the "+ New" form's Assigned To field — the pane's ONE contact search
   * (`App.handleSearchContacts`, shared with the To Do and Email tabs). Absent → the field finds no one (the prefill
   * still shows).
   */
  onSearchContacts?: (query: string) => Promise<ContactOption[]>;
  // Task 088 (UAT-1): `onViewDocument` (a callback taking the stored file's Graph webUrl) is REMOVED. View
  // Document now opens the Spaarke document RECORD through `openRecordLauncher` — no path in this pane
  // opens the Word-for-the-web file URL any more.
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
    captureDocumentContent,
    resolvedDocumentId,
    documentIdentity,
    onRetryDocumentIdentity,
    onDocumentFiled,
    canOpenRecord = false,
    canSuggestRelatedRecords = false,
    canProvideDocumentName = false,
    canDetectDocumentChanges = false,
    canOpenDesktopWord = false,
    savedState: controlledSavedState,
    onSavedStateChange,
    getAccessToken,
    apiBaseUrl = '',
    onComplete,
    onSaved,
    onSearchContacts,
    showDocumentInfo = true,
    className,
  } = props;

  const styles = useStyles();
  const { announce, liveRegion } = useAnnounce();

  // ── Task 088 (UAT-6/7, owner 2026-10-03 "Keep the form"); lifted task 094 (owner 2026-10-04) ───────
  // After a successful save the pane stays on the form: a confirmation bar, the document's name and profile,
  // and a footer whose primary button reads a gray, disabled "Saved" until a content-change event fires (or,
  // task 094, an accepted Generate Profile) — then an enabled "Save", a version save of THIS document (FR-11's
  // existing path, never a second mode). `savedState.savedDocument === null` = not saved in this pane session
  // (or the state was left via Cancel / "Save as new document"). It is committed in the hook's onComplete, in
  // the same render as flowState 'complete' (React batches both), from the snapshot `pendingSaveRef` took when
  // the save was submitted.
  //
  // Uncontrolled/controlled (task 094): `savedState`/`onSavedStateChange` are optional. Every existing caller
  // (and every test in this package) that doesn't pass them gets SaveFlow's OWN private copy, unchanged from
  // before this task. `App.tsx` passes both, so the bundle survives a Save-tab remount (switching tabs).
  const [internalSavedState, setInternalSavedState] = useState<SavedDocumentPaneState>(
    DEFAULT_SAVED_DOCUMENT_PANE_STATE
  );
  const savedStateBundle = controlledSavedState ?? internalSavedState;
  const updateSavedState = onSavedStateChange ?? setInternalSavedState;
  const { savedDocument, profileRefreshSignal, contentChangedSinceSave } = savedStateBundle;
  const pendingSaveRef = useRef<PendingSaveSnapshot | null>(null);

  // Initialize save flow hook
  const saveFlowOptions: UseSaveFlowOptions = useMemo(
    () => ({
      apiBaseUrl,
      getAccessToken,
      onComplete: (docId, docUrl) => {
        const pending = pendingSaveRef.current;
        updateSavedState(prev => ({
          savedDocument: {
            documentId: cleanGuid(docId) || docId,
            savedName: pending?.name ?? '',
            filedTo: pending?.filedTo,
            lastSave: pending?.mode ?? 'create',
          },
          profileRefreshSignal: prev.profileRefreshSignal + 1,
          // task 094: a fresh save always clears the re-enable trigger — this save IS the content as
          // of right now.
          contentChangedSinceSave: false,
        }));
        announce('Document saved successfully', 'polite');
        onComplete?.(docId, docUrl);
      },
      onError: error => {
        announce(`Error: ${error.message}`, 'assertive');
      },
      onDuplicate: (_docId, message) => {
        announce('Duplicate detected: ' + message, 'polite');
      },
    }),
    [apiBaseUrl, getAccessToken, onComplete, announce, updateSavedState]
  );

  const {
    flowState,
    selectedEntity,
    setSelectedEntity,
    selectedAttachmentIds,
    setSelectedAttachmentIds,
    jobStatus,
    error,
    clearError,
    duplicateInfo,
    isSaving,
    isValid,
    startSave,
    reset,
    retry,
    savedDocumentUrl,
  } = useSaveFlow(saveFlowOptions);

  // Task 088: the open document's own filing, from the SAME resolved identity the related-record card reads
  // (no second call) — used to name the record in the confirmation bar after a version save.
  const resolvedRelatedRecord = useRelatedRecord(documentIdentity);

  // Task 088 (NFR-10): a "Save version" needs document BYTES to send. Whether this pane has them is decided by
  // the host's capability upstream (SaveView passes `captureDocumentContent` only when the adapter reports
  // `canGetDocumentContent`), never by `hostType`. Without bytes — an Outlook email, which is immutable and
  // has no version path — the saved state stays "Saved" and its name is shown read-only.
  const canSaveNewVersion = Boolean(captureDocumentContent) || Boolean(documentContentBase64);

  // Local state for document metadata fields.
  //
  // Task 020 (FR-06): gated on `canProvideDocumentName` (NFR-10 — converted from an initial
  // `hostType === 'word'` gate per task 040's precedent; see that capability's doc comment in
  // `shared/adapters/types.ts`). When true, the field defaults to the open item's own name minus its
  // extension (derived from `itemName` — see `defaultDocumentName` below) and is editable in-pane
  // behind a pencil affordance. When false (Outlook, always, today), `documentName` still starts
  // empty with no default and no pencil, exactly as before task 020, so `effectiveDocumentName` /
  // `email.isNameSystemDerived` (useSaveFlow.ts, task 046 (b)) keep their existing meaning.
  // Lazy-initialized from the default so a capable pane's very first render already shows it
  // (SaveView only mounts SaveFlow once its own itemName load completes) rather than flashing
  // empty-then-populated.
  const [documentName, setDocumentName] = useState<string>(() =>
    canProvideDocumentName && itemName ? stripDocumentExtension(itemName) : ''
  );
  // Whether the user has ever edited the value (typed a character while the pencil was open). Once true, the
  // default-sync effect below never overwrites it again — an identity/itemName refresh must not clobber an
  // edit. Reset by Cancel (the wizard "Cancel" pattern) and by a fully-emptied commit (§ closeDocumentNameEditing).
  const [documentNameTouched, setDocumentNameTouched] = useState(false);
  // Read-only display (with a pencil affordance) vs. an editable Input. Word only — see above.
  const [isEditingDocumentName, setIsEditingDocumentName] = useState(false);
  // The value at the moment editing opened, so Escape can revert to it without depending on effect timing.
  const documentNameBeforeEditRef = useRef('');
  // Suppresses the onBlur "commit" handler for a blur the component itself triggers (Enter-commit or
  // Escape-cancel unmount the <Input>, which can also fire a native blur) — see closeDocumentNameEditing.
  const suppressNextDocumentNameBlurRef = useRef(false);

  // Task 020 (FR-06): the FR-06 default — the open item's own name, minus its extension. `undefined`
  // when the host has no `canProvideDocumentName` capability (Outlook, today) and while `itemName`
  // hasn't loaded yet.
  const defaultDocumentName = useMemo(
    () => (canProvideDocumentName && itemName ? stripDocumentExtension(itemName) : undefined),
    [canProvideDocumentName, itemName]
  );

  // Keeps the field in sync with the default until the user edits it or opens the editor — covers the
  // ordinary case where SaveView mounts SaveFlow before its own `itemName` load resolves, and the
  // identity-retry re-read (task 027 / FR-10) which can re-fire `itemName`/the capability.
  useEffect(() => {
    if (!canProvideDocumentName) return;
    if (documentNameTouched || isEditingDocumentName) return;
    if (defaultDocumentName !== undefined) setDocumentName(defaultDocumentName);
  }, [defaultDocumentName, canProvideDocumentName, documentNameTouched, isEditingDocumentName]);

  // Task 099 focus management: the control focus lands on once the name becomes editable again
  // ("Save as new document" removed the locked name). `nameFocusTick` is bumped by that action.
  const nameFocusRef = useRef<HTMLElement | null>(null);
  const [nameFocusTick, setNameFocusTick] = useState(0);
  useEffect(() => {
    if (nameFocusTick > 0) nameFocusRef.current?.focus();
  }, [nameFocusTick]);

  // Task 099 (owner item 2): while the picker's "+ New" create form is open, only the pills + the form show.
  const [relatedCreating, setRelatedCreating] = useState(false);

  // Task 099 focus management: when a save completes the Save button disappears — land on the confirmation.
  const savedBarRef = useRef<HTMLDivElement | null>(null);

  const handleStartEditDocumentName = useCallback(() => {
    documentNameBeforeEditRef.current = documentName;
    setIsEditingDocumentName(true);
    announce('Editing document name', 'polite');
  }, [documentName, announce]);

  const handleDocumentNameChange = useCallback((_e: unknown, data: { value: string }) => {
    setDocumentName(data.value);
    setDocumentNameTouched(true);
  }, []);

  // commit=true (Enter, or blur-to-commit — the standard inline-edit convention): an emptied value falls
  // back to the FR-06 default rather than left showing a blank name (the "never empty" acceptance criterion
  // holds either way, via buildSaveContext's truthy check below, but this avoids the confusing blank label).
  // commit=false (Escape): reverts to the pre-edit value.
  const closeDocumentNameEditing = useCallback(
    (commit: boolean) => {
      suppressNextDocumentNameBlurRef.current = true;
      if (commit) {
        const trimmed = documentName.trim();
        if (trimmed) {
          setDocumentNameTouched(true);
        } else {
          setDocumentName(defaultDocumentName ?? '');
          setDocumentNameTouched(false);
        }
      } else {
        setDocumentName(documentNameBeforeEditRef.current);
      }
      setIsEditingDocumentName(false);
      announce(commit ? 'Document name updated' : 'Document name edit canceled', 'polite');
    },
    [documentName, defaultDocumentName, announce]
  );

  const handleDocumentNameKeyDown = useCallback(
    (e: React.KeyboardEvent<HTMLInputElement>) => {
      if (e.key === 'Enter') {
        e.preventDefault();
        closeDocumentNameEditing(true);
      } else if (e.key === 'Escape') {
        e.preventDefault();
        closeDocumentNameEditing(false);
      }
    },
    [closeDocumentNameEditing]
  );

  const handleDocumentNameBlur = useCallback(() => {
    if (suppressNextDocumentNameBlurRef.current) {
      suppressNextDocumentNameBlurRef.current = false;
      return;
    }
    closeDocumentNameEditing(true);
  }, [closeDocumentNameEditing]);

  // Auto-match candidates for the "Related to" cards (engine suggestions, ranked
  // highest-first). Replaces the old single pre-selection with the reconciliation-
  // style card list (UI feedback 2026-09-02).
  const [relatedCandidates, setRelatedCandidates] = useState<RelatedCandidate[]>([]);
  const [candidatesLoading, setCandidatesLoading] = useState(false);

  // The "+ New" form's data (task 100; matter types since task 038): the matter-type, practice-area and project-type
  // reference lists and the Assigned To prefill. Loaded LAZILY, the first time the form opens (`relatedCreating`),
  // then kept — host-neutral (NFR-10: no hostType check). A failed list load is a readable, announced (NFR-11),
  // non-blocking message with Retry, never a required field that blocks silently (task 038's coordinator fix).
  const createFormData = useCreateRecordFormData({
    ...(apiBaseUrl ? { apiBaseUrl } : {}),
    getAccessToken,
    enabled: relatedCreating,
    demo: isBrowserTestMode(),
    announce,
  });

  // ── FR-11 save mode (task 024) ──────────────────────────────────────────────────────────────────
  // The user's EXPLICIT choice; null = the identity's default (a new version when resolved). A new identity
  // (first resolution, or a retry) starts again from its own default — never from a stale override.
  const [saveModeChoice, setSaveModeChoice] = useState<SaveModeChoice | null>(null);
  useEffect(() => {
    setSaveModeChoice(null);
  }, [documentIdentity]);
  const saveMode = useMemo(() => resolveSaveMode(documentIdentity, saveModeChoice), [documentIdentity, saveModeChoice]);
  const isVersionMode = saveMode.target?.mode === 'version';
  // What the LAST submitted save was — drives the success copy and whether "Related to" is handed on.
  const [submittedTarget, setSubmittedTarget] = useState<SaveTarget | null>(null);

  const handleSaveModeChange = useCallback(
    (choice: SaveModeChoice | null) => {
      setSaveModeChoice(choice);
      // NFR-11: every mode change is announced.
      if (choice === 'new') {
        announce('Save mode: a new document. The existing document will not be changed.', 'polite');
      } else if (choice === 'version') {
        announce(`Save mode: a new version of ${saveMode.documentLabel ?? 'the existing document'}.`, 'polite');
      } else {
        announce('Save as a new document is no longer selected. Save is unavailable.', 'polite');
      }
    },
    [announce, saveMode.documentLabel]
  );

  // NFR-11: announce what identity resolution decided for Save.
  useEffect(() => {
    switch (saveMode.view) {
      case 'version':
        announce(
          `This document is already in Spaarke. Save will add a new version of ${saveMode.documentLabel ?? 'it'}.`,
          'polite'
        );
        break;
      case 'conflict':
        announce(
          'This document can’t be saved from here: Spaarke has a conflicting record for this file.',
          'assertive'
        );
        break;
      case 'undetermined':
        announce(
          'Couldn’t check whether this document is already in Spaarke. Try again, or choose to save it as a new document.',
          'polite'
        );
        break;
      case 'denied':
        announce('You can’t add a version to this document. You can save your copy as a new document.', 'polite');
        break;
      default:
        break;
    }
  }, [saveMode.view, saveMode.documentLabel, announce]);

  // ── task 027 / FR-10: open the related record / Document record from the pane ──────────────────
  // Spike-2 (notes/spikes/spike-2-dialog-api.md) selected Option 3 — a browser-tab escape hatch via
  // Office.context.ui.openBrowserWindow, never the Office Dialog API. `canOpenRecord` (NFR-10) is a
  // capability flag threaded from SaveView's hostAdapter.getCapabilities().canOpenBrowserWindow —
  // gating happens here and in the JSX below, never on `hostType`.
  // The Open buttons also need ORG_URL. Unset, `openRecord` can only no-op, so a visible button would
  // do nothing when clicked; hide it instead. The deploy workflow sets ORG_URL.
  const openRecordAvailable = canOpenRecord && Boolean(process.env.ORG_URL);
  const hasOpenedExternalRecordRef = useRef(false);

  const handleOpenRelatedRecord = useCallback((record: RelatedRecordView) => {
    const result = openRecord({
      orgUrl: process.env.ORG_URL,
      entityType: record.entityType,
      recordId: record.id,
    });
    if (result.opened) {
      hasOpenedExternalRecordRef.current = true;
    }
  }, []);

  // Task 105: transient per-button feedback state ('done' = check + "Copied"/"Opened", 'failed' = error label).
  const [buttonFeedback, setButtonFeedback] = useState<{
    copy?: 'done' | 'failed';
    view?: 'done' | 'failed';
  }>({});
  const feedbackTimers = useRef<Partial<Record<'copy' | 'view', ReturnType<typeof setTimeout> | undefined>>>({});
  const flashButton = useCallback((which: 'copy' | 'view', outcome: 'done' | 'failed') => {
    const existing = feedbackTimers.current[which];
    if (existing) clearTimeout(existing);
    setButtonFeedback(prev => ({ ...prev, [which]: outcome }));
    feedbackTimers.current[which] = setTimeout(() => {
      setButtonFeedback(prev => ({ ...prev, [which]: undefined }));
      feedbackTimers.current[which] = undefined;
    }, BUTTON_FEEDBACK_MS);
  }, []);
  useEffect(() => {
    const timers = feedbackTimers.current;
    return () => {
      Object.values(timers).forEach(t => t && clearTimeout(t));
    };
  }, []);

  // Task 088 (UAT-1/7): View Document, Open Document and the duplicate card's View Existing Document all open
  // the Spaarke `sprk_document` RECORD — in the Spaarke app, by the launcher's `appname=` (SPAARKE_APP_NAME) —
  // never the stored file's Graph webUrl. One handler, so the three cannot drift apart.
  const openDocumentRecord = useCallback((documentId: string) => {
    const result = openRecord({
      orgUrl: process.env.ORG_URL,
      entityType: 'sprk_document',
      recordId: documentId,
    });
    if (result.opened) {
      hasOpenedExternalRecordRef.current = true;
    }
    return result.opened;
  }, []);

  // The document "Open Document" opens: the one this pane just saved, else the one identity resolution found.
  const openableDocumentId = savedDocument?.documentId ?? resolvedDocumentId;

  // ── Task 111 (owner UAT round 11 items 3-6): a document ALREADY in Spaarke ────────────────────────
  // A document that opened as a resolved identity (no save in this pane session) shows the SAME green box as after
  // a save — in the default version mode only: once the user picks "Save as new document" the pane is composing a
  // different document and the box would describe the wrong one. The record it names comes from the identity.
  //
  // `resolvedRelatedRecord.kind` decides everything about filing, and only a KNOWN-unfiled document is offered the
  // picker + "File to record": `'unassociated'` is a URL-resolved identity whose record slots the server read and
  // found empty. `'unknown'` is a stamp-only identity — nothing was read, so it must never be filed (the route sets a
  // lookup and never clears another slot; a document filed to a slot the pane cannot see would get a second parent).
  const resolvedIdentity =
    documentIdentity && typeof documentIdentity === 'object' && documentIdentity.kind === 'resolved'
      ? documentIdentity
      : null;
  const showResolvedBox = savedDocument === null && isVersionMode && resolvedIdentity !== null;
  const showFilingPicker = showResolvedBox && resolvedRelatedRecord.kind === 'unassociated';
  const [fileTarget, setFileTarget] = useState<EntitySearchResult | null>(null);
  const [filing, setFiling] = useState(false);
  const [filingError, setFilingError] = useState<string | null>(null);
  // A new identity (retry, or the filing itself landing) starts the picker afresh.
  useEffect(() => {
    setFileTarget(null);
    setFilingError(null);
  }, [documentIdentity]);

  const handleFileTargetSelect = useCallback(
    (entity: EntitySearchResult | null) => {
      if (entity?.canFile === false) return; // same backstop as handleEntitySelect (task 084)
      setFileTarget(entity);
      setFilingError(null);
      if (entity) {
        announce(`Selected ${entity.entityType}: ${entity.name}`, 'polite');
      }
    },
    [announce]
  );

  const handleFileToRecord = useCallback(async () => {
    if (!resolvedIdentity || !fileTarget || filing) return;
    setFiling(true);
    setFilingError(null);
    const outcome = await fileDocumentToRecord({
      apiBaseUrl,
      getAccessToken,
      documentId: resolvedIdentity.documentId,
      entityType: fileTarget.entityType,
      recordId: fileTarget.id,
      recordName: fileTarget.name,
      // `displayInfo` is the record's number for a Matter/Project; an Invoice's is not a number.
      recordNumber: fileTarget.entityType === 'Invoice' ? null : (fileTarget.displayInfo ?? null),
    });
    setFiling(false);
    if (outcome.ok) {
      announce(`Filed to ${fileTarget.name}.`, 'polite');
      onDocumentFiled?.(resolvedIdentity.documentId, outcome.record);
    } else {
      setFilingError(outcome.message);
      announce(outcome.message, 'assertive');
    }
  }, [resolvedIdentity, fileTarget, filing, apiBaseUrl, getAccessToken, announce, onDocumentFiled]);

  // Return path (Spike-2 §d): an unmodified Dataverse form never calls `messageParent`, so every
  // option Spike-2 compared — including this one — falls back to a focus/visibility-triggered
  // re-read rather than a push notification. Weaker ("the user came back", not "the user saved a
  // change") but explicit and the SAME limitation the spike found for every mechanism, not a
  // shortcut taken here. Scoped to fire only once the user has actually opened a record via this
  // pane (hasOpenedExternalRecordRef), so ordinary Word/pane focus churn never triggers a re-read.
  useEffect(() => {
    function handlePaneReturn(): void {
      if (!hasOpenedExternalRecordRef.current) return;
      if (typeof document.visibilityState === 'string' && document.visibilityState !== 'visible') return;
      onRetryDocumentIdentity?.();
      updateSavedState(prev => ({ ...prev, profileRefreshSignal: prev.profileRefreshSignal + 1 }));
      announce('Refreshed with the latest changes from the record.', 'polite');
    }
    document.addEventListener('visibilitychange', handlePaneReturn);
    window.addEventListener('focus', handlePaneReturn);
    return () => {
      document.removeEventListener('visibilitychange', handlePaneReturn);
      window.removeEventListener('focus', handlePaneReturn);
    };
  }, [onRetryDocumentIdentity, announce, updateSavedState]);

  // Task 088: what the next Save sends. In the SAVED state it is always a new version of the document this pane
  // just saved — the FR-11 version path (existingDocumentId + isNewVersion), reused, not a second mode — or
  // nothing at all when this pane has no document bytes to version (see canSaveNewVersion). Before any save,
  // it is task 024's identity-derived mode, unchanged.
  const activeTarget = useMemo<SaveTarget | null>(() => {
    if (savedDocument) {
      return canSaveNewVersion ? { mode: 'version', existingDocumentId: savedDocument.documentId } : null;
    }
    return saveMode.target;
  }, [savedDocument, canSaveNewVersion, saveMode.target]);

  // Task 094 (owner, 2026-10-04 — "Re-enable on document edits"), tightened by task 099 (owner decision A,
  // 2026-10-05): after a save there is NO Save button until the document is edited. Save (= a new version of
  // this document) comes back when
  // - a Word content-change event fired since the save (`contentChangedSinceSave`), or
  // - this host CANNOT detect content changes at all (`!canDetectDocumentChanges`) — the owner's binding
  //   "never block a save" rule outranks "hide Save": with no way to know the document changed, hiding Save
  //   would make a second save impossible.
  // The 088 "Generate Profile re-enables Save" trigger is gone: the post-save Profile has Refresh, not Generate.
  // Never true on a pane without document bytes (canSaveNewVersion false — Outlook): nothing to version.
  const hasUnsavedChanges =
    savedDocument !== null && canSaveNewVersion && (!canDetectDocumentChanges || contentChangedSinceSave);

  // Task 088: the record a save to `target` is filed to, as the confirmation bar will name it.
  const filingOf = useCallback(
    (target: SaveTarget): FiledTo => {
      if (target.mode === 'create') {
        return selectedEntity?.name ?? null;
      }
      if (savedDocument && cleanGuid(target.existingDocumentId) === savedDocument.documentId) {
        return savedDocument.filedTo; // a version never re-files the document (task 023 D-4/D-5)
      }
      if (resolvedRelatedRecord.kind === 'associated') return relatedRecordLabel(resolvedRelatedRecord);
      if (resolvedRelatedRecord.kind === 'unassociated') return null;
      return undefined;
    },
    [selectedEntity, savedDocument, resolvedRelatedRecord]
  );

  // Task 088: every submission records what the confirmation bar and the "Saved" comparison will need once it
  // completes. `retry()` resends the last context, so the snapshot taken for that context still applies.
  const submitSave = useCallback(
    (context: SaveFlowContext, filedTo: FiledTo) => {
      pendingSaveRef.current = { name: documentName, filedTo, mode: context.saveTarget?.mode ?? 'create' };
      void startSave(context);
    },
    [documentName, startSave]
  );

  // Build save context
  const buildSaveContext = useCallback(
    (): SaveFlowContext => ({
      hostType,
      attachments,
      ...(activeTarget ? { saveTarget: activeTarget } : {}),
      ...(itemId !== undefined ? { itemId } : {}),
      ...(itemName !== undefined ? { itemName } : {}),
      ...(documentName ? { documentName } : {}),
      ...(senderEmail !== undefined ? { senderEmail } : {}),
      ...(senderDisplayName !== undefined ? { senderDisplayName } : {}),
      ...(recipients !== undefined ? { recipients } : {}),
      ...(sentDate !== undefined ? { sentDate } : {}),
      ...(emailBody !== undefined ? { emailBody } : {}),
      ...(documentUrl !== undefined ? { documentUrl } : {}),
      ...(documentContentBase64 !== undefined ? { documentContentBase64 } : {}),
      // Task 045: the LIVE capture function, forwarded as-is (never invoked here) — `startSave` calls
      // it at submission time, fresh, for every attempt that reaches it (first Save, "Keep both",
      // "Save as new version", a hook `retry()` that resends this same context, and the next Save
      // press after "Save Another").
      ...(captureDocumentContent ? { captureDocumentContent } : {}),
    }),
    [
      hostType,
      itemId,
      itemName,
      documentName,
      attachments,
      senderEmail,
      senderDisplayName,
      recipients,
      sentDate,
      emailBody,
      documentUrl,
      documentContentBase64,
      captureDocumentContent,
      activeTarget,
    ]
  );

  // Handle save button click. A null target means the save mode is not settled (identity still checking,
  // a conflict, or an undetermined identity with no explicit choice; or, task 088, a saved document this pane
  // cannot version) — the button is disabled then, and this guard makes sure nothing is sent even if it were not.
  const handleSave = useCallback(() => {
    if (!activeTarget) return;
    setSubmittedTarget(activeTarget);
    submitSave(buildSaveContext(), filingOf(activeTarget));
  }, [activeTarget, buildSaveContext, filingOf, submitSave]);

  // Cancel = clear the current selection + fields (wizard "Cancel" pattern), and return the save mode to
  // the identity's default. Task 088: in the saved state it also leaves that state — the pane returns to an
  // empty form (what "Save Another" did before the success card was replaced), so Outlook can still save the
  // same email again, e.g. with other attachments.
  const handleCancel = useCallback(() => {
    setSelectedEntity(null);
    setDocumentName('');
    // Task 020: un-touch so the sync effect re-applies the Word FR-06 default (Outlook has none, so this
    // is a no-op there — the field simply stays empty, exactly as before task 020).
    setDocumentNameTouched(false);
    setIsEditingDocumentName(false);
    setSaveModeChoice(null);
    updateSavedState(() => DEFAULT_SAVED_DOCUMENT_PANE_STATE);
    pendingSaveRef.current = null;
    reset();
  }, [setSelectedEntity, reset, updateSavedState]);

  // Task 099 (owner item 6): the post-save Profile's Refresh. The profile section re-reads the profile itself;
  // this re-reads the document's identity (name + the record it is filed to) and, when it settles on THIS
  // document, updates what the header and the confirmation bar show. A refresh that finds nothing new (or
  // fails) changes nothing — the saved bar keeps what the save itself reported.
  const refreshPendingRef = useRef(false);
  const handleRefreshSaved = useCallback(() => {
    refreshPendingRef.current = Boolean(onRetryDocumentIdentity);
    onRetryDocumentIdentity?.();
    announce('Refreshing the saved document.', 'polite');
  }, [onRetryDocumentIdentity, announce]);
  useEffect(() => {
    if (!refreshPendingRef.current || documentIdentity === 'checking') return;
    refreshPendingRef.current = false;
    if (!savedDocument || documentIdentity === undefined) return;
    if (documentIdentity.kind !== 'resolved' || cleanGuid(documentIdentity.documentId) !== savedDocument.documentId) {
      return;
    }
    const refreshedName = documentIdentity.documentName || documentIdentity.fileName || null;
    const refreshedFiledTo: FiledTo =
      resolvedRelatedRecord.kind === 'associated'
        ? relatedRecordLabel(resolvedRelatedRecord)
        : resolvedRelatedRecord.kind === 'unassociated'
          ? null
          : undefined;
    updateSavedState(prev =>
      prev.savedDocument
        ? {
            ...prev,
            savedDocument: {
              ...prev.savedDocument,
              ...(refreshedName ? { savedName: refreshedName } : {}),
              ...(refreshedFiledTo !== undefined ? { filedTo: refreshedFiledTo } : {}),
            },
          }
        : prev
    );
    announce('Saved document refreshed.', 'polite');
    // eslint-disable-next-line react-hooks/exhaustive-deps -- runs when identity settles after a Refresh; savedDocument/resolvedRelatedRecord are read at that moment
  }, [documentIdentity]);

  // A refused VERSION save whose cause is the existing document (task 024): switch to "a new document" so
  // the user can choose where to file it and save. Deliberately does not save on its own.
  // Task 088: also reachable from the SAVED state — a version save of the document this pane just created can
  // be refused (e.g. OFFICE_009 if the caller may not write that file; notes/025 Q6). Leaving the saved state is
  // what makes the switch take effect there, since the saved state always targets a version.
  // Task 094: ALSO the "Save as new document" affordance beside the LOCKED name box — before any save
  // (pre-save version mode) it clears nothing (there is no saved state yet) but still flips the save
  // mode choice, unlocking the name box into the plain create form; after a save it leaves the saved
  // state the same way 088 did.
  const handleSaveAsNewInstead = useCallback(() => {
    updateSavedState(() => DEFAULT_SAVED_DOCUMENT_PANE_STATE);
    setSaveModeChoice('new');
    clearError();
    // Task 099: the locked name (and its link) disappear — put focus on the now-editable name control.
    setNameFocusTick(t => t + 1);
    announce('Save mode: a new document. Choose where to file it, then select Save.', 'polite');
  }, [clearError, announce, updateSavedState]);

  // Task 025: the pane's "Keep both" choice after a refused CREATE collision (OFFICE_020) — an immediate
  // retry (unlike handleSaveAsNewInstead, no new required input is unlocked: the entity, content and name
  // are already fully specified) asking the server to upload under a Graph-chosen non-colliding name
  // instead of refusing again.
  const handleKeepBoth = useCallback(() => {
    clearError();
    submitSave({ ...buildSaveContext(), allowRename: true }, selectedEntity?.name ?? null);
    announce('Saving under a new name.', 'polite');
  }, [buildSaveContext, clearError, submitSave, selectedEntity, announce]);

  // Task 025: the pane's "Save as new version" choice after a refused CREATE collision — resubmits
  // through the ALREADY-SHIPPED FR-11 version-save path, targeting the document the server's refusal
  // resolved. Task 088: offered ONLY on the server's `canSaveAsVersion` flag (that document is filed to the
  // record this save targets — #1005), never on the id's presence; the guard repeats the button's rule.
  const handleSaveAsVersionInstead = useCallback(() => {
    const existingDocumentId = error?.collisionExistingDocumentId;
    if (!existingDocumentId || error?.collisionCanSaveAsVersion !== true) return;
    clearError();
    // canSaveAsVersion means that document is filed to the selected record, so the bar can name it.
    submitSave(
      { ...buildSaveContext(), saveTarget: { mode: 'version', existingDocumentId } },
      selectedEntity?.name ?? null
    );
    announce('Saving as a new version of the existing document.', 'polite');
  }, [buildSaveContext, clearError, submitSave, selectedEntity, announce, error]);

  // ── Task 088 (UAT-5), two buttons by platform added task 094: "Open" on the name-collision prompt ──
  // Opens the file that already holds the name, through the EXISTING `GET /api/documents/{id}/open-links`
  // (no new route; its endpoint filter re-checks Read, so this can never open more than the server's own
  // redaction already let the pane name). "Open in browser" launches the response's https `webUrl` — WHICH
  // opener runs is decided by capability (`canOpenRecord` = canOpenBrowserWindow), never by hostType — on
  // every platform. "Open in Word" (PC/Mac only, `canOpenDesktopWord`) additionally anchor-clicks the
  // response's `desktopUrl` — an UNSUPPORTED mechanism trialled per the owner (`openDesktopUrl`'s doc
  // comment; notes/088 §3 explains why `openBrowserWindow` cannot do this). A failed call shows its reason
  // in the prompt; the pane never builds a file URL itself.
  const [openFileBusy, setOpenFileBusy] = useState(false);
  const [openFileError, setOpenFileError] = useState<string | null>(null);
  useEffect(() => {
    setOpenFileError(null);
    setOpenFileBusy(false);
  }, [error]);

  const handleOpenCollisionFile = useCallback(
    async (mode: 'desktop' | 'browser') => {
      const documentId = error?.collisionExistingDocumentId;
      if (!documentId) return;
      setOpenFileBusy(true);
      setOpenFileError(null);
      try {
        if (!apiBaseUrl) {
          throw new Error("Couldn't open the file: the pane isn't fully configured. Reload and try again.");
        }
        let res: Response;
        try {
          const token = await getAccessToken();
          res = await authenticatedJsonFetch(
            `${apiBaseUrl}/api/documents/${encodeURIComponent(cleanGuid(documentId))}/open-links`,
            { headers: { 'Content-Type': 'application/json' } },
            token,
            { getRetryToken: getAccessToken }
          );
        } catch {
          throw new Error("Couldn't reach Spaarke to open the file. Check your connection and try again.");
        }
        if (!res.ok) {
          throw new Error((await describeFetchFailure(res)).message);
        }
        const links = (await res.json()) as { webUrl?: string | null; desktopUrl?: string | null };
        if (mode === 'desktop') {
          if (!links.desktopUrl) {
            throw new Error('Spaarke did not return a desktop link for this file.');
          }
          const result = openDesktopUrl(links.desktopUrl);
          if (!result.opened) {
            throw new Error(result.reason ?? "Couldn't open the file in Word.");
          }
          announce(`Opening ${error?.collisionExistingDocumentName ?? 'the existing file'} in Word.`, 'polite');
          return;
        }
        if (!links.webUrl) {
          throw new Error('Spaarke did not return a link for this file.');
        }
        const result = openFileUrl(links.webUrl, canOpenRecord);
        if (!result.opened) {
          throw new Error(result.reason ?? "Couldn't open the file.");
        }
        announce(`Opened ${error?.collisionExistingDocumentName ?? 'the existing file'}.`, 'polite');
      } catch (err) {
        const message = err instanceof Error && err.message ? err.message : "Couldn't open the file.";
        setOpenFileError(message);
        announce(message, 'assertive');
      } finally {
        setOpenFileBusy(false);
      }
    },
    [error, apiBaseUrl, getAccessToken, canOpenRecord, announce]
  );

  // Handle entity selection (Confirm a card / select a search result / Change).
  const handleEntitySelect = useCallback(
    (entity: EntitySearchResult | null) => {
      // Task 084: a record the caller cannot file to is never selected, whatever path asked for it. The
      // picker already offers no way to select one; this is the pane-level backstop.
      if (entity?.canFile === false) return;
      setSelectedEntity(entity);
      if (entity) {
        announce(`Selected ${entity.entityType}: ${entity.name}`, 'polite');
      }
    },
    [setSelectedEntity, announce]
  );

  // Fetch the engine's ranked "Related to" candidates for the auto-match cards.
  // Reuses the SHARED derivePrimaryReview model (no fork; ADR-045). Capability-gated (task 040 /
  // FR-19, NFR-10) — never a `hostType` check: `canSuggestRelatedRecords` is always false on Word
  // (no captured-communication record for the engine to key off), so this is a no-op there without
  // needing to know which host is running. Best-effort: a 404 (email not captured) / failure → no
  // cards (the user searches instead — never an auto-filed guess). The browser test harness seeds
  // demo candidates so the card UX is iterable.
  useEffect(() => {
    if (!canSuggestRelatedRecords || !itemId) return;
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
  }, [canSuggestRelatedRecords, itemId]);

  // §C — when a save completes, hand the selected "Related to" record to the host once so it can
  // seed the Create To Do tab's regarding. Reset on a fresh save (idle/selecting) so a second save
  // re-notifies. Fires on 'complete' and 'duplicate' (both mean "this email is now filed to X").
  // A VERSION save (task 024) files nothing: it never re-associates the existing record, and it does not send
  // the picker's selection, so there is no "Related to" to hand on.
  const savedNotifiedRef = useRef(false);
  useEffect(() => {
    if (
      (flowState === 'complete' || flowState === 'duplicate') &&
      selectedEntity &&
      submittedTarget?.mode !== 'version' &&
      !savedNotifiedRef.current
    ) {
      savedNotifiedRef.current = true;
      onSaved?.(selectedEntity);
    } else if (flowState === 'idle' || flowState === 'selecting') {
      savedNotifiedRef.current = false;
    }
  }, [flowState, selectedEntity, submittedTarget, onSaved]);

  // "Look up another record" search — scoped to the selected chip type.
  //
  // Task 053: a failed search used to render identically to "nothing matched" (`return []` on both a
  // non-OK response and a thrown exception) — the user could never tell the two apart. Now a failure
  // THROWS a descriptive Error (server `detail` when the response is ProblemDetails-shaped, a sensible
  // status-aware fallback otherwise, via `describeFetchFailure`); `RelatedToPicker`'s `runSearch` catches
  // it and renders it distinctly from an empty result set, with a Retry. A genuinely empty search still
  // resolves to `[]`, unchanged.
  //
  // Task 084 (#1037, "pickable equals savable"): this search lists records the caller can READ, but the
  // save demands AppendTo on the chosen record. So it asks the server for filing access (`access=file`,
  // opt-in — the To Do Contact search in App.tsx deliberately does NOT send it) and carries each row's
  // `canFile` into the picker, which shows a `canFile === false` row disabled with the reason.
  const relatedSearch = useCallback(
    async (query: string, type: EntityType): Promise<EntitySearchResult[]> => {
      if (!apiBaseUrl || !getAccessToken) {
        throw new Error("Couldn't search: the pane isn't fully configured. Reload and try again.");
      }

      let res: Response;
      try {
        const token = await getAccessToken();
        res = await authenticatedJsonFetch(
          `${apiBaseUrl}/api/office/search/entities?q=${encodeURIComponent(query)}&type=${type}&top=10&access=file`,
          { headers: { 'Content-Type': 'application/json' } },
          token,
          { getRetryToken: getAccessToken }
        );
      } catch {
        // Network/token failure before any response existed — never silently "no matches".
        throw new Error("Couldn't reach Spaarke to search. Check your connection and try again.");
      }

      if (!res.ok) {
        const errorMsg = await describeFetchFailure(res);
        throw new Error(errorMsg.message);
      }

      const data = await res.json();
      const rows = (data.results ?? []) as Array<{
        id: string;
        entityType: string;
        logicalName: string;
        name: string;
        displayInfo?: string;
        canFile?: boolean | null;
      }>;
      // An EXPLICIT field map: any field not listed here is dropped. `canFile` must stay listed, or a
      // record the save would refuse renders as selectable again (task 084 pins this with a test).
      return rows.map(item => ({
        id: item.id,
        entityType: item.entityType as EntityType,
        logicalName: item.logicalName,
        name: item.name,
        ...(item.displayInfo ? { displayInfo: item.displayInfo } : {}),
        ...(item.canFile !== undefined ? { canFile: item.canFile } : {}),
      }));
    },
    [apiBaseUrl, getAccessToken]
  );

  // "New record" — BFF-backed inline create (Slice 3, #10). POST /api/office/quickcreate/{type}
  // creates the sprk_matter/sprk_project under the caller's ownership and returns it; the picker
  // auto-selects the created record as the Related-to. Matter (task 038): the request always carries
  // `matterTypeId` — a bare lowercase GUID (ADR-044) — and any server warning (e.g. an unresolvable
  // type, or a record that came back without a number) is surfaced, never swallowed. The pane never
  // sends or constructs a number: Dataverse's autonumber assigns MAT-/PRJ-###### (task 076, interim).
  //
  // Task 053: task 031 made Project owner resolution load-bearing — an unresolvable caller now gets a
  // 403 `owner_unresolved` with an actionable `detail` and NO row created. This used to be discarded
  // (`if (!res.ok) return null`), so the user clicked Create and saw nothing happen — the server did the
  // right thing and the pane hid it. A failure now THROWS a descriptive Error carrying the server's own
  // message (via `describeFetchFailure` — the same ProblemDetails-parsing path `errorMessages.ts` already
  // uses for the top-level save/collision flows); `RelatedToPicker`'s `handleCreate` catches it and shows
  // the real message instead of a generic "Couldn't create the {type}."
  //
  // Task 100 (owner UAT round 5 item 3): the body carries the "+ New" form's fields — description; matterTypeId +
  // practiceAreaId (Matter); projectTypeId (Project); assignedToContactId (all three; the server requires the caller
  // to hold Read on it). Every GUID is canonicalized with the shared `cleanGuid` (ADR-044); a field the form left
  // empty is omitted, which for Assigned To means "the server's default".
  const createRelatedRecord = useCallback(
    async (type: EntityType, input: CreateRecordInput): Promise<CreateRecordResult> => {
      const { name } = input;
      // Test harness: return a mock created record so the flow is iterable without the BFF.
      if (isBrowserTestMode()) {
        await new Promise(resolve => setTimeout(resolve, 400));
        return {
          record: {
            id: `demo-new-${Date.now()}`,
            entityType: type,
            logicalName: type === 'Matter' ? 'sprk_matter' : type === 'Project' ? 'sprk_project' : 'sprk_invoice',
            name,
            displayInfo: 'New',
          },
        };
      }
      if (!apiBaseUrl || !getAccessToken) {
        throw new Error(`Couldn't create the ${type}: the pane isn't fully configured. Reload and try again.`);
      }

      let res: Response;
      try {
        const token = await getAccessToken();
        const body = {
          name,
          ...(input.description ? { description: input.description } : {}),
          ...(input.matterTypeId ? { matterTypeId: cleanGuid(input.matterTypeId) } : {}),
          ...(input.practiceAreaId ? { practiceAreaId: cleanGuid(input.practiceAreaId) } : {}),
          ...(input.projectTypeId ? { projectTypeId: cleanGuid(input.projectTypeId) } : {}),
          ...(input.assignedToContactId ? { assignedToContactId: cleanGuid(input.assignedToContactId) } : {}),
        };
        res = await authenticatedJsonFetch(
          `${apiBaseUrl}/api/office/quickcreate/${type.toLowerCase()}`,
          { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) },
          token,
          { getRetryToken: getAccessToken }
        );
      } catch {
        // Network/token failure before any response existed — never silently "nothing happened".
        throw new Error(`Couldn't reach Spaarke to create the ${type}. Check your connection and try again.`);
      }

      if (!res.ok) {
        const errorMsg = await describeFetchFailure(res);
        throw new Error(errorMsg.message);
      }

      const data = (await res.json()) as {
        id: string;
        logicalName: string;
        name: string;
        warnings?: string[];
      };
      // A chosen reference row didn't resolve (renamed/removed on the server since this pane's cache was populated) —
      // clear THAT list's cache so the next fetch (a later create, or the pane's next open) reads the current table
      // rather than serving the same stale entry again.
      const sentLists: ReferenceListName[] = [
        ...(input.matterTypeId ? (['matter-types'] as const) : []),
        ...(input.practiceAreaId ? (['practice-areas'] as const) : []),
        ...(input.projectTypeId ? (['project-types'] as const) : []),
      ];
      for (const list of sentLists) {
        if (warningsIndicateReferenceNotFound(list, data.warnings)) clearReferenceListCache(list);
      }
      return {
        record: { id: data.id, entityType: type, logicalName: data.logicalName, name: data.name },
        ...(data.warnings && data.warnings.length > 0 ? { warnings: data.warnings } : {}),
      };
    },
    [apiBaseUrl, getAccessToken]
  );

  // Handle copy link. Task 088: unchanged on purpose (owner, 2026-10-03) — it copies what it always copied, the
  // saved file's link; only View Document moved to the Spaarke record. Task 111: the URL is the bar's own
  // (`SavedBarModel.copyUrl`) — the session's saved link, or, for a document already in Spaarke, its record link.
  const handleCopyLink = useCallback(
    async (url: string | null) => {
      if (url) {
        try {
          await navigator.clipboard.writeText(url);
          announce('Link copied', 'polite');
          flashButton('copy', 'done');
        } catch {
          announce('Failed to copy link', 'assertive');
          flashButton('copy', 'failed');
        }
      }
    },
    [announce, flashButton]
  );

  // Task 105 (UAT round 7 item 3): View Document / Copy Link confirm the click for ~2 s, then revert.
  const handleViewDocument = useCallback(
    (documentId: string) => {
      const opened = openDocumentRecord(documentId);
      announce(opened ? 'Opened' : 'Could not open the document', opened ? 'polite' : 'assertive');
      flashButton('view', opened ? 'done' : 'failed');
    },
    [openDocumentRecord, announce, flashButton]
  );

  // Task 099: a save just completed (the Save button the user pressed is gone) — focus the confirmation.
  useEffect(() => {
    if (flowState === 'complete') savedBarRef.current?.focus();
  }, [flowState]);

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
          {(jobStatus.stages ?? []).map(stage => (
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

  // Task 088 (UAT-1/6, owner 2026-10-03 "Keep the form"): the confirmation bar at the top of the SAVED state.
  // Replaces the full-screen success card. Names the record the document is filed to — or says it is filed to
  // none — with View Document (the Spaarke record, in the Spaarke app) and Copy Link (unchanged).
  const renderSavedBar = (bar: SavedBarModel) => (
    <div ref={savedBarRef} tabIndex={-1} className={styles.savedBarWrap}>
      <MessageBar intent="success" layout="multiline">
        <MessageBarBody>
          <MessageBarTitle>{bar.title}</MessageBarTitle>
          {bar.filingUnknown ? (
            <Text size={200} className={styles.savedBarDetail}>
              Filing record not available here.
            </Text>
          ) : (
            bar.filedTo !== undefined && (
              <Text size={200} className={styles.savedBarDetail}>
                {bar.filedTo === null ? (
                  bar.notFiledText
                ) : (
                  <>
                    Filed to <Text weight="semibold">{bar.filedTo}</Text>.
                  </>
                )}
              </Text>
            )
          )}
        </MessageBarBody>
        <MessageBarActions>
          {/* NFR-10: rendered only when the host can open a browser tab AND ORG_URL is set — never rendered
            disabled (AC4). */}
          {openRecordAvailable && (
            <Button
              appearance="outline"
              size="small"
              className={styles.savedBarButton}
              icon={buttonFeedback.view === 'done' ? <CheckmarkRegular /> : <OpenRegular />}
              onClick={() => handleViewDocument(bar.documentId)}
            >
              {buttonFeedback.view === 'done'
                ? 'Opened'
                : buttonFeedback.view === 'failed'
                  ? "Couldn't open"
                  : 'View Document'}
            </Button>
          )}
          {(bar.copyUrl !== null || !bar.hideCopyWithoutUrl) && (
            <Button
              appearance="outline"
              size="small"
              className={styles.savedBarButton}
              icon={buttonFeedback.copy === 'done' ? <CheckmarkRegular /> : <CopyRegular />}
              onClick={() => void handleCopyLink(bar.copyUrl)}
              disabled={!bar.copyUrl}
            >
              {buttonFeedback.copy === 'done'
                ? 'Copied'
                : buttonFeedback.copy === 'failed'
                  ? "Couldn't copy"
                  : 'Copy Link'}
            </Button>
          )}
        </MessageBarActions>
      </MessageBar>
    </div>
  );

  // The box for a save this pane just made.
  const savedBarOf = (saved: SavedDocumentState): SavedBarModel => ({
    documentId: saved.documentId,
    title: saved.lastSave === 'version' ? 'New version saved to Spaarke' : 'Saved to Spaarke',
    filedTo: saved.filedTo,
    notFiledText: 'Not filed to a record.',
    copyUrl: savedDocumentUrl ?? null,
    hideCopyWithoutUrl: false,
  });

  // Task 111: the box for a document that was already in Spaarke when the pane opened. Copy Link copies the session's
  // saved URL when there is one, else the document's Spaarke record link; with neither, it is not shown.
  const resolvedBarOf = (identity: { documentId: string }): SavedBarModel => {
    const orgUrl = process.env.ORG_URL;
    const recordUrl = orgUrl
      ? buildOpenRecordUrl(orgUrl, 'sprk_document', cleanGuid(identity.documentId), configuredSpaarkeAppName())
      : null;
    return {
      documentId: identity.documentId,
      title: 'Saved to Spaarke',
      filedTo: resolvedRelatedRecord.kind === 'associated' ? relatedRecordLabel(resolvedRelatedRecord) : null,
      filingUnknown: resolvedRelatedRecord.kind === 'unknown',
      notFiledText: 'Not filed to a record yet.',
      copyUrl: savedDocumentUrl ?? recordUrl,
      hideCopyWithoutUrl: true,
    };
  };

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
        {/* Task 088: this used to hand a document ID to a callback that expected a URL — a broken link. It now
            opens the existing document's Spaarke record, through the same opener and gate as View Document. */}
        {openRecordAvailable && duplicateInfo && (
          <Button
            appearance="primary"
            icon={<OpenRegular />}
            onClick={() => openDocumentRecord(duplicateInfo.documentId)}
          >
            View Existing Document
          </Button>
        )}
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
        {error?.offerSaveAsNew && (saveMode.view === 'version' || savedDocument !== null) && (
          <Button appearance="outline" size="small" onClick={handleSaveAsNewInstead}>
            Save as new document
          </Button>
        )}
        <Button appearance="subtle" size="small" onClick={clearError}>
          Dismiss
        </Button>
      </MessageBarActions>
    </MessageBar>
  );

  // Task 025: a refused CREATE save's filename collision (OFFICE_020) — the shipped two-option choice,
  // mirroring the OBO upload wizard's own dialog (Keep both / Save as new version) without importing it
  // (ADR-012 exception). Kept as ITS OWN MessageBar, separate from renderErrorState, so a collision never
  // reads as a failure: `intent="warning"` (never "error"), stated in neutral text — "Nothing was saved"
  // — per the shipped wizard's own copy convention. Dismissing (the Dismiss button, same clearError as
  // every other error state) writes nothing further: the refusal itself already left no bytes and no
  // sprk_document row (server-side; task 025's non-destructive invariant).
  //
  // Task 088 (UAT-5): the choices are now Keep both · Save as new version (only on the server's
  // `canSaveAsVersion`) · Open (whenever the server identified a document the caller can read) · Dismiss.
  const renderCollisionState = () => {
    const existingDocumentId = error?.collisionExistingDocumentId;
    const offerVersion = Boolean(existingDocumentId) && error?.collisionCanSaveAsVersion === true;
    return (
      <MessageBar intent="warning">
        <MessageBarBody>
          <MessageBarTitle>{error?.title || 'Name Already Exists'}</MessageBarTitle>
          {error?.message}
          {/* Task 055 (#1005): NAME the document that holds the name. Offering a retry against an opaque id is
              what let a document be written as a new version of an unrelated one that merely shared Word's
              default file name. Absent when the server withheld it (the caller cannot read that document) —
              and then neither "Open" nor a version retry is offered. */}
          {error?.collisionExistingDocumentName && (
            <Text
              size={200}
              style={{
                display: 'block',
                marginTop: tokens.spacingVerticalXS,
                fontWeight: tokens.fontWeightSemibold,
              }}
            >
              That name belongs to: {error.collisionExistingDocumentName}
            </Text>
          )}
          <Text size={200} className={styles.collisionNote}>
            {offerVersion
              ? 'Keep both uploads this file under a new name. Save as new version keeps the existing document and adds this file as its latest version.'
              : 'Keep both uploads this file under a new name.'}
            {existingDocumentId
              ? canOpenDesktopWord
                ? ' Open in Word opens desktop Word; Open in browser opens Word for the web.'
                : ' Open in browser shows the existing file.'
              : ''}
          </Text>
          {/* Shown in the prompt; screen readers already get it from the assertive live region (announce),
              so no second role="alert" here. */}
          {openFileError && (
            <Text size={200} className={styles.collisionError}>
              {openFileError}
            </Text>
          )}
        </MessageBarBody>
        <MessageBarActions>
          <Button appearance="primary" size="small" onClick={handleKeepBoth}>
            Keep both
          </Button>
          {offerVersion && (
            <Button appearance="secondary" size="small" onClick={handleSaveAsVersionInstead}>
              Save as new version
            </Button>
          )}
          {/* Task 094: PC/Mac only (`canOpenDesktopWord`, NFR-10 — platform, never hostType) — the
              unsupported `ms-word:` anchor-click trial. "Open in browser" is always offered alongside
              it (and alone everywhere else) as the real fallback, never a retry of this one. */}
          {existingDocumentId && canOpenDesktopWord && (
            <Button
              appearance="secondary"
              size="small"
              icon={openFileBusy ? <Spinner size="tiny" /> : <OpenRegular />}
              onClick={() => void handleOpenCollisionFile('desktop')}
              disabled={openFileBusy}
            >
              Open in Word
            </Button>
          )}
          {existingDocumentId && (
            <Button
              appearance="secondary"
              size="small"
              icon={openFileBusy ? <Spinner size="tiny" /> : <OpenRegular />}
              onClick={() => void handleOpenCollisionFile('browser')}
              disabled={openFileBusy}
            >
              Open in browser
            </Button>
          )}
          <Button appearance="subtle" size="small" onClick={clearError}>
            Dismiss
          </Button>
        </MessageBarActions>
      </MessageBar>
    );
  };

  // Render main form
  //
  // task 040 / FR-19 audit: the `hostType === 'outlook'` reads below (label, sender, sent date,
  // attachments) are host-shaped DATA, not feature gates — by the time these props reach SaveFlow,
  // SaveView has already capability-gated WHICH VALUES got populated (canGetSender/canGetAttachments/
  // etc.), so senderEmail/senderDisplayName/sentDate/attachments are already empty on Word regardless
  // of this check. Left as-is (documented in notes/parity-checklist.md) rather than threading a
  // separate `itemType` prop through for a condition the data already enforces.
  const renderForm = () => {
    // Task 099 (owner item 2): while "+ New" is open, only the pills + the create form show. Only meaningful
    // where the picker is rendered (a new document); version mode never shows it.
    const hideForCreate = relatedCreating && (!isVersionMode || showFilingPicker);
    return (
      <>
        {/* Task 111: a document already in Spaarke says so with the same green box a save ends on. */}
        {!hideForCreate && showResolvedBox && resolvedIdentity && renderSavedBar(resolvedBarOf(resolvedIdentity))}

        {!hideForCreate &&
          // Task 099 (owner item 5): the Document header IS where the name is edited (or shown locked) — there is
          // no separate "Document Details" card any more.
          (!isVersionMode
            ? renderDocumentHeader('editable')
            : renderDocumentHeader(
                canSaveNewVersion ? 'locked' : 'readonly',
                saveMode.documentLabel ?? itemName ?? undefined
              ))}

        {/* Filed-to card — task 026 / FR-09. A READ-BACK of what the identified document is ALREADY filed to,
          sourced from the SAME resolved identity SaveModeSection reads (no second network call, so the pane
          never shows two different answers). Deliberately OUTSIDE the `!isVersionMode` gate below: a resolved
          identity DEFAULTS to a version save (task 024), which is exactly when RelatedToPicker is hidden — the
          filed-to card is the pane's only indication of the record in that common case. It stays visually and
          functionally distinct from RelatedToPicker (an INPUT for an unfiled document) even when both render
          together for an explicit "a new document" override. The click seam (`onOpenRecord`) is task 027 /
          FR-10 (NFR-10): supplied only when `canOpenRecord` is true — absent it, the card renders as
          plain, non-interactive text (its own fallback, not a host-type branch here). */}
        {!hideForCreate && !showFilingPicker && (
          <RelatedRecordCard
            {...(documentIdentity !== undefined ? { documentIdentity } : {})}
            {...(openRecordAvailable ? { onOpenRecord: handleOpenRelatedRecord } : {})}
          />
        )}

        {/* Task 111 (owner items 4-5): a KNOWN-unfiled document (a URL-resolved identity whose record slots the server
          read as empty) gets the record picker in place of the empty "Filed to" card, with an explicit "File to
          record" action. Never offered for a stamp-only identity (record unknown — see showFilingPicker). */}
        {showFilingPicker && renderFilingSection()}

        {/* Related to applies to a NEW document only. A version save (task 024) keeps the existing record's
          name and associations — the server never renames or re-associates on that path (task 023 D-5) — so
          the picker would have no effect there, and is not shown. It appears as soon as the user chooses "A new
          document" (task 094: a document already in Spaarke gets the LOCKED name in the header, with its own
          "Save as new document" unlock). */}
        {!isVersionMode && (
          <div className={styles.section}>
            <RelatedToPicker
              value={selectedEntity}
              onChange={handleEntitySelect}
              candidates={relatedCandidates}
              candidatesLoading={candidatesLoading}
              onSearch={relatedSearch}
              onCreateRecord={createRelatedRecord}
              // Matter/Project/Invoice only (Account/Contact removed — UI feedback 2026-09-02).
              allowedTypes={['Matter', 'Project', 'Invoice']}
              defaultType="Matter"
              createForm={createFormData}
              {...(onSearchContacts ? { onSearchContacts } : {})}
              onCreatingChange={setRelatedCreating}
              disabled={isSaving}
            />
          </div>
        )}

        {!hideForCreate && (
          <>
            {/* Profile — task 021 / FR-07. Read-only AI profile for the document identity task 013 resolved, or
              an honest per-status / no-identity state. `refreshSignal` is the task 027 / FR-10 return-path
              re-read. Task 088: the Document-record affordance that sat here moved to the footer. */}
            <div className={styles.section}>
              <DocumentProfileSection
                {...(resolvedDocumentId !== undefined ? { documentId: resolvedDocumentId } : {})}
                refreshSignal={profileRefreshSignal}
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

            {/* AI processing (Profile Summary + Search Index) is mandatory for all content saved to Spaarke —
              always on (DEFAULT_PROCESSING_OPTIONS), no toggles (UI feedback 2026-09-02). */}

            {/* FR-11 save mode (task 024) — directly above Cancel / Save: a resolved document defaults to "a new
              version", with an explicit "a new document" override; every other identity outcome states what
              happens and what the user can do. Inline, not a modal (narrow pane). */}
            <SaveModeSection
              identity={documentIdentity}
              resolution={saveMode}
              onChoiceChange={handleSaveModeChange}
              {...(onRetryDocumentIdentity ? { onRetryIdentity: onRetryDocumentIdentity } : {})}
              disabled={isSaving}
            />

            {renderFooter()}
          </>
        )}
      </>
    );
  };

  // The Document header (task 099, owner round 5 item 5): the ONE place the document's name is shown and edited,
  // shared by the pre-save form and the saved state. It replaces the former "Document Details" card.
  //
  // Task 094 (owner, 2026-10-04 — "Locked, with Save as new"): once a document is in Spaarke — the
  // pane opened on an already-resolved identity (pre-save `isVersionMode`), or this pane session
  // already saved it — a save can never rename it (name/file names are set only by a NEW document or
  // "Save as new document"). Three modes:
  // - `'editable'`: the plain new-document path (020's pencil/Input, or Outlook's Textarea) — unchanged.
  // - `'locked'`: the Spaarke name, read-only, with a hint + a "Save as new document" link that
  //   unlocks it (pre-save: switches the save mode choice to "new"; post-save: leaves the saved state —
  //   both routes through `handleSaveAsNewInstead`, so there is exactly one unlock path).
  // - `'readonly'`: the name, read-only, with NO hint/unlock — the saved state of a pane with no
  //   document bytes at all (Outlook), where there is nothing to version or rename from here.
  //
  // `lockedName` overrides the displayed value for `'locked'` only — the TRUE Spaarke name (the
  // resolved identity's `documentName`/`fileName` for a version save, or the name this pane's own
  // create save used) rather than the live `documentName` field state, which this mode never lets the
  // user edit and which may otherwise still carry a stale FR-06 default derived from `itemName`.
  //
  // A host that cannot supply a document name (`!canProvideDocumentName` — Outlook) additionally shows the item's
  // own name (the email subject) as the first row, with the sender/date rows — host-shaped DATA, as before.
  function renderDocumentHeader(mode: 'editable' | 'locked' | 'readonly', lockedName?: string): React.ReactElement {
    const shownName =
      mode === 'locked' && lockedName !== undefined ? lockedName : documentName || itemName || 'Untitled Document';
    return (
      <div className={styles.section}>
        <div className={styles.sectionTitle}>
          <DocumentRegular />
          <Text weight="semibold">{hostType === 'outlook' ? 'Email' : 'Document'}</Text>
        </div>
        <div className={styles.documentInfo}>
          {showDocumentInfo && itemName && !canProvideDocumentName && (
            <div className={styles.documentInfoRow}>
              <Text weight="semibold">{itemName}</Text>
            </div>
          )}
          {showDocumentInfo && hostType === 'outlook' && (senderDisplayName || senderEmail) && (
            <div className={styles.documentInfoRow}>
              <Text size={200}>From: {senderDisplayName || senderEmail}</Text>
            </div>
          )}
          {showDocumentInfo && hostType === 'outlook' && sentDate && (
            <div className={styles.documentInfoRow}>
              <Text size={200}>Sent: {sentDate.toLocaleDateString()}</Text>
            </div>
          )}
          {mode === 'readonly' && <Text className={styles.documentNameText}>{shownName}</Text>}
          {mode === 'locked' && (
            <div className={styles.documentNameLockedRow}>
              <Text className={styles.documentNameText}>{shownName}</Text>
              <Text size={200} className={styles.documentNameHint}>
                This document&rsquo;s name is set in Spaarke. To rename it, save a new document.
              </Text>
              {/* The error banner above (renderErrorState) already offers this SAME action while it's
                  showing one (OFFICE_009/OFFICE_016 "Save as new document") — suppressed here so the
                  pane never shows two buttons with the identical label and effect at once. */}
              {!error?.offerSaveAsNew && (
                <Button
                  appearance="transparent"
                  size="small"
                  className={styles.secondaryLink}
                  onClick={handleSaveAsNewInstead}
                  disabled={isSaving}
                >
                  Save as new document
                </Button>
              )}
            </div>
          )}
          {mode === 'editable' &&
            (canProvideDocumentName ? (
              isEditingDocumentName ? (
                <Input
                  id="document-name"
                  value={documentName}
                  onChange={handleDocumentNameChange}
                  onKeyDown={handleDocumentNameKeyDown}
                  onBlur={handleDocumentNameBlur}
                  placeholder="Enter document name"
                  disabled={isSaving}
                  aria-label="Document name"
                  maxLength={850}
                  autoFocus
                />
              ) : (
                <div className={styles.documentNameDisplay}>
                  <Text className={styles.documentNameText}>{documentName || 'Untitled Document'}</Text>
                  <Button
                    ref={el => {
                      nameFocusRef.current = el;
                    }}
                    appearance="subtle"
                    size="small"
                    icon={<EditRegular />}
                    onClick={handleStartEditDocumentName}
                    disabled={isSaving}
                    aria-label="Edit document name"
                  />
                </div>
              )
            ) : (
              <Textarea
                id="document-name"
                ref={el => {
                  nameFocusRef.current = el;
                }}
                value={documentName}
                onChange={(_e, data) => setDocumentName(data.value)}
                placeholder="Enter document name"
                disabled={isSaving}
                aria-label="Document name"
                rows={2}
              />
            ))}
        </div>
      </div>
    );
  }

  // Task 111: the filing section — the existing picker (same types, search and "+ New" as a new save) plus the
  // explicit "File to record" action. Its own selection state (`fileTarget`), never `selectedEntity`: a version save
  // sends no record, and the two must not share a selection.
  function renderFilingSection(): React.ReactElement {
    return (
      <div className={styles.section}>
        {!relatedCreating && (
          <div className={styles.sectionTitle}>
            <Text weight="semibold">File to a record</Text>
          </div>
        )}
        <RelatedToPicker
          value={fileTarget}
          onChange={handleFileTargetSelect}
          candidates={relatedCandidates}
          candidatesLoading={candidatesLoading}
          onSearch={relatedSearch}
          onCreateRecord={createRelatedRecord}
          allowedTypes={['Matter', 'Project', 'Invoice']}
          defaultType="Matter"
          createForm={createFormData}
          {...(onSearchContacts ? { onSearchContacts } : {})}
          onCreatingChange={setRelatedCreating}
          disabled={filing}
        />
        {!relatedCreating && (
          <>
            {filingError && (
              <Text size={200} className={styles.filingError} role="alert">
                {filingError}
              </Text>
            )}
            <div className={styles.filingActions}>
              <Button
                appearance="primary"
                {...(filing ? { icon: <Spinner size="tiny" /> } : {})}
                onClick={() => void handleFileToRecord()}
                disabled={!fileTarget || filing}
              >
                {filing ? 'Filing...' : 'File to record'}
              </Button>
            </div>
          </>
        )}
      </div>
    );
  }

  // Footer actions (task 099, owner round 5 item 6 + decision A).
  // BEFORE a save: wizard pattern — Cancel (left); Open Document then Save (right). Open Document (the
  // `sprk_document` record, in the Spaarke app) shows when there is a document to open — the resolved one — and
  // the host can open a tab with ORG_URL set (NFR-10). Save stays disabled while the save mode is unsettled
  // (identity checking, a conflict, or an undetermined identity with no choice).
  // AFTER a save: no Cancel, no Open Document (the confirmation's View Document is the way in), and no Save until
  // the document is edited (`hasUnsavedChanges`) — then a lone "Save" (= a new version of this document). The
  // button is never "Save version" and never a gray "Saved" (the confirmation says so).
  function renderFooter(): React.ReactElement | null {
    if (savedDocument !== null) {
      if (!hasUnsavedChanges && !isSaving) return null;
      return (
        <div className={styles.footer}>
          <div className={styles.footerEnd}>
            <Button appearance="primary" onClick={handleSave} disabled={isSaving || !isValid || !activeTarget}>
              {isSaving ? 'Saving...' : 'Save'}
            </Button>
          </div>
        </div>
      );
    }
    return (
      <div className={styles.footer}>
        <Button appearance="secondary" onClick={handleCancel} disabled={isSaving}>
          Cancel
        </Button>
        <div className={styles.footerEnd}>
          {/* Task 111 (owner item 6): the green box above already has View Document — no second button. */}
          {openRecordAvailable && openableDocumentId && !showResolvedBox && (
            <Button
              appearance="secondary"
              icon={<OpenRegular />}
              onClick={() => openDocumentRecord(openableDocumentId)}
              disabled={isSaving}
            >
              Open Document
            </Button>
          )}
          <Button appearance="primary" onClick={handleSave} disabled={isSaving || !isValid || !activeTarget}>
            {isSaving ? 'Saving...' : 'Save'}
          </Button>
        </div>
      </div>
    );
  }

  // The SAVED state (task 088, "Keep the form"; round 5 task 099): the confirmation bar (View Document, Copy
  // Link), the Document header (the name, locked, with "Save as new document"), the Profile with Refresh, and —
  // only once the document has been edited — Save. Host-neutral: an Outlook save lands here too (NFR-10); what
  // differs is only what the capability allows (no document bytes → the name is read-only and there is no Save).
  //
  // Task 094: the LOCKED name shows the document's TRUE Spaarke name — for a version save, the resolved
  // identity's own name/file name (the record's name, which a version save never changes — task 023 D-5),
  // not necessarily `saved.savedName` (what this pane's documentName field held at submission, which for a
  // version save was never user-edited and may drift from the record's actual name). A create save's name
  // IS what was typed, so `saved.savedName` is exactly right there.
  const renderSavedForm = (saved: SavedDocumentState) => {
    const identityName =
      documentIdentity && typeof documentIdentity === 'object' && documentIdentity.kind === 'resolved'
        ? documentIdentity.documentName || documentIdentity.fileName || null
        : null;
    const lockedName = saved.lastSave === 'version' ? identityName || saved.savedName : saved.savedName;
    return (
      <>
        {renderSavedBar(savedBarOf(saved))}
        {renderDocumentHeader(canSaveNewVersion ? 'locked' : 'readonly', lockedName)}
        <div className={styles.section}>
          <DocumentProfileSection
            documentId={saved.documentId}
            refreshSignal={profileRefreshSignal}
            mode="refresh"
            onRefresh={handleRefreshSaved}
          />
        </div>
        {renderFooter()}
      </>
    );
  };

  // The form for the current state: the saved state once this pane has saved, else the pre-save form.
  const renderCurrentForm = () => (savedDocument ? renderSavedForm(savedDocument) : renderForm());

  // Determine what to render based on flow state
  const renderContent = () => {
    switch (flowState) {
      case 'complete':
        return renderCurrentForm();
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
            {error?.offerCollisionChoice ? renderCollisionState() : renderErrorState()}
            {renderCurrentForm()}
          </>
        );
      case 'idle':
      case 'selecting':
      default:
        return renderCurrentForm();
    }
  };

  return (
    <div className={mergeClasses(styles.container, className)} role="form" aria-label="Save to Spaarke">
      {/* React-owned ARIA live regions (task 018 / NFR-11) -- must be rendered
          by this component per useAnnounce's contract; placement doesn't
          matter visually since the regions are sr-only. */}
      {liveRegion}
      {renderContent()}
    </div>
  );
}

export default SaveFlow;
