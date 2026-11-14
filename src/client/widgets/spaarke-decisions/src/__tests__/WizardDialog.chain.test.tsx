/**
 * Where the decision wizard shows, and what happens when a place fails (task 032, O-5, ADR-050). The wizard itself is replaced here by a
 * stand-in that registers with the launcher the way the real host does, so these tests are about the CHAIN: dialog -> master-detail ->
 * a link to the Console through the host (`openLink`) -> nothing opened without a say from the user.
 *
 * (The same chain with the real wizard, and the Confirm that records, is in `wizardFlow.test.tsx`.)
 */
import * as React from 'react';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { DecisionWizardLaunchRequest } from '@spaarke/ui-components/components/DecisionWizard/decisionWizardLauncher';
import type { WorklistData } from '@spaarke/ui-components/components/Worklist/worklistService';
import { WizardDialog, type WizardRequest } from '../wizard/WizardDialog';
import { DISCONNECTED, McpHostContext, type McpHostConnection } from '../mcp/mcpHostContext';
import { CONSOLE_BASE } from './laneFixtures';

/** What the stand-in host does for the current test. */
const stand = {
  /** Renders throw for these: 'dialog' (not embedded) and / or 'detail' (embedded). */
  throwIn: new Set<'dialog' | 'detail'>(),
  /** Registers with the launcher (a mounted host) or not. */
  registers: true,
  /** What a launch answers: stays open until the test closes it, or answers at once. */
  answer: 'open' as 'open' | 'busy',
  launches: [] as DecisionWizardLaunchRequest[],
  close: null as null | (() => void),
};

jest.mock('@spaarke/ui-components/components/DecisionWizard/DecisionWizardHost', () => {
  const launcher = jest.requireActual('@spaarke/ui-components/components/DecisionWizard/decisionWizardLauncher');
  return {
    DecisionWizardHost: (props: { embedded?: boolean; collectBecause?: boolean }) => {
      const place = props.embedded ? 'detail' : 'dialog';
      if (stand.throwIn.has(place)) throw new Error(`the ${place} failed`);
      React.useEffect(() => {
        if (!stand.registers) return undefined;
        return launcher.registerDecisionWizardHost(
          (request: DecisionWizardLaunchRequest) =>
            new Promise(resolve => {
              stand.launches.push(request);
              if (stand.answer === 'busy') resolve({ opened: false, reason: 'busy' });
              else stand.close = () => resolve({ opened: true });
            })
        );
      }, []);
      return <div data-testid={`stand-in-${place}`} data-because={String(props.collectBecause)} />;
    },
  };
});

const LANE = {
  set: 'lanes',
  Decide: [{ core: null, items: [{ signalId: 'sig-1' }, { signalId: 'sig-2' }] }],
  Do: [],
} as unknown as WorklistData;

const request = (seq: number, signalId = 'sig-1'): WizardRequest => ({
  seq,
  event: { signalId, core: null, visibleList: ['sig-1', 'sig-2'], position: 1, total: 2 },
});

interface Setup {
  displayMode: 'fullscreen' | 'inline' | null;
  offered: string[] | undefined;
  consoleBaseUrl: string | null | undefined;
  openLinkResult: boolean;
  lane: WorklistData | null;
}

function mount(overrides: Partial<Setup> = {}) {
  const setup: Setup = {
    displayMode: 'fullscreen',
    offered: ['inline', 'fullscreen'],
    consoleBaseUrl: CONSOLE_BASE,
    openLinkResult: true,
    lane: LANE,
    ...overrides,
  };
  const requestDisplayMode = jest.fn(async () => setup.displayMode);
  const openLink = jest.fn(async () => setup.openLinkResult);
  const onRecorded = jest.fn();
  const connection: McpHostConnection = {
    ...DISCONNECTED,
    isConnected: true,
    hostContext: setup.offered ? { availableDisplayModes: setup.offered } : undefined,
    callTool: jest.fn(async () => ({})),
    openLink,
    requestDisplayMode,
  };
  const ui = (req: WizardRequest | null) => (
    <McpHostContext.Provider value={connection}>
      <WizardDialog
        request={req}
        getLane={() => setup.lane}
        consoleBaseUrl={setup.consoleBaseUrl}
        onRecorded={onRecorded}
      >
        <div data-testid="the-lane">lane</div>
      </WizardDialog>
    </McpHostContext.Provider>
  );
  const view = render(ui(null));
  return {
    ...view,
    ui,
    open: (req: WizardRequest) => view.rerender(ui(req)),
    requestDisplayMode,
    openLink,
    onRecorded,
  };
}

const place = () => screen.getByTestId('wizard-place').getAttribute('data-place');
const consoleUrl = `${CONSOLE_BASE}/main.aspx?pagetype=webresource&webresourceName=sprk_spaarkeai`;

let innerWidth: number;
beforeEach(() => {
  stand.throwIn = new Set();
  stand.registers = true;
  stand.answer = 'open';
  stand.launches = [];
  stand.close = null;
  innerWidth = window.innerWidth;
  // A rendering error is reported by React and the boundary; it is the expected path of these tests.
  jest.spyOn(console, 'error').mockImplementation(() => undefined);
});
afterEach(() => {
  Object.defineProperty(window, 'innerWidth', { configurable: true, value: innerWidth });
  jest.restoreAllMocks();
});

describe('the places, in order', () => {
  it("fullscreen granted: the dialog, launched for the item with the widget's own reader, type-ahead and Because box", async () => {
    const view = mount();
    view.open(request(1));
    await waitFor(() => expect(place()).toBe('dialog'));
    expect(view.requestDisplayMode).toHaveBeenCalledWith('fullscreen');
    expect(screen.getByTestId('stand-in-dialog')).toHaveAttribute('data-because', 'true');
    await waitFor(() => expect(stand.launches).toHaveLength(1));
    expect(stand.launches[0]).toMatchObject({ itemId: 'sig-1', visibleList: ['sig-1', 'sig-2'] });
    expect(typeof stand.launches[0].loadItem).toBe('function');
    expect(typeof stand.launches[0].lookup).toBe('function');
    // The Console's pickers are not used: the host has no `openLookup` / `openRecord`.
    expect(stand.launches[0].openLookup).toBeUndefined();
    expect(stand.launches[0].openRecord).toBeUndefined();
    expect(view.openLink).not.toHaveBeenCalled();
  });

  it('fullscreen refused: master-detail, embedded, with the lane still there', async () => {
    const view = mount({ displayMode: 'inline' });
    view.open(request(1));
    await waitFor(() => expect(place()).toBe('detail'));
    expect(screen.getByTestId('stand-in-detail')).toBeInTheDocument();
    expect(screen.queryByTestId('stand-in-dialog')).toBeNull();
    expect(screen.getByTestId('the-lane')).toBeInTheDocument();
    expect(view.openLink).not.toHaveBeenCalled();
  });

  it('fullscreen not offered by the host: it is not asked, and master-detail is used', async () => {
    const view = mount({ offered: ['inline'] });
    view.open(request(1));
    await waitFor(() => expect(place()).toBe('detail'));
    expect(view.requestDisplayMode).not.toHaveBeenCalled();
  });

  it('a host that lists no modes is asked (it may still say yes)', async () => {
    const view = mount({ offered: undefined });
    view.open(request(1));
    await waitFor(() => expect(place()).toBe('dialog'));
    expect(view.requestDisplayMode).toHaveBeenCalledWith('fullscreen');
  });

  it('a frame too narrow for master-detail: the Console is opened through the host and the banner stays', async () => {
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 360 });
    const view = mount({ displayMode: 'inline' });
    view.open(request(1));
    await waitFor(() => expect(place()).toBe('link'));
    await waitFor(() => expect(view.openLink).toHaveBeenCalledWith(consoleUrl));
    expect(screen.getByTestId('wizard-link-fallback')).toHaveTextContent('Open Spaarke');
    expect(screen.queryByText('The link could not be opened.')).toBeNull();
    expect(stand.launches).toHaveLength(0);
  });
});

describe('a place that fails steps to the next', () => {
  it('the dialog throwing while it renders: master-detail takes over and the wizard launches there', async () => {
    stand.throwIn = new Set(['dialog']);
    const view = mount();
    view.open(request(1));
    await waitFor(() => expect(place()).toBe('detail'));
    expect(screen.getByTestId('stand-in-detail')).toBeInTheDocument();
    await waitFor(() => expect(stand.launches).toHaveLength(1));
    expect(view.openLink).not.toHaveBeenCalled();
  });

  it('both throwing: the Console link, opened through the host', async () => {
    stand.throwIn = new Set(['dialog', 'detail']);
    const view = mount();
    view.open(request(1));
    await waitFor(() => expect(place()).toBe('link'));
    await waitFor(() => expect(view.openLink).toHaveBeenCalledWith(consoleUrl));
    expect(screen.getByTestId('wizard-link-fallback')).toBeInTheDocument();
  });

  it('no host mounted for the place (the launch reports no-host): the next place', async () => {
    stand.registers = false;
    const view = mount();
    view.open(request(1));
    await waitFor(() => expect(place()).toBe('link'));
    await waitFor(() => expect(view.openLink).toHaveBeenCalledTimes(1));
  });
});

describe('the link, the last place', () => {
  async function toLink(overrides: Partial<Setup> = {}) {
    stand.throwIn = new Set(['dialog', 'detail']);
    const view = mount(overrides);
    view.open(request(1));
    await waitFor(() => expect(place()).toBe('link'));
    return view;
  }

  it('says so when the host did not open it, and the link in the banner asks the host again', async () => {
    const view = await toLink({ openLinkResult: false });
    expect(await screen.findByText(/The link could not be opened\./)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('link', { name: 'Open Spaarke' }));
    await waitFor(() => expect(view.openLink).toHaveBeenCalledTimes(2));
  });

  it('with no usable Console address there is no link, and nothing is opened', async () => {
    const view = await toLink({ consoleBaseUrl: null });
    expect(screen.getByTestId('wizard-link-fallback')).toHaveTextContent(
      'Open Spaarke and decide it from your Decide lane.'
    );
    expect(screen.queryByRole('link')).toBeNull();
    expect(view.openLink).not.toHaveBeenCalled();
  });

  it('Close dismisses the banner and returns the frame to inline (it had asked for fullscreen)', async () => {
    const view = await toLink();
    fireEvent.click(screen.getByTestId('wizard-link-close'));
    expect(screen.queryByTestId('wizard-link-fallback')).toBeNull();
    expect(place()).toBe('idle');
    expect(view.requestDisplayMode).toHaveBeenLastCalledWith('inline');
  });
});

describe('one wizard at a time, and closing', () => {
  it('a second request while one is open is refused with a notice, and the open one is not replaced', async () => {
    const view = mount();
    view.open(request(1));
    await waitFor(() => expect(stand.launches).toHaveLength(1));
    view.open(request(2, 'sig-2'));
    expect(await screen.findByTestId('wizard-notice')).toHaveTextContent(
      'Finish or close the open decision before opening another.'
    );
    expect(stand.launches).toHaveLength(1);
    expect(place()).toBe('dialog');
  });

  it('a launch the shared host calls busy says so and opens nothing', async () => {
    stand.answer = 'busy';
    const view = mount();
    view.open(request(1));
    expect(await screen.findByTestId('wizard-notice')).toHaveTextContent('Finish or close the open decision');
  });

  it('the wizard closing returns the frame to inline, and the next request opens again', async () => {
    const view = mount();
    view.open(request(1));
    await waitFor(() => expect(stand.launches).toHaveLength(1));
    await act(async () => stand.close?.());
    await waitFor(() => expect(place()).toBe('idle'));
    expect(view.requestDisplayMode).toHaveBeenLastCalledWith('inline');

    view.open(request(2, 'sig-2'));
    await waitFor(() => expect(stand.launches).toHaveLength(2));
    expect(stand.launches[1].itemId).toBe('sig-2');
  });

  it('a recorded decision is passed on so the lane is read again', async () => {
    const view = mount();
    view.open(request(1));
    await waitFor(() => expect(stand.launches).toHaveLength(1));
    stand.launches[0].onRecorded?.({ signalId: 'sig-1', alsoResolved: [] });
    expect(view.onRecorded).toHaveBeenCalledTimes(1);
  });

  it('an item that is not in the lane the user saw opens nothing and says so', async () => {
    const view = mount();
    view.open(request(1, 'sig-gone'));
    expect(await screen.findByTestId('wizard-notice')).toHaveTextContent('no longer on your list');
    expect(view.requestDisplayMode).not.toHaveBeenCalled();
    expect(stand.launches).toHaveLength(0);
    expect(place()).toBe('idle');
  });

  it('no lane read yet: the same notice, and nothing opens', async () => {
    const view = mount({ lane: null });
    view.open(request(1));
    expect(await screen.findByTestId('wizard-notice')).toHaveTextContent('no longer on your list');
    expect(stand.launches).toHaveLength(0);
  });

  it('a request that has already been handled is not opened again by a re-render', async () => {
    const view = mount();
    view.open(request(1));
    await waitFor(() => expect(stand.launches).toHaveLength(1));
    view.open(request(1));
    view.open(request(1));
    await act(async () => undefined);
    expect(stand.launches).toHaveLength(1);
  });
});
