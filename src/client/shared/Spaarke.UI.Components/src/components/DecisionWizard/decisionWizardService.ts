/**
 * decisionWizardService — the three BFF calls the decision wizard makes, over an injected fetch (ADR-012, ADR-028: the host
 * injects `@spaarke/auth`'s `authenticatedFetch`; this package never holds a token).
 *
 *   GET  /api/v1/signals/{id}/decision-plan   task 036   the steps of the wizard
 *   GET  /api/v1/signals/{id}/suppression     task 034   the dismissal count the dismissal step shows
 *   POST /api/v1/signals/{id}/decision        task 043   the ONE write (D-17): one review, one record
 *
 * Failures reach the wizard in two ways and both are handled: the injected fetch THROWS an `ApiError` (problem details
 * parsed) or RETURNS a non-2xx `Response`. The commit route's failure body is the ProblemDetails of
 * `DecisionCommitEndpoints.Problem`; a `Failed` outcome (HTTP 500) carries the D-17 lists of writes that landed.
 *
 * Task: spaarke-ontology-platform-r1, task 058.
 */

import type { ResponseFetchFn } from '../../utils/fetchTypes';
import { isApiError, problemOf } from '../../utils/thrownFetchError';
import type { IThrownProblemDetails } from '../../utils/thrownFetchError';
import { DecisionCommitError } from './decisionWizardTypes';
import type {
  DecisionCommitFailure,
  DecisionCommitRef,
  DecisionCommitResult,
  DecisionCommitStep,
  DecisionPlan,
  DecisionReview,
  DecisionWizardService,
  SuppressionStatus,
} from './decisionWizardTypes';

const SIGNALS = '/api/v1/signals';

export interface DecisionWizardServiceOptions {
  /** The injected fetch. An `AuthenticatedFetchFn` is assignable. */
  fetch: ResponseFetchFn;
  /** The BFF base URL, with or without a trailing slash. */
  baseUrl: string;
}

const asArray = <T>(value: unknown): T[] => (Array.isArray(value) ? (value as T[]) : []);

function failureFrom(
  httpStatus: number,
  problem: IThrownProblemDetails | null,
  fallbackDetail: string
): DecisionCommitFailure {
  const p = problem ?? {};
  const title = typeof p.title === 'string' && p.title ? p.title : 'The decision was not recorded';
  const detail = typeof p.detail === 'string' && p.detail ? p.detail : fallbackDetail;
  return {
    httpStatus,
    title,
    detail,
    reasonCode: typeof p.reasonCode === 'string' ? p.reasonCode : null,
    correlationId: typeof p.correlationId === 'string' ? p.correlationId : null,
    actionCode: typeof p.actionCode === 'string' ? p.actionCode : null,
    failedStep: typeof p.failedStep === 'number' ? p.failedStep : null,
    landed: asArray<DecisionCommitRef>(p.landed),
    possiblyLanded: asArray<DecisionCommitRef>(p.possiblyLanded),
    steps: asArray<DecisionCommitStep>(p.steps),
    recordMayExist: p.recordMayExist === true,
    sentButNotRecorded: p.sentButNotRecorded === true,
    resumable: p.resumable === true,
    replayed: p.replayed === true,
  };
}

async function readProblem(response: Response): Promise<IThrownProblemDetails | null> {
  try {
    const body: unknown = await response.json();
    return typeof body === 'object' && body !== null ? (body as IThrownProblemDetails) : null;
  } catch {
    return null;
  }
}

/** A plain error for the read routes, carrying the HTTP status when there is one. */
export class DecisionWizardRequestError extends Error {
  readonly httpStatus: number | null;
  constructor(message: string, httpStatus: number | null) {
    super(message);
    this.name = 'DecisionWizardRequestError';
    this.httpStatus = httpStatus;
  }
}

export function createDecisionWizardService({ fetch, baseUrl }: DecisionWizardServiceOptions): DecisionWizardService {
  const root = baseUrl.replace(/\/+$/, '');
  const url = (signalId: string, tail: string) => `${root}${SIGNALS}/${encodeURIComponent(signalId)}/${tail}`;

  async function getJson<T>(address: string, what: string): Promise<T> {
    let response: Response;
    try {
      response = await fetch(address, { method: 'GET', headers: { Accept: 'application/json' } });
    } catch (err) {
      if (isApiError(err)) {
        const detail = problemOf(err)?.detail;
        throw new DecisionWizardRequestError(detail || `${what} could not be loaded (${err.status}).`, err.status);
      }
      throw new DecisionWizardRequestError(`${what} could not be loaded.`, null);
    }
    if (!response.ok) {
      const detail = (await readProblem(response))?.detail;
      throw new DecisionWizardRequestError(
        typeof detail === 'string' && detail ? detail : `${what} could not be loaded (${response.status}).`,
        response.status
      );
    }
    return (await response.json()) as T;
  }

  return {
    getPlan: signalId => getJson<DecisionPlan>(url(signalId, 'decision-plan'), 'The decision plan'),

    getSuppression: signalId => getJson<SuppressionStatus>(url(signalId, 'suppression'), 'The dismissal history'),

    async commit(signalId: string, review: DecisionReview): Promise<DecisionCommitResult> {
      const init: RequestInit = {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify(review),
      };
      let response: Response;
      try {
        response = await fetch(url(signalId, 'decision'), init);
      } catch (err) {
        if (isApiError(err)) {
          throw new DecisionCommitError(
            failureFrom(err.status, problemOf(err), err.message || 'The decision was not recorded.')
          );
        }
        // No answer at all (httpStatus 0): the request may or may not have reached the route. Sending the SAME review
        // again is safe - a repeated review id returns the first outcome and writes nothing more.
        throw new DecisionCommitError({
          ...failureFrom(
            0,
            null,
            'The request could not be completed, so it is not known whether the decision was recorded.'
          ),
          title: 'The decision may not have been recorded',
        });
      }

      if (!response.ok) {
        throw new DecisionCommitError(
          failureFrom(
            response.status,
            await readProblem(response),
            `The decision was not recorded (${response.status}).`
          )
        );
      }
      const body = (await response.json()) as DecisionCommitResult;
      return {
        ...body,
        steps: asArray<DecisionCommitStep>(body.steps),
        followOns: asArray<DecisionCommitRef>(body.followOns),
        signals: asArray(body.signals),
        warnings: asArray<string>(body.warnings),
      };
    },
  };
}
