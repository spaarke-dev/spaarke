/**
 * intakeRegistry - which intake wizard starts which request type, and who may start it (r3 task 052, FR-05).
 *
 * Two jobs, both small:
 *
 *   1. WIZARDS. `INTAKE_WIZARDS` maps a `sprk_requesttype` value to its wizard component and label. Task 052 registers NDA
 *      Assessment and Policy Question; task 053 adds Invention Submission and Trademark Search by appending two entries
 *      here and giving the matching Quick Start actions no other change. A request type with no entry has no wizard yet.
 *
 *   2. VISIBILITY. `isIntakeTypeAvailable(me, requestType)` decides whether an entry point (a Quick Start card, a More
 *      Services entry) is shown. It is driven by `/me/entitlements`, NOT by the plane (review 011 K3): with
 *      `ExternalAccess:PartnerServiceRequestsEnabled` on, a CIAM partner holds `legal-front-door` and sees the entry
 *      points; with it off, it does not. The server enforces the same rule on the submit route (the client is UX only).
 *
 *        - no `legal-front-door` entitlement                   -> hidden (whatever `intakeRequestTypes` says);
 *        - `intakeRequestTypes` is an array (the server spoke) -> shown iff it lists the type (`[]` = none);
 *        - `intakeRequestTypes` is `undefined` (an older server, or the rollout fallback in me-client.ts) -> TODAY'S RULE:
 *          NDA, Policy Question and Trademark Search are shown to any legal-front-door caller, Invention Submission only
 *          to a workforce caller. `undefined` is never read as "none" and never as "all".
 */
import type * as React from 'react';
import type { MeEntitlementsResponse } from '../api/me-client';
import { INTAKE_REQUEST_TYPE } from '../api/intake-client';
import { NdaAssessmentWizard } from './NdaAssessmentWizard';
import { PolicyQuestionWizard } from './PolicyQuestionWizard';

/** The module entitlement that gates every Legal Front Door entry point (`ModuleEntitlementResolver.LegalFrontDoorModule`). */
export const LEGAL_FRONT_DOOR_ENTITLEMENT = 'legal-front-door';

/** What every intake wizard component takes. A wizard is mounted only while it is wanted, so each open starts fresh. */
export interface IntakeWizardProps {
  open: boolean;
  onClose: () => void;
  /** Opens the Service Requests tab (the success screen's link); omitted = no such button. */
  onOpenServiceRequests?: () => void;
}

export interface IntakeWizardDefinition {
  /** The `sprk_requesttype` option value this wizard submits. */
  requestType: number;
  /** The wizard's name (its title, and the entry point's label). */
  label: string;
  Wizard: React.ComponentType<IntakeWizardProps>;
}

/** The registered wizards. Task 053 appends Invention Submission and Trademark Search. */
export const INTAKE_WIZARDS: readonly IntakeWizardDefinition[] = [
  { requestType: INTAKE_REQUEST_TYPE.ndaAssessment, label: 'NDA Assessment', Wizard: NdaAssessmentWizard },
  { requestType: INTAKE_REQUEST_TYPE.policyQuestion, label: 'Policy Question', Wizard: PolicyQuestionWizard },
];

/** The wizard for a request type, or `undefined` when none is built yet. */
export function getIntakeWizard(requestType: number): IntakeWizardDefinition | undefined {
  return INTAKE_WIZARDS.find(w => w.requestType === requestType);
}

/** The types shown to a legal-front-door caller when the server did not report `intakeRequestTypes`. */
const FALLBACK_TYPES_FOR_ANY_CALLER: readonly number[] = [
  INTAKE_REQUEST_TYPE.ndaAssessment,
  INTAKE_REQUEST_TYPE.policyQuestion,
  INTAKE_REQUEST_TYPE.trademarkSearch,
];

/** Whether `me` may see the entry points for `requestType`. See the file header for the rule. */
export function isIntakeTypeAvailable(me: MeEntitlementsResponse, requestType: number): boolean {
  if (!me.entitlements.includes(LEGAL_FRONT_DOOR_ENTITLEMENT)) return false;
  if (Array.isArray(me.intakeRequestTypes)) return me.intakeRequestTypes.includes(requestType);
  // Not reported (older server / rollout fallback): today's rule.
  if (requestType === INTAKE_REQUEST_TYPE.inventionSubmission) return me.plane === 'workforce';
  return FALLBACK_TYPES_FOR_ANY_CALLER.includes(requestType);
}
