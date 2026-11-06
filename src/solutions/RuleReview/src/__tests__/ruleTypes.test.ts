import {
  MAX_CODES,
  RULE_TYPES,
  buildBody,
  parseCodes,
  ruleTypeDef,
  sameBody,
  toParamValues,
} from '../services/ruleTypes';

const def = (type: string) => {
  const d = ruleTypeDef(type);
  if (!d) throw new Error(`no ${type}`);
  return d;
};

describe('rule type catalogue', () => {
  it('covers the eleven billing rule types', () => {
    expect(RULE_TYPES.map(t => t.type).sort()).toEqual(
      [
        'AiObservation', 'ApprovedRate', 'ApprovedTimekeeper', 'CoreRecordWindow', 'DailyHoursCap', 'Duplicate',
        'ExpenseRule', 'InvoiceAge', 'NonBillableActivity', 'RequiredData', 'TimeIncrement',
      ].sort()
    );
  });
});

describe('buildBody bounds', () => {
  it('DailyHoursCap: more than 0, at most 24', () => {
    const d = def('DailyHoursCap');
    expect(buildBody(d, { maxHoursPerDay: '10' })).toEqual({ ok: true, body: { type: 'DailyHoursCap', maxHoursPerDay: 10 } });
    expect(buildBody(d, { maxHoursPerDay: '24' }).ok).toBe(true);
    expect(buildBody(d, { maxHoursPerDay: '24.5' }).ok).toBe(false);
    expect(buildBody(d, { maxHoursPerDay: '25' }).ok).toBe(false);
    expect(buildBody(d, { maxHoursPerDay: '0' }).ok).toBe(false);
    expect(buildBody(d, { maxHoursPerDay: '' }).ok).toBe(false);
    expect(buildBody(d, { maxHoursPerDay: 'abc' }).ok).toBe(false);
  });

  it('ApprovedRate: 0 to 100 inclusive', () => {
    const d = def('ApprovedRate');
    expect(buildBody(d, { tolerancePercent: '0' }).ok).toBe(true);
    expect(buildBody(d, { tolerancePercent: '100' }).ok).toBe(true);
    expect(buildBody(d, { tolerancePercent: '-1' }).ok).toBe(false);
    expect(buildBody(d, { tolerancePercent: '100.1' }).ok).toBe(false);
  });

  it('InvoiceAge: a whole number from 1 to 3650', () => {
    const d = def('InvoiceAge');
    expect(buildBody(d, { maxAgeDays: '90' }).ok).toBe(true);
    expect(buildBody(d, { maxAgeDays: '9.5' }).ok).toBe(false);
    expect(buildBody(d, { maxAgeDays: '0' }).ok).toBe(false);
    expect(buildBody(d, { maxAgeDays: '3651' }).ok).toBe(false);
  });

  it('AiObservation needs an observation and a confidence in (0, 1]', () => {
    const d = def('AiObservation');
    expect(buildBody(d, { observation: 'blockBilling', minConfidence: '0.8' })).toEqual({
      ok: true, body: { type: 'AiObservation', observation: 'blockBilling', minConfidence: 0.8 },
    });
    expect(buildBody(d, { observation: 'other', minConfidence: '0.8' }).ok).toBe(false);
    expect(buildBody(d, { observation: 'blockBilling', minConfidence: '0' }).ok).toBe(false);
    expect(buildBody(d, { observation: 'blockBilling', minConfidence: '1.1' }).ok).toBe(false);
  });
});

describe('buildBody lists', () => {
  it('Duplicate and RequiredData need at least one choice, in vocabulary order', () => {
    expect(buildBody(def('Duplicate'), { matchFields: [] }).ok).toBe(false);
    expect(buildBody(def('Duplicate'), { matchFields: ['hours', 'firmInvoiceNumber'] })).toEqual({
      ok: true, body: { type: 'Duplicate', matchFields: ['firmInvoiceNumber', 'hours'] },
    });
    expect(buildBody(def('RequiredData'), { fields: [] }).ok).toBe(false);
  });

  it('NonBillableActivity: codes are split, de-duplicated and checked', () => {
    const d = def('NonBillableActivity');
    expect(buildBody(d, { codes: 'L110, L120; L110  L130' })).toEqual({ ok: true, body: { type: 'NonBillableActivity', codes: ['L110', 'L120', 'L130'] } });
    expect(buildBody(d, { codes: '' }).ok).toBe(false);
    expect(buildBody(d, { codes: 'L110, bad code!' }).ok).toBe(false);
    expect(buildBody(d, { codes: 'A'.repeat(21) }).ok).toBe(false);
    expect(buildBody(d, { codes: Array.from({ length: MAX_CODES + 1 }, (_, i) => `C${i}`).join(',') }).ok).toBe(false);
    expect(buildBody(d, { codes: Array.from({ length: MAX_CODES }, (_, i) => `C${i}`).join(',') }).ok).toBe(true);
  });

  it('ExpenseRule needs at least one of the codes or the cap', () => {
    const d = def('ExpenseRule');
    const none = buildBody(d, { nonReimbursableCodes: '', maxAmountPerItem: '' });
    expect(none.ok).toBe(false);
    expect(buildBody(d, { nonReimbursableCodes: 'E101', maxAmountPerItem: '' })).toEqual({
      ok: true, body: { type: 'ExpenseRule', nonReimbursableCodes: ['E101'] },
    });
    expect(buildBody(d, { nonReimbursableCodes: '', maxAmountPerItem: '250' })).toEqual({
      ok: true, body: { type: 'ExpenseRule', maxAmountPerItem: 250 },
    });
  });

  it('ApprovedTimekeeper has no parameters', () => {
    expect(buildBody(def('ApprovedTimekeeper'), {})).toEqual({ ok: true, body: { type: 'ApprovedTimekeeper' } });
  });
});

describe('round trip', () => {
  it('seeding the inputs from a body and building it again gives the same body', () => {
    const cases: Record<string, unknown>[] = [
      { type: 'DailyHoursCap', maxHoursPerDay: 10 },
      { type: 'NonBillableActivity', codes: ['L110', 'L120'] },
      { type: 'Duplicate', matchFields: ['firmInvoiceNumber', 'hours'] },
      { type: 'ExpenseRule', nonReimbursableCodes: ['E1'], maxAmountPerItem: 99.5 },
      { type: 'AiObservation', observation: 'vagueDescription', minConfidence: 0.7 },
    ];
    for (const body of cases) {
      const d = def(String(body.type));
      const built = buildBody(d, toParamValues(d, body));
      expect(built.ok).toBe(true);
      if (built.ok) expect(sameBody(built.body, body)).toBe(true);
    }
  });
});

describe('helpers', () => {
  it('parseCodes drops blanks', () => {
    expect(parseCodes(' a, ,b;;c ')).toEqual(['a', 'b', 'c']);
  });

  it('sameBody ignores key order and compares arrays in order', () => {
    expect(sameBody({ a: 1, b: [1, 2] }, { b: [1, 2], a: 1 })).toBe(true);
    expect(sameBody({ b: [1, 2] }, { b: [2, 1] })).toBe(false);
    expect(sameBody({ a: 1 }, { a: 1, b: 2 })).toBe(false);
  });
});
