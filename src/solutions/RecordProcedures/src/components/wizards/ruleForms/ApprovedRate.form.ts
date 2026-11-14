import type { RuleFormDef } from './types';

export const approvedRateForm: RuleFormDef = {
  ruleType: 'ApprovedRate',
  label: 'Approved rate',
  fields: [
    { key: 'tolerancePercent', kind: 'number', label: "Tolerance (%)", help: "Percent over the approved rate still accepted, 0 to 100 (0 = the approved rate exactly).", required: true, min: 0, max: 100 },
  ],
  // A rate card rule: the rates come from the Procedure Version's rate card, so a record keeps earlier rates by pinning that version.
  noChange: "Rates come from the version's rate card, so this rule is not changed for one record. To keep earlier rates on this record, pin that version of the Procedure.",
};
