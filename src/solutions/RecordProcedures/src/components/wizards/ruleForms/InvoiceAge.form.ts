import type { RuleFormDef } from './types';

export const invoiceAgeForm: RuleFormDef = {
  ruleType: 'InvoiceAge',
  label: 'Invoice age',
  fields: [
    { key: 'maxAgeDays', kind: 'number', label: "Most days before the invoice date", help: "A line dated more days than this before the invoice date is flagged. 1 to 3650.", required: true, integer: true, min: 1, max: 3650 },
  ],
};
