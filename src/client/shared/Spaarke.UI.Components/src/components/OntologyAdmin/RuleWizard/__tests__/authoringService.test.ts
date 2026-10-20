/**
 * authoringService (task 102; D-22, D-149, task 102a contract): the five authoring routes, called as the caller with the right verbs and
 * bodies; a failure is a result, never a throw, whether the fetch THROWS (an ApiError) or RETURNS a non-2xx; and both refusal shapes
 * (validate's 200 body, the 400 ValidationProblem) give the same `Record<field, messages[]>` with a reason code's message on its field.
 */

import {
  ONTOLOGY_ADMIN_AUTHORING_ROUTE,
  REASON_COLUMN_NOT_AUTHORABLE,
  REASON_DRY_RUN_REQUIRED,
  REASON_DUPLICATE_ACTION,
  REASON_POLICY_CHANGED,
  REASON_POLICY_CODE_TAKEN,
  REASON_POLICY_RETIRED,
  REASON_OWNER_UNAVAILABLE,
  REASON_RULE_HELD_SECURE,
  REASON_SCOPE_INTENT_REQUIRED,
  REASON_SCOPE_MATTER_SECURED,
  REASON_SCOPE_SUBJECT_MISMATCH,
  createOntologyAuthoringService,
  fieldErrorsOfValidation,
} from '../authoringService';
import { catalog, validation } from './fixtures';

const ok = (json: unknown, status = 200) => ({ ok: true, status, json: async () => json }) as unknown as Response;
const bad = (status: number, json: unknown) => ({ ok: false, status, json: async () => json }) as unknown as Response;
const unreadable = (status: number) =>
  ({
    ok: false,
    status,
    json: async () => {
      throw new Error('not json');
    },
  }) as unknown as Response;

const apiError = (status: number, problemDetails: unknown) =>
  Object.assign(new Error(`HTTP ${status}`), { name: 'ApiError', status, problemDetails });

const draft = {
  ruleType: 'Existence',
  ruleBody: '{"type":"Existence"}',
  messageTemplate: 'm',
  shortHeadline: 'h',
  proposedAction: 'p',
  decisionPlan: '{"actions":[],"nextSteps":[]}',
};

describe('createOntologyAuthoringService - the routes', () => {
  it('calls the five routes on the base URL with the right verbs and bodies', async () => {
    const fetch = jest.fn(async () => ok({}));
    const service = createOntologyAuthoringService({ fetch, baseUrl: 'https://bff.example/' });

    await service.getCatalog();
    await service.validate({ lane: 'Decide', draft });
    await service.dryRun({ lane: 'Decide', ruleType: 'Existence', ruleBody: '{}', messageTemplate: 'm' });
    await service.savePolicy({
      policyCode: 'POL-X',
      name: 'X',
      shortName: null,
      lane: 'Decide',
      severity: null,
      workType: null,
      priority: null,
      countsTowardSuppression: true,
      draft,
    });
    await service.publishVersion('a b', { draft });

    const calls = fetch.mock.calls as unknown as Array<[string, RequestInit]>;
    const root = `https://bff.example${ONTOLOGY_ADMIN_AUTHORING_ROUTE}`;
    expect(calls.map(c => [c[1].method, c[0]])).toEqual([
      ['GET', `${root}/catalog`],
      ['POST', `${root}/validate`],
      ['POST', `${root}/dry-run`],
      ['POST', `${root}/policies`],
      ['POST', `${root}/policies/a%20b/versions`],
    ]);
    expect(JSON.parse(calls[1][1].body as string)).toEqual({ lane: 'Decide', draft });
    expect(JSON.parse(calls[4][1].body as string)).toEqual({ draft });
  });

  it('reads the catalog the BFF returns, and sends the rule body and plan exactly as the caller built them', async () => {
    const fetch = jest.fn(async (_url: string, init?: RequestInit) => (init?.method === 'GET' ? ok(catalog) : ok({})));
    const service = createOntologyAuthoringService({ fetch, baseUrl: 'https://bff.example' });

    const answer = await service.getCatalog();
    expect(answer.ok && answer.data.lanes.map(l => l.lane)).toEqual(['Decide', 'Do']);

    await service.validate({ lane: 'Do', draft });
    const sent = JSON.parse((fetch.mock.calls[1][1] as RequestInit).body as string) as { draft: typeof draft };
    expect(sent.draft.ruleBody).toBe(draft.ruleBody);
    expect(sent.draft.decisionPlan).toBe(draft.decisionPlan);
  });
});

describe('createOntologyAuthoringService - failures are results', () => {
  it('reads a RETURNED 400 ValidationProblem: field errors by field, the reason code, and the first message', async () => {
    const service = createOntologyAuthoringService({
      fetch: jest.fn(async () =>
        bad(400, {
          detail: 'x',
          reasonCode: REASON_COLUMN_NOT_AUTHORABLE,
          errors: { ruleBody: ['Column zz_secret is not allowed.'], messageTemplate: ['Bad token.'] },
        })
      ),
      baseUrl: 'https://bff.example',
    });

    const answer = await service.dryRun({
      lane: 'Decide',
      ruleType: 'Existence',
      ruleBody: '{}',
      messageTemplate: 'm',
    });

    expect(answer).toMatchObject({
      ok: false,
      status: 400,
      reasonCode: REASON_COLUMN_NOT_AUTHORABLE,
      outcomeUnknown: false,
      fieldErrors: { ruleBody: ['Column zz_secret is not allowed.'], messageTemplate: ['Bad token.'] },
    });
    expect(answer.ok ? '' : answer.message).toBe('Column zz_secret is not allowed.');
  });

  it('reads a THROWN ApiError (what @spaarke/auth throws for a non-2xx) the same way as a returned one', async () => {
    const problem = { reasonCode: REASON_DUPLICATE_ACTION, errors: { decisionPlan: ['Each code once.'] } };
    const thrown = createOntologyAuthoringService({
      fetch: jest.fn(async () => {
        throw apiError(400, problem);
      }),
      baseUrl: 'https://bff.example',
    });
    const returned = createOntologyAuthoringService({
      fetch: jest.fn(async () => bad(400, problem)),
      baseUrl: 'https://bff.example',
    });

    const a = await thrown.publishVersion('p', { draft });
    const b = await returned.publishVersion('p', { draft });

    expect(a).toEqual(b);
    expect(a).toMatchObject({ ok: false, status: 400, fieldErrors: { decisionPlan: ['Each code once.'] } });
  });

  it("puts the reason code's own message on its field when the 400 body lists none", async () => {
    const service = createOntologyAuthoringService({
      fetch: jest.fn(async () => bad(400, { reasonCode: REASON_COLUMN_NOT_AUTHORABLE, errors: {} })),
      baseUrl: 'https://bff.example',
    });

    const answer = await service.savePolicy({
      policyCode: 'POL-X',
      name: 'X',
      shortName: null,
      lane: 'Decide',
      severity: null,
      workType: null,
      priority: null,
      countsTowardSuppression: true,
      draft,
    });

    expect(answer.ok).toBe(false);
    if (!answer.ok) {
      expect(answer.fieldErrors.ruleBody).toHaveLength(1);
      expect(answer.fieldErrors.ruleBody[0]).toMatch(/column/i);
    }
  });

  it('puts a duplicate-action refusal on decisionPlan even when the body names only the code', async () => {
    const service = createOntologyAuthoringService({
      fetch: jest.fn(async () => bad(400, { reasonCode: REASON_DUPLICATE_ACTION })),
      baseUrl: 'https://bff.example',
    });
    const answer = await service.publishVersion('p', { draft });
    expect(!answer.ok && Object.keys(answer.fieldErrors)).toEqual(['decisionPlan']);
  });

  it('puts a scope_subject_mismatch refusal on matterId, the scope field, even when the body names only the code (task 102b)', async () => {
    const service = createOntologyAuthoringService({
      fetch: jest.fn(async () => bad(400, { reasonCode: REASON_SCOPE_SUBJECT_MISMATCH })),
      baseUrl: 'https://bff.example',
    });
    const answer = await service.publishVersion('p', { draft, matterId: 'm-1' });
    expect(!answer.ok && answer.fieldErrors).toEqual({
      matterId: ['A matter limit is allowed only on a rule about matters.'],
    });
    expect(fieldErrorsOfValidation(validation({ valid: false, reasonCode: REASON_SCOPE_SUBJECT_MISMATCH }))).toEqual({
      matterId: ['A matter limit is allowed only on a rule about matters.'],
    });
  });

  it('puts a scope_intent_required refusal on matterId, the scope field (fix round 1, F4)', async () => {
    const service = createOntologyAuthoringService({
      fetch: jest.fn(async () => bad(400, { reasonCode: REASON_SCOPE_INTENT_REQUIRED })),
      baseUrl: 'https://bff.example',
    });
    const answer = await service.publishVersion('p', { draft });
    expect(!answer.ok && answer.fieldErrors).toEqual({ matterId: ['Choose one matter, or All matters.'] });
  });

  it('puts a scope_matter_secured refusal on matterId, the scope field, with or without a message (fix round 4)', async () => {
    const secured =
      'A rule cannot be limited to a Secure or Restricted matter, or to one whose access level could not be read.';
    const codeOnly = createOntologyAuthoringService({
      fetch: jest.fn(async () => bad(400, { reasonCode: REASON_SCOPE_MATTER_SECURED })),
      baseUrl: 'https://bff.example',
    });
    const withMessage = createOntologyAuthoringService({
      fetch: jest.fn(async () =>
        bad(400, { reasonCode: REASON_SCOPE_MATTER_SECURED, errors: { matterId: [secured] } })
      ),
      baseUrl: 'https://bff.example',
    });
    for (const service of [codeOnly, withMessage]) {
      const answer = await service.dryRun({
        ruleType: 'Existence',
        ruleBody: '{}',
        messageTemplate: 't',
        matterId: 'm-1',
      } as never);
      expect(!answer.ok && answer.fieldErrors).toEqual({ matterId: [secured] });
    }
    expect(fieldErrorsOfValidation(validation({ valid: false, reasonCode: REASON_SCOPE_MATTER_SECURED }))).toEqual({
      matterId: [secured],
    });
  });

  it('sends the scope as given: the matter id on the body, nothing else', async () => {
    const fetch = jest.fn(async () => ok({}));
    const service = createOntologyAuthoringService({ fetch, baseUrl: 'https://bff.example' });
    await service.dryRun({
      policyId: 'p',
      ruleType: 'Existence',
      ruleBody: '{}',
      messageTemplate: 't',
      matterId: 'm-1',
    });
    await service.publishVersion('p', { draft, matterId: 'm-1' });
    for (const call of fetch.mock.calls as unknown as Array<[string, RequestInit]>) {
      expect(JSON.parse(call[1].body as string)).toMatchObject({ matterId: 'm-1' });
    }
  });

  it.each([
    [REASON_DRY_RUN_REQUIRED, /test/i],
    [REASON_POLICY_CODE_TAKEN, /code/i],
    [REASON_POLICY_RETIRED, /retired/i],
    [REASON_POLICY_CHANGED, /changed/i],
  ])('says in words why a 409 with %s was refused', async (reasonCode, pattern) => {
    const service = createOntologyAuthoringService({
      fetch: jest.fn(async () => bad(409, { reasonCode })),
      baseUrl: 'https://bff.example',
    });
    const answer = await service.publishVersion('p', { draft });
    expect(answer).toMatchObject({ ok: false, status: 409, reasonCode, outcomeUnknown: false });
    expect(answer.ok ? '' : answer.message).toMatch(pattern);
  });

  it("says a rule held by a Secure matter's team must be replaced by a new rule, naming no matter (uac-r2 #2084)", async () => {
    const service = createOntologyAuthoringService({
      fetch: jest.fn(async () => bad(409, { reasonCode: 'ontology.admin.rule_held_secure' })),
      baseUrl: 'https://bff.example',
    });
    const answer = await service.publishVersion('p', { draft, matterId: 'm-1' });
    expect(REASON_RULE_HELD_SECURE).toBe('ontology.admin.rule_held_secure');
    expect(answer).toMatchObject({
      ok: false,
      status: 409,
      reasonCode: REASON_RULE_HELD_SECURE,
      outcomeUnknown: false,
    });
    expect(answer.ok ? '' : answer.message).toBe(
      "This rule is held by a Secure matter's team; publish a new rule instead."
    );
    expect(answer.ok ? '' : answer.message).not.toContain('m-1');
  });

  it('falls back to the generic 409 sentence for a reason code it does not know (an older Console against a newer BFF)', async () => {
    const service = createOntologyAuthoringService({
      fetch: jest.fn(async () => bad(409, { reasonCode: 'ontology.admin.some_future_code' })),
      baseUrl: 'https://bff.example',
    });
    const answer = await service.publishVersion('p', { draft });
    expect(answer.ok ? '' : answer.message).toBe('The rule is in a state that does not allow this change.');
  });

  it('reads 403 and 404 as plain refusals, with nothing changed', async () => {
    const forbidden = createOntologyAuthoringService({
      fetch: jest.fn(async () => bad(403, {})),
      baseUrl: 'https://bff.example',
    });
    const missing = createOntologyAuthoringService({
      fetch: jest.fn(async () => bad(404, {})),
      baseUrl: 'https://bff.example',
    });

    const a = await forbidden.publishVersion('p', { draft });
    const b = await missing.publishVersion('p', { draft });

    expect(a.ok ? '' : a.message).toContain('Ontology Administrator');
    expect(b.ok ? '' : b.message).toMatch(/no longer exists/);
  });

  it('says a WRITE that ended in a 5xx or with no answer may not have been saved; a read says only that it could not be reached', async () => {
    const down = createOntologyAuthoringService({
      fetch: jest.fn(async () => unreadable(503)),
      baseUrl: 'https://bff.example',
    });
    const offline = createOntologyAuthoringService({
      fetch: jest.fn(async () => {
        throw new Error('network');
      }),
      baseUrl: 'https://bff.example',
    });

    const write = await down.publishVersion('p', { draft });
    const noAnswer = await offline.publishVersion('p', { draft });
    const read = await down.getCatalog();
    const check = await offline.validate({ lane: 'Decide', draft });

    expect(write).toMatchObject({ ok: false, status: 503, outcomeUnknown: true });
    expect(write.ok ? '' : write.message).toMatch(/may not have been saved/);
    expect(noAnswer).toMatchObject({ ok: false, status: null, outcomeUnknown: true });
    expect(read).toMatchObject({ ok: false, outcomeUnknown: false });
    expect(read.ok ? '' : read.message).not.toMatch(/may not have been saved/);
    // Validate and the dry run write nothing, so a failure there never claims anything may have been saved.
    expect(check).toMatchObject({ ok: false, outcomeUnknown: false });
  });

  it('says nothing was changed for a publish 503 the server answered before any write (uac-r2 #2084 round 2)', async () => {
    const service = createOntologyAuthoringService({
      fetch: jest.fn(async () =>
        bad(503, {
          reasonCode: 'ontology.admin.owner_unavailable',
          detail: 'The rule service is unavailable. Try again.',
        })
      ),
      baseUrl: 'https://bff.example',
    });
    const other = createOntologyAuthoringService({
      fetch: jest.fn(async () => bad(503, { reasonCode: 'ontology.admin.unavailable' })),
      baseUrl: 'https://bff.example',
    });

    const answer = await service.publishVersion('p', { draft, matterId: 'm-1' });
    const unknown = await other.publishVersion('p', { draft });

    expect(REASON_OWNER_UNAVAILABLE).toBe('ontology.admin.owner_unavailable');
    expect(answer).toMatchObject({
      ok: false,
      status: 503,
      reasonCode: REASON_OWNER_UNAVAILABLE,
      outcomeUnknown: false,
    });
    expect(answer.ok ? '' : answer.message).toBe('Nothing was changed. Try again later.');
    // Must not fire: every other 503 on a write still says the outcome is unknown.
    expect(unknown).toMatchObject({ ok: false, status: 503, outcomeUnknown: true });
    expect(unknown.ok ? '' : unknown.message).toMatch(/may not have been saved/);
  });

  it('carries the id of a policy that was created Off but has no version in force yet', async () => {
    const service = createOntologyAuthoringService({
      fetch: jest.fn(async () =>
        bad(500, { policyId: 'p-1', detail: 'The rule was created Off; publish a version to finish.' })
      ),
      baseUrl: 'https://bff.example',
    });
    const answer = await service.savePolicy({
      policyCode: 'POL-X',
      name: 'X',
      shortName: null,
      lane: 'Decide',
      severity: null,
      workType: null,
      priority: null,
      countsTowardSuppression: true,
      draft,
    });
    expect(answer).toMatchObject({ ok: false, policyId: 'p-1', outcomeUnknown: true });
    expect(answer.ok ? '' : answer.message).toContain('created Off');
  });

  it('says an unresolved caller changed nothing', async () => {
    const service = createOntologyAuthoringService({
      fetch: jest.fn(async () => bad(403, { reasonCode: 'sdap.access.deny.caller_unresolved' })),
      baseUrl: 'https://bff.example',
    });
    const answer = await service.publishVersion('p', { draft });
    expect(answer.ok ? '' : answer.message).toMatch(/could not be matched/);
  });
});

describe('fieldErrorsOfValidation - the 200 shape', () => {
  it('groups the errors by field', () => {
    const errors = fieldErrorsOfValidation(
      validation({
        valid: false,
        errors: [
          { field: 'ruleBody', message: 'a' },
          { field: 'ruleBody', message: 'b' },
          { field: 'messageTemplate', message: 'c' },
        ],
      })
    );
    expect(errors).toEqual({ ruleBody: ['a', 'b'], messageTemplate: ['c'] });
  });

  it('gives a reason code a message on its field when the list names none, and leaves a listed message alone', () => {
    const bare = fieldErrorsOfValidation(validation({ valid: false, reasonCode: REASON_COLUMN_NOT_AUTHORABLE }));
    expect(bare.ruleBody).toHaveLength(1);

    const listed = fieldErrorsOfValidation(
      validation({
        valid: false,
        reasonCode: REASON_COLUMN_NOT_AUTHORABLE,
        errors: [{ field: 'ruleBody', message: 'Their message.' }],
      })
    );
    expect(listed).toEqual({ ruleBody: ['Their message.'] });
  });

  it('shows the same field and the same message for the 200 shape as for the 400 shape', async () => {
    const fromValidate = fieldErrorsOfValidation(
      validation({
        valid: false,
        errors: [{ field: 'decisionPlan', message: 'Each code once.' }],
        reasonCode: REASON_DUPLICATE_ACTION,
      })
    );
    const service = createOntologyAuthoringService({
      fetch: jest.fn(async () =>
        bad(400, { reasonCode: REASON_DUPLICATE_ACTION, errors: { decisionPlan: ['Each code once.'] } })
      ),
      baseUrl: 'https://bff.example',
    });
    const from400 = await service.publishVersion('p', { draft });

    expect(!from400.ok && from400.fieldErrors).toEqual(fromValidate);
  });
});
