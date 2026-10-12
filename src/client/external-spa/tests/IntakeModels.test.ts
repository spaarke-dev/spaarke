/**
 * R3 task 052 (FR-05) - the pure half of the intake wizards: validation, the answers JSON and the titles.
 *
 * Nothing here renders. "Today" is always a parameter, so the date rules are pinned (2026-10-12 is a Monday).
 */
import { describe, expect, it } from 'vitest';
import {
  addDaysIso,
  businessDaysUntil,
  compact,
  dateError,
  emailError,
  textError,
  todayIso,
} from '../src/intake/intakeValidation';
import {
  NDA_LIMITS,
  buildNdaAnswers,
  emptyNdaForm,
  ndaReviewRows,
  ndaTitle,
  showsNeededByReason,
  validateNda,
  type NdaForm,
} from '../src/intake/ndaModel';
import {
  buildPolicyAnswers,
  emptyPolicyForm,
  policyTitle,
  validatePolicy,
  type PolicyForm,
} from '../src/intake/policyModel';
import { getPolicyTopics, policyTopicLabel } from '../src/intake/policyTopics';
import { MAX_INTAKE_TITLE_LENGTH, INTAKE_REQUEST_TYPE } from '../src/api/intake-client';

const TODAY = '2026-10-12'; // a Monday

const nda = (over: Partial<NdaForm> = {}): NdaForm => ({
  ...emptyNdaForm(),
  ndaAction: 'review',
  counterpartyName: 'Contoso Ltd',
  counterpartyType: 'vendor',
  purpose: 'Evaluate their analytics platform for the Q3 pilot',
  disclosureDirection: 'mutual',
  ...over,
});

const issue = (over: Partial<NdaForm> = {}): NdaForm =>
  nda({
    ndaAction: 'issue',
    counterpartySignerName: 'Pat Doe',
    counterpartySignerEmail: 'pat@contoso.example',
    term: 'twoYears',
    governingLaw: 'Delaware, USA',
    neededBy: '2026-10-30',
    ...over,
  });

const policy = (over: Partial<PolicyForm> = {}): PolicyForm => ({
  ...emptyPolicyForm(),
  topic: 'gifts',
  question: 'Can I accept a conference ticket worth about $400 from a supplier?',
  situation: 'planning',
  urgency: 'routine',
  ...over,
});

describe('date helpers', () => {
  it('todayIso is the LOCAL calendar day, zero padded', () => {
    expect(todayIso(new Date(2026, 0, 5, 23, 59))).toBe('2026-01-05');
  });

  it('addDaysIso crosses month and year ends', () => {
    expect(addDaysIso('2026-12-31', 1)).toBe('2027-01-01');
    expect(addDaysIso(TODAY, 365)).toBe('2027-10-12');
  });

  it('businessDaysUntil counts weekdays after today up to the date', () => {
    expect(businessDaysUntil(TODAY, TODAY)).toBe(0);
    expect(businessDaysUntil('2026-10-15', TODAY)).toBe(3); // Tue, Wed, Thu
    expect(businessDaysUntil('2026-10-16', TODAY)).toBe(4);
    expect(businessDaysUntil('2026-10-12', '2026-10-16')).toBe(0); // past
    expect(businessDaysUntil('2026-10-19', '2026-10-16')).toBe(1); // Fri -> Mon skips the weekend
  });

  it('dateError: required, malformed, impossible and out-of-range dates', () => {
    const max = addDaysIso(TODAY, 365);
    expect(dateError('', true, TODAY, max, 'x')).toBe('Choose a date.');
    expect(dateError('', false, TODAY, max, 'x')).toBeNull();
    expect(dateError('12/10/2026', false, TODAY, max, 'x')).toBe('Enter a valid date.');
    expect(dateError('2026-02-30', false, TODAY, max, 'x')).toBe('Enter a valid date.');
    expect(dateError('2026-10-11', false, TODAY, max, 'in range')).toBe('Choose a date in range.');
    expect(dateError(addDaysIso(max, 1), false, TODAY, max, 'in range')).toBe('Choose a date in range.');
    expect(dateError(TODAY, false, TODAY, max, 'x')).toBeNull();
    expect(dateError(max, false, TODAY, max, 'x')).toBeNull();
  });
});

describe('text, email and compact helpers', () => {
  it('textError counts the trimmed value', () => {
    expect(textError('   ', 'the name', 2, 10)).toBe('Enter the name.');
    expect(textError('a', 'the name', 2, 10)).toBe('The name must be at least 2 characters.');
    expect(textError(' ab ', 'the name', 2, 10)).toBeNull();
    expect(textError('x'.repeat(11), 'the name', 2, 10)).toBe('The name must be at most 10 characters.');
    expect(textError('', 'the name', 0, 10)).toBeNull();
  });

  it('emailError needs an address of the right shape and at most 254 characters', () => {
    expect(emailError('', 'the signer')).toBe('Enter the signer.');
    expect(emailError('pat@contoso', 'the signer')).toBe('Enter a valid email address for the signer.');
    expect(emailError('pat contoso@x.com', 'the signer')).toBe('Enter a valid email address for the signer.');
    expect(emailError('pat@contoso.example', 'the signer')).toBeNull();
    expect(emailError(`${'a'.repeat(250)}@x.com`, 'the signer')).toBe('Enter a valid email address for the signer.');
  });

  it('compact drops undefined, null, blank strings and empty arrays, and trims strings', () => {
    expect(
      compact({ a: ' x ', b: '', c: '   ', d: undefined, e: null, f: [], g: ['k'], h: 0, i: false, schemaVersion: 1 })
    ).toEqual({ a: 'x', g: ['k'], h: 0, i: false, schemaVersion: 1 });
  });
});

describe('NDA Assessment model', () => {
  it('a complete review form is valid; an empty form fails on every required field', () => {
    expect(validateNda(nda(), TODAY)).toEqual({});
    const errors = validateNda(emptyNdaForm(), TODAY);
    expect(Object.keys(errors).sort()).toEqual(
      ['counterpartyName', 'counterpartyType', 'disclosureDirection', 'ndaAction', 'purpose'].sort()
    );
  });

  it('the issue path also needs signer name, signer email (format), term and governing law, and neededBy', () => {
    expect(validateNda(issue(), TODAY)).toEqual({});
    const errors = validateNda(nda({ ndaAction: 'issue' }), TODAY);
    expect(Object.keys(errors).sort()).toEqual(
      ['counterpartySignerEmail', 'counterpartySignerName', 'governingLaw', 'neededBy', 'term'].sort()
    );
    expect(validateNda(issue({ counterpartySignerEmail: 'not-an-email' }), TODAY).counterpartySignerEmail).toMatch(
      /valid email/
    );
    expect(validateNda(issue({ neededBy: '' }), TODAY).neededBy).toBe('Choose a date.');
  });

  it('neededBy is optional on the review path but bounded: today to a year ahead', () => {
    expect(validateNda(nda({ neededBy: '' }), TODAY)).toEqual({});
    expect(validateNda(nda({ neededBy: '2026-10-11' }), TODAY).neededBy).toMatch(/Choose a date/);
    expect(validateNda(nda({ neededBy: '2027-10-13' }), TODAY).neededBy).toMatch(/Choose a date/);
    expect(validateNda(nda({ neededBy: '2027-10-12' }), TODAY)).toEqual({});
  });

  it('"other" relationship needs a description; any other relationship ignores a stale one', () => {
    expect(validateNda(nda({ counterpartyType: 'other' }), TODAY).counterpartyTypeOther).toBe(
      'Enter the relationship.'
    );
    expect(validateNda(nda({ counterpartyType: 'vendor', counterpartyTypeOther: 'x' }), TODAY)).toEqual({});
  });

  it('the length caps are enforced on the trimmed value', () => {
    expect(
      validateNda(nda({ counterpartyName: 'x'.repeat(NDA_LIMITS.counterpartyName.max + 1) }), TODAY).counterpartyName
    ).toMatch(/at most 200/);
    expect(validateNda(nda({ purpose: 'short' }), TODAY).purpose).toMatch(/at least 10/);
    expect(validateNda(nda({ purpose: 'x'.repeat(1001) }), TODAY).purpose).toMatch(/at most 1000/);
    expect(validateNda(nda({ notes: 'x'.repeat(2001) }), TODAY).notes).toMatch(/at most 2000/);
  });

  it('the reason field is shown only for a date within 3 business days', () => {
    expect(showsNeededByReason(nda({ neededBy: '2026-10-14' }), TODAY)).toBe(true);
    expect(showsNeededByReason(nda({ neededBy: '2026-10-16' }), TODAY)).toBe(false);
    expect(showsNeededByReason(nda({ neededBy: '' }), TODAY)).toBe(false);
    expect(showsNeededByReason(nda({ neededBy: '2026-10-01' }), TODAY)).toBe(false); // past: invalid, no reason field
  });

  it('answers: schemaVersion 1, codes not labels, no key for an empty or hidden field (review path)', () => {
    expect(buildNdaAnswers(nda({ counterpartyCountry: '  ', notes: '' }), TODAY)).toEqual({
      schemaVersion: 1,
      ndaAction: 'review',
      counterpartyName: 'Contoso Ltd',
      counterpartyType: 'vendor',
      purpose: 'Evaluate their analytics platform for the Q3 pilot',
      disclosureDirection: 'mutual',
    });
  });

  it('answers: the issue path adds the signer, term and governing law; a review form drops stale issue answers', () => {
    expect(buildNdaAnswers(issue({ sensitiveInfo: ['personalData', 'financial'] }), TODAY)).toEqual({
      schemaVersion: 1,
      ndaAction: 'issue',
      counterpartyName: 'Contoso Ltd',
      counterpartyType: 'vendor',
      purpose: 'Evaluate their analytics platform for the Q3 pilot',
      disclosureDirection: 'mutual',
      sensitiveInfo: ['personalData', 'financial'],
      counterpartySignerName: 'Pat Doe',
      counterpartySignerEmail: 'pat@contoso.example',
      term: 'twoYears',
      governingLaw: 'Delaware, USA',
      neededBy: '2026-10-30',
    });
    const stale = buildNdaAnswers(issue({ ndaAction: 'review' }), TODAY);
    for (const key of ['counterpartySignerName', 'counterpartySignerEmail', 'term', 'governingLaw']) {
      expect(stale).not.toHaveProperty(key);
    }
  });

  it('answers: neededByReason is kept only while its field is shown; typeOther only for "other"', () => {
    const near = buildNdaAnswers(nda({ neededBy: '2026-10-14', neededByReason: 'Kick-off on the 14th' }), TODAY);
    expect(near.neededByReason).toBe('Kick-off on the 14th');
    const far = buildNdaAnswers(nda({ neededBy: '2026-10-30', neededByReason: 'stale' }), TODAY);
    expect(far).not.toHaveProperty('neededByReason');
    expect(buildNdaAnswers(nda({ counterpartyTypeOther: 'stale' }), TODAY)).not.toHaveProperty('counterpartyTypeOther');
    expect(
      buildNdaAnswers(nda({ counterpartyType: 'other', counterpartyTypeOther: 'Joint venture' }), TODAY)
    ).toMatchObject({ counterpartyType: 'other', counterpartyTypeOther: 'Joint venture' });
  });

  it('answers never carry a department or requester (the server stamps them)', () => {
    const keys = Object.keys(buildNdaAnswers(issue({ notes: 'n', counterpartyCountry: 'US' }), TODAY));
    expect(keys.some(k => /department|requester/i.test(k))).toBe(false);
  });

  it('title is "NDA – {counterpartyName}" only, never the purpose or notes, capped at 850', () => {
    expect(ndaTitle(nda({ notes: 'secret deal', counterpartyName: '  Contoso Ltd ' }))).toBe('NDA – Contoso Ltd');
    expect(ndaTitle(nda({ counterpartyName: 'x'.repeat(2000) })).length).toBe(MAX_INTAKE_TITLE_LENGTH);
  });

  it('the review rows show labels, and only the issue rows on the issue path', () => {
    const review = ndaReviewRows(nda(), TODAY);
    expect(review.find(r => r.label === 'Relationship')?.value).toBe('Vendor or supplier');
    expect(review.some(r => r.label === 'Signer email')).toBe(false);
    expect(ndaReviewRows(issue(), TODAY).find(r => r.label === 'Term')?.value).toBe('2 years');
  });
});

describe('Policy Question model', () => {
  it('a complete routine form is valid; an empty form fails on the required fields', () => {
    expect(validatePolicy(policy(), TODAY)).toEqual({});
    expect(Object.keys(validatePolicy(emptyPolicyForm(), TODAY)).sort()).toEqual(
      ['question', 'situation', 'topic', 'urgency'].sort()
    );
  });

  it('"other" needs a topic text; soon/urgent need neededBy within 90 days; routine does not ask', () => {
    expect(validatePolicy(policy({ topic: 'other' }), TODAY).topicOther).toBe('Enter the topic.');
    expect(validatePolicy(policy({ urgency: 'soon' }), TODAY).neededBy).toBe('Choose a date.');
    expect(validatePolicy(policy({ urgency: 'urgent', neededBy: '2027-01-10' }), TODAY)).toEqual({}); // today + 90 days
    expect(validatePolicy(policy({ urgency: 'urgent', neededBy: '2027-01-11' }), TODAY).neededBy).toMatch(
      /Choose a date/
    );
    expect(validatePolicy(policy({ urgency: 'routine', neededBy: '' }), TODAY)).toEqual({});
  });

  it('question length: 20 to 1000; background up to 4000', () => {
    expect(validatePolicy(policy({ question: 'too short' }), TODAY).question).toMatch(/at least 20/);
    expect(validatePolicy(policy({ question: 'x'.repeat(1001) }), TODAY).question).toMatch(/at most 1000/);
    expect(validatePolicy(policy({ background: 'x'.repeat(4001) }), TODAY).background).toMatch(/at most 4000/);
  });

  it('answers: codes, schemaVersion 1, no incident or department keys, neededBy only when asked', () => {
    expect(buildPolicyAnswers(policy({ background: 'Supplier is bidding', neededBy: '2026-10-20' }))).toEqual({
      schemaVersion: 1,
      topic: 'gifts',
      question: 'Can I accept a conference ticket worth about $400 from a supplier?',
      background: 'Supplier is bidding',
      situation: 'planning',
      urgency: 'routine',
    });
    const soon = buildPolicyAnswers(policy({ urgency: 'soon', neededBy: '2026-10-20' }));
    expect(soon.neededBy).toBe('2026-10-20');
    const keys = Object.keys(soon);
    expect(keys.some(k => /department|requester|incident/i.test(k))).toBe(false);
  });

  it('title is the topic label only - never the question (or background)', () => {
    const title = policyTitle(policy({ background: 'very private background' }));
    expect(title).toBe('Policy question – Gifts, entertainment and hospitality');
    expect(title).not.toContain('conference');
    expect(title).not.toContain('private');
  });

  it('title for "other" uses the typed topic, cut to 120 characters', () => {
    expect(policyTitle(policy({ topic: 'other', topicOther: 'Lobbying' }))).toBe('Policy question – Lobbying');
    expect(policyTitle(policy({ topic: 'other', topicOther: 'y'.repeat(200) }))).toBe(
      `Policy question – ${'y'.repeat(120)}`
    );
  });
});

describe('policy topics (one module, stable codes)', () => {
  it('the fixed list carries the design note codes, "other" last, no duplicates', () => {
    const codes = getPolicyTopics().map(t => t.code);
    expect(codes).toEqual([
      'ethics',
      'gifts',
      'antiBribery',
      'conflicts',
      'privacy',
      'infoSecurity',
      'communications',
      'signingAuthority',
      'employment',
      'competition',
      'tradeCompliance',
      'records',
      'other',
    ]);
    expect(new Set(codes).size).toBe(codes.length);
    expect(policyTopicLabel('antiBribery')).toBe('Anti-bribery and corruption');
    expect(policyTopicLabel('nope')).toBeUndefined();
  });
});

describe('request type codes', () => {
  it('match ExternalDataService.ServiceRequestTypes', () => {
    expect(INTAKE_REQUEST_TYPE).toEqual({
      ndaAssessment: 100000000,
      inventionSubmission: 100000001,
      policyQuestion: 100000002,
      trademarkSearch: 100000003,
    });
  });
});
