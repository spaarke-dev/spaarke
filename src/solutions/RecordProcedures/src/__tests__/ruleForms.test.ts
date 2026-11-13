/**
 * The per-type rule forms (ebilling-r1 task 084c): the body each builds, the bounds it enforces, and the server's refusal put back on the
 * fields. The forms are pinned against the server's schemas by tests/Spaarke.ArchTests/RecordProceduresRuleFormAgreementTests.cs.
 */

import { RULE_FORMS, buildBody, formFor, sameBody, serverFieldErrors, toFormValues, describeBody, parseCodes } from '../components/wizards/ruleForms';
import type { FormValues } from '../components/wizards/ruleForms';

/** One valid body per authorable type: what the server accepts. */
const VALID: Record<string, Record<string, unknown>> = {
  ApprovedTimekeeper: { type: 'ApprovedTimekeeper' },
  ApprovedRate: { type: 'ApprovedRate', tolerancePercent: 5 },
  TimeIncrement: { type: 'TimeIncrement', incrementHours: 0.1 },
  DailyHoursCap: { type: 'DailyHoursCap', maxHoursPerDay: 12 },
  NonBillableActivity: { type: 'NonBillableActivity', codes: ['L110', 'L120'] },
  ExpenseRule: { type: 'ExpenseRule', nonReimbursableCodes: ['E101'], maxAmountPerItem: 250 },
  InvoiceAge: { type: 'InvoiceAge', maxAgeDays: 90 },
  CoreRecordWindow: { type: 'CoreRecordWindow', graceDays: 7 },
  Duplicate: { type: 'Duplicate', matchFields: ['firmInvoiceNumber', 'timekeeper'] },
  RequiredData: { type: 'RequiredData', fields: ['firmInvoiceNumber', 'invoiceDate'] },
  AiObservation: { type: 'AiObservation', observation: 'blockBilling', minConfidence: 0.8 },
  AddedFee: { type: 'AddedFee', feeKind: 'lateFee', allowance: 'cap', feeCodes: ['LATE1'], capAmount: 500 },
  Discount: { type: 'Discount', discountPercent: 10, tolerancePercent: 2 },
};

const form = (t: string) => formFor(t)!;

describe('the table of forms', () => {
  it('has one form per authorable type, each found by the name the read routes send, and none for the system type or an unknown word', () => {
    expect(RULE_FORMS.map(f => f.ruleType).sort()).toEqual(Object.keys(VALID).sort());
    expect(new Set(RULE_FORMS.map(f => f.ruleType)).size).toBe(RULE_FORMS.length);
    for (const t of Object.keys(VALID)) expect(formFor(t)?.ruleType).toBe(t);
    expect(formFor('SubmissionIntegrity')).toBeUndefined();
    expect(formFor('Time-entry check')).toBeUndefined();
    expect(formFor(null)).toBeUndefined();
    expect(formFor(undefined)).toBeUndefined();
  });

  it('a type with no values, or with a rate card, says why it is not changed; every other form has fields', () => {
    expect(RULE_FORMS.filter(f => f.noChange).map(f => f.ruleType).sort()).toEqual(['ApprovedRate', 'ApprovedTimekeeper']);
    expect(form('ApprovedRate').noChange).toMatch(/pin that version/i);
    for (const f of RULE_FORMS) expect([f.ruleType, f.fields.length > 0 || !!f.noChange]).toEqual([f.ruleType, true]);
  });
});

describe('every form builds the body the server accepts, and reads one back', () => {
  it.each(Object.keys(VALID))('%s: a body read into the inputs builds the same body', t => {
    const f = form(t);
    const built = buildBody(f, toFormValues(f, VALID[t]));
    expect(built).toEqual({ ok: true, body: VALID[t] });
  });

  it('the body carries the type, and only the fields that apply', () => {
    const f = form('DailyHoursCap');
    expect(buildBody(f, { maxHoursPerDay: ' 12 ' })).toEqual({ ok: true, body: { type: 'DailyHoursCap', maxHoursPerDay: 12 } });
  });
});

describe('the bounds are the schema\'s', () => {
  const cases: Array<[string, string, string, boolean]> = [
    ['DailyHoursCap', 'maxHoursPerDay', '0', false],
    ['DailyHoursCap', 'maxHoursPerDay', '0.5', true],
    ['DailyHoursCap', 'maxHoursPerDay', '24', true],
    ['DailyHoursCap', 'maxHoursPerDay', '24.5', false],
    ['DailyHoursCap', 'maxHoursPerDay', '', false],
    ['DailyHoursCap', 'maxHoursPerDay', 'twelve', false],
    ['TimeIncrement', 'incrementHours', '1', true],
    ['TimeIncrement', 'incrementHours', '1.01', false],
    ['InvoiceAge', 'maxAgeDays', '3650', true],
    ['InvoiceAge', 'maxAgeDays', '3651', false],
    ['InvoiceAge', 'maxAgeDays', '0', false],
    ['InvoiceAge', 'maxAgeDays', '30.5', false],
    ['CoreRecordWindow', 'graceDays', '0', true],
    ['CoreRecordWindow', 'graceDays', '-1', false],
    ['CoreRecordWindow', 'graceDays', '366', false],
    ['AiObservation', 'minConfidence', '1', true],
    ['AiObservation', 'minConfidence', '0', false],
    ['Discount', 'discountPercent', '100', false],
    ['Discount', 'discountPercent', '99.9', true],
    ['Discount', 'discountPercent', '0', false],
  ];
  it.each(cases)('%s.%s = "%s" is %s', (type, key, text, ok) => {
    const f = form(type);
    const values: FormValues = { ...toFormValues(f, VALID[type]), [key]: text };
    const built = buildBody(f, values);
    expect(built.ok).toBe(ok);
    if (!built.ok) expect(Object.keys(built.errors)).toContain(key);
  });

  it('an optional number may be left empty and is then absent from the body (Discount tolerance)', () => {
    expect(buildBody(form('Discount'), { discountPercent: '10', tolerancePercent: '' })).toEqual({ ok: true, body: { type: 'Discount', discountPercent: 10 } });
    expect(buildBody(form('Discount'), { discountPercent: '10', tolerancePercent: '101' }).ok).toBe(false);
  });
});

describe('choices, sets and code lists', () => {
  it('a choice must be one of the options; a set needs one pick and is written in the schema\'s order', () => {
    expect(buildBody(form('AiObservation'), { observation: 'other', minConfidence: '0.5' }).ok).toBe(false);
    expect(buildBody(form('AiObservation'), { observation: '', minConfidence: '0.5' }).ok).toBe(false);
    expect(buildBody(form('Duplicate'), { matchFields: [] }).ok).toBe(false);
    const swapped = buildBody(form('Duplicate'), { matchFields: ['timekeeper', 'firmInvoiceNumber'] });
    expect(swapped).toEqual({ ok: true, body: { type: 'Duplicate', matchFields: ['firmInvoiceNumber', 'timekeeper'] } });
  });

  it('codes: separators, duplicates, the pattern and the empty list', () => {
    expect(parseCodes(' L110,L120;  L110\nL130 ')).toEqual(['L110', 'L120', 'L110', 'L130']);
    expect(buildBody(form('NonBillableActivity'), { codes: 'L110, L120 L110' })).toEqual({ ok: true, body: { type: 'NonBillableActivity', codes: ['L110', 'L120'] } });
    const bad = buildBody(form('NonBillableActivity'), { codes: 'L110, bad code!' });
    expect(bad.ok).toBe(false);
    expect(buildBody(form('NonBillableActivity'), { codes: '   ' }).ok).toBe(false);
    expect(buildBody(form('NonBillableActivity'), { codes: 'X'.repeat(21) }).ok).toBe(false);
    expect(buildBody(form('NonBillableActivity'), { codes: Array.from({ length: 501 }, (_, i) => `C${i}`).join(' ') }).ok).toBe(false);
  });

  it('ExpenseRule needs at least one of the codes and the cap, and either alone is enough', () => {
    const f = form('ExpenseRule');
    expect(buildBody(f, { nonReimbursableCodes: '', maxAmountPerItem: '' }).ok).toBe(false);
    expect(buildBody(f, { nonReimbursableCodes: 'E101', maxAmountPerItem: '' })).toEqual({ ok: true, body: { type: 'ExpenseRule', nonReimbursableCodes: ['E101'] } });
    expect(buildBody(f, { nonReimbursableCodes: '', maxAmountPerItem: '75.5' })).toEqual({ ok: true, body: { type: 'ExpenseRule', maxAmountPerItem: 75.5 } });
  });
});

describe('AddedFee: only the allowance the server checks can be saved', () => {
  const f = form('AddedFee');

  it('Not allowed and Allowed after N days are listed and refused, with the reason', () => {
    const options = f.fields.find(x => x.key === 'allowance')!.options!;
    expect(options.map(o => [o.value, !!o.refused])).toEqual([['notAllowed', true], ['cap', false], ['afterDays', true]]);
    for (const allowance of ['notAllowed', 'afterDays']) {
      const built = buildBody(f, { feeKind: 'lateFee', allowance, feeCodes: 'LATE1', capAmount: '5', afterDays: '30' });
      expect(built.ok).toBe(false);
      if (!built.ok) expect(built.errors.allowance).toMatch(/cannot check this form yet/);
    }
  });

  it('a cap needs the fee codes and exactly one of an amount or a percent; a field of another allowance never reaches the body', () => {
    const base = { feeKind: 'lateFee', allowance: 'cap', feeCodes: 'LATE1', afterDays: '30' };
    expect(buildBody(f, { ...base, capAmount: '', capPercent: '' }).ok).toBe(false);
    expect(buildBody(f, { ...base, capAmount: '5', capPercent: '10' }).ok).toBe(false);
    expect(buildBody(f, { ...base, capAmount: '', capPercent: '10' })).toEqual({ ok: true, body: { type: 'AddedFee', feeKind: 'lateFee', allowance: 'cap', feeCodes: ['LATE1'], capPercent: 10 } });
    expect(buildBody(f, { ...base, feeCodes: '', capAmount: '5', capPercent: '' }).ok).toBe(false);
  });
});

describe('describing and comparing bodies', () => {
  it('sameBody ignores key order and compares arrays in order; describeBody names the values', () => {
    expect(sameBody({ a: 1, b: [1, 2] }, { b: [1, 2], a: 1 })).toBe(true);
    expect(sameBody({ a: 1, b: [1, 2] }, { a: 1, b: [2, 1] })).toBe(false);
    expect(sameBody({ a: 1 }, { a: 1, b: undefined })).toBe(false);
    expect(sameBody(null, {})).toBe(false);
    expect(describeBody(form('DailyHoursCap'), VALID.DailyHoursCap)).toBe('Most hours per day 12');
    expect(describeBody(form('Duplicate'), VALID.Duplicate)).toBe('Fields compared Firm invoice number, Timekeeper');
  });
});

describe('the server\'s refusal goes back on the fields it names', () => {
  const f = form('DailyHoursCap');
  const err = (field: string, message: string) => ({ field, message, reason: null });

  it('a body error with a JSON pointer lands on that field, without the pointer', () => {
    const r = serverFieldErrors(f, [err('ruleBody', '/maxHoursPerDay: value must be at most 24')]);
    expect(r).toEqual({ byField: { maxHoursPerDay: ['value must be at most 24'] }, firm: [], general: [] });
  });

  it('"Required properties" names every field of the form it lists', () => {
    const r = serverFieldErrors(form('AiObservation'), [err('ruleBody', 'Required properties ["observation", "minConfidence"] are not present')]);
    expect(Object.keys(r.byField).sort()).toEqual(['minConfidence', 'observation']);
    expect(r.general).toEqual([]);
  });

  it('the firm field gets firmId; everything else, and a pointer to a member the form does not have, is general: nothing is dropped', () => {
    const r = serverFieldErrors(f, [err('firmId', 'The firm was not found.'), err('reason', 'A reason is required.'), err('ruleBody', '/unknownMember: nope'), err('ruleBody', 'The body is not valid JSON.'), err('', 'Something else.')]);
    expect(r.firm).toEqual(['The firm was not found.']);
    expect(r.byField).toEqual({});
    expect(r.general).toEqual(['A reason is required.', '/unknownMember: nope', 'The body is not valid JSON.', 'Something else.']);
  });
});
