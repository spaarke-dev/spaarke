/**
 * How the decision wizard opens and closes in the widget (task 032 fix round). Each case spies on the host: the commit tool, the paging tool
 * (a lane read) and the display-mode requests.
 *
 *  - Escape closes the wizard without writing (0 commits), reads the lane again in place and gives the focus back to the row, in the dialog
 *    and in master-detail; with choices made it asks before discarding
 *  - however the wizard closes, the lane is read again in place; a Work Item found gone says so without asking for anything impossible,
 *    and the lane read on closing no longer lists it
 *  - the widget never asks the host for a display mode, opening or closing (owner decision 2026-11-24: the wizard is the in-frame dialog,
 *    inline over the lane); closing the dialog returns to the lane cards
 *  - master-detail in a frame too narrow to sit beside the lane: the decision comes BEFORE the lane, takes the focus and is scrolled into view
 *  - under React StrictMode (the dev build) the wizard still opens and stays open
 */
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { COMPLETE_TOOL, PAGE_TOOL, PLAN_TOOL, wizardHost } from './wizardFixtures';
import { guid } from './laneFixtures';
import {
  LANE_OPEN_KEY,
  button,
  count,
  lane,
  lineIds,
  mount,
  oneClickItems,
  openItem,
  recordButtonReady,
  rowOf,
  toLastActionStep,
} from './wizardHarness';

const items = () => oneClickItems(2);
// [name, dialogFails]: the dialog is the first place; master-detail is reached when the dialog fails.
const MODES: ReadonlyArray<readonly [string, boolean]> = [
  ['the dialog', false],
  ['master-detail', true],
];

beforeEach(() => {
  window.localStorage.clear();
  window.localStorage.setItem(LANE_OPEN_KEY, JSON.stringify({ Decide: true }));
});

describe('Escape closes the wizard without writing', () => {
  it.each(MODES)(
    '%s: nothing is committed, the lane is read again and the focus returns to the row',
    async (_n, mode) => {
      const host = wizardHost(items(), { dialogFails: mode });
      mount(host);
      await openItem(1);
      const readsBefore = host.callsTo(PAGE_TOOL).length;

      fireEvent.keyDown(screen.getByTestId('step-found'), { key: 'Escape', code: 'Escape' });

      await waitFor(() => expect(screen.queryByTestId('step-found')).toBeNull());
      expect(host.callsTo(COMPLETE_TOOL)).toHaveLength(0);
      await waitFor(() => expect(host.callsTo(PAGE_TOOL).length).toBeGreaterThan(readsBefore));
      await waitFor(() => expect(document.activeElement).toBe(rowOf(1)));
      expect(count()).toHaveTextContent('3');
    }
  );

  it.each(MODES)(
    '%s: with a choice made it asks before discarding, and Keep deciding keeps the wizard',
    async (_n, mode) => {
      const host = wizardHost(items(), { dialogFails: mode });
      mount(host);
      await openItem(1);
      await toLastActionStep();
      fireEvent.click(button('Next'));
      const confirm = await screen.findByTestId('step-confirm');

      fireEvent.keyDown(confirm, { key: 'Escape', code: 'Escape' });

      // The prompt's own buttons: the dialog fades in over the wizard, so wait for each rather than reading them the instant one shows.
      fireEvent.click(await screen.findByRole('button', { name: 'Keep deciding' }));
      await waitFor(() => expect(screen.queryByRole('button', { name: 'Discard and close' })).toBeNull());
      expect(screen.getByTestId('step-confirm')).toBeInTheDocument();
      expect(host.callsTo(COMPLETE_TOOL)).toHaveLength(0);
      // Closing at last, by Escape and Discard: still nothing was written.
      fireEvent.keyDown(screen.getByTestId('step-confirm'), { key: 'Escape', code: 'Escape' });
      fireEvent.click(await screen.findByRole('button', { name: 'Discard and close' }));
      await waitFor(() => expect(screen.queryByTestId('step-confirm')).toBeNull());
      expect(host.callsTo(COMPLETE_TOOL)).toHaveLength(0);
    }
  );
});

describe('the lane is read again whenever the wizard closes', () => {
  it.each(MODES)('%s: closing with Close reads it again, in place and without the loading state', async (_n, mode) => {
    const host = wizardHost(items(), { dialogFails: mode });
    mount(host);
    await openItem(1);
    const readsBefore = host.callsTo(PAGE_TOOL).length;

    fireEvent.click(button('Close'));

    await waitFor(() => expect(host.callsTo(PAGE_TOOL).length).toBeGreaterThan(readsBefore));
    expect(screen.queryByTestId('decide-lane-loading')).toBeNull();
    await waitFor(() => expect(document.activeElement).toBe(rowOf(1)));
  });

  it('an item found gone says so without asking for anything the user cannot do, and closing reads the lane without it', async () => {
    const host = wizardHost(items(), {
      dialogFails: true,
      // The plan tool reads the plan but the Work Item has left the caller's lanes (decided elsewhere): no row, no row error.
      plan: (signalId, item) => ({
        structuredContent: { plan: { ...item.plan, signalId }, suppression: null, row: null },
      }),
    });
    mount(host);
    await screen.findByTestId('decide-lane');
    fireEvent.click(rowOf(1));
    const error = await screen.findByTestId('item-error');
    expect(error).toHaveTextContent('This item is no longer on your list.');
    expect(error).toHaveTextContent('Close this decision to see the list as it is now.');
    expect(error).not.toHaveTextContent(/read the list again/i);

    // It was decided elsewhere: the server's lane no longer has it.
    host.recorded.set(guid('5', 1), {} as never);
    fireEvent.click(button('Close'));

    await waitFor(() => expect(count()).toHaveTextContent('2'));
    expect(lineIds()).not.toContain(guid('5', 1));
    // Its row is gone, so the focus goes to the lane rather than being lost.
    await waitFor(() => expect(document.activeElement?.contains(lane())).toBe(true));
  });

  it('a Work Item that has left the lane when it is opened says the list has been read again, and reads it', async () => {
    const host = wizardHost(items(), { dialogFails: true });
    mount(host);
    await screen.findByTestId('decide-lane');
    const row = rowOf(1);
    // The user's lane still shows it, but the server's no longer does; the lane's own snapshot is replaced by the next read.
    host.recorded.set(guid('5', 2), {} as never);
    const readsBefore = host.callsTo(PAGE_TOOL).length;
    fireEvent.click(rowOf(2));
    await screen.findByTestId('step-found'); // it opens: the snapshot still has it
    fireEvent.click(button('Close'));
    await waitFor(() => expect(host.callsTo(PAGE_TOOL).length).toBeGreaterThan(readsBefore));
    await waitFor(() => expect(lineIds()).not.toContain(guid('5', 2)));
    expect(row).toBeDefined();
  });
});

describe('the display mode is never requested (owner decision 2026-11-24)', () => {
  const withMode = (displayMode?: string) => ({
    hostContext: {
      theme: 'light',
      availableDisplayModes: ['inline', 'fullscreen', 'pip'],
      ...(displayMode ? { displayMode } : {}),
    },
  });

  it.each(['inline', 'pip', 'fullscreen', undefined])(
    'a frame that is %s: opening and closing the dialog ask the host for no display mode, and the dialog is inline over the lane',
    async frameMode => {
      const host = wizardHost(items());
      mount(host, withMode(frameMode));
      await openItem(1);
      expect(screen.getByRole('dialog')).toBeInTheDocument();
      expect(screen.getByTestId('wizard-place')).toHaveAttribute('data-place', 'dialog');
      expect(lane()).toBeInTheDocument();
      fireEvent.click(button('Close'));
      await waitFor(() => expect(screen.queryByTestId('step-found')).toBeNull());
      await new Promise(resolve => setTimeout(resolve, 50));
      expect(host.requestDisplayMode).not.toHaveBeenCalled();
    }
  );

  it('Cancel on the last step closes the dialog back to the lane cards, with no display-mode call', async () => {
    const host = wizardHost(items());
    mount(host, withMode('inline'));
    await openItem(1);
    await toLastActionStep();
    fireEvent.click(button('Next'));
    await screen.findByTestId('step-confirm');

    fireEvent.click(button('Cancel'));
    // A choice was made: the shell asks before discarding.
    fireEvent.click(await screen.findByRole('button', { name: 'Discard and close' }));

    await waitFor(() => expect(screen.queryByTestId('step-confirm')).toBeNull());
    expect(screen.getByTestId('wizard-place')).toHaveAttribute('data-place', 'idle');
    expect(lineIds()).toHaveLength(3);
    await waitFor(() => expect(document.activeElement).toBe(rowOf(1)));
    expect(host.callsTo(COMPLETE_TOOL)).toHaveLength(0);
    expect(host.requestDisplayMode).not.toHaveBeenCalled();
  });
});

describe('master-detail in a frame too narrow to sit beside the lane', () => {
  const setWidth = (width: number) => Object.defineProperty(window, 'innerWidth', { configurable: true, value: width });
  let width = window.innerWidth;
  let scrolled: jest.Mock;
  beforeEach(() => {
    width = window.innerWidth;
    scrolled = jest.fn();
    Element.prototype.scrollIntoView = scrolled;
  });
  afterEach(() => {
    setWidth(width);
    delete (Element.prototype as { scrollIntoView?: unknown }).scrollIntoView;
  });

  const before = (a: Element, b: Element) => Boolean(a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING);

  it('stacked (600 px): the decision comes before the lane, takes the focus and is scrolled into view', async () => {
    setWidth(600);
    const host = wizardHost(items(), { dialogFails: true });
    mount(host);
    await screen.findByTestId('decide-lane');
    const laneNode = lane();
    fireEvent.click(rowOf(1));
    const detail = await screen.findByTestId('wizard-detail');
    await screen.findByTestId('step-found');

    expect(before(detail, lane())).toBe(true);
    expect(lane()).toBe(laneNode); // the lane was reordered around, not remounted
    await waitFor(() => expect(document.activeElement).toBe(detail));
    expect(scrolled.mock.contexts).toContain(detail);
  });

  it('side by side (1100 px): the lane stays first, and the decision still takes the focus', async () => {
    setWidth(1100);
    const host = wizardHost(items(), { dialogFails: true });
    mount(host);
    await screen.findByTestId('decide-lane');
    fireEvent.click(rowOf(1));
    const detail = await screen.findByTestId('wizard-detail');

    expect(before(lane(), detail)).toBe(true);
    await waitFor(() => expect(document.activeElement).toBe(detail));
  });
});

describe('under React StrictMode (the dev build)', () => {
  it.each(MODES)('%s: the wizard opens and stays open, and records once', async (_n, mode) => {
    const host = wizardHost(items(), { dialogFails: mode });
    mount(host, {}, true);
    await openItem(1);
    // Past any effect's second run: a wizard that is going to close itself has done so by now.
    await new Promise(resolve => setTimeout(resolve, 150));
    expect(screen.getByTestId('step-found')).toBeInTheDocument();

    fireEvent.click(await toLastActionStep());
    fireEvent.click(await recordButtonReady(/^record decision$/i));
    await waitFor(() => expect(host.callsTo(COMPLETE_TOOL)).toHaveLength(1));
    // The plan is read for the item: the dev build may read it more than once, never zero times.
    expect(host.callsTo(PLAN_TOOL).length).toBeGreaterThan(0);
  });
});
