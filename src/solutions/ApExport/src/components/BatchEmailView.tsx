/**
 * Email to AP for one batch (ebilling-r1 task 151c-1b, EB-D-181 to EB-D-184). Shown after a successful export (the
 * Email to AP button of the result step) and when the dialog is opened on an existing batch from the Billing Export form.
 *
 *   - the batch (name, invoices, total, any Sent to AP Date already held) and the AP address from the eBilling settings;
 *   - no address: the not-configured message and Download only (no CC, no message, no Send);
 *   - an address: optional CCs (up to 10) and message (up to 2000 characters), checked on the client, then Send to AP.
 *     The Send button is disabled while a send is in flight and the {@link createEmailSender} refuses a second call, so a
 *     double click sends once. The sender is owned by the caller (ExportDialog keeps one per batch), so closing the step
 *     (Back) and reopening it starts from the sender's last outcome and cannot send a second time. After the email went there is no Send at all: a 200 whose date was not recorded
 *     (`sentToApDateSet: false`, `sentToApDate: null`) says so and offers Mark Sent to AP instead;
 *   - a refusal: one plain sentence that says whether to try again or to use Download plus Mark Sent to AP.
 *
 * The page decides nothing about rights or recipients: the server does (Read and Write on the batch, the monitored
 * mailbox check, the batch owner). Fluent v9, semantic tokens only (ADR-021).
 */

import * as React from 'react';
import {
  Button,
  Field,
  Input,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Spinner,
  Text,
  Textarea,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { ArrowDownload20Regular, Mail20Regular } from '@fluentui/react-icons';
import { getXrm } from '@spaarke/ui-components';
import type { ApExportApi, ExportFormat } from '../services/apExportApi';
import { EXPORT_FORMATS } from '../services/apExportApi';
import { formatDateTime, readApAddress, type ApAddress, type BatchInfo } from '../services/batch';
import type { WebApiLike } from '../services/invoices';
import {
  AP_NOT_CONFIGURED_TEXT,
  MAX_CC,
  MAX_MESSAGE,
  RESEND_HELD_TEXT,
  RESEND_HELD_TITLE,
  RESEND_UNKNOWN_TEXT,
  allowsAnotherSend,
  createEmailSender,
  phaseOf,
  resendCaution,
  sendLabel,
  showMarkSent,
  validateEmailInput,
  withMarkedDate,
  type EmailPhase,
  type EmailSender,
} from '../services/emailFlow';
import { downloadFailureMessage } from '../services/exportFlow';
import { saveBlob } from '../services/download';
import { DialogLayout } from './DialogLayout';
import { MarkSentSection } from './MarkSentSection';

const useStyles = makeStyles({
  facts: {
    display: 'grid',
    gridTemplateColumns: 'max-content 1fr',
    columnGap: tokens.spacingHorizontalL,
    rowGap: tokens.spacingVerticalXS,
  },
  label: { color: tokens.colorNeutralForeground3 },
  actions: { display: 'flex', flexWrap: 'wrap', gap: tokens.spacingHorizontalS },
  section: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalS },
  footerStart: { display: 'flex', gap: tokens.spacingHorizontalS },
});

export interface BatchEmailViewProps {
  api: ApExportApi;
  batch: BatchInfo;
  /** True when the batch details could not be read; the dialog still works (the server decides). */
  readFailed?: boolean;
  /**
   * The sender for this batch. A caller that can close and reopen the step (ExportDialog) owns it, so its lock and last
   * outcome survive; without one the step makes its own.
   */
  sender?: EmailSender;
  /** Back to the export result (set when the step was opened from it). */
  onBack?: () => void;
  onClose: () => void;
}

export const BatchEmailView: React.FC<BatchEmailViewProps> = ({ api, batch, readFailed = false, sender: given, onBack, onClose }) => {
  const s = useStyles();
  const sender = React.useMemo(() => given ?? createEmailSender(api, batch.batchId), [given, api, batch.batchId]);
  const [address, setAddress] = React.useState<ApAddress | null>(null);
  const [phase, setPhase] = React.useState<EmailPhase>(() => phaseOf(sender.lastOutcome));
  const [ccText, setCcText] = React.useState('');
  const [message, setMessage] = React.useState('');
  const [showErrors, setShowErrors] = React.useState(false);
  const [downloading, setDownloading] = React.useState<ExportFormat | null>(null);
  const [downloadError, setDownloadError] = React.useState<string | null>(null);
  // The date Mark Sent to AP recorded in this step (formatted), so the step can show it and drop the stale warning.
  const [markedDate, setMarkedDate] = React.useState<string | null>(null);

  React.useEffect(() => {
    let cancelled = false;
    const webApi = getXrm('webApi')?.WebApi as unknown as WebApiLike | undefined;
    void readApAddress(webApi).then(result => {
      if (!cancelled) setAddress(result);
    });
    return () => {
      cancelled = true;
    };
  }, []);

  const check = React.useMemo(() => validateEmailInput(ccText, message), [ccText, message]);
  const notCompleted = batch.completed === false;
  const batchGone = phase.name === 'failed' && phase.failure.next === 'none';
  const noAddress = address?.kind === 'none';
  const canCompose =
    !notCompleted && !noAddress && (phase.name === 'compose' || phase.name === 'sending' || (phase.name === 'failed' && allowsAnotherSend(phase.failure.next)));
  const sending = phase.name === 'sending';
  const caution = resendCaution(batch.sentToApDate, readFailed);
  const sentView = phase.name === 'sent' ? withMarkedDate(phase.sent, markedDate) : null;

  const send = React.useCallback(async () => {
    setShowErrors(true);
    if (!check.ok || sender.locked) return;
    setPhase({ name: 'sending' });
    const result = await sender.send(check.request);
    if (result.kind === 'skipped') return; // a second click while the first is in flight: the first one answers
    setPhase(result.kind === 'sent' ? { name: 'sent', sent: result.sent } : { name: 'failed', failure: result.failure });
  }, [check, sender]);

  const download = React.useCallback(
    async (format: ExportFormat) => {
      setDownloading(format);
      setDownloadError(null);
      const result = await api.downloadFile(batch.batchId, format);
      setDownloading(null);
      if (result.ok) saveBlob(result.value.blob, result.value.fileName);
      else setDownloadError(downloadFailureMessage(result.failure));
    },
    [api, batch.batchId]
  );

  const footerStart = (
    <div className={s.footerStart}>
      {onBack && (
        <Button onClick={onBack} disabled={sending}>
          Back
        </Button>
      )}
      <Button onClick={onClose} disabled={sending}>
        Close
      </Button>
    </div>
  );

  const footer = canCompose ? (
    <Button
      appearance="primary"
      icon={<Mail20Regular />}
      disabled={sending || sender.locked || !address || (showErrors && !check.ok)}
      onClick={() => void send()}
      data-testid="send-to-ap"
    >
      {sendLabel(caution, phase, sending)}
    </Button>
  ) : undefined;

  return (
    <DialogLayout footerStart={footerStart} footer={footer}>
      {readFailed && (
        <MessageBar intent="warning">
          <MessageBarBody>The batch details could not be read. The server still checks every step.</MessageBarBody>
        </MessageBar>
      )}
      {caution === 'held' && canCompose && (
        <MessageBar intent="warning" data-testid="resend-caution">
          <MessageBarBody>
            <MessageBarTitle>{RESEND_HELD_TITLE}</MessageBarTitle>
            {RESEND_HELD_TEXT}
          </MessageBarBody>
        </MessageBar>
      )}
      {caution === 'unknown' && canCompose && (
        <MessageBar intent="warning" data-testid="resend-caution">
          <MessageBarBody>{RESEND_UNKNOWN_TEXT}</MessageBarBody>
        </MessageBar>
      )}

      <div className={s.facts}>
        <Text className={s.label}>Batch</Text>
        <Text>{batch.name ?? batch.batchId}</Text>
        {batch.invoiceCount !== null && (
          <>
            <Text className={s.label}>Invoices</Text>
            <Text>{batch.invoiceCount}</Text>
          </>
        )}
        {batch.totalLabel && (
          <>
            <Text className={s.label}>Total</Text>
            <Text>{batch.totalLabel}</Text>
          </>
        )}
        {(markedDate || batch.sentToApDate) && (
          <>
            <Text className={s.label}>Sent to AP Date</Text>
            <Text data-testid="batch-sent-date">{markedDate ?? formatDateTime(batch.sentToApDate)}</Text>
          </>
        )}
      </div>

      {notCompleted && (
        <MessageBar intent="warning">
          <MessageBarBody>This batch is not Completed, so it has no files to send. Nothing can be emailed or marked Sent to AP.</MessageBarBody>
        </MessageBar>
      )}

      {!notCompleted && (
        <div className={s.section}>
          {!address && <Spinner size="tiny" label="Reading the AP address…" labelPosition="after" />}
          {address?.kind === 'address' && (
            <div className={s.facts}>
              <Text className={s.label}>To (AP)</Text>
              <Text data-testid="ap-address">{address.address}</Text>
            </div>
          )}
          {address?.kind === 'unknown' && (
            <Text>The email goes to the AP address in the eBilling settings; the server checks that it is set.</Text>
          )}
          {noAddress && (
            <MessageBar intent="warning">
              <MessageBarBody>
                <MessageBarTitle>No AP email address</MessageBarTitle>
                {AP_NOT_CONFIGURED_TEXT}
              </MessageBarBody>
            </MessageBar>
          )}
        </div>
      )}

      {sentView && (
        <MessageBar intent={sentView.kind === 'not_recorded' ? 'warning' : 'success'}>
          <MessageBarBody>
            <MessageBarTitle>{sentView.title}</MessageBarTitle>
            {sentView.message}
          </MessageBarBody>
        </MessageBar>
      )}
      {sentView && (
        <div className={s.facts} data-testid="sent-details">
          <Text className={s.label}>Sent to</Text>
          <Text>{[sentView.sentTo, ...sentView.cc.map(c => `CC ${c}`)].join(', ')}</Text>
          {sentView.sentToApDate && (
            <>
              <Text className={s.label}>Sent to AP Date</Text>
              <Text>{sentView.sentToApDate}</Text>
            </>
          )}
        </div>
      )}
      {phase.name === 'failed' && (
        <MessageBar intent={phase.failure.next === 'retry' || phase.failure.next === 'edit' ? 'warning' : 'error'}>
          <MessageBarBody>
            <MessageBarTitle>Not sent</MessageBarTitle>
            {phase.failure.message}
          </MessageBarBody>
        </MessageBar>
      )}

      {canCompose && (
        <div className={s.section}>
          <Field
            label={`CC (optional, up to ${MAX_CC})`}
            hint="Email addresses separated by commas."
            validationState={showErrors && !check.ok && check.ccError ? 'error' : 'none'}
            validationMessage={showErrors && !check.ok ? check.ccError ?? undefined : undefined}
          >
            <Input value={ccText} disabled={sending} onChange={(_, d) => setCcText(d.value)} data-testid="cc-input" />
          </Field>
          <Field
            label="Message (optional)"
            hint={`${message.length} of ${MAX_MESSAGE} characters`}
            validationState={!check.ok && check.messageError ? 'error' : 'none'}
            validationMessage={!check.ok ? check.messageError ?? undefined : undefined}
          >
            <Textarea value={message} rows={4} resize="vertical" disabled={sending} onChange={(_, d) => setMessage(d.value)} data-testid="message-input" />
          </Field>
        </div>
      )}

      {!notCompleted && !batchGone && (
        <div className={s.actions}>
          {EXPORT_FORMATS.map(format => (
            <Button key={format} icon={<ArrowDownload20Regular />} disabled={downloading !== null} onClick={() => void download(format)}>
              {downloading === format ? 'Downloading…' : `Download ${format.toUpperCase()}`}
            </Button>
          ))}
        </div>
      )}
      {downloadError && (
        <MessageBar intent="error">
          <MessageBarBody>{downloadError}</MessageBarBody>
        </MessageBar>
      )}

      {!notCompleted && !batchGone && showMarkSent(!!batch.sentToApDate, phase) && (
        <MarkSentSection api={api} batchId={batch.batchId} onMarked={date => setMarkedDate(date)} />
      )}
    </DialogLayout>
  );
};
