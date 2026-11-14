/**
 * DocumentEmailWizard.tsx
 *
 * Three-step wizard for emailing one or many selected documents:
 *
 *   1. Confirm Selection — Review the documents to send. Per-row deselect.
 *                          Two header toggles: "Send Document Links" and
 *                          "Attach Files". Size warning at > 25 MB.
 *   2. Summary           — TL;DR / summary preview for each kept doc.
 *   3. Compose           — The shared `EmailComposer` (compose-only, through
 *                          `SendEmailFollowOnStep`): From (My mailbox / Spaarke
 *                          shared mailbox), To, Cc, Bcc, attachments, template
 *                          picker and AI draft (spaarke-ontology-platform-r1
 *                          task 183, owner D-219). The kept documents are the
 *                          composer's attachment rows (Attach / Link per row);
 *                          the two step-1 toggles set those rows' defaults.
 *
 * On finish the wizard sends with {@link sendComposedEmail} (the composer's own
 * request path, `sendCommunication` underneath):
 *   - `attachmentDocumentIds`: the `documentId` of each attachment row that is
 *     still ticked "Attach". See `services/communicationApi.ts` for the
 *     important note about driveItem-vs-sprk_document semantics.
 *   - Composed subject/body — a "Linked documents" block is appended for the
 *     rows ticked "Link".
 *   - `associations: [{entityType: parentEntityType, entityId: parentEntityId}]`
 *     when the caller supplied a parent.
 *   - `sendMode`: the From choice; it starts on the shared mailbox, as before.
 *   - `archiveToSpe: false`, as before (a send filed to a record is archived either way).
 * A failed send keeps the wizard open on the Compose step with the composer's own
 * failure wording, so the user can fix it and send again.
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
import type { SprkModalSize } from '../SprkModal/sizes';
import type { IWizardShellHandle, IWizardStepConfig, IWizardSuccessConfig } from '../Wizard/wizardShellTypes';

import { SendEmailFollowOnStep } from '../WizardFollowOns/steps/SendEmailFollowOnStep';
import { EMPTY_COMPOSED_EMAIL, isComposedEmailReady, sendComposedEmail } from '../EmailComposer/composeOnlySend';
import type { IComposedEmail, IEmailComposeHandlers } from '../EmailComposer/composeOnlySend';
import type { IAttachmentItem } from '../EmailComposer/EmailComposer.types';

import { searchUsersAndContacts } from '../../services/userLookup';
import type { ICommunicationAssociation } from '../../services/communicationApi';
import type { AuthenticatedFetchFn } from '../../services/EntityCreationService';
import type { IDataService } from '../../types/serviceInterfaces';
import { readSseStream, parseSseEvent, describeStreamError } from '../../hooks/useSseStream';
import { getXrm } from '../../utils/xrmContext';

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
  // NOTE (unified-access-control-r2, task 070): the `driveId` / `itemId`
  // SharePoint Embedded pointers were REMOVED from this item shape. They were
  // declaration-only — no code in this component ever read them, and the
  // Summary step's `runCombinedSummary` invokes
  // `POST /api/ai/analysis/execute` with `{ documentIds }` alone. Their doc
  // comment claimed they were "required to run AI summarization" with a
  // fallback when omitted; both claims were stale (there is no gate, and the
  // DocumentRelationshipViewer host has always constructed these items
  // without the pointers). Under the broker-only decision no client receives
  // raw SPE pointers — document-id-keyed BFF routes carry the authorization
  // gate. Do NOT re-add these fields.
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
   * Host-built composer handlers (`createXrmEmailComposeHandlers({ authenticatedFetch, bffBaseUrl })` in a Dataverse host):
   * the recipient side pane, record lookup, file upload, template picker, AI draft and record-link resolver. The wizard
   * never touches Xrm (ADR-012); a handler the host leaves out hides its feature in the composer, so every host passes them.
   *
   * @since ontology task 183 (owner D-219)
   */
  emailComposeHandlers?: IEmailComposeHandlers;
  /**
   * Dataverse client URL (e.g. `https://contoso.crm.dynamics.com`). Used to
   * construct deep links when "Send Document Links" is enabled. When omitted
   * the wizard falls back to `Xrm.Utility.getGlobalContext().getClientUrl()`
   * when available; otherwise links use a relative path.
   */
  clientUrl?: string;
  /**
   * Named modal size, threaded through to {@link WizardShell}'s `size` (modal mode only).
   * Defaults to `'wizard'` when omitted. SemanticSearchControl passes `'lg'` (1280px / 85vh) to match
   * the FilePreviewDialog footprint when the wizard is stacked over an open preview.
   *
   * @since ontology task 111 (replaces the removed raw `maxWidth` / `height` strings)
   */
  size?: SprkModalSize;
}

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Warn (but do not block) at this attachment size. */
const ATTACHMENT_WARNING_BYTES = 25 * 1024 * 1024;

/** Soft cap for inline summary preview rendering. */
const _SUMMARY_PREVIEW_CHAR_CAP = 600;

/**
 * Stable playbook ID (GUID format) for the "Summarize New File(s)" playbook
 * used by the wizard's Combined Summary step. Resolved via
 * `/api/ai/playbooks/by-id/{id}` (stable, immutable `sprk_playbookid` alt-key)
 * per spec FR-03 Pattern B migration (task 021).
 *
 * Previously this wizard resolved the playbook by literal name via
 * `/api/ai/playbooks/by-name/Summarize%20New%20File(s)` — that path is being
 * retired (zero production callers expected post-migration).
 *
 * The constant lives at module scope (not in shared config) because:
 *   1. The wizard is a context-agnostic shared library component; the backend
 *      resolves its playbook via the `sprk_playbookconsumer` Binding row for
 *      consumerType 'summarize-file' (FR-P3-01 — no config fallback), and no
 *      playbook configuration is plumbed through to consumers (PCF and
 *      code-page hosts inject only auth + bffBaseUrl).
 *   2. The playbook ID is immutable per Q1 (2026-06-22) — the
 *      `sprk_playbookid` column mirrors the row's primary key and is portable
 *      across environments by convention.
 *   3. Lifting to a shared `playbookIds.ts` module would only be justified
 *      if a second wizard needed the same ID; today this is the sole consumer
 *      in this library (`useAiSummary.ts` uses a different playbook —
 *      Document Profile).
 *
 * DEV environment GUID (also the production value per existing convention):
 *   `4a72f99c-a119-f111-8343-7ced8d1dc988` — Summarize New File(s) playbook.
 */
const SUMMARIZE_NEW_FILES_PLAYBOOK_ID = '4a72f99c-a119-f111-8343-7ced8d1dc988';

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
    // Shared cross-frame walker (task 081 / C-8) — was a single-frame
    // `globalThis.Xrm` read, which missed Xrm when hosted in an iframe.
    const ctx = getXrm('clientUrl')?.Utility?.getGlobalContext?.();
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

/** Prefix of the composer attachment id of a kept document (`doc:{documentId}`); anything else the user added themselves. */
const DOCUMENT_ATTACHMENT_PREFIX = 'doc:';

const isDocumentAttachment = (a: IAttachmentItem): boolean => a.id.startsWith(DOCUMENT_ATTACHMENT_PREFIX);

/**
 * The kept documents as the composer's attachment rows. `attachFiles` / `sendLinks` (the step-1 toggles) set each row's
 * Attach / Link ticks. A row is offered a link only when the record link can be built (a client URL is known): a
 * relative link would be dead in a mail client.
 *
 * Exported for unit-testing.
 */
export function buildDocumentAttachments(
  docs: IDocumentEmailWizardItem[],
  attachFiles: boolean,
  sendLinks: boolean,
  clientUrl: string
): IAttachmentItem[] {
  return docs.map(d => {
    const linkUrl = clientUrl ? buildDocumentRecordLink(clientUrl, d.documentId) : undefined;
    return {
      id: `${DOCUMENT_ATTACHMENT_PREFIX}${d.documentId}`,
      source: 'wizard',
      fileName: d.name,
      sizeBytes: d.fileSizeBytes ?? 0,
      documentId: d.documentId,
      selected: attachFiles,
      linkSelected: sendLinks && !!linkUrl,
      linkUrl,
    };
  });
}

/** Re-applies the step-1 toggles to the kept documents' rows; rows the user added in the composer are left alone. */
function applyDocumentToggles(
  attachments: IAttachmentItem[],
  attachFiles: boolean,
  sendLinks: boolean
): IAttachmentItem[] {
  return attachments.map(a =>
    isDocumentAttachment(a) ? { ...a, selected: attachFiles, linkSelected: sendLinks && !!a.linkUrl } : a
  );
}

/**
 * The email as it starts: nothing chosen yet, the default subject and body for the kept documents (plain text, as before;
 * the composer appends the "Linked documents" block for ticked links), the kept documents as attachment rows, and the
 * shared mailbox in From (the mailbox this wizard has always sent from).
 *
 * Exported for unit-testing.
 */
export function buildInitialEmail(
  docs: IDocumentEmailWizardItem[],
  attachFiles: boolean,
  sendLinks: boolean,
  clientUrl: string
): IComposedEmail {
  return {
    ...EMPTY_COMPOSED_EMAIL,
    subject: buildDefaultSubject(docs),
    body: buildDefaultBody(docs, false, clientUrl),
    bodyFormat: 'PlainText',
    attachments: buildDocumentAttachments(docs, attachFiles, sendLinks, clientUrl),
  };
}

/** Label of the wizard's Finish button; a sign-in failure names it as the button that sends again. */
const FINISH_LABEL = 'Send';

/** Shown when the host gave the wizard no authenticated fetch: the email cannot be sent, so the step says so. */
const NO_EMAIL_SERVICE_MESSAGE =
  'This page has no connection to the Spaarke email service, so the email cannot be sent from here.';

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
  emailComposeHandlers,
  clientUrl,
  // v1.1.63 — sizing pass-through (defaults left undefined so WizardShell's
  // own defaults — 95vw / 70vh — apply when consumers don't override).
  size,
}) => {
  const styles = useStyles();
  const shellRef = React.useRef<IWizardShellHandle>(null);

  // -- Kept (in-wizard) document state -----------------------------------
  const [kept, setKept] = React.useState<IDocumentEmailWizardItem[]>(() => [...selectedDocuments]);

  // -- Toggles ------------------------------------------------------------
  // They set the Attach / Link ticks of the kept documents' rows in the composer.
  const [sendLinks, setSendLinks] = React.useState<boolean>(true);
  const [attachFiles, setAttachFiles] = React.useState<boolean>(true);

  // Resolved client URL (lazy: try prop, then Xrm global, else '').
  const resolvedClientUrl = React.useMemo<string>(() => {
    return clientUrl ?? resolveClientUrlSafe();
  }, [clientUrl]);

  // -- Email state: the composer's fields, lifted (SendEmailFollowOnStep) -----
  // Starts with the default subject/body and the kept documents as attachment rows, so a user who only picks a recipient
  // sends what the wizard always sent.
  const [email, setEmail] = React.useState<IComposedEmail>(() =>
    buildInitialEmail(selectedDocuments, true, true, clientUrl ?? resolveClientUrlSafe())
  );
  // The subject/body the wizard itself wrote last: while the email still holds them, the user has not written their own,
  // and a change to the kept documents rewrites them.
  const seededRef = React.useRef<{ subject: string; body: string }>({
    subject: email.subject,
    body: email.body,
  });

  // -- Reset on (re)open --------------------------------------------------
  React.useEffect(() => {
    if (open) {
      const initial = buildInitialEmail(selectedDocuments, true, true, resolvedClientUrl);
      setKept([...selectedDocuments]);
      setSendLinks(true);
      setAttachFiles(true);
      setEmail(initial);
      seededRef.current = { subject: initial.subject, body: initial.body };
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
  const emailRef = React.useRef(email);
  emailRef.current = email;

  // -- Recipient picker bridge --------------------------------------------
  const handleSearchRecipients = React.useCallback(
    (q: string) => (dataService ? searchUsersAndContacts(dataService, q) : Promise.resolve([])),
    [dataService]
  );

  // -- Per-row deselect ----------------------------------------------------
  // The removed document leaves the composer's attachment rows too, and the subject/body the wizard wrote are rewritten
  // for the documents that remain (unless the user wrote their own).
  const deselect = React.useCallback(
    (documentId: string) => {
      const remaining = keptRef.current.filter(d => d.documentId !== documentId);
      const current = emailRef.current;
      const seeded = seededRef.current;
      const defaults = buildInitialEmail(remaining, attachFilesRef.current, sendLinksRef.current, resolvedClientUrl);
      const rewriteSubject = current.subject === seeded.subject;
      const rewriteBody = current.body === seeded.body;
      seededRef.current = {
        subject: rewriteSubject ? defaults.subject : seeded.subject,
        body: rewriteBody ? defaults.body : seeded.body,
      };
      setKept(remaining);
      setEmail({
        ...current,
        subject: rewriteSubject ? defaults.subject : current.subject,
        // The wizard rewrote the body: plain text, whatever the composer last held.
        ...(rewriteBody ? { body: defaults.body, bodyFormat: 'PlainText' as const } : {}),
        attachments: current.attachments.filter(a => a.id !== `${DOCUMENT_ATTACHMENT_PREFIX}${documentId}`),
      });
    },
    [resolvedClientUrl]
  );

  const handleSendLinksChange = React.useCallback((next: boolean) => {
    setSendLinks(next);
    setEmail(prev => ({ ...prev, attachments: applyDocumentToggles(prev.attachments, attachFilesRef.current, next) }));
  }, []);

  const handleAttachFilesChange = React.useCallback((next: boolean) => {
    setAttachFiles(next);
    setEmail(prev => ({ ...prev, attachments: applyDocumentToggles(prev.attachments, next, sendLinksRef.current) }));
  }, []);

  // True when the host gave the wizard a way to reach the BFF; without it the email cannot be sent.
  const canSend = typeof authenticatedFetch === 'function';

  // -- onFinish -----------------------------------------------------------
  const handleFinish = React.useCallback(async (): Promise<IWizardSuccessConfig> => {
    const currentKept = keptRef.current;
    const current = emailRef.current;

    if (!canSend) {
      throw new Error(NO_EMAIL_SERVICE_MESSAGE);
    }
    if (current.to.length === 0) {
      throw new Error('At least one recipient is required.');
    }
    if (!current.subject.trim()) {
      throw new Error('Subject is required.');
    }
    if (currentKept.length === 0) {
      throw new Error('At least one document must be kept in the selection.');
    }
    if (!isComposedEmailReady(current)) {
      throw new Error(
        'The email is not ready to send: check the recipient addresses and the body, and wait for uploads to finish.'
      );
    }

    const associations: ICommunicationAssociation[] = [];
    if (parentEntityType && parentEntityId) {
      associations.push({ entityType: parentEntityType, entityId: parentEntityId });
    }

    // The composer's own request path: ticked links are resolved to Spaarke record links and appended to the body, ticked
    // documents travel as `attachmentDocumentIds`, From is `sendMode`. It never throws: a failure comes back as the
    // composer's wording (an unconfirmed send says the email "may have been sent"), which the shell shows on this step
    // with the composer still filled in.
    const result = await sendComposedEmail(current, {
      associations,
      authenticatedFetch,
      bffBaseUrl,
      onResolveShareLink: emailComposeHandlers?.onResolveShareLink,
      // As before: no archive request of its own (a send filed to a record is archived either way).
      archiveToSpe: false,
      // The composer stays open after a failed send; the wizard's own Finish button sends it again.
      retryLabel: FINISH_LABEL,
    });
    if (!result.success) {
      throw new Error(result.warning ?? 'The email was not sent.');
    }
    if (result.communicationId) onSent?.(result.communicationId);

    const attachedCount = current.attachments.filter(a => a.selected !== false && a.documentId).length;
    return {
      icon: <CheckmarkCircleFilled fontSize={64} style={{ color: tokens.colorPaletteGreenForeground1 }} />,
      title: 'Email sent',
      body: (
        <Text size={300} style={{ color: tokens.colorNeutralForeground2 }}>
          {attachedCount > 0
            ? `Your message and ${attachedCount} document${attachedCount === 1 ? '' : 's'} have been sent.`
            : 'Your message has been sent.'}
        </Text>
      ),
      actions: (
        <Button appearance="primary" onClick={onClose}>
          Close
        </Button>
      ),
    };
  }, [
    authenticatedFetch,
    bffBaseUrl,
    canSend,
    emailComposeHandlers,
    onClose,
    onSent,
    parentEntityId,
    parentEntityType,
  ]);

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
            onChange={(_, data) => handleSendLinksChange(!!data.checked)}
            label="Send document links"
          />
          <Checkbox
            checked={attachFiles}
            onChange={(_, data) => handleAttachFilesChange(!!data.checked)}
            label="Attach files"
          />
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
  }, [attachFiles, deselect, handleAttachFilesChange, handleSendLinksChange, kept, sendLinks, styles]);

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
      // 1) Resolve "Summarize New File(s)" playbook ID via stable-ID lookup.
      //    Spec FR-03 Pattern B migration (task 021): replaces the prior
      //    by-name resolution (`/by-name/Summarize%20New%20File(s)`) with a
      //    stable-ID call (`/by-id/{id}`) keyed on the immutable
      //    `sprk_playbookid` alt-key. Same playbook the Summarize Files wizard
      //    uses; purpose-built for file summarization with a structured result
      //    (tldr, summary, practice areas, parties, call to action). Document
      //    Profile is for individual document classification — not used here.
      const playbookUrl = `${bffBaseUrl ?? ''}/api/ai/playbooks/by-id/${encodeURIComponent(SUMMARIZE_NEW_FILES_PLAYBOOK_ID)}`;
      const playbookRes = await authenticatedFetch(playbookUrl, {
        method: 'GET',
        headers: { Accept: 'application/json' },
        signal: abort.signal,
      });
      if (!playbookRes.ok) {
        // Per ADR-019, the BFF returns RFC 7807 ProblemDetails for 404
        // (`type: https://spaarke.com/problems/playbook-not-found`). Surface
        // a user-friendly message — ADR-015: we log only the HTTP status, not
        // the response body or playbook ID, to keep client telemetry clean.
        if (playbookRes.status === 404) {
          throw new Error('Playbook unavailable. Please contact your administrator.');
        }
        throw new Error('Playbook unavailable. Please contact your administrator.');
      }
      const playbook = await playbookRes.json();
      const playbookId = playbook.playbookId || playbook.id;

      // 2) + 3) Execute analysis with ALL documents in one call and consume the
      // SSE stream. task 045 (FR-P3-06, NFR-08): the canonical readSseStream +
      // parseSseEvent own the fetch/reader/buffer/`data:`-parse machinery; the
      // wizard's authenticatedFetch is supplied as fetchImpl (it owns auth per
      // ADR-028). Per-chunk handling preserved verbatim.
      const execUrl = `${bffBaseUrl ?? ''}/api/ai/analysis/execute`;
      let accumulated = '';
      // Set once the terminal done chunk is seen — subsequent lines are skipped
      // (pre-consolidation behavior returned out of the read loop).
      let finished = false;
      await readSseStream({
        url: execUrl,
        body: {
          documentIds: kept.map(d => d.documentId),
          playbookId,
          actionId: null,
          additionalContext: null,
        },
        fetchImpl: (url, init) =>
          authenticatedFetch(url, {
            ...init,
            headers: {
              ...(init.headers as Record<string, string>),
              Accept: 'text/event-stream',
            },
          }),
        signal: abort.signal,
        mapHttpError: async res => {
          const errText = await res.text().catch(() => '');
          return new Error(errText || `Analysis failed (HTTP ${res.status})`);
        },
        onLine: (line: string) => {
          if (finished) return;
          // Server chunks always carry a string `type` (AnalysisStreamChunk);
          // the `data: [DONE]` terminator parses to null and is skipped.
          const chunk = parseSseEvent(line) as unknown as {
            type?: string;
            content?: string;
            error?: string;
            correlationId?: string;
            done?: boolean;
          } | null;
          if (!chunk) return;
          if (chunk.type === 'error' || chunk.error) {
            throw new Error(describeStreamError(chunk, 'Analysis returned an error'));
          }
          if (chunk.type === 'chunk' || chunk.content) {
            accumulated += chunk.content ?? '';
            setCombinedSummary(accumulated);
          }
          if (chunk.type === 'done' || chunk.done) {
            finished = true;
            setSummaryStatus('done');
          }
        },
      });
      if (!finished) {
        setSummaryStatus('done');
      }
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
    const docCount = kept.length;
    return (
      <SendEmailFollowOnStep
        title="Compose email"
        subtitle={`Sending ${docCount} document${docCount === 1 ? '' : 's'}. The email is sent when you finish, from the mailbox chosen in From${
          parentEntityType ? ', and is saved as a Communication record on the parent record' : ''
        }.`}
        value={email}
        onChange={setEmail}
        composeHandlers={emailComposeHandlers}
        onSearchRecipients={handleSearchRecipients}
        headerContent={
          canSend ? undefined : (
            <MessageBar intent="warning" data-testid="email-service-unavailable">
              <MessageBarBody>{NO_EMAIL_SERVICE_MESSAGE}</MessageBarBody>
            </MessageBar>
          )
        }
      />
    );
  }, [canSend, email, emailComposeHandlers, handleSearchRecipients, kept.length, parentEntityType]);

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
        // A recipient, a subject, a body, valid addresses and every attachment uploaded (uploads still running keep
        // Finish off); without a way to reach the email service nothing can be sent.
        canAdvance: () => canSend && kept.length > 0 && isComposedEmailReady(email),
      },
    ],
    [canSend, email, kept.length, renderComposeStep, renderConfirmStep, renderSummaryStep]
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
      finishLabel={FINISH_LABEL}
      embedded={embedded}
      hideTitle={embedded}
      // Named size; when omitted WizardShell uses its `wizard` default.
      size={size}
    />
  );
};

DocumentEmailWizard.displayName = 'DocumentEmailWizard';

export default DocumentEmailWizard;
