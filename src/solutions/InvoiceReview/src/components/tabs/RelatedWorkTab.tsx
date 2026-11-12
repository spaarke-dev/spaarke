/**
 * The Related work pane tab (ebilling-r1 task 111; task 107): the documents the timekeeper filed on the line's record around the line's date,
 * ranked by how well they match the narrative. On demand: the route runs a search (107's latency K1), so nothing is fetched until the
 * reviewer asks, and the answer is for the selected line only (a different line starts again).
 *
 * An empty answer says why in words (`relatedWorkEmptyText`: the server's reason code), an unavailable one shows the server's message and
 * code. Nothing is ranked or filtered here.
 */

import * as React from 'react';
import { Button, Caption1, Spinner, Text, makeStyles, tokens } from '@fluentui/react-components';
import { decisionFailureMessage, type InvoiceDecisionApi, type RelatedWork } from '../../services/decisionApi';
import { relatedWorkEmptyText } from '../../services/decisionModel';
import type { ApiFailure, ReviewLine } from '../../services/invoiceReviewApi';
import { isAdjustment, shortDate } from '../../services/reviewModel';

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalM, padding: tokens.spacingHorizontalL },
  muted: { color: tokens.colorNeutralForeground3 },
  list: { margin: 0, paddingLeft: tokens.spacingHorizontalL, display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalXS },
});

export interface RelatedWorkTabProps {
  api: InvoiceDecisionApi;
  invoiceId: string;
  line: ReviewLine | null;
}

export function RelatedWorkTab({ api, invoiceId, line }: RelatedWorkTabProps) {
  const s = useStyles();
  if (!line) {
    return (
      <div className={s.root} data-testid="related-no-line">
        <Text>Select a fee line to see the documents behind it.</Text>
      </div>
    );
  }
  // Keyed by the line: the answer belongs to one line and never carries over to the next.
  return <RelatedWorkForLine key={line.id} api={api} invoiceId={invoiceId} line={line} />;
}

function RelatedWorkForLine({ api, invoiceId, line }: RelatedWorkTabProps & { line: ReviewLine }) {
  const s = useStyles();
  const [result, setResult] = React.useState<RelatedWork | null>(null);
  const [failure, setFailure] = React.useState<ApiFailure | null>(null);
  const [loading, setLoading] = React.useState(false);
  const alive = React.useRef(true);
  React.useEffect(() => {
    alive.current = true;
    return () => {
      alive.current = false;
    };
  }, []);

  const find = async () => {
    setLoading(true);
    setFailure(null);
    const r = await api.getRelatedWork(invoiceId, line.id);
    if (!alive.current) return;
    setLoading(false);
    if (r.ok) setResult(r.value);
    else setFailure(r.failure);
  };

  // An Adjustment line is computed, not billed work: nothing to search.
  if (isAdjustment(line)) {
    return (
      <div className={s.root} data-testid="related-not-fee">
        <Text>{relatedWorkEmptyText('not-a-fee-line', null)}</Text>
      </div>
    );
  }

  return (
    <div className={s.root} data-testid="related-work-tab">
      <Text>
        Line {line.sequence}: documents by this timekeeper on the same record near the line date, best match first.
      </Text>
      <div>
        <Button size="small" onClick={() => void find()} disabled={loading} data-testid="find-related">
          {result || failure ? 'Search again' : 'Find related work'}
        </Button>
      </div>
      {loading && <Spinner size="tiny" label="Searching…" />}
      {failure && (
        <Caption1 data-testid="related-error">The search could not be completed. {decisionFailureMessage(failure)}</Caption1>
      )}
      {result && !loading && result.status === 'unavailable' && (
        <Caption1 data-testid="related-unavailable">
          {result.message ?? 'Related work is not available right now.'}
          {result.code ? ` (${result.code})` : ''}
        </Caption1>
      )}
      {result && !loading && result.status !== 'unavailable' && result.items.length === 0 && (
        <Caption1 className={s.muted} data-testid="related-empty">
          {relatedWorkEmptyText(result.reason, result.windowDays)}
        </Caption1>
      )}
      {result && !loading && result.status !== 'unavailable' && result.items.length > 0 && (
        <ol className={s.list} aria-label="Related documents" data-testid="related-items">
          {result.items.map(i => (
            <li key={i.documentId}>
              <Text>{i.name ?? 'Untitled document'}</Text>
              <Caption1 className={s.muted}> · filed {shortDate(i.createdOn.slice(0, 10))}</Caption1>
            </li>
          ))}
          {result.truncated && (
            <Caption1 className={s.muted}>More documents matched than are shown.</Caption1>
          )}
        </ol>
      )}
    </div>
  );
}
