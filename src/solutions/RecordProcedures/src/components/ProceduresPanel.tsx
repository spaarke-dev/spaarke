/**
 * Procedures on the record (ebilling-r1 task 084a, EB-D-217): the Billing tab card of a matter, project or work assignment. For one firm and
 * one work date it shows the Procedures the record is subject to and the rules in force, from task 084's two read routes, read-only.
 *
 * Layout (the prototype's panel): "Invoices from" (firm) and "Work dated" (date) at the top; a gap bar when an invoice for that firm and
 * date would be held, with Show; two tabs, Procedures (n) and Rules in force (n), each with count cards that filter; rows with one state
 * badge each, excluded rows dimmed. Rows carry no buttons and open nothing: the wizards are 084b to 084d.
 *
 * Fail closed. Both routes are read for every (firm, date); if either fails, NO row is shown on either tab (never a partial list). A 404 is
 * the one not-available message, whether the record is missing or the caller cannot read it. The page decides nothing about who may see a
 * row: the server already refuses unless the caller can Read every row it names.
 *
 * The panel imports shared components only through `@spaarke/ui-components` (MetricCardRow, StatusBadge) and Fluent v9 tokens, so the host's
 * light/dark theme (resolved in `main.tsx`) is the only thing that decides colours (ADR-021).
 */

import * as React from 'react';
import {
  Button,
  Caption1,
  Dropdown,
  Field,
  Input,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  MessageBarTitle,
  Option,
  Spinner,
  Tab,
  TabList,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { MetricCardRow } from '@spaarke/ui-components';
import {
  failureKind,
  failureMessage,
  pickFailure,
  type ApiFailure,
  type CoreRoute,
  type FirmRef,
  type GapView,
  type PanelFailureKind,
  type ProcedureItem,
  type RecordProceduresApi,
  type RecordProceduresView,
  type RecordRulesInForceView,
  type RuleItem,
} from '../services/recordProceduresApi';
import type { CoreTable } from '../services/launch';
import type { FirmChoices, FirmOption, InvoicedFirms } from '../services/firmChoices';
import {
  PROCEDURE_FILTERS,
  RULE_FILTERS,
  buildCountCards,
  gapBarText,
  matchesProcedureFilter,
  matchesRuleFilter,
  resolveFilter,
  runningCount,
  todayIso,
  type ProcedureFilter,
  type RuleFilter,
} from '../services/model';
import type { OverridesApi } from '../services/overridesApi';
import { ProcedureList } from './ProcedureList';
import { RuleList } from './RuleList';
import { ProcedureWizard } from './wizards/ProcedureWizard';
import { RuleWizard } from './wizards/RuleWizard';

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalM,
    boxSizing: 'border-box',
    minHeight: '100%',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
  bar: { display: 'flex', alignItems: 'flex-end', gap: tokens.spacingHorizontalL, flexWrap: 'wrap' },
  firm: { minWidth: '240px' },
  centered: { display: 'flex', alignItems: 'center', justifyContent: 'center', padding: tokens.spacingVerticalXL },
  muted: { color: tokens.colorNeutralForeground3 },
});

export interface ProceduresPanelProps {
  /** The route segment and the table of the form's record. */
  core: CoreRoute;
  table: CoreTable;
  recordId: string;
  api: RecordProceduresApi;
  /** The writes of the Procedure and rule wizards (task 084b), on task 089's routes. */
  overrides: OverridesApi;
  /** Where the picker finds the firms that have invoiced the record (wired to Xrm.WebApi in `main.tsx`). */
  firmChoices: FirmChoices;
  /** Today as yyyy-MM-dd; defaults to the user's own today. Injected by the tests. */
  today?: string;
}

type Load =
  | { state: 'loading' }
  | { state: 'error'; failure: ApiFailure }
  | { state: 'ready'; procedures: RecordProceduresView; rules: RecordRulesInForceView };

/** What is known of the record's billing firm: nothing yet, none, or this firm. */
type Billing = { kind: 'unknown' } | { kind: 'none' } | { kind: 'firm'; firm: FirmRef };

const DEFAULT_KEY = '__billing_default__';
const DATE = /^\d{4}-\d{2}-\d{2}$/;

/** The failures that leave nothing to change: the message stands alone, without the pickers. */
const STANDALONE: ReadonlySet<PanelFailureKind> = new Set(['notAvailable', 'ebillingOff', 'signin']);
/** The failures a retry can cure. */
const RETRYABLE: ReadonlySet<PanelFailureKind> = new Set(['signin', 'unavailable', 'network', 'other', 'invalidQuery']);

/** The row a wizard is open on: a snapshot of the row and of the answer it came from, so a re-read after a write does not change the open wizard. */
type OpenRow =
  | { kind: 'procedure'; key: string; row: ProcedureItem; gaps: GapView[]; date: string }
  | { kind: 'rule'; key: string; row: RuleItem };

const procedureKey = (p: ProcedureItem): string => p.id.toLowerCase();
const ruleKey = (r: RuleItem): string => `${r.policyId}:${r.policyVersionId ?? ''}`.toLowerCase();

export const ProceduresPanel: React.FC<ProceduresPanelProps> = ({ core, table, recordId, api, overrides, firmChoices, today }) => {
  const s = useStyles();
  const todayStr = React.useMemo(() => today ?? todayIso(), [today]);

  const [firmId, setFirmId] = React.useState<string | null>(null);
  const [date, setDate] = React.useState(todayStr);
  const [attempt, setAttempt] = React.useState(0);
  const [load, setLoad] = React.useState<Load>({ state: 'loading' });
  const [billing, setBilling] = React.useState<Billing>({ kind: 'unknown' });
  const [invoiced, setInvoiced] = React.useState<InvoicedFirms>({ firms: [], complete: true });
  const [tab, setTab] = React.useState<'procedures' | 'rules'>('procedures');
  const [pf, setPf] = React.useState<ProcedureFilter | 'all'>('all');
  const [rf, setRf] = React.useState<RuleFilter | 'all'>('all');
  const [open, setOpen] = React.useState<OpenRow | null>(null);

  // An empty or half-typed date reads as today (the prototype's `date || TODAY`).
  const requestDate = DATE.test(date) ? date : todayStr;

  // The firms that have invoiced the record, once per record. A failure is not fatal: the picker offers fewer firms and says so.
  React.useEffect(() => {
    let cancelled = false;
    firmChoices
      .invoicedBy(table, recordId)
      .then(found => {
        if (!cancelled) setInvoiced(found);
      })
      .catch(() => {
        if (!cancelled) setInvoiced({ firms: [], complete: false });
      });
    return () => {
      cancelled = true;
    };
  }, [firmChoices, table, recordId]);

  // The newest read wins: a read started later (a new firm or date, or a re-read after a write) makes an earlier one's answer stale.
  const readSeq = React.useRef(0);

  // After a wizard's write: read both lists again for the same firm and date WITHOUT going back to "loading", so the open wizard (and its
  // success screen) stays where it is. Either read failing is the same fail-closed state as on any read: no rows.
  const refresh = React.useCallback(() => {
    const seq = ++readSeq.current;
    const query = { firmId, date: requestDate };
    Promise.all([api.procedures(core, recordId, query), api.rulesInForce(core, recordId, query)])
      .then(([procedures, rules]) => {
        if (seq !== readSeq.current) return;
        if (procedures.ok && rules.ok) {
          setLoad({ state: 'ready', procedures: procedures.value, rules: rules.value });
          return;
        }
        const [first, second] = [procedures, rules].flatMap(r => (r.ok ? [] : [r.failure]));
        setLoad({ state: 'error', failure: pickFailure(first, second ?? null) });
      })
      .catch(() => {
        if (seq === readSeq.current) setLoad({ state: 'error', failure: { status: 0, errorCode: null, detail: null, signInFailed: false } });
      });
  }, [api, core, recordId, firmId, requestDate]);

  // BOTH routes for every (firm, date). Either failing is one failure and no rows.
  React.useEffect(() => {
    let cancelled = false;
    const seq = ++readSeq.current;
    setLoad({ state: 'loading' });
    const query = { firmId, date: requestDate };
    Promise.all([api.procedures(core, recordId, query), api.rulesInForce(core, recordId, query)])
      .then(([procedures, rules]) => {
        if (cancelled || seq !== readSeq.current) return;
        if (procedures.ok && rules.ok) {
          // The default read (no firmId) tells which firm is the record's billing firm.
          if (firmId === null) {
            const v = procedures.value;
            setBilling(v.firmSource === 'Record' && v.firm ? { kind: 'firm', firm: v.firm } : v.firmSource === 'None' ? { kind: 'none' } : { kind: 'unknown' });
          }
          setLoad({ state: 'ready', procedures: procedures.value, rules: rules.value });
          return;
        }
        const [first, second] = [procedures, rules].flatMap(r => (r.ok ? [] : [r.failure]));
        setLoad({ state: 'error', failure: pickFailure(first, second ?? null) });
      })
      .catch(() => {
        if (!cancelled) setLoad({ state: 'error', failure: { status: 0, errorCode: null, detail: null, signInFailed: false } });
      });
    return () => {
      cancelled = true;
    };
  }, [api, core, recordId, firmId, requestDate, attempt]);

  // The picker: the billing firm first, then the other firms that have invoiced the record, each once.
  const options = React.useMemo(() => {
    const list: Array<{ key: string; label: string }> = [];
    const seen = new Set<string>();
    if (billing.kind === 'firm') {
      list.push({ key: billing.firm.id.toLowerCase(), label: `${billing.firm.name ?? 'Billing firm'} (billing firm)` });
      seen.add(billing.firm.id.toLowerCase());
    } else {
      list.push({ key: DEFAULT_KEY, label: billing.kind === 'none' ? 'No billing firm' : 'Record’s billing firm' });
    }
    for (const f of invoiced.firms as FirmOption[]) {
      if (seen.has(f.id)) continue;
      seen.add(f.id);
      list.push({ key: f.id, label: f.name });
    }
    return list;
  }, [billing, invoiced]);

  const selectedKey = firmId ? firmId.toLowerCase() : billing.kind === 'firm' ? billing.firm.id.toLowerCase() : DEFAULT_KEY;
  const selectedLabel = options.find(o => o.key === selectedKey)?.label ?? '';

  const kind: PanelFailureKind | null = load.state === 'error' ? failureKind(load.failure) : null;
  const toolbar = (
    <>
      <div className={s.bar}>
        <Field label="Invoices from" className={s.firm}>
          <Dropdown
            value={selectedLabel}
            selectedOptions={[selectedKey]}
            onOptionSelect={(_, d) => setFirmId(d.optionValue && d.optionValue !== DEFAULT_KEY ? d.optionValue : null)}
            data-testid="firm-picker"
          >
            {options.map(o => (
              <Option key={o.key} value={o.key} text={o.label}>
                {o.label}
              </Option>
            ))}
          </Dropdown>
        </Field>
        <Field label="Work dated">
          <Input type="date" value={date} onChange={(_, d) => setDate(d.value)} data-testid="date-picker" />
        </Field>
      </div>
      {!invoiced.complete && (
        <Caption1 className={s.muted} data-testid="firms-incomplete">
          Some firms that have invoiced this record could not be listed.
        </Caption1>
      )}
    </>
  );

  // ── The wizards (task 084b) ────────────────────────────────────────────────
  // Rendered next to the body, not inside it: a re-read after a write must not unmount the wizard that is showing its success screen.
  const readyLoad = load.state === 'ready' ? load : null;
  const shownProcedureRows = readyLoad ? readyLoad.procedures.procedures.filter(p => matchesProcedureFilter(resolveFilter(PROCEDURE_FILTERS, readyLoad.procedures.procedures, pf), p)) : [];
  const shownRuleRows = readyLoad ? readyLoad.rules.rules.filter(r => matchesRuleFilter(resolveFilter(RULE_FILTERS, readyLoad.rules.rules, rf), r)) : [];
  const openProcedure = (p: ProcedureItem) => {
    if (readyLoad) setOpen({ kind: 'procedure', key: procedureKey(p), row: p, gaps: readyLoad.procedures.gaps, date: readyLoad.procedures.date });
  };
  const openRule = (r: RuleItem) => setOpen({ kind: 'rule', key: ruleKey(r), row: r });

  /** ‹ › through the list as the user sees it (filters applied); absent when the open row is not in it any more. */
  function navFor<T>(rows: readonly T[], keyOf: (row: T) => string, currentKey: string, show: (row: T) => void) {
    const index = rows.findIndex(r => keyOf(r) === currentKey);
    if (index < 0) return undefined;
    return {
      index,
      total: rows.length,
      onNavigate: (dir: 'prev' | 'next') => {
        const target = rows[dir === 'next' ? index + 1 : index - 1];
        if (target) show(target);
      },
    };
  }

  // The firms the rule wizard's "For invoices from" offers: the billing firm, the firm in use and the firms that have invoiced the record.
  const ruleFirms = React.useMemo(() => {
    const out: FirmOption[] = [];
    const add = (id: string | undefined, name: string | null | undefined) => {
      if (id && !out.some(f => f.id === id.toLowerCase())) out.push({ id: id.toLowerCase(), name: name ?? 'Firm' });
    };
    if (billing.kind === 'firm') add(billing.firm.id, billing.firm.name);
    if (readyLoad) add(readyLoad.rules.firm?.id, readyLoad.rules.firm?.name);
    for (const f of invoiced.firms) add(f.id, f.name);
    return out;
  }, [billing, invoiced, readyLoad]);

  const record = { table, name: readyLoad ? readyLoad.procedures.record.name : null };
  const closeWizard = () => setOpen(null);
  const wizard =
    open?.kind === 'procedure' ? (
      <ProcedureWizard
        row={open.row}
        gaps={open.gaps}
        core={core}
        recordId={recordId}
        record={record}
        date={open.date}
        api={overrides}
        nav={navFor(shownProcedureRows, procedureKey, open.key, openProcedure)}
        onChanged={refresh}
        onClose={closeWizard}
      />
    ) : open?.kind === 'rule' ? (
      <RuleWizard
        row={open.row}
        core={core}
        recordId={recordId}
        record={record}
        firms={ruleFirms}
        api={overrides}
        nav={navFor(shownRuleRows, ruleKey, open.key, openRule)}
        onChanged={refresh}
        onClose={closeWizard}
      />
    ) : null;

  const renderBody = (): React.ReactNode => {
    if (kind && STANDALONE.has(kind)) {
      const m = failureMessage(kind);
      return (
        <div className={s.root} data-testid="recordprocedures-error" data-error-kind={kind}>
          <MessageBar intent={kind === 'notAvailable' ? 'info' : 'warning'} layout="multiline">
            <MessageBarBody>
              <MessageBarTitle>{m.title}</MessageBarTitle>
              {m.body}
            </MessageBarBody>
            {RETRYABLE.has(kind) && (
              <MessageBarActions>
                <Button onClick={() => setAttempt(n => n + 1)}>Retry</Button>
              </MessageBarActions>
            )}
          </MessageBar>
        </div>
      );
    }

    if (load.state === 'error') {
      const errorKind = failureKind(load.failure);
      const m = failureMessage(errorKind);
      return (
        <div className={s.root} data-testid="recordprocedures-error" data-error-kind={errorKind}>
          {toolbar}
          <MessageBar intent="warning" layout="multiline">
            <MessageBarBody>
              <MessageBarTitle>{m.title}</MessageBarTitle>
              {m.body}
            </MessageBarBody>
            <MessageBarActions>
              <Button onClick={() => setAttempt(n => n + 1)}>Retry</Button>
            </MessageBarActions>
          </MessageBar>
        </div>
      );
    }

    if (load.state === 'loading') {
      return (
        <div className={s.root}>
          {toolbar}
          <div className={s.centered} data-testid="recordprocedures-loading">
            <Spinner size="small" label="Loading procedures…" />
          </div>
        </div>
      );
    }

    const { procedures: pv, rules: rv } = load;
    const procedures = pv.procedures;
    const rules = rv.rules;
    const resolvedPf = resolveFilter(PROCEDURE_FILTERS, procedures, pf);
    const resolvedRf = resolveFilter(RULE_FILTERS, rules, rf);
    const shownProcedures = procedures.filter(p => matchesProcedureFilter(resolvedPf, p));
    const shownRules = rules.filter(r => matchesRuleFilter(resolvedRf, r));
    const running = runningCount(rules);

    const procedureCards = buildCountCards({
      idPrefix: 'procedures',
      noun: { singular: 'Procedure', plural: 'Procedures' },
      filters: PROCEDURE_FILTERS,
      rows: procedures,
      selected: resolvedPf,
      onSelect: setPf,
      allLabel: 'All',
    });
    const ruleCards = buildCountCards({
      idPrefix: 'rules',
      noun: { singular: 'rule', plural: 'rules' },
      filters: RULE_FILTERS,
      rows: rules,
      selected: resolvedRf,
      onSelect: setRf,
      allLabel: 'All',
      allNote: `${running} running`,
    });

    return (
      <div className={s.root} data-testid="recordprocedures-ready">
        {toolbar}

        {pv.gaps.length > 0 && (
          <MessageBar intent="error" layout="multiline" data-testid="gap-bar">
            <MessageBarBody>{gapBarText(pv.firm?.name ?? null, pv.date, pv.gaps)}</MessageBarBody>
            <MessageBarActions>
              <Button
                onClick={() => {
                  setTab('procedures');
                  setPf('attention');
                }}
              >
                Show
              </Button>
            </MessageBarActions>
          </MessageBar>
        )}

        <TabList selectedValue={tab} onTabSelect={(_, d) => setTab(d.value as 'procedures' | 'rules')}>
          <Tab value="procedures">Procedures ({procedures.length})</Tab>
          <Tab value="rules">Rules in force ({running})</Tab>
        </TabList>

        {tab === 'procedures' ? (
          <>
            <MetricCardRow layout="wide" ariaLabel="Filter Procedures" cards={procedureCards} />
            <ProcedureList
              procedures={shownProcedures}
              gaps={pv.gaps}
              emptyText={procedures.length === 0 ? 'No Procedures apply to this record for this firm and date.' : 'Nothing here.'}
              onOpen={openProcedure}
            />
          </>
        ) : (
          <>
            <MetricCardRow layout="wide" ariaLabel="Filter rules" cards={ruleCards} />
            <RuleList
              rules={shownRules}
              emptyText={rules.length === 0 ? 'No rules are in force on this record for this firm and date.' : 'Nothing here.'}
              onOpen={openRule}
            />
          </>
        )}
      </div>
    );
  };

  return (
    <>
      {renderBody()}
      {wizard}
    </>
  );
};
