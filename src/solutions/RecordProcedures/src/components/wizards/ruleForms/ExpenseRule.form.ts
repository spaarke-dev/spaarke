import type { RuleFormDef } from './types';

export const expenseRuleForm: RuleFormDef = {
  ruleType: 'ExpenseRule',
  label: 'Expense rule',
  fields: [
    { key: 'nonReimbursableCodes', kind: 'codes', label: "Non-reimbursable expense codes", help: "Expense codes that are not reimbursable, separated by commas or spaces. Leave empty to use only a cap." },
    { key: 'maxAmountPerItem', kind: 'number', label: "Most per expense item", help: "The most one expense item may be, in the invoice currency. More than 0. Leave empty to use only the codes.", min: 0, minExclusive: true, max: 1000000000 },
  ],
  atLeastOneOf: ['nonReimbursableCodes', 'maxAmountPerItem'],
};
