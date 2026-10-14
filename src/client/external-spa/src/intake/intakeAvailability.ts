/**
 * intakeAvailability - who may be offered an intake request type (r3 task 052, FR-05; review 052 F3).
 *
 * A LEAF module: no React, no wizard components. Both `intakeRegistry.ts` (Quick Start / More Services entry points) and
 * `widgetRegistry.ts` (the "Add widget" library, via `EntitlementGate.requiredIntakeType`) import the one rule from
 * here, so the registry does not pull in the wizard components and the two surfaces cannot drift.
 *
 * The rule is driven by `/me/entitlements`, NOT by the plane (review 011 K3): with
 * `ExternalAccess:PartnerServiceRequestsEnabled` on, a CIAM partner holds `legal-front-door`; with it off, it does not.
 * The server enforces the same rule on the submit route (the client is UX only).
 *
 *   - no `legal-front-door` entitlement                   -> not available (whatever `intakeRequestTypes` says);
 *   - `intakeRequestTypes` is an array (the server spoke) -> available iff it lists the type (`[]` = none);
 *   - `intakeRequestTypes` is `undefined` (an older server, or the rollout fallback in me-client.ts) -> TODAY'S RULE:
 *     NDA, Policy Question and Trademark Search are available to any legal-front-door caller, Invention Submission only
 *     to a workforce caller. `undefined` is never read as "none" and never as "all".
 */
import type { MeEntitlementsResponse } from '../api/me-client';
import { INTAKE_REQUEST_TYPE } from '../api/intake-client';

/** The module entitlement that gates every Legal Front Door entry point (`ModuleEntitlementResolver.LegalFrontDoorModule`). */
export const LEGAL_FRONT_DOOR_ENTITLEMENT = 'legal-front-door';

/** The types offered to a legal-front-door caller when the server did not report `intakeRequestTypes`. */
const FALLBACK_TYPES_FOR_ANY_CALLER: readonly number[] = [
  INTAKE_REQUEST_TYPE.ndaAssessment,
  INTAKE_REQUEST_TYPE.policyQuestion,
  INTAKE_REQUEST_TYPE.trademarkSearch,
];

/** Whether `me` may be offered `requestType`. See the file header for the rule. */
export function isIntakeTypeAvailable(me: MeEntitlementsResponse, requestType: number): boolean {
  if (!me.entitlements.includes(LEGAL_FRONT_DOOR_ENTITLEMENT)) return false;
  if (Array.isArray(me.intakeRequestTypes)) return me.intakeRequestTypes.includes(requestType);
  // Not reported (older server / rollout fallback): today's rule.
  if (requestType === INTAKE_REQUEST_TYPE.inventionSubmission) return me.plane === 'workforce';
  return FALLBACK_TYPES_FOR_ANY_CALLER.includes(requestType);
}
