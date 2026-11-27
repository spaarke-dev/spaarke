/**
 * @jest-environment node
 *
 * The pure half of the playbook run widget (task 037c): parsing the three tool shapes (start, status, results; notes/037-decisions.md §5.4) and
 * the bounded poll schedule. No clock is read: every time is passed in.
 */
import {
  MAX_FAILED_POLLS,
  NOT_FOUND_REASON,
  POLL_DELAYS_MS,
  RUN_CAP_MS,
  failureOf,
  isTerminal,
  nextPoll,
  outcomeLabel,
  parseRunResults,
  parseRunState,
  pollDeadline,
  pollDelay,
  progressLine,
  safeLink,
} from '../run/playbookRun';

const RUN = '0b8a1f5e-2d4c-4e6b-9a7f-1c2d3e4f5a6b';
const PLAYBOOK = '11111111-2222-4333-8444-555555555555';
const LINK = 'https://org.example.crm/main.aspx?pagetype=webresource&webresourceName=sprk_spaarkeai';

describe('parseRunState', () => {
  it('reads the start result { runId, status, consoleLink }', () => {
    const parsed = parseRunState({ runId: RUN, status: 'running', consoleLink: LINK });
    expect(parsed).toEqual({
      ok: true,
      value: {
        runId: RUN,
        status: 'running',
        outcome: 'running',
        progress: null,
        completedNodes: null,
        failedNodes: null,
        skippedNodes: null,
        startedAt: null,
        consoleLink: LINK,
      },
    });
  });

  it('reads the status result with its progress, counts, outcome and start time', () => {
    const parsed = parseRunState({
      runId: RUN,
      status: 'running',
      progress: 40,
      result: {
        playbookId: PLAYBOOK,
        outcome: 'running',
        startedAt: '2026-11-27T09:00:00Z',
        completedNodes: 2,
        failedNodes: 0,
        skippedNodes: 1,
        summary: 'The run is running (40% done). Nodes: 2 completed, 0 failed, 1 skipped.',
      },
    });
    expect(parsed.ok && parsed.value).toMatchObject({
      progress: 40,
      completedNodes: 2,
      failedNodes: 0,
      skippedNodes: 1,
      startedAt: Date.parse('2026-11-27T09:00:00Z'),
    });
  });

  it('takes the outcome from the result (a failed status that was interrupted or cancelled)', () => {
    const parsed = parseRunState({ runId: RUN, status: 'failed', result: { outcome: 'interrupted' } });
    expect(parsed.ok && parsed.value.outcome).toBe('interrupted');
  });

  it('clamps progress to 0..100 and ignores a field of the wrong type', () => {
    expect(parseRunState({ runId: RUN, status: 'running', progress: 250 }).ok && true).toBe(true);
    const high = parseRunState({ runId: RUN, status: 'running', progress: 250 });
    expect(high.ok && high.value.progress).toBe(100);
    const wrong = parseRunState({ runId: RUN, status: 'running', progress: '50', result: { completedNodes: 'x' } });
    expect(wrong.ok && wrong.value.progress).toBeNull();
    expect(wrong.ok && wrong.value.completedNodes).toBeNull();
  });

  it('a failure body (numeric status) or isError is a failure, and the uniform not-found is "not-found"', () => {
    const notFound = parseRunState(
      { reasonCode: NOT_FOUND_REASON, message: 'The record is unavailable.', status: 404 },
      { isError: true, reasonCode: NOT_FOUND_REASON }
    );
    expect(notFound).toEqual({ ok: false, failure: expect.objectContaining({ kind: 'not-found' }) });

    const failed = parseRunState({ reasonCode: 'playbook.run-read-failed', message: 'x', status: 503 });
    expect(failed).toEqual({ ok: false, failure: expect.objectContaining({ kind: 'error' }) });
  });

  it('a result with no run id or an unknown status is not a run', () => {
    expect(parseRunState({ status: 'running' }).ok).toBe(false);
    expect(parseRunState({ runId: 'not-a-guid', status: 'running' }).ok).toBe(false);
    expect(parseRunState({ runId: RUN, status: 'exploded' }).ok).toBe(false);
    expect(parseRunState(null).ok).toBe(false);
  });

  it('keeps only an http(s) console link', () => {
    const parsed = parseRunState({ runId: RUN, status: 'running', consoleLink: 'javascript:alert(1)' });
    expect(parsed.ok && parsed.value.consoleLink).toBeNull();
  });
});

describe('parseRunResults', () => {
  const base = {
    runId: RUN,
    status: 'completed',
    outcome: 'completed',
    results: 'available',
    playbookId: PLAYBOOK,
    playbookName: 'Summarize an NDA',
    sections: [
      { heading: 'Summary', text: 'One **bold** point.', truncated: false },
      { text: '', truncated: false },
      { heading: 'Risks', text: 'Cut here', truncated: true },
    ],
    truncated: true,
    moreLink: LINK + '&analysisId=1',
    consoleLink: LINK + '&analysisId=1',
    documents: [
      { documentId: 'd1', name: 'NDA.pdf', link: LINK + '&doc=1' },
      { documentId: 'd2', name: 'No link.docx' },
      { documentId: 'd3', name: 'Bad link', link: 'javascript:alert(1)' },
    ],
  };

  it('reads the sections, the cut, the links and the documents; an empty section is dropped', () => {
    const parsed = parseRunResults(base);
    expect(parsed.ok).toBe(true);
    if (!parsed.ok) return;
    expect(parsed.value.playbookName).toBe('Summarize an NDA');
    expect(parsed.value.sections).toEqual([
      { heading: 'Summary', text: 'One **bold** point.', truncated: false },
      { heading: 'Risks', text: 'Cut here', truncated: true },
    ]);
    expect(parsed.value.truncated).toBe(true);
    expect(parsed.value.moreLink).toBe(LINK + '&analysisId=1');
    expect(parsed.value.documents).toEqual([
      { documentId: 'd1', name: 'NDA.pdf', link: LINK + '&doc=1' },
      { documentId: 'd2', name: 'No link.docx', link: null },
      { documentId: 'd3', name: 'Bad link', link: null },
    ]);
  });

  it('moreLink is ignored unless the result says it was truncated', () => {
    const parsed = parseRunResults({ ...base, truncated: false });
    expect(parsed.ok && parsed.value.moreLink).toBeNull();
  });

  it.each(['pending', 'none'])('reads results "%s" with no sections', state => {
    const parsed = parseRunResults({ ...base, results: state, sections: [], truncated: false });
    expect(parsed.ok && parsed.value.results).toBe(state);
  });

  it('the uniform not-found is "not-found"; a 503 is an error', () => {
    expect(parseRunResults({ reasonCode: NOT_FOUND_REASON, message: 'm', status: 404 }, { isError: true })).toEqual({
      ok: false,
      failure: expect.objectContaining({ kind: 'not-found' }),
    });
    expect(
      parseRunResults({ reasonCode: 'playbook.run-read-failed', message: 'm', status: 503 }, { isError: true })
    ).toEqual({
      ok: false,
      failure: expect.objectContaining({ kind: 'error' }),
    });
  });

  it('a transport failure (no structuredContent, widget.* code) is an error in plain words', () => {
    const parsed = parseRunResults(undefined, { isError: true, reasonCode: 'widget.host_timeout' });
    expect(parsed).toEqual({
      ok: false,
      failure: { kind: 'error', message: expect.stringContaining('host did not answer') },
    });
  });
});

describe('poll schedule (bounded)', () => {
  it('grows, then repeats its last delay', () => {
    expect(POLL_DELAYS_MS.map((_, i) => pollDelay(i))).toEqual([2_000, 3_000, 5_000, 8_000, 13_000, 20_000, 30_000]);
    expect(pollDelay(50)).toBe(30_000);
    expect(pollDelay(-1)).toBe(2_000);
  });

  it('the deadline is the run start plus 50 minutes; with no start, when the widget first saw it', () => {
    expect(pollDeadline({ startedAt: 1_000 }, 5_000)).toBe(1_000 + RUN_CAP_MS);
    expect(pollDeadline({ startedAt: null }, 5_000)).toBe(5_000 + RUN_CAP_MS);
    // A start time in the widget's future (clock skew) is not trusted to push the deadline out.
    expect(pollDeadline({ startedAt: 9_000 }, 5_000)).toBe(5_000 + RUN_CAP_MS);
  });

  it('stops at the cap, and the last poll lands on it rather than after it', () => {
    expect(nextPoll(0, 0, RUN_CAP_MS)).toBe(2_000);
    expect(nextPoll(10, RUN_CAP_MS - 10_000, RUN_CAP_MS)).toBe(10_000);
    expect(nextPoll(10, RUN_CAP_MS, RUN_CAP_MS)).toBeNull();
    expect(nextPoll(0, RUN_CAP_MS + 1, RUN_CAP_MS)).toBeNull();
  });

  it('a whole run is a bounded number of polls (at most about 110 over 50 minutes)', () => {
    let now = 0;
    let polls = 0;
    for (let delay = nextPoll(polls, now, RUN_CAP_MS); delay !== null; delay = nextPoll(polls, now, RUN_CAP_MS)) {
      now += delay;
      polls += 1;
    }
    expect(now).toBe(RUN_CAP_MS);
    expect(polls).toBeLessThanOrEqual(110);
    expect(MAX_FAILED_POLLS).toBe(5);
  });
});

describe('labels', () => {
  it('only completed and failed are terminal', () => {
    expect(['queued', 'running', 'completed', 'failed'].filter(s => isTerminal(s as never))).toEqual([
      'completed',
      'failed',
    ]);
  });

  it('every outcome has a word', () => {
    expect(
      ['queued', 'running', 'completed', 'failed', 'cancelled', 'interrupted'].map(o => outcomeLabel(o as never))
    ).toEqual(['Queued', 'Running', 'Completed', 'Failed', 'Stopped', 'Interrupted']);
  });

  it('the progress line reads the counts', () => {
    const state = {
      runId: RUN,
      status: 'running' as const,
      outcome: 'running' as const,
      progress: 50,
      completedNodes: 1,
      failedNodes: 1,
      skippedNodes: 0,
      startedAt: null,
      consoleLink: null,
    };
    expect(progressLine(state)).toBe('50% done, 1 step finished, 1 failed.');
    expect(progressLine({ ...state, progress: null, completedNodes: null, failedNodes: null })).toBe(
      'Starting the run.'
    );
  });

  it('failureOf: a 401 asks to sign in again; anything else is a plain retry message', () => {
    expect(failureOf('DATAVERSE_USER_CONTEXT_REQUIRED', 401).message).toMatch(/Sign in again/);
    expect(failureOf('mcp.invalid_argument', 400).message).toMatch(/without a valid playbook run/);
    expect(failureOf('anything', 500).message).toMatch(/could not read this playbook run/);
  });

  it('safeLink keeps http(s) only', () => {
    expect(safeLink('https://a.example/x')).toBe('https://a.example/x');
    expect(safeLink('data:text/html,<script>')).toBeNull();
    expect(safeLink('/relative')).toBeNull();
    expect(safeLink(42)).toBeNull();
  });
});
