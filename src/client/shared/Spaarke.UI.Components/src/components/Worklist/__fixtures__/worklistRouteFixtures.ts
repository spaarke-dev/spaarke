/**
 * Bodies shaped like the Signal read route's answer (`GET /api/v1/signals/worklist`, task 038, `WorklistResponse`): camelCase JSON,
 * lane and work-type names as the route spells them, a resolution type as the option value. Test/harness use only.
 *
 * One population exercises every worklist rule:
 *  - Decide: matter M has two items (ask outside counsel, approve or rebudget); a service request has one (chase a reply)
 *  - Do: the groups arrive work assignment, project, matter, then Not filed LAST (as the route emits them since task 154); the
 *    widget renders the route's order and never its own, which a test proves with a separate body that puts Not filed first
 */

import type {
  InquiryDispositionRequest,
  InquiryForTodo,
  RecordDispositionResult,
  WorklistService,
  WorklistSetName,
} from '../worklistService';
import { parseWorklistResponse } from '../worklistService';

export const ID = {
  matter: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
  project: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
  workAssignment: 'cccccccc-cccc-cccc-cccc-cccccccccccc',
  serviceRequest: 'dddddddd-dddd-dddd-dddd-dddddddddddd',
};

export const matterCore = {
  recordType: 'sprk_matter',
  recordId: ID.matter,
  typeLabel: 'Matter',
  name: 'Acme v. Beta',
  number: 'MTR-00042',
};
export const projectCore = {
  recordType: 'sprk_project',
  recordId: ID.project,
  typeLabel: 'Project',
  name: 'Contract refresh 2026',
  number: 'PRJT-00007',
};
export const workAssignmentCore = {
  recordType: 'sprk_workassignment',
  recordId: ID.workAssignment,
  typeLabel: 'Work assignment',
  name: 'Review vendor agreement',
  number: 'WRK-00019',
};
export const serviceRequestCore = {
  recordType: 'sprk_servicerequest',
  recordId: ID.serviceRequest,
  typeLabel: 'Service request',
  name: 'Onboarding request from Northwind',
  number: 'SVCR-00003',
};

type Raw = Record<string, unknown>;

/** One item as the route returns it. The id is the only required field. */
export function routeItem(signalId: string, lane: 'Decide' | 'Do', extra: Raw = {}): Raw {
  return {
    signalId,
    lane,
    status: 'Open',
    title: null,
    subjectName: `Subject ${signalId.slice(0, 4)}`,
    ruleShortName: 'A rule',
    workType: null,
    pastDueWording: 'overdue',
    raisedOn: '2026-10-06T12:00:00Z',
    dueDate: null,
    evidenceRefs: null,
    policyVersionId: '99999999-9999-9999-9999-999999999999',
    resolvedOn: null,
    resolutionType: null,
    suppressedUntil: null,
    ...extra,
  };
}

export const group = (core: Raw | null, items: Raw[]): Raw => ({ core, notFiled: core === null, items });
export const body = (set: string, decide: Raw[], doGroups: Raw[]): Raw => ({
  set,
  lanes: [
    { lane: 'Decide', groups: decide },
    { lane: 'Do', groups: doGroups },
  ],
});

// ---- ids --------------------------------------------------------------------------------------------------------------
export const A1 = '10000000-0000-0000-0000-000000000001'; // Decide, matter, ask outside counsel
export const A2 = '10000000-0000-0000-0000-000000000002'; // Decide, matter, approve or rebudget
export const A3 = '10000000-0000-0000-0000-000000000003'; // Decide, service request, chase a reply
export const D1 = '20000000-0000-0000-0000-000000000001'; // Do, matter, finish or reschedule (past due)
export const D2 = '20000000-0000-0000-0000-000000000002'; // Do, matter, coming due
export const D3 = '20000000-0000-0000-0000-000000000003'; // Do, project, coming due
export const D4 = '20000000-0000-0000-0000-000000000004'; // Do, work assignment, chase a response (late)
export const D5 = '20000000-0000-0000-0000-000000000005'; // Do, Not filed, finish or reschedule
export const C1 = '30000000-0000-0000-0000-000000000001'; // closed today, Decide, Acted
export const C2 = '30000000-0000-0000-0000-000000000002'; // closed today, Do, cleared on its own
export const E1 = '40000000-0000-0000-0000-000000000001'; // closed earlier, Do, Dismissed
export const S1 = '50000000-0000-0000-0000-000000000001'; // suppressed, Do

export const lanesBody = body(
  'lanes',
  [
    group(matterCore, [
      routeItem(A1, 'Decide', {
        subjectName: 'Invoice 4411 from Acme LLP',
        ruleShortName: 'Fee or scope change',
        workType: 'askOutsideCounsel',
      }),
      routeItem(A2, 'Decide', {
        subjectName: 'Spend to date against budget',
        ruleShortName: 'Spend over budget',
        workType: 'approveOrRebudget',
      }),
    ]),
    group(serviceRequestCore, [
      routeItem(A3, 'Decide', {
        subjectName: 'Reply from Northwind',
        ruleShortName: 'Awaiting a reply',
        workType: 'chaseReply',
      }),
    ]),
  ],
  [
    group(workAssignmentCore, [
      routeItem(D4, 'Do', {
        subjectName: 'Outside counsel response',
        ruleShortName: 'Response overdue',
        workType: 'chaseResponse',
        pastDueWording: 'late',
        dueDate: '2026-10-06',
      }),
    ]),
    group(projectCore, [
      routeItem(D3, 'Do', {
        subjectName: 'File the status report',
        ruleShortName: 'Coming due',
        workType: 'comingDue',
        dueDate: '2026-10-12',
      }),
    ]),
    group(matterCore, [
      routeItem(D1, 'Do', {
        subjectName: 'Send engagement letter',
        ruleShortName: 'Overdue task',
        workType: 'finishOrReschedule',
        dueDate: '2026-10-04',
      }),
      routeItem(D2, 'Do', {
        subjectName: 'Confirm hearing date',
        ruleShortName: 'Coming due',
        workType: 'comingDue',
        dueDate: '2026-10-11',
      }),
    ]),
    group(null, [
      routeItem(D5, 'Do', {
        subjectName: 'Call the client back',
        ruleShortName: 'Overdue To Do',
        workType: 'finishOrReschedule',
        dueDate: '2026-10-05',
      }),
    ]),
  ]
);

// ---- the inquiry-outcome To Do (task 155, D-111) ------------------------------------------------------------------------
export const T1 = '60000000-0000-0000-0000-000000000001'; // Do, matter, a To Do (subject table sprk_todo): the inquiry-outcome one
export const T2 = '60000000-0000-0000-0000-000000000002'; // Do, matter, another To Do: the route finds no inquiry for it (404)
export const T3 = '60000000-0000-0000-0000-000000000003'; // Do, matter, an event (subject table sprk_event): never a lookup
export const TODO_ID = {
  inquiry: '70000000-0000-0000-0000-000000000001',
  other: '70000000-0000-0000-0000-000000000002',
  event: '70000000-0000-0000-0000-000000000003',
};
export const SERVICE_REQUEST_ID = '80000000-0000-0000-0000-000000000001';
export const REPLY = {
  newest: '90000000-0000-0000-0000-000000000002',
  older: '90000000-0000-0000-0000-000000000001',
};

/** A Do lane with a To Do that records an inquiry's outcome, another To Do, and an event; plus one Decide row (no subject reference). */
export const inquiryLanesBody = body(
  'lanes',
  [group(matterCore, [routeItem(A1, 'Decide', { subjectName: 'Invoice 4411', workType: 'askOutsideCounsel' })])],
  [
    group(matterCore, [
      routeItem(T1, 'Do', {
        subjectName: 'Record the outcome of the budget inquiry',
        ruleShortName: 'Overdue To Do',
        workType: 'finishOrReschedule',
        dueDate: '2026-10-05',
        sentence: 'A To Do is overdue.',
        subjectRecordType: 'sprk_todo',
        subjectRecordId: TODO_ID.inquiry,
      }),
      routeItem(T2, 'Do', {
        subjectName: 'Call the client back',
        workType: 'finishOrReschedule',
        dueDate: '2026-10-05',
        subjectRecordType: 'sprk_todo',
        subjectRecordId: TODO_ID.other,
      }),
      routeItem(T3, 'Do', {
        subjectName: 'Hearing on the 14th',
        workType: 'comingDue',
        dueDate: '2026-10-14',
        subjectRecordType: 'sprk_event',
        subjectRecordId: TODO_ID.event,
      }),
    ]),
  ]
);

/** What `GET /api/v1/inquiries/for-todo/{todoId}` returns for the inquiry-outcome To Do: replies newest first. */
export const inquiryForTodo: InquiryForTodo = {
  serviceRequestId: SERVICE_REQUEST_ID,
  replies: [
    {
      communicationId: REPLY.newest,
      subject: 'RE: Budget inquiry - revised estimate',
      receivedOn: '2026-10-08T15:00:00Z',
    },
    { communicationId: REPLY.older, subject: 'RE: Budget inquiry', receivedOn: '2026-10-02T09:00:00Z' },
  ],
};

export const closedTodayBody = body(
  'closedToday',
  [
    group(matterCore, [
      routeItem(C1, 'Decide', {
        status: 'Resolved',
        subjectName: 'Retainer top-up',
        resolutionType: 100000000,
        resolvedOn: '2026-10-09T09:00:00Z',
        evidenceRefs: JSON.stringify([
          {
            kind: 'sprk_budget',
            ref: 'b1',
            tier: 'Fact',
            text: '$12,000 spent',
            source: 'Budget',
            asOf: '2026-10-08T00:00:00Z',
            clause: 'all[0]',
          },
          {
            kind: 'sprk_communication',
            ref: 'c1',
            tier: 'Interpretation',
            clause: 'all[1]',
            asOf: '2026-10-08T00:00:00Z',
            withheld: true,
          },
        ]),
      }),
    ]),
  ],
  [
    group(matterCore, [
      routeItem(C2, 'Do', {
        status: 'Resolved',
        subjectName: 'Order the transcript',
        resolutionType: 100000002,
        resolvedOn: '2026-10-09T08:00:00Z',
      }),
    ]),
  ]
);

export const closedEarlierBody = body(
  'closedEarlier',
  [],
  [
    group(projectCore, [
      routeItem(E1, 'Do', {
        status: 'Resolved',
        subjectName: 'Update the risk register',
        resolutionType: 100000001,
        resolvedOn: '2026-10-02T12:00:00Z',
      }),
    ]),
  ]
);

export const suppressedBody = body(
  'suppressed',
  [],
  [
    group(matterCore, [
      routeItem(S1, 'Do', { subjectName: 'Chase the audit letter', suppressedUntil: '2026-10-28T12:00:00Z' }),
    ]),
  ]
);

export const ROUTE_BODIES: Record<WorklistSetName, Raw> = {
  lanes: lanesBody,
  closedToday: closedTodayBody,
  closedEarlier: closedEarlierBody,
  suppressed: suppressedBody,
};

/** A service over the bodies (through the real parser). `read` is a spy-able function; unknown sets read empty. */
export function fakeService(bodies: Partial<Record<WorklistSetName, Raw>> = ROUTE_BODIES): WorklistService & {
  read: jest.Mock<ReturnType<WorklistService['read']>, [WorklistSetName]>;
  findInquiryForTodo: jest.Mock<Promise<InquiryForTodo | null>, [string]>;
  recordDisposition: jest.Mock<Promise<RecordDispositionResult>, [string, InquiryDispositionRequest]>;
} {
  return {
    read: jest.fn(async (set: WorklistSetName) => parseWorklistResponse(bodies[set] ?? body(set, [], []), set)),
    // No To Do is an inquiry's unless a test says so (the route answers 404).
    findInquiryForTodo: jest.fn(async (_todoId: string): Promise<InquiryForTodo | null> => null),
    recordDisposition: jest.fn(
      async (_id: string, _request: InquiryDispositionRequest): Promise<RecordDispositionResult> => ({
        todoCompleted: true,
      })
    ),
  };
}
