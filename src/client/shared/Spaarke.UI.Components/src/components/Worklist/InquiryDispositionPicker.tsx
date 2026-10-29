/**
 * InquiryDispositionPicker - the D-111 picker on an inquiry-outcome To Do: the reply the person read, and the four-way outcome they
 * choose. Confirming posts `{ replyCommunicationId, disposition }` through the worklist service (the BFF; no Dataverse, D-15).
 *
 * A person chooses the outcome: it starts unselected and nothing infers it (ADR-013). The newest reply starts selected (the route
 * returns them newest first). While the outcome is not recorded the To Do stays open: a failure is said in plain words in the dialog
 * and the person can choose again or cancel. It is a part of the widget (like WorklistDisclosures), not a shared component: the
 * dialog is the `FormModal` preset (MODAL-DECISION-CRITERIA: a light-edit form).
 *
 * Task: spaarke-ontology-platform-r1, task 155. Fluent v9 tokens only (ADR-021).
 */

import * as React from 'react';
import {
  Button,
  Caption1,
  Field,
  MessageBar,
  MessageBarBody,
  Radio,
  RadioGroup,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { FormModal } from '../SprkModal/presets/FormModal';
import { DISPOSITION_OPTIONS, dispositionFailureMessage, isAlreadyResolved } from './inquiryDisposition';
import type { InquiryForTodo, RecordDispositionResult, WorklistService } from './worklistService';
import { formatShortDate } from './worklistWizardItem';

export interface InquiryDispositionPickerProps {
  /** The inquiry and its replies, from `findInquiryForTodo`. */
  inquiry: InquiryForTodo;
  /** The To Do's subject name, shown under the title. */
  subjectName?: string | null;
  service: Pick<WorklistService, 'recordDisposition'>;
  /** The outcome was recorded (200). The host closes the picker and reads the lanes again. */
  onRecorded: (result: RecordDispositionResult) => void;
  /** The inquiry already has an outcome (409 `inquiry.already_resolved`). The host closes the picker and reads the lanes again. */
  onAlreadyResolved: () => void;
  /** The person left without recording. The To Do stays open. */
  onCancel: () => void;
  /** Open the decision wizard on the To Do instead (its other actions). */
  onOpenWizard: () => void;
}

const useStyles = makeStyles({
  body: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalL },
  meta: { color: tokens.colorNeutralForeground3 },
});

export const InquiryDispositionPicker: React.FC<InquiryDispositionPickerProps> = ({
  inquiry,
  subjectName,
  service,
  onRecorded,
  onAlreadyResolved,
  onCancel,
  onOpenWizard,
}) => {
  const styles = useStyles();
  const { replies } = inquiry;
  const [replyId, setReplyId] = React.useState<string>(replies.length > 0 ? replies[0].communicationId : '');
  const [disposition, setDisposition] = React.useState<number | null>(null);
  const [busy, setBusy] = React.useState(false);
  const [failure, setFailure] = React.useState<string | null>(null);
  // Another outcome is already on the inquiry: another try can never succeed, so Record is off and the person closes and refreshes.
  const [alreadyResolved, setAlreadyResolved] = React.useState(false);
  // The submit awaits the BFF and must not set state if the picker has gone by the time it answers.
  const mounted = React.useRef(true);
  React.useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);
  const today = React.useMemo(() => new Date(), []);

  const submit = async () => {
    if (disposition === null || replyId === '' || busy) return;
    setBusy(true);
    setFailure(null);
    let result: RecordDispositionResult;
    try {
      result = await service.recordDisposition(inquiry.serviceRequestId, {
        replyCommunicationId: replyId,
        disposition,
      });
    } catch (err) {
      if (!mounted.current) return;
      setFailure(dispositionFailureMessage(err));
      if (isAlreadyResolved(err)) setAlreadyResolved(true);
      setBusy(false);
      return;
    }
    if (!mounted.current) return;
    onRecorded(result);
  };

  return (
    <FormModal
      open
      onClose={busy ? () => undefined : onCancel}
      onSubmit={() => void submit()}
      title="Record the outcome of the inquiry"
      submitLabel="Record outcome"
      submitDisabled={disposition === null || replyId === '' || alreadyResolved}
      busy={busy}
    >
      <div className={styles.body} data-testid="disposition-picker">
        {subjectName ? <Caption1 className={styles.meta}>{subjectName}</Caption1> : null}

        {failure ? (
          <MessageBar intent="error" data-testid="disposition-error">
            <MessageBarBody>{failure}</MessageBarBody>
          </MessageBar>
        ) : null}

        {replies.length === 0 ? (
          <MessageBar intent="info" data-testid="disposition-no-replies">
            <MessageBarBody>
              No reply you can see has been filed against this inquiry yet. Record the outcome once the reply is filed.
            </MessageBarBody>
          </MessageBar>
        ) : (
          <Field label="The reply you are answering from">
            <RadioGroup
              value={replyId}
              onChange={(_, data) => setReplyId(data.value)}
              data-testid="disposition-replies"
            >
              {replies.map(r => {
                const received = formatShortDate(r.receivedOn, today);
                return (
                  <Radio
                    key={r.communicationId}
                    value={r.communicationId}
                    label={`${r.subject ?? 'No subject'}${received ? ` - received ${received}` : ''}`}
                  />
                );
              })}
            </RadioGroup>
          </Field>
        )}

        <Field label="How did the inquiry turn out?">
          <RadioGroup
            value={disposition === null ? '' : String(disposition)}
            onChange={(_, data) => setDisposition(Number(data.value))}
            data-testid="disposition-options"
          >
            {DISPOSITION_OPTIONS.map(o => (
              <Radio key={o.value} value={String(o.value)} label={o.label} />
            ))}
          </RadioGroup>
        </Field>

        <div>
          {alreadyResolved ? (
            <Button appearance="primary" size="small" onClick={onAlreadyResolved}>
              Close and refresh
            </Button>
          ) : null}
          <Button appearance="subtle" size="small" onClick={onOpenWizard} disabled={busy}>
            Open the decision wizard instead
          </Button>
        </div>
      </div>
    </FormModal>
  );
};

InquiryDispositionPicker.displayName = 'InquiryDispositionPicker';
