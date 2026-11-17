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
  comparable,
  DEFAULT_IGNORABLE_RANGES,
  isDefaultIgnorable,
  type FirmDescriptionContext,
  firmDescriptionRefusals,
  firmDescriptionToSend,
} from '../services/firmDescription';
import { diffProposals, saveEdit } from '../services/reviewActions';
import { createRuleSetApi, toFailure } from '../services/ruleSetApi';
import { POLICY_ID, SET_ID, mockApi, problem, proposal } from './testKit';

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

  it('must-not-fire: a Duplicate rule never sends a text', () => {
    expect(firmDescriptionToSend(null, 'anything', true)).toBeUndefined();
    expect(firmDescriptionToSend('stored', 'anything', true)).toBeUndefined();
    expect(firmDescriptionToSend('stored', 'stored', true)).toBeUndefined();
    expect(firmDescriptionToSend(null, '', true)).toBeUndefined();
  });

  it('a Duplicate rule that holds a text sends "" only when it has been removed (F3)', () => {
    expect(firmDescriptionToSend('stored', '', true)).toBe('');
    expect(firmDescriptionToSend('stored', '   ', true)).toBe('');
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
    ['a zero-width space (Format)', 'one\u200Btwo'],
    ['a right-to-left override (Format)', 'one\u202Etwo'],
    ['a soft hyphen (Format)', 'one\u00ADtwo'],
    ['a line separator', 'one\u2028two'],
    ['a paragraph separator', 'one\u2029two'],
    ['a TAG character', 'one\u{E0041}two'],
    ['a lone high surrogate', `one${String.fromCharCode(0xd83d)}two`],
    ['a lone low surrogate', `one${String.fromCharCode(0xde00)}two`],
    ['a swapped pair', `one${String.fromCharCode(0xde00, 0xd83d)}two`],
  ])('an invisible character is refused: %s', (_n, text) => {
    expect(reasonOf(text)).toBe(REASON_INVISIBLE);
  });

  describe('default-ignorable code points that are not Control or Format (fix round 2 F1)', () => {
    const cp = (n: number) => String.fromCodePoint(n);

    it.each([
      ['the combining grapheme joiner', 0x034f],
      ['a Mongolian free variation selector', 0x180b],
      ['variation selector 1', 0xfe00],
      ['variation selector 16', 0xfe0f],
      ['variation selector 17 (astral)', 0xe0100],
      ['variation selector 256 (astral)', 0xe01ef],
      ['the Hangul filler', 0x3164],
      ['the halfwidth Hangul filler', 0xffa0],
      ['the Hangul choseong filler', 0x115f],
      ['the Khmer inherent AQ', 0x17b4],
      ['an unassigned code point inside U+2060 to U+206F', 0x2065],
      ['the last of U+E0000 to U+E0FFF', 0xe0fff],
    ])('must-fire: %s is refused as invisible', (_n, code) => {
      expect(isDefaultIgnorable(code)).toBe(true);
      expect(reasonOf(`Rate${cp(code)} too high.`)).toBe(REASON_INVISIBLE);
    });

    it('must-fire: a mark inside a rule name no longer gets the name past the check (it is refused as invisible)', () => {
      for (const code of [0x034f, 0xfe0f, 0xe0100, 0x180b]) {
        const text = `Billed under the YIL${cp(code)}LIK SINIR.`;
        expect(reasonOf(text, { ruleName: 'YILLIK SINIR' })).toBe(REASON_INVISIBLE);
        expect(reasonOf(text, { ruleSetName: 'YILLIK SINIR' })).toBe(REASON_INVISIBLE);
      }
    });

    it.each([
      ['precomposed e acute', 'caf\u00E9 hours'],
      ['decomposed e + combining acute (not default-ignorable)', 'cafe\u0301 hours'],
      ['the registered sign, next to U+00AD', 'Registered \u00AE rates'],
      ['either side of U+034F', 'Mark \u034E \u0350'],
      ['after U+FE00 to U+FE0F', 'Presentation \uFE10 \uFE20'],
      ['after U+2060 to U+206F', 'Superscript \u2070 \u2071'],
      ['the Mongolian digit zero, after U+180B to U+180F', 'Digit \u1810'],
    ])('must-not-fire: %s', (_n, text) => {
      expect(reasonOf(text)).toBeNull();
    });

    it('a decomposed accent is composed by NFKC, so a decomposed name is still caught by the name check', () => {
      expect(reasonOf('Per the cafe\u0301 rule.', { ruleName: 'CAF\u00C9' })).toBe(REASON_NAMES_RULE);
      expect(reasonOf('Per the caf\u00E9 rule.', { ruleName: 'CAFE\u0301' })).toBe(REASON_NAMES_RULE);
    });

    it('the range ends and their neighbours', () => {
      for (const [first, last] of DEFAULT_IGNORABLE_RANGES) {
        expect([first, isDefaultIgnorable(first)]).toEqual([first, true]);
        expect([last, isDefaultIgnorable(last)]).toEqual([last, true]);
        expect([first - 1, isDefaultIgnorable(first - 1)]).toEqual([first - 1, false]);
        expect([last + 1, isDefaultIgnorable(last + 1)]).toEqual([last + 1, false]);
      }
    });
  });

  it('must-not-fire: a paired surrogate, accents and an ordinary space are fine', () => {
    expect(reasonOf('Smile \u{1F600} and caf\u00E9 hours')).toBeNull();
    expect(reasonOf('One no-break\u00A0space is white space, not invisible')).toBeNull();
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
      expect(reasonOf('See \uFF24\uFF41\uFF49\uFF4C\uFF59 hours cap here')).toBe(REASON_NAMES_RULE);
    });

    it.each([
      ['a doubled space', 'Daily  hours cap'],
      ['a no-break space', 'Daily\u00A0hours\u00A0cap'],
      ['an ideographic space', 'Daily\u3000hours cap'],
      ['an en space and a tab-free run', 'Daily\u2002\u2003hours cap'],
    ])('white space is folded: %s', (_n, text) => {
      expect(reasonOf(`Rule: ${text} was broken`)).toBe(REASON_NAMES_RULE);
    });

    it('a name written with odd white space is folded too', () => {
      expect(reasonOf('Acme guidelines apply', { ruleSetName: 'Acme\u00A0 guidelines', ruleName: RULE_NAME })).toBe(REASON_NAMES_RULE);
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

    describe('the Turkish dotless i and dotted I read as "i" on both sides (fix round 1 F1, as the server does)', () => {
      const dotless = 'y\u0131ll\u0131k s\u0131n\u0131r';
      const dotted = 'Y\u0130LL\u0130K S\u0130N\u0130R';

      it.each([
        ['a dotless text against a plain upper-case name', `Billed under the ${dotless}.`, 'YILLIK SINIR'],
        ['a plain upper-case text against a dotless name', 'Billed under the YILLIK SINIR.', dotless],
        ['a dotted text against a plain lower-case name', `Billed under the ${dotted}.`, 'yillik sinir'],
        ['a plain lower-case text against a dotted name', 'Billed under the yillik sinir.', dotted],
        ['a dotless text against a dotted name', `Billed under the ${dotless}.`, dotted],
        ['a dotted text against a dotless name', `Billed under the ${dotted}.`, dotless],
      ])('must-fire: %s', (_n, text, name) => {
        expect(reasonOf(text, { ruleName: name })).toBe(REASON_NAMES_RULE);
        expect(reasonOf(text, { ruleSetName: name })).toBe(REASON_NAMES_RULE);
      });

      it('"Y\u0131ll\u0131k s\u0131n\u0131r" against "yillik sinir" is a match, on both sides', () => {
        expect(comparable('Y\u0131ll\u0131k s\u0131n\u0131r')).toBe(comparable('yillik sinir'));
        expect(reasonOf('Y\u0131ll\u0131k s\u0131n\u0131r', { ruleName: 'yillik sinir' })).toBe(REASON_NAMES_RULE);
      });

      it('must-not-fire: unrelated text holding a dotless or dotted i', () => {
        expect(reasonOf('Ak\u0131ll\u0131 hizmet bedeli', { ruleName: 'YILLIK SINIR', ruleSetName: 'Acme guidelines' })).toBeNull();
        expect(reasonOf('\u0130stanbul office rates apply', { ruleName: 'YILLIK SINIR' })).toBeNull();
        expect(reasonOf('Billed under the y\u0131ll\u0131k tarife', { ruleName: 'YILLIK SINIR' })).toBeNull();
      });
    });

    describe('JavaScript upper-casing against .NET OrdinalIgnoreCase on common scripts (fix round 1 F2 re-check)', () => {
      // Pairs the two engines agree on: a name and the same name in the other case must match, and a different letter must not.
      it.each([
        ['Latin with accents', 'Caf\u00E9 Rules', 'CAF\u00C9 RULES'],
        ['Greek', '\u03B1\u03B2\u03B3 \u03BA\u03B1\u03BD\u03CC\u03BD\u03B1\u03C2', '\u0391\u0392\u0393 \u039A\u0391\u039D\u038C\u039D\u0391\u03A3'],
        ['Cyrillic', '\u043F\u0440\u0430\u0432\u0438\u043B\u043E', '\u041F\u0420\u0410\u0412\u0418\u041B\u041E'],
        ['German sharp s', 'Gro\u00DFe Regel', 'Gro\u00DFe Regel'],
      ])('%s matches in either case', (_n, name, text) => {
        expect(reasonOf(`Per ${text}.`, { ruleName: name })).toBe(REASON_NAMES_RULE);
        expect(reasonOf(`Per ${name}.`, { ruleName: text })).toBe(REASON_NAMES_RULE);
      });

      it('the sharp s does not match "SS" (.NET keeps it too), and a final sigma matches a plain sigma (both engines upper-case both to the same letter)', () => {
        expect(reasonOf('Per GROSSE REGEL.', { ruleName: 'Gro\u00DFe Regel' })).toBeNull();
        expect(reasonOf('Per \u03BA\u03B1\u03BD\u03CC\u03BD\u03B1\u03C2', { ruleName: '\u03BA\u03B1\u03BD\u03CC\u03BD\u03B1\u03C3' })).toBe(REASON_NAMES_RULE);
      });
    });

    it('a look-alike letter from another alphabet is NOT caught (known limit, said in the notice)', () => {
      expect(reasonOf('Daily hours c\u0430p')).toBeNull(); // Cyrillic a
    });
  });

  it('reports the first failure in the server\'s order (invisible before length before braces before markup before names)', () => {
    expect(reasonOf(`${'{'.repeat(201)}`)).toBe(REASON_TOO_LONG);
    expect(reasonOf('{<')).toBe(REASON_BRACES);
    expect(reasonOf('< daily hours cap')).toBe(REASON_MARKUP);
    expect(reasonOf(`${'a'.repeat(200)}\n`)).toBeNull(); // a trailing line break is trimmed first, so 200 + break still fits
    expect(reasonOf(`{${'a'.repeat(300)}\u200B`)).toBe(REASON_INVISIBLE);
  });

  it('the text is trimmed the way the server trims it (U+FEFF is not white space there)', () => {
    expect(checkFirmDescription('\u00A0\u3000 text \u2003', ctx).text).toBe('text');
    expect(reasonOf('text\uFEFF')).toBe(REASON_INVISIBLE);
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

// The save path itself (the form's edit, the PATCH, the reload that shows the new draft) is tested in `ruleForm.test.ts`.
describe('the reload after a 409', () => {
  it('reports a changed firm description', () => {
    const lines = diffProposals(proposal({ firmDescription: 'a' }), proposal({ firmDescription: 'b' }));
    expect(lines).toEqual([{ field: 'Firm description', before: 'a', after: 'b' }]);
    expect(diffProposals(proposal({ firmDescription: null }), proposal({ firmDescription: null }))).toEqual([]);
  });
});
