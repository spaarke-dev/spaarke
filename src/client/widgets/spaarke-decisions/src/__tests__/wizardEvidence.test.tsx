/**
 * What the widget's wizard says about an item's evidence (task 032 fix round), with the whole widget over a host double:
 *
 *  - "Open in Spaarke" on an evidence line whose ref names a record: the host is asked (`openLink`) to open the Console's own record form
 *    for that table and id. Never for a line that names no record or that the server withheld; never for the matter (the lane's core type
 *    is a placeholder, so the subject stays plain text); never without a usable Console address
 *  - the plan tool bounds a row's evidence: "Showing N of M evidence lines." with a link to the Console, instead of the first lines
 *    standing for all of them
 */
import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { restRow, wizardHost } from './wizardFixtures';
import { CONSOLE_BASE } from './laneFixtures';
import { LANE_OPEN_KEY, mount, ONE_CLICK_PLAN, openItem } from './wizardHarness';

const COMM = 'aaaaaaaa-0000-4000-8000-000000000001';
const MATTER = 'bbbbbbbb-0000-4000-8000-000000000002';
const COMMUNICATION_URL = `${CONSOLE_BASE}/main.aspx?etn=sprk_communication&id=${COMM}&pagetype=entityrecord`;
const CONSOLE_PAGE = `${CONSOLE_BASE}/main.aspx?pagetype=webresource&webresourceName=sprk_spaarkeai`;

const EVIDENCE = [
  {
    kind: 'communication',
    ref: `sprk_communication:${COMM}`,
    tier: 'Fact',
    text: 'Invoice 4411 dated 3 Oct 2026',
    source: 'Email',
  },
  { kind: 'snapshot', ref: 'c-9', tier: 'Fact', text: 'A line that names no record' },
  { kind: 'matter', ref: `sprk_matter:${MATTER}`, tier: 'Fact', text: null, withheld: true },
];

const MODES: ReadonlyArray<readonly [string, 'fullscreen' | 'inline']> = [
  ['the dialog', 'fullscreen'],
  ['master-detail', 'inline'],
];
const openLinks = () => screen.queryAllByRole('button', { name: /^open in spaarke/i });

beforeEach(() => {
  window.localStorage.clear();
  window.localStorage.setItem(LANE_OPEN_KEY, JSON.stringify({ Decide: true }));
});

describe('Open in Spaarke on an evidence line', () => {
  it.each(MODES)(
    '%s: a line that names a record opens its Console form through the host, and only that line',
    async (_n, mode) => {
      const host = wizardHost([{ n: 1, plan: ONE_CLICK_PLAN, evidence: EVIDENCE }], { displayMode: mode });
      mount(host);
      await openItem(1);

      expect(openLinks()).toHaveLength(1);
      fireEvent.click(screen.getByRole('button', { name: 'Open in Spaarke: Invoice 4411 dated 3 Oct 2026' }));

      await waitFor(() => expect(host.openLink).toHaveBeenCalledWith(COMMUNICATION_URL));
      expect(host.openLink).toHaveBeenCalledTimes(1);
      // The wizard keeps its place and its choices.
      expect(screen.getByTestId('step-found')).toBeInTheDocument();
    }
  );

  it('the matter in the heading stays plain text: the lane names no table for it, so none is invented', async () => {
    const host = wizardHost([{ n: 1, plan: ONE_CLICK_PLAN, evidence: EVIDENCE }], { displayMode: 'fullscreen' });
    mount(host);
    await openItem(1);
    expect(within(screen.getByTestId('found-meta')).queryByRole('button')).toBeNull();
    expect(within(screen.getByTestId('found-meta')).queryByRole('link')).toBeNull();
    expect(host.openLink).not.toHaveBeenCalled();
  });

  it('without a usable Console address no line is a link', async () => {
    const host = wizardHost([{ n: 1, plan: ONE_CLICK_PLAN, evidence: EVIDENCE }], { displayMode: 'fullscreen' });
    mount(host, { toolResult: {} });
    await openItem(1);
    expect(openLinks()).toHaveLength(0);
    expect(screen.getAllByTestId('evidence-line')).toHaveLength(3);
  });
});

describe('Showing N of M evidence lines', () => {
  it.each(MODES)(
    '%s: a bounded row says how many lines the item stores, and links to the Console',
    async (_n, mode) => {
      const host = wizardHost([{ n: 1, plan: ONE_CLICK_PLAN, evidence: EVIDENCE.slice(0, 2), evidenceStored: 130 }], {
        displayMode: mode,
      });
      mount(host);
      await openItem(1);

      expect(screen.getByTestId('evidence-truncated')).toHaveTextContent('Showing 2 of 130 evidence lines.');
      fireEvent.click(screen.getByRole('button', { name: 'Open in Spaarke' }));
      await waitFor(() => expect(host.openLink).toHaveBeenCalledWith(CONSOLE_PAGE));
    }
  );

  it('a row inside the bounds says nothing, and a bounded row with no count still says some lines are not shown', async () => {
    const whole = wizardHost([{ n: 1, plan: ONE_CLICK_PLAN, evidence: EVIDENCE }], { displayMode: 'fullscreen' });
    const first = mount(whole);
    await openItem(1);
    expect(screen.queryByTestId('evidence-truncated')).toBeNull();
    first.unmount();

    const noCount = wizardHost([{ n: 1, plan: ONE_CLICK_PLAN, evidence: EVIDENCE.slice(0, 2) }], {
      displayMode: 'fullscreen',
      plan: (signalId, item) => ({
        structuredContent: {
          plan: { ...item.plan, signalId },
          suppression: null,
          row: restRow(item),
          evidenceTruncated: true,
        },
      }),
    });
    mount(noCount);
    await openItem(1);
    expect(screen.getByTestId('evidence-truncated')).toHaveTextContent(
      'Showing the first 2 evidence lines; more are not shown.'
    );
  });
});
