/**
 * rulesService - the client of the Ontology admin Rules routes (spaarke-ontology-platform-r1 task 100; spec FR-57, D-22):
 *
 *   GET  /api/v1/ontology/admin/rules                    the list
 *   GET  /api/v1/ontology/admin/rules/{id}               the read-only rule page
 *   PUT  /api/v1/ontology/admin/rules/{id}/enabled       On / Off
 *   POST /api/v1/ontology/admin/rules/{id}/retire        Retire with a reason
 *
 * Every route runs AS THE CALLER, so Dataverse enforces the Spaarke Ontology Administrator role on each write; this client holds no
 * role check. It never throws: every failure is a `{ ok: false }` result with a message the admin can read. A failure reaches here
 * two ways and both are handled: the injected fetch THROWS (`@spaarke/auth`'s `authenticatedFetch` throws an `ApiError` on a
 * non-2xx) or RETURNS a non-2xx `Response` (the external SPA's fetch).
 *
 * The fetch is injected (ADR-012, ADR-028): the host passes its authenticated fetch; this package holds no token.
 */

import type { ResponseFetchFn } from '../../../utils/fetchTypes';
import { isApiError, problemOf } from '../../../utils/thrownFetchError';

export const ONTOLOGY_ADMIN_RULES_ROUTE = '/api/v1/ontology/admin/rules';

// ---- wire shapes (mirror Services/Ontology/OntologyAdminRulesModels.cs; camelCase) -------------------------------------------

export interface RuleActionRate {
  acted: number;
  dismissed: number;
  rate: number | null;
  tooFewToJudge: boolean;
}

/**
 * The Signal numbers. The three are read independently by the BFF: a number that could not be read is null here and its flag in
 * `RuleMetricsAvailability` is false. `lastFiredOn` null with its flag true means no Signal in the window, not "unreadable".
 */
export interface RuleMetrics {
  openItems: number | null;
  /** The newest Signal created for the rule in the last `lastFiredWindowDays` days. */
  lastFiredOn: string | null;
  actionRate: RuleActionRate | null;
  needsALook: boolean;
}

export interface RuleMetricsAvailability {
  openItems: boolean;
  lastFired: boolean;
  actionRate: boolean;
}

export interface RuleSummary {
  policyId: string;
  code: string;
  name: string;
  /** "Decide" or "Do". */
  lane: string;
  /** "On", "Off", "Retired" or "Inactive". */
  state: string;
  enabled: boolean;
  versionNumber: number | null;
  headline: string | null;
  severity: string;
  subjectType: string;
  metrics: RuleMetrics;
}

export interface RuleListResponse {
  rules: RuleSummary[];
  /** True only when all three numbers were read. */
  metricsAvailable: boolean;
  metricsAvailability: RuleMetricsAvailability;
  actionRateWindowDays: number;
  /** How far back "last fired" looks. */
  lastFiredWindowDays: number;
  minimumJudged: number;
}

export interface RuleClause {
  clauseId: string;
  kind: string;
  text: string;
  formal: string;
}

export interface RuleCondition {
  ruleType: string;
  clauses: RuleClause[];
  refusal: string | null;
}

export interface RuleWording {
  headline: string | null;
  messageTemplate: string | null;
  proposedAction: string | null;
}

export interface RulePlanAction {
  code: string;
  label: string;
  effectLines: string[];
}

export interface RulePlan {
  actions: RulePlanAction[];
  nextSteps: RulePlanAction[];
  dismissalReasons: string[];
  refusal: string | null;
}

export interface RuleScope {
  tenant: string | null;
  matterId: string | null;
  matterName: string | null;
  priority: number | null;
}

export interface RuleNoise {
  countsTowardSuppression: boolean;
  suppressAfterDismissals: number;
  suppressionWindowDays: number;
  quietWindowDays: number;
}

export interface RuleBehaviour {
  lane: string;
  severity: string;
  workType: string | null;
  subjectType: string;
  state: string;
  retiredReason: string | null;
}

export interface RuleVersion {
  versionId: string;
  number: number;
  headline: string | null;
  authoredBy: string | null;
  createdOn: string | null;
  inForceFrom: string | null;
  inForceTo: string | null;
  inForce: boolean;
}

export interface RuleHistoryEntry {
  when: string | null;
  by: string | null;
  event: string;
}

export interface RuleDetail {
  summary: RuleSummary;
  condition: RuleCondition;
  wording: RuleWording;
  plan: RulePlan;
  scope: RuleScope;
  noise: RuleNoise;
  behaviour: RuleBehaviour;
  versions: RuleVersion[];
  history: RuleHistoryEntry[];
  metricsAvailable: boolean;
  metricsAvailability: RuleMetricsAvailability;
  actionRateWindowDays: number;
  lastFiredWindowDays: number;
  minimumJudged: number;
}

export interface RuleEnabledResult {
  policyId: string;
  enabled: boolean;
}

export interface RuleClosure {
  closed: number;
  changedSinceRead: number;
  failed: number;
  policyFailures: number;
  complete: boolean;
  unavailable: boolean;
}

export interface RuleRetiredResult {
  policyId: string;
  state: string;
  reason: string;
  closure: RuleClosure;
}

// ---- results ----------------------------------------------------------------------------------------------------------------

export interface RulesFailure {
  ok: false;
  /** The HTTP status, or null for a network failure or an unreadable answer. */
  status: number | null;
  reasonCode: string | null;
  /** Readable text for the admin. */
  message: string;
  /**
   * True for a write (On/Off, Retire) whose outcome the server did not prove: a 503, another 5xx, or a network failure. The change may
   * or may not have been saved; the message says so. False for every read, and for a refusal (4xx) where the server proved no write.
   */
  outcomeUnknown: boolean;
}

export type RulesResult<T> = { ok: true; data: T } | RulesFailure;

export interface OntologyRulesServiceOptions {
  /** The host's authenticated fetch (it may throw on a non-2xx, or return it). */
  fetch: ResponseFetchFn;
  /** The BFF base URL, with or without a trailing slash. */
  baseUrl: string;
}

export interface OntologyRulesService {
  list: () => Promise<RulesResult<RuleListResponse>>;
  get: (policyId: string) => Promise<RulesResult<RuleDetail>>;
  setEnabled: (policyId: string, enabled: boolean) => Promise<RulesResult<RuleEnabledResult>>;
  retire: (policyId: string, reason: string) => Promise<RulesResult<RuleRetiredResult>>;
}

// ---- failures ---------------------------------------------------------------------------------------------------------------

interface Problem {
  detail?: unknown;
  reasonCode?: unknown;
  errors?: unknown;
}

/** The first validation message in an RFC 7807 `errors` member, or null. */
function firstValidationMessage(errors: unknown): string | null {
  if (typeof errors !== 'object' || errors === null) {
    return null;
  }
  for (const value of Object.values(errors)) {
    if (Array.isArray(value) && typeof value[0] === 'string') {
      return value[0];
    }
  }
  return null;
}

/** What a write that ended without a proven outcome says. The only honest answer is "maybe": do not claim it was or was not saved. */
const OUTCOME_UNKNOWN = 'The change may not have been saved. Reload to check.';

/**
 * Whether a failed WRITE leaves the outcome unknown. A 4xx is the server's refusal before any write, so nothing changed; a 5xx, or no
 * answer at all (a network failure, an unreadable reply), could have come before or after the write landed.
 */
function isOutcomeUnknown(status: number | null, writes: boolean): boolean {
  return writes && (status === null || status >= 500);
}

/** The text the admin reads for a failure. Never the raw problem JSON. */
function messageFor(status: number | null, reasonCode: string | null, problem: Problem | null, writes: boolean): string {
  if (reasonCode === 'sdap.access.deny.caller_unresolved') {
    return 'Your sign-in could not be matched to a Dataverse user, so nothing was changed.';
  }
  if (status === 403) {
    return 'Dataverse refused this change. It needs the Spaarke Ontology Administrator role. Nothing was changed.';
  }
  if (status === 404) {
    return writes
      ? 'That rule no longer exists, or it is not an Ontology rule. Nothing was changed.'
      : 'That rule no longer exists, or it is not an Ontology rule.';
  }
  if (status === 400 || status === 409) {
    const validation = firstValidationMessage(problem?.errors);
    if (validation !== null) {
      return validation;
    }
    if (typeof problem?.detail === 'string' && problem.detail.length > 0) {
      return problem.detail;
    }
    return status === 409 ? 'The rule is in a state that does not allow this change.' : 'The request was not accepted.';
  }
  if (isOutcomeUnknown(status, writes)) {
    return OUTCOME_UNKNOWN;
  }
  return 'The rules could not be reached. Try again.';
}

function failure(status: number | null, problem: Problem | null, writes: boolean): RulesFailure {
  const reasonCode = typeof problem?.reasonCode === 'string' ? problem.reasonCode : null;
  return {
    ok: false,
    status,
    reasonCode,
    message: messageFor(status, reasonCode, problem, writes),
    outcomeUnknown: isOutcomeUnknown(status, writes),
  };
}

/** `writes` is true for the PUT / POST calls: a failure there may have come after the change was saved. */
async function call<T>(fetch: ResponseFetchFn, url: string, init: RequestInit, writes = false): Promise<RulesResult<T>> {
  try {
    const response = await fetch(url, init);
    if (!response.ok) {
      let problem: Problem | null = null;
      try {
        problem = (await response.json()) as Problem | null;
      } catch {
        problem = null;
      }
      return failure(response.status, problem, writes);
    }
    return { ok: true, data: (await response.json()) as T };
  } catch (error: unknown) {
    if (isApiError(error)) {
      return failure(error.status, problemOf(error), writes);
    }
    return failure(null, null, writes);
  }
}

// ---- the service ------------------------------------------------------------------------------------------------------------

export function createOntologyRulesService({ fetch, baseUrl }: OntologyRulesServiceOptions): OntologyRulesService {
  const root = `${baseUrl.replace(/\/+$/, '')}${ONTOLOGY_ADMIN_RULES_ROUTE}`;
  const read = { Accept: 'application/json' };
  const write = { Accept: 'application/json', 'Content-Type': 'application/json' };
  const one = (policyId: string): string => `${root}/${encodeURIComponent(policyId)}`;

  return {
    list: () => call<RuleListResponse>(fetch, root, { method: 'GET', headers: read }),
    get: policyId => call<RuleDetail>(fetch, one(policyId), { method: 'GET', headers: read }),
    setEnabled: (policyId, enabled) =>
      call<RuleEnabledResult>(
        fetch,
        `${one(policyId)}/enabled`,
        { method: 'PUT', headers: write, body: JSON.stringify({ enabled }) },
        true
      ),
    retire: (policyId, reason) =>
      call<RuleRetiredResult>(
        fetch,
        `${one(policyId)}/retire`,
        { method: 'POST', headers: write, body: JSON.stringify({ reason }) },
        true
      ),
  };
}
