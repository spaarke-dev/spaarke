/**
 * Reject confirmation with a required reason (ebilling-r1 task 077, FR-28, D-113).
 *
 * A `FormModal` (ADR-050): the shared `ConfirmModal` has a message and no field, and it cannot disable its confirm button, so it cannot
 * hold a required reason. The Reject button stays disabled until the trimmed reason is 1 to 2,000 characters (`canConfirmReject`); the
 * server checks the same rule (400 otherwise). The reason is shown to admins on the rejected draft and never to firms.
 */

import * as React from 'react';
import { Field, Text, Textarea, makeStyles, tokens } from '@fluentui/react-components';
import { FormModal } from '@spaarke/ui-components';
import { canConfirmReject, checkRejectionReason } from '../services/reviewActions';
import { MAX_REJECTION_REASON } from '../services/ruleSetApi';

const useStyles = makeStyles({
  muted: { color: tokens.colorNeutralForeground3 },
});

export interface RejectModalProps {
  open: boolean;
  /** The rule being rejected, for the message. */
  ruleName: string;
  busy: boolean;
  onClose: () => void;
  /** Called with the trimmed reason; only when {@link canConfirmReject} holds. */
  onReject: (reason: string) => void;
}

export const RejectModal: React.FC<RejectModalProps> = ({ open, ruleName, busy, onClose, onReject }) => {
  const s = useStyles();
  const [text, setText] = React.useState('');

  React.useEffect(() => {
    if (open) setText('');
  }, [open]);

  const check = checkRejectionReason(text);
  const tooLong = check.over > 0;
  return (
    <FormModal
      open={open}
      onClose={onClose}
      onSubmit={() => canConfirmReject(text, busy) && onReject(check.reason)}
      title="Reject this rule?"
      size="sm"
      submitLabel="Reject"
      submitDisabled={!canConfirmReject(text, false)}
      busy={busy}
    >
      <Text>
        <strong>{ruleName}</strong> will not be published. While the rule set is a draft you can still accept it later.
      </Text>
      <Field
        label="Reason"
        required
        hint="Why this rule is rejected. Admins see it on the rejected rule; firms never do."
        validationState={tooLong ? 'error' : 'none'}
        validationMessage={tooLong ? `${check.over} character${check.over === 1 ? '' : 's'} too long (at most ${MAX_REJECTION_REASON}).` : undefined}
      >
        <Textarea
          resize="vertical"
          rows={4}
          value={text}
          onChange={(_, d) => setText(d.value)}
          disabled={busy}
          data-testid="reject-reason"
        />
      </Field>
      <Text size={200} className={s.muted} data-testid="reject-reason-count">
        {check.reason.length} / {MAX_REJECTION_REASON}
      </Text>
    </FormModal>
  );
};
