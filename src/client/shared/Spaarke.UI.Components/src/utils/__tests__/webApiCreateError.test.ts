/**
 * describeWebApiCreateError (issue #2382): a create wizard showed Dataverse's privilege template with "{0}..{6}"
 * unfilled. A privilege or access refusal now becomes one plain sentence naming no ids, users, tables or privilege
 * names, chosen from the privilege the raw message names (Create / AppendTo or Append / none); a BFF child-record
 * refusal (`child_record.denied`) gets the neutral sentence; any other error keeps a clean message. Covers the helper,
 * the two Xrm.WebApi create wizards end to end (Project, Matter), and the five BFF-routed create wizards end to end
 * (Event, Invoice, Report Card, To Do, Work Assignment) through the BFF's 403.
 */

import { describeWebApiCreateError } from '../webApiCreateError';
import { ChildRecordWriteError } from '../adapters/bffChildWriteAdapter';
import { ProjectService } from '../../components/CreateProjectWizard/projectService';
import { MatterService } from '../../components/CreateMatterWizard/matterService';
import { EventService } from '../../components/CreateEventWizard/eventService';
import { InvoiceService } from '../../components/CreateInvoiceWizard/invoiceService';
import { ReportCardService } from '../../components/CreateReportCardWizard/reportCardService';
import { TodoService } from '../../components/CreateTodoWizard/todoService';
import { WorkAssignmentService } from '../../components/CreateWorkAssignmentWizard/workAssignmentService';
import { EMPTY_EVENT_FORM } from '../../components/CreateEventWizard/formTypes';
import { buildEmptyInvoiceForm } from '../../components/CreateInvoiceWizard/formTypes';
import { buildEmptyReportCardForm } from '../../components/CreateReportCardWizard/formTypes';
import { EMPTY_TODO_FORM } from '../../components/CreateTodoWizard/formTypes';
import { EMPTY_WORK_ASSIGNMENT_FORM } from '../../components/CreateWorkAssignmentWizard/formTypes';
import type { ICreateProjectFormState } from '../../components/CreateProjectWizard/projectFormTypes';
import type { ICreateMatterFormState } from '../../components/CreateMatterWizard/formTypes';
import type { IDataService } from '../../types/serviceInterfaces';
import { fakeResponse } from '../../__mocks__/bffChildWriteFake';
import { apiErrorFor, throwingAuthenticatedFetch } from '../../__tests__/helpers/authenticatedFetchDouble';

// The shape Xrm.WebApi rejects with for a missing privilege, with the template unfilled (no privilege named).
const PRIVILEGE_ERROR = {
  errorCode: -2147220960,
  message:
    '{0} with id {1} does not have {2} right(s) for record with id {3} of entity {4}. Consider assigning a role with ' +
    'the required access level to the user or team. More details: {6}',
  code: 0x80040220,
};

const LINK_SENTENCE = (noun: string) =>
  `You don't have permission to link one of the selected values to this ${noun}. Ask your administrator.`;
const CREATE_SENTENCE = (noun: string) => `You don't have permission to create this ${noun}. Ask your administrator.`;
const NEUTRAL_SENTENCE = (noun: string) =>
  `You don't have permission to save this ${noun} with the values you selected. Ask your administrator.`;

const missing = (privilege: string) =>
  new Error(
    `Principal user (Id=11111111-1111-1111-1111-111111111111, type=8) is missing ${privilege} privilege (Id=22222222-2222-2222-2222-222222222222)`
  );

describe('describeWebApiCreateError', () => {
  it('turns the 0x80040220 template (no privilege named) into the neutral sentence', () => {
    expect(describeWebApiCreateError(PRIVILEGE_ERROR, 'project')).toEqual({
      message: NEUTRAL_SENTENCE('project'),
      isAccessDenied: true,
    });
  });

  it.each([
    ['signed errorCode only', { errorCode: -2147220960, message: 'x' }],
    ['unsigned errorCode', { errorCode: 2147746336, message: 'x' }],
    ['hex string code', { code: '0x80040220', message: 'x' }],
    ['a SecLib message', { message: 'SecLib::AccessCheckEx failed. Returned hr = -2147220960' }],
    ['a privilege that is neither Create nor AppendTo/Append', missing('prvReadContact')],
    ['an unfilled "{2} right(s)" form', { message: 'SystemUser with id {1} does not have {2} right(s) for record' }],
  ])('a privilege refusal naming no Create/AppendTo/Append privilege gets the neutral sentence: %s', (_label, err) => {
    expect(describeWebApiCreateError(err, 'matter')).toEqual({
      message: NEUTRAL_SENTENCE('matter'),
      isAccessDenied: true,
    });
  });

  it.each([
    ['prvAppendToContact', missing('prvAppendToContact')],
    ['prvAppendTosprk_MatterType_Ref', missing('prvAppendTosprk_MatterType_Ref')],
    ['prvAppendsprk_Project (Append on the record itself)', missing('prvAppendsprk_Project')],
    [
      'a filled "does not have AppendTo right(s)"',
      { message: 'SystemUser with id 1 does not have AppendTo right(s) for record' },
    ],
    [
      'a filled "does not have AppendToAccess right(s)" with the code',
      {
        errorCode: -2147220960,
        message: 'SystemUser with id 1 does not have AppendToAccess right(s) for record with id 2',
      },
    ],
  ])('an AppendTo or Append refusal gets the link sentence: %s', (_label, err) => {
    expect(describeWebApiCreateError(err, 'matter')).toEqual({
      message: LINK_SENTENCE('matter'),
      isAccessDenied: true,
    });
  });

  it.each([
    ['prvCreatesprk_Project', missing('prvCreatesprk_Project')],
    [
      'prvCreatesprk_ReportCard with the code',
      { errorCode: -2147220960, message: 'is missing prvCreatesprk_ReportCard privilege' },
    ],
    [
      'a filled "does not have CreateAccess right(s)"',
      { message: 'SystemUser with id 1 does not have CreateAccess right(s) for record' },
    ],
  ])('a Create refusal gets the create sentence: %s', (_label, err) => {
    expect(describeWebApiCreateError(err, 'report card')).toEqual({
      message: CREATE_SENTENCE('report card'),
      isAccessDenied: true,
    });
  });

  it('a BFF child-record refusal (ChildRecordWriteError, child_record.denied) gets the neutral sentence, not its detail', () => {
    const err = new ChildRecordWriteError(
      "You do not have permission to create records in 'sprk_todo' (prvCreatesprk_Todo).",
      403,
      'child_record.denied'
    );
    expect(describeWebApiCreateError(err, 'to do')).toEqual({
      message: NEUTRAL_SENTENCE('to do'),
      isAccessDenied: true,
    });
  });

  it('the same refusal thrown by authenticatedFetch as an ApiError (reasonCode in its ProblemDetails) gets the neutral sentence', () => {
    const err = apiErrorFor(403, {
      title: 'Not permitted',
      status: 403,
      detail: "You do not have permission to create records in 'sprk_event' (prvAppendTosprk_Organization).",
      reasonCode: 'child_record.denied',
    });
    expect(describeWebApiCreateError(err, 'event')).toEqual({
      message: NEUTRAL_SENTENCE('event'),
      isAccessDenied: true,
    });
  });

  it('a ChildRecordWriteError with any other reasonCode is not a privilege refusal', () => {
    const err = new ChildRecordWriteError('The BFF did not return the new record id.', 201);
    expect(describeWebApiCreateError(err, 'to do')).toEqual({
      message: 'The BFF did not return the new record id.',
      isAccessDenied: false,
    });
  });

  it.each([
    ['AppendTo', missing('prvAppendTosprk_Organization')],
    ['Create', missing('prvCreatesprk_Project')],
    ['none named', PRIVILEGE_ERROR],
    [
      'BFF refusal',
      new ChildRecordWriteError(
        "You do not have permission to create records in 'sprk_invoice' (prvCreatesprk_Invoice).",
        403,
        'child_record.denied'
      ),
    ],
  ])('never names ids, users, tables or privileges in the sentence: %s', (_label, err) => {
    const out = describeWebApiCreateError(err, 'project');
    expect(out.message).not.toMatch(/[0-9a-f]{8}-[0-9a-f]{4}/i);
    expect(out.message).not.toMatch(/prv/i);
    expect(out.message).not.toMatch(/sprk_/i);
    expect(out.message).not.toMatch(/\{\d\}/);
  });

  it('keeps any other error message as it is', () => {
    expect(describeWebApiCreateError(new Error('Bad Request - Error in query syntax.'), 'project')).toEqual({
      message: 'Bad Request - Error in query syntax.',
      isAccessDenied: false,
    });
    expect(
      describeWebApiCreateError({ errorCode: -2147088238, message: 'A duplicate record exists.' }, 'to do')
    ).toEqual({
      message: 'A duplicate record exists.',
      isAccessDenied: false,
    });
  });

  it('replaces an unfilled template or an empty error with a clean fallback', () => {
    expect(describeWebApiCreateError({ errorCode: 1, message: 'Value {0} is invalid for {1}' }, 'event').message).toBe(
      'Unknown error'
    );
    expect(describeWebApiCreateError(undefined, 'event')).toEqual({ message: 'Unknown error', isAccessDenied: false });
    expect(describeWebApiCreateError({}, 'event').message).toBe('Unknown error');
  });
});

// ---------------------------------------------------------------------------
// The Xrm.WebApi create wizards, end to end through their service
// ---------------------------------------------------------------------------

function rejectingDataService(err: unknown): IDataService {
  return {
    createRecord: jest.fn().mockRejectedValue(err),
    retrieveRecord: jest.fn().mockResolvedValue({}),
    retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
    updateRecord: jest.fn().mockResolvedValue(undefined),
    deleteRecord: jest.fn().mockResolvedValue(undefined),
  };
}

const PROJECT_FORM: ICreateProjectFormState = {
  projectTypeId: '',
  projectTypeName: '',
  practiceAreaId: '',
  practiceAreaName: '',
  projectName: 'Test Project',
  assignedAttorneyId: '',
  assignedAttorneyName: '',
  assignedParalegalId: '',
  assignedParalegalName: '',
  assignedOutsideCounselId: '',
  assignedOutsideCounselName: '',
  description: '',
  isSecure: false,
};

const MATTER_FORM: ICreateMatterFormState = {
  matterTypeId: '',
  matterTypeName: '',
  practiceAreaId: '',
  practiceAreaName: '',
  matterName: 'Test Matter',
  assignedAttorneyId: '',
  assignedAttorneyName: '',
  assignedParalegalId: '',
  assignedParalegalName: '',
  assignedOutsideCounselId: '',
  assignedOutsideCounselName: '',
  summary: '',
};

describe('create wizards show the sentence for a privilege refusal and log the full error', () => {
  let errorSpy: jest.SpyInstance;

  beforeEach(() => {
    (global as any).fetch = jest.fn().mockResolvedValue({ ok: true, json: async () => ({ value: [] }) });
    errorSpy = jest.spyOn(console, 'error').mockImplementation(() => undefined);
    jest.spyOn(console, 'info').mockImplementation(() => undefined);
    jest.spyOn(console, 'warn').mockImplementation(() => undefined);
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('Project: the sentence, no template, and the full error object in console.error', async () => {
    const result = await new ProjectService(rejectingDataService(PRIVILEGE_ERROR)).createProject(PROJECT_FORM);

    expect(result.success).toBe(false);
    expect(result.errorMessage).toBe(NEUTRAL_SENTENCE('project'));
    expect(errorSpy).toHaveBeenCalledWith('[ProjectService] createRecord error:', PRIVILEGE_ERROR);
  });

  it('Project: any other error keeps the "Failed to create project record" message', async () => {
    const result = await new ProjectService(rejectingDataService(new Error('Dataverse boom'))).createProject(
      PROJECT_FORM
    );

    expect(result.errorMessage).toBe('Failed to create project record: Dataverse boom');
  });

  it.each([
    ['AppendTo on a reference table', missing('prvAppendTosprk_ProjectType_Ref'), LINK_SENTENCE('project')],
    ['Create on the project itself', missing('prvCreatesprk_Project'), CREATE_SENTENCE('project')],
  ])('Project: %s gets its own sentence', async (_label, err, sentence) => {
    const result = await new ProjectService(rejectingDataService(err)).createProject(PROJECT_FORM);

    expect(result.errorMessage).toBe(sentence);
  });

  it('Matter: the sentence, no template, and the full error object in console.error', async () => {
    const authFetch = jest
      .fn()
      .mockResolvedValue({ ok: true, status: 200, json: async () => ({}), text: async () => '' });
    const service = new MatterService(
      rejectingDataService(PRIVILEGE_ERROR),
      authFetch as unknown as typeof fetch,
      'https://example.test',
      'container'
    );

    const result = await service.createMatter(MATTER_FORM, [], {});

    expect(result.status).toBe('error');
    expect(result.errorMessage).toBe(NEUTRAL_SENTENCE('matter'));
    expect(errorSpy).toHaveBeenCalledWith('[MatterService] createRecord error:', PRIVILEGE_ERROR);
  });
});

// ---------------------------------------------------------------------------
// The BFF-routed create wizards, end to end through their service and the BFF's 403
// ---------------------------------------------------------------------------

/** The BFF's refusal by the caller's own rights: ChildRecordEndpoints' 403 ProblemDetails (its detail names the privilege). */
const BFF_DENIED = {
  type: 'https://tools.ietf.org/html/rfc9110#section-15.5.4',
  title: 'Not permitted',
  status: 403,
  detail: "You do not have permission to create records in 'sprk_x' (prvAppendTosprk_Organization).",
  reasonCode: 'child_record.denied',
};

const CHILD_CREATE = /\/api\/v1\/child-records\/[a-z_]+$/i;

/**
 * `authenticatedFetch` in its production shape (it THROWS ApiError for a non-2xx): the child-record create answers the
 * BFF's 403; any other BFF call (field mapping, defaults) answers 200 with an empty body.
 */
function bffDenyingFetch() {
  return throwingAuthenticatedFetch(async (url: string, init?: RequestInit) =>
    CHILD_CREATE.test(url) && (init?.method ?? 'GET').toUpperCase() === 'POST'
      ? fakeResponse(403, BFF_DENIED)
      : fakeResponse(200, {})
  );
}

function readOnlyDataService(): IDataService {
  return {
    createRecord: jest.fn().mockRejectedValue(new Error('a child create must not reach Xrm.WebApi')),
    retrieveRecord: jest.fn().mockResolvedValue({}),
    retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
    updateRecord: jest.fn().mockResolvedValue(undefined),
    deleteRecord: jest.fn().mockResolvedValue(undefined),
  };
}

const BFF = 'https://bff.test';

type DenyingFetch = ReturnType<typeof bffDenyingFetch>;

describe('the BFF-routed create wizards show the neutral sentence for the BFF refusal, never its detail', () => {
  beforeEach(() => {
    (global as any).fetch = jest.fn().mockResolvedValue({ ok: true, json: async () => ({ value: [] }) });
    jest.spyOn(console, 'error').mockImplementation(() => undefined);
    jest.spyOn(console, 'info').mockImplementation(() => undefined);
    jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    jest.spyOn(console, 'log').mockImplementation(() => undefined);
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  const cases: Array<[string, string, (f: DenyingFetch) => Promise<{ errorMessage?: string }>]> = [
    [
      'Event',
      'event',
      f => new EventService(readOnlyDataService(), f, BFF).createEvent({ ...EMPTY_EVENT_FORM, eventName: 'Hearing' }),
    ],
    [
      'Invoice',
      'invoice',
      f =>
        new InvoiceService(readOnlyDataService(), f, BFF).createInvoice(
          { ...buildEmptyInvoiceForm(), name: 'INV' },
          null,
          []
        ),
    ],
    [
      'Report Card',
      'report card',
      f =>
        new ReportCardService(readOnlyDataService(), f, BFF).createReportCard(
          { ...buildEmptyReportCardForm(), name: 'Q3' },
          null
        ),
    ],
    [
      'To Do',
      'to do',
      f => new TodoService(readOnlyDataService(), f, BFF).createTodo({ ...EMPTY_TODO_FORM, title: 'Call' }),
    ],
    [
      'Work Assignment',
      'work assignment',
      f =>
        new WorkAssignmentService(readOnlyDataService(), f, BFF).createWorkAssignment(
          { ...EMPTY_WORK_ASSIGNMENT_FORM, name: 'Review', assignWithoutRecord: true },
          [],
          []
        ),
    ],
  ];

  it.each(cases)('%s', async (_label, noun, create) => {
    const fetch = bffDenyingFetch();

    const result = await create(fetch);

    // The save went to the BFF's child-record route and was refused there.
    expect(fetch.mock.calls.some(([url, init]) => CHILD_CREATE.test(url) && init?.method === 'POST')).toBe(true);
    expect(result.errorMessage).toBe(NEUTRAL_SENTENCE(noun));
    expect(result.errorMessage).not.toMatch(/prv|sprk_/i);
  });
});
