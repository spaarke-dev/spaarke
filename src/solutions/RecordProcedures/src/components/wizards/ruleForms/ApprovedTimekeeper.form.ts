import type { RuleFormDef } from './types';

export const approvedTimekeeperForm: RuleFormDef = {
  ruleType: 'ApprovedTimekeeper',
  label: 'Approved timekeeper',
  fields: [],
  noChange: "This rule has no values to change: the approved timekeepers are reference data. Exclude the rule for this record instead.",
};
