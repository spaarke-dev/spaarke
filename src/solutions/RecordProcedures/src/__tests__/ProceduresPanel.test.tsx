import '@testing-library/jest-dom';
import * as fs from 'fs';
import * as path from 'path';
import { act, render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { ProceduresPanel } from '../components/ProceduresPanel';
import { createRecordProceduresApi } from '../services/recordProceduresApi';
import type { FirmChoices, InvoicedFirms } from '../services/firmChoices';
import { DIMMED_MARK_KINDS } from '../services/model';
import {
  FENWICK,
  MATTER_ID,
  OKAFOR,
  PROJECT_ID,
  TODAY,
  WA_ID,
  mark,
  matterProcedures,
  matterRules,
  projectProcedures,
  projectRules,
  waProcedures,
  waRules,
} from './fixtures';
import { AuthError, BASE, serverFake, problem, type Handler, type Query } from './testKit';

interface Setup {
  procedures?: Handler;
  rules?: Handler;
  firms?: InvoicedFirms | Promise<InvoicedFirms>;
  theme?: typeof webLightTheme;
  kind?: 'matter' | 'project' | 'workassignment';
}

const KINDS = {
  matter: { core: 'matters', table: 'sprk_matter', id: MATTER_ID } as const,
  project: { core: 'projects', table: 'sprk_project', id: PROJECT_ID } as const,
  workassignment: { core: 'workassignments', table: 'sprk_workassignment', id: WA_ID } as const,
};

function renderPanel(setup: Setup = {}) {
  const k = KINDS[setup.kind ?? 'matter'];
  const server = serverFake({
    procedures: setup.procedures ?? (() => matterProcedures),
    rules: setup.rules ?? (() => matterRules),
  });
  const firmChoices: FirmChoices = {
    invoicedBy: jest.fn(() => Promise.resolve(setup.firms ?? { firms: [{ id: OKAFOR.id, name: OKAFOR.name! }], complete: true })),
  };
  const api = createRecordProceduresApi(server.fetchFn, BASE);
  const view = render(
    <FluentProvider theme={setup.theme ?? webLightTheme}>
      <ProceduresPanel core={k.core} table={k.table} recordId={k.id} api={api} firmChoices={firmChoices} today={TODAY} />
    </FluentProvider>
  );
  return { ...view, ...server, firmChoices };
}

async function ready() {
  return screen.findByTestId('recordprocedures-ready');
}

const rowIds = (prefix: 'procedure' | 'rule') => screen.queryAllByTestId(new RegExp(`^${prefix}-row-`)).map(r => r.getAttribute('data-testid')!.replace(`${prefix}-row-`, ''));
const card = (name: RegExp | string) => screen.getByRole('button', { name });

describe('both lists, with counts', () => {
  it('shows the Procedures tab first: a row per Procedure in the server order, and a count on each tab', async () => {
    renderPanel();
    await ready();

    expect(screen.getByRole('tab', { name: 'Procedures (6)' })).toBeInTheDocument();
    // Rules in force counts the rules that RUN (VAGUE is excluded).
    expect(screen.getByRole('tab', { name: 'Rules in force (3)' })).toBeInTheDocument();
    expect(rowIds('procedure')).toEqual(['p-ocg', 'p-rate', 'p-travel', 'p-instr', 'p-fee', 'p-next']);
  });

  it('a row carries its name, source, version and ONE state badge', async () => {
    renderPanel();
    await ready();

    const rate = within(screen.getByTestId('procedure-row-p-rate'));
    expect(rate.getByText('Fenwick rate card')).toBeInTheDocument();
    expect(rate.getByText('Assigned to this record')).toBeInTheDocument();
    expect(rate.getByText('and 1 more')).toBeInTheDocument();
    expect(rate.getByText('Pinned here')).toBeInTheDocument();
    expect(rate.getByText('pinned to v1, v2 available')).toBeInTheDocument();
    expect(rate.getAllByTestId('status-badge')).toHaveLength(1);
    expect(rate.getByTestId('status-badge')).toHaveTextContent('Pinned');

    const states = Object.fromEntries(rowIds('procedure').map(id => [id, within(screen.getByTestId(`procedure-row-${id}`)).getByTestId('status-badge').textContent]));
    expect(states).toEqual({
      'p-ocg': 'In force',
      'p-rate': 'Pinned',
      'p-travel': 'Excluded here',
      'p-instr': 'In force',
      'p-fee': 'No version',
      'p-next': 'Not yet in force',
    });
  });

  it('excluded and not-yet-in-force rows are dimmed; the others are not', async () => {
    renderPanel();
    await ready();
    const dimmed = screen.getAllByTestId(/^procedure-row-/).filter(r => r.getAttribute('data-dimmed') === 'true').map(r => r.getAttribute('data-testid'));
    expect(dimmed).toEqual(['procedure-row-p-travel', 'procedure-row-p-next']);
  });

  it('the Rules in force tab lists the rules with one badge per mark; excluded rows are dimmed', async () => {
    renderPanel();
    await ready();
    fireEvent.click(screen.getByRole('tab', { name: /Rules in force/ }));

    expect(rowIds('rule')).toEqual(['r-block', 'r-hours-rec', 'r-meals', 'r-vague']);
    const badges = (id: string) => within(screen.getByTestId(`rule-row-${id}`)).getAllByTestId('status-badge').map(b => b.textContent);
    expect(badges('r-block')).toEqual(['In force']);
    expect(badges('r-hours-rec')).toEqual(['Changed here', 'Base rule updated']);
    expect(badges('r-vague')).toEqual(['Excluded']);
    expect(screen.getByTestId('rule-row-r-vague')).toHaveAttribute('data-dimmed', 'true');
    expect(screen.getByTestId('rule-row-r-hours-rec')).toHaveAttribute('data-dimmed', 'false');
    // The changed rule shows what runs and the published values.
    expect(within(screen.getByTestId('rule-row-r-hours-rec')).getByText('12 hours per timekeeper per day')).toBeInTheDocument();
    expect(within(screen.getByTestId('rule-row-r-hours-rec')).getByText('Published: 11 hours per timekeeper per day')).toBeInTheDocument();
  });

  it('a base-retired record rule shows its badge and is not dimmed', async () => {
    renderPanel({ kind: 'project', procedures: () => projectProcedures, rules: () => projectRules });
    await ready();
    fireEvent.click(screen.getByRole('tab', { name: /Rules in force/ }));

    const row = screen.getByTestId('rule-row-r-rush-rec');
    expect(within(row).getByTestId('status-badge')).toHaveTextContent('Base rule retired');
    expect(row).toHaveAttribute('data-dimmed', 'false');
    expect(screen.getByRole('tab', { name: 'Rules in force (1)' })).toBeInTheDocument();
  });
});

describe('count cards that filter', () => {
  it('each Procedures card filters the tab to exactly its rows, and the selected card again returns to All', async () => {
    renderPanel();
    await ready();

    expect(card('All, 6 Procedures')).toHaveAttribute('aria-pressed', 'true');
    fireEvent.click(card('Pinned, 1 Procedure'));
    expect(rowIds('procedure')).toEqual(['p-rate']);
    expect(card('Pinned, 1 Procedure')).toHaveAttribute('aria-pressed', 'true');

    fireEvent.click(card('Excluded, 1 Procedure'));
    expect(rowIds('procedure')).toEqual(['p-travel']);

    fireEvent.click(card('Needs attention, 1 Procedure'));
    expect(rowIds('procedure')).toEqual(['p-fee']);

    fireEvent.click(card('Needs attention, 1 Procedure'));
    expect(rowIds('procedure')).toHaveLength(6);
    // The counts never change with the filter.
    expect(card('Pinned, 1 Procedure')).toBeInTheDocument();
  });

  it('each Rules card filters to exactly its rules; the All card says how many run', async () => {
    renderPanel();
    await ready();
    fireEvent.click(screen.getByRole('tab', { name: /Rules in force/ }));

    expect(screen.getByText('3 running')).toBeInTheDocument();
    expect(card('All, 4 rules')).toBeInTheDocument();

    fireEvent.click(card('Changed here, 1 rule'));
    expect(rowIds('rule')).toEqual(['r-hours-rec']);
    fireEvent.click(card('Excluded, 1 rule'));
    expect(rowIds('rule')).toEqual(['r-vague']);
    fireEvent.click(card('Needs attention, 1 rule'));
    expect(rowIds('rule')).toEqual(['r-hours-rec']);
    fireEvent.click(card('All, 4 rules'));
    expect(rowIds('rule')).toHaveLength(4);
  });

  it('a card with nothing in it is disabled and filters nothing', async () => {
    renderPanel({ kind: 'project', procedures: () => projectProcedures, rules: () => projectRules });
    await ready();
    const excluded = card('Excluded, 0 Procedures');
    expect(excluded).toHaveAttribute('aria-disabled', 'true');
    fireEvent.click(excluded);
    expect(rowIds('procedure')).toEqual(['p-ocg', 'p-rate']);
  });

  it('a filter that no longer holds a row (after a firm or date change) returns to All', async () => {
    const { mock } = renderPanel({
      procedures: q => (q.firmId === OKAFOR.id ? projectProcedures : matterProcedures),
      rules: q => (q.firmId === OKAFOR.id ? projectRules : matterRules),
    });
    await ready();
    fireEvent.click(card('Excluded, 1 Procedure'));
    expect(rowIds('procedure')).toEqual(['p-travel']);

    fireEvent.click(screen.getByRole('combobox'));
    fireEvent.click(await screen.findByRole('option', { name: OKAFOR.name! }));
    await waitFor(() => expect(mock).toHaveBeenCalledWith(expect.stringContaining(`firmId=${OKAFOR.id}`), expect.anything()));
    await waitFor(() => expect(rowIds('procedure')).toEqual(['p-ocg', 'p-rate']));
  });
});

describe('firm and date pickers', () => {
  it('the first read asks for no firm (the record billing firm) and today; the picker then shows the billing firm', async () => {
    const { calls } = renderPanel();
    await ready();

    expect(calls.map(c => [c.leaf, c.query.firmId, c.query.date]).sort()).toEqual([
      ['procedures', null, TODAY],
      ['rules-in-force', null, TODAY],
    ]);
    expect(screen.getByRole('combobox')).toHaveValue('Fenwick & Calder LLP (billing firm)');
    expect(screen.getByLabelText('Work dated')).toHaveValue(TODAY);
  });

  it('offers the billing firm first, then the firms that have invoiced the record, each once', async () => {
    renderPanel({
      firms: {
        complete: true,
        firms: [
          { id: OKAFOR.id, name: OKAFOR.name! },
          { id: FENWICK.id, name: FENWICK.name! }, // the billing firm again: not repeated
          { id: 'cccccccc-0000-4000-8000-000000000003', name: 'Abbott Rowe LLP' },
        ],
      },
    });
    await ready();

    fireEvent.click(screen.getByRole('combobox'));
    const options = (await screen.findAllByRole('option')).map(o => o.textContent);
    expect(options).toEqual(['Fenwick & Calder LLP (billing firm)', 'Okafor Lane LLP', 'Abbott Rowe LLP']);
  });

  it('changing the firm re-reads BOTH routes with the new firmId and the same date', async () => {
    const { calls } = renderPanel();
    await ready();
    const before = calls.length;

    fireEvent.click(screen.getByRole('combobox'));
    fireEvent.click(await screen.findByRole('option', { name: OKAFOR.name! }));
    await waitFor(() => expect(calls.length).toBe(before + 2));

    const added = calls.slice(before);
    expect(added.map(c => c.leaf).sort()).toEqual(['procedures', 'rules-in-force']);
    for (const c of added) expect(c.query).toMatchObject({ firmId: OKAFOR.id, date: TODAY });
    await ready();
    expect(screen.getByRole('combobox')).toHaveValue('Okafor Lane LLP');
  });

  it('changing the date re-reads BOTH routes with the new date and the same firm', async () => {
    const { calls } = renderPanel();
    await ready();
    const before = calls.length;

    fireEvent.change(screen.getByLabelText('Work dated'), { target: { value: '2025-06-30' } });
    await waitFor(() => expect(calls.length).toBe(before + 2));

    for (const c of calls.slice(before)) expect(c.query).toMatchObject({ firmId: null, date: '2025-06-30' });
  });

  it('an empty date reads as today again; a half-typed one never reaches the server', async () => {
    const { calls } = renderPanel();
    await ready();
    fireEvent.change(screen.getByLabelText('Work dated'), { target: { value: '2025-06-30' } });
    await waitFor(() => expect(calls[calls.length - 1]?.query.date).toBe('2025-06-30'));
    fireEvent.change(screen.getByLabelText('Work dated'), { target: { value: '' } });
    await waitFor(() => expect(calls[calls.length - 1]?.query.date).toBe(TODAY));
    for (const c of calls) expect(c.query.date).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  });

  it('with no billing firm the picker says so and offers the firms that have invoiced', async () => {
    renderPanel({
      procedures: () => ({ ...matterProcedures, firm: null, firmSource: 'None' }),
      rules: () => ({ ...matterRules, firm: null, firmSource: 'None' }),
    });
    await ready();
    expect(screen.getByRole('combobox')).toHaveValue('No billing firm');
    fireEvent.click(screen.getByRole('combobox'));
    expect((await screen.findAllByRole('option')).map(o => o.textContent)).toEqual(['No billing firm', 'Okafor Lane LLP']);
    // The gap bar does not name a firm that does not exist.
    expect(screen.getByTestId('gap-bar')).toHaveTextContent(/^Invoices for work dated/);
  });

  it('an incomplete list that still holds readable firms (a failed chunk, or more than one page) offers them AND shows the caption', async () => {
    renderPanel({ firms: { firms: [{ id: OKAFOR.id, name: OKAFOR.name! }], complete: false } });
    await ready();
    expect(screen.getByTestId('firms-incomplete')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('combobox'));
    expect((await screen.findAllByRole('option')).map(o => o.textContent)).toEqual(['Fenwick & Calder LLP (billing firm)', 'Okafor Lane LLP']);
  });

  it('no caption when the list of firms is complete', async () => {
    renderPanel({ firms: { firms: [{ id: OKAFOR.id, name: OKAFOR.name! }], complete: true } });
    await ready();
    expect(screen.queryByTestId('firms-incomplete')).not.toBeInTheDocument();
  });

  it('says so when the list of firms that have invoiced is incomplete, and still shows the lists', async () => {
    renderPanel({ firms: { firms: [], complete: false } });
    await ready();
    expect(screen.getByTestId('firms-incomplete')).toBeInTheDocument();
    expect(rowIds('procedure')).toHaveLength(6);
  });
});

describe('the gap bar', () => {
  it('names the firm, the work date and the gap; Show filters the Procedures tab to the Procedure that causes it', async () => {
    renderPanel({ kind: 'workassignment', procedures: () => waProcedures, rules: () => waRules });
    await ready();

    const bar = screen.getByTestId('gap-bar');
    expect(bar).toHaveTextContent('Fenwick & Calder LLP invoices for work dated 21 Oct 2026 are held');
    expect(bar).toHaveTextContent('Fenwick rate card: The records this one is filed under choose different versions');
    expect(within(screen.getByTestId('procedure-row-p-rate')).getByTestId('status-badge')).toHaveTextContent('Conflicting pins');
    expect(within(screen.getByTestId('procedure-row-p-rate')).getByText('Parents pin different versions')).toBeInTheDocument();

    fireEvent.click(within(bar).getByRole('button', { name: 'Show' }));
    expect(rowIds('procedure')).toEqual(['p-rate']);
    expect(card('Needs attention, 1 Procedure')).toHaveAttribute('aria-pressed', 'true');
  });

  it('Show also switches back from the Rules tab', async () => {
    renderPanel({ kind: 'workassignment', procedures: () => waProcedures, rules: () => waRules });
    await ready();
    fireEvent.click(screen.getByRole('tab', { name: /Rules in force/ }));
    expect(screen.queryByTestId('procedure-list')).not.toBeInTheDocument();
    fireEvent.click(within(screen.getByTestId('gap-bar')).getByRole('button', { name: 'Show' }));
    expect(rowIds('procedure')).toEqual(['p-rate']);
  });

  it('is absent when no invoice would be held', async () => {
    renderPanel({ kind: 'project', procedures: () => projectProcedures, rules: () => projectRules });
    await ready();
    expect(screen.queryByTestId('gap-bar')).not.toBeInTheDocument();
  });
});

describe('failures show no rows (never a partial list)', () => {
  const NO_ROWS = () => {
    expect(screen.queryByTestId('procedure-list')).not.toBeInTheDocument();
    expect(screen.queryByTestId('rule-list')).not.toBeInTheDocument();
    expect(screen.queryAllByTestId(/^(procedure|rule)-row-/)).toHaveLength(0);
    expect(screen.queryByRole('tab')).not.toBeInTheDocument();
    expect(screen.queryByTestId('gap-bar')).not.toBeInTheDocument();
  };

  it('a 404 from the Procedures route is the not-available message, with no rows', async () => {
    renderPanel({ procedures: () => problem(404, null) });
    const err = await screen.findByTestId('recordprocedures-error');
    expect(err).toHaveAttribute('data-error-kind', 'notAvailable');
    expect(err).toHaveTextContent('Procedures are not available for this record');
    NO_ROWS();
  });

  it('a 404 from the rules route alone is the SAME message and shows no Procedure rows either', async () => {
    renderPanel({ rules: () => problem(404, null) });
    const err = await screen.findByTestId('recordprocedures-error');
    expect(err).toHaveAttribute('data-error-kind', 'notAvailable');
    expect(err).toHaveTextContent('Procedures are not available for this record');
    NO_ROWS();
  });

  it('the 404 text is identical whichever route sent it, and carries no hint of the cause', async () => {
    const a = renderPanel({ procedures: () => problem(404, null) });
    const textA = (await screen.findByTestId('recordprocedures-error')).textContent;
    a.unmount();
    renderPanel({ rules: () => problem(404, null) });
    const textB = (await screen.findByTestId('recordprocedures-error')).textContent;
    expect(textA).toBe(textB);
    expect(textA).toBe('Procedures are not available for this recordThe record may not exist, or you may not have access to it.');
  });

  it('a 503 procedures.unavailable from the Procedures route says "Procedures cannot be shown right now", with no rows and a way to retry', async () => {
    renderPanel({ procedures: () => problem(503, 'procedures.unavailable') });
    const err = await screen.findByTestId('recordprocedures-error');
    expect(err).toHaveAttribute('data-error-kind', 'unavailable');
    expect(err).toHaveTextContent('Procedures cannot be shown right now');
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
    NO_ROWS();
  });

  it('a 503 from the rules route alone also shows no Procedure rows (the other route succeeded)', async () => {
    renderPanel({ rules: () => problem(503, 'procedures.unavailable') });
    await screen.findByTestId('recordprocedures-error');
    NO_ROWS();
  });

  it('after a failure the firm and date can still be changed; the new read replaces the message', async () => {
    renderPanel({ procedures: q => (q.firmId === OKAFOR.id ? matterProcedures : problem(503, 'procedures.unavailable')) });
    await screen.findByTestId('recordprocedures-error');
    expect(screen.getByLabelText('Work dated')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('combobox'));
    fireEvent.click(await screen.findByRole('option', { name: OKAFOR.name! }));
    await ready();
    expect(rowIds('procedure')).toHaveLength(6);
  });

  it('Retry reads both routes again', async () => {
    let n = 0;
    const { calls } = renderPanel({ procedures: () => (n++ === 0 ? problem(503, 'procedures.unavailable') : matterProcedures) });
    await screen.findByTestId('recordprocedures-error');
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    await ready();
    expect(calls.filter(c => c.leaf === 'procedures')).toHaveLength(2);
  });

  it('503 ebilling.disabled is a short eBilling-off message, with no rows and no pickers', async () => {
    renderPanel({ procedures: () => problem(503, 'ebilling.disabled'), rules: () => problem(503, 'ebilling.disabled') });
    const err = await screen.findByTestId('recordprocedures-error');
    expect(err).toHaveAttribute('data-error-kind', 'ebillingOff');
    expect(err).toHaveTextContent('eBilling is not enabled');
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
    NO_ROWS();
  });

  it('400, a network failure and a sign-in failure each show a message and no rows', async () => {
    for (const [err, kind] of [
      [problem(400, 'procedures.invalid_query'), 'invalidQuery'],
      [new TypeError('Failed to fetch'), 'network'],
      [new AuthError('no token', 'token_unavailable' as never), 'signin'],
    ] as const) {
      const r = renderPanel({ procedures: () => err });
      expect(await screen.findByTestId('recordprocedures-error')).toHaveAttribute('data-error-kind', kind);
      NO_ROWS();
      r.unmount();
    }
  });

  it('a sign-in failure is its own standalone state (no pickers), says sign-in could not be verified, and Retry reads both routes again', async () => {
    let n = 0;
    const { calls } = renderPanel({ procedures: () => (n++ === 0 ? new AuthError('no token', 'token_unavailable' as never) : matterProcedures) });
    const err = await screen.findByTestId('recordprocedures-error');
    expect(err).toHaveAttribute('data-error-kind', 'signin');
    expect(err).toHaveTextContent('Unable to load');
    expect(err).toHaveTextContent('Sign-in could not be verified');
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
    NO_ROWS();

    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    await ready();
    expect(calls.filter(c => c.leaf === 'procedures')).toHaveLength(2);
    expect(calls.filter(c => c.leaf === 'rules-in-force')).toHaveLength(2);
    expect(rowIds('procedure')).toHaveLength(6);
  });

  describe('ready, then a change whose re-read fails: neither tab shows rows', () => {
    const CHANGE_DATE = '2025-06-30';
    const triggers = {
      'a firm change': {
        fails: (q: Query) => q.firmId === OKAFOR.id,
        change: async () => {
          fireEvent.click(screen.getByRole('combobox'));
          fireEvent.click(await screen.findByRole('option', { name: OKAFOR.name! }));
        },
      },
      'a date change': {
        fails: (q: Query) => q.date === CHANGE_DATE,
        change: async () => {
          fireEvent.change(screen.getByLabelText('Work dated'), { target: { value: CHANGE_DATE } });
        },
      },
    } as const;
    const routes = {
      'both routes fail': { procedures: true, rules: true },
      'only the Procedures route fails': { procedures: true, rules: false },
      'only the rules route fails': { procedures: false, rules: true },
    } as const;

    for (const [triggerName, trigger] of Object.entries(triggers)) {
      for (const [routeName, failing] of Object.entries(routes)) {
        for (const startTab of ['Procedures', 'Rules in force'] as const) {
          it(`${triggerName}, ${routeName}, from the ${startTab} tab: 503 procedures.unavailable shows no rows on either tab`, async () => {
            const { calls } = renderPanel({
              procedures: q => (failing.procedures && trigger.fails(q) ? problem(503, 'procedures.unavailable') : matterProcedures),
              rules: q => (failing.rules && trigger.fails(q) ? problem(503, 'procedures.unavailable') : matterRules),
            });
            await ready();
            expect(rowIds('procedure')).toHaveLength(6);
            fireEvent.click(screen.getByRole('tab', { name: new RegExp(startTab) }));
            const before = calls.length;

            await trigger.change();
            const err = await screen.findByTestId('recordprocedures-error');
            expect(err).toHaveAttribute('data-error-kind', 'unavailable');
            // Both routes were read again for the change, whichever one failed.
            expect(calls.slice(before).map(c => c.leaf).sort()).toEqual(['procedures', 'rules-in-force']);
            // No tab at all, so no way to reach rows on the other one; no row of either kind is in the page.
            NO_ROWS();
            expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
          });
        }
      }
    }
  });

  describe('out-of-order responses: the newer read wins', () => {
    interface Deferred {
      query: Query;
      resolve: (body: unknown) => void;
    }
    /** The first read (mount) answers at once with the matter lists; every later read waits for the test to answer it. */
    function deferredPanel() {
      const procs: Deferred[] = [];
      const rules: Deferred[] = [];
      let first = { procedures: true, rules: true };
      const later = (list: Deferred[], q: Query) =>
        new Promise<unknown>(resolve => {
          list.push({ query: q, resolve });
        }) as never;
      const view = renderPanel({
        procedures: q => {
          if (first.procedures) {
            first = { ...first, procedures: false };
            return matterProcedures;
          }
          return later(procs, q);
        },
        rules: q => {
          if (first.rules) {
            first = { ...first, rules: false };
            return matterRules;
          }
          return later(rules, q);
        },
      });
      const settle = (ms = 25) => act(async () => new Promise(r => setTimeout(r, ms)));
      return { ...view, procs, rules, settle };
    }

    it('after two date changes, the newer (resolved first) rows stay when the older response arrives later', async () => {
      const d = deferredPanel();
      await ready();

      fireEvent.change(screen.getByLabelText('Work dated'), { target: { value: '2025-06-30' } });
      await waitFor(() => expect(d.procs).toHaveLength(1));
      fireEvent.change(screen.getByLabelText('Work dated'), { target: { value: '2025-07-31' } });
      await waitFor(() => expect(d.procs).toHaveLength(2));
      await waitFor(() => expect(d.rules).toHaveLength(2));
      const [olderP, newerP] = d.procs;
      const [olderR, newerR] = d.rules;
      expect([olderP.query.date, newerP.query.date]).toEqual(['2025-06-30', '2025-07-31']);

      // The NEWER request answers first (the project lists), then the OLDER one (the matter lists).
      await act(async () => {
        newerP.resolve(projectProcedures);
        newerR.resolve(projectRules);
      });
      await ready();
      expect(rowIds('procedure')).toEqual(['p-ocg', 'p-rate']);

      await act(async () => {
        olderP.resolve(matterProcedures);
        olderR.resolve(matterRules);
      });
      await d.settle();
      expect(rowIds('procedure')).toEqual(['p-ocg', 'p-rate']);
      expect(screen.getByRole('tab', { name: 'Procedures (2)' })).toBeInTheDocument();
      fireEvent.click(screen.getByRole('tab', { name: /Rules in force/ }));
      expect(rowIds('rule')).toEqual(['r-block', 'r-rush-rec']);
      expect(screen.getByLabelText('Work dated')).toHaveValue('2025-07-31');
    });

    it('after a firm change and a change back to the billing firm, the newer (billing firm) rows stay when the older (Okafor) response arrives later', async () => {
      const d = deferredPanel();
      await ready();

      fireEvent.click(screen.getByRole('combobox'));
      fireEvent.click(await screen.findByRole('option', { name: OKAFOR.name! }));
      await waitFor(() => expect(d.procs).toHaveLength(1));
      fireEvent.click(screen.getByRole('combobox'));
      fireEvent.click(await screen.findByRole('option', { name: /billing firm/ }));
      await waitFor(() => expect(d.procs).toHaveLength(2));
      await waitFor(() => expect(d.rules).toHaveLength(2));
      const [olderP, newerP] = d.procs;
      const [olderR, newerR] = d.rules;
      expect([olderP.query.firmId, newerP.query.firmId]).toEqual([OKAFOR.id, FENWICK.id.toLowerCase()]);

      await act(async () => {
        newerP.resolve(matterProcedures);
        newerR.resolve(matterRules);
      });
      await ready();
      expect(rowIds('procedure')).toHaveLength(6);

      // The older (Okafor) answer is the project lists; it must not replace the billing firm's rows.
      await act(async () => {
        olderP.resolve(projectProcedures);
        olderR.resolve(projectRules);
      });
      await d.settle();
      expect(rowIds('procedure')).toHaveLength(6);
      expect(screen.getByRole('tab', { name: 'Procedures (6)' })).toBeInTheDocument();
      expect(screen.getByRole('combobox')).toHaveValue('Fenwick & Calder LLP (billing firm)');
    });

    it('an older response that arrives late, in either order of its two routes, is ignored', async () => {
      const d = deferredPanel();
      await ready();
      fireEvent.change(screen.getByLabelText('Work dated'), { target: { value: '2025-06-30' } });
      await waitFor(() => expect(d.procs).toHaveLength(1));
      fireEvent.change(screen.getByLabelText('Work dated'), { target: { value: '2025-07-31' } });
      await waitFor(() => expect(d.rules).toHaveLength(2));
      const [olderP, newerP] = d.procs;
      const [olderR, newerR] = d.rules;

      await act(async () => {
        newerR.resolve(projectRules);
        newerP.resolve(projectProcedures);
      });
      await ready();
      await act(async () => {
        olderR.resolve(matterRules);
      });
      await act(async () => {
        olderP.resolve(matterProcedures);
      });
      await d.settle();
      expect(rowIds('procedure')).toEqual(['p-ocg', 'p-rate']);
    });

    it('an older FAILURE that arrives after the newer success does not replace the rows with an error', async () => {
      const d = deferredPanel();
      await ready();
      fireEvent.change(screen.getByLabelText('Work dated'), { target: { value: '2025-06-30' } });
      await waitFor(() => expect(d.procs).toHaveLength(1));
      fireEvent.change(screen.getByLabelText('Work dated'), { target: { value: '2025-07-31' } });
      await waitFor(() => expect(d.rules).toHaveLength(2));
      const [olderP] = d.procs;
      const [olderR, newerR] = d.rules;
      const newerP = d.procs[1];

      await act(async () => {
        newerP.resolve(projectProcedures);
        newerR.resolve(projectRules);
      });
      await ready();
      // The older read answers with a body that is not the view (a failure) and an ok body.
      await act(async () => {
        olderP.resolve({ unexpected: true });
        olderR.resolve(matterRules);
      });
      await d.settle();
      expect(screen.queryByTestId('recordprocedures-error')).not.toBeInTheDocument();
      expect(rowIds('procedure')).toEqual(['p-ocg', 'p-rate']);
    });
  });

  it('a body that is not the view is a failure, not an empty list', async () => {
    renderPanel({ procedures: () => ({ unexpected: true }) });
    await screen.findByTestId('recordprocedures-error');
    NO_ROWS();
    expect(screen.queryByTestId('procedure-empty')).not.toBeInTheDocument();
  });

  it('shows a spinner while the reads are in flight, and no rows', async () => {
    renderPanel({ procedures: () => new Promise(() => undefined) as never });
    expect(await screen.findByTestId('recordprocedures-loading')).toBeInTheDocument();
    expect(screen.queryAllByTestId(/^(procedure|rule)-row-/)).toHaveLength(0);
  });

  it('while a new firm is being read the old rows are gone (no stale list under the new firm)', async () => {
    renderPanel({ procedures: q => (q.firmId === OKAFOR.id ? (new Promise(() => undefined) as never) : matterProcedures) });
    await ready();
    fireEvent.click(screen.getByRole('combobox'));
    fireEvent.click(await screen.findByRole('option', { name: OKAFOR.name! }));
    expect(await screen.findByTestId('recordprocedures-loading')).toBeInTheDocument();
    expect(screen.queryAllByTestId(/^procedure-row-/)).toHaveLength(0);
  });
});

describe('empty lists', () => {
  it('a record with no Procedures says so (a success, not a failure)', async () => {
    renderPanel({ procedures: () => ({ ...matterProcedures, procedures: [], gaps: [] }), rules: () => ({ ...matterRules, rules: [], gaps: [] }) });
    await ready();
    expect(screen.getByTestId('procedure-empty')).toHaveTextContent('No Procedures apply to this record for this firm and date.');
    expect(screen.getByRole('tab', { name: 'Procedures (0)' })).toBeInTheDocument();
    // "All" stays enabled at 0; the others are disabled.
    expect(card('All, 0 Procedures')).not.toHaveAttribute('aria-disabled', 'true');
  });
});

describe('read-only', () => {
  it('no row has a button, a link or a tab stop, and a row click opens nothing and calls nothing', async () => {
    const { calls } = renderPanel({ kind: 'workassignment', procedures: () => waProcedures, rules: () => waRules });
    await ready();
    const before = calls.length;

    const procedureTable = screen.getByTestId('procedure-list');
    expect(within(procedureTable).queryAllByRole('button')).toHaveLength(0);
    expect(within(procedureTable).queryAllByRole('link')).toHaveLength(0);
    for (const row of screen.getAllByTestId(/^procedure-row-/)) {
      expect(row).not.toHaveAttribute('tabindex');
      fireEvent.click(row);
      fireEvent.keyDown(row, { key: 'Enter' });
    }
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('tab', { name: /Rules in force/ }));
    const ruleTable = screen.getByTestId('rule-list');
    expect(within(ruleTable).queryAllByRole('button')).toHaveLength(0);
    expect(within(ruleTable).queryAllByRole('link')).toHaveLength(0);
    for (const row of screen.getAllByTestId(/^rule-row-/)) {
      expect(row).not.toHaveAttribute('tabindex');
      fireEvent.click(row);
    }
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(calls.length).toBe(before);
  });

  it('every request is a GET of the two read routes: no 089 route (assignments, overrides, previews, deletes) is ever called', async () => {
    const { calls, mock } = renderPanel();
    await ready();
    // Use every control on the page.
    fireEvent.click(card('Pinned, 1 Procedure'));
    fireEvent.click(screen.getByRole('tab', { name: /Rules in force/ }));
    fireEvent.click(card('Changed here, 1 rule'));
    fireEvent.change(screen.getByLabelText('Work dated'), { target: { value: '2025-06-30' } });
    await waitFor(() => expect(calls.length).toBe(4));
    fireEvent.click(screen.getByRole('combobox'));
    fireEvent.click(await screen.findByRole('option', { name: OKAFOR.name! }));
    await waitFor(() => expect(calls.length).toBe(6));

    for (const [url, init] of mock.mock.calls) {
      expect(init).toEqual({ method: 'GET' });
      expect(new URL(url).pathname).toMatch(/^\/api\/finance\/(matters|projects|workassignments)\/[0-9a-f-]{36}\/(procedures|rules-in-force)$/);
      expect(url).not.toMatch(/procedure-assignments|rule-overrides|overrides|removal-preview/);
    }
  });

  it('the page sources have no 089 route, no write verb and no Dataverse write call (control: the scan sees one)', () => {
    const root = path.join(__dirname, '..');
    const files = ['main.tsx', 'components/ProceduresPanel.tsx', 'components/ProcedureList.tsx', 'components/RuleList.tsx', 'services/recordProceduresApi.ts', 'services/firmChoices.ts', 'services/model.ts'];
    const forbidden = /procedure-assignments|rule-overrides|removal-preview|\/overrides|method:\s*['"](POST|PUT|PATCH|DELETE)['"]|\.(createRecord|updateRecord|deleteRecord)\(/;
    for (const f of files) expect([f, forbidden.test(fs.readFileSync(path.join(root, f), 'utf8'))]).toEqual([f, false]);
    expect(forbidden.test("fetch(url, { method: 'POST' })")).toBe(true);
    expect(forbidden.test('/matters/1/procedure-assignments')).toBe(true);
    expect(forbidden.test('webApi.createRecord(x)')).toBe(true);
  });

  it('no Xrm read of the Procedure, override or rule tables (only sprk_invoice and sprk_organization are read)', () => {
    const source = fs.readFileSync(path.join(__dirname, '..', 'services/firmChoices.ts'), 'utf8');
    const entities = [...source.matchAll(/retrieveMultipleRecords\(\s*'([a-z_]+)'/g)].map(m => m[1]).sort();
    expect(entities).toEqual(['sprk_invoice', 'sprk_organization']);
    expect(source).not.toMatch(/sprk_procedure|sprk_policy|sprk_ruleset|sprk_procedureassignment/);
  });
});

describe('marks and wording', () => {
  it('an UNKNOWN mark kind is a neutral badge with its own text; once the kind is in the dimmed set (084e) the row dims', async () => {
    const rules = {
      ...matterRules,
      rules: [{ ...matterRules.rules[0], marks: [mark('Outranked', { reason: 'x' })] }, ...matterRules.rules.slice(1)],
    };
    renderPanel({ rules: () => rules });
    await ready();
    fireEvent.click(screen.getByRole('tab', { name: /Rules in force/ }));

    const row = screen.getByTestId('rule-row-r-block');
    const badge = within(row).getByTestId('status-badge');
    expect(badge).toHaveTextContent('Outranked');
    expect(badge).toHaveAttribute('data-tone', 'neutral');
    expect(row).toHaveAttribute('data-dimmed', 'false');
  });

  it('the dimmed set decides which rows dim (a new kind is a one-line change)', async () => {
    const set = DIMMED_MARK_KINDS as Set<string>;
    set.add('Outranked');
    try {
      const rules = { ...matterRules, rules: [{ ...matterRules.rules[0], marks: [mark('Outranked')] }] };
      renderPanel({ rules: () => rules });
      await ready();
      fireEvent.click(screen.getByRole('tab', { name: /Rules in force/ }));
      expect(screen.getByTestId('rule-row-r-block')).toHaveAttribute('data-dimmed', 'true');
    } finally {
      set.delete('Outranked');
    }
  });

  it('the word "displaced" is never shown, on any of the three records', async () => {
    for (const [kind, p, r] of [
      ['matter', matterProcedures, matterRules],
      ['project', projectProcedures, projectRules],
      ['workassignment', waProcedures, waRules],
    ] as const) {
      const view = renderPanel({ kind, procedures: () => p, rules: () => r });
      await ready();
      expect(view.container.textContent).not.toMatch(/displaced/i);
      fireEvent.click(screen.getByRole('tab', { name: /Rules in force/ }));
      expect(view.container.textContent).not.toMatch(/displaced/i);
      view.unmount();
    }
  });
});

describe('theme', () => {
  it('renders in the dark theme with the same rows', async () => {
    renderPanel({ theme: webDarkTheme });
    await ready();
    expect(rowIds('procedure')).toHaveLength(6);
    fireEvent.click(screen.getByRole('tab', { name: /Rules in force/ }));
    expect(rowIds('rule')).toHaveLength(4);
  });

  it('the host FluentProvider decides the colours: the page carries no colour literal (control: the scan sees one)', () => {
    const colour = /#[0-9a-fA-F]{3,8}\b|\brgba?\(|\bhsla?\(/;
    const read = (rel: string) => fs.readFileSync(path.join(__dirname, '..', rel), 'utf8');
    for (const f of ['main.tsx', 'components/ProceduresPanel.tsx', 'components/ProcedureList.tsx', 'components/RuleList.tsx']) {
      expect([f, colour.test(read(f))]).toEqual([f, false]);
    }
    expect(colour.test('color: #fff;')).toBe(true);
    expect(colour.test('background: rgb(0, 0, 0)')).toBe(true);
    expect(colour.test('color: tokens.colorNeutralForeground1')).toBe(false);
  });
});
