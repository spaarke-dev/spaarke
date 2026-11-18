/**
 * ebilling-r1 task 130e-2: the firm description values the page holds, pinned to the BFF's C# source (the style of RecordProcedures'
 * serverWordsPin.test.ts). Nothing at run time ties the six reason codes, the 200 limit, the request member or the 422 error codes to the
 * server, so a server rename would otherwise silently drop a field message. Each test fails if a pinned value changes on either side.
 */

import * as fs from 'fs';
import * as path from 'path';
import {
  DEFAULT_IGNORABLE_RANGES,
  FIRM_DESCRIPTION_FIELD,
  FIRM_DESCRIPTION_MESSAGES,
  MAX_FIRM_DESCRIPTION,
  REASON_BRACES,
  REASON_DUPLICATE,
  REASON_INVISIBLE,
  REASON_MARKUP,
  REASON_NAMES_RULE,
  REASON_TOO_LONG,
  REFUSAL_ERROR_CODES,
} from '../services/firmDescription';

const REPO = path.resolve(__dirname, '../../../../..');
const BFF = 'src/server/api/Sprk.Bff.Api';
const read = (rel: string) => fs.readFileSync(path.join(REPO, rel), 'utf8');

const RULES = read(`${BFF}/Services/Finance/FirmDescriptionRules.cs`);
const SERVICE = read(`${BFF}/Services/Finance/RuleSetService.cs`);
const ENDPOINTS = read(`${BFF}/Api/Finance/RuleSetEndpoints.cs`);

/** Every `const string Name = "value";` (any modifiers) in a source text, as a name-to-value map. */
function stringConsts(source: string): Record<string, string> {
  return Object.fromEntries([...source.matchAll(/\bconst\s+string\s+(\w+)\s*=\s*"([^"\\]*)"\s*;/g)].map(m => [m[1], m[2]]));
}

const camel = (pascal: string) => pascal.charAt(0).toLowerCase() + pascal.slice(1);

describe('the six reason codes', () => {
  const rules = () => stringConsts(RULES);

  it('(control) the scan finds the server\'s codes in FirmDescriptionRules.cs, and only the firm_description_* ones', () => {
    const codes = Object.entries(rules()).filter(([, v]) => v.startsWith('firm_description_'));
    expect(codes.map(([k]) => k).sort()).toEqual(['Braces', 'InvisibleCharacter', 'Markup', 'NamesTheRule', 'OnDuplicate', 'TooLong']);
  });

  it('each client code is the server\'s constant of that name', () => {
    const r = rules();
    expect(REASON_TOO_LONG).toBe(r.TooLong);
    expect(REASON_BRACES).toBe(r.Braces);
    expect(REASON_MARKUP).toBe(r.Markup);
    expect(REASON_INVISIBLE).toBe(r.InvisibleCharacter);
    expect(REASON_NAMES_RULE).toBe(r.NamesTheRule);
    expect(REASON_DUPLICATE).toBe(r.OnDuplicate);
  });

  it('the client has a message for exactly the server\'s codes', () => {
    const server = Object.values(stringConsts(RULES)).filter(v => v.startsWith('firm_description_'));
    expect(Object.keys(FIRM_DESCRIPTION_MESSAGES).sort()).toEqual([...server].sort());
  });

  it('the server answers each code with a message of its own (RuleSetService.FirmDescriptionMessage has a case per code)', () => {
    const body = /static string FirmDescriptionMessage\(string code\) => code switch\s*\{([\s\S]*?)\n\s*\};/.exec(SERVICE)?.[1] ?? '';
    for (const name of ['FirmDescriptionOnDuplicateReason', 'FirmDescriptionRules.TooLong', 'FirmDescriptionRules.Braces', 'FirmDescriptionRules.Markup',
      'FirmDescriptionRules.InvisibleCharacter', 'FirmDescriptionRules.NamesTheRule']) {
      expect(body).toContain(`${name} =>`);
    }
    expect(SERVICE).toMatch(/FirmDescriptionOnDuplicateReason\s*=\s*FirmDescriptionRules\.OnDuplicate\s*;/);
  });
});

describe('the length limit', () => {
  it('is the server\'s MaxRunes (200), counted in runes (Unicode scalars)', () => {
    expect(/public const int MaxRunes\s*=\s*(\d+)\s*;/.exec(RULES)?.[1]).toBe('200');
    expect(MAX_FIRM_DESCRIPTION).toBe(200);
    expect(RULES).toMatch(/foreach \(var rune in text\.EnumerateRunes\(\)\)/);
  });
});

describe('the request member and the refusal field', () => {
  it('the PATCH body member is the camel-case of the C# record member FirmDescription, and the 422 field is the same word', () => {
    expect(ENDPOINTS).toMatch(/string\?\s+FirmDescription\s*=\s*null\s*\)\s*;/);
    expect(FIRM_DESCRIPTION_FIELD).toBe(camel('FirmDescription'));
    expect(stringConsts(SERVICE).FirmDescriptionField).toBe(FIRM_DESCRIPTION_FIELD);
  });

  it('the GET view member is ProposalView.FirmDescription (serialised as proposals[].firmDescription)', () => {
    expect(SERVICE).toMatch(/record ProposalView\([\s\S]*?string\?\s+FirmDescription\s*=\s*null\s*\)\s*;/);
  });
});

describe('the 422', () => {
  it('a refusal is a 422 when any error is on the firmDescription field', () => {
    expect(SERVICE).toMatch(/errors\.Any\(e => e\.Field == RuleSetService\.FirmDescriptionField\)\s*\?\s*StatusCodes\.Status422UnprocessableEntity/);
  });

  it('the error codes are the edit\'s and the publish\'s', () => {
    const c = stringConsts(SERVICE);
    expect([...REFUSAL_ERROR_CODES].sort()).toEqual([c.CodeInvalid, c.CodePublishInvalid].sort());
    expect(c.CodeInvalid).toBe('ruleset.invalid');
    expect(c.CodePublishInvalid).toBe('ruleset.publish_invalid');
  });

  it('an error entry carries policyId, rule, field, message and reason, in that JSON shape', () => {
    expect(SERVICE).toMatch(/record RuleSetError\(Guid\? PolicyId, string\? RuleName, string Field, string Message, string\? Reason\)/);
    expect(ENDPOINTS).toContain('new { policyId = e.PolicyId, rule = e.RuleName, field = e.Field, message = e.Message, reason = e.Reason }');
  });
});

describe('the default-ignorable table (fix round 2 F1)', () => {
  /** The `(0x00AD, 0x00AD)` tuples of the server's `DefaultIgnorableRanges`. */
  const serverRanges = (): Array<[number, number]> => {
    const body = /DefaultIgnorableRanges\s*=\s*\[([\s\S]*?)\];/.exec(RULES)?.[1] ?? '';
    return [...body.matchAll(/\(\s*0x([0-9A-Fa-f]+)\s*,\s*0x([0-9A-Fa-f]+)\s*\)/g)].map(m => [parseInt(m[1], 16), parseInt(m[2], 16)]);
  };

  /** The `[0x00ad, 0x00ad]` pairs of the client's `DEFAULT_IGNORABLE_RANGES`, read from the TypeScript source (not the imported value). */
  const clientRanges = (): Array<[number, number]> => {
    const source = fs.readFileSync(path.join(__dirname, '../services/firmDescription.ts'), 'utf8');
    const body = /DEFAULT_IGNORABLE_RANGES[^=]*=\s*\[([\s\S]*?)\n\];/.exec(source)?.[1] ?? '';
    return [...body.matchAll(/\[\s*0x([0-9A-Fa-f]+)\s*,\s*0x([0-9A-Fa-f]+)\s*\]/g)].map(m => [parseInt(m[1], 16), parseInt(m[2], 16)]);
  };

  it('(control) the scan finds the 17 ranges of Unicode 16.0 on the server, from U+00AD to U+E0FFF', () => {
    const ranges = serverRanges();
    expect(ranges).toHaveLength(17);
    expect(ranges[0]).toEqual([0x00ad, 0x00ad]);
    expect(ranges[ranges.length - 1]).toEqual([0xe0000, 0xe0fff]);
  });

  it('the client table is the server table, range for range', () => {
    expect(clientRanges()).toEqual(serverRanges());
  });

  it('the imported client value is the table in its source', () => {
    expect(DEFAULT_IGNORABLE_RANGES.map(r => [...r])).toEqual(clientRanges());
  });

  it('a range is sorted and apart from the next (so a lookup cannot depend on order)', () => {
    const ranges = serverRanges();
    ranges.forEach(([first, last], i) => {
      expect(first).toBeLessThanOrEqual(last);
      if (i > 0) expect(first).toBeGreaterThan(ranges[i - 1][1] + 1);
    });
  });

  it('the server runs the check beside the Control and Format test, with the invisible code', () => {
    expect(RULES).toMatch(/if \(IsInvisibleControl\(rune\) \|\| IsDefaultIgnorable\(rune\)\)\s*\{\s*return Refused\(InvisibleCharacter\);/);
  });
});

describe('the order of the checks the client mirrors', () => {
  it('the server runs invisible, length, braces, markup, names, in that order', () => {
    const validate = /public static FirmDescriptionCheck Validate\(string\? raw, string\? ruleSetName, string\? policyName\)\s*\{([\s\S]*?)\n    \}/.exec(RULES)?.[1] ?? '';
    const at = (needle: string) => validate.indexOf(needle);
    const order = ['Refused(InvisibleCharacter)', 'Refused(TooLong)', 'Refused(Braces)', 'Refused(Markup)', 'Refused(NamesTheRule)'].map(at);
    expect(order.every(i => i >= 0)).toBe(true);
    expect([...order].sort((a, b) => a - b)).toEqual(order);
  });
});
