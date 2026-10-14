/**
 * decisionWizardService - the three BFF calls over an injected fetch (task 058). The stated contract:
 *  - the plan, suppression and commit routes are the ones tasks 036 / 034 / 043 shipped
 *  - the commit is ONE POST with the review as JSON (D-17)
 *  - every non-200 answer rejects with a DecisionCommitError that carries the D-17 lists of writes that landed, whether the
 *    injected fetch THROWS an ApiError (the real `@spaarke/auth` fetch) or RETURNS a non-2xx Response
 *  - the 200 body's numeric `status` (no global string enum converter) does not disturb the result
 */

import { createDecisionWizardService, DecisionWizardRequestError } from '../decisionWizardService';
import { DecisionCommitError } from '../decisionWizardTypes';
import type { DecisionReview } from '../decisionWizardTypes';
import { PATH_B_ID, pathBPlan } from '../__fixtures__/decisionWizardFixtures';

const BASE = 'https://bff.example/';
const review: DecisionReview = {
  reviewId: 'r-1',
  confirmed: true,
  actions: [{ code: 'send-budget-inquiry', taken: false }],
};

// A Response-shaped double (jsdom has no Response): the service reads `ok`, `status` and `json()`.
const json = (body: unknown, status = 200): Response =>
  ({ ok: status >= 200 && status < 300, status, json: async () => body }) as unknown as Response;

/** Shaped like `@spaarke/auth`'s ApiError (structural, by name + fields). */
function apiError(status: number, problemDetails: Record<string, unknown>): Error {
  return Object.assign(new Error(String(problemDetails.detail ?? 'failed')), {
    name: 'ApiError',
    status,
    problemDetails,
  });
}

const landedProblem = {
  title: 'The decision was not recorded',
  detail: 'Step 3 failed.',
  reasonCode: 'ontology.decision.step_failed',
  correlationId: 'trace-1',
  actionCode: 'revise-budget',
  failedStep: 3,
  landed: [{ entity: 'sprk_budgetrevision', id: 'x-1' }],
  possiblyLanded: [{ entity: 'email', id: 'm-1' }],
  steps: [{ key: 'k', code: 'send-budget-inquiry', phase: 2, status: 'Done', written: [], possiblyWritten: [] }],
  recordMayExist: false,
  sentButNotRecorded: true,
  resumable: false,
  replayed: false,
};

describe('reads', () => {
  it('GETs the plan and the suppression from the signal routes', async () => {
    const fetch = jest.fn(async (url: string) =>
      json(url.endsWith('decision-plan') ? pathBPlan : { signalId: PATH_B_ID, threshold: 3 })
    );
    const service = createDecisionWizardService({ fetch, baseUrl: BASE });
    expect((await service.getPlan(PATH_B_ID)).actions).toHaveLength(3);
    await service.getSuppression(PATH_B_ID);
    expect(fetch.mock.calls.map(c => c[0])).toEqual([
      `https://bff.example/api/v1/signals/${PATH_B_ID}/decision-plan`,
      `https://bff.example/api/v1/signals/${PATH_B_ID}/suppression`,
    ]);
  });

  it('rejects a thrown ApiError and a returned 404 with a request error carrying the status', async () => {
    const thrown = createDecisionWizardService({
      fetch: jest.fn().mockRejectedValue(apiError(404, { detail: 'Not found.' })),
      baseUrl: BASE,
    });
    await expect(thrown.getPlan(PATH_B_ID)).rejects.toMatchObject({ httpStatus: 404, message: 'Not found.' });
    const returned = createDecisionWizardService({
      fetch: jest.fn().mockResolvedValue(json({ detail: 'Gone.' }, 404)),
      baseUrl: BASE,
    });
    await expect(returned.getPlan(PATH_B_ID)).rejects.toBeInstanceOf(DecisionWizardRequestError);
  });
});

describe('commit', () => {
  it('sends exactly one POST with the review as JSON and returns the 200 body', async () => {
    const fetch = jest.fn(async () =>
      json({
        status: 1,
        reviewId: 'r-1',
        decisionRecordId: 'd-1',
        recordClass: 'Judgement',
        steps: [],
        followOns: [],
        signals: [],
        warnings: [],
        replayed: false,
      })
    );
    const service = createDecisionWizardService({ fetch, baseUrl: BASE });
    const result = await service.commit(PATH_B_ID, review);
    expect(fetch).toHaveBeenCalledTimes(1);
    const [url, init] = fetch.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe(`https://bff.example/api/v1/signals/${PATH_B_ID}/decision`);
    expect(init.method).toBe('POST');
    expect(JSON.parse(String(init.body))).toEqual(review);
    expect(result.decisionRecordId).toBe('d-1');
    expect(result.replayed).toBe(false);
  });

  it.each([
    ['a thrown ApiError', () => jest.fn().mockRejectedValue(apiError(500, landedProblem))],
    ['a returned non-2xx response', () => jest.fn().mockResolvedValue(json(landedProblem, 500))],
  ])('a failed step (%s) rejects with the writes that landed and no record', async (_name, makeFetch) => {
    const service = createDecisionWizardService({ fetch: makeFetch(), baseUrl: BASE });
    const error = await service.commit(PATH_B_ID, review).catch((e: unknown) => e);
    expect(error).toBeInstanceOf(DecisionCommitError);
    const failure = (error as DecisionCommitError).failure;
    expect(failure.httpStatus).toBe(500);
    expect(failure.failedStep).toBe(3);
    expect(failure.landed).toEqual([{ entity: 'sprk_budgetrevision', id: 'x-1' }]);
    expect(failure.possiblyLanded).toEqual([{ entity: 'email', id: 'm-1' }]);
    expect(failure.sentButNotRecorded).toBe(true);
    expect(failure.resumable).toBe(false);
    expect(failure.reasonCode).toBe('ontology.decision.step_failed');
    expect(failure.correlationId).toBe('trace-1');
  });

  it('a refusal (422) carries its reason and lists nothing as landed', async () => {
    const service = createDecisionWizardService({
      fetch: jest
        .fn()
        .mockRejectedValue(
          apiError(422, {
            title: 'Unprocessable Entity',
            detail: 'A budget is required.',
            reasonCode: 'ontology.decision.review_invalid',
          })
        ),
      baseUrl: BASE,
    });
    const failure = await service.commit(PATH_B_ID, review).catch((e: DecisionCommitError) => e.failure);
    expect(failure).toMatchObject({
      httpStatus: 422,
      detail: 'A budget is required.',
      landed: [],
      possiblyLanded: [],
      resumable: false,
    });
  });

  it('no answer at all is reported as httpStatus 0: it is not known whether the decision was recorded', async () => {
    const service = createDecisionWizardService({
      fetch: jest.fn().mockRejectedValue(new TypeError('Failed to fetch')),
      baseUrl: BASE,
    });
    const failure = await service.commit(PATH_B_ID, review).catch((e: DecisionCommitError) => e.failure);
    expect(failure).toMatchObject({ httpStatus: 0, resumable: false, landed: [] });
    expect(failure?.title).toMatch(/may not have been recorded/i);
  });
});
