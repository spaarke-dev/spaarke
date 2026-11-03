import {
  createRuleSetApi,
  describeFailure,
  accessAfterFailure,
  isBillingAdminRefusal,
  isEbillingDisabled,
  isForbidden,
  isStale,
  toFailure,
} from '../services/ruleSetApi';
import { ApiError, AuthError, POLICY_ID, SET_ID, problem } from './testKit';

const BASE = 'https://bff.example.test';

function fetchReturning(body: unknown) {
  return jest.fn().mockResolvedValue({ json: async () => body });
}

describe('toFailure', () => {
  it('maps a ProblemDetails 409 with its stable code', () => {
    const f = toFailure(problem(409, 'ruleset.concurrent_edit', { detail: 'The rule changed.' }));
    expect(f.status).toBe(409);
    expect(f.errorCode).toBe('ruleset.concurrent_edit');
    expect(isStale(f)).toBe(true);
    expect(f.detail).toBe('The rule changed.');
    expect(f.signInFailed).toBe(false);
  });

  it('a 409 with another code is not stale (e.g. a proposal in force)', () => {
    expect(isStale(toFailure(problem(409, 'ruleset.proposal_in_force')))).toBe(false);
    expect(isStale(toFailure(problem(409, 'ruleset.publish_in_progress')))).toBe(false);
  });

  it('keeps the per-rule 400 errors', () => {
    const f = toFailure(
      problem(400, 'ruleset.invalid', {
        errors: [{ policyId: POLICY_ID, rule: 'DailyHoursCap', field: 'maxHoursPerDay', message: 'must be at most 24', reason: 'maximum' }],
      })
    );
    expect(f.errors).toEqual([
      { policyId: POLICY_ID, rule: 'DailyHoursCap', field: 'maxHoursPerDay', message: 'must be at most 24', reason: 'maximum' },
    ]);
  });

  it('a 403 is forbidden, and a disabled flag is its own state', () => {
    expect(isForbidden(toFailure(problem(403, null, { reasonCode: 'sdap.access.deny.insufficient_privilege' })))).toBe(true);
    const f = toFailure(problem(503, 'ebilling.disabled'));
    expect(isEbillingDisabled(f)).toBe(true);
    expect(isForbidden(f)).toBe(false);
  });

  describe('access after a failed call (uac-r2 round 3 F1)', () => {
    const admin403 = () => toFailure(problem(403, null, { reasonCode: 'sdap.access.deny.insufficient_privilege' }));
    const procedure403 = () => toFailure(problem(403, null, { reasonCode: 'sdap.access.deny.insufficient_rights', detail: "You need Write on this rule set's Procedure." }));
    const systemFailure403 = () => toFailure(problem(403, null, { reasonCode: 'sdap.access.error.system_failure' }));

    it('must-fire: the billing-admin 403 turns the page read-only', () => {
      expect(isBillingAdminRefusal(admin403())).toBe(true);
      expect(accessAfterFailure('admin', admin403())).toBe('readOnly');
      expect(accessAfterFailure('unknown', admin403())).toBe('readOnly');
    });

    it('must-not-fire: a Procedure insufficient_rights 403 leaves the access as it was', () => {
      expect(isForbidden(procedure403())).toBe(true);
      expect(isBillingAdminRefusal(procedure403())).toBe(false);
      expect(accessAfterFailure('admin', procedure403())).toBe('admin');
      expect(accessAfterFailure('unknown', procedure403())).toBe('unknown');
      expect(accessAfterFailure('readOnly', procedure403())).toBe('readOnly');
    });

    it('must-not-fire: a system_failure 403 leaves the access as it was', () => {
      expect(isBillingAdminRefusal(systemFailure403())).toBe(false);
      expect(accessAfterFailure('admin', systemFailure403())).toBe('admin');
    });

    it('must-not-fire: a 403 with no reasonCode, and a non-403 carrying the code, leave the access as it was', () => {
      expect(accessAfterFailure('admin', toFailure(problem(403, null)))).toBe('admin');
      expect(accessAfterFailure('admin', toFailure(problem(500, null, { reasonCode: 'sdap.access.deny.insufficient_privilege' })))).toBe('admin');
    });
  });

  it('an auth error is a sign-in failure; a network error is neither', () => {
    expect(toFailure(new AuthError('no token')).signInFailed).toBe(true);
    expect(toFailure(new ApiError('Unauthorized', 401, null)).signInFailed).toBe(true);
    const net = toFailure(new TypeError('Failed to fetch'));
    expect(net.signInFailed).toBe(false);
    expect(net.status).toBe(0);
  });

  it('describeFailure carries the server code', () => {
    expect(describeFailure(toFailure(problem(409, 'ruleset.concurrent_edit', { detail: 'Changed.' })))).toBe(
      'Changed. (ruleset.concurrent_edit)'
    );
  });
});

describe('createRuleSetApi routes', () => {
  it('lists and reads', async () => {
    const f = fetchReturning([]);
    const api = createRuleSetApi(f, `${BASE}/`);
    await api.list();
    await api.get(SET_ID);
    expect(f.mock.calls[0][0]).toBe(`${BASE}/api/finance/rulesets`);
    expect(f.mock.calls[1][0]).toBe(`${BASE}/api/finance/rulesets/${SET_ID}`);
  });

  it('accept sends the version the page was shown, as the body', async () => {
    const f = fetchReturning({});
    await createRuleSetApi(f, BASE).accept(SET_ID, POLICY_ID, '4711');
    const [url, init] = f.mock.calls[0];
    expect(url).toBe(`${BASE}/api/finance/rulesets/${SET_ID}/proposals/${POLICY_ID}/accept`);
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body)).toEqual({ rowVersion: '4711' });
  });

  it('reject sends the version the page was shown and the reason, as the body (D-113)', async () => {
    const f = fetchReturning({});
    await createRuleSetApi(f, BASE).reject(SET_ID, POLICY_ID, '4711', 'The guideline allows 12 hours.');
    const [url, init] = f.mock.calls[0];
    expect(url).toBe(`${BASE}/api/finance/rulesets/${SET_ID}/proposals/${POLICY_ID}/reject`);
    expect(JSON.parse(init.body)).toEqual({ rowVersion: '4711', reason: 'The guideline allows 12 hours.' });
  });

  it('edit patches only the members given', async () => {
    const f = fetchReturning({});
    await createRuleSetApi(f, BASE).edit(SET_ID, POLICY_ID, { consequence: 'Reject' });
    const [url, init] = f.mock.calls[0];
    expect(url).toBe(`${BASE}/api/finance/rulesets/${SET_ID}/proposals/${POLICY_ID}`);
    expect(init.method).toBe('PATCH');
    expect(JSON.parse(init.body)).toEqual({ consequence: 'Reject' });
  });

  it('draft, publish, assignment and visibility use their 076/075 routes', async () => {
    const f = fetchReturning({});
    const api = createRuleSetApi(f, BASE);
    await api.draft(SET_ID);
    await api.publish(SET_ID, '2026-11-01');
    await api.assign(SET_ID, 'Firm', 'abc');
    await api.assign(SET_ID, 'ClientDefault', 'ignored');
    await api.setVisibility(SET_ID, true);
    const urls = f.mock.calls.map(c => `${c[1].method} ${String(c[0]).replace(`${BASE}/api/finance/rulesets/${SET_ID}`, '')}`);
    expect(urls).toEqual(['POST /draft', 'POST /publish', 'PUT /assignment', 'PUT /assignment', 'PUT /visibility']);
    expect(JSON.parse(f.mock.calls[1][1].body)).toEqual({ effectiveDate: '2026-11-01' });
    expect(JSON.parse(f.mock.calls[2][1].body)).toEqual({ scope: 'Firm', firmId: 'abc' });
    expect(JSON.parse(f.mock.calls[3][1].body)).toEqual({ scope: 'ClientDefault', firmId: null });
    expect(JSON.parse(f.mock.calls[4][1].body)).toEqual({ visibleToFirms: true });
  });

  it('create puts the metadata in the query and the file in the multipart body', async () => {
    const f = fetchReturning({});
    const file = new Blob(['guideline'], { type: 'application/pdf' });
    await createRuleSetApi(f, BASE).create({ name: 'A & B', sourceKind: 'Guideline', scope: 'Firm', firmId: 'f-1', file, fileName: 'g.pdf' });
    const [url, init] = f.mock.calls[0];
    const u = new URL(String(url));
    expect(u.pathname).toBe('/api/finance/rulesets');
    expect(u.searchParams.get('name')).toBe('A & B');
    expect(u.searchParams.get('sourceKind')).toBe('Guideline');
    expect(u.searchParams.get('scope')).toBe('Firm');
    expect(u.searchParams.get('firmId')).toBe('f-1');
    expect(init.body).toBeInstanceOf(FormData);
    expect((init.body as FormData).get('file')).toBeTruthy();
  });

  it('create leaves the firm out of a client default', async () => {
    const f = fetchReturning({});
    await createRuleSetApi(f, BASE).create({
      name: 'N', sourceKind: 'RateCard', scope: 'ClientDefault', firmId: 'f-1', file: new Blob(['x']), fileName: 'r.xlsx',
    });
    expect(new URL(String(f.mock.calls[0][0])).searchParams.has('firmId')).toBe(false);
  });

  it('a throwing fetch becomes a failed result, never an exception', async () => {
    const f = jest.fn().mockRejectedValue(problem(409, 'ruleset.concurrent_edit'));
    const result = await createRuleSetApi(f, BASE).accept(SET_ID, POLICY_ID, '1');
    expect(result.ok).toBe(false);
    if (!result.ok) expect(isStale(result.failure)).toBe(true);
  });
});
