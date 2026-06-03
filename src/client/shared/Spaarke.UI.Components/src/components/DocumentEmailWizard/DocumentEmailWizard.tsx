/**
 * DocumentEmailWizard.tsx
 *
 * Three-step wizard for emailing one or many selected documents:
 *
 *   1. Confirm Selection — Review the documents to send. Per-row deselect.
 *                          Two header toggles: "Send Document Links" and
 *                          "Attach Files". Size warning at > 25 MB.
 *   2. Summary           — TL;DR / summary preview for each kept doc.
 *   3. Compose           — Reuses the shared `SendEmailStep` (with the
 *                          combined users+contacts picker).
 *
 * On finish the wizard calls {@link sendCommunication} with:
 *   - `attachmentDocumentIds`: the `documentId` of each kept doc (only when
 *     "Attach Files" is on). See `services/communicationApi.ts` for the
 *     important note about driveItem-vs-sprk_document semantics.
 *   - Composed subject/body — links appended when "Send Document Links" is on.
 *   - `associations: [{entityType: parentEntityType, entityId: parentEntityId}]`
 *     when the caller supplied a parent.
 *   - `sendMode: 'sharedMailbox'`.
 *
 * Authentication and dataverse access are injected via props so the wizard
 * remains usable from both PCF (React 16/17 + Xrm WebApi adapter) and Code
 * Pages (React 18 + bundled MSAL).
 */
import * as React from 'react';
import {
  Button,
  Card,
  Checkbox,
  MessageBar,
  MessageBarBody,
  Spinner,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { CheckmarkCircleFilled, DismissRegular } from '@fluentui/react-icons';

import { WizardShell } from '../Wizard/WizardShell';
import type { IWizardShellHandle, IWizardStepConfig, IWizardSuccessConfig } from '../Wizard/wizardShellTypes';

import { SendEmailStep } from '../EmailStep/SendEmailStep';
import { extractEmailFromUserName } from '../EmailStep/emailHelpers';

import { searchUsersAndContacts } from '../../services/userLookup';
import { sendCommunication } from '../../services/communicationApi';
import type { ICommunicationAssociation, SendCommunicationOptions } from '../../services/communicationApi';
import type { AuthenticatedFetchFn } from '../../services/EntityCreationService';
import type { IDataService } from '../../types/serviceInterfaces';

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

/** Minimal document descriptor consumed by the wizard. */
export interface IDocumentEmailWizardItem {
  /** `sprk_document` GUID (also used as React key and selection id). */
  documentId: string;
  /** Display name (file name or document title). */
  name: string;
  /** File size in bytes. Drives the > 25 MB warning when known. */
  fileSizeBytes?: number;
  /** Long-form AI summary if already cached (rendered immediately). */
  summary?: string;
  /** Short-form "TL;DR" string (preferred over `summary` when present). */
  tldr?: string;
  /**
   * SharePoint Embedded driveId (sprk_graphdriveid). Required to run AI
   * summarization on demand via the Document Profile playbook. When omitted,
   * the Summary step falls back to the cached `summary`/`tldr` if present, or
   * "(no summary available)".
   */
  driveId?: string;
  /**
   * SharePoint Embedded itemId (sprk_graphitemid). Required alongside driveId
   * to run AI summarization on demand.
   */
  itemId?: string;
}

/** Props for {@link DocumentEmailWizard}. */
export interface IDocumentEmailWizardProps {
  /** Whether the wizard dialog is currently open. */
  open: boolean;
  /** Called when the user cancels or after a successful send. */
  onClose: () => void;
  /** The initial list of selected documents (the wizard tracks its own "kept" subset). */
  selectedDocuments: IDocumentEmailWizardItem[];
  /** Optional parent entity logical name — used for `sprk_communication` association. */
  parentEntityType?: string;
  /** Optional parent entity GUID — used for `sprk_communication` association. */
  parentEntityId?: string;
  /** Invoked after a successful send with the new `sprk_communication` GUID. */
  onSent?: (communicationId: string) => void;
  /** When `true`, hides the wizard's title bar (Dataverse already provides chrome). */
  embedded?: boolean;

  // -- Dependencies (injected) ---------------------------------------------

  /** Authenticated fetch (Bearer-attached) — required to call the BFF. */
  authenticatedFetch: AuthenticatedFetchFn;
  /** Optional BFF base URL. Omit when `authenticatedFetch` already resolves relative URLs. */
  bffBaseUrl?: string;
  /**
   * Dataverse data service used for the "To" picker (systemuser + contact).
   * When omitted, the picker remains empty (still usable for typed addresses).
   */
  dataService?: IDataService;
  /**
   * Dataverse client URL (e.g. `https://contoso.crm.dynamics.com`). Used to
   * construct deep links when "Send Document Links" is enabled. When omitted
   * the wizard falls back to `Xrm.Utility.getGlobalContext().getClientUrl()`
   * when available; otherwise links use a relative path.
   */
  clientUrl?: string;
  /**
   * Optional dialog surface `max-width` override. Threaded through to
   * {@link WizardShell}'s `maxWidth` prop. Defaults to `'95vw'`
   * (WizardShell default) when omitted — back-compat for existing
   * consumers (SemanticSearchControl pre-v1.1.63, code pages).
   *
   * Pass `'1280px'` to mirror the SemanticSearchControl FilePreviewDialog
   * footprint so the wizard sits at the same width when launched as a
   * modal-over-modal on top of the preview.
   *
   * @since v1.1.63 (SemanticSearchControl UAT polish round — match
   *   preview footprint)
   */
  maxWidth?: string;
  /**
   * Optional dialog surface `height` override. Threaded through to
   * {@link WizardShell}'s `height` prop. Defaults to `'70vh'`
   * (WizardShell default) when omitted.
   *
   * Pass `'85vh'` to mirror the FilePreviewDialog vertical footprint.
   *
   * @since v1.1.63
   */
  height?: string;
}

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Warn (but do not block) at this attachment size. */
const ATTACHMENT_WARNING_BYTES = 25 * 1024 * 1024;

/** Soft cap for inline summary preview rendering. */
const _SUMMARY_PREVIEW_CHAR_CAP = 600;

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  stepRoot: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
  },
  stepHeader: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  stepTitle: {
    color: tokens.colorNeutralForeground1,
  },
  stepSubtitle: {
    color: tokens.colorNeutralForeground3,
  },
  togglesRow: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
  },
  docList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  docRow: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalS,
    borderTopWidth: '1px',
    borderRightWidth: '1px',
    borderBottomWidth: '1px',
    borderLeftWidth: '1px',
    borderTopStyle: 'solid',
    borderRightStyle: 'solid',
    borderBottomStyle: 'solid',
    borderLeftStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
    borderRightColor: tokens.colorNeutralStroke2,
    borderBottomColor: tokens.colorNeutralStroke2,
    borderLeftColor: tokens.colorNeutralStroke2,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  docRowLeft: {
    display: 'flex',
    flexDirection: 'column',
    minWidth: 0,
    flexGrow: 1,
  },
  docName: {
    color: tokens.colorNeutralForeground1,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  docSize: {
    color: tokens.colorNeutralForeground3,
  },
  summaryCard: {
    padding: tokens.spacingHorizontalM,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  summaryHeader: {
    color: tokens.colorNeutralForeground1,
  },
  summaryBody: {
    color: tokens.colorNeutralForeground2,
    whiteSpace: 'pre-wrap',
  },
  summaryEmpty: {
    color: tokens.colorNeutralForeground3,
    fontStyle: 'italic',
  },
  totalSize: {
    color: tokens.colorNeutralForeground2,
  },
  emptyState: {
    color: tokens.colorNeutralForeground3,
    paddingTop: tokens.spacingVerticalL,
    textAlign: 'center',
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Format a byte count as KB/MB/GB. */
function formatBytes(bytes: number | undefined): string {
  if (bytes === undefined || bytes <= 0) return '';
  const units = ['B', 'KB', 'MB', 'GB'];
  let n = bytes;
  let i = 0;
  while (n >= 1024 && i < units.length - 1) {
    n /= 1024;
    i++;
  }
  return `${n.toFixed(n >= 10 || i === 0 ? 0 : 1)} ${units[i]}`;
}

/** Compute total known size of an array of doc descriptors. */
function totalBytes(docs: IDocumentEmailWizardItem[]): number {
  let total = 0;
  for (const d of docs) total += d.fileSizeBytes ?? 0;
  return total;
}

/** Best-effort Xrm client-url lookup (no-op outside Dataverse-hosted contexts). */
function resolveClientUrlSafe(): string {
  try {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const xrm = (globalThis as any).Xrm;
    const ctx = xrm?.Utility?.getGlobalContext?.();
    const url = ctx?.getClientUrl?.();
    return typeof url === 'string' ? url : '';
  } catch {
    return '';
  }
}

/** Build the deep-link to a sprk_document record. */
function buildDocumentRecordLink(clientUrl: string, documentId: string): string {
  const base = clientUrl.replace(/\/+$/, '');
  // `pagetype=entityrecord` opens the canonical record form.
  return `${base}/main.aspx?etn=sprk_document&pagetype=entityrecord&id=${encodeURIComponent(documentId)}`;
}

/**
 * Build the default subject line based on the kept-document count.
 *
 * Exported for unit-testing / callers that want to seed external state.
 */
export function buildDefaultSubject(kept: IDocumentEmailWizardItem[]): string {
  if (kept.length === 0) return '';
  if (kept.length === 1) return `Document: ${kept[0].name}`;
  return `Documents (${kept.length}) for review`;
}

/**
 * Build the default email body. Includes a short paragraph + a list of
 * document names, and optionally a list of Dataverse record links.
 *
 * Exported for unit-testing.
 */
export function buildDefaultBody(kept: IDocumentEmailWizardItem[], includeLinks: boolean, clientUrl: string): string {
  if (kept.length === 0) return '';
  const intro =
    kept.length === 1
      ? `Hi,\n\nPlease find the following document for your review:`
      : `Hi,\n\nPlease find the following documents for your review:`;
  const nameLines = kept.map(d => `  • ${d.name}`).join('\n');
  let body = `${intro}\n\n${nameLines}\n`;
  if (includeLinks) {
    const linkLines = kept.map(d => `  ${d.name}: ${buildDocumentRecordLink(clientUrl, d.documentId)}`).join('\n');
    body += `\nLinks:\n${linkLines}\n`;
  }
  body += `\nThank you,\n`;
  return body;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * Three-step wizard for emailing selected documents. See file-level doc comment
 * for the step flow and finish semantics.
 *
 * @example
 * ```tsx
 * <DocumentEmailWizard
 *   open={open}
 *   onClose={() => setOpen(false)}
 *   selectedDocuments={selectedDocs}
 *   parentEntityType="sprk_matter"
 *   parentEntityId={matterId}
 *   authenticatedFetch={authenticatedFetch}
 *   bffBaseUrl={bffBaseUrl}
 *   dataService={dataService}
 *   onSent={(id) => toast(`Communication ${id} sent.`)}
 * />
 * ```
 */
export const DocumentEmailWizard: React.FC<IDocumentEmailWizardProps> = ({
  open,
  onClose,
  selectedDocuments,
  parentEntityType,
  parentEntityId,
  onSent,
  embedded,
  authenticatedFetch,
  bffBaseUrl,
  dataService,
  clientUrl,
  // v1.1.63 — sizing pass-through (defaults left undefined so WizardShell's
  // own defaults — 95vw / 70vh — apply when consumers don't override).
  maxWidth,
  height,
}) => {
  const styles = useStyles();
  const shellRef = React.useRef<IWizardShellHandle>(null);

  // -- Kept (in-wizard) document state -----------------------------------
  const [kept, setKept] = React.useState<IDocumentEmailWizardItem[]>(() => [...selectedDocuments]);

  // -- Toggles ------------------------------------------------------------
  const [sendLinks, setSendLinks] = React.useState<boolean>(true);
  const [attachFiles, setAttachFiles] = React.useState<boolean>(true);

  // -- Email form state ---------------------------------------------------
  const [emailTo, setEmailTo] = React.useState<string>('');
  const [emailSubject, setEmailSubject] = React.useState<string>('');
  const [emailBody, setEmailBody] = React.useState<string>('');

  // Resolved client URL (lazy: try prop, then Xrm global, else '').
  const resolvedClientUrl = React.useMemo<string>(() => {
    return clientUrl ?? resolveClientUrlSafe();
  }, [clientUrl]);

  // -- Reset on (re)open --------------------------------------------------
  React.useEffect(() => {
    if (open) {
      setKept([...selectedDocuments]);
      setSendLinks(true);
      setAttachFiles(true);
      setEmailTo('');
      setEmailSubject('');
      setEmailBody('');
    }
    // We deliberately omit `selectedDocuments` from the dep array — we only
    // reset when the dialog transitions from closed → open; otherwise users
    // would lose their kept-set as the caller re-renders.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  // -- Refs to avoid stale closures inside renderContent callbacks --------
  const keptRef = React.useRef(kept);
  keptRef.current = kept;
  const sendLinksRef = React.useRef(sendLinks);
  sendLinksRef.current = sendLinks;
  const attachFilesRef = React.useRef(attachFiles);
  attachFilesRef.current = attachFiles;
  const emailToRef = React.useRef(emailTo);
  emailToRef.current = emailTo;
  const emailSubjectRef = React.useRef(emailSubject);
  emailSubjectRef.current = emailSubject;
  const emailBodyRef = React.useRef(emailBody);
  emailBodyRef.current = emailBody;

  // -- Default subject / body once compose step is reached -----------------
  // We seed on first render of the compose step rather than eagerly so the
  // user's typed input wins after they've touched a field.
  const composeSeededRef = React.useRef<boolean>(false);
  const seedComposeFields = React.useCallback(() => {
    if (composeSeededRef.current) return;
    if (!emailSubjectRef.current) {
      setEmailSubject(buildDefaultSubject(keptRef.current));
    }
    if (!emailBodyRef.current) {
      setEmailBody(buildDefaultBody(keptRef.current, sendLinksRef.current, resolvedClientUrl));
    }
    composeSeededRef.current = true;
  }, [resolvedClientUrl]);

  // -- Recipient picker bridge --------------------------------------------
  const handleSearchRecipients = React.useCallback(
    (q: string) => (dataService ? searchUsersAndContacts(dataService, q) : Promise.resolve([])),
    [dataService]
  );

  // -- Per-row deselect ----------------------------------------------------
  const deselect = React.useCallback((documentId: string) => {
    setKept(prev => prev.filter(d => d.documentId !== documentId));
  }, []);

  // -- onFinish -----------------------------------------------------------
  const handleFinish = React.useCallback(async (): Promise<IWizardSuccessConfig> => {
    const currentKept = keptRef.current;
    const currentAttach = attachFilesRef.current;
    const currentSendLinks = sendLinksRef.current;
    const subject = emailSubjectRef.current.trim();
    const body = emailBodyRef.current;
    const toRaw = emailToRef.current;

    // Recipients can be a single LookupField selection ("Name (email)") OR
    // a typed comma/semicolon-separated list. Be tolerant of both.
    const recipients = toRaw
      .split(/[;,]/)
      .map(s => s.trim())
      .map(s => extractEmailFromUserName(s) || s)
      .filter(Boolean);

    if (recipients.length === 0) {
      throw new Error('At least one recipient is required.');
    }
    if (!subject) {
      throw new Error('Subject is required.');
    }
    if (currentKept.length === 0) {
      throw new Error('At least one document must be kept in the selection.');
    }

    const associations: ICommunicationAssociation[] = [];
    if (parentEntityType && parentEntityId) {
      associations.push({ entityType: parentEntityType, entityId: parentEntityId });
    }

    // Optionally append per-document links onto the body. We do not mutate
    // `emailBody` state to avoid surprising the user if they go back and edit.
    let finalBody = body;
    if (currentSendLinks) {
      const linkLines = currentKept
        .map(d => `  ${d.name}: ${buildDocumentRecordLink(resolvedClientUrl, d.documentId)}`)
        .join('\n');
      if (linkLines && !finalBody.includes('main.aspx?etn=sprk_document')) {
        finalBody += `\n\nLinks:\n${linkLines}\n`;
      }
    }

    const opts: SendCommunicationOptions = {
      to: recipients,
      subject,
      body: finalBody,
      bodyFormat: 'text',
      attachmentDocumentIds: currentAttach ? currentKept.map(d => d.documentId) : undefined,
      associations: associations.length > 0 ? associations : undefined,
      sendMode: 'sharedMailbox',
    };

    const result = await sendCommunication(opts, { authenticatedFetch, bffBaseUrl });
    onSent?.(result.communicationId);

    return {
      icon: <CheckmarkCircleFilled fontSize={64} style={{ color: tokens.colorPaletteGreenForeground1 }} />,
      title: 'Email sent',
      body: (
        <Text size={300} style={{ color: tokens.colorNeutralForeground2 }}>
          Your message and {currentKept.length} document{currentKept.length === 1 ? '' : 's'} have been sent.
        </Text>
      ),
      actions: (
        <Button appearance="primary" onClick={onClose}>
          Close
        </Button>
      ),
    };
  }, [authenticatedFetch, bffBaseUrl, onClose, onSent, parentEntityId, parentEntityType, resolvedClientUrl]);

  // ---------------------------------------------------------------------
  // Step 1 — Confirm Selection
  // ---------------------------------------------------------------------
  const renderConfirmStep = React.useCallback(() => {
    const docs = kept;
    const total = totalBytes(docs);
    const oversized = attachFiles && total > ATTACHMENT_WARNING_BYTES;

    return (
      <div className={styles.stepRoot}>
        <div className={styles.stepHeader}>
          <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
            Confirm selection
          </Text>
          <Text size={200} className={styles.stepSubtitle}>
            Review the documents you want to email. You can remove any item with the X button.
          </Text>
        </div>

        <div className={styles.togglesRow}>
          <Checkbox
            checked={sendLinks}
            onChange={(_, data) => setSendLinks(!!data.checked)}
            label="Send document links"
          />
          <Checkbox checked={attachFiles} onChange={(_, data) => setAttachFiles(!!data.checked)} label="Attach files" />
        </div>

        {oversized && (
          <MessageBar intent="warning">
            <MessageBarBody>
              Attachments exceed 25 MB ({formatBytes(total)}) — recipients may reject. Consider links-only.
            </MessageBarBody>
          </MessageBar>
        )}

        {docs.length === 0 ? (
          <Text className={styles.emptyState}>No documents selected.</Text>
        ) : (
          <>
            <div className={styles.docList}>
              {docs.map(d => (
                <div key={d.documentId} className={styles.docRow}>
                  <div className={styles.docRowLeft}>
                    <Text size={300} weight="semibold" className={styles.docName} title={d.name}>
                      {d.name}
                    </Text>
                    <Text size={100} className={styles.docSize}>
                      {formatBytes(d.fileSizeBytes) || '—'}
                    </Text>
                  </div>
                  <Button
                    appearance="subtle"
                    size="small"
                    icon={<DismissRegular />}
                    aria-label={`Remove ${d.name}`}
                    onClick={() => deselect(d.documentId)}
                  />
                </div>
              ))}
            </div>
            <Text size={200} className={styles.totalSize}>
              {docs.length} document{docs.length === 1 ? '' : 's'} · {formatBytes(total) || 'size unknown'}
            </Text>
          </>
        )}
      </div>
    );
  }, [attachFiles, deselect, kept, sendLinks, styles]);

  // ---------------------------------------------------------------------
  // Step 2 — Combined AI Summary via the Document Profile playbook.
  // Sends ALL selected documents in a SINGLE call to /api/ai/analysis/execute
  // (which supports multi-doc when MultiDocumentEnabled is on) and renders
  // one combined analysis card. Uses the wizard's injected authenticatedFetch
  // so no separate token plumbing is needed.
  // ---------------------------------------------------------------------
  type CombinedSummaryStatus = 'idle' | 'running' | 'done' | 'error';
  const [combinedSummary, setCombinedSummary] = React.useState<string>('');
  const [summaryStatus, setSummaryStatus] = React.useState<CombinedSummaryStatus>('idle');
  const [summaryError, setSummaryError] = React.useState<string | null>(null);
  const summaryAbortRef = React.useRef<AbortController | null>(null);
  const summaryRanForRef = React.useRef<string>('');

  const runCombinedSummary = React.useCallback(async () => {
    if (kept.length === 0) return;
    const key = kept
      .map(d => d.documentId)
      .sort()
      .join(',');
    if (summaryRanForRef.current === key) return; // already ran for this selection
    summaryRanForRef.current = key;

    // Cancel any in-flight stream
    summaryAbortRef.current?.abort();
    const abort = new AbortController();
    summaryAbortRef.current = abort;

    setSummaryStatus('running');
    setSummaryError(null);
    setCombinedSummary('');

    try {
      // 1) Resolve "Summarize New File(s)" playbook ID — same playbook the
      //    Summarize Files wizard uses (BFF config key Workspace:SummarizePlaybookId,
      //    default GUID 4a72f99c-a119-f111-8343-7ced8d1dc988). This playbook is
      //    purpose-built for file summarization and returns a structured result
      //    (tldr, summary, practice areas, parties, call to action). We were
      //    previously using Document Profile, which is for individual document
      //    classification, not multi-doc combined summarization.
      const playbookUrl = `${bffBaseUrl ?? ''}/api/ai/playbooks/by-name/${encodeURIComponent('Summarize New File(s)')}`;
      const playbookRes = await authenticatedFetch(playbookUrl, {
        method: 'GET',
        headers: { Accept: 'application/json' },
        signal: abort.signal,
      });
      if (!playbookRes.ok) {
        throw new Error(`Failed to resolve Document Profile playbook (HTTP ${playbookRes.status})`);
      }
      const playbook = await playbookRes.json();
      const playbookId = playbook.playbookId || playbook.id;

      // 2) Execute analysis with ALL documents in one call
      const execUrl = `${bffBaseUrl ?? ''}/api/ai/analysis/execute`;
      const execRes = await authenticatedFetch(execUrl, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Accept: 'text/event-stream',
        },
        body: JSON.stringify({
          documentIds: kept.map(d => d.documentId),
          playbookId,
          actionId: null,
          additionalContext: null,
        }),
        signal: abort.signal,
      });
      if (!execRes.ok) {
        const errText = await execRes.text().catch(() => '');
        throw new Error(errText || `Analysis failed (HTTP ${execRes.status})`);
      }
      if (!execRes.body) {
        throw new Error('Analysis response body not readable');
      }

      // 3) Read SSE stream and accumulate the summary text
      const reader = execRes.body.getReader();
      const decoder = new TextDecoder();
      let buffer = '';
      let accumulated = '';
      while (true) {
        const { done, value } = await reader.read();
        if (done) break;
        buffer += decoder.decode(value, { stream: true });
        const events = buffer.split('\n\n');
        buffer = events.pop() ?? '';
        for (const event of events) {
          for (const line of event.split('\n')) {
            const trimmed = line.trim();
            if (!trimmed.startsWith('data:')) continue;
            const json = trimmed.slice(5).trim();
            if (!json || json === '[DONE]') continue;
            let chunk: { type?: string; content?: string; error?: string; done?: boolean } = {};
            try {
              chunk = JSON.parse(json);
            } catch {
              continue;
            }
            if (chunk.type === 'error' || chunk.error) {
              throw new Error(chunk.error ?? 'Analysis returned an error');
            }
            if (chunk.type === 'chunk' || chunk.content) {
              accumulated += chunk.content ?? '';
              setCombinedSummary(accumulated);
            }
            if (chunk.type === 'done' || chunk.done) {
              setSummaryStatus('done');
              return;
            }
          }
        }
      }
      setSummaryStatus('done');
    } catch (err) {
      if (err instanceof Error && err.name === 'AbortError') return;
      const msg = err instanceof Error ? err.message : 'Failed to generate summary';
      setSummaryError(msg);
      setSummaryStatus('error');
    }
  }, [kept, authenticatedFetch, bffBaseUrl]);

  React.useEffect(() => {
    return () => summaryAbortRef.current?.abort();
  }, []);

  const renderSummaryStep = React.useCallback(() => {
    // Trigger the combined-summary call the first time this step renders for
    // the current selection.
    void runCombinedSummary();

    const isStreaming = summaryStatus === 'running';
    const hasError = summaryStatus === 'error';

    return (
      <div className={styles.stepRoot}>
        <div className={styles.stepHeader}>
          <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
            Combined Summary
          </Text>
          <Text size={200} className={styles.stepSubtitle}>
            One combined AI analysis of {kept.length} document{kept.length === 1 ? '' : 's'} via the "Summarize New
            File(s)" playbook. This is informational only — you can compose the email body on the next step.
          </Text>
        </div>

        {kept.length === 0 ? (
          <Text className={styles.emptyState}>No documents selected.</Text>
        ) : (
          <Card className={styles.summaryCard}>
            <div
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: tokens.spacingHorizontalS,
                marginBottom: tokens.spacingVerticalXS,
              }}
            >
              <Text size={300} weight="semibold" className={styles.summaryHeader}>
                Documents: {kept.map(d => d.name).join(', ')}
              </Text>
              {isStreaming && <Spinner size="extra-tiny" aria-label="Generating summary" />}
            </div>
            {hasError && (
              <Text size={200} className={styles.summaryEmpty}>
                Could not generate summary: {summaryError}
              </Text>
            )}
            {!hasError && combinedSummary ? (
              <Text size={200} className={styles.summaryBody}>
                {combinedSummary}
              </Text>
            ) : (
              !hasError &&
              !isStreaming && (
                <Text size={200} className={styles.summaryEmpty}>
                  (no summary available)
                </Text>
              )
            )}
            {!hasError && !combinedSummary && isStreaming && (
              <Text size={200} className={styles.summaryEmpty}>
                Generating combined summary…
              </Text>
            )}
          </Card>
        )}
      </div>
    );
  }, [kept, styles, runCombinedSummary, combinedSummary, summaryStatus, summaryError]);

  // ---------------------------------------------------------------------
  // Step 3 — Compose
  // ---------------------------------------------------------------------
  const renderComposeStep = React.useCallback(() => {
    // Seed defaults the first time the user lands here. Subsequent visits
    // preserve any edits.
    seedComposeFields();

    const docCount = kept.length;
    return (
      <SendEmailStep
        title="Compose email"
        subtitle={`Sending ${docCount} document${docCount === 1 ? '' : 's'} to:`}
        emailTo={emailTo}
        onEmailToChange={setEmailTo}
        emailSubject={emailSubject}
        onEmailSubjectChange={setEmailSubject}
        emailBody={emailBody}
        onEmailBodyChange={setEmailBody}
        onSearchUsers={handleSearchRecipients}
        regardingEntityType={parentEntityType}
        regardingId={parentEntityId}
        headerContent={
          <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
            This message will be saved as a Communication record
            {parentEntityType ? ' on the parent record' : ''}.
          </Text>
        }
        infoNote="The email is sent via the Spaarke shared mailbox."
      />
    );
  }, [
    emailBody,
    emailSubject,
    emailTo,
    handleSearchRecipients,
    kept.length,
    parentEntityId,
    parentEntityType,
    seedComposeFields,
  ]);

  // -- Step configurations -----------------------------------------------
  const stepConfigs: IWizardStepConfig[] = React.useMemo(
    () => [
      {
        id: 'confirm-selection',
        label: 'Confirm Selection',
        renderContent: renderConfirmStep,
        canAdvance: () => kept.length > 0,
      },
      {
        id: 'summary',
        label: 'Summary',
        renderContent: renderSummaryStep,
        canAdvance: () => kept.length > 0,
      },
      {
        id: 'compose',
        label: 'Compose',
        renderContent: renderComposeStep,
        canAdvance: () => {
          // Require recipient + subject + non-empty body.
          return emailTo.trim() !== '' && emailSubject.trim() !== '' && emailBody.trim() !== '';
        },
      },
    ],
    [emailBody, emailSubject, emailTo, kept.length, renderComposeStep, renderConfirmStep, renderSummaryStep]
  );

  return (
    <WizardShell
      ref={shellRef}
      open={open}
      title="Email documents"
      ariaLabel="Email documents"
      steps={stepConfigs}
      onClose={onClose}
      onFinish={handleFinish}
      finishingLabel="Sending…"
      finishLabel="Send"
      embedded={embedded}
      hideTitle={embedded}
      // v1.1.63 — when omitted, WizardShell falls back to its 95vw/70vh
      // defaults; when set (e.g. SemanticSearchControl passes 1280px/85vh
      // to match the FilePreviewDialog footprint) the values pass through.
      maxWidth={maxWidth}
      height={height}
    />
  );
};

DocumentEmailWizard.displayName = 'DocumentEmailWizard';

export default DocumentEmailWizard;
