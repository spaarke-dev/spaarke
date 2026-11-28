/**
 * The playbook run widget's state machine (task 037c): from the result that opened the widget, poll the status tool on a bounded schedule
 * until the run finishes, then call the app-only results tool ONCE (037b K1: three caller reads per call, so never per poll).
 *
 *  - Polls: `spaarke_get_playbook_run { runId }` after POLL_DELAYS_MS (2, 3, 5, 8, 13, 20 s, then every 30 s), never past the run's
 *    50-minute cap counted from its start (or from when the widget first saw it). At the cap the widget stops and offers "Check again"
 *    (one poll per press).
 *  - A poll the host or Spaarke does not answer is retried on the same schedule; MAX_FAILED_POLLS in a row stop polling ("Check again").
 *  - A not-found stops everything (the run is gone, or the caller can no longer read it).
 *  - A finished run: the results tool, once per run id. A failed results call can be retried by the reader; a "pending" answer (the status
 *    said finished, the results not yet) is shown with "Check again", never polled.
 * Every timer is cleared on a new opening result and on unmount; a late answer for an older run is dropped.
 */
import * as React from 'react';
import type { WidgetMcpTransport } from '../../../shared/mcp/widgetMcpTransport';
import {
  MAX_FAILED_POLLS,
  RESULTS_TOOL,
  STATUS_TOOL,
  isTerminal,
  nextPoll,
  parseRunResults,
  parseRunState,
  pollDeadline,
  type RunFailure,
  type RunResults,
  type RunState,
} from './playbookRun';

export type RunView =
  | { kind: 'waiting' }
  | { kind: 'polling'; state: RunState }
  | { kind: 'stopped'; state: RunState; reason: 'cap' | 'failures' }
  | { kind: 'loading-results'; state: RunState }
  | { kind: 'results'; state: RunState; results: RunResults }
  | { kind: 'failed'; failure: RunFailure; state: RunState | null; canRetry: boolean };

export interface RunController {
  view: RunView;
  /** Polls once now (after the cap or after repeated failures); the schedule resumes only while the cap has not passed. */
  checkAgain: () => void;
  /** Calls the results tool again after it failed or answered "pending" (the reader asked). */
  retryResults: () => void;
}

interface Session {
  runId: string;
  /** When polling stops: the run's start plus the 50-minute cap. */
  deadline: number;
  /** Polls made so far (picks the next delay). */
  attempt: number;
  /** Failed polls in a row. */
  failures: number;
  timer: ReturnType<typeof setTimeout> | undefined;
  /** A status poll is in flight. */
  polling: boolean;
  /** A results call is in flight for this run. */
  loadingResults: boolean;
  /** The latest known state of the run. */
  state: RunState;
}

export function useRunController(toolResult: unknown, transport: WidgetMcpTransport): RunController {
  const [view, setView] = React.useState<RunView>({ kind: 'waiting' });
  const session = React.useRef<Session | null>(null);
  const transportRef = React.useRef(transport);
  transportRef.current = transport;

  // The steps below read everything through refs, so the timers they set always call code that sees the current transport and session.
  const steps = React.useRef({
    async loadResults(s: Session) {
      if (s.loadingResults) return;
      s.loadingResults = true;
      setView({ kind: 'loading-results', state: s.state });
      const answer = await transportRef.current.callTool(RESULTS_TOOL, { runId: s.runId });
      s.loadingResults = false;
      if (session.current !== s) return;
      const parsed = parseRunResults(answer.structuredContent, {
        isError: answer.isError,
        reasonCode: answer.reasonCode,
      });
      setView(
        parsed.ok
          ? { kind: 'results', state: s.state, results: parsed.value }
          : { kind: 'failed', failure: parsed.failure, state: s.state, canRetry: parsed.failure.kind === 'error' }
      );
    },

    schedule(s: Session) {
      const delay = nextPoll(s.attempt, Date.now(), s.deadline);
      if (delay === null) {
        setView({ kind: 'stopped', state: s.state, reason: 'cap' });
        return;
      }
      setView({ kind: 'polling', state: s.state });
      s.timer = setTimeout(() => {
        s.timer = undefined;
        void steps.current.poll(s, true);
      }, delay);
    },

    /** One status poll. `continueAfter`: schedule the next one while the run is still running (false for a one-off "Check again" past the cap). */
    async poll(s: Session, continueAfter: boolean) {
      if (s.polling) return;
      s.polling = true;
      const answer = await transportRef.current.callTool(STATUS_TOOL, { runId: s.runId });
      s.polling = false;
      if (session.current !== s) return;
      s.attempt += 1;
      const parsed = parseRunState(answer.structuredContent, {
        isError: answer.isError,
        reasonCode: answer.reasonCode,
      });

      if (!parsed.ok) {
        if (parsed.failure.kind === 'not-found') {
          setView({ kind: 'failed', failure: parsed.failure, state: s.state, canRetry: false });
          return;
        }
        s.failures += 1;
        if (s.failures >= MAX_FAILED_POLLS || !continueAfter) {
          setView({ kind: 'stopped', state: s.state, reason: s.failures >= MAX_FAILED_POLLS ? 'failures' : 'cap' });
          return;
        }
        steps.current.schedule(s);
        return;
      }

      // A status for another run (the server's tools never answer one) is not this run's.
      if (parsed.value.runId.toLowerCase() !== s.runId.toLowerCase()) return;

      s.failures = 0;
      s.state = { ...parsed.value, consoleLink: parsed.value.consoleLink ?? s.state.consoleLink };
      if (isTerminal(s.state.status)) {
        await steps.current.loadResults(s);
        return;
      }
      if (continueAfter) steps.current.schedule(s);
      else setView({ kind: 'stopped', state: s.state, reason: 'cap' });
    },
  });

  // A new opening result is a new run: forget the last one and start over.
  React.useEffect(() => {
    if (toolResult === null || toolResult === undefined) {
      session.current = null;
      setView({ kind: 'waiting' });
      return undefined;
    }

    const parsed = parseRunState(toolResult);
    if (!parsed.ok) {
      session.current = null;
      setView({ kind: 'failed', failure: parsed.failure, state: null, canRetry: false });
      return undefined;
    }

    const s: Session = {
      runId: parsed.value.runId,
      deadline: pollDeadline(parsed.value, Date.now()),
      attempt: 0,
      failures: 0,
      timer: undefined,
      polling: false,
      loadingResults: false,
      state: parsed.value,
    };
    session.current = s;

    if (isTerminal(s.state.status)) void steps.current.loadResults(s);
    else steps.current.schedule(s);

    return () => {
      if (s.timer !== undefined) clearTimeout(s.timer);
      s.timer = undefined;
      if (session.current === s) session.current = null;
    };
  }, [toolResult]);

  const checkAgain = React.useCallback(() => {
    const s = session.current;
    if (!s || s.timer !== undefined || s.polling || s.loadingResults) return;
    s.failures = 0;
    setView({ kind: 'polling', state: s.state });
    // Before the cap the schedule resumes; past it, one poll per press.
    void steps.current.poll(s, Date.now() < s.deadline);
  }, []);

  const retryResults = React.useCallback(() => {
    const s = session.current;
    if (s) void steps.current.loadResults(s);
  }, []);

  return { view, checkAgain, retryResults };
}
