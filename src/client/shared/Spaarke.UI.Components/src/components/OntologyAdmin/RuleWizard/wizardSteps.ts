/**
 * wizardSteps - the seven steps of the rule wizard and where each BFF field error belongs (spaarke-ontology-platform-r1 task 102).
 *
 * The BFF names a refusal by the field it concerns (`ruleBody`, `messageTemplate`, `decisionPlan`, `policyCode`, ...), in the 200
 * shape of `validate` and in the 400 shape of the dry run, save and publish alike. `stepOfField` says which step shows that field, so
 * a refusal is always shown on the step and at the field it concerns.
 */

export type RuleWizardStepId = 'basics' | 'condition' | 'wording' | 'actions' | 'scope' | 'test' | 'save';

/** The steps in order. */
export const RULE_WIZARD_STEPS: ReadonlyArray<{ readonly id: RuleWizardStepId; readonly label: string }> = [
  { id: 'basics', label: 'Basics' },
  { id: 'condition', label: 'Condition' },
  { id: 'wording', label: 'Wording' },
  { id: 'actions', label: 'Actions' },
  { id: 'scope', label: 'Scope and noise' },
  { id: 'test', label: 'Test' },
  { id: 'save', label: 'Save' },
];

const FIELD_STEP: Readonly<Record<string, RuleWizardStepId>> = {
  policyCode: 'basics',
  name: 'basics',
  shortName: 'basics',
  lane: 'basics',
  severity: 'basics',
  workType: 'basics',
  ruleType: 'condition',
  ruleBody: 'condition',
  shortHeadline: 'wording',
  messageTemplate: 'wording',
  proposedAction: 'wording',
  decisionPlan: 'actions',
  matterId: 'scope',
  priority: 'scope',
  countsTowardSuppression: 'scope',
};

/** The step that shows a field. A field the wizard has no control for is shown on the last step, with the rest of the summary. */
export function stepOfField(field: string): RuleWizardStepId {
  return field in FIELD_STEP ? FIELD_STEP[field] : 'save';
}

export function stepLabel(id: RuleWizardStepId): string {
  return RULE_WIZARD_STEPS.find(step => step.id === id)?.label ?? id;
}

/** The fields of a step that the BFF can refuse, in the order the step shows them. */
export const STEP_FIELDS: Readonly<Record<RuleWizardStepId, readonly string[]>> = {
  basics: ['policyCode', 'name', 'shortName', 'lane', 'severity', 'workType'],
  condition: ['ruleType', 'ruleBody'],
  wording: ['shortHeadline', 'messageTemplate', 'proposedAction'],
  actions: ['decisionPlan'],
  scope: ['matterId', 'priority', 'countsTowardSuppression'],
  test: [],
  save: [],
};
