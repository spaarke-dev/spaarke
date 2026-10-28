/**
 * describeWebApiCreateError (issue #2382): a create wizard showed Dataverse's privilege template with "{0}..{6}"
 * unfilled. A privilege or access refusal now becomes one plain sentence naming no ids, users or privilege names; any
 * other error keeps a clean message. Covers the helper, the two Xrm.WebApi create wizards end to end (Project,
 * Matter), and that every create wizard's save uses the helper.
 */

import * as fs from 'fs';
import * as path from 'path';
import { describeWebApiCreateError } from '../webApiCreateError';
import { ProjectService } from '../../components/CreateProjectWizard/projectService';
import { MatterService } from '../../components/CreateMatterWizard/matterService';
import type { ICreateProjectFormState } from '../../components/CreateProjectWizard/projectFormTypes';
import type { ICreateMatterFormState } from '../../components/CreateMatterWizard/formTypes';
import type { IDataService } from '../../types/serviceInterfaces';

// The shape Xrm.WebApi rejects with for a missing AppendTo, with the template unfilled.
const PRIVILEGE_ERROR = {
  errorCode: -2147220960,
  message:
    '{0} with id {1} does not have {2} right(s) for record with id {3} of entity {4}. Consider assigning a role with ' +
    'the required access level to the user or team. More details: {6}',
  code: 0x80040220,
};

const SENTENCE = (noun: string) =>
  `You don't have permission to link one of the selected people or organizations to this ${noun}. Ask your administrator.`;

describe('describeWebApiCreateError', () => {
  it('turns the 0x80040220 template into the plain sentence', () => {
    expect(describeWebApiCreateError(PRIVILEGE_ERROR, 'project')).toEqual({
      message: SENTENCE('project'),
      isAccessDenied: true,
    });
  });

  it.each([
    ['signed errorCode only', { errorCode: -2147220960, message: 'x' }],
    ['unsigned errorCode', { errorCode: 2147746336, message: 'x' }],
    ['hex string code', { code: '0x80040220', message: 'x' }],
    [
      'a filled message with ids and a privilege name',
      new Error(
        'Principal user (Id=11111111-1111-1111-1111-111111111111, type=8) is missing prvAppendToContact privilege (Id=22222222-2222-2222-2222-222222222222)'
      ),
    ],
    [
      'a filled "does not have ... right(s)" message',
      { message: 'SystemUser with id 1 does not have AppendTo right(s) for record' },
    ],
    ['a SecLib message', { message: 'SecLib::AccessCheckEx failed. Returned hr = -2147220960' }],
  ])('recognises a privilege refusal: %s', (_label, err) => {
    const out = describeWebApiCreateError(err, 'matter');
    expect(out.isAccessDenied).toBe(true);
    expect(out.message).toBe(SENTENCE('matter'));
  });

  it('never names ids, users or privileges in the sentence', () => {
    const out = describeWebApiCreateError(
      new Error(
        'Principal user (Id=11111111-1111-1111-1111-111111111111) is missing prvAppendTosprk_Organization privilege'
      ),
      'project'
    );
    expect(out.message).not.toMatch(/[0-9a-f]{8}-[0-9a-f]{4}/i);
    expect(out.message).not.toMatch(/prv/i);
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
    expect(result.errorMessage).toBe(SENTENCE('project'));
    expect(errorSpy).toHaveBeenCalledWith('[ProjectService] createRecord error:', PRIVILEGE_ERROR);
  });

  it('Project: any other error keeps the "Failed to create project record" message', async () => {
    const result = await new ProjectService(rejectingDataService(new Error('Dataverse boom'))).createProject(
      PROJECT_FORM
    );

    expect(result.errorMessage).toBe('Failed to create project record: Dataverse boom');
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
    expect(result.errorMessage).toBe(SENTENCE('matter'));
    expect(errorSpy).toHaveBeenCalledWith('[MatterService] createRecord error:', PRIVILEGE_ERROR);
  });
});

// ---------------------------------------------------------------------------
// Every create wizard's save uses the helper (the BFF-routed ones too)
// ---------------------------------------------------------------------------

describe('every create wizard formats its save error through describeWebApiCreateError', () => {
  const components = path.join(__dirname, '..', '..', 'components');

  it.each([
    ['CreateEventWizard/eventService.ts', 'event'],
    ['CreateInvoiceWizard/invoiceService.ts', 'invoice'],
    ['CreateMatterWizard/matterService.ts', 'matter'],
    ['CreateProjectWizard/projectService.ts', 'project'],
    ['CreateReportCardWizard/reportCardService.ts', 'report card'],
    ['CreateTodoWizard/todoService.ts', 'to do'],
    ['CreateWorkAssignmentWizard/workAssignmentService.ts', 'work assignment'],
  ])('%s', (file, noun) => {
    const source = fs.readFileSync(path.join(components, file), 'utf8');
    expect(source).toContain(`describeWebApiCreateError(err, '${noun}')`);
    expect(source).not.toMatch(/errObj\?\.message/);
  });
});
