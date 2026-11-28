/**
 * The Rule Review launch envelope (ebilling-r1 DEF-069, #3112): the rule set the page opens on, read through `parseDataParams` (the same
 * decoder the page's `main.tsx` uses) from the URL, the Xrm data envelope, or an `id` + `typename` pair. Anything else opens the list.
 */

// The shared decoder at its source: the library barrel is not built for this harness (assignmentPanelFirmPicker.test.ts mocks it instead).
import { parseDataParams } from '../../../../client/shared/Spaarke.UI.Components/src/utils/parseDataParams';
import { parseRuleSetLaunch } from '../services/launch';

const SET = '3f2a9c1e-5b7d-4e08-9a41-c0d2e6f81b37';

const launch = (search: string) => parseRuleSetLaunch(parseDataParams(search));

describe('parseRuleSetLaunch - opens the rule set', () => {
  it('from ?ruleSetId=', () => {
    expect(launch(`?ruleSetId=${SET}`)).toBe(SET);
  });

  it('from the Xrm data envelope (data=ruleSetId%3D...)', () => {
    expect(launch(`?data=${encodeURIComponent(`ruleSetId=${SET}`)}`)).toBe(SET);
  });

  it('from id + typename=sprk_ruleset', () => {
    expect(launch(`?id=${SET}&typename=sprk_ruleset`)).toBe(SET);
  });

  it('strips braces and lower-cases the id', () => {
    expect(launch(`?ruleSetId=%7B${SET.toUpperCase()}%7D`)).toBe(SET);
  });

  it('accepts the ruleSetid spelling', () => {
    expect(launch(`?ruleSetid=${SET}`)).toBe(SET);
  });
});

describe('parseRuleSetLaunch - opens the list (no error)', () => {
  it('for a typename other than sprk_ruleset', () => {
    expect(launch(`?id=${SET}&typename=sprk_invoice`)).toBeUndefined();
  });

  it('for a malformed id', () => {
    expect(launch('?ruleSetId=not-a-guid')).toBeUndefined();
    expect(launch(`?ruleSetId=${SET.slice(0, -1)}`)).toBeUndefined();
  });

  it('for no id', () => {
    expect(launch('')).toBeUndefined();
    expect(launch('?typename=sprk_ruleset')).toBeUndefined();
    expect(launch('?ruleSetId=')).toBeUndefined();
  });
});
