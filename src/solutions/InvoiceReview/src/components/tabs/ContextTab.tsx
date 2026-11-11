/**
 * The Context pane tab (ebilling-r1 task 111; task 106): the matter or project the invoice sits on, its budget, prior invoices, the
 * disallow history and the rate history. Five independent sections: each is `available`, `unavailable` (with the server's message and code)
 * or `notTracked` (the record has no such data), and a section that is not available says so in its own place without hiding the others.
 *
 * Nothing is computed here: budget, spend, remaining, utilization and every rate are the server's figures as returned.
 */

import * as React from 'react';
import { Button, Caption1, Spinner, Text, makeStyles, tokens } from '@fluentui/react-components';
import type {
  ContextSection,
  InvoiceContext,
  InvoiceDecisionApi,
} from '../../services/decisionApi';
import { decisionFailureMessage } from '../../services/decisionApi';
import type { ApiFailure } from '../../services/invoiceReviewApi';
import { money, shortDate } from '../../services/reviewModel';

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalL, padding: tokens.spacingHorizontalL },
  section: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalXS },
  heading: { fontWeight: tokens.fontWeightSemibold },
  kv: { display: 'grid', gridTemplateColumns: '140px minmax(0, 1fr)', rowGap: tokens.spacingVerticalXXS, columnGap: tokens.spacingHorizontalM },
  muted: { color: tokens.colorNeutralForeground3 },
  list: { margin: 0, paddingLeft: tokens.spacingHorizontalL, display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalXXS },
  center: { display: 'flex', justifyContent: 'center', padding: tokens.spacingVerticalXL },
});

/** What a section that has no data says. Section names are the page's; the reason is the server's (message and code) or a fixed sentence. */
export function sectionNotice(section: ContextSection<unknown>, what: string): string | null {
  if (section.status === 'available') return null;
  if (section.status === 'notTracked') return section.message ? section.message : `${what} is not tracked for this record.`;
  const base = section.message ? section.message : `${what} is not available right now.`;
  return section.code ? `${base} (${section.code})` : base;
}

function Section<T>({ title, what, section, children }: { title: string; what: string; section: ContextSection<T>; children: (data: T) => React.ReactNode }) {
  const s = useStyles();
  const notice = sectionNotice(section, what);
  return (
    <section className={s.section} aria-label={title} data-testid={`ctx-${title.toLowerCase().replace(/\s+/g, '-')}`} data-status={section.status}>
      <Text className={s.heading}>{title}</Text>
      {notice !== null || !section.data ? (
        <Caption1 className={s.muted} data-testid="ctx-notice">
          {notice ?? `${what} is not available right now.`}
        </Caption1>
      ) : (
        children(section.data)
      )}
    </section>
  );
}

function Row({ label, value }: { label: string; value: React.ReactNode }) {
  const s = useStyles();
  return (
    <>
      <Caption1 className={s.muted}>{label}</Caption1>
      <Text>{value}</Text>
    </>
  );
}

export interface ContextTabProps {
  api: InvoiceDecisionApi;
  invoiceId: string;
}

export function ContextTab({ api, invoiceId }: ContextTabProps) {
  const s = useStyles();
  const [context, setContext] = React.useState<InvoiceContext | null>(null);
  const [failure, setFailure] = React.useState<ApiFailure | null>(null);
  const [loading, setLoading] = React.useState(true);
  const alive = React.useRef(true);
  React.useEffect(() => {
    alive.current = true;
    return () => {
      alive.current = false;
    };
  }, []);

  const load = React.useCallback(async () => {
    setLoading(true);
    const r = await api.getContext(invoiceId);
    if (!alive.current) return;
    if (r.ok) {
      setContext(r.value);
      setFailure(null);
    } else {
      setFailure(r.failure);
    }
    setLoading(false);
  }, [api, invoiceId]);

  React.useEffect(() => {
    void load();
  }, [load]);

  if (loading && !context) {
    return (
      <div className={s.center} data-testid="ctx-loading">
        <Spinner size="small" label="Loading context…" />
      </div>
    );
  }
  if (!context) {
    return (
      <div className={s.root} data-testid="ctx-error">
        <Text>The context could not be loaded.</Text>
        <Caption1 className={s.muted}>{failure ? decisionFailureMessage(failure) : ''}</Caption1>
        <div>
          <Button size="small" onClick={() => void load()}>
            Retry
          </Button>
        </div>
      </div>
    );
  }

  return (
    <div className={s.root} data-testid="context-tab">
      <Section title="Matter or project" what="The record" section={context.coreRecord}>
        {d => (
          <div className={s.kv}>
            <Row label="Name" value={d.name ?? d.id} />
            {d.number && <Row label="Number" value={d.number} />}
            {d.status && <Row label="Status" value={d.status} />}
            {d.openedDate && <Row label="Opened" value={shortDate(d.openedDate)} />}
            {d.closedDate && <Row label="Closed" value={shortDate(d.closedDate)} />}
          </div>
        )}
      </Section>

      <Section title="Budget" what="The budget" section={context.budget}>
        {d => (
          <div className={s.kv}>
            <Row label="Budget" value={money(d.budget)} />
            <Row label="Spend to date" value={money(d.spendToDate)} />
            <Row label="Remaining" value={money(d.remaining)} />
            <Row label="Utilization" value={d.utilizationPercent === null ? '—' : `${d.utilizationPercent}%`} />
            {d.invoiceCount !== null && <Row label="Invoices" value={d.invoiceCount} />}
            {d.asOf && <Row label="As of" value={shortDate(d.asOf.slice(0, 10))} />}
          </div>
        )}
      </Section>

      <Section title="Prior invoices" what="Prior invoices" section={context.priorInvoices}>
        {d =>
          d.items.length === 0 ? (
            <Caption1 className={s.muted}>No earlier invoices on this record.</Caption1>
          ) : (
            <ul className={s.list} aria-label="Prior invoices">
              {d.items.map(i => (
                <li key={i.id}>
                  <Caption1>
                    {i.number ?? 'No number'} · {shortDate(i.invoiceDate)} · billed {money(i.billed)} · approved {money(i.approved)} · {i.status}
                  </Caption1>
                </li>
              ))}
              {d.hasMore && (
                <li>
                  <Caption1 className={s.muted}>Showing the latest {d.limit}.</Caption1>
                </li>
              )}
            </ul>
          )
        }
      </Section>

      <Section title="Disallow history" what="The disallow history" section={context.disallowHistory}>
        {d => (
          <div className={s.kv}>
            <Row label="Invoices considered" value={d.invoicesConsidered} />
            <Row label="Reduced" value={`${d.reducedCount} lines · ${money(d.reducedAmount)}`} />
            <Row label="Disallowed" value={`${d.disallowedCount} lines · ${money(d.disallowedAmount)}`} />
            {d.topReasons.length > 0 && (
              <Row label="Top reasons" value={d.topReasons.map(r => `${r.reason} (${r.count})`).join('; ')} />
            )}
          </div>
        )}
      </Section>

      <Section title="Rate history" what="The rate history" section={context.rateHistory}>
        {d =>
          d.timekeepers.length === 0 ? (
            <Caption1 className={s.muted}>No timekeepers on this invoice have rate history.</Caption1>
          ) : (
            <ul className={s.list} aria-label="Rate history">
              {d.timekeepers.map(t => (
                <li key={t.timekeeperId ?? t.name}>
                  <Caption1>
                    <strong>{t.name}</strong>
                    {' · approved '}
                    {t.approvedRates.length === 0 ? 'none on file' : t.approvedRates.map(r => money(r.approvedRate)).join(', ')}
                    {' · billed here '}
                    {t.billedOnThisInvoice.length === 0 ? 'none' : t.billedOnThisInvoice.map(r => money(r.rate)).join(', ')}
                    {t.billedPreviously.length > 0 && ` · billed before ${t.billedPreviously.map(r => money(r.rate)).join(', ')}`}
                  </Caption1>
                </li>
              ))}
            </ul>
          )
        }
      </Section>
    </div>
  );
}
