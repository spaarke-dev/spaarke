/**
 * SendEmailFollowOnStep.tsx
 * The create wizards' Send Email follow-on step: the shared `EmailComposer`, compose-only (spaarke-ontology-platform-r1
 * task 182, owner D-219; UAT round 4 finding F-R4-3).
 *
 * The step offers what the Send Email dialog offers — From (My mailbox / Spaarke shared mailbox), To, Cc, Bcc, attachments,
 * the template picker and AI drafting — but has no Send button: the record the email is filed under does not exist yet, so
 * the wizard sends it at Finish (`sendComposedEmail`, the composer's own request path) with the created record as the
 * association.
 *
 * - State is lifted: every composer change reaches the wizard through `onChange` as an `IComposedEmail`. The wizard shell
 *   unmounts the step on Next, so the composer is mounted from `value` each time the step opens (Back then Next restores
 *   From, To, Cc, Bcc, subject, body and attachments).
 * - When the host itself replaces `value` while the step is open (the Summarize Files "short summary" toggle rewrites the
 *   body; the Work Assignment wizard adds the user to Cc when their address arrives), the composer restarts from the new
 *   value, so the host's change is shown and never overwritten by the composer's older state.
 * - The composer itself never sends: it gets a fetch that refuses. The only network calls while composing are the host
 *   handlers the user invokes (recipient pane, record lookup, file upload, template list/render, AI draft).
 * - The composer opens in rich text, where it shows the template picker, AI draft and attach tools (as the Send Email
 *   dialog does). A plain-text value (the wizards' pre-filled bodies) is opened as paragraphs; that conversion is not an
 *   edit: the value keeps the plain-text body until the user changes its text. A rich-text body is sent as HTML, whose
 *   paragraphs keep the line breaks.
 * - `headerContent` renders above the composer (the Summarize Files wizard's "short summary" toggle).
 *
 * Fluent v9 tokens only (ADR-021). No Xrm: the host injects the compose handlers (ADR-012).
 *
 * @see EmailComposer/composeOnlySend.ts — IComposedEmail, isComposedEmailReady, sendComposedEmail
 * @see DecisionWizard/steps/EmailMessageFields.tsx — the other compose-only mount
 */
import * as React from 'react';
import { MessageBar, MessageBarBody, Text, makeStyles, tokens } from '@fluentui/react-components';
import { EmailComposer } from '../../EmailComposer/EmailComposer';
import { composedEmailFromState, pendingAttachments } from '../../EmailComposer/composeOnlySend';
import { plainTextToHtml, toPlainText } from '../../EmailComposer/composeOnlyText';
import type { IComposedEmail, IEmailComposeHandlers } from '../../EmailComposer/composeOnlySend';
import type { EmailComposerState, IAttachmentItem } from '../../EmailComposer/EmailComposer.types';
import type { AuthenticatedFetchFn } from '../../../services/EntityCreationService';
import type { ILookupItem } from '../../../types/LookupTypes';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ISendEmailFollowOnStepProps {
  /** The email composed so far (wizard state). The composer starts from it each time the step opens. */
  value: IComposedEmail;
  /** Called with the whole composed email whenever the user changes it. */
  onChange: (next: IComposedEmail) => void;
  /**
   * Host-built composer handlers (`createXrmEmailComposeHandlers` in a Dataverse host): recipient side pane, record lookup,
   * file upload, template picker, AI draft. A missing handler hides its feature.
   */
  composeHandlers?: IEmailComposeHandlers;
  /** Type-ahead for To / Cc / Bcc (the wizard's user search). */
  onSearchRecipients?: (query: string) => Promise<ILookupItem[]>;
  /** Step title. Defaults to "Send Notification Email". */
  title?: string;
  /** Step subtitle. A default that says when and how the email is sent is used when omitted. */
  subtitle?: string;
  /** Extra content above the composer, e.g. a "short summary" toggle. */
  headerContent?: React.ReactNode;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalM,
  },
  headerText: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXS,
  },
  stepTitle: {
    color: tokens.colorNeutralForeground1,
  },
  stepSubtitle: {
    color: tokens.colorNeutralForeground3,
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

export const DEFAULT_SEND_EMAIL_SUBTITLE =
  'The email is sent when you finish the wizard, from the mailbox chosen in From, and is filed under the new record.';

/** The composer never sends from this step; a fetch that refuses makes that certain. */
const refuseFetch = (() =>
  Promise.reject(
    new Error('The Send Email step never sends from the composer; the wizard sends at Finish.')
  )) as unknown as AuthenticatedFetchFn;

/** What identifies a change worth lifting (the File of a local pick is carried, not compared). */
function signature(email: IComposedEmail): string {
  const addresses = (list: IComposedEmail['to']) => list.map(r => r.email);
  return JSON.stringify([
    addresses(email.to),
    addresses(email.cc),
    addresses(email.bcc),
    email.subject,
    email.body,
    email.bodyFormat,
    email.sendMode,
    email.fromMailbox ?? '',
    email.attachments.map(a => [
      a.id,
      a.documentId ?? '',
      a.selected !== false,
      a.linkSelected === true,
      a.linkUrl ?? '',
    ]),
  ]);
}

/**
 * A plain-text body opened as rich-text paragraphs is still that plain text while its text is unchanged (the rich-text
 * editor may re-serialise the paragraphs; that is not an edit), so the composer's state keeps the plain-text body.
 */
function keepPlainBody(next: IComposedEmail, previous: IComposedEmail): IComposedEmail {
  if (previous.bodyFormat !== 'PlainText' || next.bodyFormat !== 'HTML') return next;
  const sameText = toPlainText(next.body, 'HTML') === toPlainText(plainTextToHtml(previous.body), 'HTML');
  return sameText ? { ...next, body: previous.body, bodyFormat: 'PlainText' } : next;
}

/** Restored attachments keep their inclusion: the composer reads an unset `selected` on a compose as "not attached". */
function restoredAttachments(items: readonly IAttachmentItem[]): IAttachmentItem[] {
  return items.map(a => ({ ...a, selected: a.selected !== false, linkSelected: a.linkSelected === true }));
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const SendEmailFollowOnStep: React.FC<ISendEmailFollowOnStepProps> = ({
  value,
  onChange,
  composeHandlers,
  onSearchRecipients,
  title = 'Send Notification Email',
  subtitle = DEFAULT_SEND_EMAIL_SUBTITLE,
  headerContent,
}) => {
  const styles = useStyles();

  // The composer is uncontrolled: it starts from the value as it is when the step opens. `shown` is what the composer
  // holds; a `value` that differs from it came from the host, so the composer restarts (new `generation`) from it.
  const shown = React.useRef(value);
  const [generation, setGeneration] = React.useState(0);
  if (value !== shown.current && signature(value) !== signature(shown.current)) {
    shown.current = value;
    setGeneration(g => g + 1);
  }
  const initial = React.useMemo(
    () => ({
      to: value.to.map(r => r.email),
      cc: value.cc.map(r => r.email),
      bcc: value.bcc.map(r => r.email),
      subject: value.subject,
      body: value.bodyFormat === 'PlainText' ? plainTextToHtml(value.body) : value.body,
      bodyFormat: 'HTML' as const,
      sendMode: value.sendMode,
      fromMailbox: value.fromMailbox,
      attachments: restoredAttachments(value.attachments),
    }),
    // Recomputed only when the composer (re)starts; between restarts the composer owns the fields.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [generation]
  );

  const onChangeRef = React.useRef(onChange);
  onChangeRef.current = onChange;

  const onStateChange = React.useCallback((state: EmailComposerState) => {
    const next = keepPlainBody(composedEmailFromState(state), shown.current);
    // Only a real change is lifted, so mounting the composer over the starting values changes nothing.
    if (signature(next) === signature(shown.current)) return;
    shown.current = next;
    onChangeRef.current(next);
  }, []);

  const pending = pendingAttachments(value);
  const handlers = composeHandlers ?? {};

  return (
    <div className={styles.root} data-testid="send-email-followon-step">
      <div className={styles.headerText}>
        <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
          {title}
        </Text>
        <Text size={200} className={styles.stepSubtitle}>
          {subtitle}
        </Text>
      </div>
      {headerContent}
      <EmailComposer
        key={generation}
        mode="compose"
        mount="inline"
        hostOwnsChrome
        composeOnly
        composeOnlyShowsFrom
        authenticatedFetch={refuseFetch}
        initialTo={initial.to}
        initialCc={initial.cc}
        initialBcc={initial.bcc}
        initialSubject={initial.subject}
        initialBody={initial.body}
        initialBodyFormat={initial.bodyFormat}
        defaultSendMode={initial.sendMode}
        fromMailbox={initial.fromMailbox}
        initialAttachments={initial.attachments}
        showAssociations={false}
        onStateChange={onStateChange}
        onSearchRecipients={onSearchRecipients}
        onLookupRecipients={handlers.onLookupRecipients}
        recordLookupCatalog={handlers.recordLookupCatalog}
        onLookupRecord={handlers.onLookupRecord}
        onUploadLocalAttachment={handlers.onUploadLocalAttachment}
        onResolveShareLink={handlers.onResolveShareLink}
        onListEmailTemplates={handlers.onListEmailTemplates}
        onRenderEmailTemplate={handlers.onRenderEmailTemplate}
        onDraftWithAi={handlers.onDraftWithAi}
      />
      {pending.length > 0 ? (
        <MessageBar intent="warning" data-testid="attachments-pending">
          <MessageBarBody>
            {pending.map(a => a.fileName).join(', ')} {pending.length === 1 ? 'is' : 'are'} not uploaded yet. Next stays
            off until every attachment is uploaded; remove any that cannot be.
          </MessageBarBody>
        </MessageBar>
      ) : null}
    </div>
  );
};

SendEmailFollowOnStep.displayName = 'SendEmailFollowOnStep';
