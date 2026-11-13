import type { RuleFormDef } from './types';

export const coreRecordWindowForm: RuleFormDef = {
  ruleType: 'CoreRecordWindow',
  label: 'Core record window',
  fields: [
    { key: 'graceDays', kind: 'number', label: "Grace days", help: "Days allowed outside the core record open and close dates. 0 to 365.", required: true, integer: true, min: 0, max: 365 },
  ],
};
