/**
 * Widget-level tests for the search results MCP App (spaarkeai-mcp-bff-api-r2 task 035; spec FR-16, NFR-09).
 *
 * The real `SearchResultsWidget` (the Console's component) is mounted through the widget's own `App`, under a plain host connection (no
 * ext-apps SDK). The hits arrive as the result that opened the widget (task 022's `structuredContent`); further pages come from a tool call
 * through the host. The tests prove: hits draw; ZERO `fetch` / `Xrm` use; a link opens only through the host's `openLink`; actions the widget
 * cannot do are not drawn (no Load more without the call's arguments or a next offset, no score badge, a hit without a link is plain text);
 * 022's paging contract (`nextOffset` continues, a document search over every record advises narrowing); a trimmed or partial result says so;
 * AI/user text stays text; the theme follows the host. No test reads the clock: the "no result arrived" case uses jest's fake timers.
 */
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { App } from '../App';
import { DISCONNECTED, McpHostContext, type McpHostConnection } from '../../../shared/mcp/mcpHostContext';

const DOC_URL =
  'https://org.example.crm/main.aspx?pagetype=entityrecord&etn=sprk_document&id=11111111-1111-1111-1111-111111111111';
const MATTER_URL =
  'https://org.example.crm/main.aspx?pagetype=entityrecord&etn=sprk_matter&id=22222222-2222-2222-2222-222222222222';

function documentsResult(overrides: Record<string, unknown> = {}) {
  return {
    items: [
      {
        id: 'doc-1',
        name: 'Master services agreement',
        fileType: 'pdf',
        parentType: 'matter',
        parentName: 'Acme v. Globex',
        snippet: 'The supplier shall indemnify the customer against third party claims.',
        openLink: DOC_URL,
      },
      { id: 'doc-2', name: 'Side letter', fileType: 'docx', snippet: 'No link for this one.' },
    ],
    limit: 20,
    offset: 0,
    hasMore: false,
    truncated: false,
    nextOffset: null,
    ...overrides,
  };
}

function recordsResult(overrides: Record<string, unknown> = {}) {
  return {
    items: [
      {
        id: 'rec-1',
        name: 'Acme v. Globex',
        type: 'matter',
        snippet: 'Commercial dispute.',
        referenceNumbers: ['2026-001', 'GX-9'],
        openLink: MATTER_URL,
      },
    ],
    limit: 20,
    offset: 0,
    hasMore: false,
    truncated: false,
    nextOffset: null,
    ...overrides,
  };
}

const fetchSpy = jest.fn();
const xrmAccess = jest.fn();

beforeEach(() => {
  fetchSpy.mockReset();
  xrmAccess.mockClear();
  (globalThis as { fetch?: unknown }).fetch = fetchSpy;
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
  delete (globalThis as { fetch?: unknown }).fetch;
});

function renderWith(connection: Partial<McpHostConnection>, resultWaitMs?: number) {
  const value: McpHostConnection = { ...DISCONNECTED, ...connection };
  const view = render(
    <McpHostContext.Provider value={value}>
      <App resultWaitMs={resultWaitMs} />
    </McpHostContext.Provider>
  );
  const update = (next: Partial<McpHostConnection>) =>
    view.rerender(
      <McpHostContext.Provider value={{ ...value, ...next }}>
        <App resultWaitMs={resultWaitMs} />
      </McpHostContext.Provider>
    );
  return { ...view, update };
}

function hostCalling(structuredContent: Record<string, unknown>, extra: Record<string, unknown> = {}) {
  return jest.fn().mockResolvedValue({ structuredContent, content: [{ type: 'text', text: 'ok' }], ...extra });
}

const noNetworkOrXrm = () => {
  expect(fetchSpy).not.toHaveBeenCalled();
  expect(xrmAccess).not.toHaveBeenCalled();
};

describe('Search results widget: the opening result', () => {
  it('draws document hits with the matter and snippet, and uses no fetch and no Xrm', () => {
    const callTool = jest.fn();
    renderWith({ isConnected: true, toolResult: documentsResult(), toolInput: { query: 'indemnification' }, callTool });

    expect(screen.getByText('Master services agreement')).toBeInTheDocument();
    expect(screen.getByText(/pdf · in matter Acme v\. Globex — The supplier shall indemnify/)).toBeInTheDocument();
    expect(screen.getByText('indemnification')).toBeInTheDocument();
    expect(callTool).not.toHaveBeenCalled();
    noNetworkOrXrm();
  });

  it('draws record hits with the type, reference numbers and description', () => {
    renderWith({ isConnected: true, toolResult: recordsResult(), toolInput: { query: 'acme' } });

    expect(screen.getByText('Acme v. Globex')).toBeInTheDocument();
    expect(screen.getByText('Matter · ref 2026-001, GX-9 — Commercial dispute.')).toBeInTheDocument();
  });

  it('says "no results found" for an empty page, and offers nothing to continue', () => {
    renderWith({ isConnected: true, toolResult: documentsResult({ items: [] }), toolInput: { query: 'zzz' } });

    expect(screen.getByText('No results found.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /load more/i })).not.toBeInTheDocument();
  });

  it('falls back to a neutral heading when the host sent no arguments', () => {
    renderWith({ isConnected: true, toolResult: documentsResult() });
    expect(screen.getByText('your search')).toBeInTheDocument();
  });

  it('hides the relevance badge: the tools expose no score, and a 0% badge would be false', () => {
    renderWith({ isConnected: true, toolResult: documentsResult(), toolInput: { query: 'q' } });

    const badge = screen.getAllByText('0%')[0];
    const slot = badge.closest('.fui-CardHeader__action') as HTMLElement;
    expect(slot).not.toBeNull();
    expect(getComputedStyle(slot).display).toBe('none'); // the component still renders the badge; the widget's own rule hides its slot
  });

  it('renders hit text as text, never as HTML', () => {
    const hostile = documentsResult({
      items: [{ id: 'doc-x', name: '<img src=x onerror=alert(1)>', snippet: '<b>bold</b><script>boom()</script>' }],
    });
    const { container } = renderWith({ isConnected: true, toolResult: hostile, toolInput: { query: 'q' } });

    expect(screen.getByText('<img src=x onerror=alert(1)>')).toBeInTheDocument();
    expect(screen.getByText('<b>bold</b><script>boom()</script>')).toBeInTheDocument();
    expect(container.querySelector('img')).toBeNull();
    expect(container.querySelector('script')).toBeNull();
    expect(container.querySelector('b')).toBeNull();
  });

  it('shows the tool failure text for an error result, not a list', () => {
    renderWith({
      isConnected: true,
      toolResult: { reasonCode: 'QUERY_REQUIRED', message: 'Send a search query.', status: 400 },
      toolInput: {},
    });

    expect(screen.getByRole('alert')).toHaveTextContent('Send a search query.');
    expect(screen.queryByText('No results found.')).not.toBeInTheDocument();
  });
});

describe('Search results widget: links go through the host', () => {
  it('a click on a hit title calls the host openLink with the hit link, and the frame is not navigated', async () => {
    const openLink = jest.fn().mockResolvedValue(true);
    renderWith({ isConnected: true, toolResult: documentsResult(), toolInput: { query: 'q' }, openLink });

    const link = screen.getByRole('link', { name: 'Master services agreement' });
    const notPrevented = fireEvent.click(link);

    expect(notPrevented).toBe(false); // preventDefault was called: the anchor does not navigate the frame
    await waitFor(() => expect(openLink).toHaveBeenCalledTimes(1));
    expect(openLink).toHaveBeenCalledWith(DOC_URL);
    noNetworkOrXrm();
  });

  it('is keyboard operable: the title link is a focusable link and Enter activates it', async () => {
    const openLink = jest.fn().mockResolvedValue(true);
    renderWith({ isConnected: true, toolResult: documentsResult(), toolInput: { query: 'q' }, openLink });

    const link = screen.getByRole('link', { name: 'Master services agreement' });
    link.focus();
    expect(link).toHaveFocus();
    // A browser turns Enter on a focused anchor into a click; jsdom does not, so the test sends that click.
    fireEvent.keyDown(link, { key: 'Enter' });
    fireEvent.click(link);

    await waitFor(() => expect(openLink).toHaveBeenCalledWith(DOC_URL));
  });

  it('a middle click is also taken by the host, not the frame', async () => {
    const openLink = jest.fn().mockResolvedValue(true);
    renderWith({ isConnected: true, toolResult: documentsResult(), toolInput: { query: 'q' }, openLink });

    const notPrevented = fireEvent(
      screen.getByRole('link', { name: 'Master services agreement' }),
      new MouseEvent('auxclick', { bubbles: true, cancelable: true, button: 1 })
    );

    expect(notPrevented).toBe(false);
    await waitFor(() => expect(openLink).toHaveBeenCalledWith(DOC_URL));
  });

  it('a right click (the context-menu button) is left to the browser: no openLink, no preventDefault', async () => {
    const openLink = jest.fn().mockResolvedValue(true);
    renderWith({ isConnected: true, toolResult: documentsResult(), toolInput: { query: 'q' }, openLink });

    // Chromium fires auxclick for every non-primary button; button 2 is the context menu, not an open.
    const notPrevented = fireEvent(
      screen.getByRole('link', { name: 'Master services agreement' }),
      new MouseEvent('auxclick', { bubbles: true, cancelable: true, button: 2 })
    );

    expect(notPrevented).toBe(true);
    await act(async () => {
      await Promise.resolve();
    });
    expect(openLink).not.toHaveBeenCalled();
  });

  it('draws a hit without a link as plain text: no dead link', () => {
    renderWith({ isConnected: true, toolResult: documentsResult(), toolInput: { query: 'q' } });

    expect(screen.getByText('Side letter')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Side letter' })).not.toBeInTheDocument();
    expect(screen.getAllByRole('link')).toHaveLength(1);
  });

  it.each([['javascript:alert(1)'], ['data:text/html,<b>x</b>'], ['ftp://files.example/x'], ['not a url']])(
    'never links %s (only http and https reach the host)',
    async unsafe => {
      const openLink = jest.fn().mockResolvedValue(true);
      renderWith({
        isConnected: true,
        toolResult: documentsResult({ items: [{ id: 'd', name: 'Unsafe link', openLink: unsafe }] }),
        toolInput: { query: 'q' },
        openLink,
      });

      expect(screen.getByText('Unsafe link')).toBeInTheDocument();
      expect(screen.queryByRole('link')).not.toBeInTheDocument();
      expect(openLink).not.toHaveBeenCalled();
    }
  );

  it('says so when the host did not open the link, and when it rejects', async () => {
    const openLink = jest
      .fn()
      .mockResolvedValueOnce(false)
      .mockResolvedValueOnce(true)
      .mockRejectedValueOnce(new Error('bridge'));
    renderWith({ isConnected: true, toolResult: documentsResult(), toolInput: { query: 'q' }, openLink });
    const link = screen.getByRole('link', { name: 'Master services agreement' });

    fireEvent.click(link); // the host says no: the warning shows
    expect(await screen.findByText(/Your host did not open that link/)).toBeInTheDocument();

    fireEvent.click(link); // the host opens it: the warning goes
    await waitFor(() => expect(openLink).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(screen.queryByText(/Your host did not open that link/)).not.toBeInTheDocument());

    fireEvent.click(link); // the host rejects: the warning is back
    await waitFor(() => expect(openLink).toHaveBeenCalledTimes(3));
    expect(await screen.findByText(/Your host did not open that link/)).toBeInTheDocument();
  });
});

describe('Search results widget: paging (022 contract)', () => {
  it('offers Load more when the tool advised a next offset, calls the same tool with that offset and the call arguments, and appends', async () => {
    const callTool = hostCalling(
      documentsResult({ items: [{ id: 'doc-3', name: 'Third document' }], offset: 20, nextOffset: null })
    );
    renderWith({
      isConnected: true,
      toolResult: documentsResult({ nextOffset: 20, hasMore: true }),
      toolInput: {
        query: 'indemnification',
        entityType: 'matter',
        entityId: 'e1e1e1e1-0000-4000-8000-000000000000',
        limit: 20,
        extra: 'ignored',
      },
      callTool,
    });

    fireEvent.click(screen.getByRole('button', { name: 'Load more' }));

    expect(await screen.findByText('Third document')).toBeInTheDocument();
    expect(callTool).toHaveBeenCalledTimes(1);
    expect(callTool).toHaveBeenCalledWith('spaarke_search_documents', {
      query: 'indemnification',
      offset: 20,
      limit: 20,
      entityType: 'matter',
      entityId: 'e1e1e1e1-0000-4000-8000-000000000000',
    });
    // The first page is still there, and with no further offset the button is gone.
    expect(screen.getByText('Master services agreement')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /load more/i })).not.toBeInTheDocument();
  });

  it('continues a record search through spaarke_find_records with recordTypes', async () => {
    const callTool = hostCalling(
      recordsResult({ items: [{ id: 'rec-2', name: 'Second matter', type: 'matter' }], nextOffset: null })
    );
    renderWith({
      isConnected: true,
      toolResult: recordsResult({ nextOffset: 20 }),
      toolInput: { query: 'acme', recordTypes: ['matter'] },
      callTool,
    });

    fireEvent.click(screen.getByRole('button', { name: 'Load more' }));

    expect(await screen.findByText('Second matter')).toBeInTheDocument();
    expect(callTool).toHaveBeenCalledWith('spaarke_find_records', {
      query: 'acme',
      offset: 20,
      recordTypes: ['matter'],
    });
  });

  it('does not repeat a hit a later page returns again', async () => {
    const callTool = hostCalling(documentsResult({ nextOffset: null }));
    renderWith({
      isConnected: true,
      toolResult: documentsResult({ nextOffset: 20 }),
      toolInput: { query: 'q' },
      callTool,
    });

    fireEvent.click(screen.getByRole('button', { name: 'Load more' }));

    await waitFor(() => expect(callTool).toHaveBeenCalled());
    await waitFor(() => expect(screen.queryByRole('button', { name: /load more/i })).not.toBeInTheDocument());
    expect(screen.getAllByText('Master services agreement')).toHaveLength(1);
  });

  it('keeps the list and says so when the next page fails', async () => {
    const callTool = jest.fn().mockResolvedValue({
      isError: true,
      structuredContent: {
        reasonCode: 'SEMANTIC_SEARCH_FAILED',
        message: 'The search could not be completed right now. Try again shortly.',
        status: 500,
      },
      content: [{ type: 'text', text: 'The search could not be completed right now. Try again shortly.' }],
    });
    renderWith({
      isConnected: true,
      toolResult: documentsResult({ nextOffset: 20 }),
      toolInput: { query: 'q' },
      callTool,
    });

    fireEvent.click(screen.getByRole('button', { name: 'Load more' }));

    expect(
      await screen.findByText('The search could not be completed right now. Try again shortly.')
    ).toBeInTheDocument();
    expect(screen.getByText('Master services agreement')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Load more' })).toBeEnabled(); // the same page can be asked again
  });

  it('shows the narrowing advice, and no Load more, for a full page of a search over every record (no offset advised)', () => {
    const full = documentsResult({ limit: 2, hasMore: false, nextOffset: null });
    renderWith({ isConnected: true, toolResult: full, toolInput: { query: 'q' } });

    expect(screen.queryByRole('button', { name: /load more/i })).not.toBeInTheDocument();
    expect(screen.getByText(/search inside one matter, project or invoice/i)).toBeInTheDocument();
  });

  it('offers Load more for the largest offset the tools accept, and says to narrow the search (no button) above it', () => {
    const at = renderWith({
      isConnected: true,
      toolResult: documentsResult({ nextOffset: 1000 }),
      toolInput: { query: 'q' },
      callTool: jest.fn(),
    });
    expect(screen.getByRole('button', { name: 'Load more' })).toBeInTheDocument();
    at.unmount();

    renderWith({
      isConnected: true,
      toolResult: documentsResult({ nextOffset: 1003 }),
      toolInput: { query: 'q' },
      callTool: jest.fn(),
    });
    expect(screen.queryByRole('button', { name: /load more/i })).not.toBeInTheDocument();
    expect(screen.getByText(/as far as paging goes.*narrow the search/i)).toBeInTheDocument();
    expect(screen.queryByText(/ask for the next page in the chat/i)).not.toBeInTheDocument();
  });

  it('says the list ended when Load more returns an empty page, and keeps the hits already shown', async () => {
    const callTool = hostCalling(documentsResult({ items: [], offset: 20, nextOffset: null }));
    renderWith({
      isConnected: true,
      toolResult: documentsResult({ nextOffset: 20 }),
      toolInput: { query: 'q' },
      callTool,
    });

    fireEvent.click(screen.getByRole('button', { name: 'Load more' }));

    expect(await screen.findByText('No more matches after these.')).toBeInTheDocument();
    expect(screen.getByText('Master services agreement')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /load more/i })).not.toBeInTheDocument();
    expect(screen.queryByText('No results found.')).not.toBeInTheDocument();
  });

  it('does not say "No results found." for an opening result at a later offset with no rows', () => {
    renderWith({
      isConnected: true,
      toolResult: documentsResult({ items: [], offset: 40, nextOffset: null }),
      toolInput: { query: 'q' },
    });

    expect(screen.getByText('No more matches after the first 40.')).toBeInTheDocument();
    expect(screen.queryByText('No results found.')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /load more/i })).not.toBeInTheDocument();
  });

  it('keeps keyboard focus on Load more while a page loads: the button stays focusable, with a stable label and a busy state', async () => {
    let finish: (value: unknown) => void = () => undefined;
    const callTool = jest.fn().mockReturnValue(
      new Promise(resolve => {
        finish = resolve;
      })
    );
    renderWith({
      isConnected: true,
      toolResult: documentsResult({ nextOffset: 20 }),
      toolInput: { query: 'q' },
      callTool,
    });
    const button = screen.getByRole('button', { name: 'Load more' });
    button.focus();
    expect(button).toHaveFocus();

    fireEvent.click(button);

    expect(await screen.findByText('Loading more results')).toBeInTheDocument();
    const loading = screen.getByRole('button', { name: 'Load more' }); // the label did not change
    expect(loading).toBe(button);
    expect(loading).not.toBeDisabled(); // a disabled button drops focus to the page
    expect(loading).toHaveAttribute('aria-disabled', 'true');
    expect(loading).toHaveFocus();
    expect(loading.closest('[aria-busy="true"]')).not.toBeNull();
    fireEvent.click(loading);
    expect(callTool).toHaveBeenCalledTimes(1); // asking again while loading does nothing

    await act(async () => {
      finish({
        structuredContent: documentsResult({ items: [{ id: 'doc-3', name: 'Third document' }], nextOffset: null }),
        content: [{ type: 'text', text: 'ok' }],
      });
    });
    expect(await screen.findByText('Third document')).toBeInTheDocument();
  });

  it('does not call a short page the end of the list', () => {
    renderWith({ isConnected: true, toolResult: documentsResult({ limit: 20 }), toolInput: { query: 'q' } });

    expect(screen.getByText('This may not be every match.')).toBeInTheDocument();
    expect(screen.queryByText(/no more results/i)).not.toBeInTheDocument();
  });

  it('hides Load more, and points at the chat, when the host sent no arguments (a further call needs the query)', () => {
    renderWith({ isConnected: true, toolResult: documentsResult({ nextOffset: 20 }) });

    expect(screen.queryByRole('button', { name: /load more/i })).not.toBeInTheDocument();
    expect(screen.getByText(/Ask for the next page in the chat/)).toBeInTheDocument();
  });

  it('starts the list again when the host delivers a new opening result', async () => {
    const callTool = hostCalling(
      documentsResult({ items: [{ id: 'doc-3', name: 'Third document' }], nextOffset: null })
    );
    const view = renderWith({
      isConnected: true,
      toolResult: documentsResult({ nextOffset: 20 }),
      toolInput: { query: 'q' },
      callTool,
    });
    fireEvent.click(screen.getByRole('button', { name: 'Load more' }));
    await screen.findByText('Third document');

    view.update({
      isConnected: true,
      toolResult: documentsResult({ items: [{ id: 'doc-9', name: 'A new search hit' }] }),
      toolInput: { query: 'q2' },
      callTool,
    });

    expect(await screen.findByText('A new search hit')).toBeInTheDocument();
    expect(screen.queryByText('Third document')).not.toBeInTheDocument();
    expect(screen.queryByText('Master services agreement')).not.toBeInTheDocument();
  });
});

describe('Search results widget: a trimmed or partial result says so', () => {
  it('warns when the tool left hits out', () => {
    renderWith({
      isConnected: true,
      toolResult: documentsResult({ truncated: true, nextOffset: 2 }),
      toolInput: { query: 'q' },
      callTool: jest.fn(),
    });
    expect(screen.getByText('Some results were left out to keep this list short.')).toBeInTheDocument();
  });

  it('warns when the index said the page may be incomplete', () => {
    renderWith({
      isConnected: true,
      toolResult: documentsResult({ warnings: ['PARTIAL_RESULTS'] }),
      toolInput: { query: 'q' },
    });
    expect(screen.getByText('These results may be incomplete.')).toBeInTheDocument();
  });

  it('shows no warning for a complete page', () => {
    renderWith({ isConnected: true, toolResult: recordsResult(), toolInput: { query: 'q' } });
    expect(screen.queryByText(/left out|incomplete/)).not.toBeInTheDocument();
  });
});

describe('Search results widget: connection states', () => {
  it('shows a status while the host connects, and an alert when it cannot', () => {
    const view = renderWith({ isConnected: false });
    expect(screen.getByText('Connecting to the host')).toBeInTheDocument();

    view.update({ isConnected: false, error: new Error('x') });
    expect(screen.getByRole('alert')).toHaveTextContent('The widget could not connect to the host.');
  });

  it('waits for the result, then says none arrived (fake timers; no wall clock)', () => {
    jest.useFakeTimers();
    try {
      renderWith({ isConnected: true }, 1000);
      expect(screen.getByText('Waiting for the search results')).toBeInTheDocument();

      act(() => {
        jest.advanceTimersByTime(1000);
      });
      expect(screen.getByRole('alert')).toHaveTextContent('No search results arrived');
    } finally {
      jest.useRealTimers();
    }
  });

  it('is a labelled landmark', () => {
    renderWith({ isConnected: true, toolResult: recordsResult(), toolInput: { query: 'q' } });
    expect(within(screen.getByRole('main')).getByText('Acme v. Globex')).toBeInTheDocument();
    expect(screen.getByRole('main', { name: 'Spaarke search results' })).toBeInTheDocument();
  });
});

describe('Search results widget: theme follows the host context', () => {
  const providerRoot = () => document.querySelector('[data-theme]') as HTMLElement;
  const backgroundOf = (el: HTMLElement) => getComputedStyle(el).getPropertyValue('--colorNeutralBackground1');

  it('is dark when the host is dark and light when it is light, without a remount', () => {
    const view = renderWith({
      isConnected: true,
      toolResult: recordsResult(),
      toolInput: { query: 'q' },
      hostContext: { theme: 'dark' },
    });
    const main = screen.getByRole('main');
    expect(providerRoot().dataset.theme).toBe('dark');
    expect(backgroundOf(providerRoot())).toBe(webDarkTheme.colorNeutralBackground1);

    view.update({
      isConnected: true,
      toolResult: recordsResult(),
      toolInput: { query: 'q' },
      hostContext: { theme: 'light' },
    });

    expect(screen.getByRole('main')).toBe(main);
    expect(providerRoot().dataset.theme).toBe('light');
    expect(backgroundOf(providerRoot())).toBe(webLightTheme.colorNeutralBackground1);
  });

  it('writes nothing to browser storage', () => {
    const setItem = jest.spyOn(Storage.prototype, 'setItem');
    try {
      const view = renderWith({
        isConnected: true,
        toolResult: recordsResult(),
        toolInput: { query: 'q' },
        hostContext: { theme: 'dark' },
      });
      view.update({
        isConnected: true,
        toolResult: recordsResult(),
        toolInput: { query: 'q' },
        hostContext: { theme: 'light' },
      });
      expect(setItem).not.toHaveBeenCalled();
    } finally {
      setItem.mockRestore();
    }
  });
});
