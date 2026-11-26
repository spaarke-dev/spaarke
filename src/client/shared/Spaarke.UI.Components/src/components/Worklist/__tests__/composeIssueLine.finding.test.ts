/**
 * composeIssueLine `finding` (owner decision D-227): what a row and the wizard's first step lead with.
 *  - precedence: the rule's short name, else the Signal's title, else the subject's name, else the placeholder;
 *    a whitespace-only value counts as absent at every step
 *  - negative: `headline` keeps its meaning (subject name, else title, else the placeholder) for the same input, and the
 *    finding is never the rule's sentence
 */

import { composeIssueLine } from '..';
import type { WorklistItem } from '..';

const TODAY = new Date(2026, 9, 9, 12);
const base: WorklistItem = { signalId: 'x', lane: 'Decide' };
const line = (extra: Partial<WorklistItem>) => composeIssueLine({ ...base, ...extra }, TODAY);

describe('composeIssueLine finding', () => {
  it('is the rule short name when there is one, whatever else is set', () => {
    const v = line({
      ruleShortName: 'Matter has no active budget',
      title: 'Stored title',
      subjectName: 'Acme v. Beta',
    });
    expect(v.finding).toBe('Matter has no active budget');
  });

  it('is the stored title when the rule has no short name', () => {
    expect(line({ ruleShortName: null, title: 'Stored title', subjectName: 'Acme v. Beta' }).finding).toBe(
      'Stored title'
    );
  });

  it('is the subject name when there is neither a rule short name nor a title', () => {
    expect(line({ subjectName: 'Acme v. Beta' }).finding).toBe('Acme v. Beta');
  });

  it('is the placeholder when nothing is set', () => {
    expect(line({}).finding).toBe('Untitled item');
  });

  it('treats whitespace-only values as absent at every step, and trims the one it uses', () => {
    expect(line({ ruleShortName: '   ', title: '  ', subjectName: '\t' }).finding).toBe('Untitled item');
    expect(line({ ruleShortName: '  ', title: '  Stored title  ', subjectName: 'Acme v. Beta' }).finding).toBe(
      'Stored title'
    );
    expect(line({ ruleShortName: '  No budget  ' }).finding).toBe('No budget');
    expect(line({ ruleShortName: ' ', title: ' ', subjectName: ' Acme v. Beta ' }).finding).toBe('Acme v. Beta');
  });

  it('never uses the rule sentence', () => {
    const v = line({ sentence: 'A long claim about the matter', subjectName: 'Acme v. Beta' } as Partial<WorklistItem>);
    expect(v.finding).toBe('Acme v. Beta');
  });

  it('negative: headline is unchanged for the same input (subject name, else title, else the placeholder)', () => {
    const rule = 'Matter has no active budget';
    expect(line({ ruleShortName: rule, title: 'Stored title', subjectName: 'Acme v. Beta' }).headline).toBe(
      'Acme v. Beta'
    );
    expect(line({ ruleShortName: rule, title: 'Stored title' }).headline).toBe('Stored title');
    expect(line({ ruleShortName: rule, subjectName: '  ', title: ' ' }).headline).toBe('Untitled item');
    expect(line({ ruleShortName: rule, title: 'T', subjectName: ' S ' }).headline).toBe('S');
  });
});
