import React, { useCallback, useMemo, useEffect, useState, useRef } from 'react';
import {
  makeStyles,
  tokens,
  Button,
  Card,
  Text,
  Body1,
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
  Label,
} from '@fluentui/react-components';
import {
  DocumentRegular,
  ArrowResetRegular,
  CheckmarkCircleRegular,
  ErrorCircleRegular,
  OpenRegular,
  CopyRegular,
  EditRegular,
} from '@fluentui/react-icons';
import { RelatedToPicker, type CreateRecordResult } from './RelatedToPicker';
import { RelatedRecordCard } from './RelatedRecordCard';
import { AttachmentSelector } from './AttachmentSelector';
import { DocumentProfileSection } from './DocumentProfileSection';
import { SaveModeSection, resolveSaveMode, type SaveModeChoice } from './SaveModeSection';
import type { EntitySearchResult, EntityType } from '../hooks/useEntitySearch';
import type { RelatedRecordView } from '../hooks/useRelatedRecord';
import {
  useSaveFlow,
  type SaveFlowContext,
  type SaveTarget,
  type StageStatus,
  type UseSaveFlowOptions,
} from '../hooks/useSaveFlow';
import type { DocumentIdentityState } from '../services/documentIdentityService';
import { useAnnounce } from '../hooks/useAnnounce';
import { fetchRelatedCandidates, type RelatedCandidate } from '../services/communicationSuggestionsService';
import {
  fetchMatterTypes,
  clearMatterTypesCache,
  warningsIndicateMatterTypeNotFound,
  type MatterTypeChoice,
} from '../services/matterTypeLookupService';
import { openRecord } from '../services/openRecordLauncher';
import { cleanGuid } from '../utils/cleanGuid';
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
 * Task 020 (FR-06): strips a trailing `.docx`/`.doc` extension (case-insensitive) from a display name,
 * for deriving the default Document Name from the filename-shaped `itemName` prop. A value with no such
 * extension (e.g. Word's "Untitled Document" fallback) is returned unchanged — this is normalization,
 * not a requirement that an extension be present.
 */
function stripDocumentExtension(name: string): string {
  return name.replace(/\.docx?$/i, '');
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
 * Demo Matter Type options for the browser test harness ONLY — mirrors the real
 * `GET /api/office/search/matter-types` five-row dev list (task 038) so the required-field UX is
 * iterable without the BFF.
 */
const DEMO_MATTER_TYPES: MatterTypeChoice[] = [
  { id: '6cedd99b-30da-f011-8406-7ced8d1dc988', name: 'Commercial', code: 'CMRCL' },
  { id: 'cdbf53b0-30da-f011-8406-7ced8d1dc988', name: 'Employment', code: 'EMPL' },
  { id: '11aed095-30da-f011-8406-7ced8d1dc988', name: 'Litigation', code: 'LITG' },
  { id: '46c35aa2-30da-f011-8406-7ced8d1dc988', name: 'Patent', code: 'PAT' },
  { id: '60c35aa2-30da-f011-8406-7ced8d1dc988', name: 'Trademark', code: 'TMRK' },
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
    resolvedDocumentId,
    documentIdentity,
    onRetryDocumentIdentity,
    canOpenRecord = false,
    canSuggestRelatedRecords = false,
    getAccessToken,
    apiBaseUrl = '',
    onComplete,
    onSaved,
    onViewDocument,
    showDocumentInfo = true,
    className,
  } = props;

  const styles = useStyles();
  const { announce, liveRegion } = useAnnounce();

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
      onDuplicate: (_docId, message) => {
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

  // Local state for document metadata fields.
  //
  // Task 020 (FR-06): for Word (`hostType === 'word'`, i.e. ContentType=Document — the same host-shaped-data
  // proxy the task 040 audit already sanctions elsewhere in this file), the field defaults to the open
  // document's own file name minus its extension (derived from `itemName`, which for Word is what the whole
  // save pipeline already treats as "the document's name" — see `defaultDocumentName` below) and is editable
  // in-pane behind a pencil affordance. Outlook (Email) is DELIBERATELY untouched: `documentName` still starts
  // empty with no default and no pencil, exactly as before task 020, so `effectiveDocumentName` /
  // `email.isNameSystemDerived` (useSaveFlow.ts, task 046 (b)) keep their existing meaning. Lazy-initialized
  // from the default so a Word pane's very first render already shows it (SaveView only mounts SaveFlow once
  // its own itemName load completes) rather than flashing empty-then-populated.
  const [documentName, setDocumentName] = useState<string>(() =>
    hostType === 'word' && itemName ? stripDocumentExtension(itemName) : ''
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

  // Task 020 (FR-06): the FR-06 default — Word's open-document filename, minus its extension. `undefined`
  // for Outlook (no default there) and while `itemName` hasn't loaded yet.
  const defaultDocumentName = useMemo(
    () => (hostType === 'word' && itemName ? stripDocumentExtension(itemName) : undefined),
    [hostType, itemName]
  );

  // Keeps the field in sync with the default until the user edits it or opens the editor — covers the
  // ordinary case where SaveView mounts SaveFlow before its own `itemName` load resolves, and the
  // identity-retry re-read (task 027 / FR-10) which can re-fire `itemName`/`hostType`.
  useEffect(() => {
    if (hostType !== 'word') return;
    if (documentNameTouched || isEditingDocumentName) return;
    if (defaultDocumentName !== undefined) setDocumentName(defaultDocumentName);
  }, [defaultDocumentName, hostType, documentNameTouched, isEditingDocumentName]);

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

  // Matter Type list for the required field on Matter quick-create (task 038). A small reference list
  // (5 rows in dev) loaded once on mount, host-neutral (NFR-10: no hostType check), never gated on a
  // keystroke like the /search/entities typeahead.
  //
  // A failed load and "the table has zero active rows" are different states (coordinator fix,
  // 2026-09-13): fetchMatterTypes THROWS on failure, so a transient BFF/network fault surfaces as
  // `matterTypesError` — a readable, announced (NFR-11), non-blocking message with a single
  // user-initiated Retry — rather than silently making Matter quick-create permanently unsatisfiable
  // for the rest of the session. Project/Invoice quick-create never reads this state at all.
  const [matterTypes, setMatterTypes] = useState<MatterTypeChoice[]>([]);
  const [matterTypesLoading, setMatterTypesLoading] = useState(false);
  const [matterTypesError, setMatterTypesError] = useState<string | null>(null);
  const matterTypesMountedRef = useRef(true);
  useEffect(
    () => () => {
      matterTypesMountedRef.current = false;
    },
    []
  );

  const loadMatterTypes = useCallback(async () => {
    if (isBrowserTestMode()) {
      setMatterTypes(DEMO_MATTER_TYPES);
      setMatterTypesError(null);
      return;
    }
    if (!apiBaseUrl || !getAccessToken) return;
    setMatterTypesLoading(true);
    setMatterTypesError(null);
    try {
      const token = await getAccessToken();
      const types = await fetchMatterTypes(apiBaseUrl, token, getAccessToken);
      if (!matterTypesMountedRef.current) return;
      setMatterTypes(types);
    } catch {
      if (!matterTypesMountedRef.current) return;
      const message = "Couldn't load matter types. Try again, or search for an existing record instead.";
      setMatterTypesError(message);
      // NFR-11: the failure is announced — this is the one state change here a screen-reader user
      // could otherwise miss entirely (the field just stays a disabled, empty-looking dropdown).
      announce(message, 'assertive');
    } finally {
      if (matterTypesMountedRef.current) setMatterTypesLoading(false);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- apiBaseUrl/getAccessToken are stable for the pane's lifetime; announce is stable per useAnnounce
  }, [apiBaseUrl, getAccessToken]);

  useEffect(() => {
    void loadMatterTypes();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- fetched once per mount; loadMatterTypes is re-invoked explicitly by the Retry action, not by a dependency change
  }, []);

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
  const [profileRefreshSignal, setProfileRefreshSignal] = useState(0);

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

  const handleOpenDocumentRecord = useCallback(() => {
    if (!resolvedDocumentId) return;
    const result = openRecord({
      orgUrl: process.env.ORG_URL,
      entityType: 'sprk_document',
      recordId: resolvedDocumentId,
    });
    if (result.opened) {
      hasOpenedExternalRecordRef.current = true;
    }
  }, [resolvedDocumentId]);

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
      setProfileRefreshSignal(signal => signal + 1);
      announce('Refreshed with the latest changes from the record.', 'polite');
    }
    document.addEventListener('visibilitychange', handlePaneReturn);
    window.addEventListener('focus', handlePaneReturn);
    return () => {
      document.removeEventListener('visibilitychange', handlePaneReturn);
      window.removeEventListener('focus', handlePaneReturn);
    };
  }, [onRetryDocumentIdentity, announce]);

  // Build save context
  const buildSaveContext = useCallback(
    (): SaveFlowContext => ({
      hostType,
      attachments,
      ...(saveMode.target ? { saveTarget: saveMode.target } : {}),
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
      saveMode.target,
    ]
  );

  // Handle save button click. A null target means the save mode is not settled (identity still checking,
  // a conflict, or an undetermined identity with no explicit choice) — the button is disabled then, and this
  // guard makes sure nothing is sent even if it were not.
  const handleSave = useCallback(() => {
    if (!saveMode.target) return;
    setSubmittedTarget(saveMode.target);
    startSave(buildSaveContext());
  }, [buildSaveContext, saveMode.target, startSave]);

  // Cancel = clear the current selection + fields (wizard "Cancel" pattern), and return the save mode to
  // the identity's default.
  const handleCancel = useCallback(() => {
    setSelectedEntity(null);
    setDocumentName('');
    // Task 020: un-touch so the sync effect re-applies the Word FR-06 default (Outlook has none, so this
    // is a no-op there — the field simply stays empty, exactly as before task 020).
    setDocumentNameTouched(false);
    setIsEditingDocumentName(false);
    setSaveModeChoice(null);
    reset();
  }, [setSelectedEntity, reset]);

  // A refused VERSION save whose cause is the existing document (task 024): switch to "a new document" so
  // the user can choose where to file it and save. Deliberately does not save on its own.
  const handleSaveAsNewInstead = useCallback(() => {
    setSaveModeChoice('new');
    clearError();
    announce('Save mode: a new document. Choose where to file it, then select Save.', 'polite');
  }, [clearError, announce]);

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

  // "New record" — BFF-backed inline create (Slice 3, #10). POST /api/office/quickcreate/{type}
  // creates the sprk_matter/sprk_project under the caller's ownership and returns it; the picker
  // auto-selects the created record as the Related-to. Matter (task 038): the request always carries
  // `matterTypeId` — a bare lowercase GUID (ADR-044) — and any server warning (e.g. an unresolvable
  // type, or numbering not existing yet) is surfaced, never swallowed. The pane never sends or
  // constructs a matter number; that is a separate server-side numbering project.
  const createRelatedRecord = useCallback(
    async (type: EntityType, name: string, matterTypeId?: string): Promise<CreateRecordResult | null> => {
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
      if (!apiBaseUrl || !getAccessToken) return null;
      try {
        const token = await getAccessToken();
        const body = type === 'Matter' && matterTypeId ? { name, matterTypeId: cleanGuid(matterTypeId) } : { name };
        const res = await authenticatedJsonFetch(
          `${apiBaseUrl}/api/office/quickcreate/${type.toLowerCase()}`,
          { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) },
          token,
          { getRetryToken: getAccessToken }
        );
        if (!res.ok) return null;
        const data = (await res.json()) as {
          id: string;
          logicalName: string;
          name: string;
          warnings?: string[];
        };
        // The chosen matterTypeId didn't resolve (renamed/removed on the server since this pane's
        // cache was populated) — clear the cache so the NEXT fetch (a later create, or the pane's next
        // open) picks up the current reference table rather than serving the same stale entry again.
        if (type === 'Matter' && warningsIndicateMatterTypeNotFound(data.warnings)) {
          clearMatterTypesCache();
        }
        return {
          record: { id: data.id, entityType: type, logicalName: data.logicalName, name: data.name },
          ...(data.warnings && data.warnings.length > 0 ? { warnings: data.warnings } : {}),
        };
      } catch {
        return null;
      }
    },
    [apiBaseUrl, getAccessToken]
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

  // Render success state
  const renderSuccessState = () => (
    <Card className={styles.successCard}>
      <CheckmarkCircleRegular className={styles.successIcon} aria-hidden="true" />
      <Text size={500} weight="semibold">
        {submittedTarget?.mode === 'version' ? 'New Version Saved' : 'Document Saved'}
      </Text>
      <Body1 style={{ marginTop: tokens.spacingVerticalS }}>
        {submittedTarget?.mode === 'version' ? (
          <>
            A new version of <Text weight="semibold">{saveMode.documentLabel ?? 'the document'}</Text> was saved to
            Spaarke.
          </>
        ) : (
          <>
            Your document has been saved to Spaarke and associated with{' '}
            <Text weight="semibold">{selectedEntity?.name}</Text>.
          </>
        )}
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
        {error?.offerSaveAsNew && saveMode.view === 'version' && (
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

  // Render main form
  //
  // task 040 / FR-19 audit: the `hostType === 'outlook'` reads below (label, sender, sent date,
  // attachments) are host-shaped DATA, not feature gates — by the time these props reach SaveFlow,
  // SaveView has already capability-gated WHICH VALUES got populated (canGetSender/canGetAttachments/
  // etc.), so senderEmail/senderDisplayName/sentDate/attachments are already empty on Word regardless
  // of this check. Left as-is (documented in notes/parity-checklist.md) rather than threading a
  // separate `itemType` prop through for a condition the data already enforces.
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

      {/* Filed-to card — task 026 / FR-09. A READ-BACK of what the identified document is ALREADY filed to,
          sourced from the SAME resolved identity SaveModeSection reads (no second network call, so the pane
          never shows two different answers). Deliberately OUTSIDE the `!isVersionMode` gate below: a resolved
          identity DEFAULTS to a version save (task 024), which is exactly when RelatedToPicker is hidden — the
          filed-to card is the pane's only indication of the record in that common case. It stays visually and
          functionally distinct from RelatedToPicker (an INPUT for an unfiled document) even when both render
          together for an explicit "a new document" override. The click seam (`onOpenRecord`) is task 027 /
          FR-10 (NFR-10): supplied only when `canOpenRecord` is true — absent it, the card renders as
          plain, non-interactive text (its own fallback, not a host-type branch here). */}
      <RelatedRecordCard
        {...(documentIdentity !== undefined ? { documentIdentity } : {})}
        {...(openRecordAvailable ? { onOpenRecord: handleOpenRelatedRecord } : {})}
      />

      {/* Related to + Document Details apply to a NEW document only. A version save (task 024) keeps the
          existing record's name and associations — the server never renames or re-associates on that path
          (task 023 D-5) — so these inputs would have no effect there, and are not shown. They appear as soon
          as the user chooses "A new document". */}
      {!isVersionMode && (
        <>
          {/* Related to — the RelatedToPicker renders its own header + type chips
              (UI feedback 2026-09-02); reconciliation-style auto-match cards. */}
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
              matterTypeOptions={matterTypes}
              matterTypesLoading={matterTypesLoading}
              matterTypesError={matterTypesError}
              onRetryMatterTypes={loadMatterTypes}
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
                {hostType === 'word' ? (
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
                    value={documentName}
                    onChange={(_e, data) => setDocumentName(data.value)}
                    placeholder="Enter document name"
                    disabled={isSaving}
                    aria-label="Document name"
                    rows={2}
                  />
                )}
              </div>
            </Card>
          </div>
        </>
      )}

      {/* Profile — task 021 / FR-07. Read-only AI profile (sprk_filesummary, sprk_filetldr,
          sprk_filekeywords, sprk_documenttype) for the document identity task 013 resolved, or an
          honest per-status / no-identity state. Threaded down rather than re-resolved.
          `refreshSignal` is the task 027 / FR-10 return-path re-read (see the effect above). */}
      <div className={styles.section}>
        <DocumentProfileSection
          {...(resolvedDocumentId !== undefined ? { documentId: resolvedDocumentId } : {})}
          refreshSignal={profileRefreshSignal}
        />
        {/* task 027 / FR-10: the Document-record affordance — opens the `sprk_document` record
            itself, the same browser-tab escape hatch and the SAME capability gate (NFR-10) as the
            related-record card above. Only rendered once there is a resolved document to open. */}
        {openRecordAvailable && resolvedDocumentId && (
          <Button
            appearance="subtle"
            size="small"
            icon={<OpenRegular />}
            onClick={handleOpenDocumentRecord}
            aria-label="Open this document's record in Dataverse"
          >
            Open document record
          </Button>
        )}
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

      {/* FR-11 save mode (task 024) — in the footer area, directly above Cancel / Save: a resolved
          document defaults to "a new version", with an explicit "a new document" override; every other
          identity outcome states what happens and what the user can do. Inline, not a modal (narrow pane). */}
      <SaveModeSection
        identity={documentIdentity}
        resolution={saveMode}
        onChoiceChange={handleSaveModeChange}
        {...(onRetryDocumentIdentity ? { onRetryIdentity: onRetryDocumentIdentity } : {})}
        disabled={isSaving}
      />

      {/* Footer actions — wizard pattern: Cancel (left), Save (right). Save stays disabled while the save
          mode is unsettled (identity checking, a conflict, or an undetermined identity with no choice). */}
      <div className={styles.footer}>
        <Button appearance="secondary" onClick={handleCancel} disabled={isSaving}>
          Cancel
        </Button>
        <Button appearance="primary" onClick={handleSave} disabled={isSaving || !isValid || !saveMode.target}>
          {isSaving ? 'Saving...' : isVersionMode ? 'Save version' : 'Save'}
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
      {/* React-owned ARIA live regions (task 018 / NFR-11) -- must be rendered
          by this component per useAnnounce's contract; placement doesn't
          matter visually since the regions are sr-only. */}
      {liveRegion}
      {renderContent()}
    </div>
  );
}

export default SaveFlow;
