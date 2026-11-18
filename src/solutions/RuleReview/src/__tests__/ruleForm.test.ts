/**
 * ebilling-r1 task 130e-2, fix round 1 (F3, F4, F5, F6): what the rule form would send and whether it may be saved (`computeRuleForm`),
 * the firm description field's own state (`firmFieldReducer`), and which refusals the page keeps (`refusalsAfterSelect`). The component calls
 * these on every render, so a change here is a change in what a click on Save sends; the wiring itself is pinned in `firmDescriptionWiring.test.ts`.
 */

import {
  NO_REFUSALS,
  REASON_BRACES,
  REASON_MARKUP,
  firmDescriptionRefusals,
  refusalsAfterSelect,
  type FirmRefusal,
} from '../services/firmDescription';
import {
  computeRuleForm,
  firmFieldReducer,
  initialFirmField,
  shownRefusals,
  type RuleFormContext,
  type RuleFormValues,
} from '../services/ruleForm';
import { saveEdit } from '../services/reviewActions';
import { ruleTypeDef, toParamValues } from '../services/ruleTypes';
import { toFailure, type ProposalView } from '../services/ruleSetApi';
import { POLICY_ID, SET_ID, detail, mockApi, ok, problem, proposal } from './testKit';

const ctx = (over: Partial<RuleFormContext> = {}): RuleFormContext => ({ ruleSetName: 'Acme guidelines', busy: false, ...over });

/** The values of an untouched form for a rule (what the component starts from), with overrides. */
function values(p: ProposalView, over: Partial<RuleFormValues> = {}): RuleFormValues {
  const def = ruleTypeDef(p.ruleType);
  return {
    params: def ? toParamValues(def, p.ruleBody) : {},
    consequence: p.consequence ?? '',
    headline: p.shortHeadline ?? '',
    template: p.messageTemplate ?? '',
    quote: p.clauseQuote ?? '',
    location: p.clauseLocation ?? '',
    firmText: p.firmDescription ?? '',
    ...over,
  };
}

const form = (p: ProposalView, over: Partial<RuleFormValues> = {}, c: RuleFormContext = ctx()) =>
  computeRuleForm(p, ruleTypeDef(p.ruleType), values(p, over), c);

describe('computeRuleForm: what Save sends', () => {
  it('must-not-fire: an untouched form sends nothing, is not dirty and cannot be saved', () => {
    const f = form(proposal({ firmDescription: 'Hours above the daily limit were billed.' }));

    expect(f.edit).toEqual({});
    expect(f.edit).not.toHaveProperty('firmDescription');
    expect(f.dirty).toBe(false);
    expect(f.saveDisabled).toBe(true);
  });

  it('an edited text is in the edit as typed, and Save is on', () => {
    const f = form(proposal({ firmDescription: 'Old wording' }), { firmText: 'New wording ' });

    expect(f.edit).toEqual({ firmDescription: 'New wording ' });
    expect(f.dirty).toBe(true);
    expect(f.saveDisabled).toBe(false);
  });

  it('a cleared field sends "" and is a change', () => {
    const f = form(proposal({ firmDescription: 'Old wording' }), { firmText: '' });

    expect(f.edit).toEqual({ firmDescription: '' });
    expect(f.dirty).toBe(true);
  });

  it('a text added to a rule with none is sent', () => {
    expect(form(proposal({ firmDescription: null }), { firmText: 'First wording' }).edit).toEqual({ firmDescription: 'First wording' });
  });

  it('the firm description joins the other members in one edit', () => {
    const f = form(proposal({ firmDescription: null }), { firmText: 'Wording', consequence: 'Reject' });

    expect(f.edit).toEqual({ consequence: 'Reject', firmDescription: 'Wording' });
  });

  it('must-not-fire: a change to another member leaves the firm description key out', () => {
    const f = form(proposal({ firmDescription: 'Old wording' }), { consequence: 'Reject' });

    expect(f.edit).toEqual({ consequence: 'Reject' });
    expect(f.edit).not.toHaveProperty('firmDescription');
  });

  describe('Save is off while a text that would be sent fails the checks', () => {
    it.each([
      ['braces', 'Too many {{hours}}', REASON_BRACES],
      ['markup', 'See <b>this</b>', REASON_MARKUP],
      ['over the limit', 'a'.repeat(201), 'firm_description_too_long'],
      ['the rule set name', 'Per the acme guidelines', 'firm_description_names_rule'],
      ['the rule name', 'Breaks the daily hours cap', 'firm_description_names_rule'],
    ])('%s', (_name, text, reason) => {
      const f = form(proposal({ firmDescription: null }), { firmText: text });

      expect(f.firm.blocked).toBe(true);
      expect(f.saveDisabled).toBe(true);
      expect(f.firm.messages).toHaveLength(1);
      expect(reason).toBeTruthy();
    });

    it('a failing text blocks Save even when another field is also changed', () => {
      const f = form(proposal({ firmDescription: null }), { firmText: '{x}', consequence: 'Reject' });

      expect(f.dirty).toBe(true);
      expect(f.saveDisabled).toBe(true);
    });

    it('must-not-fire: a clean text, a clear and a stored failing text left alone do not block', () => {
      expect(form(proposal({ firmDescription: null }), { firmText: 'Clean wording.' }).saveDisabled).toBe(false);
      expect(form(proposal({ firmDescription: '{old}' }), { firmText: '' }).saveDisabled).toBe(false);
      const untouched = form(proposal({ firmDescription: '{old}' }), { consequence: 'Reject' });
      expect(untouched.firm.blocked).toBe(false);
      expect(untouched.saveDisabled).toBe(false);
      expect(untouched.edit).not.toHaveProperty('firmDescription');
      expect(untouched.firm.messages).toHaveLength(1); // still flagged under the field
    });
  });

  it('Save is off while a call is in flight', () => {
    expect(form(proposal({ firmDescription: null }), { firmText: 'Wording' }, ctx({ busy: true })).saveDisabled).toBe(true);
  });

  it('the counter and the over-limit flag follow the text', () => {
    const f = form(proposal(), { firmText: '\u{1F600}'.repeat(201) });

    expect(f.firm.count).toBe(201);
    expect(f.firm.over).toBe(true);
    expect(form(proposal(), { firmText: '\u{1F600}'.repeat(200) }).firm.over).toBe(false);
  });

  it('the other fields still build the edit as before (a blanked headline is a change that cannot be sent)', () => {
    const f = form(proposal(), { headline: '' });

    expect(f.edit).toEqual({});
    expect(f.blanked.headline).toBeTruthy();
    expect(f.dirty).toBe(true);
  });

  it('the edit it builds is what saveEdit puts on the PATCH, and the reloaded new draft opens clean with the new text (published rule)', async () => {
    const published = proposal({ inForce: true, state: 'Accepted', versionId: 'v-1', versionNumber: 1, firmDescription: 'Old wording' });
    const f = form(published, { firmText: 'New wording' });
    const draft = proposal({ inForce: false, hasPublishedVersion: true, state: 'Proposed', versionId: 'v-2', versionNumber: 2, firmDescription: 'New wording' });
    const api = mockApi();
    api.edit.mockResolvedValue(ok({ proposal: { ...draft, rowVersion: null }, createdNewVersion: true }));
    api.get.mockResolvedValue(ok(detail([draft])));

    const outcome = await saveEdit(api, SET_ID, POLICY_ID, f.edit);

    expect(api.edit).toHaveBeenCalledWith(SET_ID, POLICY_ID, { firmDescription: 'New wording' });
    expect(outcome.kind).toBe('saved');
    const shown = outcome.kind === 'saved' ? outcome.detail?.proposals[0] : undefined;
    expect(shown?.versionId).toBe('v-2');
    const reopened = form(shown!);
    expect(initialFirmField(shown!.firmDescription).text).toBe('New wording');
    expect(reopened.dirty).toBe(false);
    expect(reopened.edit).toEqual({});
  });
});

describe('a Duplicate rule that holds a stored text (F3)', () => {
  const duplicate = (text: string | null, over: Partial<ProposalView> = {}) =>
    proposal({ ruleType: 'Duplicate', ruleBody: { type: 'Duplicate' }, firmDescription: text, ...over });

  it('is read-only, shows the stored text and offers Remove; nothing is sent until Remove is used', () => {
    const f = computeRuleForm(duplicate('Leftover'), undefined, { ...values(duplicate('Leftover')) }, ctx());

    expect(f.firm.isDuplicate).toBe(true);
    expect(f.firm.removable).toBe(true);
    expect(f.firm.removing).toBe(false);
    expect(f.edit).toEqual({});
    expect(f.saveDisabled).toBe(true);
    expect(f.firm.count).toBeNull();
    expect(f.firm.messages).toEqual([]);
  });

  it('Remove (an emptied text) sends "" and Save is on', () => {
    const f = computeRuleForm(duplicate('Leftover'), undefined, { ...values(duplicate('Leftover')), firmText: '' }, ctx());

    expect(f.edit).toEqual({ firmDescription: '' });
    expect(f.firm.removing).toBe(true);
    expect(f.dirty).toBe(true);
    expect(f.saveDisabled).toBe(false);
  });

  it('must-not-fire: a Duplicate rule with no text offers no Remove and sends nothing, whatever the field holds', () => {
    const none = computeRuleForm(duplicate(null), undefined, values(duplicate(null)), ctx());
    expect(none.firm.removable).toBe(false);
    expect(none.edit).toEqual({});

    const typed = computeRuleForm(duplicate(null), undefined, { ...values(duplicate(null)), firmText: 'typed' }, ctx());
    expect(typed.edit).not.toHaveProperty('firmDescription');
    expect(typed.firm.messages).toEqual([]);
  });

  it('must-not-fire: a text on a Duplicate can never be sent as text', () => {
    const f = computeRuleForm(duplicate('Leftover'), undefined, { ...values(duplicate('Leftover')), firmText: 'Another' }, ctx());

    expect(f.edit).not.toHaveProperty('firmDescription');
  });

  it('a different rule type keeps the ordinary behaviour', () => {
    const f = form(proposal({ ruleType: 'DailyHoursCap', firmDescription: 'Text' }), { firmText: '' });

    expect(f.firm.isDuplicate).toBe(false);
    expect(f.firm.removable).toBe(false);
    expect(f.edit).toEqual({ firmDescription: '' });
  });
});

describe('the stored text of a rule in force that fails the checks now (F6)', () => {
  const inForce = (text: string | null, over: Partial<ProposalView> = {}) => proposal({ inForce: true, state: 'Accepted', firmDescription: text, ...over });

  it.each([
    ['braces', '{old}'],
    ['the rule name', 'Mentions the Daily hours cap'],
    ['the rule set name', 'See Acme guidelines'],
    ['over the limit', 'a'.repeat(201)],
  ])('warns before the save: %s', (_n, text) => {
    expect(form(inForce(text)).firm.carryWarning).toBe(true);
  });

  it('warns for a Duplicate rule in force that still holds a text', () => {
    const d = inForce('Leftover', { ruleType: 'Duplicate', ruleBody: { type: 'Duplicate' } });
    expect(computeRuleForm(d, undefined, values(d), ctx()).firm.carryWarning).toBe(true);
  });

  it('must-not-fire: a clean text, no text, a draft, or a text the admin has changed do not warn', () => {
    expect(form(inForce('Clean wording.')).firm.carryWarning).toBe(false);
    expect(form(inForce(null)).firm.carryWarning).toBe(false);
    expect(form(proposal({ inForce: false, firmDescription: '{old}' })).firm.carryWarning).toBe(false);
    expect(form(inForce('{old}'), { firmText: 'Fixed wording.' }).firm.carryWarning).toBe(false);
    expect(form(inForce('{old}'), { firmText: '' }).firm.carryWarning).toBe(false);
  });
});

describe('the firm description field state (F4)', () => {
  const refusal = (reason: string): FirmRefusal => ({ reason, message: `m ${reason}` });
  const arrived = [refusal(REASON_BRACES)];

  it('a refusal that arrives is shown', () => {
    expect(shownRefusals(arrived, initialFirmField('Old'))).toEqual(arrived);
  });

  it('typing dismisses it', () => {
    const state = firmFieldReducer(initialFirmField('Old'), { type: 'edit', text: 'New', refusals: arrived });

    expect(state.text).toBe('New');
    expect(shownRefusals(arrived, state)).toEqual([]);
  });

  it('Discard restores the stored text and dismisses it', () => {
    const typed = firmFieldReducer(initialFirmField('Old'), { type: 'edit', text: '{bad}', refusals: undefined });
    const state = firmFieldReducer(typed, { type: 'reset', stored: 'Old', refusals: arrived });

    expect(state.text).toBe('Old');
    expect(shownRefusals(arrived, state)).toEqual([]);
  });

  it('Discard with no stored text empties the field', () => {
    expect(firmFieldReducer(initialFirmField(null), { type: 'reset', stored: null, refusals: undefined }).text).toBe('');
  });

  it('must-fire again: a NEW refusal after the dismissal is shown', () => {
    const state = firmFieldReducer(initialFirmField('Old'), { type: 'reset', stored: 'Old', refusals: arrived });
    const next = [refusal(REASON_MARKUP)];

    expect(shownRefusals(next, state)).toEqual(next);
  });

  it('must-not-fire: with no refusals nothing is shown', () => {
    expect(shownRefusals(undefined, initialFirmField('Old'))).toEqual([]);
  });

  it('a shown refusal puts its message under the field once, with the client message', () => {
    const f = form(proposal({ firmDescription: null }), { firmText: 'See <b>' }, ctx({ shownRefusals: [refusal(REASON_MARKUP), refusal(REASON_BRACES)] }));

    expect(f.firm.messages).toEqual([
      'The firm description cannot contain < or >.',
      'm firm_description_markup',
      'm firm_description_braces',
    ]);
  });
});

describe('which refusals the page keeps when another rule is selected (F4)', () => {
  const failure = (errors: Record<string, unknown>[], code = 'ruleset.invalid') => toFailure(problem(422, code, { errors }));
  const entry = { policyId: POLICY_ID, rule: 'R', field: 'firmDescription', message: 'x', reason: REASON_BRACES };

  it("a save's refusal goes", () => {
    const state = { from: 'edit' as const, byRule: firmDescriptionRefusals(failure([entry])) };

    expect(refusalsAfterSelect(state)).toBe(NO_REFUSALS);
  });

  it('must-not-fire: an accept or publish refusal about the stored text stays', () => {
    const state = { from: 'other' as const, byRule: firmDescriptionRefusals(failure([entry], 'ruleset.publish_invalid')) };

    expect(refusalsAfterSelect(state)).toBe(state);
    expect(refusalsAfterSelect(state).byRule[POLICY_ID]).toHaveLength(1);
  });

  it('nothing held stays nothing', () => {
    expect(refusalsAfterSelect(NO_REFUSALS)).toBe(NO_REFUSALS);
  });
});
