import { createOverridesApi, failureText, toWriteFailure, type WriteFailure } from '../services/overridesApi';
import { AuthError, BASE, jsonResponse, problem } from './testKit';

const REC = '4471AAAA-0000-4000-8000-000000000001';
const rec = REC.toLowerCase();

function api(answer: unknown) {
  const fetchFn = jest.fn(async (_url: string, _init?: RequestInit) => {
    if (answer instanceof Error) throw answer;
    return jsonResponse(answer);
  });
  return { fetchFn, client: createOverridesApi(fetchFn as never, BASE) };
}

describe('the requests are exactly the shapes of task 089\'s routes', () => {
  it('assign: POST procedure-assignments with { procedureId, mode, pinnedVersionId, reason } and a JSON content type', async () => {
    const { fetchFn, client } = api({ id: 'row' });
    await client.assignProcedure('matters', REC, { procedureId: 'P-1', mode: 'Pinned', pinnedVersionId: 'V-1', reason: '  Grandfathered ' });
    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toBe(`https://bff.example.com/api/finance/matters/${rec}/procedure-assignments`);
    expect(init?.method).toBe('POST');
    expect(init?.headers).toEqual({ 'Content-Type': 'application/json' });
    expect(JSON.parse(String(init?.body))).toEqual({ procedureId: 'P-1', mode: 'Pinned', pinnedVersionId: 'V-1', reason: 'Grandfathered' });
  });

  it('assign: a mode with nothing to add sends only procedureId and mode (no blank reason, no pinnedVersionId)', async () => {
    const { fetchFn, client } = api({ id: 'row' });
    await client.assignProcedure('projects', REC, { procedureId: 'P-1', mode: 'FollowLatest', reason: '   ' });
    expect(JSON.parse(String(fetchFn.mock.calls[0][1]?.body))).toEqual({ procedureId: 'P-1', mode: 'FollowLatest' });
    expect(fetchFn.mock.calls[0][0]).toContain('/api/finance/projects/');
  });

  it('exclude rule: POST rule-overrides/exclude with { policyId, reason, firmId? }', async () => {
    const { fetchFn, client } = api({ id: 'row' });
    await client.excludeRule('workassignments', REC, { policyId: 'POL-1', reason: ' Fixed format ', firmId: 'F-1' });
    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toBe(`https://bff.example.com/api/finance/workassignments/${rec}/rule-overrides/exclude`);
    expect(init?.method).toBe('POST');
    expect(JSON.parse(String(init?.body))).toEqual({ policyId: 'POL-1', reason: 'Fixed format', firmId: 'F-1' });

    await client.excludeRule('matters', REC, { policyId: 'POL-1', reason: 'r' });
    expect(JSON.parse(String(fetchFn.mock.calls[1][1]?.body))).toEqual({ policyId: 'POL-1', reason: 'r' });
  });

  it('removal preview is a GET of overrides/{id}/removal-preview; removal is a DELETE of overrides/{id}, with no body', async () => {
    const { fetchFn, client } = api({ overrideId: 'row' });
    await client.removalPreview('matters', REC, 'ROW-1');
    await client.removeOverride('matters', REC, 'ROW-1');
    expect(fetchFn.mock.calls[0]).toEqual([`https://bff.example.com/api/finance/matters/${rec}/overrides/row-1/removal-preview`, { method: 'GET' }]);
    expect(fetchFn.mock.calls[1]).toEqual([`https://bff.example.com/api/finance/matters/${rec}/overrides/row-1`, { method: 'DELETE' }]);
  });

  it('the Pin picker reads task 081\'s Procedure and maps its versions', async () => {
    const { fetchFn, client } = api({
      versions: [{ id: 'V-1', versionNumber: 1, status: 'Superseded', effectiveFrom: '2025-01-01', effectiveTo: '2026-01-01' }, { id: 'V-2', versionNumber: 2, status: 'Active', effectiveFrom: '2026-01-01', effectiveTo: null }],
    });
    const r = await client.procedureVersions('P-1');
    expect(fetchFn.mock.calls[0]).toEqual(['https://bff.example.com/api/finance/procedures/p-1', { method: 'GET' }]);
    expect(r).toEqual({
      ok: true,
      value: [
        { id: 'V-1', number: 1, status: 'Superseded', from: '2025-01-01', to: '2026-01-01' },
        { id: 'V-2', number: 2, status: 'Active', from: '2026-01-01', to: null },
      ],
    });
  });

  it('has no call for the change route (this module knows no rule-overrides/change)', () => {
    const { client } = api({});
    expect(Object.keys(client).sort()).toEqual(['assignProcedure', 'excludeRule', 'procedureVersions', 'removalPreview', 'removeOverride']);
  });
});

describe('refusals keep the server\'s words', () => {
  it('a ProblemDetails refusal carries its title, detail, code and field problems', async () => {
    const { client } = api(problem(409, 'assignment.duplicate', { title: 'Conflict', detail: 'This record already has an override for it. Remove that override first.' }));
    const r = await client.assignProcedure('matters', REC, { procedureId: 'P-1', mode: 'Excluded', reason: 'r' });
    expect(r).toEqual({
      ok: false,
      failure: { status: 409, errorCode: 'assignment.duplicate', title: 'Conflict', detail: 'This record already has an override for it. Remove that override first.', problems: [], signInFailed: false },
    });
  });

  it('a 400 lists its problems, from the override validation and from ValidationProblem alike', () => {
    const a = toWriteFailure(problem(400, 'assignment.invalid', { title: 'Override request is not valid', errors: [{ message: 'A reason is required.' }, { message: 'The firm was not found.' }] }));
    expect(a.problems).toEqual(['A reason is required.', 'The firm was not found.']);
    const b = toWriteFailure(problem(400, null, { title: 'One or more validation errors occurred.', errors: { policyId: ['policyId is required.'] } }));
    expect(b.problems).toEqual(['policyId is required.']);
  });

  it('failureText leads with the title, then the detail and problems', () => {
    const f: WriteFailure = { status: 400, errorCode: null, title: 'Override request is not valid', detail: '1 problem(s) found; nothing was written.', problems: ['A reason is required.'], signInFailed: false };
    expect(failureText(f)).toBe('Override request is not valid. 1 problem(s) found; nothing was written. A reason is required.');
    expect(failureText({ ...f, detail: null, problems: [] })).toBe('Override request is not valid');
    expect(failureText({ ...f, detail: 'Override request is not valid', problems: [] })).toBe('Override request is not valid');
  });

  it('with no title the words are ours: no response, a sign-in failure, or a refusal that was not ProblemDetails', () => {
    expect(failureText(toWriteFailure(new TypeError('Failed to fetch')))).toMatch(/could not be sent/);
    expect(failureText(toWriteFailure(new AuthError('no token', 'token_failed')))).toMatch(/Sign-in could not be verified/);
    expect(failureText({ status: 500, errorCode: null, title: null, detail: null, problems: [], signInFailed: false })).toBe('The change was not saved.');
  });

  it('a 2xx whose body is not what the route returns is a failure, never a reported change', async () => {
    for (const body of [null, [], 'ok', {}]) {
      const { client } = api(body);
      expect((await client.assignProcedure('matters', REC, { procedureId: 'P-1', mode: 'FollowLatest' })).ok).toBe(false);
      expect((await client.removeOverride('matters', REC, 'r')).ok).toBe(false);
    }
  });
});
