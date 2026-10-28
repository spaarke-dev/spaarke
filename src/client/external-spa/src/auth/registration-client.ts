/**
 * Workforce-contact registration and stored-link client (spaarke-SPA-external-access-platform-r3 task 058; ADR-028
 * Amendment A9; design notes/a9-bff-design.md section 5 and 7). It replaces the 056 hand-off to a separate registration
 * service: a workforce contact who signed in with their company account is registered by the customer BFF itself.
 *
 * Two BFF routes, both called through `bffApiCall` (so the active backend's URL and the home token apply, and nothing
 * here builds a URL, a header or a scope of its own):
 *   - POST /api/v1/external/registration     { firstName, lastName, department?, confirmExistingRecord? }
 *   - POST /api/v1/external/workforce/link   { guestAccessToken }
 *
 * The identity (tenant, object id, email) comes from the home token on the BFF side, never from this body: the registration
 * body is closed and carries no email, no tenant and no owner. The guest (Spaarke-tenant) token in the link body is a
 * one-shot proof (see `acquireSpaarkeGuestToken`); it is never logged, stored or put in a URL or header here.
 *
 * Refusals are mapped to a fixed set of reasons; a server message is never shown, so a reason code cannot reach the
 * person as text.
 */

import { bffApiCall } from './bff-client';
import { ApiError } from '../types';

export const REGISTRATION_PATH = '/api/v1/external/registration';
export const WORKFORCE_LINK_PATH = '/api/v1/external/workforce/link';

/** The `/me` refusal that asks a signed-in workforce contact to register (`reasonCode`, with its prefill extensions). */
const REGISTRATION_REQUIRED_REASON = 'sdap.access.registration_required';

// ---------------------------------------------------------------------------
// Problem bodies
// ---------------------------------------------------------------------------

/** The `reasonCode` (else `errorCode`) of a ProblemDetails body carried by an {@link ApiError}, or null. */
function problemReason(err: unknown): string | null {
  const body = problemBody(err);
  if (!body) return null;
  if (typeof body.reasonCode === 'string') return body.reasonCode;
  return typeof body.errorCode === 'string' ? body.errorCode : null;
}

function problemBody(err: unknown): Record<string, unknown> | null {
  if (!(err instanceof ApiError)) return null;
  try {
    const parsed: unknown = JSON.parse(err.message);
    return parsed && typeof parsed === 'object' ? (parsed as Record<string, unknown>) : null;
  } catch {
    return null;
  }
}

/** What the BFF knows about the caller, for prefilling the profile form. Strings only; empty when absent. */
export interface RegistrationPrefill {
  givenName: string;
  familyName: string;
  email: string;
}

/**
 * The prefill carried by a 403 `registration_required` from `GET /api/v1/external/me`, or null when `err` is any other
 * failure. Unknown or non-string extensions become empty strings (the form then starts empty).
 */
export function parseRegistrationRequired(err: unknown): RegistrationPrefill | null {
  if (!(err instanceof ApiError) || err.statusCode !== 403) return null;
  const body = problemBody(err);
  if (!body || body.reasonCode !== REGISTRATION_REQUIRED_REASON) return null;
  const text = (value: unknown) => (typeof value === 'string' ? value : '');
  return { givenName: text(body.givenName), familyName: text(body.familyName), email: text(body.email) };
}

// ---------------------------------------------------------------------------
// Registration
// ---------------------------------------------------------------------------

/** The profile form's values. The email is not here: it comes from the token on the BFF side. */
export interface RegistrationInput {
  firstName: string;
  lastName: string;
  department?: string;
  /** Resubmit after the person confirmed the existing record the BFF found (409 `existing_record_found`). */
  confirmExistingRecord?: boolean;
}

export const MAX_NAME_LENGTH = 50;
export const MAX_DEPARTMENT_LENGTH = 100;

export type RegistrationField = 'firstName' | 'lastName' | 'department';
export type RegistrationFieldErrors = Partial<Record<RegistrationField, string>>;

// Mirrors the BFF's closed rule: no control characters (U+0000-U+001F, U+007F) in any value. A char-code check, not
// a regex with escapes: the external-SPA source scan (ExternalSpaGridViewSelectorGuardTests) refuses unicode escapes.
function hasControlCharacter(value: string): boolean {
  for (let i = 0; i < value.length; i++) {
    const code = value.charCodeAt(i);
    if (code <= 0x1f || code === 0x7f) return true;
  }
  return false;
}

function fieldError(label: string, value: string, max: number, required: boolean): string | undefined {
  const trimmed = value.trim();
  if (required && trimmed.length === 0) return `${label} is required.`;
  if (trimmed.length > max) return `${label} must be at most ${max} characters.`;
  if (hasControlCharacter(trimmed)) return `${label} contains characters that are not allowed.`;
  return undefined;
}

/** The same checks the BFF makes (required names, length limits, no control characters), so a bad form sends nothing. */
export function validateRegistrationInput(input: RegistrationInput): RegistrationFieldErrors {
  const errors: RegistrationFieldErrors = {};
  const first = fieldError('First name', input.firstName, MAX_NAME_LENGTH, true);
  const last = fieldError('Last name', input.lastName, MAX_NAME_LENGTH, true);
  const department = fieldError('Department', input.department ?? '', MAX_DEPARTMENT_LENGTH, false);
  if (first) errors.firstName = first;
  if (last) errors.lastName = last;
  if (department) errors.department = department;
  return errors;
}

/**
 * Why a registration was not completed. Each maps to one screen state:
 *   - existing_record_found           409: the BFF found a contact record for this address; ask the person to confirm it.
 *   - licensed_user_link_required     403: this address belongs to a licensed Spaarke user; link the accounts instead.
 *   - registration_contact_conflict   403: the records disagree; an administrator must resolve it.
 *   - registration_email_missing      403: the sign-in carried no email address.
 *   - contact_not_invited             403: no current invitation names this sign-in; a new invitation is the fix.
 *   - invalid                         400: the BFF refused a value.
 *   - signin                          401: the sign-in is no longer accepted.
 *   - blocked                         any OTHER 403 / 409 deny code (e.g. `registration_home_identity_required`): the
 *                                     server refused on purpose, so a retry can never succeed; an administrator must act.
 *   - unavailable                     5xx or network: try again later.
 */
export type RegistrationRefusal =
  | 'existing_record_found'
  | 'licensed_user_link_required'
  | 'registration_contact_conflict'
  | 'registration_email_missing'
  | 'contact_not_invited'
  | 'invalid'
  | 'signin'
  | 'blocked'
  | 'unavailable';

export type RegistrationResult = { kind: 'ok' } | { kind: 'refused'; reason: RegistrationRefusal };

const REGISTRATION_REASONS: Record<string, RegistrationRefusal> = {
  'sdap.access.existing_record_found': 'existing_record_found',
  'sdap.access.licensed_user_link_required': 'licensed_user_link_required',
  'sdap.access.registration_contact_conflict': 'registration_contact_conflict',
  'sdap.access.registration_email_missing': 'registration_email_missing',
  'sdap.access.deny.contact_not_invited': 'contact_not_invited',
};

function refusalOfRegistration(err: unknown): RegistrationRefusal {
  if (!(err instanceof ApiError)) return 'unavailable';
  if (err.statusCode === 401) return 'signin';
  if (err.statusCode === 400) return 'invalid';
  if (err.statusCode === 403 || err.statusCode === 409) {
    const reason = problemReason(err);
    return (reason && REGISTRATION_REASONS[reason]) || 'blocked';
  }
  return 'unavailable';
}

/** POST the profile. Resolves to `ok` (registered, bound, already registered) or a fixed refusal; never rejects. */
export async function submitRegistration(input: RegistrationInput): Promise<RegistrationResult> {
  const department = input.department?.trim();
  const body = {
    firstName: input.firstName.trim(),
    lastName: input.lastName.trim(),
    ...(department ? { department } : {}),
    ...(input.confirmExistingRecord ? { confirmExistingRecord: true } : {}),
  };
  try {
    await bffApiCall<unknown>(REGISTRATION_PATH, { method: 'POST', body: JSON.stringify(body) });
    return { kind: 'ok' };
  } catch (err) {
    return { kind: 'refused', reason: refusalOfRegistration(err) };
  }
}

// ---------------------------------------------------------------------------
// Stored link
// ---------------------------------------------------------------------------

/**
 * Why a link was not made:
 *   - no_systemuser   the Spaarke-tenant guest has no active licensed user: not a licensed person; show the profile form.
 *   - blocked         the link was refused for any other reason (disabled user, collision, ambiguity, a guest token the
 *                     BFF does not accept) or the environment cannot link at all (500 `link.environment`: the BFF is not
 *                     configured to verify Spaarke-tenant tokens): an administrator must resolve it. Retrying cannot help.
 *   - signin          401: the sign-in is no longer accepted.
 *   - unavailable     any other 5xx or network: try again.
 */
export type LinkRefusal = 'no_systemuser' | 'blocked' | 'signin' | 'unavailable';

export type LinkResult = { kind: 'ok' } | { kind: 'refused'; reason: LinkRefusal };

const NO_SYSTEMUSER_REASON = 'sdap.access.link.no_systemuser';
/** 500: a server CONFIGURATION fault (the environment cannot verify guest tokens); retrying from the browser never fixes it. */
const LINK_ENVIRONMENT_REASON = 'sdap.access.link.environment';

function refusalOfLink(err: unknown): LinkRefusal {
  if (!(err instanceof ApiError)) return 'unavailable';
  if (err.statusCode === 401) return 'signin';
  if (err.statusCode === 403) return problemReason(err) === NO_SYSTEMUSER_REASON ? 'no_systemuser' : 'blocked';
  if (err.statusCode === 400) return 'blocked';
  if (err.statusCode === 500 && problemReason(err) === LINK_ENVIRONMENT_REASON) return 'blocked';
  return 'unavailable';
}

/**
 * POST the guest token so the BFF links the home sign-in to the person's licensed Spaarke user. The token goes in the
 * body ONLY. Resolves to `ok` (linked or already linked) or a fixed refusal; never rejects.
 */
export async function linkWorkforceAccount(guestAccessToken: string): Promise<LinkResult> {
  try {
    await bffApiCall<unknown>(WORKFORCE_LINK_PATH, { method: 'POST', body: JSON.stringify({ guestAccessToken }) });
    return { kind: 'ok' };
  } catch (err) {
    return { kind: 'refused', reason: refusalOfLink(err) };
  }
}
