/**
 * Rule set API client (ebilling-r1 task 077, FR-28): every call the Rule Review page makes to the BFF routes under
 * `/api/finance/rulesets` (task 076) and the drafting trigger (task 075), through `authenticatedFetch` (ADR-028).
 *
 * The page never writes Dataverse policy rows itself (ADR-002, DATA-ACCESS-DECISION-CRITERIA): authorization (admin only,
 * D-76), validation, the domain filter and the version rules all live in the server. This module only carries the calls and
 * turns every failure into one {@link ApiFailure} shape.
 *
 * `authenticatedFetch` THROWS on any non-2xx (`ApiError`, with the parsed ProblemDetails) and on an exhausted 401
 * (`AuthError`); it never returns a failed response, so every outcome is decided in the `catch` below, through the
 * `@spaarke/auth` guards (`isApiError`, `problemOf`, `isAuthFailure`).
 */

import { isApiError, isAuthFailure, problemOf, type AuthenticatedFetchFn } from '@spaarke/auth';

// ─────────────────────────────────────────────────────────────────────────────
// Wire types (076's JSON, camelCase)
// ─────────────────────────────────────────────────────────────────────────────

export type RuleSetScope = 'ClientDefault' | 'Firm';
export type RuleSetSourceKind = 'Guideline' | 'RateCard' | 'InternalProcedure';
export type ProposalState = 'Proposed' | 'Accepted' | 'Rejected';
export type Consequence = 'Reject' | 'Flag' | 'SuggestAdjustment';

export interface RuleSetSummary {
  id: string;
  name: string;
  /** `Draft`, `Published` or `Retired`. */
  status: string;
  /** `ClientDefault`, `Firm` or `Unassigned`. */
  scope: string;
  firmId: string | null;
  visibleToFirms: boolean;
  /** `yyyy-MM-dd` or null. */
  effectiveFrom: string | null;
  effectiveTo: string | null;
  ruleCount: number;
}

export interface NotConvertedClause {
  text: string;
  location?: string | null;
  reason: string;
}

export interface ProposalView {
  policyId: string;
  policyCode: string;
  name: string;
  state: ProposalState | string;
  inForce: boolean;
  hasPublishedVersion: boolean;
  versionId: string;
  versionNumber: number;
  ruleType: string | null;
  ruleBody: Record<string, unknown> | null;
  messageTemplate: string | null;
  shortHeadline: string | null;
  consequence: string | null;
  descriptionText: string | null;
  descriptionFormal: string | null;
  descriptionRefusal: string | null;
  clauseQuote: string | null;
  clauseLocation: string | null;
  validationErrors: string[];
  /**
   * The version token of the rule as it was READ with this content. Accept and Reject send it back so a decision is never taken on
   * content the admin was not shown (076 re-check K3). Null in the response to a write: reload the rule set to decide.
   */
  rowVersion: string | null;
  /**
   * Why an admin rejected the draft (D-113): set only while `state` is `Rejected`, otherwise null. Admin-only: the BFF never sends it to a
   * firm, and the page only ever shows it on the draft detail.
   */
  rejectionReason: string | null;
}

export interface RuleSetDetail {
  ruleSet: RuleSetSummary;
  sourceDocumentId: string | null;
  /** Null until a drafting run has finished; `[]` for a run that left nothing out; a raw string if the stored list was not JSON. */
  notConverted: NotConvertedClause[] | string | null;
  proposals: ProposalView[];
}

export interface RuleSetCreated {
  ruleSetId: string;
  sourceDocumentId: string;
  status: string;
  scope: string;
  firmId: string | null;
  sourceKind: string;
  visibleToFirms: boolean;
}

export interface RuleDraftAccepted {
  ruleSetId: string;
  jobId: string;
  statusUrl: string;
}

export interface ProposalEdited {
  proposal: ProposalView;
  createdNewVersion: boolean;
}

export interface PublishedRuleVersion {
  policyId: string;
  versionId: string;
  versionNumber: number;
  supersededVersionId: string | null;
}

export interface RuleSetPublished {
  ruleSetId: string;
  status: string;
  effectiveDate: string;
  published: PublishedRuleVersion[];
  notPublishedProposed: number;
  notPublishedRejected: number;
  clauseQuotesVisibleToFirms: boolean;
  clauseVisibilityNotice: string;
}

/** The members of an edit the page can send. A member left out is left as it is. */
export interface RuleEdit {
  ruleBody?: Record<string, unknown>;
  messageTemplate?: string;
  shortHeadline?: string;
  consequence?: Consequence;
  clauseReference?: string;
  clauseLocation?: string;
}

export interface CreateRuleSetInput {
  name: string;
  sourceKind: RuleSetSourceKind;
  scope: RuleSetScope;
  firmId?: string | null;
  file: File | Blob;
  fileName: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Failures
// ─────────────────────────────────────────────────────────────────────────────

/** One problem with one field of one rule (400 `errors`). */
export interface FieldError {
  policyId: string | null;
  rule: string | null;
  field: string;
  message: string;
  reason: string | null;
}

/** Everything the page needs of a failed call, whatever raised it. */
export interface ApiFailure {
  /** HTTP status; 0 when no response arrived (network, or a token that could not be acquired). */
  status: number;
  /** The stable `errorCode` of ProblemDetails (ADR-019), when there is one. */
  errorCode: string | null;
  /** `reasonCode` of a 403 (`sdap.access.deny.insufficient_privilege`). */
  reasonCode: string | null;
  title: string | null;
  detail: string | null;
  errors: FieldError[];
  /** True when sign-in failed (an exhausted 401 or no token). */
  signInFailed: boolean;
}

export type ApiResult<T> = { ok: true; value: T } | { ok: false; failure: ApiFailure };

/** The longest rejection reason the server accepts (D-113; the column's maximum). */
export const MAX_REJECTION_REASON = 2000;

export const CODE_CONCURRENT_EDIT = 'ruleset.concurrent_edit';
export const CODE_EBILLING_DISABLED = 'ebilling.disabled';
export const CODE_ALREADY_DRAFTED = 'ruleset.already_drafted';

export const isForbidden = (f: ApiFailure): boolean => f.status === 403;

/** `reasonCode` of the billing-admin gate's 403: the caller does not hold the billing administrator right. */
export const REASON_INSUFFICIENT_PRIVILEGE = 'sdap.access.deny.insufficient_privilege';

/**
 * The 403 that means "you are not a billing administrator". Any other 403 (a Procedure's `insufficient_rights`, a `system_failure`)
 * is a refusal of that one call only: it says nothing about the caller's billing-admin right (uac-r2 re-check round 3, F1).
 */
export const isBillingAdminRefusal = (f: ApiFailure): boolean => f.status === 403 && f.reasonCode === REASON_INSUFFICIENT_PRIVILEGE;

/** What the page knows of the caller's right: decided by the server's answers, never guessed. */
export type PageAccess = 'unknown' | 'admin' | 'readOnly';

/** The page's access after a failed call: read-only only on the billing-admin refusal; any other failure leaves it as it was. */
export const accessAfterFailure = (prev: PageAccess, f: ApiFailure): PageAccess => (isBillingAdminRefusal(f) ? 'readOnly' : prev);
export const isEbillingDisabled = (f: ApiFailure): boolean => f.errorCode === CODE_EBILLING_DISABLED;
/** The rule changed after the page showed it (or the set was edited / published meanwhile): reload and look again. */
export const isStale = (f: ApiFailure): boolean => f.status === 409 && f.errorCode === CODE_CONCURRENT_EDIT;

function str(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null;
}

function fieldErrors(value: unknown): FieldError[] {
  if (!Array.isArray(value)) return [];
  return value
    .filter((e): e is Record<string, unknown> => typeof e === 'object' && e !== null)
    .map(e => ({
      policyId: str(e.policyId),
      rule: str(e.rule),
      field: str(e.field) ?? '',
      message: str(e.message) ?? '',
      reason: str(e.reason),
    }));
}

/** Turn whatever `authenticatedFetch` threw into an {@link ApiFailure}. Exported for the tests. */
export function toFailure(err: unknown): ApiFailure {
  if (isApiError(err)) {
    const p = problemOf(err);
    return {
      status: err.status,
      errorCode: str(p?.errorCode),
      reasonCode: str(p?.reasonCode),
      title: str(p?.title),
      detail: str(p?.detail) ?? str(err.message),
      errors: fieldErrors(p?.errors),
      signInFailed: isAuthFailure(err),
    };
  }
  const signIn = isAuthFailure(err);
  return {
    status: 0,
    errorCode: null,
    reasonCode: null,
    title: signIn ? 'Sign-in failed' : 'The request could not be sent',
    detail: err instanceof Error ? err.message : null,
    errors: [],
    signInFailed: signIn,
  };
}

/** One line for a failure: the server's detail, with its stable code, as the page shows it. */
export function describeFailure(f: ApiFailure): string {
  const text = f.detail ?? f.title ?? (f.status ? `The server answered ${f.status}.` : 'The request failed.');
  return f.errorCode ? `${text} (${f.errorCode})` : text;
}

// ─────────────────────────────────────────────────────────────────────────────
// Client
// ─────────────────────────────────────────────────────────────────────────────

export interface RuleSetApi {
  list(): Promise<ApiResult<RuleSetSummary[]>>;
  get(ruleSetId: string): Promise<ApiResult<RuleSetDetail>>;
  create(input: CreateRuleSetInput): Promise<ApiResult<RuleSetCreated>>;
  draft(ruleSetId: string): Promise<ApiResult<RuleDraftAccepted>>;
  edit(ruleSetId: string, policyId: string, edit: RuleEdit): Promise<ApiResult<ProposalEdited>>;
  /** `rowVersion` is the token the page was shown for this rule ({@link ProposalView.rowVersion}). */
  accept(ruleSetId: string, policyId: string, rowVersion: string): Promise<ApiResult<ProposalView>>;
  /** `reason` is required (D-113): the trimmed text, 1 to {@link MAX_REJECTION_REASON} characters; the server answers 400 otherwise. */
  reject(ruleSetId: string, policyId: string, rowVersion: string, reason: string): Promise<ApiResult<ProposalView>>;
  publish(ruleSetId: string, effectiveDate: string): Promise<ApiResult<RuleSetPublished>>;
  assign(ruleSetId: string, scope: RuleSetScope, firmId: string | null): Promise<ApiResult<RuleSetSummary>>;
  setVisibility(ruleSetId: string, visibleToFirms: boolean): Promise<ApiResult<RuleSetSummary>>;
}

const ROUTE = '/api/finance/rulesets';

export function createRuleSetApi(fetchFn: AuthenticatedFetchFn, bffBaseUrl: string): RuleSetApi {
  const base = `${bffBaseUrl.replace(/\/+$/, '')}${ROUTE}`;
  const enc = encodeURIComponent;

  async function call<T>(url: string, init?: RequestInit): Promise<ApiResult<T>> {
    try {
      const res = await fetchFn(url, init);
      return { ok: true, value: (await res.json()) as T };
    } catch (err) {
      return { ok: false, failure: toFailure(err) };
    }
  }

  const json = (method: string, body: unknown): RequestInit => ({
    method,
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });

  return {
    list: () => call<RuleSetSummary[]>(base),
    get: id => call<RuleSetDetail>(`${base}/${enc(id)}`),

    create(input) {
      // Metadata in the query string, the file in the multipart body (076 decision 9).
      const query = new URLSearchParams({ name: input.name, sourceKind: input.sourceKind, scope: input.scope });
      if (input.scope === 'Firm' && input.firmId) query.set('firmId', input.firmId);
      const form = new FormData();
      form.append('file', input.file, input.fileName);
      return call<RuleSetCreated>(`${base}?${query.toString()}`, { method: 'POST', body: form });
    },

    draft: id => call<RuleDraftAccepted>(`${base}/${enc(id)}/draft`, { method: 'POST' }),

    edit: (id, policyId, edit) =>
      call<ProposalEdited>(`${base}/${enc(id)}/proposals/${enc(policyId)}`, json('PATCH', edit)),

    accept: (id, policyId, rowVersion) =>
      call<ProposalView>(`${base}/${enc(id)}/proposals/${enc(policyId)}/accept`, json('POST', { rowVersion })),
    reject: (id, policyId, rowVersion, reason) =>
      call<ProposalView>(`${base}/${enc(id)}/proposals/${enc(policyId)}/reject`, json('POST', { rowVersion, reason })),

    publish: (id, effectiveDate) => call<RuleSetPublished>(`${base}/${enc(id)}/publish`, json('POST', { effectiveDate })),

    assign: (id, scope, firmId) =>
      call<RuleSetSummary>(`${base}/${enc(id)}/assignment`, json('PUT', { scope, firmId: scope === 'Firm' ? firmId : null })),

    setVisibility: (id, visibleToFirms) =>
      call<RuleSetSummary>(`${base}/${enc(id)}/visibility`, json('PUT', { visibleToFirms })),
  };
}
