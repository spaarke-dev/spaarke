/**
 * DecisionRecordsWidget - unit tests (task 045, D-47). The stated contract:
 *  - the tab lists the decided items with their Decision Record, newest decision first
 *  - a class filter (All / Judgements / Routine / Dismissals) shows only records of that class
 *  - Open launches the decision wizard on that item, and the wizard's item is READ-ONLY and replays the record's steps (#1959 item 2)
 *  - negative: no row offers edit or delete (FR-21, R-13); the only action is Open
 *  - negative: the tab reads nothing but the worklist service (no Xrm, no table name); no colour literal (ADR-021)
 *  - a failed read says so (Try again), a 403 says the account has no access and offers no retry; the last list is kept on a failed refresh
 */

import '@testing-library/jest-dom';
import * as fs from 'fs';
import * as path from 'path';
import * as React from 'react';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';

import { DecisionRecordsWidget } from '..';
import { WorklistRequestError, parseWorklistResponse } from '../../Worklist/worklistService';
import type { WorklistService } from '../../Worklist/worklistService';
import type { DecisionWizardLaunchRequest } from '../../DecisionWizard/decisionWizardLauncher';
import { hasRecordedDecision } from '../../DecisionWizard/DecisionItemWizard';

const MATTER = {
  recordType: 'sprk_matter',
  recordId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
  typeLabel: 'Matter',
  name: 'Acme v. Beta',
  number: 'MTR-1',
};

const STEPS = [
  { code: 'reassign', taken: true, values: { person: 'p-1', note: 'over to Sam' }, status: 'Done' },
  { code: 'extend', taken: false, values: null, status: 'Skipped' },
];

function item(id: string, cls: string | null, number: string, decidedOn: string, extra: Record<string, unknown> = {}) {
  return {
    signalId: id,
    lane: 'Decide',
    status: 'Resolved',
    title: `Item ${number}`,
    resolvedOn: decidedOn,
    resolutionType: cls === 'Dismissal' ? 100000001 : 100000000,
    decision: {
      recordId: `rec-${number}`,
      number,
      recordClass: cls,
      outcome: cls === 'Dismissal' ? 'Dismissed' : 'Authorized',
      decidedOn,
      decidedByName: 'Dana Decider',
      reason: 'Agreed with the firm',
      steps: STEPS,
      followOns: [],
    },
    ...extra,
  };
}

const BODY = {
  set: 'decided',
  lanes: [
    {
      lane: 'Decide',
      groups: [
        {
          core: MATTER,
          notFiled: false,
          items: [
            item('s-old', 'Judgement', 'DR-00001', '2026-10-01T09:00:00Z'),
            item('s-routine', 'Routine', 'DR-00002', '2026-10-03T09:00:00Z'),
            item('s-dismiss', 'Dismissal', 'DR-00003', '2026-10-05T09:00:00Z'),
            item('s-new', 'Judgement', 'DR-00004', '2026-10-09T09:00:00Z'),
          ],
        },
      ],
    },
    { lane: 'Do', groups: [] },
  ],
};

const serviceFor = (read: WorklistService['read']): WorklistService => ({
  read,
  findInquiryForTodo: jest.fn(async () => null),
  recordDisposition: jest.fn(async () => ({ todoCompleted: true })),
});
const okService = (body: unknown = BODY) => serviceFor(jest.fn(async set => parseWorklistResponse(body, set)));

type Launch = jest.Mock<Promise<{ opened: boolean; reason?: 'no-host' | 'busy' }>, [DecisionWizardLaunchRequest]>;
const launcher = (): Launch => jest.fn(async () => ({ opened: true }));

function renderTab(service: WorklistService, launchWizard: Launch = launcher(), dark = false) {
  render(
    <FluentProvider theme={dark ? webDarkTheme : webLightTheme}>
      <DecisionRecordsWidget service={service} launchWizard={launchWizard} />
    </FluentProvider>
  );
  return launchWizard;
}

const rowNumbers = () => screen.queryAllByTestId('record-row').map(r => /DR-\d+/.exec(r.textContent ?? '')?.[0] ?? '?');

describe('the Decision Record tab', () => {
  it('reads the decided set and lists the records newest first', async () => {
    const service = okService();
    renderTab(service);
    await screen.findByTestId('decision-records-widget');
    expect(service.read).toHaveBeenCalledWith('decided');
    expect(rowNumbers()).toEqual(['DR-00004', 'DR-00003', 'DR-00002', 'DR-00001']);
    // The row shows the core record, the item, who decided and the class.
    const first = screen.getAllByTestId('record-row')[0];
    expect(first).toHaveTextContent('Acme v. Beta - Item DR-00004');
    expect(first).toHaveTextContent('Dana Decider');
    expect(first).toHaveTextContent('Judgement');
  });

  it('filters by record class: only records of that class show, and the counts describe the full list', async () => {
    const user = userEvent.setup();
    renderTab(okService());
    await screen.findByTestId('decision-records-widget');
    expect(screen.getByTestId('decision-filter-all')).toHaveTextContent('All (4)');
    expect(screen.getByTestId('decision-filter-Judgement')).toHaveTextContent('Judgements (2)');

    await user.click(screen.getByTestId('decision-filter-Dismissal'));
    expect(rowNumbers()).toEqual(['DR-00003']);

    await user.click(screen.getByTestId('decision-filter-Routine'));
    expect(rowNumbers()).toEqual(['DR-00002']);

    await user.click(screen.getByTestId('decision-filter-Judgement'));
    expect(rowNumbers()).toEqual(['DR-00004', 'DR-00001']);

    await user.click(screen.getByTestId('decision-filter-all'));
    expect(rowNumbers()).toHaveLength(4);
  });

  it('a class with no records says so instead of showing the other classes', async () => {
    const user = userEvent.setup();
    const only = {
      ...BODY,
      lanes: [
        {
          lane: 'Decide',
          groups: [
            { core: MATTER, notFiled: false, items: [item('s1', 'Judgement', 'DR-00009', '2026-10-09T09:00:00Z')] },
          ],
        },
        BODY.lanes[1],
      ],
    };
    renderTab(okService(only));
    await screen.findByTestId('decision-records-widget');
    await user.click(screen.getByTestId('decision-filter-Dismissal'));
    expect(screen.queryAllByTestId('record-row')).toHaveLength(0);
    expect(screen.getByTestId('decision-records-empty')).toHaveTextContent('No dismissals');
  });

  it('a record of an unknown class shows under All only', async () => {
    const user = userEvent.setup();
    const odd = {
      ...BODY,
      lanes: [
        {
          lane: 'Decide',
          groups: [
            { core: MATTER, notFiled: false, items: [item('s1', 'Mystery', 'DR-00077', '2026-10-09T09:00:00Z')] },
          ],
        },
        BODY.lanes[1],
      ],
    };
    renderTab(okService(odd));
    await screen.findByTestId('decision-records-widget');
    expect(rowNumbers()).toEqual(['DR-00077']);
    await user.click(screen.getByTestId('decision-filter-Judgement'));
    expect(rowNumbers()).toEqual([]);
  });

  it('Open launches the wizard on that item, and the wizard item is read-only and replays the record', async () => {
    const user = userEvent.setup();
    const launch = renderTab(okService());
    await screen.findByTestId('decision-records-widget');
    await user.click(within(screen.getAllByTestId('record-row')[1]).getByText(/Open/));

    expect(launch).toHaveBeenCalledTimes(1);
    const request = launch.mock.calls[0][0];
    expect(request.itemId).toBe('s-dismiss');
    expect(request.core).toEqual({ recordType: 'sprk_matter', recordId: MATTER.recordId });
    expect(request.visibleList).toEqual(['s-new', 's-dismiss', 's-routine', 's-old']);

    const wizardItem = await request.loadItem!('s-dismiss');
    expect(wizardItem.resolution).not.toBeNull();
    expect(wizardItem.resolution?.resolutionType).toBe('Dismissed');
    expect(wizardItem.resolution?.recordNumber).toBe('DR-00003');
    expect(wizardItem.resolution?.decidedByName).toBe('Dana Decider');
    expect(wizardItem.resolution?.reason).toBe('Agreed with the firm');
    expect(wizardItem.resolution?.steps?.map(s => [s.code, s.taken])).toEqual([
      ['reassign', true],
      ['extend', false],
    ]);
    expect(hasRecordedDecision(wizardItem.resolution)).toBe(true);
    expect(wizardItem.otherOpenItems).toEqual([]);
  });

  it('the loader refuses an item that is not on the list', async () => {
    const user = userEvent.setup();
    const launch = renderTab(okService());
    await screen.findByTestId('decision-records-widget');
    await user.click(within(screen.getAllByTestId('record-row')[0]).getByText(/Open/));
    await expect(launch.mock.calls[0][0].loadItem!('nope')).rejects.toThrow(/no longer on the list/);
  });

  it('NEGATIVE: no row offers edit or delete; the only action is Open', async () => {
    renderTab(okService());
    await screen.findByTestId('decision-records-widget');
    const rows = screen.getAllByTestId('record-row');
    for (const row of rows) {
      expect(within(row).queryByRole('button', { name: /edit|delete|remove|change|save/i })).toBeNull();
      expect(within(row).queryByRole('menu')).toBeNull();
      // RecordRow's Open is a Fluent Link without an href, which renders as a button: it is the row's only control.
      expect(
        within(row)
          .queryAllByRole('button')
          .map(b => b.textContent?.trim())
      ).toEqual(['Open ›']);
      expect(within(row).queryAllByRole('link')).toHaveLength(0);
    }
    expect(screen.queryByRole('button', { name: /edit|delete|remove/i })).toBeNull();
  });

  it('says when the wizard host is missing, and when a decision is already open', async () => {
    const user = userEvent.setup();
    const launch: Launch = jest.fn(async () => ({ opened: false, reason: 'no-host' as const }));
    renderTab(okService(), launch);
    await screen.findByTestId('decision-records-widget');
    await user.click(within(screen.getAllByTestId('record-row')[0]).getByText(/Open/));
    expect(await screen.findByTestId('decision-records-notice')).toHaveTextContent('not available in this view');

    launch.mockResolvedValueOnce({ opened: false, reason: 'busy' });
    await user.click(within(screen.getAllByTestId('record-row')[0]).getByText(/Open/));
    await waitFor(() => expect(screen.getByTestId('decision-records-notice')).toHaveTextContent('already open'));
  });

  it('a launcher that throws is reported, not swallowed', async () => {
    const user = userEvent.setup();
    const launch: Launch = jest.fn(async () => {
      throw new Error('boom');
    });
    renderTab(okService(), launch);
    await screen.findByTestId('decision-records-widget');
    await user.click(within(screen.getAllByTestId('record-row')[0]).getByText(/Open/));
    expect(await screen.findByTestId('decision-records-notice')).toHaveTextContent('could not be opened');
  });

  it('shows the empty state when there are no decisions', async () => {
    renderTab(
      okService({
        set: 'decided',
        lanes: [
          { lane: 'Decide', groups: [] },
          { lane: 'Do', groups: [] },
        ],
      })
    );
    expect(await screen.findByTestId('decision-records-empty')).toHaveTextContent('No decisions in the last 90 days.');
  });

  it('an item without a Decision Record is not listed (the route returns none, and the tab never invents one)', async () => {
    const bare = {
      ...BODY,
      lanes: [
        {
          lane: 'Decide',
          groups: [{ core: MATTER, notFiled: false, items: [{ signalId: 's-x', lane: 'Decide', title: 'No record' }] }],
        },
        BODY.lanes[1],
      ],
    };
    renderTab(okService(bare));
    expect(await screen.findByTestId('decision-records-empty')).toBeInTheDocument();
    expect(screen.queryAllByTestId('record-row')).toHaveLength(0);
  });

  it('a failed read shows the route message and Try again reads it again', async () => {
    const user = userEvent.setup();
    const read = jest
      .fn<ReturnType<WorklistService['read']>, Parameters<WorklistService['read']>>()
      .mockRejectedValueOnce(new WorklistRequestError('The worklist could not be read.', 503))
      .mockImplementation(async set => parseWorklistResponse(BODY, set));
    renderTab(serviceFor(read));
    expect(await screen.findByTestId('decision-records-error')).toHaveTextContent('could not be read');
    await user.click(screen.getByRole('button', { name: 'Try again' }));
    await screen.findByTestId('decision-records-widget');
    expect(read).toHaveBeenCalledTimes(2);
  });

  it('a 403 says the account has no access and offers no retry', async () => {
    renderTab(serviceFor(jest.fn(async () => Promise.reject(new WorklistRequestError('x', 403)))));
    const bar = await screen.findByTestId('decision-records-error');
    expect(bar).toHaveTextContent("aren't available for your account");
    expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull();
  });

  it('renders under the dark theme', async () => {
    renderTab(okService(), launcher(), true);
    await screen.findByTestId('decision-records-widget');
    expect(rowNumbers()).toHaveLength(4);
  });
});

describe('sources', () => {
  const dir = path.join(__dirname, '..');
  const files = fs
    .readdirSync(dir)
    .filter(f => /\.(ts|tsx)$/.test(f))
    .map(f => path.join(dir, f));
  const code = (f: string) =>
    fs
      .readFileSync(f, 'utf8')
      .replace(/\/\*[\s\S]*?\*\//g, '')
      .replace(/^\s*\/\/.*$/gm, '');

  it('contains no hex, rgb/hsl or named colour literal (ADR-021)', () => {
    const literal =
      /#[0-9a-fA-F]{3,8}\b|\brgba?\(|\bhsla?\(|:\s*['"](?:red|green|blue|black|white|gray|grey|orange|yellow)['"]/;
    expect(files.length).toBeGreaterThan(0);
    for (const f of files) expect({ f, hit: literal.test(code(f)) }).toEqual({ f, hit: false });
  });

  it('reads nothing itself: no Xrm, no Web API, no table name (D-15)', () => {
    for (const f of files) {
      expect({ f, hit: /Xrm|WebApi|sprk_signal|sprk_decisionrecord|fetch\(/.test(code(f)) }).toEqual({ f, hit: false });
    }
  });
});
