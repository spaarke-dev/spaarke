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

  it('change rule: POST rule-overrides/change with { policyId, reason, firmId?, ruleBody } where ruleBody is JSON TEXT, not an object', async () => {
    const created = { override: { id: 'row' }, recordRule: { ruleSetId: 'RS', policyId: 'POL-9' }, closeComplete: true };
    const { fetchFn, client } = api(created);
    const ruleBody = '{"type":"DailyHoursCap","maxHoursPerDay":12}';
    const r = await client.changeRule('matters', REC, { policyId: 'POL-1', reason: '  Fixed cap ', firmId: 'F-1', ruleBody });
    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toBe(`https://bff.example.com/api/finance/matters/${rec}/rule-overrides/change`);
    expect(init?.method).toBe('POST');
    expect(init?.headers).toEqual({ 'Content-Type': 'application/json' });
    const sent = JSON.parse(String(init?.body));
    expect(sent).toEqual({ policyId: 'POL-1', reason: 'Fixed cap', firmId: 'F-1', ruleBody });
    expect(typeof sent.ruleBody).toBe('string');
    expect(r).toEqual({ ok: true, value: created });

    await client.changeRule('projects', REC, { policyId: 'POL-1', reason: 'r', ruleBody });
    expect(JSON.parse(String(fetchFn.mock.calls[1][1]?.body))).toEqual({ policyId: 'POL-1', reason: 'r', ruleBody });
    expect(fetchFn.mock.calls[1][0]).toContain('/api/finance/projects/');
  });

  it('change rule: a 201 body without the override or the record rule is not taken as success', async () => {
    const { client } = api({ id: 'row' });
    const r = await client.changeRule('matters', REC, { policyId: 'POL-1', reason: 'r', ruleBody: '{}' });
    expect(r.ok).toBe(false);
  });

  it('rule values: GET the rule set and find the rule by policy id; a body sent as text is parsed; a missing rule is a failure', async () => {
    const answer = {
      proposals: [
        { policyId: 'POL-0', versionId: 'V0', versionNumber: 1, inForce: true, ruleType: 'InvoiceAge', ruleBody: { type: 'InvoiceAge', maxAgeDays: 90 } },
        { policyId: 'POL-1', versionId: 'V1', versionNumber: 3, inForce: true, ruleType: 'DailyHoursCap', ruleBody: '{"type":"DailyHoursCap","maxHoursPerDay":10}' },
      ],
    };
    const { fetchFn, client } = api(answer);
    const r = await client.ruleValues('RS-1', 'pol-1');
    expect(fetchFn.mock.calls[0]).toEqual(['https://bff.example.com/api/finance/rulesets/rs-1', { method: 'GET' }]);
    expect(r).toEqual({ ok: true, value: { policyId: 'pol-1', versionId: 'V1', versionNumber: 3, inForce: true, ruleType: 'DailyHoursCap', body: { type: 'DailyHoursCap', maxHoursPerDay: 10 } } });
    expect((await api(answer).client.ruleValues('RS-1', 'POL-404')).ok).toBe(false);
    expect((await api({ proposals: [{ policyId: 'POL-1', inForce: false, ruleBody: 'not json' }] }).client.ruleValues('RS-1', 'POL-1'))).toMatchObject({ ok: true, value: { inForce: false, body: null } });
  });
});

describe('refusals keep the server\'s words', () => {
  it('a ProblemDetails refusal carries its title, detail, code and field problems', async () => {
    const { client } = api(problem(409, 'assignment.duplicate', { title: 'Conflict', detail: 'This record already has an override for it. Remove that override first.' }));
    const r = await client.assignProcedure('matters', REC, { procedureId: 'P-1', mode: 'Excluded', reason: 'r' });
    expect(r).toEqual({
      ok: false,
      failure: { status: 409, errorCode: 'assignment.duplicate', title: 'Conflict', detail: 'This record already has an override for it. Remove that override first.', problems: [], fieldErrors: [], signInFailed: false },
    });
  });

  it('a 400 lists its problems, from the override validation and from ValidationProblem alike', () => {
    const a = toWriteFailure(problem(400, 'assignment.invalid', { title: 'Override request is not valid', errors: [{ message: 'A reason is required.' }, { message: 'The firm was not found.' }] }));
    expect(a.problems).toEqual(['A reason is required.', 'The firm was not found.']);
    const b = toWriteFailure(problem(400, null, { title: 'One or more validation errors occurred.', errors: { policyId: ['policyId is required.'] } }));
    expect(b.problems).toEqual(['policyId is required.']);
  });

  it('a 400 of the change route keeps each problem with the request member it names, and the dictionary form keeps its keys', () => {
    const a = toWriteFailure(
      problem(400, 'ruleoverride.invalid', {
        title: 'Rule change is not valid',
        errors: [
          { policyId: 'POL-1', rule: 'DailyHoursCap', field: 'ruleBody', message: '/maxHoursPerDay: value must be at most 24', reason: 'schema' },
          { field: 'firmId', message: 'The firm was not found.' },
          { message: 'A reason is required.' },
        ],
      })
    );
    expect(a.fieldErrors).toEqual([
      { field: 'ruleBody', message: '/maxHoursPerDay: value must be at most 24', reason: 'schema' },
      { field: 'firmId', message: 'The firm was not found.', reason: null },
      { field: '', message: 'A reason is required.', reason: null },
    ]);
    expect(a.problems).toHaveLength(3);
    const b = toWriteFailure(problem(400, null, { errors: { ruleBody: ['bad'] } }));
    expect(b.fieldErrors).toEqual([{ field: 'ruleBody', message: 'bad', reason: null }]);
  });

  it('failureText leads with the title, then the detail and problems', () => {
    const f: WriteFailure = { status: 400, errorCode: null, title: 'Override request is not valid', detail: '1 problem(s) found; nothing was written.', problems: ['A reason is required.'], fieldErrors: [], signInFailed: false };
    expect(failureText(f)).toBe('Override request is not valid. 1 problem(s) found; nothing was written. A reason is required.');
    expect(failureText({ ...f, detail: null, problems: [] })).toBe('Override request is not valid');
    expect(failureText({ ...f, detail: 'Override request is not valid', problems: [] })).toBe('Override request is not valid');
  });

  it('with no title the words are ours: no response, a sign-in failure, or a refusal that was not ProblemDetails', () => {
    expect(failureText(toWriteFailure(new TypeError('Failed to fetch')))).toMatch(/could not be sent/);
    expect(failureText(toWriteFailure(new AuthError('no token', 'token_failed')))).toMatch(/Sign-in could not be verified/);
    expect(failureText({ status: 500, errorCode: null, title: null, detail: null, problems: [], fieldErrors: [], signInFailed: false })).toBe('The change was not saved.');
  });

  it('a 2xx whose body is not what the route returns is a failure, never a reported change', async () => {
    for (const body of [null, [], 'ok', {}]) {
      const { client } = api(body);
      expect((await client.assignProcedure('matters', REC, { procedureId: 'P-1', mode: 'FollowLatest' })).ok).toBe(false);
      expect((await client.removeOverride('matters', REC, 'r')).ok).toBe(false);
    }
  });
});
