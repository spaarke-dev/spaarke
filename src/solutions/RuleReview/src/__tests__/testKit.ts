/**
 * Shared builders for the Rule Review page's logic tests (ebilling-r1 task 077).
 */

import { ApiError, AuthError } from '@spaarke/auth';
import type {
  ApiResult,
  ProposalView,
  RuleSetApi,
  RuleSetDetail,
  RuleSetSummary,
} from '../services/ruleSetApi';

export const SET_ID = '11111111-1111-1111-1111-111111111111';
export const POLICY_ID = '22222222-2222-2222-2222-222222222222';

export function proposal(overrides: Partial<ProposalView> = {}): ProposalView {
  return {
    policyId: POLICY_ID,
    policyCode: 'POL-1',
    name: 'Daily hours cap',
    state: 'Proposed',
    inForce: false,
    hasPublishedVersion: false,
    versionId: 'v-1',
    versionNumber: 1,
    ruleType: 'DailyHoursCap',
    ruleBody: { type: 'DailyHoursCap', maxHoursPerDay: 10 },
    messageTemplate: 'Too many hours',
    shortHeadline: 'Daily cap',
    consequence: 'Flag',
    descriptionText: null,
    descriptionFormal: null,
    descriptionRefusal: null,
    clauseQuote: 'No timekeeper may bill more than 10 hours a day.',
    clauseLocation: 'Section 4.2',
    validationErrors: [],
    rowVersion: '4711',
    rejectionReason: null,
    ...overrides,
  };
}

export function summary(overrides: Partial<RuleSetSummary> = {}): RuleSetSummary {
  return {
    id: SET_ID,
    name: 'Acme guidelines',
    status: 'Draft',
    scope: 'ClientDefault',
    firmId: null,
    visibleToFirms: false,
    effectiveFrom: null,
    effectiveTo: null,
    ruleCount: 1,
    ...overrides,
  };
}

export function detail(proposals: ProposalView[] = [proposal()], overrides: Partial<RuleSetDetail> = {}): RuleSetDetail {
  return { ruleSet: summary(), sourceDocumentId: null, notConverted: [], proposals, ...overrides };
}

export const ok = <T>(value: T): ApiResult<T> => ({ ok: true, value });

export function problem(status: number, errorCode: string | null, extra: Record<string, unknown> = {}): ApiError {
  return new ApiError(`HTTP ${status}`, status, { status, title: 'x', errorCode, ...extra } as never);
}

export { ApiError, AuthError };

/** A RuleSetApi whose every method is a jest mock returning a configurable result. */
export type MockApi = { [K in keyof RuleSetApi]: jest.Mock<ReturnType<RuleSetApi[K]>, Parameters<RuleSetApi[K]>> };

export function mockApi(): MockApi {
  const never = (name: string) => jest.fn().mockRejectedValue(new Error(`${name} not set up`));
  return {
    list: never('list'),
    get: never('get'),
    create: never('create'),
    draft: never('draft'),
    edit: never('edit'),
    accept: never('accept'),
    reject: never('reject'),
    publish: never('publish'),
    assign: never('assign'),
    setVisibility: never('setVisibility'),
  } as unknown as MockApi;
}
