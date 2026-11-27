/**
 * "Done today" on the All card (DEF-001 #2832, spec FR-13). The server gives `closedToday` (a count per category key, `all` = the Decide
 * lane's total, the Console's figure) in the structured content of `spaarke_show_decisions`; the shell passes `closedToday.all` to the lane.
 *
 *  - the helper reads `closedToday.all` and nothing else: a whole number of at least 0, else no value
 *  - the whole widget: the line "n of m done today" (m = n + what is open) shows when the opening result carries the figure, and is absent
 *    when it does not (an older server, or a server whose closed-today read did not answer)
 *  - a decision recorded in the widget hides the line from then on: the opening figure is not read again, so it would be wrong
 */
import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { doneTodayOf } from '../lane/doneToday';
import { COMPLETE_TOOL, wizardHost } from './wizardFixtures';
import { CONSOLE_BASE } from './laneFixtures';
import {
  LANE_OPEN_KEY,
  count,
  lane,
  mount,
  oneClickItems,
  openItem,
  recordButtonReady,
  toLastActionStep,
} from './wizardHarness';

beforeEach(() => {
  window.localStorage.clear();
  window.localStorage.setItem(LANE_OPEN_KEY, JSON.stringify({ Decide: true }));
});

describe('doneTodayOf', () => {
  it('reads closedToday.all, a whole number of at least 0', () => {
    expect(doneTodayOf({ closedToday: { all: 4, chaseReply: 1 } })).toBe(4);
    expect(doneTodayOf({ closedToday: { all: 0 } })).toBe(0);
  });

  it.each([
    ['no result yet', null],
    ['not an object', 'closedToday'],
    ['no closedToday (an older server)', { consoleBaseUrl: CONSOLE_BASE }],
    ['closedToday is null', { closedToday: null }],
    ['closedToday is not an object', { closedToday: 4 }],
    ['no all, only a category', { closedToday: { chaseReply: 2 } }],
    ['all is a string', { closedToday: { all: '4' } }],
    ['all is negative', { closedToday: { all: -1 } }],
    ['all is not whole', { closedToday: { all: 1.5 } }],
    ['all is not finite', { closedToday: { all: Number.POSITIVE_INFINITY } }],
  ])('is undefined (no line), not zero: %s', (_name, result) => {
    expect(doneTodayOf(result)).toBeUndefined();
  });
});

describe('the widget shows "done today" from the opening result', () => {
  it('says "n of m done today" on the All card when the result carries closedToday.all', async () => {
    mount(wizardHost(oneClickItems(2)), { toolResult: { consoleBaseUrl: CONSOLE_BASE, closedToday: { all: 4 } } });

    await screen.findByTestId('decide-lane');
    expect(within(lane()).getByText('4 of 7 done today')).toBeInTheDocument(); // 3 open, 4 done
  });

  it('shows 0 done as a count, not as a missing line', async () => {
    mount(wizardHost(oneClickItems(2)), { toolResult: { consoleBaseUrl: CONSOLE_BASE, closedToday: { all: 0 } } });

    await screen.findByTestId('decide-lane');
    expect(within(lane()).getByText('0 of 3 done today')).toBeInTheDocument();
  });

  it('shows no line when the result has no closedToday (an older server, or its closed-today read did not answer)', async () => {
    mount(wizardHost(oneClickItems(2)), { toolResult: { consoleBaseUrl: CONSOLE_BASE } });

    await screen.findByTestId('decide-lane');
    expect(count()).toHaveTextContent('3');
    expect(within(lane()).queryByText(/done today/)).not.toBeInTheDocument();
  });

  it('stops showing the line once a decision is recorded here (the opening figure would be wrong), and the row has left the lane', async () => {
    const host = wizardHost(oneClickItems(2));
    mount(host, { toolResult: { consoleBaseUrl: CONSOLE_BASE, closedToday: { all: 4 } } });
    await openItem(1);
    expect(within(lane()).getByText('4 of 7 done today')).toBeInTheDocument();

    fireEvent.click(await toLastActionStep());
    fireEvent.click(await recordButtonReady(/^record decision$/i));
    await waitFor(() => expect(host.callsTo(COMPLETE_TOOL)).toHaveLength(1));

    await waitFor(() => expect(count()).toHaveTextContent('2'));
    expect(within(lane()).queryByText(/done today/)).not.toBeInTheDocument();
  });
});
