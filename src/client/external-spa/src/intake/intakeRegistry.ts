/**
 * intakeRegistry - which intake wizard starts which request type, and who may start it (r3 task 052, FR-05).
 *
 * Two jobs, both small:
 *
 *   1. WIZARDS. `INTAKE_WIZARDS` maps a `sprk_requesttype` value to its wizard component and label. Task 052 registers NDA
 *      Assessment and Policy Question; task 053 adds Invention Submission and Trademark Search, so all four request types
 *      have a wizard. A request type with no entry has no wizard.
 *
 *   2. VISIBILITY. `isIntakeTypeAvailable(me, requestType)` decides whether an entry point (a Quick Start card, a More
 *      Services entry) is shown. The rule itself lives in the leaf module `intakeAvailability.ts` (driven by
 *      `/me/entitlements`, not the plane - review 011 K3), re-exported here; the widget registry imports the same rule
 *      from there so the "Add widget" library cannot disagree with the Quick Start cards (review 052 F3).
 */
import type * as React from 'react';
import { INTAKE_REQUEST_TYPE } from '../api/intake-client';
import { NdaAssessmentWizard } from './NdaAssessmentWizard';
import { PolicyQuestionWizard } from './PolicyQuestionWizard';
import { InventionSubmissionWizard } from './InventionSubmissionWizard';
import { TrademarkSearchWizard } from './TrademarkSearchWizard';

// The visibility rule lives in a leaf module (shared with widgetRegistry); re-exported so existing imports keep working.
export { LEGAL_FRONT_DOOR_ENTITLEMENT, isIntakeTypeAvailable } from './intakeAvailability';

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

/** The registered wizards: one per `sprk_requesttype`. */
export const INTAKE_WIZARDS: readonly IntakeWizardDefinition[] = [
  { requestType: INTAKE_REQUEST_TYPE.ndaAssessment, label: 'NDA Assessment', Wizard: NdaAssessmentWizard },
  { requestType: INTAKE_REQUEST_TYPE.policyQuestion, label: 'Policy Question', Wizard: PolicyQuestionWizard },
  {
    requestType: INTAKE_REQUEST_TYPE.inventionSubmission,
    label: 'Invention Submission',
    Wizard: InventionSubmissionWizard,
  },
  { requestType: INTAKE_REQUEST_TYPE.trademarkSearch, label: 'Trademark Search', Wizard: TrademarkSearchWizard },
];

/** The wizard for a request type, or `undefined` when none is built yet. */
export function getIntakeWizard(requestType: number): IntakeWizardDefinition | undefined {
  return INTAKE_WIZARDS.find(w => w.requestType === requestType);
}
