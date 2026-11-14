/**
 * Test data for the decision wizard in the widget (task 032): Decide plans for each lookup-bearing action type, and a double for the HOST
 * (`callServerTool`) that answers the four tools the widget calls (the lane's page, the plan, the lookup and the commit). The host is the
 * module boundary the wizard tests mock; everything between it and the DOM is the real code (the shim, the shared worklist service and
 * lane panel, the shared decision wizard).
 *
 * The double behaves as the server does where it matters to the widget: the page answers only the Work Items still open (so a recorded one
 * leaves the lane on the next read), the plan tool answers the plan, the suppression and the full row in one call, the lookup answers the
 * candidates that contain the typed text, and the commit records a review once.
 */
import {
  CONTACT,
  addTodo,
  approveVariance,
  commitResult,
  createBudget,
  decideReasons,
  pathBPlan,
  reassign,
  reviseBudget,
  sendInquiry,
  suppression,
} from '@spaarke/ui-components/components/DecisionWizard/__fixtures__/decisionWizardFixtures';
import type {
  DecisionAction,
  DecisionPlan,
  DecisionReview,
} from '@spaarke/ui-components/components/DecisionWizard/decisionWizardTypes';
import type { CallServerTool, McpToolResult } from '../mcp/mcpFetch';
import { CORES, MATTERS, guid, row, type SlimRow } from './laneFixtures';

export const PLAN_TOOL = 'spaarke_get_decision_plan';
export const LOOKUP_TOOL = 'spaarke_search_decision_lookup';
export const COMPLETE_TOOL = 'spaarke_complete_decision';
export const PAGE_TOOL = 'spaarke_list_decision_page';

export const SENTENCE =
  'A communication on this matter reads as a fee or scope change and no budget revision followed it.';

export const FIRM = { id: 'ffffffff-0000-0000-0000-000000000001', name: 'Acme LLP' };
export const BUDGET = { id: 'bbbbbbbb-0000-0000-0000-00000000000a', name: 'FY26 litigation budget' };
export const BUDGET_2 = { id: 'bbbbbbbb-0000-0000-0000-00000000000b', name: 'FY26 appeal budget' };
export const PERSON = { id: CONTACT.id, name: 'Dana Whitfield', email: 'dana@firm.example' };
export const PERSON_2 = {
  id: 'cccccccc-0000-0000-0000-000000000002',
  name: 'Daniel Ortiz',
  email: 'daniel@firm.example',
};

const SINGLE_LEVEL = {
  lane: 'Decide' as const,
  policyVersionId: 'dddddddd-0000-0000-0000-000000000001',
  dismissalReasons: decideReasons,
};

/** Assign Task as the plan route sends it (DecisionActionCatalog): Person, TextArea, Date and a Boolean that starts ticked. */
export const assignTask: DecisionAction = {
  code: 'assign-task',
  label: 'Assign Task',
  workType: null,
  recordClass: 'Judgement',
  isRecommended: true,
  parameters: [
    { code: 'assignee', label: 'Assign to', kind: 'Person', required: true },
    { code: 'description', label: 'What to do', kind: 'TextArea', required: true },
    { code: 'dueDate', label: 'Due', kind: 'Date', required: false },
    { code: 'notify', label: 'Send email notification', kind: 'Boolean', required: false, defaultValue: 'true' },
  ],
  effectLines: ['Creates a task linked to the record, assigned to the person above, when the decision is recorded'],
  excludes: [],
};

/** Assign Work as a Decide-lane plan action (D-51). */
export const assignWork: DecisionAction = {
  code: 'assign-work',
  label: 'Assign Work',
  workType: null,
  recordClass: 'Judgement',
  isRecommended: true,
  parameters: [
    { code: 'assignee', label: 'Assign to', kind: 'Person', required: true },
    { code: 'description', label: 'What to do', kind: 'TextArea', required: true },
  ],
  effectLines: ['Creates a work assignment linked to the record, created when the decision is recorded'],
  excludes: [],
};

/** One action of every parameter kind, so each kind is rendered and submitted once. */
export const everyKind: DecisionAction = {
  code: 'every-kind',
  label: 'Record everything',
  workType: null,
  recordClass: 'Judgement',
  isRecommended: true,
  parameters: [
    { code: 'subject', label: 'Subject line', kind: 'Text', required: true },
    { code: 'details', label: 'Details', kind: 'TextArea', required: true },
    {
      code: 'category',
      label: 'Category',
      kind: 'Choice',
      required: true,
      options: [
        { value: '100000000', label: 'Fees' },
        { value: '100000001', label: 'Expenses' },
      ],
    },
    { code: 'amount', label: 'Amount', kind: 'Money', required: true },
    { code: 'dueDate', label: 'Due on', kind: 'Date', required: true },
    { code: 'notify', label: 'Tell them', kind: 'Boolean', required: false },
    { code: 'person', label: 'Contact', kind: 'Person', required: true },
    { code: 'budget', label: 'Budget', kind: 'Lookup', required: true, lookupEntity: 'sprk_budget' },
  ],
  effectLines: ['Records every kind of field'],
  excludes: [],
};

const plan = (actions: DecisionAction[], nextSteps: DecisionAction[] = []): DecisionPlan => ({
  ...pathBPlan,
  ...SINGLE_LEVEL,
  actions,
  nextSteps,
});

/** The plan for each lookup-bearing action type (and the every-kind action). */
export const PLANS = {
  reviseBudget: plan([reviseBudget]),
  reassign: plan([reassign]),
  // add-todo is a Next step: its plan has one action (skipped) and the To Do as a Next step.
  addTodo: plan([approveVariance], [addTodo]),
  assignWork: plan([assignWork]),
  assignTask: plan([assignTask]),
  inquiry: plan([sendInquiry]),
  createBudget: plan([createBudget]),
  everyKind: plan([everyKind]),
};

/** One Work Item the server holds: its number (the row's id is `guid('5', n)`), its matter and the plan it offers. */
export interface ServerItem {
  n: number;
  plan: DecisionPlan;
  core?: string;
  subjectName?: string;
}

export type CommitAnswer = (signalId: string, review: DecisionReview) => McpToolResult;

export interface WizardHostOptions {
  /** Answers `spaarke_complete_decision`. Default: the review is recorded and the Work Item leaves the lane. */
  complete?: CommitAnswer;
  /** The plan tool's answer for one Work Item, instead of the default (a refusal, a missing row ...). */
  plan?: (signalId: string, item: ServerItem) => McpToolResult | Promise<McpToolResult> | undefined;
  /** Candidates per parameter code, for `spaarke_search_decision_lookup`. */
  candidates?: Record<string, ReadonlyArray<{ id: string; name: string; email?: string }>>;
  /** Display modes: what `requestDisplayMode('fullscreen')` answers. Default `fullscreen`. */
  displayMode?: 'fullscreen' | 'inline' | null;
}

export interface HostCall {
  name: string;
  args: Record<string, unknown>;
}

const DEFAULT_CANDIDATES: NonNullable<WizardHostOptions['candidates']> = {
  budget: [BUDGET, BUDGET_2],
  outsideFirm: [FIRM],
  assignee: [PERSON, PERSON_2],
  person: [PERSON, PERSON_2],
};

/** The REST worklist row the plan tool returns for an item (the full row: sentence and evidence the paging tool leaves out). */
export function restRow(item: ServerItem) {
  return {
    signalId: guid('5', item.n),
    lane: 'Decide',
    status: 'Open',
    title: 'Fee or scope change with no budget revision',
    subjectName: item.subjectName ?? `Subject ${item.n}`,
    ruleShortName: `Rule ${item.n % 7}`,
    pastDueWording: 'overdue',
    raisedOn: '2026-10-06T12:00:00Z',
    sentence: SENTENCE,
    evidenceRefs: JSON.stringify([
      { kind: 'sprk_communication', ref: 'c-1', tier: 'Fact', text: 'Invoice 4411 dated 3 Oct 2026', source: 'Email' },
    ]),
  };
}

export function wizardHost(items: readonly ServerItem[], options: WizardHostOptions = {}) {
  const calls: HostCall[] = [];
  const recorded = new Map<string, DecisionReview>();
  const reviews = new Set<string>();

  const slimRows = (): SlimRow[] =>
    items
      .filter(i => !recorded.has(guid('5', i.n)))
      .map(i => row(i.n, { core: i.core ?? MATTERS.a.id, workType: 'chaseReply', subjectName: i.subjectName }));

  const callTool: CallServerTool = async (name, args) => {
    calls.push({ name, args });
    switch (name) {
      case PAGE_TOOL: {
        const cores: Record<string, { name: string; number: string }> = {};
        const rows = slimRows();
        for (const r of rows)
          if (r.coreRecordId && CORES[r.coreRecordId]) cores[r.coreRecordId] = CORES[r.coreRecordId];
        return { structuredContent: { items: rows, cores } };
      }
      case PLAN_TOOL: {
        const item = items.find(i => guid('5', i.n) === args.signalId);
        if (!item) throw new Error(`unexpected plan read ${String(args.signalId)}`);
        const custom = await options.plan?.(String(args.signalId), item);
        if (custom) return custom;
        const signalId = guid('5', item.n);
        return {
          structuredContent: {
            plan: { ...item.plan, signalId },
            suppression: suppression({ signalId }),
            row: restRow(item),
          },
        };
      }
      case LOOKUP_TOOL: {
        const code = String(args.parameterCode);
        const query = String(args.query ?? '').toLowerCase();
        const all = (options.candidates ?? DEFAULT_CANDIDATES)[code] ?? [];
        return {
          structuredContent: {
            parameterCode: code,
            entity: code === 'budget' ? 'sprk_budget' : code === 'outsideFirm' ? 'sprk_organization' : 'contact',
            candidates: all.filter(c => c.name.toLowerCase().includes(query)).slice(0, 25),
          },
        };
      }
      case COMPLETE_TOOL: {
        const signalId = String(args.signalId);
        const review = args.review as DecisionReview;
        if (options.complete) {
          const answer = options.complete(signalId, review);
          if (answer.isError !== true) {
            recorded.set(signalId, review);
            reviews.add(review.reviewId);
          }
          return answer;
        }
        recorded.set(signalId, review);
        reviews.add(review.reviewId);
        return {
          structuredContent: commitResult({ reviewId: review.reviewId }),
          content: [{ type: 'text', text: 'Decision recorded.' }],
        };
      }
      default:
        throw new Error(`unexpected tool ${name}`);
    }
  };

  const requestDisplayMode = jest.fn(async () => options.displayMode ?? 'fullscreen');
  const openLink = jest.fn(async () => true);
  const callsTo = (name: string) => calls.filter(c => c.name === name);
  return { callTool, calls, callsTo, recorded, requestDisplayMode, openLink };
}

export type WizardHost = ReturnType<typeof wizardHost>;
