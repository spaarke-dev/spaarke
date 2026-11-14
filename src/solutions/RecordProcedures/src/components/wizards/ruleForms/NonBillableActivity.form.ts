import type { RuleFormDef } from './types';

export const nonBillableActivityForm: RuleFormDef = {
  ruleType: 'NonBillableActivity',
  label: 'Non-billable activity',
  fields: [
    { key: 'codes', kind: 'codes', label: "Non-billable codes", help: "Activity or task codes, separated by commas or spaces (for example L110, L120).", required: true },
  ],
};
