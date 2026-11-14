/**
 * The forms of the billing rule types (ebilling-r1 task 084c): ONE form per `RuleType` that can be authored
 * (`BillingRuleCatalogue.Types`), keyed by the member name the read routes send in `ruleType`. The system type (SubmissionIntegrity) is not
 * authorable and has no form. `RecordProceduresRuleFormAgreementTests` pins this table, one form per type, against the server.
 */

import { addedFeeForm } from './AddedFee.form';
import { aiObservationForm } from './AiObservation.form';
import { approvedRateForm } from './ApprovedRate.form';
import { approvedTimekeeperForm } from './ApprovedTimekeeper.form';
import { coreRecordWindowForm } from './CoreRecordWindow.form';
import { dailyHoursCapForm } from './DailyHoursCap.form';
import { discountForm } from './Discount.form';
import { duplicateForm } from './Duplicate.form';
import { expenseRuleForm } from './ExpenseRule.form';
import { invoiceAgeForm } from './InvoiceAge.form';
import { nonBillableActivityForm } from './NonBillableActivity.form';
import { requiredDataForm } from './RequiredData.form';
import { timeIncrementForm } from './TimeIncrement.form';
import type { RuleFormDef } from './types';

export const RULE_FORMS: readonly RuleFormDef[] = [
  approvedTimekeeperForm,
  approvedRateForm,
  timeIncrementForm,
  dailyHoursCapForm,
  nonBillableActivityForm,
  expenseRuleForm,
  invoiceAgeForm,
  coreRecordWindowForm,
  duplicateForm,
  requiredDataForm,
  aiObservationForm,
  addedFeeForm,
  discountForm,
];

/** The form of a rule type as the server names it, or undefined for a type this page has no form for (it then offers no Change). */
export const formFor = (ruleType: string | null | undefined): RuleFormDef | undefined => RULE_FORMS.find(f => f.ruleType === ruleType);

export * from './body';
export type { FieldKind, FieldOption, FormField, FormValues, RuleFormDef } from './types';
