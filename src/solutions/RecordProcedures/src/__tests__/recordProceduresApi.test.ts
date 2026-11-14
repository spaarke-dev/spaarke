import {
  createRecordProceduresApi,
  failureKind,
  failureMessage,
  pickFailure,
  proceduresUrl,
  readRulesView,
  rulesInForceUrl,
  toFailure,
  type ApiFailure,
} from '../services/recordProceduresApi';
import { matterProcedures, matterRules, MATTER_ID, FENWICK } from './fixtures';
import { ApiError, AuthError, BASE, jsonResponse, problem } from './testKit';

const q = { firmId: FENWICK.id.toUpperCase(), date: '2026-10-21' };
const fail = (over: Partial<ApiFailure>): ApiFailure => ({ status: 0, errorCode: null, detail: null, signInFailed: false, ...over });

describe('URLs', () => {
  it('one literal chain per core record, the id lower case, the firm and date as query', () => {
    expect(proceduresUrl(BASE, 'matters', MATTER_ID.toUpperCase(), q)).toBe(
      `https://bff.example.com/api/finance/matters/${MATTER_ID}/procedures?firmId=${FENWICK.id}&date=2026-10-21`
    );
    expect(rulesInForceUrl(BASE, 'workassignments', MATTER_ID, q)).toContain('/api/finance/workassignments/');
    expect(rulesInForceUrl(BASE, 'projects', MATTER_ID, q)).toContain('/projects/' + MATTER_ID + '/rules-in-force?');
  });

  it('no firm is no firmId (the record billing firm is the server default)', () => {
    expect(proceduresUrl(BASE, 'matters', MATTER_ID, { date: '2026-10-21' })).toBe(`https://bff.example.com/api/finance/matters/${MATTER_ID}/procedures?date=2026-10-21`);
    expect(proceduresUrl(BASE, 'matters', MATTER_ID, { firmId: null, date: '2026-10-21' })).not.toContain('firmId');
  });
});

describe('the client', () => {
  it('reads the Procedures view and the rules view with GET and nothing else', async () => {
    const fetchFn = jest.fn().mockResolvedValueOnce(jsonResponse(matterProcedures)).mockResolvedValueOnce(jsonResponse(matterRules));
    const api = createRecordProceduresApi(fetchFn, BASE);

    const p = await api.procedures('matters', MATTER_ID, q);
    const r = await api.rulesInForce('matters', MATTER_ID, q);

    expect(p).toMatchObject({ ok: true, value: { procedures: expect.any(Array), gaps: expect.any(Array) } });
    expect(r).toMatchObject({ ok: true, value: { rules: expect.any(Array) } });
    expect(fetchFn).toHaveBeenCalledTimes(2);
    for (const [, init] of fetchFn.mock.calls) expect(init).toEqual({ method: 'GET' });
  });

  it('an empty list is a success (nothing applies), a body that is not the view is a failure, never an empty list', async () => {
    const empty = await createRecordProceduresApi(jest.fn().mockResolvedValue(jsonResponse({ ...matterProcedures, procedures: [], gaps: [] })), BASE).procedures('matters', MATTER_ID, q);
    expect(empty).toMatchObject({ ok: true, value: { procedures: [] } });

    for (const body of [{ message: 'oops' }, [], null, 'x', { procedures: 'nope' }]) {
      const result = await createRecordProceduresApi(jest.fn().mockResolvedValue(jsonResponse(body)), BASE).procedures('matters', MATTER_ID, q);
      expect(result.ok).toBe(false);
    }
    const rules = await createRecordProceduresApi(jest.fn().mockResolvedValue(jsonResponse({ rules: {} })), BASE).rulesInForce('matters', MATTER_ID, q);
    expect(rules.ok).toBe(false);
  });

  it('a rule with no marks array is read with an empty one (the reader never meets undefined)', () => {
    const view = readRulesView({ ...matterRules, rules: [{ ...matterRules.rules[0], marks: undefined }] });
    expect(view?.rules[0].marks).toEqual([]);
  });

  it('a thrown ApiError keeps its status and code; a network error is status 0', async () => {
    const api = (err: unknown) => createRecordProceduresApi(jest.fn().mockRejectedValue(err), BASE);
    expect(await api(problem(404, null)).procedures('matters', MATTER_ID, q)).toMatchObject({ ok: false, failure: { status: 404 } });
    expect(await api(problem(503, 'procedures.unavailable')).rulesInForce('matters', MATTER_ID, q)).toMatchObject({
      ok: false,
      failure: { status: 503, errorCode: 'procedures.unavailable' },
    });
    expect(await api(problem(400, 'procedures.invalid_query')).procedures('matters', MATTER_ID, q)).toMatchObject({ ok: false, failure: { status: 400 } });
    expect(await api(new TypeError('Failed to fetch')).procedures('matters', MATTER_ID, q)).toMatchObject({ ok: false, failure: { status: 0, signInFailed: false } });
  });

  it('an exhausted sign-in is reported as a sign-in failure', () => {
    expect(toFailure(new AuthError('no token', 'token_unavailable' as never)).signInFailed).toBe(true);
    expect(problem(404, null)).toBeInstanceOf(ApiError);
  });
});

describe('failures', () => {
  it('maps each failure to its own kind', () => {
    expect(failureKind(fail({ signInFailed: true }))).toBe('signin');
    expect(failureKind(fail({ status: 404 }))).toBe('notAvailable');
    expect(failureKind(fail({ status: 503, errorCode: 'ebilling.disabled' }))).toBe('ebillingOff');
    expect(failureKind(fail({ status: 503, errorCode: 'procedures.unavailable' }))).toBe('unavailable');
    expect(failureKind(fail({ status: 503 }))).toBe('unavailable');
    expect(failureKind(fail({ status: 400, errorCode: 'procedures.invalid_query' }))).toBe('invalidQuery');
    expect(failureKind(fail({ status: 0 }))).toBe('network');
    expect(failureKind(fail({ status: 500 }))).toBe('other');
  });

  it('the 404 is one message whoever sent it: it never says whether the record exists', () => {
    const m = failureMessage('notAvailable');
    expect(m.title).toBe('Procedures are not available for this record');
    expect(m.body).toMatch(/may not exist, or you may not have access/);
  });

  it('503 procedures.unavailable says "Procedures cannot be shown right now"', () => {
    expect(failureMessage('unavailable').title).toBe('Procedures cannot be shown right now');
    expect(failureMessage('ebillingOff').body).toMatch(/eBilling is not enabled/);
  });

  it('when both routes fail, the most definite failure is the one reported', () => {
    const notFound = fail({ status: 404 });
    const unavailable = fail({ status: 503, errorCode: 'procedures.unavailable' });
    expect(pickFailure(unavailable, notFound)).toBe(notFound);
    expect(pickFailure(notFound, unavailable)).toBe(notFound);
    expect(pickFailure(unavailable, null)).toBe(unavailable);
  });
});
