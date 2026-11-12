/**
 * The History pane tab (ebilling-r1 task 111): the invoice's Decision Records (task 100) and its corrections (task 101), newest first, as the
 * shared `RecordRow` (append-only: no edit, no delete). A decision's badge carries the eBilling words (Accepted, Reduced, Disallowed,
 * Approved, Rejected, Returned to firm, Overridden) through task 109's `statusOverride`; a correction's says what was done to the value
 * (Corrected, Confirmed) and its entry shows the value as read and the final value. The records are the server's: the Decision Records come
 * with the review (task 110's read), the corrections are read when the tab opens; the page adds none and changes none.
 */

import * as React from 'react';
import { Caption1, Text, makeStyles, tokens } from '@fluentui/react-components';
import { RecordRow, type RecordClass } from '@spaarke/ui-components';
import type { Correction, InvoiceReview, InvoiceReviewApi } from '../../services/invoiceReviewApi';
import { correctionBadge, correctionTitle, historyItems, recordBadge, recordTitle } from '../../services/decisionModel';
import { fieldLabel } from '../../services/reviewModel';
import { HEADER_FIELDS } from '../Dialogs';

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalS, padding: tokens.spacingHorizontalL },
  muted: { color: tokens.colorNeutralForeground3 },
});

const CLASSES: ReadonlySet<string> = new Set(['Judgement', 'Routine', 'Dismissal']);

export interface HistoryTabProps {
  review: InvoiceReview;
  api: Pick<InvoiceReviewApi, 'listCorrections'>;
  invoiceId: string;
}

export function HistoryTab({ review, api, invoiceId }: HistoryTabProps) {
  const s = useStyles();
  const [corrections, setCorrections] = React.useState<Correction[]>([]);
  const [correctionsFailed, setCorrectionsFailed] = React.useState(false);
  const alive = React.useRef(true);
  React.useEffect(() => {
    alive.current = true;
    return () => {
      alive.current = false;
    };
  }, []);

  React.useEffect(() => {
    void api.listCorrections(invoiceId).then(r => {
      if (!alive.current) return;
      if (r.ok) {
        setCorrections(r.value);
        setCorrectionsFailed(false);
      } else {
        setCorrectionsFailed(true);
      }
    });
  }, [api, invoiceId, review.decisionHistory.length]);

  const sequenceOf = React.useMemo(() => new Map(review.lines.map(l => [l.id, l.sequence])), [review.lines]);
  const items = React.useMemo(() => historyItems(review.decisionHistory, corrections), [review.decisionHistory, corrections]);

  if (items.length === 0) {
    return (
      <div className={s.root} data-testid="history-empty">
        <Text>No decisions or corrections are recorded on this invoice yet.</Text>
        {correctionsFailed && <Caption1 className={s.muted}>The corrections could not be loaded.</Caption1>}
      </div>
    );
  }

  return (
    <div className={s.root} data-testid="history-tab" role="list" aria-label="Decision Records and corrections, newest first">
      {correctionsFailed && (
        <Caption1 className={s.muted} data-testid="history-corrections-failed">
          The corrections could not be loaded; only the Decision Records are listed.
        </Caption1>
      )}
      {items.map(item => {
        if (item.kind === 'decision') {
          const r = item.record;
          const badge = recordBadge(r);
          const seq = r.lineId ? (sequenceOf.get(r.lineId) ?? null) : null;
          return (
            <div role="listitem" key={`d-${r.recordId}`} data-kind="decision">
              <RecordRow
                recordId={r.number ?? r.recordId}
                title={recordTitle(r, seq)}
                state="Decided"
                statusOverride={{ label: badge.label, tone: badge.tone }}
                recordClass={r.recordClass && CLASSES.has(r.recordClass) ? (r.recordClass as RecordClass) : null}
                decidedBy={r.decidedBy?.name ?? null}
                decidedOn={r.decidedOn}
              >
                {r.reason ? <Caption1>{r.reason}</Caption1> : undefined}
              </RecordRow>
            </div>
          );
        }
        const c = item.correction;
        const badge = correctionBadge(c.outcome);
        const seq = c.lineId ? (sequenceOf.get(c.lineId) ?? null) : null;
        return (
          <div role="listitem" key={`c-${c.id}`} data-kind="correction">
            <RecordRow
              recordId="Correction"
              title={correctionTitle(c, c.lineId ? fieldLabel(c.field) : (HEADER_FIELDS.find(h => h.field === c.field)?.label ?? fieldLabel(c.field)), seq)}
              state="Decided"
              statusOverride={{ label: badge.label, tone: badge.tone }}
              decidedOn={c.reviewedOn ?? null}
            >
              <Caption1>
                As read: {c.aiValue ?? 'nothing'} · Final: {c.finalValue ?? 'nothing'}
              </Caption1>
            </RecordRow>
          </div>
        );
      })}
    </div>
  );
}
