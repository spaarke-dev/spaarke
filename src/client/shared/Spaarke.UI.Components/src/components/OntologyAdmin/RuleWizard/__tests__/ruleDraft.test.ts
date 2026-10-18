/**
 * ruleDraft (task 102; D-22, D-149): the condition is composed only from the catalog and the controls; one serialisation serves
 * validate, the dry run, save and publish (the dry run's proof is a hash of those exact strings); ANY edit changes the draft key (so a
 * test is stale after an edit); a value that cannot be written is reported, anything else is left to the BFF.
 */

import {
  buildDecisionPlan,
  buildDraftBody,
  buildRuleBody,
  composeRelativeDate,
  draftKey,
  draftPayload,
  emptyClause,
  emptyDraft,
  emptyRow,
  hasWork,
  newPolicyRequest,
  priorityProblem,
  quietWindowProblem,
  scopeChangeSentence,
  scopeIntentRequest,
  scopeRequest,
  validateRequest,
} from '../ruleDraft';
import { initialDraftOf, versionModeOf } from '../ruleWizardTypes';
import type { RuleDetail } from '../../Rules/rulesService';
import { stepOfField, STEP_FIELDS } from '../wizardSteps';
import type { ConditionDraft, FilterRow, RuleDraft } from '../ruleDraft';
import { catalog } from './fixtures';

const row = (over: Partial<FilterRow>): FilterRow => ({ ...emptyRow(catalog), ...over });

const existence = (over: Partial<ConditionDraft> = {}): ConditionDraft => ({
  ruleType: 'Existence',
  subject: 'zz_widget',
  when: [],
  clauses: [],
  thresholdField: '',
  thresholdOperator: '>=',
  thresholdValue: '',
  ...over,
});

const bodyOf = (condition: ConditionDraft, quiet = ''): Record<string, unknown> => {
  const built = buildRuleBody(condition, quiet, catalog);
  expect(built.issues).toEqual({});
  return JSON.parse(built.ruleBody as string) as Record<string, unknown>;
};

describe('buildRuleBody - Existence', () => {
  it('writes the type, the subject and nothing else for a bare condition', () => {
    expect(bodyOf(existence())).toEqual({ type: 'Existence', subject: 'zz_widget', all: [] });
  });

  it('writes "=" as a bare scalar and the other comparisons as {op: value}, typed', () => {
    const when = [
      row({ field: 'zz_state', op: '=', valueKind: 'number', value: '0' }),
      row({ field: 'zz_colour', op: '<>', valueKind: 'text', value: 'red' }),
      row({ field: 'zz_due', op: '>=', valueKind: 'date', date: { form: 'now-{N}d', days: '7' } }),
    ];
    expect(bodyOf(existence({ when })).when).toEqual({
      zz_state: 0,
      zz_colour: { '<>': 'red' },
      zz_due: { '>=': 'now-7d' },
    });
  });

  it('writes a yes-or-no value as a JSON boolean', () => {
    const when = [row({ field: 'zz_state', valueKind: 'boolean', value: 'false' })];
    expect(bodyOf(existence({ when })).when).toEqual({ zz_state: false });
  });

  it("writes a list, a window and a null test as the grammar's shapes", () => {
    const when = [
      row({ field: 'zz_state', test: 'in', valueKind: 'number', listText: '1, 2' }),
      row({
        field: 'zz_due',
        test: 'window',
        lowerOp: '>=',
        lower: { form: 'now', days: '' },
        upperOp: '<',
        upper: { form: 'now+{N}d', days: '3' },
      }),
      row({ field: 'zz_colour', test: 'null', op: 'notNull' }),
    ];
    expect(bodyOf(existence({ when })).when).toEqual({
      zz_state: [1, 2],
      zz_due: { '>=': 'now', '<': 'now+3d' },
      zz_colour: { notNull: true },
    });
  });

  it('sends the triage category IDS, never the names', () => {
    const clause = {
      ...emptyClause(catalog, 'zz_mail'),
      filter: [row({ field: 'zz_triage', test: 'in', ids: ['cat-aaa', 'cat-bbb'] })],
    };
    const body = bodyOf(existence({ clauses: [clause] }));
    expect(body.all).toEqual([
      { exists: 'zz_mail', path: 'zz_widgetlink', filter: { zz_triage: ['cat-aaa', 'cat-bbb'] } },
    ]);
    expect(JSON.stringify(body)).not.toContain('Cheese');
  });

  it('sends chosen ids only for the triage column of the triage table; another column never inherits them', () => {
    const stale = row({ field: 'zz_noted', test: 'in', ids: ['cat-aaa'], listText: '7', valueKind: 'number' });
    const clause = { ...emptyClause(catalog, 'zz_mail'), filter: [stale] };
    expect(bodyOf(existence({ clauses: [clause] })).all).toEqual([
      { exists: 'zz_mail', path: 'zz_widgetlink', filter: { zz_noted: [7] } },
    ]);
    // The triage column's name on another table is not the triage column.
    const elsewhere = row({ field: 'zz_triage', test: 'in', ids: ['cat-aaa'], listText: '5', valueKind: 'number' });
    const other = { ...emptyClause(catalog, 'zz_note'), filter: [elsewhere] };
    expect(bodyOf(existence({ clauses: [other] })).all).toEqual([
      { exists: 'zz_note', path: 'zz_widgetref', filter: { zz_triage: [5] } },
    ]);
  });

  it('writes the join path from the catalog into a clause; the admin never chooses it', () => {
    const clause = {
      ...emptyClause(catalog, 'zz_note'),
      kind: 'notExists' as const,
      filter: [row({ field: 'zz_kind', value: 'x' })],
    };
    expect(bodyOf(existence({ clauses: [clause] })).all).toEqual([
      { notExists: 'zz_note', path: 'zz_widgetref', filter: { zz_kind: 'x' } },
    ]);
  });

  it('writes the quiet window only when it is set', () => {
    expect(bodyOf(existence(), '')).not.toHaveProperty('quietWindowDays');
    expect(bodyOf(existence(), ' 30 ')).toHaveProperty('quietWindowDays', 30);
  });
});

describe('buildRuleBody - Threshold', () => {
  const threshold = (over: Partial<ConditionDraft> = {}) =>
    existence({
      ruleType: 'Threshold',
      thresholdField: 'zz_load',
      thresholdOperator: '>=',
      thresholdValue: '80',
      ...over,
    });

  it('writes one comparison and no clauses', () => {
    expect(bodyOf(threshold())).toEqual({
      type: 'Threshold',
      subject: 'zz_widget',
      threshold: { field: 'zz_load', operator: '>=', value: 80 },
    });
  });

  it('reports the measure and the value that are missing, and writes no body', () => {
    expect(buildRuleBody(threshold({ thresholdField: '' }), '', catalog)).toEqual({
      ruleBody: null,
      issues: { threshold: 'Choose the measure to compare.' },
    });
    expect(buildRuleBody(threshold({ thresholdValue: 'lots' }), '', catalog).issues.threshold).toBe('Enter a number.');
  });
});

describe('buildRuleBody - only what cannot be written is refused here', () => {
  it('reports a missing subject and a missing column, and leaves every other judgement to the BFF', () => {
    expect(buildRuleBody(existence({ subject: '' }), '', catalog).ruleBody).toBeNull();

    const noColumn = row({ field: '', value: 'x' });
    const built = buildRuleBody(existence({ when: [noColumn] }), '', catalog);
    expect(built.ruleBody).toBeNull();
    expect(built.issues[noColumn.id]).toBe('Choose a column.');

    // A column the catalog does not list for the table, a value out of any plausible range: written as chosen. The BFF refuses them.
    const unusual = existence({ when: [row({ field: 'not_in_any_list', valueKind: 'number', value: '99999999' })] });
    expect(buildRuleBody(unusual, '', catalog).ruleBody).not.toBeNull();
  });

  it("reports an empty value, a non-number, an empty list and a list over the catalog's limit", () => {
    const empty = row({ field: 'zz_state', value: '  ' });
    const nan = row({ field: 'zz_colour', valueKind: 'number', value: 'abc' });
    const none = row({ field: 'zz_due', test: 'in', listText: ' , ' });
    const tooMany = row({ field: 'zz_load', test: 'in', listText: 'a,b,c,d' });
    const built = buildRuleBody(existence({ when: [empty, nan, none, tooMany] }), '', catalog);

    expect(built.issues[empty.id]).toBe('Enter a value.');
    expect(built.issues[nan.id]).toBe('Enter a number.');
    expect(built.issues[none.id]).toBe('Add at least one value.');
    expect(built.issues[tooMany.id]).toBe('A list takes at most 3 values.');
  });

  it('reports a relative date whose day count is not a whole number', () => {
    expect(composeRelativeDate({ form: 'now-{N}d', days: '7' })).toBe('now-7d');
    expect(composeRelativeDate({ form: 'now-{N}d', days: '7.5' })).toBeNull();
    expect(composeRelativeDate({ form: 'now-{N}d', days: '' })).toBeNull();
    expect(composeRelativeDate({ form: 'now', days: '' })).toBe('now');
  });
});

describe('quiet window and priority', () => {
  it('the quiet window is written for the Decide lane only', () => {
    const base: RuleDraft = { ...emptyDraft(catalog), condition: existence(), quietWindowDays: '30' };
    expect(JSON.parse(buildDraftBody({ ...base, lane: 'Decide' }, catalog).ruleBody as string)).toHaveProperty(
      'quietWindowDays',
      30
    );
    expect(JSON.parse(buildDraftBody({ ...base, lane: 'Do' }, catalog).ruleBody as string)).not.toHaveProperty(
      'quietWindowDays'
    );
  });

  it("checks the quiet window against the catalog's limit and the priority for a whole number", () => {
    expect(quietWindowProblem('', catalog)).toBeNull();
    expect(quietWindowProblem('90', catalog)).toBeNull();
    expect(quietWindowProblem('91', catalog)).toMatch(/0 to 90/);
    expect(quietWindowProblem('1.5', catalog)).not.toBeNull();
    expect(priorityProblem('')).toBeNull();
    expect(priorityProblem('250')).toBeNull();
    expect(priorityProblem('-1')).not.toBeNull();
  });
});

describe('one serialisation for validate, the dry run, save and publish', () => {
  const draft: RuleDraft = {
    ...emptyDraft(catalog),
    policyCode: ' POL-X ',
    name: ' Name ',
    shortName: ' ',
    lane: 'Decide',
    severity: 11,
    workType: 7,
    condition: existence({ when: [row({ field: 'zz_state', value: 'open' })] }),
    shortHeadline: 'Head',
    messageTemplate: 'Msg {{zz_state}}',
    proposedAction: 'Do it',
    actions: ['frobnicate'],
    nextSteps: ['ring-bell'],
    priority: ' 250 ',
  };

  it('puts the same body, template and plan text in every request', () => {
    const body = buildDraftBody(draft, catalog).ruleBody as string;
    const validate = validateRequest(draft, body, null);
    const save = newPolicyRequest(draft, body);
    const publish = { draft: draftPayload(draft, body) };

    expect(validate.draft).toEqual(save.draft);
    expect(save.draft).toEqual(publish.draft);
    expect(save.draft.decisionPlan).toBe(buildDecisionPlan(draft));
    expect(JSON.parse(save.draft.decisionPlan)).toEqual({ actions: ['frobnicate'], nextSteps: ['ring-bell'] });
  });

  it('validates a new rule by its lane and a new version by its policy', () => {
    const body = buildDraftBody(draft, catalog).ruleBody as string;
    expect(validateRequest(draft, body, null)).toMatchObject({ lane: 'Decide' });
    expect(validateRequest(draft, body, null)).not.toHaveProperty('policyId');
    expect(validateRequest(draft, body, 'p-1')).toMatchObject({ policyId: 'p-1' });
    expect(validateRequest(draft, body, 'p-1')).not.toHaveProperty('lane');
  });

  it('trims the identity, sends no short name when blank, and a priority only when typed', () => {
    const body = buildDraftBody(draft, catalog).ruleBody as string;
    expect(newPolicyRequest(draft, body)).toMatchObject({
      policyCode: 'POL-X',
      name: 'Name',
      shortName: null,
      priority: 250,
    });
    expect(newPolicyRequest({ ...draft, priority: '' }, body).priority).toBeNull();
  });
});

describe('draftKey - a test is for exactly one draft', () => {
  const base: RuleDraft = {
    ...emptyDraft(catalog),
    lane: 'Decide',
    condition: existence({ when: [row({ field: 'zz_state', value: 'a' })] }),
  };

  it('changes on every kind of edit', () => {
    const edits: Array<(d: RuleDraft) => RuleDraft> = [
      d => ({ ...d, messageTemplate: 'x' }),
      d => ({ ...d, shortHeadline: 'x' }),
      d => ({ ...d, proposedAction: 'x' }),
      d => ({ ...d, actions: ['frobnicate'] }),
      d => ({ ...d, nextSteps: ['ring-bell'] }),
      d => ({ ...d, priority: '1' }),
      d => ({ ...d, quietWindowDays: '5' }),
      d => ({ ...d, countsTowardSuppression: false }),
      d => ({ ...d, matterId: 'm-1' }),
      d => ({ ...d, condition: { ...d.condition, thresholdValue: '1' } }),
      d => ({ ...d, condition: { ...d.condition, when: d.condition.when.map(r => ({ ...r, value: 'b' })) } }),
    ];
    for (const edit of edits) {
      expect(draftKey(edit(base))).not.toBe(draftKey(base));
    }
  });

  it('binds the matter scope by its id, not by the name shown for it (task 102b)', () => {
    const scoped: RuleDraft = { ...base, matterId: 'm-1', matterName: 'Matter One' };
    expect(draftKey({ ...scoped, matterName: 'Renamed' })).toBe(draftKey(scoped));
    expect(draftKey({ ...scoped, matterId: 'm-2' })).not.toBe(draftKey(scoped));
  });

  it('ignores the identity of rows, which is never sent', () => {
    const renamed: RuleDraft = {
      ...base,
      condition: { ...base.condition, when: base.condition.when.map(r => ({ ...r, id: 'other' })) },
    };
    expect(draftKey(renamed)).toBe(draftKey(base));
    expect(hasWork(renamed, base)).toBe(false);
    expect(hasWork({ ...base, name: 'x' }, base)).toBe(true);
  });
});

describe('the matter scope (task 102b; D-165)', () => {
  const draft: RuleDraft = {
    ...emptyDraft(catalog),
    lane: 'Decide',
    condition: existence({ when: [row({ field: 'zz_state', value: 'open' })] }),
    messageTemplate: 'Msg',
    actions: ['frobnicate'],
  };
  const scoped: RuleDraft = { ...draft, matterId: 'm-1', matterName: 'Matter One' };

  it('says the scope intent explicitly on the test and the publish: the matter id, or allMatters (fix round 1, F4)', () => {
    expect(scopeIntentRequest(scoped)).toEqual({ matterId: 'm-1' });
    expect(scopeIntentRequest(draft)).toEqual({ allMatters: true });
  });

  it('states a scope change in plain words, and nothing when the scope is kept (fix round 1, F4)', () => {
    const storedM = { matterId: 'm-1', matterName: 'Matter One' };
    expect(scopeChangeSentence(storedM, draft)).toBe('This changes the rule from Matter One to all matters.');
    expect(scopeChangeSentence({ matterId: null, matterName: null }, scoped)).toBe(
      'This changes the rule from all matters to Matter One.'
    );
    expect(scopeChangeSentence(storedM, { ...draft, matterId: 'm-2', matterName: 'Matter Two' })).toBe(
      'This changes the rule from Matter One to Matter Two.'
    );
    expect(scopeChangeSentence({ matterId: 'm-7', matterName: null }, draft)).toBe(
      'This changes the rule from m-7 to all matters.'
    );
    expect(scopeChangeSentence(storedM, scoped)).toBeNull();
    expect(scopeChangeSentence({ matterId: null, matterName: null }, draft)).toBeNull();
  });

  it('a new draft applies to all matters', () => {
    expect(emptyDraft(catalog)).toMatchObject({ matterId: null, matterName: null });
  });

  it('sends only the matter id, on validate and save alike, and nothing for all matters', () => {
    const body = buildDraftBody(draft, catalog).ruleBody as string;
    expect(scopeRequest(scoped)).toEqual({ matterId: 'm-1' });
    expect(scopeRequest(draft)).toEqual({});
    expect(validateRequest(scoped, body, null).matterId).toBe('m-1');
    expect(validateRequest(scoped, body, 'p-1').matterId).toBe('m-1');
    expect(newPolicyRequest(scoped, body).matterId).toBe('m-1');
    expect(validateRequest(draft, body, null)).not.toHaveProperty('matterId');
    expect(newPolicyRequest(draft, body)).not.toHaveProperty('matterId');
    for (const request of [validateRequest(scoped, body, null), newPolicyRequest(scoped, body)]) {
      expect(JSON.stringify(request)).not.toContain('Matter One');
      expect(request).not.toHaveProperty('tenant');
    }
  });

  it('validate says the scope it means, like the test and the publish: the matter, or allMatters when All matters is chosen', () => {
    const body = buildDraftBody(draft, catalog).ruleBody as string;
    expect(validateRequest(draft, body, 'p-1')).toMatchObject({ allMatters: true });
    expect(validateRequest(draft, body, 'p-1')).not.toHaveProperty('matterId');
    expect(validateRequest(draft, body, null)).toMatchObject({ allMatters: true });
    expect(validateRequest(scoped, body, 'p-1')).toMatchObject({ matterId: 'm-1' });
    expect(validateRequest(scoped, body, 'p-1')).not.toHaveProperty('allMatters');
    expect(validateRequest(scoped, body, null)).not.toHaveProperty('allMatters');
    // Save carries only the matter: it only ever creates a new rule.
    expect(newPolicyRequest(draft, body)).not.toHaveProperty('allMatters');
    expect(newPolicyRequest(scoped, body)).not.toHaveProperty('allMatters');
  });

  it('places a matterId refusal on the Scope and noise step', () => {
    expect(stepOfField('matterId')).toBe('scope');
    expect(STEP_FIELDS.scope).toContain('matterId');
  });

  it('a new version starts with the scope the rule has', () => {
    const detail = {
      summary: { policyId: 'p-1', code: 'POL-A', name: 'Rule A', lane: 'Decide', versionNumber: 2, enabled: true },
      wording: { headline: null, messageTemplate: null, proposedAction: null },
      plan: { actions: [], nextSteps: [] },
      scope: { tenant: null, matterId: 'm-9', matterName: 'Matter Nine', priority: null },
      noise: {
        countsTowardSuppression: true,
        suppressAfterDismissals: 3,
        suppressionWindowDays: 30,
        quietWindowDays: 21,
      },
    } as unknown as RuleDetail;
    const mode = versionModeOf(detail);
    expect(mode.scope).toEqual({ tenant: null, matter: 'Matter Nine', matterId: 'm-9' });
    expect(initialDraftOf(catalog, mode)).toMatchObject({ matterId: 'm-9', matterName: 'Matter Nine' });
    expect(initialDraftOf(catalog, { kind: 'new' })).toMatchObject({ matterId: null });
  });
});
