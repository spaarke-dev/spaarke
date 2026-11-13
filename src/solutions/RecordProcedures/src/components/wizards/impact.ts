/**
 * What the Impact step says of a dry run (ebilling-r1 task 084d-4), as pure functions of the route's answer. No React, no calls.
 *
 * Now -> After is computed from the answer's two sides only: each Procedure (by id) and each rule (by policy id) whose one-line description
 * differs, or that is listed on one side only. The descriptions use the panel's own words (`procedureState`, `ruleBadges`), so a row reads
 * the same in the Impact step as in the list it will show in. Nothing here resolves anything.
 *
 * Findings text is the server's: the `closes` warning and message and the `affectedOpenInvoices` text are shown as they came (ontology-r1 F2).
 * The only sentences of ours are about what cannot be shown: a count that is null (O-K1b: "can't be counted", never 0 or blank) and a
 * refusal (the R6 sentence for a firm the request named, else the ProblemDetails title and detail).
 */

import type { GapView, ProcedureItem, RuleItem } from '../../services/recordProceduresApi';
import { procedureState, ruleBadges, versionLabel } from '../../services/model';
import { CODE_DRY_RUN_UNAVAILABLE, type DryRunAffected, type DryRunView } from '../../services/dryRunApi';
import { failureText, type WriteFailure } from '../../services/overridesApi';

/** One row that changes between Now and After. A side is null when the row is not listed on it. */
export interface ImpactChange {
  key: string;
  label: string;
  now: string | null;
  after: string | null;
}

export interface ImpactChanges {
  procedures: ImpactChange[];
  rules: ImpactChange[];
}

/** "In force · v2", "Pinned · v1", "Excluded here", ... (the panel's state badge, then the version in force). */
export function describeProcedure(p: ProcedureItem, gaps: readonly GapView[]): string {
  const state = procedureState(p, gaps).label;
  return p.versionInForce ? `${state} · ${versionLabel(p.versionInForce)}` : state;
}

/** "In force · v3", "Excluded · v2", "Changed here · v1" (the panel's badges, then the version shown). */
export function describeRule(r: RuleItem): string {
  const badges = ruleBadges(r)
    .map(b => b.label)
    .join(', ');
  return r.versionNumber !== null && r.versionNumber !== undefined ? `${badges} · v${r.versionNumber}` : badges;
}

const ruleLabel = (r: RuleItem): string => (r.name ? `${r.code} · ${r.name}` : r.code);

function diff<T>(now: readonly T[], after: readonly T[], keyOf: (row: T) => string, labelOf: (row: T) => string, describe: (row: T, side: 'now' | 'after') => string): ImpactChange[] {
  const nowBy = new Map(now.map(r => [keyOf(r), r] as const));
  const afterBy = new Map(after.map(r => [keyOf(r), r] as const));
  const keys = [...nowBy.keys(), ...[...afterBy.keys()].filter(k => !nowBy.has(k))];
  const out: ImpactChange[] = [];
  for (const key of keys) {
    const n = nowBy.get(key);
    const a = afterBy.get(key);
    const nowText = n ? describe(n, 'now') : null;
    const afterText = a ? describe(a, 'after') : null;
    if (nowText !== afterText) out.push({ key, label: labelOf((a ?? n)!), now: nowText, after: afterText });
  }
  return out;
}

/** The Procedures and rules whose description differs between Now and After (in Now's order, then the rows only After lists). */
export function impactChanges(view: DryRunView): ImpactChanges {
  return {
    procedures: diff(
      view.now.procedures.procedures,
      view.after.procedures.procedures,
      p => p.id.toLowerCase(),
      p => p.name,
      (p, side) => describeProcedure(p, (side === 'now' ? view.now : view.after).procedures.gaps)
    ),
    rules: diff(view.now.rules.rules, view.after.rules.rules, r => r.policyId.toLowerCase(), ruleLabel, r => describeRule(r)),
  };
}

/** A row's side when it is not listed. */
export const NOT_LISTED = 'Not listed';

/** O-K1b: a null count is said as "can't be counted", never as 0 or a blank. */
export const COUNT_UNKNOWN = "How many open invoices you can see are affected can't be counted.";

/** The affected-invoices line: the server's text for a count; for a count without text (an older body), the count itself. */
export function affectedLine(a: DryRunAffected): string {
  if (a.count === null) return COUNT_UNKNOWN;
  return a.text ?? `${a.count} open invoice${a.count === 1 ? '' : 's'} you can see ${a.count === 1 ? 'is' : 'are'} affected.`;
}

/**
 * R6 (uac-r2, notes/084d-dry-run-design.md): a firm the request names that the caller cannot Read gives 503 `procedures.unavailable`, and the
 * client words it as this, not as an outage. Its scope is a request that names a firm; any other 503 is shown with the server's words.
 */
export const R6_TEXT = "This preview isn't available for this firm.";

/** The preview is advisory: a refused or failed dry run never stops a save (the write route decides). */
export const ADVISORY_LINE = 'You can still go on: the preview is only a guide, and the save itself decides.';

/** What a refused dry run says: the R6 sentence for a request-named firm's 503, else the ProblemDetails title and detail. */
export function dryRunFailureText(f: WriteFailure, requestNamesFirm: boolean): string {
  if (f.status === 503 && f.errorCode === CODE_DRY_RUN_UNAVAILABLE && requestNamesFirm && !f.signInFailed) return R6_TEXT;
  if (!f.signInFailed && f.title === null) {
    return f.status === 0 ? 'The preview could not be read. Check your connection.' : `The preview could not be read.${f.detail ? ` ${f.detail}` : ''}`;
  }
  return failureText(f);
}
