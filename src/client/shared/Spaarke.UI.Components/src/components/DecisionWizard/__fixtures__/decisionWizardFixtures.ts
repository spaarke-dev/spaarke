/**
 * Fixtures for the decision wizard, shaped like task 036's plan response, task 034's suppression response and task 043's
 * commit review / result (the C# records field for field). The item shape is the wizard's own `DecisionWizardItem`
 * (the Signal read route is task 038). Test/harness use only.
 */

import type { ILookupItem } from '../../../types/LookupTypes';
import type { WorklistCore, WorklistItem } from '../../Worklist/types';
import type {
  DecisionAction,
  DecisionCommitResult,
  DecisionPlan,
  DecisionResolution,
  DecisionWizardItem,
  DismissalReason,
  SuppressionStatus,
} from '../decisionWizardTypes';

export const TODAY = new Date(2026, 9, 9, 10, 30);

export const PATH_B_ID = '11111111-1111-1111-1111-111111111111';
export const THRESHOLD_ID = '22222222-2222-2222-2222-222222222222';
export const DECIDED_ID = '33333333-3333-3333-3333-333333333333';
export const DO_ID = '44444444-4444-4444-4444-444444444444';
export const OTHER_ID = '55555555-5555-5555-5555-555555555555';

export const BUDGET_A: ILookupItem = { id: 'bbbbbbbb-0000-0000-0000-00000000000a', name: 'FY26 litigation budget' };
export const BUDGET_B: ILookupItem = { id: 'bbbbbbbb-0000-0000-0000-00000000000b', name: 'FY26 appeal budget' };
export const CONTACT: ILookupItem = { id: 'cccccccc-0000-0000-0000-000000000001', name: 'Dana Whitfield' };

export const matterCore: WorklistCore = {
  recordType: 'sprk_matter',
  recordId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
  typeLabel: 'Matter',
  name: 'Acme v. Beta',
  number: 'MTR-00042',
};

const mailParameters: DecisionAction['parameters'] = [
  { code: 'to', label: 'To', kind: 'Text', required: true },
  { code: 'subject', label: 'Subject', kind: 'Text', required: true },
  { code: 'body', label: 'Message', kind: 'TextArea', required: true },
];

export const sendInquiry: DecisionAction = {
  code: 'send-budget-inquiry',
  label: 'Send budget inquiry',
  workType: 'ask',
  recordClass: 'Judgement',
  isRecommended: true,
  parameters: [
    ...mailParameters,
    { code: 'outsideFirm', label: 'Outside firm', kind: 'Lookup', required: false, lookupEntity: 'sprk_organization' },
  ],
  effectLines: ['Sends the message above to the recipient', 'Records the inquiry against the matter'],
  excludes: [],
};

export const reviseBudget: DecisionAction = {
  code: 'revise-budget',
  label: 'Revise budget',
  workType: 'fund',
  recordClass: 'Judgement',
  isRecommended: false,
  parameters: [
    { code: 'budget', label: 'Budget to revise', kind: 'Lookup', required: true, lookupEntity: 'sprk_budget' },
    { code: 'amount', label: 'New budget', kind: 'Money', required: true },
    { code: 'reason', label: 'Reason for revision', kind: 'TextArea', required: true },
  ],
  effectLines: ['Records a budget revision on the matter', 'Updates the amount of the budget you picked'],
  excludes: ['approve-variance'],
};

/** Task 167 (D-193): Create budget as the plan route returns it: live option lists, a currency default from the matter's business unit. */
export const EURO_ID = 'eeeeeeee-0000-0000-0000-0000000000e1';
export const DOLLAR_ID = 'eeeeeeee-0000-0000-0000-0000000000d1';
export const createBudget: DecisionAction = {
  code: 'create-budget',
  label: 'Create budget',
  workType: 'fund',
  recordClass: 'Judgement',
  isRecommended: true,
  parameters: [
    { code: 'amount', label: 'Budget amount', kind: 'Money', required: true },
    {
      code: 'category',
      label: 'Category',
      kind: 'Choice',
      required: false,
      options: [
        { value: '100000000', label: 'Fees' },
        { value: '100000001', label: 'Expenses' },
      ],
      optionsSource: 'metadata:sprk_budget.sprk_budgetcategory',
    },
    { code: 'year', label: 'Year', kind: 'Text', required: false },
    { code: 'startDate', label: 'Start date', kind: 'Date', required: false },
    { code: 'endDate', label: 'End date', kind: 'Date', required: false, notBefore: 'startDate' },
    {
      code: 'period',
      label: 'Period',
      kind: 'Choice',
      required: false,
      options: [
        { value: '0', label: 'Annual' },
        { value: '1', label: 'Quarter 1' },
      ],
      optionsSource: 'metadata:sprk_budget.sprk_budgetperiod',
    },
    {
      code: 'currency',
      label: 'Currency',
      kind: 'Choice',
      required: false,
      options: [
        { value: DOLLAR_ID, label: 'US Dollar (USD)' },
        { value: EURO_ID, label: 'Euro (EUR)' },
      ],
      optionsSource: 'currency:active',
      defaultValue: EURO_ID,
    },
    { code: 'reason', label: 'Reason for the budget', kind: 'TextArea', required: true },
  ],
  effectLines: ["Creates the matter's first budget", 'Records the budget and your reason on the Decision Record'],
  excludes: ['approve-variance'],
};

export const approveVariance: DecisionAction = {
  code: 'approve-variance',
  label: 'Approve variance',
  workType: 'fund',
  recordClass: 'Judgement',
  isRecommended: false,
  parameters: [
    { code: 'amount', label: 'Approved overage', kind: 'Money', required: true },
    { code: 'note', label: 'Approval note', kind: 'TextArea', required: false },
  ],
  effectLines: ['The Decision Record is the approval; nothing else is written'],
  excludes: ['revise-budget'],
};

export const addTodo: DecisionAction = {
  code: 'add-todo',
  label: 'Add To Do',
  recordClass: 'Judgement',
  isRecommended: false,
  parameters: [
    { code: 'title', label: 'To Do', kind: 'Text', required: true },
    { code: 'assignee', label: 'Assigned to', kind: 'Person', required: true },
    { code: 'dueDate', label: 'Due', kind: 'Date', required: true },
  ],
  effectLines: ['Creates a To Do linked to the record, created when the decision is recorded'],
  excludes: [],
};

export const createEvent: DecisionAction = {
  code: 'create-event',
  label: 'Create Event',
  recordClass: 'Judgement',
  isRecommended: false,
  parameters: [
    { code: 'title', label: 'Event', kind: 'Text', required: true },
    { code: 'date', label: 'Date', kind: 'Date', required: true },
  ],
  effectLines: ['Creates an event linked to the record, created when the decision is recorded'],
  excludes: [],
};

export const markComplete: DecisionAction = {
  code: 'mark-complete',
  label: 'Mark complete',
  recordClass: 'Routine',
  isRecommended: true,
  parameters: [{ code: 'note', label: 'Completion note', kind: 'TextArea', required: false }],
  effectLines: ['Sets the item to Completed'],
  // The real catalog: Mark complete excludes both (and the plan builder makes the lists mutual).
  excludes: ['reschedule', 'reassign'],
};

export const reschedule: DecisionAction = {
  code: 'reschedule',
  label: 'Reschedule',
  recordClass: 'Routine',
  isRecommended: false,
  parameters: [
    { code: 'dueDate', label: 'New due date', kind: 'Date', required: true },
    { code: 'reason', label: 'Reason', kind: 'Text', required: true },
  ],
  effectLines: ['Moves the due date of the item'],
  excludes: ['mark-complete'],
};

export const reassign: DecisionAction = {
  code: 'reassign',
  label: 'Reassign',
  recordClass: 'Routine',
  isRecommended: false,
  parameters: [
    { code: 'assignee', label: 'Reassign to', kind: 'Person', required: true },
    { code: 'note', label: 'Note to them', kind: 'TextArea', required: false },
  ],
  effectLines: ['Assigns the item to the person above'],
  excludes: ['mark-complete'],
};

export const decideReasons: DismissalReason[] = [
  {
    code: 'already-approved-offline',
    label: 'Already approved offline',
    countsTowardSuppression: true,
    requiresDetail: false,
  },
  {
    code: 'wrong-matter-misresolved',
    label: 'Wrong matter - misresolved',
    countsTowardSuppression: false,
    requiresDetail: false,
  },
  { code: 'other', label: 'Other', countsTowardSuppression: true, requiresDetail: true },
];

export const doReasons: DismissalReason[] = [
  { code: 'not-mine', label: 'Not mine', countsTowardSuppression: true, requiresDetail: false },
  { code: 'other', label: 'Other', countsTowardSuppression: true, requiresDetail: true },
];

const ruleDescription = {
  clauses: [
    {
      clauseId: 'c1',
      kind: 'evidence',
      text: 'A communication on the matter was classified as a fee or scope change.',
      formal: 'comm.class in (fee, scope)',
    },
    {
      clauseId: 'c2',
      kind: 'absence',
      text: 'No budget revision has been recorded since.',
      formal: 'not exists budget.revision since comm',
    },
  ],
};

/** Path B (cross-source): inquiry (recommended), revise, approve - revise and approve exclude each other. */
export const pathBPlan: DecisionPlan = {
  signalId: PATH_B_ID,
  lane: 'Decide',
  policyVersionId: 'dddddddd-0000-0000-0000-000000000001',
  actions: [sendInquiry, reviseBudget, approveVariance],
  nextSteps: [addTodo, createEvent],
  dismissalReasons: decideReasons,
  ruleDescription,
};

/** Task 167: the no-budget plan after the server swapped Create budget in for Revise budget. */
export const createBudgetPlan: DecisionPlan = {
  ...pathBPlan,
  actions: [createBudget],
  nextSteps: [],
};

/** The same actions with Approve variance FIRST (a rule may list either; the earlier one taken removes the later). */
export const approveFirstPlan: DecisionPlan = {
  ...pathBPlan,
  signalId: THRESHOLD_ID,
  actions: [approveVariance, reviseBudget, sendInquiry],
};

export const doPlan: DecisionPlan = {
  signalId: DO_ID,
  lane: 'Do',
  policyVersionId: 'dddddddd-0000-0000-0000-000000000002',
  actions: [markComplete, reschedule, reassign],
  nextSteps: [addTodo],
  dismissalReasons: doReasons,
};

export const suppression = (overrides: Partial<SuppressionStatus> = {}): SuppressionStatus => ({
  signalId: PATH_B_ID,
  dismissalCount: 1,
  dismissalsRecorded: 1,
  threshold: 3,
  windowDays: 30,
  suppressedUntil: null,
  countsTowardSuppression: true,
  ...overrides,
});

export const pathBRow: WorklistItem = {
  signalId: PATH_B_ID,
  lane: 'Decide',
  subjectName: 'Invoice 4411 from Acme LLP',
  title: 'Fee or scope change with no budget revision',
  ruleShortName: 'Fee or scope change',
  raisedOn: new Date(2026, 9, 6, 12).toISOString(),
};

export const thresholdRow: WorklistItem = {
  signalId: THRESHOLD_ID,
  lane: 'Decide',
  subjectName: 'Spend to date against budget',
  ruleShortName: 'Spend over budget',
  raisedOn: new Date(2026, 9, 9, 12).toISOString(),
};

export const otherRow: WorklistItem = {
  signalId: OTHER_ID,
  lane: 'Decide',
  subjectName: 'Second invoice from Acme LLP',
  ruleShortName: 'Fee or scope change',
  raisedOn: new Date(2026, 9, 8, 12).toISOString(),
};

export const doRow: WorklistItem = {
  signalId: DO_ID,
  lane: 'Do',
  subjectName: 'Send engagement letter',
  ruleShortName: 'Overdue task',
  dueDate: '2026-10-04',
};

export const SENTENCE =
  'A communication on this matter reads as a fee or scope change and no budget revision followed it.';

export const pathBItem: DecisionWizardItem = {
  row: pathBRow,
  core: matterCore,
  sentence: SENTENCE,
  evidence: [
    {
      kind: 'sprk_communication',
      ref: 'c-1',
      tier: 'Fact',
      text: 'Invoice 4411 dated 3 Oct 2026',
      source: 'Email',
      clause: 'A communication was classified fee or scope change',
    },
    { kind: 'classifier', ref: 'c-2', tier: 'Observation', text: 'Reads as a fee increase', source: 'Classifier' },
    { kind: 'sprk_budget', ref: 'b-1', tier: 'Fact', text: null, subject: 'Spend snapshot' },
  ],
  otherOpenItems: [otherRow],
};

export const thresholdItem: DecisionWizardItem = {
  row: thresholdRow,
  core: matterCore,
  sentence: 'Spend to date is over the approved budget.',
  evidence: [{ kind: 'sprk_budget', ref: 'b-1', tier: 'Fact', text: 'Spend is 112% of budget' }],
  otherOpenItems: [],
};

export const notFiledItem: DecisionWizardItem = {
  row: { ...pathBRow, signalId: '99999999-9999-9999-9999-999999999999' },
  core: null,
  sentence: SENTENCE,
  evidence: [],
  otherOpenItems: [],
};

export const doItem: DecisionWizardItem = {
  row: doRow,
  core: matterCore,
  sentence: 'The task is past its due date.',
  evidence: [{ kind: 'sprk_event', ref: 'e-1', tier: 'Fact', text: 'Due 4 Oct 2026' }],
  otherOpenItems: [
    { ...doRow, signalId: '66666666-6666-6666-6666-666666666666', subjectName: 'File the status report' },
  ],
};

/** A Path B item that was decided: inquiry + revise taken, approve skipped, one To Do. */
export const decidedResolution: DecisionResolution = {
  resolutionType: 'Acted',
  recordClass: 'Judgement',
  recordNumber: 'DR-00042',
  decidedByName: 'Pat Reviewer',
  decidedOn: new Date(2026, 9, 7, 9, 0).toISOString(),
  steps: [
    {
      code: 'send-budget-inquiry',
      taken: true,
      values: { to: 'budgets@acme.example', subject: 'Budget question', body: 'Please confirm the position.' },
    },
    { code: 'revise-budget', taken: true, values: { budget: BUDGET_A.id, amount: 250000, reason: 'Appeal added' } },
    { code: 'approve-variance', taken: false, values: null },
  ],
  followOns: [{ code: 'add-todo', entity: 'sprk_todo', id: 'dddddddd-0000-0000-0000-0000000000aa' }],
  factSnapshot: [
    {
      kind: 'sprk_communication',
      ref: 'c-1',
      tier: 'Fact',
      text: 'Invoice 4411 dated 3 Oct 2026 (as recorded)',
      source: 'Email',
    },
  ],
};

export const decidedItem: DecisionWizardItem = {
  row: { ...pathBRow, signalId: DECIDED_ID },
  core: matterCore,
  sentence: SENTENCE,
  // Today's data differs from the snapshot: the snapshot must win.
  evidence: [{ kind: 'sprk_communication', ref: 'c-1', tier: 'Fact', text: 'TODAYS VALUE, NOT THE SNAPSHOT' }],
  otherOpenItems: [],
  resolution: decidedResolution,
};

export const dismissedItem: DecisionWizardItem = {
  ...decidedItem,
  row: { ...pathBRow, signalId: '77777777-7777-7777-7777-777777777777' },
  resolution: {
    resolutionType: 'Dismissed',
    recordClass: 'Dismissal',
    recordNumber: 'DR-00043',
    decidedByName: 'Pat Reviewer',
    decidedOn: new Date(2026, 9, 7, 9, 0).toISOString(),
    reason: 'Already approved offline',
    steps: [
      { code: 'send-budget-inquiry', taken: false },
      { code: 'revise-budget', taken: false },
      { code: 'approve-variance', taken: false },
    ],
    followOns: [],
    factSnapshot: [],
  },
};

export const supersededItem: DecisionWizardItem = {
  ...decidedItem,
  row: { ...pathBRow, signalId: '88888888-8888-8888-8888-888888888888' },
  resolution: { resolutionType: 'Superseded', supersededBy: 'DR-00050' },
};

export function commitResult(overrides: Partial<DecisionCommitResult> = {}): DecisionCommitResult {
  return {
    reviewId: 'ignored',
    decisionRecordId: 'eeeeeeee-0000-0000-0000-000000000001',
    recordClass: 'Judgement',
    gateTier: 'Tier2b',
    steps: [],
    followOns: [],
    signals: [],
    warnings: [],
    replayed: false,
    ...overrides,
  };
}

export const lookupResults = {
  sprk_budget: [BUDGET_A],
  sprk_organization: [{ id: 'ffffffff-0000-0000-0000-000000000001', name: 'Acme LLP' }],
  Person: [CONTACT],
};
