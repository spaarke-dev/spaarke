import type { RuleFormDef } from './types';

export const discountForm: RuleFormDef = {
  ruleType: 'Discount',
  label: 'Discount',
  fields: [
    { key: 'discountPercent', kind: 'number', label: "Agreed discount (%)", help: "The agreed discount off the rack rate, in percent. More than 0, less than 100.", required: true, min: 0, minExclusive: true, max: 100, maxExclusive: true },
    { key: 'tolerancePercent', kind: 'number', label: "Tolerance (%)", help: "Percent over the discounted rate still accepted, 0 to 100. Leave empty for 0.", min: 0, max: 100 },
  ],
};
