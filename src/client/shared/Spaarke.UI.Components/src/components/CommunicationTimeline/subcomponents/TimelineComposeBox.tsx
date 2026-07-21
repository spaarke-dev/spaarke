/**
 * TimelineComposeBox.tsx
 *
 * The timeline's compose/send box (task 060, FR-10, §11). REUSES
 * `<EmailComposer/>`'s `RecipientField` / `BodyEditor` / `AttachmentList`
 * sub-components rather than re-implementing recipient normalization,
 * rich-text/plain-text authoring, or attachment caps — those carry over
 * unmodified (150 count / 35 MB total / 25 MB warn, `EmailComposer.reducer.ts`
 * `ATTACHMENT_MAX_*` constants).
 *
 * State is local `useState` (not a `useReducer` engine like `<EmailComposer/>`)
 * — this box is a small, single-purpose reply form, not a multi-mode
 * compose/view/reply/forward/draft engine; a full reducer would be scope
 * creep here (root CLAUDE.md §11).
 *
 * `local`-sourced attachments without a resolved `documentId` (no
 * upload-to-Document callback is wired in this task, same documented scope
 * boundary as `<EmailComposer/>`'s `AttachmentList`) are tracked for display
 * but excluded from the outbound `attachmentDocumentIds` payload — this
 * mirrors `EmailComposer.tsx`'s `mapStateToSendRequest` filter, not a bug.
 *
 * Task 060 (FR-10) — Subject + structured To/Cc/Bcc. Subject is now a
 * required field (mirrors `<EmailComposer/>`'s own `Field label="Subject"
 * required`) so this form can never hand the send path an empty subject that
 * falls back to the server default "(No Subject)"
 * (`CommunicationService.cs`/`ThreadResolver.cs`). Cc/Bcc reuse `RecipientField`
 * exactly like `<EmailComposer/>` renders them (always visible, not
 * collapsible — one canonical recipient-input layout, not a second one).
 * Resolved recipients (directory match via `onSearchRecipients`) carry
 * `IRecipient.entityType` ('contact'/'systemuser') through to the caller;
 * free-text (unresolved) recipients are still accepted — the participant
 * index (task 050) resolves each address server-side via
 * `QueryContactByEmailAsync`, so sending the exact directory-resolved email
 * (rather than a hand-typed one that may not match) is what lets a resolved
 * pick land as a typed junction row instead of an unresolved-address row.
 */
import * as React from 'react';
import { Button, Field, Input, Spinner, Text, makeStyles, tokens } from '@fluentui/react-components';
import { RecipientField, BodyEditor, AttachmentList } from '../../EmailComposer';
import type {
  IRecipient,
  IAttachmentItem,
  EmailComposerBodyFormat,
  IComposerAttachmentSource,
} from '../../EmailComposer';
import type { ILookupItem } from '../../../types/LookupTypes';
import type { CommunicationTimelinePrefill } from '../CommunicationTimeline.types';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ITimelineSendPayload {
  to: string[];
  /** Cc recipient emails (task 060, FR-10). Omitted/undefined when empty — mirrors `sendCommunication()`'s optional `cc`. */
  cc?: string[];
  /** Bcc recipient emails (task 060, FR-10). Omitted/undefined when empty — mirrors `sendCommunication()`'s optional `bcc`. */
  bcc?: string[];
  /**
   * Subject/topic (task 060, FR-10) — required (never empty at this point;
   * `handleSend` blocks the call otherwise). Populates `sprk_subject` and
   * drives the server-derived `sprk_name` ("Message: {subject}"/"Email:
   * {subject}") — the meaningful name source for CC-1 thread naming.
   */
  subject: string;
  body: string;
  bodyFormat: EmailComposerBodyFormat;
  attachmentDocumentIds: string[];
}

export interface ITimelineComposeBoxProps {
  /** Reply-to seed (e.g. the thread's most recent sender), overridden by `prefill.to` when present. */
  defaultTo?: string[];
  /** Prop-driven prefill (task 063 quoting seam) — updates the live draft without re-mounting. */
  prefill?: CommunicationTimelinePrefill;
  isSending: boolean;
  onSend: (payload: ITimelineSendPayload) => Promise<void>;
  onSearchRecipients?: (query: string) => Promise<ILookupItem[]>;
  disabled?: boolean;
}

const ATTACHMENT_SOURCES: IComposerAttachmentSource[] = [{ kind: 'local' }];

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  wrapper: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderTopWidth: tokens.strokeWidthThin,
    borderTopStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  actionsRow: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'flex-end',
    gap: tokens.spacingHorizontalS,
  },
  errorText: {
    color: tokens.colorPaletteRedForeground1,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

function toRecipients(emails: string[] | undefined): IRecipient[] {
  return (emails ?? []).filter(e => e && e.trim().length > 0).map(e => ({ email: e.trim(), resolved: false }));
}

export const TimelineComposeBox: React.FC<ITimelineComposeBoxProps> = ({
  defaultTo,
  prefill,
  isSending,
  onSend,
  onSearchRecipients,
  disabled,
}) => {
  const styles = useStyles();

  const [to, setTo] = React.useState<IRecipient[]>(() => toRecipients(prefill?.to ?? defaultTo));
  const [cc, setCc] = React.useState<IRecipient[]>([]);
  const [bcc, setBcc] = React.useState<IRecipient[]>([]);
  const [subject, setSubject] = React.useState(prefill?.subject ?? '');
  const [body, setBody] = React.useState(prefill?.body ?? '');
  const [bodyFormat, setBodyFormat] = React.useState<EmailComposerBodyFormat>(prefill?.bodyFormat ?? 'HTML');
  const [attachments, setAttachments] = React.useState<IAttachmentItem[]>([]);
  const [error, setError] = React.useState<string | undefined>();

  // Re-seed the draft when the host pushes a new prefill (task 063 quoting) — without unmounting.
  const prefillSubject = prefill?.subject;
  const prefillBody = prefill?.body;
  const prefillFormat = prefill?.bodyFormat;
  const prefillToKey = prefill?.to?.join(',');
  React.useEffect(() => {
    if (prefillSubject !== undefined) setSubject(prefillSubject);
    if (prefillBody !== undefined) setBody(prefillBody);
    if (prefillFormat !== undefined) setBodyFormat(prefillFormat);
    if (prefillToKey !== undefined) setTo(toRecipients(prefill?.to));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [prefillSubject, prefillBody, prefillFormat, prefillToKey]);

  const defaultToKey = defaultTo?.join(',');
  React.useEffect(() => {
    // Only apply the heuristic reply-to seed when the host hasn't already supplied an explicit prefill.
    if (prefill?.to !== undefined) return;
    if (defaultTo && defaultTo.length > 0) setTo(toRecipients(defaultTo));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [defaultToKey]);

  const canSend = to.length > 0 && subject.trim().length > 0 && body.trim().length > 0 && !isSending && !disabled;

  const handleSend = React.useCallback(async () => {
    if (to.length === 0) {
      setError('At least one recipient is required.');
      return;
    }
    if (!subject.trim()) {
      setError('Subject is required.');
      return;
    }
    if (!body.trim()) {
      setError('Message body is required.');
      return;
    }
    setError(undefined);

    const attachmentDocumentIds = attachments
      .filter((a): a is IAttachmentItem & { documentId: string } => Boolean(a.documentId))
      .map(a => a.documentId);

    try {
      await onSend({
        to: to.map(r => r.email),
        cc: cc.length > 0 ? cc.map(r => r.email) : undefined,
        bcc: bcc.length > 0 ? bcc.map(r => r.email) : undefined,
        subject: subject.trim(),
        body,
        bodyFormat,
        attachmentDocumentIds,
      });
      setBody('');
      setAttachments([]);
      // `to`/`cc`/`bcc`/`subject` are intentionally NOT cleared after send —
      // mirrors the existing `to` behavior (a reply box keeps its recipients
      // and topic for the next message in the same conversation).
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to send message.');
    }
  }, [to, cc, bcc, subject, body, bodyFormat, attachments, onSend]);

  return (
    <div className={styles.wrapper} role="region" aria-label="Compose message">
      <div role="region" aria-label="Recipients">
        <RecipientField
          label="To"
          required
          value={to}
          onChange={setTo}
          onSearch={onSearchRecipients}
          disabled={disabled || isSending}
        />
        <RecipientField label="Cc" value={cc} onChange={setCc} onSearch={onSearchRecipients} disabled={disabled || isSending} />
        <RecipientField
          label="Bcc"
          value={bcc}
          onChange={setBcc}
          onSearch={onSearchRecipients}
          disabled={disabled || isSending}
        />
      </div>
      <Field label="Subject" required>
        <Input
          value={subject}
          onChange={e => setSubject(e.target.value)}
          placeholder="Subject"
          disabled={disabled || isSending}
        />
      </Field>
      <BodyEditor
        value={body}
        format={bodyFormat}
        onChange={setBody}
        onFormatChange={setBodyFormat}
        readOnly={disabled || isSending}
        minHeight={120}
      />
      <AttachmentList
        mode="compose"
        sources={ATTACHMENT_SOURCES}
        items={attachments}
        onAdd={item => setAttachments(prev => [...prev, item])}
        onRemove={id => setAttachments(prev => prev.filter(a => a.id !== id))}
        onToggleSelected={id =>
          setAttachments(prev => prev.map(a => (a.id === id ? { ...a, selected: a.selected === false } : a)))
        }
        readOnly={disabled || isSending}
      />
      {error && (
        <Text role="alert" className={styles.errorText}>
          {error}
        </Text>
      )}
      <div className={styles.actionsRow}>
        {isSending && <Spinner size="tiny" label="Sending..." />}
        <Button appearance="primary" onClick={() => void handleSend()} disabled={!canSend}>
          Send
        </Button>
      </div>
    </div>
  );
};

TimelineComposeBox.displayName = 'TimelineComposeBox';
