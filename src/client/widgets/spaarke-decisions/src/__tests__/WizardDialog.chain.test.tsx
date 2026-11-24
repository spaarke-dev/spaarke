/**
 * Where the decision wizard shows, and what happens when a place fails (task 032, O-5, ADR-050). The wizard itself is replaced here by a
 * stand-in that registers with the launcher the way the real host does, so these tests are about the CHAIN: the in-frame dialog (inline,
 * with no display mode ever requested: owner decision 2026-11-24) -> master-detail -> a link to the Console through the host
 * (`openLink`) -> nothing opened without a say from the user.
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
    DecisionWizardHost: (props: { embedded?: boolean; collectBecause?: boolean; evidenceLinks?: boolean }) => {
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
      return (
        <div
          data-testid={`stand-in-${place}`}
          data-because={String(props.collectBecause)}
          data-evidence-links={String(props.evidenceLinks)}
        />
      );
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
  offered: string[] | undefined;
  consoleBaseUrl: string | null | undefined;
  openLinkResult: boolean;
  lane: WorklistData | null;
}

function mount(overrides: Partial<Setup> = {}) {
  const setup: Setup = {
    offered: ['inline', 'fullscreen'],
    consoleBaseUrl: CONSOLE_BASE,
    openLinkResult: true,
    lane: LANE,
    ...overrides,
  };
  // Answers a grant if it is ever asked: the widget must not ask (the tests assert it is never called).
  const requestDisplayMode = jest.fn(async () => 'fullscreen' as const);
  const openLink = jest.fn(async () => setup.openLinkResult);
  const onRecorded = jest.fn();
  const onActiveChange = jest.fn();
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
        onActiveChange={onActiveChange}
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
    onActiveChange,
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
  it("the dialog first, inline: launched for the item with the widget's own reader, type-ahead and Because box, with no display mode requested", async () => {
    const view = mount();
    view.open(request(1));
    await waitFor(() => expect(place()).toBe('dialog'));
    expect(view.requestDisplayMode).not.toHaveBeenCalled();
    expect(screen.getByTestId('stand-in-dialog')).toHaveAttribute('data-because', 'true');
    await waitFor(() => expect(stand.launches).toHaveLength(1));
    expect(stand.launches[0]).toMatchObject({ itemId: 'sig-1', visibleList: ['sig-1', 'sig-2'] });
    expect(typeof stand.launches[0].loadItem).toBe('function');
    expect(typeof stand.launches[0].lookup).toBe('function');
    // The Console's side-pane picker is not used (no `openLookup`); a record opens through the host's `openLink` (see the next block).
    expect(stand.launches[0].openLookup).toBeUndefined();
    expect(typeof stand.launches[0].openRecord).toBe('function');
    expect(screen.getByTestId('stand-in-dialog')).toHaveAttribute('data-evidence-links', 'true');
    expect(view.openLink).not.toHaveBeenCalled();
  });

  it('the lane stays mounted as the master while the dialog is open over it', async () => {
    const view = mount();
    view.open(request(1));
    await waitFor(() => expect(place()).toBe('dialog'));
    expect(screen.getByTestId('the-lane')).toBeInTheDocument();
    expect(screen.queryByTestId('stand-in-detail')).toBeNull();
  });

  it.each([
    ['only inline is offered', ['inline']],
    ['no modes are listed', undefined],
    ['fullscreen is offered', ['inline', 'fullscreen']],
  ])('%s: the dialog opens in the mode the frame is in, and the host is not asked for another', async (_n, offered) => {
    const view = mount({ offered });
    view.open(request(1));
    await waitFor(() => expect(place()).toBe('dialog'));
    await waitFor(() => expect(stand.launches).toHaveLength(1));
    expect(view.requestDisplayMode).not.toHaveBeenCalled();
  });

  it('the dialog gives the widget room: the root has a minimum height only while the dialog place is active', async () => {
    const view = mount();
    const root = screen.getByTestId('wizard-place').parentElement!;
    const idleClasses = root.className;
    view.open(request(1));
    await waitFor(() => expect(place()).toBe('dialog'));
    expect(root.className).not.toBe(idleClasses);
    await act(async () => stand.close?.());
    await waitFor(() => expect(place()).toBe('idle'));
    expect(root.className).toBe(idleClasses);
  });

  it('the dialog failing in a frame too narrow for master-detail: the Console is opened through the host and the banner stays', async () => {
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 360 });
    stand.throwIn = new Set(['dialog']);
    const view = mount();
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

  it('Close dismisses the banner, and the display mode is not touched (the widget never changed it)', async () => {
    const view = await toLink();
    fireEvent.click(screen.getByTestId('wizard-link-close'));
    expect(screen.queryByTestId('wizard-link-fallback')).toBeNull();
    expect(place()).toBe('idle');
    expect(view.requestDisplayMode).not.toHaveBeenCalled();
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

  it('a launch the shared host calls busy says so, opens nothing and releases the place it was given', async () => {
    stand.answer = 'busy';
    const view = mount();
    view.open(request(1));
    expect(await screen.findByTestId('wizard-notice')).toHaveTextContent('Finish or close the open decision');
    await waitFor(() => expect(place()).toBe('idle'));
    expect(view.onActiveChange).toHaveBeenLastCalledWith(false);
    expect(view.requestDisplayMode).not.toHaveBeenCalled();
  });

  it('tells the lane when a decision is open and when it is not, so the lane can refuse another', async () => {
    const view = mount();
    expect(view.onActiveChange).not.toHaveBeenCalled();
    view.open(request(1));
    await waitFor(() => expect(view.onActiveChange).toHaveBeenLastCalledWith(true));
    await waitFor(() => expect(stand.launches).toHaveLength(1));
    await act(async () => stand.close?.());
    await waitFor(() => expect(view.onActiveChange).toHaveBeenLastCalledWith(false));
    expect(view.onActiveChange.mock.calls.map(c => c[0])).toEqual([true, false]);
  });

  it('an item that is not in the lane never marks a decision as open', async () => {
    const view = mount();
    view.open(request(1, 'sig-gone'));
    await screen.findByTestId('wizard-notice');
    expect(view.onActiveChange).not.toHaveBeenCalled();
  });

  it('the wizard closing returns to the lane with no display-mode call, and the next request opens again', async () => {
    const view = mount();
    view.open(request(1));
    await waitFor(() => expect(stand.launches).toHaveLength(1));
    await act(async () => stand.close?.());
    await waitFor(() => expect(place()).toBe('idle'));
    expect(screen.getByTestId('the-lane')).toBeInTheDocument();
    expect(view.requestDisplayMode).not.toHaveBeenCalled();

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

describe('opening a record from the wizard ("Open in Spaarke" on an evidence line)', () => {
  const GUID = 'aaaaaaaa-0000-4000-8000-000000000001';
  const launched = async (overrides: Parameters<typeof mount>[0] = {}) => {
    const view = mount(overrides);
    view.open(request(1));
    await waitFor(() => expect(stand.launches).toHaveLength(1));
    return { view, openRecord: stand.launches[0].openRecord };
  };

  it("asks the host to open the Console's own record form for that table and id, and nothing else", async () => {
    const { view, openRecord } = await launched();
    await openRecord!('SPRK_Communication', GUID.toUpperCase());
    expect(view.openLink).toHaveBeenCalledTimes(1);
    expect(view.openLink).toHaveBeenCalledWith(
      `${CONSOLE_BASE}/main.aspx?etn=sprk_communication&id=${GUID}&pagetype=entityrecord`
    );
  });

  it('refuses a table or id that is not a logical name and a guid: nothing is opened, and the wizard is told', async () => {
    const { view, openRecord } = await launched();
    await expect(openRecord!('core', 'not-a-guid')).rejects.toThrow();
    await expect(openRecord!('sprk x&y', GUID)).rejects.toThrow();
    await expect(openRecord!('', GUID)).rejects.toThrow();
    expect(view.openLink).not.toHaveBeenCalled();
  });

  it('says so (the wizard logs it and keeps its place) when the host does not open the link', async () => {
    const { view, openRecord } = await launched({ openLinkResult: false });
    await expect(openRecord!('sprk_budget', GUID)).rejects.toThrow('did not open');
    expect(view.openLink).toHaveBeenCalledTimes(1);
  });

  it.each([null, undefined, 'http://org.crm.dynamics.com', 'not a url'])(
    'a Console address of %p: the wizard is given no way to open a record, so no line shows a link that cannot work',
    async consoleBaseUrl => {
      const { openRecord } = await launched({ consoleBaseUrl });
      expect(openRecord).toBeUndefined();
    }
  );
});
