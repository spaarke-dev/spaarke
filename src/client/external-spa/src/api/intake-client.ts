/**
 * intake-client - the Legal Front Door submit over the external plane
 * (spaarke-SPA-external-access-platform-r3 task 052, FR-05).
 *
 *   POST /api/v1/external/servicerequests   one sprk_servicerequest per wizard submission
 *
 * Goes through `bffApiCall` (active backend + active token acquirer), so no BFF URL or scope ever comes from a link.
 *
 * CONTRACT (read from `ExternalIntakeEndpoints.cs` / `ExternalIntakeDtos.cs`, not from docs):
 *   - Body: `{ requestType, title, answers }`. `requestType` is a `sprk_requesttype` option value; `title` is required and
 *     at most 850 characters (it becomes `sprk_name`, which is NOT field-secured); `answers` is a JSON OBJECT of at most
 *     100 KB (UTF-8), stored as `sprk_intakedata`. The DTO is closed: a requester, owner or creator in the body never binds.
 *   - The requester is the caller's contact; `requesterDepartment` is a SYSTEM key the server writes into the stored
 *     answers (owner decision 2026-10-12) and drops from a client body, so this client never sends it.
 *   - 201 `{ sprk_servicerequestid }`. Errors are ProblemDetails: 403 module / type / no contact (`reasonCode`, and
 *     `errorCode` for the type deny), 400 validation (`errorCode`), 413 body > 256 KB, 429 throttle, 500
 *     `EXT_INTAKE_OWNER_UNRESOLVED`, 503 `EXT_INTAKE_DATAVERSE_UNAVAILABLE` / `EXT_INTAKE_CONTACT_UNREADABLE` (nothing was
 *     submitted, so a retry is safe).
 */
import { bffApiCall } from '../auth/bff-client';
import { accessFailureOf } from '../components/auth/BffAccessState';
import { messageProblemOf } from './messages-client';

/** The `sprk_requesttype` option values (`ExternalDataService.ServiceRequestTypes`). */
export const INTAKE_REQUEST_TYPE = {
  ndaAssessment: 100000000,
  inventionSubmission: 100000001,
  policyQuestion: 100000002,
  trademarkSearch: 100000003,
} as const;

/** The `schemaVersion` system key every wizard writes into `answers` (design note section 5.3). */
export const INTAKE_SCHEMA_VERSION = 1;

/** `ExternalIntakeEndpoints.MaxTitleLength` (`sprk_name` is NVARCHAR(850)). */
export const MAX_INTAKE_TITLE_LENGTH = 850;

/** `ExternalIntakeEndpoints.MaxAnswersBytes`: 100 KB of UTF-8 JSON. */
export const MAX_INTAKE_ANSWERS_BYTES = 100 * 1024;

/** One wizard submission. */
export interface SubmitServiceRequestInput {
  requestType: number;
  /** Becomes the unsecured `sprk_name`: only what the design note allows (never a free-text answer). */
  title: string;
  /** A flat JSON object: `schemaVersion`, codes not labels, no keys for empty or hidden fields. */
  answers: Record<string, unknown>;
}

/** The 201 body. */
export interface ServiceRequestCreated {
  sprk_servicerequestid: string;
}

/** A submission refused locally, before any request: its message is already user-facing. */
export class IntakeInputError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'IntakeInputError';
  }
}

/**
 * POST the submission. Refuses locally (no request sent) a title or answers the server would answer 400 to, so the
 * wizard's own caps are the only thing a requester meets.
 */
export function submitServiceRequest(input: SubmitServiceRequestInput): Promise<ServiceRequestCreated> {
  const title = input.title.trim();
  if (title.length === 0 || title.length > MAX_INTAKE_TITLE_LENGTH) {
    return Promise.reject(
      new IntakeInputError(`The request title must be 1 to ${MAX_INTAKE_TITLE_LENGTH} characters.`)
    );
  }
  const body = JSON.stringify({ requestType: input.requestType, title, answers: input.answers });
  if (new TextEncoder().encode(JSON.stringify(input.answers)).length > MAX_INTAKE_ANSWERS_BYTES) {
    return Promise.reject(new IntakeInputError('The answers are too long to submit. Shorten them and try again.'));
  }
  return bffApiCall<ServiceRequestCreated>('/api/v1/external/servicerequests', { method: 'POST', body });
}

/** Shown when the submit is refused for want of a valid session (review 052 K3); the answers stay in the wizard. */
export const SESSION_EXPIRED_MESSAGE =
  'Your session has expired. Sign in again, then submit. Your answers have been kept.';

/** `ExternalIntakeEndpoints.DenyInventionNotAvailable` (also sent as `errorCode`). */
export const DENY_TYPE_NOT_AVAILABLE = 'sdap.external.intake.invention_not_available';

/**
 * The message to show for a failed submit. The server's ProblemDetails `detail` is user-facing text written for exactly
 * the refusal (never exception text), so it is shown when present; otherwise a message per status. Never throws.
 */
export function intakeErrorMessage(err: unknown): string {
  if (err instanceof IntakeInputError) return err.message;
  // A 401 (after bff-client's one forced-refresh retry), a failed Teams token or a sign-in that cannot be started: the
  // session, not the request, is the problem. The wizard keeps the answers, so signing in again loses nothing.
  if (accessFailureOf(err) === 'signin') return SESSION_EXPIRED_MESSAGE;
  const problem = messageProblemOf(err);
  switch (problem.status) {
    case 403:
      if (problem.reasonCode === DENY_TYPE_NOT_AVAILABLE || problem.errorCode === DENY_TYPE_NOT_AVAILABLE) {
        return 'This kind of request is not available to you.';
      }
      return problem.detail ?? 'You are not allowed to submit this request.';
    case 400:
      return problem.detail ?? 'Some of your answers were not accepted. Check them and try again.';
    case 413:
      return 'The request is too large to submit. Shorten your answers and try again.';
    case 429:
      return 'You are submitting too quickly. Wait a moment and try again.';
    case 503:
      return 'Nothing was submitted. The service is busy right now; try again shortly.';
    default:
      if (problem.status !== null && problem.detail) return problem.detail;
      return 'The request could not be submitted. Please try again.';
  }
}
