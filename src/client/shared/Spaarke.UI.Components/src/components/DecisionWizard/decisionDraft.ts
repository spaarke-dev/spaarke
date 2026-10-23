/**
 * decisionDraft — the pure logic of one item's unrecorded review: what the user chose and typed, which later steps an
 * earlier choice removes, the record class the choices derive, and the one body posted to the commit route.
 *
 * No React, no I/O. The wizard keeps one `ItemDraft` per item while it is open (browsing loses nothing typed) and never
 * writes anything: `buildReview` only produces the body for `POST /api/v1/signals/{id}/decision` (task 043, D-17).
 *
 * Steps come from data (the plan): nothing here switches on a rule, a lane or a catalog code. Exclusions are read from the
 * plan's own `excludes` lists, which the catalog made MUTUAL; they are applied in PLAN ORDER ("taking the earlier one
 * removes the later step", HANDOFF 1.1), and a removed step's own choice never counts while it is removed.
 *
 * Task: spaarke-ontology-platform-r1, task 058.
 */

import type {
  DecisionAction,
  DecisionParameter,
  DecisionPlan,
  DecisionReview,
  DecisionReviewAction,
  DecisionReviewNextStep,
  DecisionValue,
} from './decisionWizardTypes';
import { EMAIL_RE } from '../EmailComposer/EmailComposer.reducer';

export type Decision = 'taken' | 'skipped';

/** A scope keys one step's choice and values: an action of the plan or a Next step. */
export type StepScope = `action:${string}` | `next:${string}`;

export const actionScope = (code: string): StepScope => `action:${code}`;
export const nextScope = (code: string): StepScope => `next:${code}`;

/** One item's unrecorded review. Every field is user input except `defaults`, which is NOT a choice. */
export interface ItemDraft {
  /** The client's review id for this item; one review posts once (a repeat returns the first outcome). */
  reviewId: string;
  /** Taken / skipped per step. A step with no entry is undecided. */
  decisions: Partial<Record<StepScope, Decision>>;
  /** What the user typed, as strings, per step. */
  values: Partial<Record<StepScope, Record<string, string>>>;
  /** Templated or host-supplied starting values (D-53, D-18). Not a choice: a draft with only defaults is not dirty. */
  defaults: Partial<Record<StepScope, Record<string, string>>>;
  /** Display names of picked Lookup / Person ids, keyed `scope|parameterCode`, so Confirm can name what was picked. */
  labels: Record<string, string>;
  /** The Next-step cards ticked, in card order. */
  nextSelected: string[];
  dismissalCode: string;
  dismissalDetail: string;
  /** Other open Decide items on the same core record this decision also resolves. */
  alsoResolve: string[];
}

export function emptyDraft(reviewId: string): ItemDraft {
  return {
    reviewId,
    decisions: {},
    values: {},
    defaults: {},
    labels: {},
    nextSelected: [],
    dismissalCode: '',
    dismissalDetail: '',
    alsoResolve: [],
  };
}

// ---------------------------------------------------------------------------
// Edits (pure updaters)
// ---------------------------------------------------------------------------

export function withDecision(draft: ItemDraft, scope: StepScope, decision: Decision): ItemDraft {
  return draft.decisions[scope] === decision
    ? draft
    : { ...draft, decisions: { ...draft.decisions, [scope]: decision } };
}

/** Forget a step's decision (a step removed by an exclusion has not been decided; its typed values are kept). */
export function clearDecision(draft: ItemDraft, scope: StepScope): ItemDraft {
  if (!(scope in draft.decisions)) return draft;
  const decisions = { ...draft.decisions };
  delete decisions[scope];
  return { ...draft, decisions };
}

export function withValue(draft: ItemDraft, scope: StepScope, parameterCode: string, value: string): ItemDraft {
  return { ...draft, values: { ...draft.values, [scope]: { ...(draft.values[scope] ?? {}), [parameterCode]: value } } };
}

/** Starting values for a step. Never overwrites what the user typed; replaces earlier defaults. */
export function withDefaults(draft: ItemDraft, scope: StepScope, defaults: Record<string, string>): ItemDraft {
  return { ...draft, defaults: { ...draft.defaults, [scope]: { ...(draft.defaults[scope] ?? {}), ...defaults } } };
}

const labelKey = (scope: StepScope, parameterCode: string): string => `${scope}|${parameterCode}`;

/** The name shown for a picked Lookup / Person value, or null. */
export function fieldLabel(draft: ItemDraft, scope: StepScope, parameterCode: string): string | null {
  return draft.labels[labelKey(scope, parameterCode)] ?? null;
}

/**
 * A Lookup / Person pick. `asDefault` records a pre-selection (a matter with exactly one budget, D-18): a starting value, not
 * a choice, so it does not make the draft dirty. A pick the user makes (`asDefault` false) is a typed value.
 */
export function withLookup(
  draft: ItemDraft,
  scope: StepScope,
  parameterCode: string,
  item: { id: string; name: string } | null,
  asDefault = false
): ItemDraft {
  const labels = { ...draft.labels };
  if (item) labels[labelKey(scope, parameterCode)] = item.name;
  else delete labels[labelKey(scope, parameterCode)];
  const next = { ...draft, labels };
  return asDefault
    ? withDefaults(next, scope, { [parameterCode]: item?.id ?? '' })
    : withValue(next, scope, parameterCode, item?.id ?? '');
}

/** The Next-step selection as `FollowOnGrid` reports it. A card un-ticked loses its decision (its typed values are kept). */
export function withNextSelected(draft: ItemDraft, selected: string[]): ItemDraft {
  const decisions = { ...draft.decisions };
  for (const code of draft.nextSelected) {
    if (!selected.includes(code)) delete decisions[nextScope(code)];
  }
  return { ...draft, nextSelected: selected, decisions };
}

export function withDismissal(draft: ItemDraft, code: string, detail?: string): ItemDraft {
  // Detail belongs to the reason it was typed for: choosing another reason starts with none.
  const kept = code === draft.dismissalCode ? draft.dismissalDetail : '';
  return { ...draft, dismissalCode: code, dismissalDetail: detail ?? kept };
}

export function withAlsoResolve(draft: ItemDraft, signalId: string, ticked: boolean): ItemDraft {
  const has = draft.alsoResolve.includes(signalId);
  if (ticked === has) return draft;
  return {
    ...draft,
    alsoResolve: ticked ? [...draft.alsoResolve, signalId] : draft.alsoResolve.filter(id => id !== signalId),
  };
}

// ---------------------------------------------------------------------------
// Exclusions
// ---------------------------------------------------------------------------

/**
 * Codes whose step is REMOVED because an earlier action in plan order was taken and excludes them. An earlier action that is
 * itself removed does not exclude anything (its choice is void while it is removed). The catalog's lists are mutual, so it
 * does not matter which side a plan lists first.
 */
export function excludedActionCodes(plan: DecisionPlan, draft: ItemDraft): ReadonlySet<string> {
  const excluded = new Set<string>();
  plan.actions.forEach((later, j) => {
    for (let i = 0; i < j; i++) {
      const earlier = plan.actions[i];
      if (excluded.has(earlier.code)) continue;
      if (draft.decisions[actionScope(earlier.code)] === 'taken' && earlier.excludes.includes(later.code)) {
        excluded.add(later.code);
        return;
      }
    }
  });
  return excluded;
}

/** For a step that is removed: the taken earlier action that removed it, for the sentence that says why. */
export function excludedBy(plan: DecisionPlan, draft: ItemDraft, code: string): DecisionAction | null {
  const index = plan.actions.findIndex(a => a.code === code);
  const excluded = new Set<string>();
  for (let j = 0; j < plan.actions.length; j++) {
    for (let i = 0; i < j; i++) {
      const earlier = plan.actions[i];
      if (excluded.has(earlier.code)) continue;
      if (draft.decisions[actionScope(earlier.code)] === 'taken' && earlier.excludes.includes(plan.actions[j].code)) {
        if (j === index) return earlier;
        excluded.add(plan.actions[j].code);
        break;
      }
    }
  }
  return null;
}

/** The actions still on the path, in plan order. */
export function visibleActions(plan: DecisionPlan, draft: ItemDraft): DecisionAction[] {
  const excluded = excludedActionCodes(plan, draft);
  return plan.actions.filter(a => !excluded.has(a.code));
}

/** The visible actions that were taken. */
export function takenActions(plan: DecisionPlan, draft: ItemDraft): DecisionAction[] {
  return visibleActions(plan, draft).filter(a => draft.decisions[actionScope(a.code)] === 'taken');
}

/** The ticked Next steps that were taken (not skipped), in card order. */
export function takenNextSteps(plan: DecisionPlan, draft: ItemDraft): DecisionAction[] {
  return plan.nextSteps.filter(
    n => draft.nextSelected.includes(n.code) && draft.decisions[nextScope(n.code)] === 'taken'
  );
}

// ---------------------------------------------------------------------------
// Values and validity
// ---------------------------------------------------------------------------

/** The value a field shows and sends: what the user typed, else the starting value. */
export function fieldValue(draft: ItemDraft, scope: StepScope, parameterCode: string): string {
  const typed = draft.values[scope]?.[parameterCode];
  return typed !== undefined ? typed : (draft.defaults[scope]?.[parameterCode] ?? '');
}

const isBlank = (value: string): boolean => value.trim() === '';
const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/;

/**
 * A Money field is a number of zero or more (the executors refuse a negative amount). Only two shapes are read, so a value is
 * never read differently from how it was meant: plain digits with an optional ".cents" (`1250`, `1250.50`), or digits grouped
 * by commas in threes with an optional ".cents" (`1,250.50`). Anything else is refused, in particular `1,5` and `1.234,56`, where
 * a decimal comma would otherwise be read as a thousands separator and change the amount by a factor of ten or more.
 */
export function parseMoney(value: string): number | null {
  const text = value.trim();
  if (text === '') return null;
  if (!/^(\d+|\d{1,3}(,\d{3})+)(\.\d+)?$/.test(text)) return null;
  return Number(text.replace(/,/g, ''));
}

/** Whether one field is acceptable: a required field must be filled; a filled field must be well formed. */
export function parameterValid(parameter: DecisionParameter, value: string): boolean {
  if (isBlank(value)) return !parameter.required;
  switch (parameter.kind) {
    case 'Money':
      return parseMoney(value) !== null;
    case 'Date':
      return ISO_DATE.test(value.trim());
    case 'Choice':
      return !parameter.options || parameter.options.some(o => o.value === value);
    default:
      return true;
  }
}

/** True when every parameter of the step is acceptable ("Next is disabled until required fields are valid"). */
export function stepValid(action: DecisionAction, draft: ItemDraft, scope: StepScope): boolean {
  return (
    action.parameters.every(p => parameterValid(p, fieldValue(draft, scope, p.code))) &&
    composerRecipientsValid(action, draft, scope)
  );
}

/**
 * A step the EmailComposer edits commits addresses in `to` (task 160), and the server refuses an address that is not one, so every
 * address must pass the composer's own check before Next. Other steps keep their free-text `to` (a contact or firm id is allowed).
 */
function composerRecipientsValid(action: DecisionAction, draft: ItemDraft, scope: StepScope): boolean {
  if (action.composer !== 'email' || !action.parameters.some(p => p.code === 'to')) return true;
  return fieldValue(draft, scope, 'to')
    .split(/[;,\s]+/)
    .filter(a => a.trim() !== '')
    .every(a => EMAIL_RE.test(a.trim()));
}

/** The values a step sends: only filled fields, Money as a number, everything else as text. */
export function stepValues(action: DecisionAction, draft: ItemDraft, scope: StepScope): Record<string, DecisionValue> {
  const values: Record<string, DecisionValue> = {};
  for (const p of action.parameters) {
    const raw = fieldValue(draft, scope, p.code);
    if (isBlank(raw)) continue;
    values[p.code] = p.kind === 'Money' ? (parseMoney(raw) as number) : raw.trim();
  }
  return values;
}

// ---------------------------------------------------------------------------
// What Confirm shows and what is posted
// ---------------------------------------------------------------------------

/** Steps on the path with no choice yet (a Confirm cannot be recorded over them). */
export function undecidedSteps(plan: DecisionPlan, draft: ItemDraft): string[] {
  const missing: string[] = [];
  for (const a of visibleActions(plan, draft)) {
    if (!draft.decisions[actionScope(a.code)]) missing.push(a.label);
  }
  for (const n of plan.nextSteps) {
    if (draft.nextSelected.includes(n.code) && !draft.decisions[nextScope(n.code)]) missing.push(n.label);
  }
  return missing;
}

/** Skipping everything is a dismissal (Z-7): nothing taken, no Next step taken. */
export function isDismissal(plan: DecisionPlan, draft: ItemDraft): boolean {
  return takenActions(plan, draft).length === 0 && takenNextSteps(plan, draft).length === 0;
}

export type DerivedRecordClass = 'Dismissal' | 'Routine' | 'Judgement';

/**
 * The class the record will take and why, derived from the catalog and never chosen (R-2). A Next step makes it Judgement
 * whatever else was taken; otherwise it is Routine only if every action taken is. The server derives it again; this is the
 * preview Confirm shows ("Confirm shows the class before recording").
 */
export function deriveRecordClass(
  plan: DecisionPlan,
  draft: ItemDraft
): { recordClass: DerivedRecordClass; why: string } {
  const actions = takenActions(plan, draft);
  const nextSteps = takenNextSteps(plan, draft);
  if (actions.length === 0 && nextSteps.length === 0) {
    return { recordClass: 'Dismissal', why: 'Nothing was taken, so this is recorded as a dismissal.' };
  }
  if (nextSteps.length > 0) {
    return {
      recordClass: 'Judgement',
      why: `A Next step was added (${nextSteps.map(n => n.label).join(', ')}), and any Next step makes the record a judgement.`,
    };
  }
  const judging = actions.filter(a => a.recordClass !== 'Routine');
  if (judging.length > 0) {
    return {
      recordClass: 'Judgement',
      why: `${judging.map(a => a.label).join(', ')} ${judging.length === 1 ? 'is' : 'are'} a judgement.`,
    };
  }
  return {
    recordClass: 'Routine',
    why: `Only ${actions.map(a => a.label).join(', ')} of your own work ${actions.length === 1 ? 'was' : 'were'} taken, which is routine.`,
  };
}

/** The dismissal's reason is valid when it is on the lane's list and, for Other, has detail. */
export function dismissalValid(plan: DecisionPlan, draft: ItemDraft): boolean {
  const reason = plan.dismissalReasons.find(r => r.code === draft.dismissalCode);
  if (!reason) return false;
  return !reason.requiresDetail || !isBlank(draft.dismissalDetail);
}

/** True when the review can be recorded: everything decided, taken steps valid, a dismissal has its reason. */
export function reviewReady(plan: DecisionPlan, draft: ItemDraft): boolean {
  if (undecidedSteps(plan, draft).length > 0) return false;
  for (const a of takenActions(plan, draft)) {
    if (!stepValid(a, draft, actionScope(a.code))) return false;
  }
  for (const n of takenNextSteps(plan, draft)) {
    if (!stepValid(n, draft, nextScope(n.code))) return false;
  }
  return isDismissal(plan, draft) ? dismissalValid(plan, draft) : true;
}

/**
 * The ONE body posted to the commit route. Every action the plan offered appears exactly once (taken or skipped; a removed
 * step is skipped), the Next steps are the ones taken, and a dismissal is sent only when nothing was taken. Nothing is
 * written by building it.
 */
export function buildReview(plan: DecisionPlan, draft: ItemDraft, options: { retry?: boolean } = {}): DecisionReview {
  const taken = new Set(takenActions(plan, draft).map(a => a.code));
  const actions: DecisionReviewAction[] = plan.actions.map(a =>
    taken.has(a.code)
      ? { code: a.code, taken: true, values: stepValues(a, draft, actionScope(a.code)) }
      : { code: a.code, taken: false }
  );
  const nextSteps: DecisionReviewNextStep[] = takenNextSteps(plan, draft).map(n => ({
    code: n.code,
    values: stepValues(n, draft, nextScope(n.code)),
  }));

  const review: DecisionReview = { reviewId: draft.reviewId, confirmed: true, actions };
  if (nextSteps.length > 0) review.nextSteps = nextSteps;
  if (draft.alsoResolve.length > 0) review.alsoResolve = [...draft.alsoResolve];
  if (taken.size === 0 && nextSteps.length === 0) {
    // Detail is sent only for a reason that asks for it: the field is not shown for the others, so text left in the draft from
    // another reason must not be posted.
    const reason = plan.dismissalReasons.find(r => r.code === draft.dismissalCode);
    const detail = reason?.requiresDetail ? draft.dismissalDetail.trim() : '';
    review.dismissal = detail ? { code: draft.dismissalCode, detail } : { code: draft.dismissalCode };
  }
  if (options.retry) review.retry = true;
  return review;
}

/**
 * Whether the draft holds UNRECORDED CHOICES (the discard check). A choice is a decision, a ticked card, a reason, an
 * "Also resolve" tick or a value the user typed. Starting values (templated drafts, the only budget) are not choices:
 * "Opening and browsing without choosing anything closes without asking".
 */
export function hasUnrecordedChoices(draft: ItemDraft): boolean {
  return (
    Object.keys(draft.decisions).length > 0 ||
    draft.nextSelected.length > 0 ||
    draft.dismissalCode !== '' ||
    draft.dismissalDetail.trim() !== '' ||
    draft.alsoResolve.length > 0 ||
    Object.values(draft.values).some(group => group && Object.values(group).some(v => !isBlank(v)))
  );
}

/** A client review id (a GUID). `crypto.randomUUID` when present, else a RFC 4122 v4 from Math.random. */
export function newReviewId(): string {
  const g = (globalThis as { crypto?: { randomUUID?: () => string } }).crypto;
  if (g?.randomUUID) return g.randomUUID();
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, c => {
    const r = (Math.random() * 16) | 0;
    return (c === 'x' ? r : (r & 0x3) | 0x8).toString(16);
  });
}
