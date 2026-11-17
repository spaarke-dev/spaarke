/**
 * ebilling-r1 task 130e-2 (DEF-060): the firm description's client checks, what the save path sends, and the reading of a server refusal.
 * The values these mirror are pinned to the C# source in `firmDescriptionPin.test.ts`.
 */

import {
  FIRM_DESCRIPTION_MESSAGES,
  MAX_FIRM_DESCRIPTION,
  REASON_BRACES,
  REASON_DUPLICATE,
  REASON_INVISIBLE,
  REASON_MARKUP,
  REASON_NAMES_RULE,
  REASON_TOO_LONG,
  checkFirmDescription,
  type FirmDescriptionContext,
  firmDescriptionRefusals,
  firmDescriptionToSend,
} from '../services/firmDescription';
import { diffProposals, saveEdit } from '../services/reviewActions';
import { createRuleSetApi, toFailure } from '../services/ruleSetApi';
import { POLICY_ID, SET_ID, detail, mockApi, ok, problem, proposal } from './testKit';

const SET_NAME = 'Acme guidelines';
const RULE_NAME = 'Daily hours cap';
const ctx: FirmDescriptionContext = { ruleSetName: SET_NAME, ruleName: RULE_NAME };
const reasonOf = (text: string, c: FirmDescriptionContext = ctx) => checkFirmDescription(text, c).reason;

describe('firmDescriptionToSend: only a change is sent', () => {
  it('an edit sends the text as typed (the server trims it)', () => {
    expect(firmDescriptionToSend('Old wording', 'New wording ')).toBe('New wording ');
    expect(firmDescriptionToSend(null, 'First wording')).toBe('First wording');
  });

  it('a cleared field over a stored text sends "" (not null)', () => {
    expect(firmDescriptionToSend('Old wording', '')).toBe('');
    expect(firmDescriptionToSend('Old wording', '   ')).toBe('');
  });

  it('must-not-fire: an untouched field leaves the key out, stored text or none', () => {
    expect(firmDescriptionToSend('Old wording', 'Old wording')).toBeUndefined();
    expect(firmDescriptionToSend(null, '')).toBeUndefined();
    expect(firmDescriptionToSend(undefined, '')).toBeUndefined();
    expect(firmDescriptionToSend('Old wording', '  Old wording  ')).toBeUndefined();
    expect(firmDescriptionToSend(null, '   ')).toBeUndefined();
  });

  it('must-not-fire: a Duplicate rule never sends one', () => {
    expect(firmDescriptionToSend(null, 'anything', true)).toBeUndefined();
    expect(firmDescriptionToSend('stored', '', true)).toBeUndefined();
  });

  it('the member is on the PATCH body only when set (JSON has no undefined, never a null)', async () => {
    const calls: string[] = [];
    const fetchFn = jest.fn(async (_url: string, init?: RequestInit) => {
      calls.push(String(init?.body));
      return { json: async () => ({}) } as never;
    });
    const api = createRuleSetApi(fetchFn as never, 'https://bff.example.test');

    await api.edit(SET_ID, POLICY_ID, { consequence: 'Flag' });
    await api.edit(SET_ID, POLICY_ID, { firmDescription: '' });
    await api.edit(SET_ID, POLICY_ID, { firmDescription: 'Text' });

    expect(calls).toEqual(['{"consequence":"Flag"}', '{"firmDescription":""}', '{"firmDescription":"Text"}']);
    expect(calls[0]).not.toContain('firmDescription');
  });
});

describe('checkFirmDescription: the server checks, early', () => {
  it('a clean text passes, and a blank one is "no description"', () => {
    expect(checkFirmDescription('Hours above the daily limit were billed.', ctx)).toMatchObject({ reason: null, count: 40 });
    expect(checkFirmDescription('   ', ctx)).toEqual({ text: null, count: 0, reason: null });
    expect(checkFirmDescription('', ctx)).toEqual({ text: null, count: 0, reason: null });
  });

  describe('length is counted in Unicode scalars, not UTF-16 units', () => {
    it('200 plain characters pass; 201 are refused', () => {
      expect(reasonOf('a'.repeat(200))).toBeNull();
      expect(reasonOf('a'.repeat(201))).toBe(REASON_TOO_LONG);
    });

    it('200 astral characters (400 UTF-16 units) pass; 201 are refused', () => {
      const astral = '\u{1F600}';
      expect(astral.length).toBe(2);
      expect(checkFirmDescription(astral.repeat(200), ctx)).toMatchObject({ reason: null, count: 200 });
      expect(checkFirmDescription(astral.repeat(201), ctx)).toMatchObject({ reason: REASON_TOO_LONG, count: 201 });
    });

    it('the limit is the server\'s 200, counted after trimming', () => {
      expect(MAX_FIRM_DESCRIPTION).toBe(200);
      expect(reasonOf(`  ${'a'.repeat(200)}  `)).toBeNull();
    });
  });

  it.each([['{', 'a { b'], ['}', 'a } b'], ['a placeholder', 'Too many {{hours}}']])('braces are refused: %s', (_n, text) => {
    expect(reasonOf(text)).toBe(REASON_BRACES);
  });

  it.each([['<', 'a < b'], ['>', 'a > b'], ['a tag', 'See <b>this</b>']])('markup is refused: %s', (_n, text) => {
    expect(reasonOf(text)).toBe(REASON_MARKUP);
  });

  it.each([
    ['a line break', 'one\ntwo'],
    ['a tab', 'one\ttwo'],
    ['a zero-width space (Format)', 'one​two'],
    ['a right-to-left override (Format)', 'one‮two'],
    ['a soft hyphen (Format)', 'one­two'],
    ['a line separator', 'one two'],
    ['a paragraph separator', 'one two'],
    ['a TAG character', 'one\u{E0041}two'],
    ['a lone high surrogate', `one${String.fromCharCode(0xd83d)}two`],
    ['a lone low surrogate', `one${String.fromCharCode(0xde00)}two`],
    ['a swapped pair', `one${String.fromCharCode(0xde00, 0xd83d)}two`],
  ])('an invisible character is refused: %s', (_n, text) => {
    expect(reasonOf(text)).toBe(REASON_INVISIBLE);
  });

  it('must-not-fire: a paired surrogate, accents and an ordinary space are fine', () => {
    expect(reasonOf('Smile \u{1F600} and café hours')).toBeNull();
    expect(reasonOf('One no-break space is white space, not invisible')).toBeNull();
  });

  it('a Duplicate rule takes none', () => {
    expect(reasonOf('Anything', { ...ctx, isDuplicate: true })).toBe(REASON_DUPLICATE);
    expect(checkFirmDescription('', { ...ctx, isDuplicate: true }).reason).toBeNull();
  });

  describe('the rule set\'s or the rule\'s name (NFKC, white space folded, case-insensitive)', () => {
    it('the rule name, any case', () => {
      expect(reasonOf('Breaks the DAILY HOURS CAP.')).toBe(REASON_NAMES_RULE);
    });

    it('the rule set name', () => {
      expect(reasonOf('Per the acme guidelines, no.')).toBe(REASON_NAMES_RULE);
    });

    it('fullwidth letters fold to the plain name (NFKC)', () => {
      expect(reasonOf('See Ｄａｉｌｙ hours cap here')).toBe(REASON_NAMES_RULE);
    });

    it.each([
      ['a doubled space', 'Daily  hours cap'],
      ['a no-break space', 'Daily hours cap'],
      ['an ideographic space', 'Daily　hours cap'],
      ['an en space and a tab-free run', 'Daily  hours cap'],
    ])('white space is folded: %s', (_n, text) => {
      expect(reasonOf(`Rule: ${text} was broken`)).toBe(REASON_NAMES_RULE);
    });

    it('a name written with odd white space is folded too', () => {
      expect(reasonOf('Acme guidelines apply', { ruleSetName: 'Acme  guidelines', ruleName: RULE_NAME })).toBe(REASON_NAMES_RULE);
    });

    it('must-fire on the rule name alone when the rule set name is absent, and on the set name alone', () => {
      expect(reasonOf('daily hours cap', { ruleName: RULE_NAME })).toBe(REASON_NAMES_RULE);
      expect(reasonOf('acme guidelines', { ruleSetName: SET_NAME })).toBe(REASON_NAMES_RULE);
    });

    it('must-not-fire: a clean text, a partial word, a blank or missing name', () => {
      expect(reasonOf('Hours above the daily limit were billed.')).toBeNull();
      expect(reasonOf('Daily hours were too many')).toBeNull();
      expect(reasonOf('Any text at all', { ruleSetName: '   ', ruleName: null })).toBeNull();
      expect(reasonOf('Any text at all', {})).toBeNull();
    });

    it('a look-alike letter from another alphabet is NOT caught (known limit, said in the notice)', () => {
      expect(reasonOf('Daily hours cаp')).toBeNull(); // Cyrillic a
    });
  });

  it('reports the first failure in the server\'s order (invisible before length before braces before markup before names)', () => {
    expect(reasonOf(`${'{'.repeat(201)}`)).toBe(REASON_TOO_LONG);
    expect(reasonOf('{<')).toBe(REASON_BRACES);
    expect(reasonOf('< daily hours cap')).toBe(REASON_MARKUP);
    expect(reasonOf(`${'a'.repeat(200)}\n`)).toBeNull(); // a trailing line break is trimmed first, so 200 + break still fits
    expect(reasonOf(`{${'a'.repeat(300)}​`)).toBe(REASON_INVISIBLE);
  });

  it('the text is trimmed the way the server trims it (U+FEFF is not white space there)', () => {
    expect(checkFirmDescription(' 　 text  ', ctx).text).toBe('text');
    expect(reasonOf('text﻿')).toBe(REASON_INVISIBLE);
  });
});

describe('a 422 firm description refusal', () => {
  const failure = (errors: Record<string, unknown>[], code = 'ruleset.invalid', status = 422) =>
    toFailure(problem(status, code, { errors }));
  const entry = (reason: string | null, extra: Record<string, unknown> = {}) => ({
    policyId: POLICY_ID, rule: RULE_NAME, field: 'firmDescription', message: `server says ${reason}`, reason, ...extra,
  });

  it.each(Object.keys(FIRM_DESCRIPTION_MESSAGES))('%s gives its own message', reason => {
    const out = firmDescriptionRefusals(failure([entry(reason)]));
    expect(out[POLICY_ID]).toEqual([{ reason, message: FIRM_DESCRIPTION_MESSAGES[reason] }]);
  });

  it('there are six reason codes, each with a different message', () => {
    expect(Object.keys(FIRM_DESCRIPTION_MESSAGES).sort()).toEqual(
      [REASON_BRACES, REASON_DUPLICATE, REASON_INVISIBLE, REASON_MARKUP, REASON_NAMES_RULE, REASON_TOO_LONG].sort()
    );
    expect(new Set(Object.values(FIRM_DESCRIPTION_MESSAGES)).size).toBe(6);
  });

  it('a publish refusal names its rules; one message per reason code per rule', () => {
    const other = '33333333-3333-3333-3333-333333333333';
    const out = firmDescriptionRefusals(
      failure([entry(REASON_BRACES), entry(REASON_BRACES), entry(REASON_TOO_LONG), entry(REASON_MARKUP, { policyId: other })], 'ruleset.publish_invalid')
    );
    expect(out[POLICY_ID].map(r => r.reason)).toEqual([REASON_BRACES, REASON_TOO_LONG]);
    expect(out[other].map(r => r.reason)).toEqual([REASON_MARKUP]);
  });

  it('an entry with no policy id belongs to the rule the call was about', () => {
    expect(firmDescriptionRefusals(failure([entry(REASON_BRACES, { policyId: null })]), POLICY_ID)[POLICY_ID]).toHaveLength(1);
    expect(firmDescriptionRefusals(failure([entry(REASON_BRACES, { policyId: null })]))).toEqual({});
  });

  it('an unknown reason shows the server\'s own message', () => {
    expect(firmDescriptionRefusals(failure([entry('firm_description_new_thing')]))[POLICY_ID][0].message).toBe('server says firm_description_new_thing');
  });

  it('must-not-fire: other fields, a 400, another error code, or no errors give nothing', () => {
    expect(firmDescriptionRefusals(failure([entry(REASON_BRACES, { field: 'shortHeadline' })]))).toEqual({});
    expect(firmDescriptionRefusals(failure([entry(REASON_BRACES)], 'ruleset.invalid', 400))).toEqual({});
    expect(firmDescriptionRefusals(failure([entry(REASON_BRACES)], 'ruleset.concurrent_edit'))).toEqual({});
    expect(firmDescriptionRefusals(failure([]))).toEqual({});
  });

  it('saveEdit hands the 422 back as a failure and reloads nothing', async () => {
    const api = mockApi();
    api.edit.mockResolvedValue({ ok: false, failure: failure([entry(REASON_NAMES_RULE)]) });

    const outcome = await saveEdit(api, SET_ID, POLICY_ID, { firmDescription: 'Daily hours cap' });

    expect(outcome.kind).toBe('failed');
    expect(api.get).not.toHaveBeenCalled();
    if (outcome.kind === 'failed') expect(firmDescriptionRefusals(outcome.failure)[POLICY_ID][0].reason).toBe(REASON_NAMES_RULE);
  });
});

describe('after a save on a published rule', () => {
  it('the edit carries the member and the reload shows the new draft with the new text', async () => {
    const api = mockApi();
    const published = proposal({ inForce: true, state: 'Accepted', versionId: 'v-1', versionNumber: 1, firmDescription: 'Old wording' });
    const draft = proposal({ inForce: false, hasPublishedVersion: true, state: 'Proposed', versionId: 'v-2', versionNumber: 2, firmDescription: 'New wording' });
    api.edit.mockResolvedValue(ok({ proposal: { ...draft, rowVersion: null }, createdNewVersion: true }));
    api.get.mockResolvedValue(ok(detail([draft])));

    const outcome = await saveEdit(api, SET_ID, POLICY_ID, { firmDescription: 'New wording' });

    expect(api.edit).toHaveBeenCalledWith(SET_ID, POLICY_ID, { firmDescription: 'New wording' });
    expect(outcome.kind).toBe('saved');
    if (outcome.kind === 'saved') {
      expect(outcome.createdNewVersion).toBe(true);
      expect(outcome.detail?.proposals[0]).toMatchObject({ versionId: 'v-2', firmDescription: 'New wording' });
      expect(diffProposals(published, outcome.detail!.proposals[0]).map(l => l.field)).toContain('Firm description');
    }
  });

  it('a stale reload reports a changed firm description', () => {
    const lines = diffProposals(proposal({ firmDescription: 'a' }), proposal({ firmDescription: 'b' }));
    expect(lines).toEqual([{ field: 'Firm description', before: 'a', after: 'b' }]);
    expect(diffProposals(proposal({ firmDescription: null }), proposal({ firmDescription: null }))).toEqual([]);
  });
});
