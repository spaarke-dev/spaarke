/**
 * authoringService - the rule wizard's client of the Ontology admin authoring routes (spaarke-ontology-platform-r1 task 102; D-22,
 * D-149, FR-58):
 *
 *   GET  /api/v1/ontology/admin/authoring/catalog                        the closed catalog every list the wizard shows comes from
 *   POST /api/v1/ontology/admin/authoring/validate                       compile-only check (200 with errors in the body)
 *   POST /api/v1/ontology/admin/authoring/dry-run                        Test: evaluate a draft over current data, record the proof
 *   POST /api/v1/ontology/admin/authoring/policies                       save a new rule (Off), 201
 *   POST /api/v1/ontology/admin/authoring/policies/{id}/versions         publish a new version
 *
 * The wire shapes mirror `Services/Ontology/OntologyAdminAuthoringModels.cs` (camelCase). The client mirrors NO list, code or column
 * (owner decision D-149, option A): everything the wizard offers is read from the catalog and from `validate`.
 *
 * Every route runs AS THE CALLER, so Dataverse enforces the Spaarke Ontology Administrator role on each write; this module holds no
 * role check and no token. It never throws: every failure is a `{ ok: false }` result with a readable message. A failure reaches
 * here two ways and both are handled: the injected fetch THROWS (`@spaarke/auth`'s `authenticatedFetch` throws an `ApiError` on a
 * non-2xx) or RETURNS a non-2xx `Response`.
 *
 * The two refusal shapes carry the same field errors and the same `reasonCode` (task 102a): validate answers 200 with `errors[]` in the
 * body, dry run / save / publish answer 400 with an RFC 7807 `errors` member. `fieldErrorsOfValidation` and `AuthoringFailure.fieldErrors`
 * both give a `Record<field, messages[]>`, so one renderer shows either.
 *
 * The fetch is injected (ADR-012, ADR-028): the host passes its authenticated fetch; this package holds no token.
 */

import type { ResponseFetchFn } from '../../../utils/fetchTypes';
import { isApiError, problemOf } from '../../../utils/thrownFetchError';

export const ONTOLOGY_ADMIN_AUTHORING_ROUTE = '/api/v1/ontology/admin/authoring';

// ---- the reason codes the wizard places on a field (stable BFF codes, task 101 / 102a) ----------------------------------------

export const REASON_COLUMN_NOT_AUTHORABLE = 'ontology.admin.column_not_authorable';
export const REASON_DUPLICATE_ACTION = 'ontology.admin.duplicate_action';
export const REASON_DRY_RUN_REQUIRED = 'ontology.admin.dry_run_required';
export const REASON_POLICY_CODE_TAKEN = 'ontology.admin.policy_code_taken';
export const REASON_POLICY_RETIRED = 'ontology.admin.policy_retired';
export const REASON_POLICY_CHANGED = 'ontology.admin.policy_changed';
export const REASON_CALLER_UNRESOLVED = 'sdap.access.deny.caller_unresolved';
/** Task 102b (D-165): a matter limit on a rule that is not about matters. */
export const REASON_SCOPE_SUBJECT_MISMATCH = 'ontology.admin.scope_subject_mismatch';
/**
 * Task 102b fix round 1 (F4): a test or publish of a rule limited to a matter that says neither which matter nor "all matters", or says
 * both. The wizard always sends one of them; the code is placed on the scope field.
 */
export const REASON_SCOPE_INTENT_REQUIRED = 'ontology.admin.scope_intent_required';
/**
 * Task 102b fix round 4 (uac-r2 E, D-123): a matter limit on a Secure or Restricted matter, or on one whose access level the caller's read
 * did not return. The matter picker searches every matter the caller can read, secure ones included, so the refusal is placed on the scope
 * field.
 */
export const REASON_SCOPE_MATTER_SECURED = 'ontology.admin.scope_matter_secured';
/**
 * uac-r2 #2084 (owner round 102 item 4, A3): a publish of a rule its Secure matter's team holds. The new version would be that team's,
 * so nothing is written. A 409 that names no matter: the rule cannot be changed here, only replaced by a new rule.
 */
export const REASON_RULE_HELD_SECURE = 'ontology.admin.rule_held_secure';
/**
 * uac-r2 #2084 round 2: a publish's 503 when the new version's owner could not be decided. The server asks before any write, so nothing
 * was changed: unlike any other 503 on a write, the outcome is known.
 */
export const REASON_OWNER_UNAVAILABLE = 'ontology.admin.owner_unavailable';

// ---- wire shapes: the catalog -----------------------------------------------------------------------------------------------

export interface CatalogThresholdField {
  field: string;
  min: number;
  max: number;
}

export interface CatalogJoin {
  related: string;
  /** The one verified lookup on the related table. The wizard writes it into the clause; the admin never chooses it. */
  path: string;
  dateOnlyFields: string[];
  authorableFields: string[];
}

export interface CatalogSubject {
  entity: string;
  dateOnlyFields: string[];
  thresholdFields: CatalogThresholdField[];
  joins: CatalogJoin[];
  authorableFields: string[];
}

export interface CatalogOperators {
  comparison: string[];
  threshold: string[];
  rangeLower: string[];
  rangeUpper: string[];
  nullTests: string[];
  maxInListLength: number;
}

export interface CatalogRelativeDates {
  /** Written with a day-count placeholder, for example `now-{N}d`. */
  forms: string[];
  maxDays: number;
}

export interface CatalogLimits {
  maxClauses: number;
  maxRuleBodyLength: number;
  defaultQuietWindowDays: number;
  maxQuietWindowDays: number;
}

export interface CatalogCategory {
  id: string;
  name: string;
  enabled: boolean;
}

export interface CatalogTriage {
  entity: string;
  field: string;
  categories: CatalogCategory[];
}

export interface CatalogAction {
  code: string;
  label: string;
  /** Codes that cannot be combined with this one (mutual). */
  excludes: string[];
}

export interface CatalogChoice {
  value: string;
  label: string;
}

export interface CatalogOption {
  value: number;
  label: string;
}

export interface CatalogLane {
  lane: string;
  actions: CatalogAction[];
  nextSteps: CatalogAction[];
  dismissalReasons: CatalogChoice[];
  severities: CatalogOption[];
  workTypes: CatalogOption[];
}

export interface AuthoringCatalog {
  ruleTypes: string[];
  subjects: CatalogSubject[];
  operators: CatalogOperators;
  relativeDates: CatalogRelativeDates;
  limits: CatalogLimits;
  triage: CatalogTriage;
  lanes: CatalogLane[];
}

// ---- wire shapes: requests --------------------------------------------------------------------------------------------------

/**
 * Task 102b (D-165): the rule's scope, on every authoring request. `matterId` limits the rule to one matter (a rule about matters only);
 * absent means all matters. Only the id is sent: the BFF reads the matter as the caller. The wizard never sends a tenant.
 */
export interface RuleScopeRequest {
  matterId?: string;
}

/**
 * Task 102b fix round 1 (F4): the test and the publish say the scope they mean explicitly: a matter, or `allMatters: true`. A publish of
 * a rule limited to a matter that names neither is refused (`ontology.admin.scope_intent_required`), so a limit is never cleared by
 * omission. Validate (fix round 2, K2) carries the intent the same way, so it refuses what the test and the publish refuse; save carries
 * only `matterId` (a save only ever creates a new rule, which has no stored scope).
 */
export interface RuleScopeIntentRequest extends RuleScopeRequest {
  allMatters?: true;
}

/** A rule version as the wizard authors it. `ruleBody` and `decisionPlan` are JSON text, exactly as they will be stored. */
export interface RuleDraftPayload {
  ruleType: string;
  ruleBody: string;
  messageTemplate: string;
  shortHeadline: string;
  proposedAction: string;
  decisionPlan: string;
}

export interface ValidateRequest extends RuleScopeIntentRequest {
  policyId?: string;
  lane?: string;
  draft: RuleDraftPayload;
}

export interface DryRunRequest extends RuleScopeIntentRequest {
  policyId?: string;
  lane?: string;
  ruleType: string;
  ruleBody: string;
  messageTemplate: string;
}

export interface NewPolicyRequest extends RuleScopeRequest {
  policyCode: string;
  name: string;
  shortName: string | null;
  lane: string;
  severity: number | null;
  workType: number | null;
  priority: number | null;
  countsTowardSuppression: boolean;
  draft: RuleDraftPayload;
}

/** A publish carries the scope the new version is in force with: a matter, or `allMatters: true` (clears a limit the rule had). */
export interface PublishRequest extends RuleScopeIntentRequest {
  draft: RuleDraftPayload;
}

// ---- wire shapes: answers ---------------------------------------------------------------------------------------------------

export interface ValidationError {
  field: string;
  message: string;
}

export interface RuleValidation {
  valid: boolean;
  errors: ValidationError[];
  reasonCode: string | null;
  templateEligibleFields: string[];
}

export interface DryRunRow {
  subjectEntity: string;
  subjectId: string;
  name: string | null;
  number: string | null;
  coreEntity: string | null;
  coreId: string | null;
  /** `open`, `suppressed` or `new`. */
  status: string;
}

export interface DryRunResult {
  rows: DryRunRow[];
  open: number;
  suppressed: number;
  new: number;
  wouldSupersede: number;
  wouldReraise: number;
  notPlaceable: number;
  /** Whether the policy is On (null for a new rule). */
  policyEnabled: boolean | null;
  windowAnchorUtc: string;
  /** Whether the test was recorded for this caller and this exact rule; save and publish need it. */
  recorded: boolean;
  /**
   * Task 102b fix round 1 (F4): for an existing rule, the matter it is limited to as stored when the test ran (null: all matters). The
   * publish is refused (409 policy_changed) if it changes before the publish.
   */
  storedMatterId?: string | null;
  /** The stored matter's name, when the caller can read it (otherwise null: only its id is known). */
  storedMatterName?: string | null;
}

export interface PolicySaved {
  policyId: string;
  policyVersionId: string;
  versionNumber: number;
  enabled: boolean;
}

export interface VersionPublished {
  policyId: string;
  policyVersionId: string;
  versionNumber: number;
  previousVersionId: string | null;
  /** `completed`, `failed` (the nightly run retries) or `notEvaluated` (the rule is Off). */
  reevaluation: string;
  supersededClosed: number;
  raised: number;
}

// ---- results ----------------------------------------------------------------------------------------------------------------

export type FieldErrors = Record<string, string[]>;

export interface AuthoringFailure {
  ok: false;
  /** The HTTP status, or null for a network failure or an unreadable answer. */
  status: number | null;
  reasonCode: string | null;
  /** Readable text for the admin. */
  message: string;
  /** The 400 `errors` member by field (empty for every other status). */
  fieldErrors: FieldErrors;
  /** After a failed save: the policy that was created Off but has no current version yet (publish a version of it to finish). */
  policyId: string | null;
  /** True for a write whose outcome the server did not prove (5xx, or no answer). */
  outcomeUnknown: boolean;
}

export type AuthoringResult<T> = { ok: true; data: T } | AuthoringFailure;

export interface OntologyAuthoringServiceOptions {
  /** The host's authenticated fetch (it may throw on a non-2xx, or return it). */
  fetch: ResponseFetchFn;
  /** The BFF base URL, with or without a trailing slash. */
  baseUrl: string;
}

export interface OntologyAuthoringService {
  getCatalog: () => Promise<AuthoringResult<AuthoringCatalog>>;
  validate: (request: ValidateRequest) => Promise<AuthoringResult<RuleValidation>>;
  dryRun: (request: DryRunRequest) => Promise<AuthoringResult<DryRunResult>>;
  savePolicy: (request: NewPolicyRequest) => Promise<AuthoringResult<PolicySaved>>;
  publishVersion: (policyId: string, request: PublishRequest) => Promise<AuthoringResult<VersionPublished>>;
}

// ---- field errors from either shape -----------------------------------------------------------------------------------------

/** The fields a reason code concerns, for the case where the BFF names the code but lists no message under the field. */
const REASON_FIELD: Readonly<Record<string, { field: string; message: string }>> = {
  [REASON_COLUMN_NOT_AUTHORABLE]: {
    field: 'ruleBody',
    message: 'The rule names a column a rule may not use. Choose columns from the lists the condition offers.',
  },
  [REASON_DUPLICATE_ACTION]: {
    field: 'decisionPlan',
    message: 'Each code may appear once, in actions or in next steps, never twice in one list and never in both.',
  },
  [REASON_POLICY_CODE_TAKEN]: { field: 'policyCode', message: 'Another rule already uses this code.' },
  [REASON_SCOPE_SUBJECT_MISMATCH]: {
    field: 'matterId',
    message: 'A matter limit is allowed only on a rule about matters.',
  },
  [REASON_SCOPE_INTENT_REQUIRED]: {
    field: 'matterId',
    message: 'Choose one matter, or All matters.',
  },
  [REASON_SCOPE_MATTER_SECURED]: {
    field: 'matterId',
    message:
      'A rule cannot be limited to a Secure or Restricted matter, or to one whose access level could not be read.',
  },
};

/** The field a stable reason code belongs on, or null when the code is not about one field. */
export function fieldOfReasonCode(reasonCode: string | null): string | null {
  return reasonCode !== null && reasonCode in REASON_FIELD ? REASON_FIELD[reasonCode].field : null;
}

/**
 * Field errors by field, whichever shape they arrived in. A reason code the BFF returns always has a message on its field: if the
 * body listed none, the code's own message is placed there, so a refusal is never visible only as a code.
 */
function withReasonCode(errors: FieldErrors, reasonCode: string | null): FieldErrors {
  if (reasonCode === null || !(reasonCode in REASON_FIELD)) {
    return errors;
  }
  const { field, message } = REASON_FIELD[reasonCode];
  return field in errors && errors[field].length > 0 ? errors : { ...errors, [field]: [message] };
}

/** The 200 shape (`validate`): `errors[]` of `{ field, message }`, plus the reason code. */
export function fieldErrorsOfValidation(validation: RuleValidation): FieldErrors {
  const byField: FieldErrors = {};
  for (const error of validation.errors) {
    if (!(error.field in byField)) {
      byField[error.field] = [];
    }
    byField[error.field].push(error.message);
  }
  return withReasonCode(byField, validation.reasonCode);
}

function fieldErrorsOfProblem(errors: unknown, reasonCode: string | null): FieldErrors {
  const byField: FieldErrors = {};
  if (typeof errors === 'object' && errors !== null) {
    for (const [field, value] of Object.entries(errors)) {
      if (Array.isArray(value)) {
        const messages = value.filter((m): m is string => typeof m === 'string');
        if (messages.length > 0) {
          byField[field] = messages;
        }
      }
    }
  }
  return withReasonCode(byField, reasonCode);
}

// ---- failures ---------------------------------------------------------------------------------------------------------------

interface Problem {
  detail?: unknown;
  reasonCode?: unknown;
  errors?: unknown;
  policyId?: unknown;
}

/** What a write that ended without a proven outcome says. The only honest answer is "maybe": do not claim it was or was not saved. */
const OUTCOME_UNKNOWN = 'The change may not have been saved. Reload to check.';

/** What a 503 the server answered BEFORE any write says (#2084 round 2: {@link REASON_OWNER_UNAVAILABLE}). */
const NOTHING_CHANGED_TRY_LATER = 'Nothing was changed. Try again later.';

/** True for a write whose outcome the server did not prove: a 5xx or no answer, except a 503 it answered before any write. */
function isOutcomeUnknown(status: number | null, reasonCode: string | null, writes: boolean): boolean {
  return writes && (status === null || status >= 500) && reasonCode !== REASON_OWNER_UNAVAILABLE;
}

const CONFLICT_MESSAGES: Readonly<Record<string, string | undefined>> = {
  [REASON_DRY_RUN_REQUIRED]: 'Run Test on this exact rule before saving it; any edit needs a new test run.',
  [REASON_POLICY_CODE_TAKEN]: 'Another rule already uses this code.',
  [REASON_POLICY_RETIRED]: 'The rule is retired; a retired rule cannot be published.',
  [REASON_POLICY_CHANGED]:
    'The rule changed while this was being saved. Nothing new is in force; reload and try again.',
  [REASON_RULE_HELD_SECURE]: "This rule is held by a Secure matter's team; publish a new rule instead.",
};

function firstMessage(fieldErrors: FieldErrors): string | null {
  for (const messages of Object.values(fieldErrors)) {
    if (messages.length > 0) {
      return messages[0];
    }
  }
  return null;
}

function detailOf(problem: Problem | null): string | null {
  return typeof problem?.detail === 'string' && problem.detail.length > 0 ? problem.detail : null;
}

function messageFor(
  status: number | null,
  reasonCode: string | null,
  problem: Problem | null,
  fieldErrors: FieldErrors,
  writes: boolean
): string {
  if (reasonCode === REASON_CALLER_UNRESOLVED) {
    return 'Your sign-in could not be matched to a Dataverse user, so nothing was changed.';
  }
  if (status === 400) {
    return firstMessage(fieldErrors) ?? detailOf(problem) ?? 'The request was not accepted.';
  }
  if (status === 403) {
    return (
      detailOf(problem) ??
      'Dataverse refused this for your account. It needs the Spaarke Ontology Administrator role. Nothing was changed.'
    );
  }
  if (status === 404) {
    return 'That rule no longer exists, or it is not an Ontology rule. Nothing was changed.';
  }
  if (status === 409) {
    return (
      (reasonCode !== null ? CONFLICT_MESSAGES[reasonCode] : undefined) ??
      detailOf(problem) ??
      'The rule is in a state that does not allow this change.'
    );
  }
  if (reasonCode === REASON_OWNER_UNAVAILABLE) {
    return NOTHING_CHANGED_TRY_LATER;
  }
  if (isOutcomeUnknown(status, reasonCode, writes)) {
    // A saved-Off policy whose first version could not be made current (K6) carries its own, more exact, sentence.
    return typeof problem?.policyId === 'string' ? (detailOf(problem) ?? OUTCOME_UNKNOWN) : OUTCOME_UNKNOWN;
  }
  return 'The rule service could not be reached. Try again.';
}

function failure(status: number | null, problem: Problem | null, writes: boolean): AuthoringFailure {
  const reasonCode = typeof problem?.reasonCode === 'string' ? problem.reasonCode : null;
  const fieldErrors = status === 400 ? fieldErrorsOfProblem(problem?.errors, reasonCode) : {};
  return {
    ok: false,
    status,
    reasonCode,
    message: messageFor(status, reasonCode, problem, fieldErrors, writes),
    fieldErrors,
    policyId: typeof problem?.policyId === 'string' ? problem.policyId : null,
    outcomeUnknown: isOutcomeUnknown(status, reasonCode, writes),
  };
}

async function call<T>(
  fetch: ResponseFetchFn,
  url: string,
  init: RequestInit,
  writes: boolean
): Promise<AuthoringResult<T>> {
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

export function createOntologyAuthoringService({
  fetch,
  baseUrl,
}: OntologyAuthoringServiceOptions): OntologyAuthoringService {
  const root = `${baseUrl.replace(/\/+$/, '')}${ONTOLOGY_ADMIN_AUTHORING_ROUTE}`;
  const read = { Accept: 'application/json' };
  const write = { Accept: 'application/json', 'Content-Type': 'application/json' };
  const post = <T>(url: string, body: unknown, writes: boolean) =>
    call<T>(fetch, url, { method: 'POST', headers: write, body: JSON.stringify(body) }, writes);

  return {
    getCatalog: () => call<AuthoringCatalog>(fetch, `${root}/catalog`, { method: 'GET', headers: read }, false),
    // Validate and the dry run write nothing (the dry run records a proof, not a rule), so a failure never says "may not have been saved".
    validate: request => post<RuleValidation>(`${root}/validate`, request, false),
    dryRun: request => post<DryRunResult>(`${root}/dry-run`, request, false),
    savePolicy: request => post<PolicySaved>(`${root}/policies`, request, true),
    publishVersion: (policyId, request) =>
      post<VersionPublished>(`${root}/policies/${encodeURIComponent(policyId)}/versions`, request, true),
  };
}
