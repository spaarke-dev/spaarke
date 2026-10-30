/**
 * The Rule Review page's workflow, kept free of React so it can be tested (ebilling-r1 task 077).
 *
 * - {@link decideProposal}: Accept and Reject send the version of the rule the page DISPLAYED (076 re-check K3). When the
 *   server answers 409 `ruleset.concurrent_edit` (another admin changed the rule after this page loaded it), the page reloads the
 *   rule set, works out what changed and reports it; nothing was accepted or rejected.
 * - {@link saveEdit}: an edit, then a reload (the response to a write carries no version token, so the page always decides from a read).
 * - {@link runDraft}: start the drafting job (075, 202) and poll the rule set read until the run has left its marker.
 * - {@link publishNotice}: what the publish confirmation says about firms seeing clause quotes (D-66).
 */

import {
  CODE_ALREADY_DRAFTED,
  MAX_REJECTION_REASON,
  isStale,
  type ApiFailure,
  type ProposalView,
  type RuleEdit,
  type RuleSetApi,
  type RuleSetDetail,
} from './ruleSetApi';

// ─────────────────────────────────────────────────────────────────────────────
// What changed
// ─────────────────────────────────────────────────────────────────────────────

export interface ChangeLine {
  /** The field in words: "Consequence", "Parameter maxHoursPerDay", "Clause quote". */
  field: string;
  before: string;
  after: string;
}

const show = (v: unknown): string => {
  if (v === null || v === undefined || v === '') return '(empty)';
  return typeof v === 'string' ? v : JSON.stringify(v);
};

const sameValue = (a: unknown, b: unknown): boolean => JSON.stringify(a ?? null) === JSON.stringify(b ?? null);

/** What differs between the rule the admin was shown and the rule as stored now. Empty when nothing the admin sees differs. */
export function diffProposals(before: ProposalView, after: ProposalView): ChangeLine[] {
  const lines: ChangeLine[] = [];
  const add = (field: string, a: unknown, b: unknown): void => {
    if (!sameValue(a, b)) lines.push({ field, before: show(a), after: show(b) });
  };

  if (before.versionNumber !== after.versionNumber) {
    lines.push({ field: 'Version', before: `v${before.versionNumber}`, after: `v${after.versionNumber} (a new version was created)` });
  }

  add('Status', before.state, after.state);
  add('Rejection reason', before.rejectionReason, after.rejectionReason);
  add('Rule type', before.ruleType, after.ruleType);

  const keys = new Set<string>([...Object.keys(before.ruleBody ?? {}), ...Object.keys(after.ruleBody ?? {})]);
  keys.delete('type');
  for (const key of [...keys].sort()) {
    add(`Parameter ${key}`, before.ruleBody?.[key], after.ruleBody?.[key]);
  }

  add('Consequence', before.consequence, after.consequence);
  add('Headline', before.shortHeadline, after.shortHeadline);
  add('Message', before.messageTemplate, after.messageTemplate);
  add('Clause quote', before.clauseQuote, after.clauseQuote);
  add('Clause location', before.clauseLocation, after.clauseLocation);
  return lines;
}

// ─────────────────────────────────────────────────────────────────────────────
// Accept / reject
// ─────────────────────────────────────────────────────────────────────────────

export type DecisionOutcome =
  /** The decision was saved. `detail` is the reloaded rule set (null when only the reload failed). */
  | { kind: 'decided'; decided: ProposalView; detail: RuleSetDetail | null }
  /**
   * The rule changed after the page showed it: nothing was decided. `detail` is the reloaded rule set; `changes` is what differs from
   * what the admin saw (empty when the difference is not a visible field, or the page had no token to compare).
   */
  | { kind: 'stale'; detail: RuleSetDetail; changes: ChangeLine[] }
  /** Any other failure (400 with per-rule errors, 403, 404, 409 in force, 5xx). Nothing was decided. */
  | { kind: 'failed'; failure: ApiFailure };

/** What the reject dialog knows about the reason typed so far (D-113). */
export interface ReasonCheck {
  /** The reason as it will be sent: trimmed. */
  reason: string;
  /** 1 to {@link MAX_REJECTION_REASON} characters once trimmed: the only state in which Reject can be confirmed. */
  valid: boolean;
  /** Characters over the limit (0 when within it). */
  over: number;
}

/** The server's rule for a rejection reason, applied before the request so the dialog can say what is wrong. */
export function checkRejectionReason(text: string): ReasonCheck {
  const reason = text.trim();
  return { reason, valid: reason.length >= 1 && reason.length <= MAX_REJECTION_REASON, over: Math.max(0, reason.length - MAX_REJECTION_REASON) };
}

/** Reject can be confirmed only with a valid reason and nothing in flight: the dialog's Reject button is disabled otherwise. */
export const canConfirmReject = (text: string, busy: boolean): boolean => !busy && checkRejectionReason(text).valid;

/**
 * Accept or reject the rule the admin is looking at, naming the version they were shown. A refusal because the rule changed
 * (409 `ruleset.concurrent_edit`) reloads the rule set and reports what changed.
 *
 * A reject needs a reason (D-113). One that is blank or too long is refused here as a 400-shaped failure without a request, so a
 * caller that skips the dialog's disabled button still cannot reject without one; an accept never sends a reason.
 */
export async function decideProposal(
  api: RuleSetApi, ruleSetId: string, shown: ProposalView, accept: boolean, reason = ''
): Promise<DecisionOutcome> {
  const checked = checkRejectionReason(reason);
  if (!accept && !checked.valid) {
    return {
      kind: 'failed',
      failure: {
        status: 400,
        errorCode: 'ruleset.invalid',
        reasonCode: null,
        title: 'A reason is required',
        detail: `Give a reason to reject a rule: 1 to ${MAX_REJECTION_REASON} characters.`,
        errors: [],
        signInFailed: false,
      },
    };
  }

  if (!shown.rowVersion) {
    // A view built from a write carries no token; deciding on it is exactly what the token exists to prevent.
    const reloaded = await api.get(ruleSetId);
    return reloaded.ok ? { kind: 'stale', detail: reloaded.value, changes: [] } : { kind: 'failed', failure: reloaded.failure };
  }

  const result = accept
    ? await api.accept(ruleSetId, shown.policyId, shown.rowVersion)
    : await api.reject(ruleSetId, shown.policyId, shown.rowVersion, checked.reason);

  if (result.ok) {
    const reloaded = await api.get(ruleSetId);
    return { kind: 'decided', decided: result.value, detail: reloaded.ok ? reloaded.value : null };
  }

  // Any 409 means the rule is no longer what the page showed (changed, decided, or put in force by a publish): reload and show it.
  if (result.failure.status !== 409) {
    return { kind: 'failed', failure: result.failure };
  }

  const reloaded = await api.get(ruleSetId);
  if (!reloaded.ok) {
    return { kind: 'failed', failure: result.failure };
  }

  const now = reloaded.value.proposals.find(p => p.policyId === shown.policyId);
  return { kind: 'stale', detail: reloaded.value, changes: now ? diffProposals(shown, now) : [] };
}

// ─────────────────────────────────────────────────────────────────────────────
// Edit
// ─────────────────────────────────────────────────────────────────────────────

export type EditOutcome =
  | { kind: 'saved'; createdNewVersion: boolean; detail: RuleSetDetail | null }
  /** 409: the rule changed while the edit was being made. Nothing was saved; the rule set was reloaded. */
  | { kind: 'stale'; detail: RuleSetDetail; failure: ApiFailure }
  | { kind: 'failed'; failure: ApiFailure };

export async function saveEdit(api: RuleSetApi, ruleSetId: string, policyId: string, edit: RuleEdit): Promise<EditOutcome> {
  const result = await api.edit(ruleSetId, policyId, edit);
  if (result.ok) {
    const reloaded = await api.get(ruleSetId);
    return { kind: 'saved', createdNewVersion: result.value.createdNewVersion, detail: reloaded.ok ? reloaded.value : null };
  }

  if (!isStale(result.failure)) {
    return { kind: 'failed', failure: result.failure };
  }

  const reloaded = await api.get(ruleSetId);
  return reloaded.ok ? { kind: 'stale', detail: reloaded.value, failure: result.failure } : { kind: 'failed', failure: result.failure };
}

// ─────────────────────────────────────────────────────────────────────────────
// Drafting (075 trigger, then poll the rule set read)
// ─────────────────────────────────────────────────────────────────────────────

export type DraftOutcome =
  | { kind: 'finished'; detail: RuleSetDetail }
  | { kind: 'timeout'; detail: RuleSetDetail | null }
  | { kind: 'failed'; failure: ApiFailure };

export interface PollOptions {
  intervalMs?: number;
  timeoutMs?: number;
  sleep?: (ms: number) => Promise<void>;
  now?: () => number;
  /** Called with each poll's read, so the page can show the rules as they appear. */
  onProgress?: (detail: RuleSetDetail) => void;
  isCancelled?: () => boolean;
}

const defaultSleep = (ms: number): Promise<void> => new Promise(resolve => setTimeout(resolve, ms));

/** A finished run has written its not-converted list, even when empty (075): `null` means the run has not finished. */
export const draftFinished = (detail: RuleSetDetail): boolean => detail.notConverted !== null;

/**
 * Start the drafting job (202) and poll the rule set read until its not-converted list is present. `409 ruleset.already_drafted` is
 * not an error to the admin: the run already happened, so the current read is returned.
 */
export async function runDraft(api: RuleSetApi, ruleSetId: string, options: PollOptions = {}): Promise<DraftOutcome> {
  const { intervalMs = 3000, timeoutMs = 5 * 60_000, sleep = defaultSleep, now = Date.now, onProgress, isCancelled } = options;

  const started = await api.draft(ruleSetId);
  if (!started.ok && started.failure.errorCode !== CODE_ALREADY_DRAFTED) {
    return { kind: 'failed', failure: started.failure };
  }

  const deadline = now() + timeoutMs;
  let last: RuleSetDetail | null = null;
  for (;;) {
    // Checked before the read as well as after it, so a page that was left mid-wait does not issue one more request.
    if (isCancelled?.()) {
      return { kind: 'timeout', detail: last };
    }

    const read = await api.get(ruleSetId);
    if (!read.ok) {
      return { kind: 'failed', failure: read.failure };
    }

    last = read.value;
    onProgress?.(last);
    if (draftFinished(last)) {
      return { kind: 'finished', detail: last };
    }

    if (isCancelled?.() || now() >= deadline) {
      return { kind: 'timeout', detail: last };
    }

    await sleep(intervalMs);
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Publish
// ─────────────────────────────────────────────────────────────────────────────

/**
 * What the publish confirmation says about firms (D-66). The sentence "Clause quotes will be visible to firms." appears exactly when
 * the rule set's "visible to firms" flag is on; with the flag off the dialog says firms will not see the quotes, in words that do
 * not contain it.
 */
export function publishNotice(visibleToFirms: boolean): string {
  return visibleToFirms
    ? 'Clause quotes will be visible to firms.'
    : 'Firms will not see clause quotes from this rule set.';
}

export interface ProposalCounts {
  total: number;
  proposed: number;
  accepted: number;
  rejected: number;
  /** Accepted rules that are not in force yet: what a publish would put in force. */
  toPublish: number;
}

export function countProposals(detail: RuleSetDetail): ProposalCounts {
  const by = (s: string): number => detail.proposals.filter(p => p.state === s).length;
  return {
    total: detail.proposals.length,
    proposed: by('Proposed'),
    accepted: by('Accepted'),
    rejected: by('Rejected'),
    toPublish: detail.proposals.filter(p => p.state === 'Accepted' && !p.inForce).length,
  };
}
