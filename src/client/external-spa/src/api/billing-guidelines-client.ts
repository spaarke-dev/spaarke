/**
 * billing-guidelines-client — the Billing Guidelines view's BFF contract (ebilling-r1 task 135, FR-49, D-66).
 *
 *   GET /api/v1/external/ebilling/billing-guidelines                      the published rules in force today for the caller's firm(s)
 *   GET /api/v1/external/ebilling/billing-guidelines?coreRecordId={guid}  the same, for one matter, project or work assignment
 *
 * The answer is an allow-list: rule sets (named for the firm), each with its scope, version and dates, and its rules (a name, a
 * plain-language description and parameters when the BFF could describe the rule, and the contract clause as the firm may see it). The
 * route is CIAM-only; the client never decides who may see what (NFR-06): it shows what the server returns.
 *
 * Failures: 401 (after bffApiCall's retry) and 403 reject with the ApiError. A 503 `ebilling.disabled` (the kill switch) is told apart
 * by {@link isBillingGuidelinesDisabledError}: the view is then hidden, not shown as an outage. A 404 is a record the caller cannot
 * see (or none), the same answer for both.
 */

import { bffApiCall } from '../auth/bff-client';
import { isEbillingDisabledError } from '@spaarke/ui-components/components/InvoiceSubmission';
import { toInvoiceSubmissionError } from './web-api-client';

const GUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** One parameter of a rule: a label, its value as text, and the unit when the rule type has one. */
export interface BillingGuidelineParameter {
  name: string;
  value: string;
  unit: string | null;
}

/** One rule of a set. A rule the BFF could not describe has `descriptionAvailable: false` and no description or parameters. */
export interface BillingGuidelineRule {
  name: string;
  descriptionAvailable: boolean;
  description: string | null;
  parameters: BillingGuidelineParameter[];
  /** The contract wording, when the firm may see it. */
  clauseQuote: string | null;
  /** Where in the contract (a section), when the firm may see it. */
  sectionReference: string | null;
  /** The date (yyyy-MM-dd) the rule version came into force. */
  inForceFrom: string | null;
}

/** A set of rules under one name, for one firm. `scope` is a display label the BFF decides ("Applies to all records", ...). */
export interface BillingGuidelineSet {
  firmName: string;
  name: string;
  scope: string;
  version: number | null;
  /** yyyy-MM-dd */
  effectiveFrom: string | null;
  /** yyyy-MM-dd; null = open-ended */
  effectiveTo: string | null;
  rules: BillingGuidelineRule[];
}

export interface BillingGuidelinesResponse {
  sets: BillingGuidelineSet[];
}

/** Reads the guidelines in force today. With `coreRecordId`, the guidelines for that record (a record the caller cannot read is a 404). */
export async function getBillingGuidelines(coreRecordId?: string): Promise<BillingGuidelinesResponse> {
  let path = '/api/v1/external/ebilling/billing-guidelines';
  if (coreRecordId !== undefined) {
    if (!GUID_RE.test(coreRecordId)) throw new Error('Invalid record id.');
    path += `?coreRecordId=${encodeURIComponent(coreRecordId)}`;
  }
  const response = await bffApiCall<BillingGuidelinesResponse>(path);
  // A body that is not the contract is a failed read, never an empty list shown as "no guidelines".
  if (!response || !Array.isArray(response.sets)) throw new Error('The billing guidelines answer was not understood.');
  return response;
}

/**
 * True when the error is the eBilling kill switch (503 `ebilling.disabled`): the capability is off in this environment. An outage 503
 * is not it. Reuses the shared detector the invoice routes use.
 */
export function isBillingGuidelinesDisabledError(err: unknown): boolean {
  return isEbillingDisabledError(toInvoiceSubmissionError(err));
}
