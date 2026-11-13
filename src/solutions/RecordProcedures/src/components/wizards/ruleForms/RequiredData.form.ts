import type { RuleFormDef } from './types';

export const requiredDataForm: RuleFormDef = {
  ruleType: 'RequiredData',
  label: 'Required data',
  fields: [
    { key: 'fields', kind: 'multi', label: "Required fields", help: "The header or line data that must be present. Pick at least one.", required: true, options: [{ value: 'firmInvoiceNumber', label: "Firm invoice number" }, { value: 'invoiceDate', label: "Invoice date" }, { value: 'billingPeriodStart', label: "Billing period start" }, { value: 'billingPeriodEnd', label: "Billing period end" }, { value: 'timekeeper', label: "Timekeeper" }, { value: 'lineDate', label: "Line date" }, { value: 'narrative', label: "Narrative" }, { value: 'taskCode', label: "Task code" }, { value: 'activityCode', label: "Activity code" }, { value: 'expenseCode', label: "Expense code" }] },
  ],
};
