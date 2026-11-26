/**
 * Widget-level tests for the Daily Briefing MCP App (spaarkeai-mcp-bff-api-r2 task 034; spec FR-09, FR-16).
 *
 * The real `DailyBriefingApp` (the Console's component) is mounted through the widget's own `App`, under a plain host connection (no ext-apps
 * SDK). The briefing arrives either as the result that opened the widget or from a `spaarke_get_briefing` call through the host; the tests prove
 * it draws, that it makes ZERO `authenticatedFetch` / `getXrm` / `window.Xrm` accesses, that the actions the host cannot do are hidden, that
 * Refresh calls the tool, that a failure lands in the component's own error state, and that the theme follows the host context.
 */
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { App, omittedNotice } from '../App';
import { DISCONNECTED, McpHostContext, type McpHostConnection } from '../../../shared/mcp/mcpHostContext';

const mockAuthenticatedFetch = jest.fn();
jest.mock('@spaarke/auth', () => ({
  authenticatedFetch: (...args: unknown[]) => mockAuthenticatedFetch(...args),
  isApiError: () => false,
  isAuthFailure: () => false,
}));

const mockGetXrm = jest.fn();
jest.mock('@spaarke/ui-components', () => {
  const actual = jest.requireActual('../shims/uiComponents');
  return {
    ...actual,
    getXrm: (...args: unknown[]) => {
      mockGetXrm(...args);
      return actual.getXrm(...args);
    },
  };
});

const MATTER_ID = '11111111-1111-1111-1111-111111111111';
const DOC_ID = '33333333-3333-3333-3333-333333333333';

function fixtureBriefing() {
  return {
    tldr: {
      summary: 'Two things need you today.',
      keyTakeaways: ['Review the Acme filing'],
      categoryCount: 2,
      priorityItemCount: 1,
      itemRefs: [],
    },
    channelNarratives: [
      {
        category: 'matters',
        bullets: [
          {
            narrative: 'Acme v. Globex',
            itemIds: ['item-1'],
            primaryEntityType: 'sprk_matter',
            primaryEntityId: MATTER_ID,
            primaryEntityName: 'Acme v. Globex',
          },
        ],
      },
      {
        category: 'documents',
        bullets: [
          {
            narrative: 'Master services agreement',
            itemIds: [DOC_ID],
            primaryEntityType: 'sprk_document',
            primaryEntityId: DOC_ID,
            primaryEntityName: 'Master services agreement',
          },
        ],
      },
    ],
    generatedAtUtc: '2026-11-26T08:00:00Z',
  };
}

const xrmAccess = jest.fn();

beforeEach(() => {
  mockAuthenticatedFetch.mockReset();
  mockAuthenticatedFetch.mockRejectedValue(new Error('no network'));
  mockGetXrm.mockClear();
  xrmAccess.mockClear();
  // Any read of window.Xrm is recorded: the widget must never touch it.
  Object.defineProperty(window, 'Xrm', {
    configurable: true,
    get: () => {
      xrmAccess();
      return undefined;
    },
  });
});

afterEach(() => {
  delete (window as unknown as { Xrm?: unknown }).Xrm;
});

function renderWith(connection: Partial<McpHostConnection>, openingWaitMs = 20) {
  const value: McpHostConnection = { ...DISCONNECTED, ...connection };
  const view = render(
    <McpHostContext.Provider value={value}>
      <App openingWaitMs={openingWaitMs} />
    </McpHostContext.Provider>
  );
  const update = (next: Partial<McpHostConnection>) =>
    view.rerender(
      <McpHostContext.Provider value={{ ...value, ...next }}>
        <App openingWaitMs={openingWaitMs} />
      </McpHostContext.Provider>
    );
  return { ...view, update };
}

/** A host whose `callTool` answers with a tool result. */
function hostCalling(structuredContent: Record<string, unknown>, extra: Record<string, unknown> = {}) {
  const callTool = jest
    .fn()
    .mockResolvedValue({ structuredContent, content: [{ type: 'text', text: 'ok' }], ...extra });
  return callTool;
}

const noNetworkOrXrm = () => {
  expect(mockAuthenticatedFetch).not.toHaveBeenCalled();
  expect(mockGetXrm).not.toHaveBeenCalled();
  expect(xrmAccess).not.toHaveBeenCalled();
};

describe('Briefing widget: the opening result', () => {
  it('draws the briefing the host delivered, with no tool call, no authenticatedFetch and no Xrm', async () => {
    const callTool = hostCalling(fixtureBriefing());
    renderWith({ isConnected: true, toolResult: fixtureBriefing(), callTool });

    expect(await screen.findByText('Acme v. Globex')).toBeInTheDocument();
    expect(screen.getByText('Master services agreement')).toBeInTheDocument();
    expect(callTool).not.toHaveBeenCalled();
    noNetworkOrXrm();
  });

  it('hides the actions the host cannot do: row menus, the header menu, Preferences and record links', async () => {
    renderWith({ isConnected: true, toolResult: fixtureBriefing(), callTool: hostCalling(fixtureBriefing()) });

    await screen.findByText('Acme v. Globex');

    expect(screen.queryByRole('button', { name: 'More actions' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Preferences' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Acme v\. Globex/ })).not.toBeInTheDocument();
    noNetworkOrXrm();
  });

  it('generates only once when the result arrives after the first render', async () => {
    const callTool = hostCalling(fixtureBriefing());
    const view = renderWith({ isConnected: true, toolResult: null, callTool }, 5000);

    view.update({ isConnected: true, toolResult: fixtureBriefing(), callTool });

    expect(await screen.findByText('Acme v. Globex')).toBeInTheDocument();
    expect(callTool).not.toHaveBeenCalled();
  });
});

describe('Briefing widget: no opening result', () => {
  it('calls spaarke_get_briefing through the host after the wait and draws the answer', async () => {
    const callTool = hostCalling(fixtureBriefing());
    renderWith({ isConnected: true, toolResult: null, callTool });

    expect(await screen.findByText('Acme v. Globex')).toBeInTheDocument();
    expect(callTool).toHaveBeenCalledTimes(1);
    // The component asks for its default windows (5 days, 7 days = 168 hours of recency). The tool's own defaults for an absent window are
    // the same two values (GetBriefingTool.DefaultDueWithinDays / DefaultRecencyHours; GetBriefingToolTests pins them), so this read and a
    // model's call with no arguments show the same briefing.
    expect(callTool).toHaveBeenCalledWith('spaarke_get_briefing', { dueWithinDays: 5, recencyHours: 168 });
    noNetworkOrXrm();
  });

  it('Refresh calls the tool again', async () => {
    const callTool = hostCalling(fixtureBriefing());
    renderWith({ isConnected: true, toolResult: fixtureBriefing(), callTool });
    await screen.findByText('Acme v. Globex');
    expect(callTool).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole('button', { name: 'Refresh notifications' }));

    await waitFor(() => expect(callTool).toHaveBeenCalledTimes(1));
    expect(callTool).toHaveBeenCalledWith('spaarke_get_briefing', expect.any(Object));
    noNetworkOrXrm();
  });
});

describe('Briefing widget: a briefing the tool trimmed', () => {
  const NOTICE = '12 highlights were left out; open the Spaarke Console for all of them.';

  it('shows one warning that says how many highlights were left out, above the briefing', async () => {
    renderWith({
      isConnected: true,
      toolResult: { ...fixtureBriefing(), omittedHighlights: 12 },
      callTool: hostCalling(fixtureBriefing()),
    });

    expect(await screen.findByText(NOTICE)).toBeInTheDocument();
    expect(screen.getAllByText(/left out/)).toHaveLength(1);
    expect(screen.getByText('Acme v. Globex')).toBeInTheDocument();
  });

  it('shows no notice for a briefing that was not trimmed', async () => {
    renderWith({ isConnected: true, toolResult: fixtureBriefing(), callTool: hostCalling(fixtureBriefing()) });

    await screen.findByText('Acme v. Globex');

    expect(screen.queryByText(/left out/)).not.toBeInTheDocument();
  });

  it('follows Refresh: the notice goes when the next briefing is whole, and comes when it is trimmed', async () => {
    const callTool = jest
      .fn()
      .mockResolvedValueOnce({ structuredContent: fixtureBriefing(), content: [] })
      .mockResolvedValueOnce({ structuredContent: { ...fixtureBriefing(), omittedHighlights: 5 }, content: [] });
    renderWith({ isConnected: true, toolResult: { ...fixtureBriefing(), omittedHighlights: 12 }, callTool });
    await screen.findByText(NOTICE);

    fireEvent.click(screen.getByRole('button', { name: 'Refresh notifications' }));
    await waitFor(() => expect(screen.queryByText(/left out/)).not.toBeInTheDocument());

    fireEvent.click(screen.getByRole('button', { name: 'Refresh notifications' }));
    expect(await screen.findByText(omittedNotice(5))).toBeInTheDocument();
  });

  it('says "1 highlight was left out" for one', () => {
    expect(omittedNotice(1)).toBe('1 highlight was left out; open the Spaarke Console for all of them.');
  });
});

describe('Briefing widget: failures', () => {
  it('shows the component error state with the tool message when the tool failed', async () => {
    const callTool = hostCalling(
      { reasonCode: 'briefing.timeout', message: 'The briefing took too long to generate.', status: 504 },
      { isError: true }
    );
    renderWith({ isConnected: true, toolResult: null, callTool });

    expect(await screen.findByText('Could not load daily briefing.')).toBeInTheDocument();
    expect(screen.getByText(/took too long to generate/)).toBeInTheDocument();
  });

  it('shows the unavailable state for a 503', async () => {
    const callTool = hostCalling(
      { reasonCode: 'briefing.unavailable', message: 'down', status: 503 },
      { isError: true }
    );
    renderWith({ isConnected: true, toolResult: null, callTool });

    expect(await screen.findByText('Daily briefing temporarily unavailable.')).toBeInTheDocument();
    expect(screen.getByText(/temporarily unavailable/i, { selector: 'div' })).toBeInTheDocument();
  });

  it('shows the error state, not a hang, when the host call rejects', async () => {
    const callTool = jest.fn().mockRejectedValue(new Error('bridge down'));
    renderWith({ isConnected: true, toolResult: null, callTool });

    expect(await screen.findByText('Could not load daily briefing.')).toBeInTheDocument();
  });
});

describe('Briefing widget: shell states', () => {
  it('shows the connecting indicator before the handshake', () => {
    renderWith({ isConnected: false });
    expect(screen.getByText('Connecting to the host')).toBeInTheDocument();
  });

  it('says so when the connection failed', () => {
    renderWith({ isConnected: false, error: new Error('boom') });
    expect(screen.getByRole('alert')).toHaveTextContent('could not connect to the host');
    expect(screen.queryByText(/boom/)).not.toBeInTheDocument();
  });
});

describe('Briefing widget: theme follows the host context', () => {
  const providerRoot = () => document.querySelector('[data-theme]') as HTMLElement;
  const backgroundOf = (el: HTMLElement) => getComputedStyle(el).getPropertyValue('--colorNeutralBackground1');

  it('is dark when the host is dark and light when it is light, without a remount', () => {
    const view = renderWith({ isConnected: false, hostContext: { theme: 'dark' } });
    const main = screen.getByRole('main');
    expect(providerRoot().dataset.theme).toBe('dark');
    expect(backgroundOf(providerRoot())).toBe(webDarkTheme.colorNeutralBackground1);

    view.update({ isConnected: false, hostContext: { theme: 'light' } });

    expect(screen.getByRole('main')).toBe(main);
    expect(providerRoot().dataset.theme).toBe('light');
    expect(backgroundOf(providerRoot())).toBe(webLightTheme.colorNeutralBackground1);
  });

  it('writes nothing to browser storage', () => {
    const setItem = jest.spyOn(Storage.prototype, 'setItem');
    try {
      const view = renderWith({ isConnected: false, hostContext: { theme: 'dark' } });
      view.update({ isConnected: false, hostContext: { theme: 'light' } });
      expect(setItem).not.toHaveBeenCalled();
    } finally {
      setItem.mockRestore();
    }
  });
});
