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
import {
  buildInventionAnswers,
  emptyInventionForm,
  inventionReviewRows,
  inventionTitle,
  showsDisclosureWarning,
  stepHasError,
  validateInvention,
  type InventionForm,
} from '../src/intake/inventionModel';
import {
  buildTrademarkAnswers,
  emptyTrademarkForm,
  neededByMax,
  trademarkReviewRows,
  trademarkTitle,
  validateTrademark,
  type TrademarkForm,
} from '../src/intake/trademarkModel';
import {
  getTrademarkTerritories,
  trademarkTerritoryLabel,
  TRADEMARK_TERRITORY_OTHER,
} from '../src/intake/trademarkTerritories';
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

const invention = (over: Partial<InventionForm> = {}): InventionForm => ({
  ...emptyInventionForm(),
  shortTitle: 'Improved battery cooling method',
  problemSolved: 'Batteries overheat during fast charging in compact devices.',
  description: 'A phase-change layer routes heat to the chassis so charging can stay at full rate without throttling.',
  submitterIsInventor: 'yes',
  publicDisclosure: 'none',
  sharedExternally: 'no',
  productUse: 'no',
  externalInvolvement: 'no',
  accuracyConfirmed: true,
  ...over,
});

describe('Invention Submission model', () => {
  it('a complete minimal form is valid; an empty form fails on every required field', () => {
    expect(validateInvention(invention(), TODAY)).toEqual({});
    expect(Object.keys(validateInvention(emptyInventionForm(), TODAY)).sort()).toEqual(
      [
        'accuracyConfirmed',
        'description',
        'externalInvolvement',
        'problemSolved',
        'productUse',
        'publicDisclosure',
        'shortTitle',
        'sharedExternally',
        'submitterIsInventor',
      ].sort()
    );
  });

  it('length rules: title 5-150, problem 20-2000, description 50-8000, alternatives <= 3000, notes <= 2000', () => {
    expect(validateInvention(invention({ shortTitle: 'abcd' }), TODAY).shortTitle).toMatch(/at least 5/);
    expect(validateInvention(invention({ shortTitle: 'x'.repeat(151) }), TODAY).shortTitle).toMatch(/at most 150/);
    expect(validateInvention(invention({ problemSolved: 'x'.repeat(19) }), TODAY).problemSolved).toMatch(/at least 20/);
    expect(validateInvention(invention({ description: 'x'.repeat(49) }), TODAY).description).toMatch(/at least 50/);
    expect(validateInvention(invention({ description: 'x'.repeat(8001) }), TODAY).description).toMatch(/at most 8000/);
    expect(validateInvention(invention({ knownAlternatives: 'x'.repeat(3001) }), TODAY).knownAlternatives).toMatch(
      /at most 3000/
    );
    expect(validateInvention(invention({ notes: 'x'.repeat(2001) }), TODAY).notes).toMatch(/at most 2000/);
  });

  it('"not an inventor" needs at least one listed inventor; rows validate name, optional email and organization; max 20', () => {
    expect(validateInvention(invention({ submitterIsInventor: 'no' }), TODAY).coInventors).toBe(
      'Add at least one inventor.'
    );
    const row = { name: 'Pat Doe', email: '', organization: '' };
    expect(validateInvention(invention({ submitterIsInventor: 'no', coInventors: [row] }), TODAY)).toEqual({});
    expect(validateInvention(invention({ coInventors: [{ ...row, name: ' ' }] }), TODAY)['coInventors.0.name']).toBe(
      'Enter the inventor’s name.'
    );
    expect(
      validateInvention(invention({ coInventors: [{ ...row, email: 'nope' }] }), TODAY)['coInventors.0.email']
    ).toMatch(/valid email/);
    expect(
      validateInvention(invention({ coInventors: [{ ...row, organization: 'x'.repeat(201) }] }), TODAY)[
        'coInventors.0.organization'
      ]
    ).toMatch(/at most 200/);
    expect(
      validateInvention(invention({ coInventors: Array.from({ length: 21 }, () => row) }), TODAY).coInventors
    ).toBe('Add at most 20 inventors.');
  });

  it('stepHasError matches a step field and the keys nested under it', () => {
    expect(stepHasError({ 'coInventors.1.name': 'x' }, ['coInventors'])).toBe(true);
    expect(stepHasError({ coInventors: 'x' }, ['coInventors'])).toBe(true);
    expect(stepHasError({ coInventorsX: 'x' }, ['coInventors'])).toBe(false);
    expect(stepHasError({ notes: 'x' }, ['coInventors'])).toBe(false);
  });

  it('conception date may not be in the future', () => {
    expect(validateInvention(invention({ conceptionDate: '2026-10-13' }), TODAY).conceptionDate).toMatch(
      /on or before today/
    );
    expect(validateInvention(invention({ conceptionDate: TODAY }), TODAY)).toEqual({});
  });

  it('disclosure: "already" needs a date <= today, "planned" a date >= today; both need a method', () => {
    const done = invention({ publicDisclosure: 'done' });
    expect(Object.keys(validateInvention(done, TODAY)).sort()).toEqual(['disclosureDate', 'disclosureMethod']);
    expect(
      validateInvention({ ...done, disclosureDate: '2026-10-13', disclosureMethod: 'demo' }, TODAY).disclosureDate
    ).toMatch(/on or before today/);
    expect(validateInvention({ ...done, disclosureDate: '2026-09-01', disclosureMethod: 'demo' }, TODAY)).toEqual({});

    const planned = invention({ publicDisclosure: 'planned', disclosureMethod: 'publication' });
    expect(validateInvention({ ...planned, disclosureDate: '2026-10-11' }, TODAY).disclosureDate).toMatch(/from today/);
    expect(validateInvention({ ...planned, disclosureDate: TODAY }, TODAY)).toEqual({});
    expect(validateInvention({ ...planned, disclosureDate: '2026-12-01' }, TODAY)).toEqual({});
    expect(
      validateInvention({ ...planned, disclosureDate: '2026-12-01', disclosureMethod: 'bogus' }, TODAY).disclosureMethod
    ).toMatch(/from the list/);
  });

  it('disclosure warning: shown for "already"; for "planned" only with a valid date within 60 days', () => {
    expect(showsDisclosureWarning(invention({ publicDisclosure: 'none' }), TODAY)).toBe(false);
    expect(showsDisclosureWarning(invention({ publicDisclosure: 'done' }), TODAY)).toBe(true);
    const planned = (disclosureDate: string): InventionForm =>
      invention({ publicDisclosure: 'planned', disclosureDate });
    expect(showsDisclosureWarning(planned(''), TODAY)).toBe(false);
    expect(showsDisclosureWarning(planned('2026-12-11'), TODAY)).toBe(true); // today + 60
    expect(showsDisclosureWarning(planned('2026-12-12'), TODAY)).toBe(false);
    expect(showsDisclosureWarning(planned('2026-10-01'), TODAY)).toBe(false); // past: invalid
  });

  it('follow-ups are required only when their trigger is yes / planned / released', () => {
    expect(validateInvention(invention({ sharedExternally: 'yes' }), TODAY).sharedWith).toBe(
      'Enter who it was shared with.'
    );
    expect(validateInvention(invention({ sharedExternally: 'unsure' }), TODAY)).toEqual({});
    expect(validateInvention(invention({ productUse: 'planned' }), TODAY).productName).toBe(
      'Enter the product or service name.'
    );
    expect(validateInvention(invention({ productUse: 'released', productName: 'Widget' }), TODAY)).toEqual({});
    expect(validateInvention(invention({ externalInvolvement: 'yes' }), TODAY).externalInvolvementDetails).toBe(
      'Enter who was involved.'
    );
  });

  it('the accuracy confirmation must be true', () => {
    expect(validateInvention(invention({ accuracyConfirmed: false }), TODAY).accuracyConfirmed).toBe(
      'Confirm the statement to submit.'
    );
  });

  it('answers: schemaVersion 1, codes and booleans, no key for an empty or hidden field (shortest path)', () => {
    expect(buildInventionAnswers(invention())).toEqual({
      schemaVersion: 1,
      shortTitle: 'Improved battery cooling method',
      problemSolved: 'Batteries overheat during fast charging in compact devices.',
      description:
        'A phase-change layer routes heat to the chassis so charging can stay at full rate without throttling.',
      submitterIsInventor: true,
      publicDisclosure: 'none',
      sharedExternally: 'no',
      productUse: 'no',
      externalInvolvement: 'no',
      accuracyConfirmed: true,
    });
  });

  it('answers: a stale answer behind a closed branch is dropped; a false boolean is kept', () => {
    const stale = buildInventionAnswers(
      invention({
        submitterIsInventor: 'no',
        coInventors: [{ name: ' Pat Doe ', email: ' ', organization: 'Contoso' }],
        publicDisclosure: 'none',
        disclosureDate: '2026-09-01',
        disclosureMethod: 'demo',
        disclosureVenue: 'x',
        sharedExternally: 'unsure',
        sharedWith: 'stale',
        productUse: 'no',
        productName: 'stale',
        productDate: '2026-12-01',
        externalInvolvement: 'no',
        externalInvolvementDetails: 'stale',
      })
    );
    expect(stale.submitterIsInventor).toBe(false);
    expect(stale.coInventors).toEqual([{ name: 'Pat Doe', organization: 'Contoso' }]);
    for (const key of [
      'disclosureDate',
      'disclosureMethod',
      'disclosureVenue',
      'sharedWith',
      'productName',
      'productDate',
      'externalInvolvementDetails',
    ]) {
      expect(stale).not.toHaveProperty(key);
    }
    expect(Object.keys(stale).some(k => /department|requester/i.test(k))).toBe(false);
  });

  it('title is "Invention – {shortTitle}" only - never the problem, description or alternatives - capped at 850', () => {
    expect(inventionTitle(invention({ shortTitle: '  Improved battery cooling method ' }))).toBe(
      'Invention – Improved battery cooling method'
    );
    expect(inventionTitle(invention())).not.toMatch(/phase-change|overheat/);
    expect(inventionTitle(invention({ shortTitle: 'x'.repeat(2000) })).length).toBe(MAX_INTAKE_TITLE_LENGTH);
  });

  it('the review rows show labels, and only the rows of open branches', () => {
    const rows = inventionReviewRows(
      invention({ publicDisclosure: 'planned', disclosureDate: '2026-12-01', disclosureMethod: 'demo' })
    );
    expect(rows.find(r => r.label === 'Shared publicly')?.value).toBe('Not yet, but planned');
    expect(rows.find(r => r.label === 'Planned date')?.value).toBe('2026-12-01');
    expect(rows.find(r => r.label === 'How')?.value).toBe('Demo to a customer or third party');
    expect(inventionReviewRows(invention()).some(r => r.label === 'Product or service')).toBe(false);
  });
});

const trademark = (over: Partial<TrademarkForm> = {}): TrademarkForm => ({
  ...emptyTrademarkForm(),
  proposedMark: 'AURORA',
  markForm: 'word',
  goodsServices: 'A cloud analytics product for retail teams.',
  territories: ['us'],
  currentUse: 'notYet',
  ...over,
});

describe('Trademark Search model', () => {
  it('a complete minimal form is valid; an empty form fails on every required field', () => {
    expect(validateTrademark(trademark(), TODAY)).toEqual({});
    expect(Object.keys(validateTrademark(emptyTrademarkForm(), TODAY)).sort()).toEqual(
      ['currentUse', 'goodsServices', 'markForm', 'proposedMark', 'territories'].sort()
    );
  });

  it('length rules: mark 1-200, meaning <= 500, goods 10-2000, notes <= 2000', () => {
    expect(validateTrademark(trademark({ proposedMark: 'x'.repeat(201) }), TODAY).proposedMark).toMatch(/at most 200/);
    expect(validateTrademark(trademark({ markMeaning: 'x'.repeat(501) }), TODAY).markMeaning).toMatch(/at most 500/);
    expect(validateTrademark(trademark({ goodsServices: 'short' }), TODAY).goodsServices).toMatch(/at least 10/);
    expect(validateTrademark(trademark({ goodsServices: 'x'.repeat(2001) }), TODAY).goodsServices).toMatch(
      /at most 2000/
    );
    expect(validateTrademark(trademark({ notes: 'x'.repeat(2001) }), TODAY).notes).toMatch(/at most 2000/);
  });

  it('territories: at least one, only listed codes; "other" needs text (2-300) and a stale text is ignored otherwise', () => {
    expect(validateTrademark(trademark({ territories: [] }), TODAY).territories).toBe('Choose at least one place.');
    expect(validateTrademark(trademark({ territories: ['us', 'mars'] }), TODAY).territories).toBe(
      'Choose from the list.'
    );
    expect(validateTrademark(trademark({ territories: ['us', 'other'] }), TODAY).territoriesOther).toBe(
      'Enter the other countries.'
    );
    expect(validateTrademark(trademark({ territories: ['other'], territoriesOther: 'Brazil' }), TODAY)).toEqual({});
    expect(validateTrademark(trademark({ territories: ['us'], territoriesOther: 'x' }), TODAY)).toEqual({});
  });

  it('the optional "what will it name" must be a listed code when given', () => {
    expect(validateTrademark(trademark({ markRole: 'product' }), TODAY)).toEqual({});
    expect(validateTrademark(trademark({ markRole: 'bogus' }), TODAY).markRole).toMatch(/from the list/);
  });

  it('first-use date is <= today and asked only for public use; launch date is today..+10 years', () => {
    expect(
      validateTrademark(trademark({ currentUse: 'public', firstUseDate: '2026-10-13' }), TODAY).firstUseDate
    ).toMatch(/on or before today/);
    expect(validateTrademark(trademark({ currentUse: 'public', firstUseDate: '2020-01-01' }), TODAY)).toEqual({});
    expect(validateTrademark(trademark({ currentUse: 'internal', firstUseDate: '2099-01-01' }), TODAY)).toEqual({});
    expect(validateTrademark(trademark({ launchDate: '2026-10-11' }), TODAY).launchDate).toMatch(/Choose a date/);
    expect(validateTrademark(trademark({ launchDate: '2027-03-01' }), TODAY)).toEqual({});
  });

  it('neededBy: today up to the launch date when set, else a year; never after launch', () => {
    expect(neededByMax(trademark(), TODAY)).toBe('2027-10-12');
    expect(neededByMax(trademark({ launchDate: '2026-12-01' }), TODAY)).toBe('2026-12-01');
    expect(neededByMax(trademark({ launchDate: '2026-01-01' }), TODAY)).toBe('2027-10-12'); // invalid launch: fall back
    expect(validateTrademark(trademark({ launchDate: '2026-12-01', neededBy: '2026-12-02' }), TODAY).neededBy).toMatch(
      /up to the launch date/
    );
    expect(validateTrademark(trademark({ launchDate: '2026-12-01', neededBy: '2026-12-01' }), TODAY)).toEqual({});
    expect(validateTrademark(trademark({ neededBy: '2026-10-11' }), TODAY).neededBy).toMatch(/Choose a date/);
  });

  it('answers: schemaVersion 1, codes, no key for an empty or hidden field (shortest path)', () => {
    expect(buildTrademarkAnswers(trademark())).toEqual({
      schemaVersion: 1,
      proposedMark: 'AURORA',
      markForm: 'word',
      goodsServices: 'A cloud analytics product for retail teams.',
      territories: ['us'],
      currentUse: 'notYet',
    });
  });

  it('answers: stale answers behind a closed branch are dropped; no department, requester or class key', () => {
    const a = buildTrademarkAnswers(
      trademark({
        territoriesOther: 'stale',
        currentUse: 'internal',
        firstUseDate: '2020-01-01',
        useLocations: 'stale',
      })
    );
    for (const key of ['territoriesOther', 'firstUseDate', 'useLocations']) expect(a).not.toHaveProperty(key);
    expect(Object.keys(a).some(k => /department|requester|class/i.test(k))).toBe(false);
    const open = buildTrademarkAnswers(
      trademark({
        territories: ['eu', 'other'],
        territoriesOther: ' Brazil ',
        currentUse: 'public',
        firstUseDate: '2020-01-01',
      })
    );
    expect(open).toMatchObject({
      territories: ['eu', 'other'],
      territoriesOther: 'Brazil',
      firstUseDate: '2020-01-01',
    });
  });

  it('title is "Trademark search – {proposedMark}" only - never the goods or services - capped at 850', () => {
    expect(trademarkTitle(trademark({ proposedMark: '  AURORA ' }))).toBe('Trademark search – AURORA');
    expect(trademarkTitle(trademark())).not.toMatch(/analytics|retail/);
    expect(trademarkTitle(trademark({ proposedMark: 'x'.repeat(2000) })).length).toBe(MAX_INTAKE_TITLE_LENGTH);
  });

  it('the review rows show labels (territories joined), and only the rows of open branches', () => {
    const rows = trademarkReviewRows(trademark({ territories: ['us', 'eu'], markRole: 'service' }));
    expect(rows.find(r => r.label === 'Where')?.value).toBe('United States, European Union');
    expect(rows.find(r => r.label === 'It will name')?.value).toBe('A service or programme');
    expect(rows.some(r => r.label === 'In use since')).toBe(false);
  });
});

describe('trademark territories (one module, stable codes)', () => {
  it('the fixed list carries the design note codes, "other" last, no duplicates, no classes', () => {
    const codes = getTrademarkTerritories().map(t => t.code);
    expect(codes).toEqual(['us', 'ca', 'eu', 'uk', 'cn', 'jp', 'in', 'au', 'worldwide', 'other']);
    expect(codes.at(-1)).toBe(TRADEMARK_TERRITORY_OTHER);
    expect(new Set(codes).size).toBe(codes.length);
    expect(trademarkTerritoryLabel('uk')).toBe('United Kingdom');
    expect(trademarkTerritoryLabel('nope')).toBeUndefined();
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
