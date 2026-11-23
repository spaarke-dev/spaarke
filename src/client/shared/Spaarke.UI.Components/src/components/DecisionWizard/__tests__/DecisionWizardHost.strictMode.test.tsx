/**
 * The decision wizard host under React StrictMode (spaarkeai-mcp-bff-api-r1 task 032 fix round).
 *
 * StrictMode (the dev build) runs every effect's cleanup and the effect again, straight away, on each component that has just mounted.
 * The host's cleanup used to settle the open launch, so a wizard launched in the same commit was closed the moment it opened. The host's
 * cleanup now settles only when no host is mounted again by the end of that task; a real unmount still never leaves a launcher waiting.
 */

import * as React from 'react';
import '@testing-library/jest-dom';
import { act, fireEvent, render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { DO_ID, doItem, doPlan } from '../__fixtures__/decisionWizardFixtures';
import { DecisionWizardHost } from '../DecisionWizardHost';
import { launchDecisionWizard } from '../decisionWizardLauncher';
import type { DecisionWizardLaunchResult } from '../decisionWizardLauncher';
import { makeLoader } from './decisionTestKit';

// The host builds its own service from `fetch`: answer the plan, the suppression and nothing else.
const fetchFn = jest.fn(async (url: string) => {
  const body = url.includes('suppression')
    ? { signalId: DO_ID, dismissalsInWindow: 0, windowDays: 30, threshold: 3, suppressed: false }
    : doPlan;
  return { ok: true, status: 200, json: async () => body } as unknown as Response;
});

const launch = () =>
  launchDecisionWizard({
    itemId: DO_ID,
    core: null,
    visibleList: [DO_ID],
    loadItem: makeLoader([doItem]),
  } as never);

/**
 * Mounts the host and launches in the SAME commit, as the widget does: a parent mounts the host and, in its own effect (which runs after
 * the host's), launches. That is the order in which StrictMode's second run of the host's effect used to close the wizard.
 */
function Opener({ onLaunch }: { onLaunch: () => void }) {
  const [shown, setShown] = React.useState(false);
  React.useEffect(() => {
    if (shown) onLaunch();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- once, when the host has been mounted
  }, [shown]);
  return (
    <FluentProvider theme={webLightTheme}>
      <button onClick={() => setShown(true)}>open</button>
      {shown && <DecisionWizardHost authenticatedFetch={fetchFn as never} bffBaseUrl="" />}
    </FluentProvider>
  );
}

function mountOpener(strict: boolean, onLaunch: () => void) {
  const tree = <Opener onLaunch={onLaunch} />;
  const view = render(strict ? <React.StrictMode>{tree}</React.StrictMode> : tree);
  fireEvent.click(screen.getByRole('button', { name: 'open' }));
  return view;
}

describe('DecisionWizardHost', () => {
  it.each([[true], [false]])('StrictMode %s: a launch opens the wizard and it stays open', async strict => {
    let settled: DecisionWizardLaunchResult | null = null;
    let launched!: Promise<DecisionWizardLaunchResult>;
    mountOpener(strict, () => {
      launched = launch();
      void launched.then(result => (settled = result));
    });

    expect(await screen.findByTestId('step-found')).toBeInTheDocument();
    await act(async () => {
      await new Promise(resolve => setTimeout(resolve, 50));
    });
    expect(screen.getByTestId('step-found')).toBeInTheDocument();
    expect(settled).toBeNull(); // not settled: nothing closed it

    fireEvent.click(screen.getByRole('button', { name: /^close$/i }));
    await expect(launched).resolves.toEqual({ opened: true });
  });

  it('a real unmount while the wizard is open still settles the launch (a launcher is never left waiting)', async () => {
    let launched!: Promise<DecisionWizardLaunchResult>;
    const view = mountOpener(false, () => {
      launched = launch();
    });
    await screen.findByTestId('step-found');

    view.unmount();

    await expect(launched).resolves.toEqual({ opened: true });
  });
});
