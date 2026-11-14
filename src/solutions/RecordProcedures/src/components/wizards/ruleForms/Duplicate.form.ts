import type { RuleFormDef } from './types';

export const duplicateForm: RuleFormDef = {
  ruleType: 'Duplicate',
  label: 'Duplicate',
  fields: [
    { key: 'matchFields', kind: 'multi', label: "Fields compared", help: "Firm invoice number compares invoices; the others compare lines. Pick at least one.", required: true, options: [{ value: 'firmInvoiceNumber', label: "Firm invoice number" }, { value: 'timekeeper', label: "Timekeeper" }, { value: 'lineDate', label: "Line date" }, { value: 'hours', label: "Hours" }, { value: 'narrative', label: "Narrative" }, { value: 'amount', label: "Amount" }] },
  ],
};
