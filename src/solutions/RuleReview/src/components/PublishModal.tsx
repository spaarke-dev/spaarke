/**
 * Publish confirmation (ebilling-r1 task 077, FR-28, D-66): the effective date, what will and will not be published, and - exactly
 * when the rule set's "visible to firms" flag is on - the sentence "Clause quotes will be visible to firms." (`publishNotice`).
 *
 * A `FormModal` (ADR-050). A refused publish (400 `ruleset.publish_invalid`) keeps the dialog open and lists the per-rule errors the
 * server returned; nothing was published.
 */

import * as React from 'react';
import { Field, Input, MessageBar, MessageBarBody, Text, makeStyles, tokens } from '@fluentui/react-components';
import { FormModal } from '@spaarke/ui-components';
import { NoticeBar, type Notice } from './Notices';
import { publishNotice, type ProposalCounts } from '../services/reviewActions';

const useStyles = makeStyles({
  summary: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXS },
  muted: { color: tokens.colorNeutralForeground3 },
});

const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/;

/** Today as `yyyy-MM-dd` in the browser's calendar (the default effective date). */
export function todayIso(now: Date = new Date()): string {
  const month = String(now.getMonth() + 1).padStart(2, '0');
  const day = String(now.getDate()).padStart(2, '0');
  return `${now.getFullYear()}-${month}-${day}`;
}

export interface PublishModalProps {
  open: boolean;
  ruleSetName: string;
  visibleToFirms: boolean;
  counts: ProposalCounts;
  busy: boolean;
  /** The refusal of the last attempt, with the server's per-rule errors. */
  notice: Notice | null;
  onClose: () => void;
  onPublish: (effectiveDate: string) => void;
}

export const PublishModal: React.FC<PublishModalProps> = ({
  open, ruleSetName, visibleToFirms, counts, busy, notice, onClose, onPublish,
}) => {
  const s = useStyles();
  const [date, setDate] = React.useState(todayIso);

  React.useEffect(() => {
    if (open) setDate(todayIso());
  }, [open]);

  const valid = ISO_DATE.test(date);
  return (
    <FormModal
      open={open}
      onClose={onClose}
      onSubmit={() => onPublish(date)}
      title="Publish rule set"
      size="sm"
      submitLabel="Publish"
      submitDisabled={!valid || counts.toPublish === 0}
      busy={busy}
    >
      <div className={s.summary} data-testid="publish-summary">
        <Text>
          Publishing <strong>{ruleSetName}</strong> puts {counts.toPublish} accepted rule{counts.toPublish === 1 ? '' : 's'} in force from
          the date below. Published versions never change: editing a published rule drafts a new version.
        </Text>
        {(counts.proposed > 0 || counts.rejected > 0) && (
          <Text size={200} className={s.muted} data-testid="publish-skipped">
            Not published: {counts.proposed} still proposed, {counts.rejected} rejected.
          </Text>
        )}
      </div>

      <Field label="Effective date" required validationState={valid ? 'none' : 'error'} validationMessage={valid ? undefined : 'Enter a date.'}>
        <Input type="date" value={date} onChange={(_, d) => setDate(d.value)} data-testid="publish-date" />
      </Field>

      <MessageBar intent={visibleToFirms ? 'warning' : 'info'} layout="multiline" data-testid="publish-notice">
        <MessageBarBody>{publishNotice(visibleToFirms)}</MessageBarBody>
      </MessageBar>

      {notice && <NoticeBar notice={notice} />}
    </FormModal>
  );
};
