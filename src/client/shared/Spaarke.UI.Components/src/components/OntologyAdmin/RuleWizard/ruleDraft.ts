/**
 * ruleDraft - the rule wizard's draft and the pure functions over it (spaarke-ontology-platform-r1 task 102; D-22, D-149).
 *
 * The draft holds what the admin chose, in the shape of the steps. The condition is built ONLY from the BFF catalog
 * (`authoringService.AuthoringCatalog`): the wizard offers no free-text JSON, and `buildRuleBody` only composes the closed grammar of
 * `existence-rule.schema.json` / `threshold-rule.schema.json` from the controls. It does not decide whether the body is acceptable:
 * the BFF's `POST /authoring/validate` does (the same validator save and publish run), and the wizard shows its answer.
 *
 * What this file does check locally is only what cannot be written down at all: a column not chosen, a value left empty, a number
 * that is not a number. Those cannot be serialised into any body, so the row says so and the wizard does not ask the BFF yet.
 *
 * The body, the headline, the plan and every other part are serialised ONCE per draft by these functions, and the same text goes to
 * validate, the dry run, save and publish. The dry run's proof is a hash of that exact text, so a second serialiser would break it.
 */

import type {
  AuthoringCatalog,
  CatalogLane,
  CatalogSubject,
  NewPolicyRequest,
  RuleDraftPayload,
  RuleScopeIntentRequest,
  RuleScopeRequest,
  ValidateRequest,
} from './authoringService';

// ---- the condition ------------------------------------------------------------------------------------------------------------

/** How a typed value is written into the body: a JSON string, a JSON number, a JSON boolean, or a relative date string. */
export type ValueKind = 'text' | 'number' | 'boolean' | 'date';

export interface RelativeDateDraft {
  /** One of the catalog's `relativeDates.forms`, for example `now` or `now-{N}d`. */
  form: string;
  /** The day count that replaces `{N}`; ignored by a form without one. */
  days: string;
}

/** The kinds of test one column can take; each is one shape of the grammar's filter value. */
export type FilterTestKind = 'compare' | 'in' | 'window' | 'null';

export interface FilterRow {
  id: string;
  /** The column. Empty until the admin chooses one. */
  field: string;
  test: FilterTestKind;
  /** `compare`: a catalog comparison operator. `null`: a catalog null-test member. */
  op: string;
  valueKind: ValueKind;
  value: string;
  date: RelativeDateDraft;
  /** `in` over typed values: the values, separated by commas. */
  listText: string;
  /** `in` over the triage categories: the chosen category ids. */
  ids: string[];
  /** `window`: one lower and one upper bound, both relative dates. */
  lowerOp: string;
  lower: RelativeDateDraft;
  upperOp: string;
  upper: RelativeDateDraft;
}

export type ClauseKind = 'exists' | 'notExists';

export interface ClauseDraft {
  id: string;
  kind: ClauseKind;
  /** The related table, one of the subject's catalog joins. */
  related: string;
  filter: FilterRow[];
}

export interface ConditionDraft {
  /** One of the catalog's rule types. */
  ruleType: string;
  /** One of the catalog's subjects. Empty until chosen. */
  subject: string;
  /** The subject's own conditions (`when`). */
  when: FilterRow[];
  /** Existence only. */
  clauses: ClauseDraft[];
  /** Threshold only: a column from the subject's `thresholdFields`, an operator from `operators.threshold`, a number. */
  thresholdField: string;
  thresholdOperator: string;
  thresholdValue: string;
}

// ---- the whole draft ----------------------------------------------------------------------------------------------------------

export interface RuleDraft {
  // Basics (a new rule only; a new version keeps its rule's)
  policyCode: string;
  name: string;
  shortName: string;
  /** One of the catalog's lanes. Empty until chosen. */
  lane: string;
  /** A `severities` value of the lane, or null for none. */
  severity: number | null;
  workType: number | null;
  condition: ConditionDraft;
  // Wording
  shortHeadline: string;
  messageTemplate: string;
  proposedAction: string;
  // Actions: catalog codes. Order matters; the first action is the recommendation.
  actions: string[];
  nextSteps: string[];
  // Scope and noise
  /** Task 102b (D-165): the one matter the rule is limited to, or null for all matters. Only the id is sent. */
  matterId: string | null;
  /** The chosen matter's name, for display only (never sent). */
  matterName: string | null;
  priority: string;
  countsTowardSuppression: boolean;
  quietWindowDays: string;
}

/** The lane whose dismissed items wait out a quiet window (FR-17a). The other lane re-raises when the item's date changes. */
export const QUIET_WINDOW_LANE = 'Decide';

/** The rule type whose body has the Threshold shape (one comparison, no clauses). Every other catalog type is composed as Existence. */
export const RULE_TYPE_THRESHOLD = 'Threshold';

let rowSequence = 0;
/** A fresh id for a row or clause (UI identity only; never sent). */
export function newRowId(prefix: string): string {
  rowSequence += 1;
  return `${prefix}-${rowSequence}`;
}

/** The first entry of a catalog list, or an empty string when the catalog offers none. */
export function firstOf(list: readonly string[]): string {
  return list.length > 0 ? list[0] : '';
}

export function emptyDate(catalog: AuthoringCatalog): RelativeDateDraft {
  return { form: firstOf(catalog.relativeDates.forms), days: '' };
}

export function emptyRow(catalog: AuthoringCatalog): FilterRow {
  const date = emptyDate(catalog);
  return {
    id: newRowId('row'),
    field: '',
    test: 'compare',
    op: firstOf(catalog.operators.comparison),
    valueKind: 'text',
    value: '',
    date,
    listText: '',
    ids: [],
    lowerOp: firstOf(catalog.operators.rangeLower),
    lower: date,
    upperOp: firstOf(catalog.operators.rangeUpper),
    upper: { ...date },
  };
}

export function emptyClause(catalog: AuthoringCatalog, related: string): ClauseDraft {
  return { id: newRowId('clause'), kind: 'exists', related, filter: [emptyRow(catalog)] };
}

export function emptyCondition(catalog: AuthoringCatalog): ConditionDraft {
  return {
    ruleType: firstOf(catalog.ruleTypes),
    subject: '',
    when: [],
    clauses: [],
    thresholdField: '',
    thresholdOperator: firstOf(catalog.operators.threshold),
    thresholdValue: '',
  };
}

export function emptyDraft(catalog: AuthoringCatalog): RuleDraft {
  return {
    policyCode: '',
    name: '',
    shortName: '',
    lane: '',
    severity: null,
    workType: null,
    condition: emptyCondition(catalog),
    shortHeadline: '',
    messageTemplate: '',
    proposedAction: '',
    actions: [],
    nextSteps: [],
    matterId: null,
    matterName: null,
    priority: '',
    countsTowardSuppression: true,
    quietWindowDays: '',
  };
}

export function laneOf(catalog: AuthoringCatalog, lane: string): CatalogLane | undefined {
  return catalog.lanes.find(l => l.lane === lane);
}

export function subjectOf(catalog: AuthoringCatalog, entity: string): CatalogSubject | undefined {
  return catalog.subjects.find(s => s.entity === entity);
}

// ---- scalar values ------------------------------------------------------------------------------------------------------------

/** The relative date a draft date stands for (`now-{N}d` with 7 days is `now-7d`), or null when the day count is not a whole number. */
export function composeRelativeDate(date: RelativeDateDraft): string | null {
  if (!date.form.includes('{N}')) {
    return date.form === '' ? null : date.form;
  }
  const days = date.days.trim();
  return /^\d+$/.test(days) ? date.form.split('{N}').join(String(Number(days))) : null;
}

/** One scalar of the grammar, or the sentence that says why this input cannot be written as one. */
function scalarOf(
  kind: ValueKind,
  value: string,
  date: RelativeDateDraft
): { value: string | number | boolean } | { problem: string } {
  switch (kind) {
    case 'text':
      return value.trim() === '' ? { problem: 'Enter a value.' } : { value };
    case 'number': {
      const trimmed = value.trim();
      const parsed = Number(trimmed);
      return trimmed === '' || !Number.isFinite(parsed) ? { problem: 'Enter a number.' } : { value: parsed };
    }
    case 'boolean':
      return value === 'true' || value === 'false' ? { value: value === 'true' } : { problem: 'Choose yes or no.' };
    case 'date': {
      const composed = composeRelativeDate(date);
      return composed === null ? { problem: 'Enter a whole number of days.' } : { value: composed };
    }
  }
}

// ---- the body -----------------------------------------------------------------------------------------------------------------

export interface BuiltBody {
  /** The `sprk_rulebody` JSON text, or null while some input cannot be written into it yet. */
  ruleBody: string | null;
  /** Why not, by row id (`subject`, `threshold`, `all` for the condition as a whole). */
  issues: Record<string, string>;
}

function filterOf(
  rows: FilterRow[],
  /** The table the rows are about: chosen category ids count only on the catalog's triage column of the triage table. */
  entity: string,
  catalog: AuthoringCatalog,
  issues: Record<string, string>
): Record<string, unknown> {
  const filter: Record<string, unknown> = {};
  for (const row of rows) {
    if (row.field === '') {
      issues[row.id] = 'Choose a column.';
      continue;
    }
    switch (row.test) {
      case 'compare': {
        const scalar = scalarOf(row.valueKind, row.value, row.date);
        if ('problem' in scalar) {
          issues[row.id] = scalar.problem;
        } else {
          filter[row.field] = row.op === '=' ? scalar.value : { [row.op]: scalar.value };
        }
        break;
      }
      case 'in': {
        const values: Array<string | number> = [];
        let problem: string | undefined;
        if (entity === catalog.triage.entity && row.field === catalog.triage.field && row.ids.length > 0) {
          values.push(...row.ids);
        } else {
          for (const part of row.listText.split(',')) {
            const scalar = scalarOf(row.valueKind === 'number' ? 'number' : 'text', part, row.date);
            if ('problem' in scalar) {
              if (part.trim() !== '') {
                problem = scalar.problem;
              }
            } else if (typeof scalar.value !== 'boolean') {
              values.push(scalar.value);
            }
          }
        }
        if (problem !== undefined) {
          issues[row.id] = problem;
        } else if (values.length === 0) {
          issues[row.id] = 'Add at least one value.';
        } else if (values.length > catalog.operators.maxInListLength) {
          issues[row.id] = `A list takes at most ${catalog.operators.maxInListLength} values.`;
        } else {
          filter[row.field] = values;
        }
        break;
      }
      case 'window': {
        const lower = composeRelativeDate(row.lower);
        const upper = composeRelativeDate(row.upper);
        if (lower === null || upper === null) {
          issues[row.id] = 'Enter a whole number of days for each end of the window.';
        } else {
          filter[row.field] = { [row.lowerOp]: lower, [row.upperOp]: upper };
        }
        break;
      }
      case 'null':
        filter[row.field] = { [row.op]: true };
        break;
    }
  }
  return filter;
}

/**
 * The rule body for a condition, composed from the controls only. `quietWindowDays` is written only when it is set; a body without
 * it takes the BFF's default.
 */
export function buildRuleBody(
  condition: ConditionDraft,
  quietWindowDays: string,
  catalog: AuthoringCatalog
): BuiltBody {
  const issues: Record<string, string> = {};
  if (condition.ruleType === '') {
    issues.ruleType = 'Choose a rule type.';
  }
  if (condition.subject === '') {
    issues.subject = 'Choose what the rule is about.';
  }
  if (Object.keys(issues).length > 0) {
    return { ruleBody: null, issues };
  }

  const body: Record<string, unknown> = { type: condition.ruleType, subject: condition.subject };
  const when = filterOf(condition.when, condition.subject, catalog, issues);

  if (condition.ruleType === RULE_TYPE_THRESHOLD) {
    const value = scalarOf('number', condition.thresholdValue, emptyDate(catalog));
    if (condition.thresholdField === '') {
      issues.threshold = 'Choose the measure to compare.';
    } else if ('problem' in value) {
      issues.threshold = value.problem;
    } else {
      body.threshold = { field: condition.thresholdField, operator: condition.thresholdOperator, value: value.value };
    }
    if (Object.keys(when).length > 0) {
      body.when = when;
    }
  } else {
    if (Object.keys(when).length > 0) {
      body.when = when;
    }
    body.all = condition.clauses.map(clause => {
      const join = subjectOf(catalog, condition.subject)?.joins.find(j => j.related === clause.related);
      if (join === undefined) {
        issues[clause.id] = 'Choose the related records.';
      }
      return {
        [clause.kind]: clause.related,
        path: join?.path ?? '',
        filter: filterOf(clause.filter, clause.related, catalog, issues),
      };
    });
  }

  const window = quietWindowDays.trim();
  if (window !== '') {
    const parsed = Number(window);
    if (/^\d+$/.test(window) && Number.isSafeInteger(parsed)) {
      body.quietWindowDays = parsed;
    }
  }

  return Object.keys(issues).length > 0 ? { ruleBody: null, issues } : { ruleBody: JSON.stringify(body), issues };
}

/** The body of a draft: its condition, and the quiet window only for the lane that has one. */
export function buildDraftBody(draft: RuleDraft, catalog: AuthoringCatalog): BuiltBody {
  return buildRuleBody(draft.condition, draft.lane === QUIET_WINDOW_LANE ? draft.quietWindowDays : '', catalog);
}

/** The quiet window as typed: empty, or a whole number within the catalog's limit. Returns the sentence when it is not. */
export function quietWindowProblem(quietWindowDays: string, catalog: AuthoringCatalog): string | null {
  const text = quietWindowDays.trim();
  if (text === '') {
    return null;
  }
  const parsed = Number(text);
  if (!/^\d+$/.test(text) || !Number.isSafeInteger(parsed) || parsed > catalog.limits.maxQuietWindowDays) {
    return `Enter a whole number of days from 0 to ${catalog.limits.maxQuietWindowDays}.`;
  }
  return null;
}

/** The priority as typed: empty (the BFF's default), or a whole number. The BFF owns the range. */
export function priorityProblem(priority: string): string | null {
  const text = priority.trim();
  return text === '' || /^\d+$/.test(text) ? null : 'Enter a whole number.';
}

// ---- the plan -----------------------------------------------------------------------------------------------------------------

/** The `sprk_decisionplan` JSON text: `{"actions":[...],"nextSteps":[...]}`. Empty actions are sent as such; the BFF refuses them. */
export function buildDecisionPlan(draft: RuleDraft): string {
  return JSON.stringify({ actions: draft.actions, nextSteps: draft.nextSteps });
}

// ---- requests -----------------------------------------------------------------------------------------------------------------

export function draftPayload(draft: RuleDraft, ruleBody: string): RuleDraftPayload {
  return {
    ruleType: draft.condition.ruleType,
    ruleBody,
    messageTemplate: draft.messageTemplate,
    shortHeadline: draft.shortHeadline,
    proposedAction: draft.proposedAction,
    decisionPlan: buildDecisionPlan(draft),
  };
}

/**
 * The scope every request carries (task 102b): the matter id when the draft is limited to one, nothing for all matters. It is sent as
 * the draft holds it, whatever the subject: the BFF's one shared check refuses a limit on a rule that is not about matters, and the
 * wizard shows that refusal on the scope field.
 */
export function scopeRequest(draft: RuleDraft): RuleScopeRequest {
  return draft.matterId === null ? {} : { matterId: draft.matterId };
}

/**
 * Task 102b fix round 1 (F4): the scope the validate (fix round 2, K2), the test and the publish carry, said explicitly: the matter id, or `allMatters: true` when "All
 * matters" is chosen. Never neither: the BFF refuses a publish of a rule limited to a matter that names no scope, so a limit is cleared
 * only when the admin chose All matters.
 */
export function scopeIntentRequest(draft: RuleDraft): RuleScopeIntentRequest {
  return draft.matterId === null ? { allMatters: true } : { matterId: draft.matterId };
}

/**
 * Task 102b fix round 1 (F4): the sentence the publish confirmation adds when the publish changes the rule's scope, or null when it keeps
 * it. `stored` is the scope in force (its id and, when known, its name); `draft` holds the scope chosen.
 */
export function scopeChangeSentence(
  stored: { matterId: string | null; matterName: string | null },
  draft: RuleDraft
): string | null {
  if (stored.matterId === draft.matterId) {
    return null;
  }
  const from = stored.matterId === null ? 'all matters' : (stored.matterName ?? stored.matterId);
  const to = draft.matterId === null ? 'all matters' : (draft.matterName ?? draft.matterId);
  return `This changes the rule from ${from} to ${to}.`;
}

export function validateRequest(draft: RuleDraft, ruleBody: string, policyId: string | null): ValidateRequest {
  return policyId === null
    ? { lane: draft.lane, draft: draftPayload(draft, ruleBody), ...scopeIntentRequest(draft) }
    : { policyId, draft: draftPayload(draft, ruleBody), ...scopeIntentRequest(draft) };
}

export function newPolicyRequest(draft: RuleDraft, ruleBody: string): NewPolicyRequest {
  const priority = draft.priority.trim();
  return {
    policyCode: draft.policyCode.trim(),
    name: draft.name.trim(),
    shortName: draft.shortName.trim() === '' ? null : draft.shortName.trim(),
    lane: draft.lane,
    severity: draft.severity,
    workType: draft.workType,
    priority: priority === '' ? null : Number(priority),
    countsTowardSuppression: draft.countsTowardSuppression,
    draft: draftPayload(draft, ruleBody),
    ...scopeRequest(draft),
  };
}

/**
 * Identity of everything the admin chose. The Test is bound to it: ANY edit changes the key, so a test run for an earlier draft is
 * never current for this one (publish stays disabled until the Test is run again).
 */
export function draftKey(draft: RuleDraft): string {
  // The matter's display name is not part of the rule (the same id is the same scope); its id is.
  return JSON.stringify(draft, (key, value: unknown) => (key === 'id' || key === 'matterName' ? undefined : value));
}

/** Whether anything has been chosen or typed (so closing needs a discard check). */
export function hasWork(draft: RuleDraft, initial: RuleDraft): boolean {
  return draftKey(draft) !== draftKey(initial);
}
