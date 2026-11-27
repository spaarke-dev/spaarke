/**
 * What the playbook run widget reads, and when it asks again (spaarkeai-mcp-bff-api-r2 task 037c; notes/037-decisions.md §5, §6).
 *
 * Three tool shapes, all parsed defensively (a field of the wrong type is treated as absent, never trusted):
 *  - the START result of `spaarke_run_playbook`: `{ runId, status: "queued" | "running", consoleLink }` (McpLongRunning.Started);
 *  - the STATUS result of `spaarke_get_playbook_run` (and of a start whose run already finished):
 *    `{ runId, status, progress?, result?: { playbookId, outcome, reasonCode?, startedAt, completedAt?, completedNodes, failedNodes,
 *    skippedNodes, summary } }` (GetPlaybookRunTool.StatusResult);
 *  - the RESULTS of the app-only `spaarke_get_playbook_run_results` (037b, §5.4).
 * A failure of any of them is `{ reasonCode, message, status: <number>, correlationId? }` with `isError` (McpToolResults); the transport's
 * own failures carry a `widget.*` reasonCode and no structuredContent (widgetMcpTransport.ts).
 *
 * The poll schedule is pure and bounded: a growing delay, never past the run's 50-minute cap (PlaybookRunLimits.MaxRunDuration).
 */

export const RUN_TOOL = 'spaarke_run_playbook';
export const STATUS_TOOL = 'spaarke_get_playbook_run';
export const RESULTS_TOOL = 'spaarke_get_playbook_run_results';

/** The run's cap on the server (PlaybookRunLimits.MaxRunDuration): the widget stops polling once it has passed. */
export const RUN_CAP_MS = 50 * 60 * 1000;

/**
 * The wait before each status poll, in milliseconds: quick at first (most runs finish in under a minute), then every 30 seconds. The last
 * value repeats. Over the 50-minute cap this is at most about 100 polls.
 */
export const POLL_DELAYS_MS: readonly number[] = [2_000, 3_000, 5_000, 8_000, 13_000, 20_000, 30_000];

/** Consecutive failed polls (the host or Spaarke not answering) after which the widget stops and offers "Check again". */
export const MAX_FAILED_POLLS = 5;

/** The uniform not-found (ProblemDetailsHelper.RecordUnavailableReasonCode). */
export const NOT_FOUND_REASON = 'sdap.access.deny.record_unavailable';

export type RunStatus = 'queued' | 'running' | 'completed' | 'failed';
export type RunOutcome = 'queued' | 'running' | 'completed' | 'failed' | 'cancelled' | 'interrupted';
export type ResultsState = 'pending' | 'available' | 'none';

/** What the widget knows of the run's state, from a start or a status result. */
export interface RunState {
  runId: string;
  status: RunStatus;
  outcome: RunOutcome;
  /** 0-100 while running, when the server reports it. */
  progress: number | null;
  completedNodes: number | null;
  failedNodes: number | null;
  skippedNodes: number | null;
  /** When the run started (epoch ms), when the status result says. */
  startedAt: number | null;
  /** The start's console link (opens the documents in Spaarke). */
  consoleLink: string | null;
}

export interface ResultSection {
  heading: string | null;
  text: string;
  truncated: boolean;
}

export interface SourceDocument {
  documentId: string;
  name: string;
  link: string | null;
}

export interface RunResults {
  runId: string;
  status: RunStatus;
  outcome: RunOutcome;
  results: ResultsState;
  playbookName: string | null;
  sections: ResultSection[];
  truncated: boolean;
  moreLink: string | null;
  consoleLink: string | null;
  documents: SourceDocument[];
}

/** A tool failure, in plain words for the reader. `notFound` is the uniform not-found. */
export interface RunFailure {
  kind: 'not-found' | 'error';
  message: string;
}

export type Parsed<T> = { ok: true; value: T } | { ok: false; failure: RunFailure };

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function str(value: unknown): string | null {
  return typeof value === 'string' && value.trim() !== '' ? value : null;
}

function int(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value) ? Math.trunc(value) : null;
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function runIdOf(value: unknown): string | null {
  const id = str(value);
  return id && GUID.test(id) ? id : null;
}

const STATUSES: readonly RunStatus[] = ['queued', 'running', 'completed', 'failed'];
const OUTCOMES: readonly RunOutcome[] = ['queued', 'running', 'completed', 'failed', 'cancelled', 'interrupted'];
const RESULT_STATES: readonly ResultsState[] = ['pending', 'available', 'none'];

function oneOf<T extends string>(value: unknown, allowed: readonly T[]): T | null {
  return typeof value === 'string' && (allowed as readonly string[]).includes(value) ? (value as T) : null;
}

/** Only an absolute http(s) URL is ever handed to the host's openLink; anything else is no link at all. */
export function safeLink(value: unknown): string | null {
  const text = str(value);
  if (!text) return null;
  try {
    const url = new URL(text);
    return url.protocol === 'https:' || url.protocol === 'http:' ? url.toString() : null;
  } catch {
    return null;
  }
}

/** True once the run has finished: then the results tool is called, once. */
export function isTerminal(status: RunStatus): boolean {
  return status === 'completed' || status === 'failed';
}

/** A failure body (`{ reasonCode, message, status: number }`) or a transport failure, in plain words. */
export function failureOf(reasonCode: string | null | undefined, status: number | null | undefined): RunFailure {
  if (reasonCode === NOT_FOUND_REASON || status === 404) {
    return { kind: 'not-found', message: 'This playbook run was not found, or you can no longer open it.' };
  }
  if (reasonCode === 'DATAVERSE_USER_CONTEXT_REQUIRED' || status === 401) {
    return { kind: 'error', message: 'Spaarke could not tell who you are. Sign in again, then ask in the chat.' };
  }
  if (reasonCode && reasonCode.startsWith('widget.')) {
    return { kind: 'error', message: 'Your host did not answer. Check again in a moment.' };
  }
  if (status === 400) {
    return { kind: 'error', message: 'This widget was opened without a valid playbook run.' };
  }
  return { kind: 'error', message: 'Spaarke could not read this playbook run right now. Check again in a moment.' };
}

/** A tool result (structuredContent plus isError / reasonCode) that is a failure, as a RunFailure; null when it is not one. */
function failureFrom(
  structured: unknown,
  isError: boolean | undefined,
  reasonCode: string | undefined
): RunFailure | null {
  const body = isRecord(structured) ? structured : null;
  const code = reasonCode ?? str(body?.reasonCode);
  // A failure body's `status` is the HTTP number; a run's is a word.
  const numericStatus = typeof body?.status === 'number' ? body.status : null;
  if (isError || numericStatus !== null) return failureOf(code, numericStatus);
  return null;
}

/** Reads the run's state from the result that opened the widget or from a status poll. */
export function parseRunState(
  structured: unknown,
  options: { isError?: boolean; reasonCode?: string } = {}
): Parsed<RunState> {
  const failure = failureFrom(structured, options.isError, options.reasonCode);
  if (failure) return { ok: false, failure };
  if (!isRecord(structured)) return { ok: false, failure: failureOf(undefined, 400) };

  const runId = runIdOf(structured.runId);
  const status = oneOf(structured.status, STATUSES);
  if (!runId || !status) return { ok: false, failure: failureOf(undefined, 400) };

  const result = isRecord(structured.result) ? structured.result : null;
  const outcome = oneOf(result?.outcome, OUTCOMES) ?? (status as RunOutcome);
  const progress = int(structured.progress);
  const started = str(result?.startedAt);
  const startedAt = started ? Date.parse(started) : NaN;

  return {
    ok: true,
    value: {
      runId,
      status,
      outcome,
      progress: progress === null ? null : Math.min(100, Math.max(0, progress)),
      completedNodes: int(result?.completedNodes),
      failedNodes: int(result?.failedNodes),
      skippedNodes: int(result?.skippedNodes),
      startedAt: Number.isFinite(startedAt) ? startedAt : null,
      consoleLink: safeLink(structured.consoleLink),
    },
  };
}

/** Reads the results tool's answer. */
export function parseRunResults(
  structured: unknown,
  options: { isError?: boolean; reasonCode?: string } = {}
): Parsed<RunResults> {
  const failure = failureFrom(structured, options.isError, options.reasonCode);
  if (failure) return { ok: false, failure };
  if (!isRecord(structured)) return { ok: false, failure: failureOf(undefined, 500) };

  const runId = runIdOf(structured.runId);
  const status = oneOf(structured.status, STATUSES);
  const results = oneOf(structured.results, RESULT_STATES);
  if (!runId || !status || !results) return { ok: false, failure: failureOf(undefined, 500) };

  const sections: ResultSection[] = Array.isArray(structured.sections)
    ? structured.sections.filter(isRecord).flatMap(section => {
        const text = typeof section.text === 'string' ? section.text : '';
        const heading = str(section.heading);
        return text.trim() === '' && !heading ? [] : [{ heading, text, truncated: section.truncated === true }];
      })
    : [];

  const documents: SourceDocument[] = Array.isArray(structured.documents)
    ? structured.documents.filter(isRecord).flatMap(document => {
        const documentId = str(document.documentId);
        const name = str(document.name);
        return documentId && name ? [{ documentId, name, link: safeLink(document.link) }] : [];
      })
    : [];

  const truncated = structured.truncated === true;
  return {
    ok: true,
    value: {
      runId,
      status,
      outcome: oneOf(structured.outcome, OUTCOMES) ?? (status as RunOutcome),
      results,
      playbookName: str(structured.playbookName),
      sections,
      truncated,
      moreLink: truncated ? safeLink(structured.moreLink) : null,
      consoleLink: safeLink(structured.consoleLink),
      documents,
    },
  };
}

/** The wait before poll number `attempt` (0-based). */
export function pollDelay(attempt: number): number {
  const index = Math.min(Math.max(0, Math.trunc(attempt)), POLL_DELAYS_MS.length - 1);
  return POLL_DELAYS_MS[index];
}

/** When the widget stops polling: the run's start (or, when the result does not say, when the widget first saw it) plus the 50-minute cap. */
export function pollDeadline(state: Pick<RunState, 'startedAt'>, firstSeenAt: number): number {
  const start = state.startedAt !== null && state.startedAt <= firstSeenAt ? state.startedAt : firstSeenAt;
  return start + RUN_CAP_MS;
}

/**
 * The next poll: the delay to wait, or null when the widget must stop (the cap has passed, or would pass before the poll). The last poll may
 * be shortened so it lands at the cap rather than after it.
 */
export function nextPoll(attempt: number, now: number, deadline: number): number | null {
  const remaining = deadline - now;
  if (remaining <= 0) return null;
  return Math.min(pollDelay(attempt), remaining);
}

/** The state badge's words. */
export function outcomeLabel(outcome: RunOutcome): string {
  switch (outcome) {
    case 'queued':
      return 'Queued';
    case 'running':
      return 'Running';
    case 'completed':
      return 'Completed';
    case 'cancelled':
      return 'Stopped';
    case 'interrupted':
      return 'Interrupted';
    default:
      return 'Failed';
  }
}

/** One line under the progress bar, from the status tool's counts. */
export function progressLine(state: RunState): string {
  const parts: string[] = [];
  if (state.progress !== null) parts.push(`${state.progress}% done`);
  if (state.completedNodes !== null)
    parts.push(`${state.completedNodes} ${state.completedNodes === 1 ? 'step' : 'steps'} finished`);
  if (state.failedNodes) parts.push(`${state.failedNodes} failed`);
  return parts.length ? parts.join(', ') + '.' : 'Starting the run.';
}
