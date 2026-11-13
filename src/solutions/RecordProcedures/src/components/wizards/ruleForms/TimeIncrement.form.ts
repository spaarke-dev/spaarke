import type { RuleFormDef } from './types';

export const timeIncrementForm: RuleFormDef = {
  ruleType: 'TimeIncrement',
  label: 'Time increment',
  fields: [
    { key: 'incrementHours', kind: 'number', label: "Increment (hours)", help: "The billing increment in hours, for example 0.1 (six minutes). More than 0, at most 1.", required: true, min: 0, minExclusive: true, max: 1 },
  ],
};
