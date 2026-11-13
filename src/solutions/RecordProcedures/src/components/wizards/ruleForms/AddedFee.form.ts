import type { RuleFormDef } from './types';

/** The server refuses these two allowances until extraction returns a per-line fee kind and the due date (model 6.3; `rule_form_not_supported`). */
const NOT_YET = "The server cannot check this form yet: it needs the fee kind of each invoice line (and the invoice due date). Use a cap.";

export const addedFeeForm: RuleFormDef = {
  ruleType: 'AddedFee',
  label: 'Added fee',
  fields: [
    { key: 'feeKind', kind: 'choice', label: "Fee kind", help: "The kind of added fee the rule is about.", required: true, options: [{ value: 'lateFee', label: "Late fee" }, { value: 'administrativeFee', label: "Administrative fee" }, { value: 'interest', label: "Interest" }, { value: 'other', label: "Other" }] },
    { key: 'allowance', kind: 'choice', label: "Allowance", help: "A cap is the only allowance that is checked today.", required: true, options: [{ value: 'notAllowed', label: "Not allowed", refused: NOT_YET }, { value: 'cap', label: "Capped" }, { value: 'afterDays', label: "Allowed after N days", refused: NOT_YET }] },
    { key: 'feeCodes', kind: 'codes', label: "Fee codes", help: "The expense codes of the invoice lines that carry this fee, separated by commas or spaces.", showWhen: { key: 'allowance', equals: 'cap' } },
    { key: 'capAmount', kind: 'number', label: "Cap (amount)", help: "The most the fee lines may total on one invoice, in the invoice currency. More than 0. Give this or a percent, not both.", min: 0, minExclusive: true, max: 1000000000, showWhen: { key: 'allowance', equals: 'cap' } },
    { key: 'capPercent', kind: 'number', label: "Cap (% of the other lines)", help: "The most the fee lines may total, as a percent of the invoice's other lines. More than 0, at most 100. Give this or an amount, not both.", min: 0, minExclusive: true, max: 100, showWhen: { key: 'allowance', equals: 'cap' } },
    { key: 'afterDays', kind: 'number', label: "Days after the due date", help: "The fee is allowed only this many days after the invoice due date. 1 to 3650.", integer: true, min: 1, max: 3650, showWhen: { key: 'allowance', equals: 'afterDays' } },
  ],
  exactlyOneOf: ['capAmount', 'capPercent'],
};
