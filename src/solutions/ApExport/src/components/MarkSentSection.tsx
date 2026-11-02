/**
 * Mark Sent to AP (ebilling-r1 task 151c-1b, EB-D-182): records that the batch's files reached AP another way. One
 * button; it is disabled while the call is in flight and once the date is recorded, and a repeat is safe anyway (the
 * server returns the date it already holds).
 */

import * as React from 'react';
import { Button, MessageBar, MessageBarBody, MessageBarTitle, Text, makeStyles, tokens } from '@fluentui/react-components';
import { CheckmarkCircle20Regular } from '@fluentui/react-icons';
import type { ApExportApi } from '../services/apExportApi';
import { createSingleFlight } from '../services/singleFlight';
import { markSentOutcome, type MarkSentOutcome } from '../services/markSentFlow';

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalS },
});

export interface MarkSentSectionProps {
  api: Pick<ApExportApi, 'markSentToAp'>;
  batchId: string;
  /** The sentence above the button. */
  intro?: string;
  /** Called once the date is recorded (or was already held). */
  onMarked?: () => void;
}

export const MarkSentSection: React.FC<MarkSentSectionProps> = ({
  api,
  batchId,
  intro = 'If the files reached AP another way (a portal, a hand delivery), record it here. This sets the Sent to AP Date once; it never changes a date that is already set.',
  onMarked,
}) => {
  const s = useStyles();
  const flight = React.useMemo(() => createSingleFlight(), []);
  const [busy, setBusy] = React.useState(false);
  const [outcome, setOutcome] = React.useState<MarkSentOutcome | null>(null);
  const marked = outcome?.kind === 'marked';

  const run = React.useCallback(async () => {
    setBusy(true);
    const ran = await flight.run(async () => markSentOutcome(await api.markSentToAp(batchId)));
    setBusy(false);
    if (!ran.ran) return;
    setOutcome(ran.value);
    if (ran.value.kind === 'marked') onMarked?.();
  }, [api, batchId, flight, onMarked]);

  return (
    <div className={s.root}>
      {!marked && <Text>{intro}</Text>}
      {outcome?.kind === 'marked' && (
        <MessageBar intent="success">
          <MessageBarBody>
            <MessageBarTitle>{outcome.title}</MessageBarTitle>
            {outcome.message} Sent to AP Date: {outcome.sentToApDate}.
          </MessageBarBody>
        </MessageBar>
      )}
      {outcome?.kind === 'failed' && (
        <MessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>Not marked</MessageBarTitle>
            {outcome.message}
          </MessageBarBody>
        </MessageBar>
      )}
      {!marked && (!outcome || outcome.retryable) && (
        <div>
          <Button icon={<CheckmarkCircle20Regular />} disabled={busy} onClick={() => void run()} data-testid="mark-sent-to-ap">
            {busy ? 'Marking…' : outcome ? 'Try again' : 'Mark Sent to AP'}
          </Button>
        </div>
      )}
    </div>
  );
};
