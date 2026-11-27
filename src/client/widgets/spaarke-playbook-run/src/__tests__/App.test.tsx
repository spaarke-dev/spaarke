/**
 * Widget-level tests for the playbook run MCP App (spaarkeai-mcp-bff-api-r2 task 037c; spec FR-16, NFR-09).
 *
 * The widget's own `App` under a plain host connection (no ext-apps SDK). The run arrives as the result that opened the widget (the start or
 * status tool's `structuredContent`); polls and the results call go through the host's `callTool`. Jest's fake timers drive the schedule (no
 * test reads the real clock). The tests prove: the bounded poll schedule (its delays, its stop on a finished run, at the 50-minute cap and after
 * repeated failures); the results tool is called ONCE per finished run (037b K1); every state renders in plain words (running, available, none,
 * pending, not-found, error); section text is safe Markdown; truncation shows "More in Spaarke"; every link opens through the host's openLink;
 * ZERO fetch / Xrm use; the theme follows the host; the controls are buttons (keyboard operable).
 */
import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { App, RESULT_WAIT_MS } from '../App';
import { McpHostContext, type McpHostConnection } from '../../../shared/mcp/mcpHostContext';
import { NOT_FOUND_REASON, RUN_CAP_MS } from '../run/playbookRun';

const RUN = '0b8a1f5e-2d4c-4e6b-9a7f-1c2d3e4f5a6b';
const PLAYBOOK = '11111111-2222-4333-8444-555555555555';
const START_LINK = 'https://org.example.crm/main.aspx?pagetype=webresource&webresourceName=sprk_spaarkeai&data=doc';
const ANALYSIS_LINK =
  'https://org.example.crm/main.aspx?pagetype=webresource&webresourceName=sprk_spaarkeai&data=analysis';
const DOC_LINK = 'https://org.example.crm/main.aspx?pagetype=webresource&webresourceName=sprk_spaarkeai&data=doc-1';

const STATUS_TOOL = 'spaarke_get_playbook_run';
const RESULTS_TOOL = 'spaarke_get_playbook_run_results';

function started() {
  return { runId: RUN, status: 'running', consoleLink: START_LINK };
}

function status(state: 'running' | 'completed' | 'failed', extra: Record<string, unknown> = {}) {
  return {
    runId: RUN,
    status: state,
    ...(state === 'running' ? { progress: 50 } : {}),
    result: {
      playbookId: PLAYBOOK,
      outcome: state,
      startedAt: new Date(Date.now()).toISOString(),
      completedNodes: 1,
      failedNodes: 0,
      skippedNodes: 0,
      summary: 'counts only',
      ...extra,
    },
  };
}

function results(overrides: Record<string, unknown> = {}) {
  return {
    runId: RUN,
    status: 'completed',
    outcome: 'completed',
    results: 'available',
    playbookId: PLAYBOOK,
    playbookName: 'Summarize an NDA',
    analysisId: '99999999-8888-4777-8666-555555555555',
    sections: [
      {
        heading: 'Summary',
        text: 'The NDA is **mutual**.\n\n- Term: 2 years\n- Governing law: Delaware',
        truncated: false,
      },
      { heading: 'Risks', text: 'Broad *residuals* clause.', truncated: false },
    ],
    truncated: false,
    consoleLink: ANALYSIS_LINK,
    documents: [
      { documentId: 'd1', name: 'Acme NDA.pdf', link: DOC_LINK },
      { documentId: 'd2', name: 'Side letter.docx' },
    ],
    ...overrides,
  };
}

type Answer = { structuredContent?: unknown; isError?: boolean; content?: unknown[] } | Error;

/** A host whose callTool answers from per-tool queues (the last answer repeats). */
function host(
  opening: unknown,
  answers: Partial<Record<string, Answer[]>> = {},
  extra: Partial<McpHostConnection> = {}
) {
  const calls: Array<{ name: string; args: Record<string, unknown> }> = [];
  const callTool = jest.fn(async (name: string, args: Record<string, unknown>) => {
    calls.push({ name, args });
    const queue = answers[name] ?? [];
    const answer = queue.length > 1 ? queue.shift()! : queue[0];
    if (!answer) return { content: [{ type: 'text', text: 'no answer configured' }], isError: true };
    if (answer instanceof Error) throw answer;
    return answer;
  });
  const openLink = jest.fn(async (_url: string) => true);
  const connection: McpHostConnection = {
    isConnected: true,
    error: null,
    hostContext: { theme: 'light' },
    toolResult: opening,
    callTool: callTool as McpHostConnection['callTool'],
    openLink,
    requestDisplayMode: async () => null,
    ...extra,
  };
  return { connection, calls, callTool, openLink };
}

function renderWith(connection: McpHostConnection) {
  return render(
    <McpHostContext.Provider value={connection}>
      <App />
    </McpHostContext.Provider>
  );
}

async function advance(ms: number) {
  await act(async () => {
    await jest.advanceTimersByTimeAsync(ms);
  });
}

const ok = (structuredContent: unknown) => ({ structuredContent, content: [{ type: 'text', text: 'line' }] });
const fail = (reasonCode: string, statusCode: number) => ({
  isError: true,
  structuredContent: { reasonCode, message: 'server message', status: statusCode },
  content: [{ type: 'text', text: 'server message' }],
});

const fetchSpy = jest.fn();
const xrmAccess = jest.fn();

beforeEach(() => {
  jest.useFakeTimers({ now: new Date('2026-11-27T09:00:00Z') });
  fetchSpy.mockReset();
  xrmAccess.mockClear();
  (globalThis as { fetch?: unknown }).fetch = fetchSpy;
  Object.defineProperty(window, 'Xrm', {
    configurable: true,
    get: () => {
      xrmAccess();
      return undefined;
    },
  });
});

afterEach(() => {
  jest.useRealTimers();
  expect(fetchSpy).not.toHaveBeenCalled();
  expect(xrmAccess).not.toHaveBeenCalled();
});

describe('progress and the poll schedule', () => {
  it('opens from the start result: header, Running badge, a progress bar, and the first poll after 2 s', async () => {
    const h = host(started(), { [STATUS_TOOL]: [ok(status('running'))] });
    renderWith(h.connection);

    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('Playbook run');
    expect(screen.getByText('Running')).toBeInTheDocument();
    expect(screen.getByRole('progressbar', { name: 'Run progress' })).toBeInTheDocument();
    expect(screen.getByText('Starting the run.')).toBeInTheDocument();

    await advance(1_999);
    expect(h.calls).toHaveLength(0);
    await advance(1);
    expect(h.calls).toEqual([{ name: STATUS_TOOL, args: { runId: RUN } }]);
    expect(screen.getByText('50% done, 1 step finished.')).toBeInTheDocument();
  });

  it('backs off (2, 3, 5, 8, 13, 20, then every 30 s)', async () => {
    const h = host(started(), { [STATUS_TOOL]: [ok(status('running'))] });
    renderWith(h.connection);

    const at: number[] = [];
    const start = Date.now();
    for (let elapsed = 0; elapsed < 140_000; elapsed += 1_000) {
      const before = h.calls.length;
      await advance(1_000);
      if (h.calls.length > before) at.push(Date.now() - start);
    }
    expect(at).toEqual([2_000, 5_000, 10_000, 18_000, 31_000, 51_000, 81_000, 111_000]);
    expect(h.calls.every(c => c.name === STATUS_TOOL)).toBe(true);
  });

  it('stops polling on a finished run and calls the results tool ONCE', async () => {
    const h = host(started(), {
      [STATUS_TOOL]: [ok(status('running')), ok(status('completed'))],
      [RESULTS_TOOL]: [ok(results())],
    });
    renderWith(h.connection);

    await advance(2_000);
    await advance(3_000);
    expect(h.calls.map(c => c.name)).toEqual([STATUS_TOOL, STATUS_TOOL, RESULTS_TOOL]);
    expect(h.calls[2].args).toEqual({ runId: RUN });

    await advance(60 * 60 * 1000);
    expect(h.calls).toHaveLength(3);
    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('Summarize an NDA');
    expect(screen.getByText('Completed')).toBeInTheDocument();
    expect(screen.queryByRole('progressbar')).toBeNull();
  });

  it('stops at the 50-minute cap counted from the run start, says so, and "Check again" polls once', async () => {
    const startedAt = new Date(Date.now() - (RUN_CAP_MS - 10_000)).toISOString();
    const h = host(status('running', { startedAt }), { [STATUS_TOOL]: [ok(status('running', { startedAt }))] });
    renderWith(h.connection);

    await advance(2_000); // poll 1
    await advance(3_000); // poll 2
    await advance(5_000); // poll 3, at the cap exactly
    expect(h.calls).toHaveLength(3);
    expect(screen.getByText(/still going after 50 minutes/)).toBeInTheDocument();

    await advance(60 * 60 * 1000);
    expect(h.calls).toHaveLength(3);

    fireEvent.click(screen.getByRole('button', { name: 'Check again' }));
    await advance(0);
    expect(h.calls).toHaveLength(4);
    await advance(60 * 60 * 1000);
    expect(h.calls).toHaveLength(4);
    expect(screen.getByText(/still going after 50 minutes/)).toBeInTheDocument();
    // Opened from a status result: no console link is known, so none is offered.
    expect(screen.queryByRole('button', { name: 'Open in Spaarke' })).toBeNull();
  });

  it('a start result with no start time is capped 50 minutes after the widget first saw it', async () => {
    const h = host(started(), { [STATUS_TOOL]: [ok({ runId: RUN, status: 'running' })] });
    renderWith(h.connection);

    await advance(RUN_CAP_MS);
    const polls = h.calls.length;
    expect(polls).toBeGreaterThan(90);
    expect(polls).toBeLessThanOrEqual(110);
    expect(screen.getByText(/still going after 50 minutes/)).toBeInTheDocument();
    await advance(RUN_CAP_MS);
    expect(h.calls).toHaveLength(polls);
  });

  it('stops after 5 failed polls in a row and offers "Check again", which resumes the schedule', async () => {
    const h = host(started(), {
      [STATUS_TOOL]: [
        new Error('bridge down'),
        new Error('bridge down'),
        new Error('bridge down'),
        new Error('bridge down'),
        new Error('bridge down'),
        ok(status('running')),
      ],
    });
    renderWith(h.connection);

    await advance(2_000 + 3_000 + 5_000 + 8_000 + 13_000);
    expect(h.calls).toHaveLength(5);
    expect(screen.getByText(/did not answer for a while/)).toBeInTheDocument();
    await advance(10 * 60 * 1000);
    expect(h.calls).toHaveLength(5);

    // The start's console link, through the host.
    fireEvent.click(screen.getByRole('button', { name: 'Open in Spaarke' }));
    await advance(0);
    expect(h.openLink).toHaveBeenCalledWith(START_LINK);

    fireEvent.click(screen.getByRole('button', { name: 'Check again' }));
    await advance(0);
    expect(h.calls).toHaveLength(6);
    expect(screen.queryByText(/did not answer for a while/)).toBeNull();
    await advance(30_000);
    expect(h.calls.length).toBeGreaterThan(6);
  });

  it('a not-found poll stops polling and says so', async () => {
    const h = host(started(), { [STATUS_TOOL]: [fail(NOT_FOUND_REASON, 404)] });
    renderWith(h.connection);

    await advance(2_000);
    expect(screen.getByRole('alert')).toHaveTextContent(
      'This playbook run was not found, or you can no longer open it.'
    );
    await advance(10 * 60 * 1000);
    expect(h.calls).toHaveLength(1);
  });

  it('a new opening result clears the old schedule', async () => {
    const h = host(started(), { [STATUS_TOOL]: [ok(status('running'))] });
    const { rerender } = renderWith(h.connection);
    await advance(1_000);

    const other = '22222222-3333-4444-8555-666666666666';
    rerender(
      <McpHostContext.Provider
        value={{ ...h.connection, toolResult: { runId: other, status: 'running', consoleLink: START_LINK } }}
      >
        <App />
      </McpHostContext.Provider>
    );
    await advance(1_999);
    expect(h.calls).toHaveLength(0);
    await advance(1);
    expect(h.calls).toEqual([{ name: STATUS_TOOL, args: { runId: other } }]);
  });

  it('unmounting stops polling', async () => {
    const h = host(started(), { [STATUS_TOOL]: [ok(status('running'))] });
    const { unmount } = renderWith(h.connection);
    unmount();
    await advance(10 * 60 * 1000);
    expect(h.calls).toHaveLength(0);
  });
});

describe('results', () => {
  it('a run already finished when the widget opens: the results at once, no status poll', async () => {
    const h = host(status('completed'), { [RESULTS_TOOL]: [ok(results())] });
    renderWith(h.connection);
    await advance(0);

    expect(h.calls.map(c => c.name)).toEqual([RESULTS_TOOL]);
    // Collapsible sections: the first open, the rest closed; headers are buttons (keyboard operable).
    const summary = screen.getByRole('button', { name: 'Summary' });
    const risks = screen.getByRole('button', { name: 'Risks' });
    expect(summary).toHaveAttribute('aria-expanded', 'true');
    expect(risks).toHaveAttribute('aria-expanded', 'false');
    expect(screen.getByText('mutual').tagName).toBe('STRONG');
    expect(screen.getByText('Term: 2 years').tagName).toBe('LI');

    fireEvent.click(risks);
    expect(risks).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByText('residuals').tagName).toBe('EM');
    fireEvent.click(summary);
    expect(summary).toHaveAttribute('aria-expanded', 'false');
  });

  it('the console link and each document open through the host; a document without a link is plain text', async () => {
    const h = host(status('completed'), { [RESULTS_TOOL]: [ok(results())] });
    const { container } = renderWith(h.connection);
    await advance(0);

    fireEvent.click(screen.getByRole('button', { name: 'Open in Spaarke' }));
    fireEvent.click(screen.getByRole('button', { name: 'Acme NDA.pdf' }));
    await advance(0);
    expect(h.openLink.mock.calls).toEqual([[ANALYSIS_LINK], [DOC_LINK]]);
    expect(
      within(screen.getByRole('region', { name: 'Source documents' })).getByText('Side letter.docx').tagName
    ).not.toBe('BUTTON');
    // No anchor at all: nothing for the frame to navigate.
    expect(container.querySelector('a')).toBeNull();
  });

  it('a link the host refuses shows a warning; one it opens clears it', async () => {
    const openLink = jest
      .fn()
      .mockResolvedValueOnce(false)
      .mockResolvedValueOnce(true)
      .mockRejectedValueOnce(new Error('no'));
    const h = host(status('completed'), { [RESULTS_TOOL]: [ok(results())] }, { openLink });
    renderWith(h.connection);
    await advance(0);

    const button = screen.getByRole('button', { name: 'Open in Spaarke' });
    fireEvent.click(button);
    await advance(0);
    expect(screen.getByText(/did not open that link/)).toBeInTheDocument();
    fireEvent.click(button);
    await advance(0);
    expect(screen.queryByText(/did not open that link/)).toBeNull();
    fireEvent.click(button);
    await advance(0);
    expect(screen.getByText(/did not open that link/)).toBeInTheDocument();
  });

  it('truncated: a cut section says so and "More in Spaarke" opens the moreLink through the host', async () => {
    const h = host(status('completed'), {
      [RESULTS_TOOL]: [
        ok(
          results({
            sections: [{ heading: 'Long', text: 'Part of it', truncated: true }],
            truncated: true,
            moreLink: ANALYSIS_LINK,
          })
        ),
      ],
    });
    renderWith(h.connection);
    await advance(0);

    expect(screen.getByText('This section was shortened. The full text is in Spaarke.')).toBeInTheDocument();
    expect(screen.getByText('Some of the results were left out to keep this short.')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'More in Spaarke' }));
    await advance(0);
    expect(h.openLink).toHaveBeenCalledWith(ANALYSIS_LINK);
  });

  it('not truncated: no "More in Spaarke" (and a moreLink without truncated is ignored)', async () => {
    const h = host(status('completed'), { [RESULTS_TOOL]: [ok(results({ moreLink: ANALYSIS_LINK }))] });
    renderWith(h.connection);
    await advance(0);
    expect(screen.queryByRole('button', { name: 'More in Spaarke' })).toBeNull();
    expect(screen.queryByText(/were left out/)).toBeNull();
  });

  it('section text with HTML or script is rendered inert', async () => {
    const payload = '<img src=x onerror="window.__pwned=1"><script>window.__pwned=1</script>';
    const h = host(status('completed'), {
      [RESULTS_TOOL]: [ok(results({ sections: [{ text: payload, truncated: false }] }))],
    });
    const { container } = renderWith(h.connection);
    await advance(0);

    expect(container.querySelector('img, script, iframe')).toBeNull();
    expect((window as unknown as { __pwned?: number }).__pwned).toBeUndefined();
    expect(screen.getByText(payload)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Results' })).toBeInTheDocument();
  });

  it('"none": plain words and the console link', async () => {
    const h = host(status('completed'), { [RESULTS_TOOL]: [ok(results({ results: 'none', sections: [] }))] });
    renderWith(h.connection);
    await advance(0);
    expect(screen.getByText('The run finished but saved no results to show here.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Open in Spaarke' })).toBeInTheDocument();
  });

  it('a failed run: the Failed badge, the plain sentence, and its (none) results', async () => {
    const h = host(status('failed'), {
      [RESULTS_TOOL]: [ok(results({ status: 'failed', outcome: 'interrupted', results: 'none', sections: [] }))],
    });
    renderWith(h.connection);
    await advance(0);
    expect(screen.getByText('Interrupted')).toBeInTheDocument();
    expect(screen.getByText(/was interrupted/)).toBeInTheDocument();
    expect(screen.getByText('There are no results to show for this run.')).toBeInTheDocument();
  });

  it('"pending": says the results are still being saved; "Check again" asks the results tool again (never polled)', async () => {
    const h = host(status('completed'), {
      [RESULTS_TOOL]: [
        ok(results({ status: 'running', outcome: 'running', results: 'pending', sections: [] })),
        ok(results()),
      ],
    });
    renderWith(h.connection);
    await advance(0);
    expect(screen.getByText('The run has finished and is still saving its results.')).toBeInTheDocument();
    await advance(10 * 60 * 1000);
    expect(h.calls).toHaveLength(1);

    fireEvent.click(screen.getByRole('button', { name: 'Check again' }));
    await advance(0);
    expect(h.calls.map(c => c.name)).toEqual([RESULTS_TOOL, RESULTS_TOOL]);
    expect(screen.getByRole('button', { name: 'Summary' })).toBeInTheDocument();
  });

  it('not-found: the uniform words, no retry', async () => {
    const h = host(status('completed'), { [RESULTS_TOOL]: [fail(NOT_FOUND_REASON, 404)] });
    renderWith(h.connection);
    await advance(0);
    expect(screen.getByRole('alert')).toHaveTextContent(
      'This playbook run was not found, or you can no longer open it.'
    );
    expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull();
  });

  it('an error: plain words (never the server text) and "Try again"', async () => {
    const h = host(status('completed'), { [RESULTS_TOOL]: [fail('playbook.run-read-failed', 503), ok(results())] });
    renderWith(h.connection);
    await advance(0);
    expect(screen.getByRole('alert')).toHaveTextContent(
      'Spaarke could not read this playbook run right now. Check again in a moment.'
    );
    expect(screen.queryByText('server message')).toBeNull();

    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));
    await advance(0);
    expect(screen.getByRole('button', { name: 'Summary' })).toBeInTheDocument();
  });

  it('an opening error result (the start was refused) is shown, and nothing is polled', async () => {
    const h = host({ reasonCode: 'sdap.access.deny.record_unavailable', message: 'x', status: 404 });
    renderWith(h.connection);
    await advance(60_000);
    expect(screen.getByRole('alert')).toHaveTextContent('not found');
    expect(h.calls).toHaveLength(0);
  });
});

describe('the host', () => {
  it('no result arriving says so after a while', async () => {
    const h = host(null);
    renderWith(h.connection);
    expect(screen.getByText('Waiting for the playbook run')).toBeInTheDocument();
    await advance(RESULT_WAIT_MS);
    expect(screen.getByRole('alert')).toHaveTextContent('No playbook run arrived. Ask again in the chat.');
  });

  it('follows the host theme', () => {
    const light = host(started());
    const { container, rerender } = renderWith(light.connection);
    expect(container.querySelector('[data-theme]')).toHaveAttribute('data-theme', 'light');
    rerender(
      <McpHostContext.Provider value={{ ...light.connection, hostContext: { theme: 'dark' } }}>
        <App />
      </McpHostContext.Provider>
    );
    expect(container.querySelector('[data-theme]')).toHaveAttribute('data-theme', 'dark');
  });

  it('not connected: a spinner and no calls; a connection error says so', async () => {
    const h = host(started(), {}, { isConnected: false });
    const { rerender } = renderWith(h.connection);
    expect(screen.getByText('Connecting to the host')).toBeInTheDocument();
    await advance(60_000);
    expect(h.calls).toHaveLength(0);
    rerender(
      <McpHostContext.Provider value={{ ...h.connection, error: new Error('handshake') }}>
        <App />
      </McpHostContext.Provider>
    );
    expect(screen.getByRole('alert')).toHaveTextContent('could not connect to the host');
  });
});
