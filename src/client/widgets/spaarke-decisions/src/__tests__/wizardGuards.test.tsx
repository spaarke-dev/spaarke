/**
 * What stops a decision being recorded by accident, in the widget (task 032 fix round; NFR-06: a decision is recorded only by an explicit
 * Confirm). Each case spies on the commit tool (`spaarke_complete_decision`), the one write.
 *
 *  - Next and Record decision are the same button, so a double click on Next at the last action step, or a held Enter, used to move to Confirm
 *    and record the decision before the person had seen it. Confirm now holds its button for half a second and takes focus off it.
 */
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { COMPLETE_TOOL, wizardHost } from './wizardFixtures';
import {
  LANE_OPEN_KEY,
  button,
  count,
  mount,
  oneClickItems,
  openItem,
  pressEnter,
  recordButtonReady,
  toLastActionStep,
} from './wizardHarness';

const items = () => oneClickItems(1);

beforeEach(() => {
  window.localStorage.clear();
  window.localStorage.setItem(LANE_OPEN_KEY, JSON.stringify({ Decide: true }));
});

describe.each([
  ['the dialog', false],
  ['master-detail', true],
])('Confirm is never reached and recorded in one gesture (%s)', (_name, dialogFails) => {
  const setup = async () => {
    const host = wizardHost(items(), { dialogFails });
    mount(host);
    await openItem(1);
    return host;
  };

  it('a double click on Next at the last action step moves to Confirm and records nothing', async () => {
    const host = await setup();
    const next = await toLastActionStep();

    fireEvent.click(next);
    fireEvent.click(next); // the second click of the double click lands on the same node, which is Record decision now

    await screen.findByTestId('step-confirm');
    expect(button('Record decision')).toBeDisabled();
    await new Promise(resolve => setTimeout(resolve, 150));
    expect(host.callsTo(COMPLETE_TOOL)).toHaveLength(0);
  });

  it('a held Enter on Next records nothing: focus leaves the button when Confirm appears', async () => {
    const host = await setup();
    await toLastActionStep();

    pressEnter(false);
    await screen.findByTestId('step-confirm');
    pressEnter(true);
    pressEnter(true);
    pressEnter(true);

    await new Promise(resolve => setTimeout(resolve, 150));
    expect(host.callsTo(COMPLETE_TOOL)).toHaveLength(0);
  });

  it('a deliberate click once Confirm has been showing records exactly once', async () => {
    const host = await setup();
    fireEvent.click(await toLastActionStep());

    const record = await recordButtonReady(/^record decision$/i);
    fireEvent.click(record);
    fireEvent.click(record);

    await waitFor(() => expect(host.callsTo(COMPLETE_TOOL)).toHaveLength(1));
    await waitFor(() => expect(count()).toHaveTextContent('1'));
  });
});
