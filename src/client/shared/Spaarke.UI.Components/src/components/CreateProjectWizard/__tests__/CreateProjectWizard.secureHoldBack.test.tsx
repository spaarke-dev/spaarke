/**
 * CreateProjectWizard.secureHoldBack.test.tsx — unified-access-control-r2 task 150, THE ORDERING TRAP.
 *
 * `sprk_issecure` is field-secured and the client no longer writes it: the server marks a project secure as the first
 * write of provisioning. So a secure-requested project whose provisioning stopped before that write is an ORDINARY
 * record, and a file uploaded to it — or a work assignment, event or email added to it — lands in shared storage that
 * SharePoint Embedded cannot take back. These tests run the wizard's own `onFinish` (the generic shell is a stub that
 * hands over its config; the project create, upload, child and email services are doubles) and assert:
 *   - secure requested + provisioning NOT successful (refused, retryable or not, or no BFF to ask) → no upload, no
 *     document record, no work assignment, no event, no email, and one warning naming what was held back;
 *   - secure requested + provisioning successful → everything is added, AFTER provisioning;
 *   - secure NOT requested → everything is added, and provisioning is never called.
 */
import * as React from 'react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import type { ICreateRecordWizardConfig, IFinishContext, IFollowOnState } from '../../CreateRecordWizard';
import type { IDataService } from '../../../types/serviceInterfaces';
import type { IUploadedFile } from '../../FileUpload/fileUploadTypes';
import { CreateProjectWizard } from '../CreateProjectWizard';
import { describeHeldBackForSecure } from '../provisioningService';
import { ASSIGNED_ACCESS_SYNC_PATH } from '../../../services/assignedAccessSync';
import { throwingAuthenticatedFetch } from '../../../__tests__/helpers/authenticatedFetchDouble';

const mockShell: { config?: ICreateRecordWizardConfig } = {};
jest.mock('../../CreateRecordWizard', () => ({
  CreateRecordWizard: (props: { config: ICreateRecordWizardConfig }) => {
    mockShell.config = props.config;
    return null;
  },
}));

const PROJECT_ID = '11111111-1111-1111-1111-111111111111';
const BFF = 'https://bff.example.test';

/** One ordered log of every side effect, so a test can assert what happened and in what order. */
const mockCalls: string[] = [];

jest.mock('../projectService', () => ({
  ProjectService: jest.fn().mockImplementation(() => ({
    createProject: jest.fn().mockImplementation(async () => {
      mockCalls.push('create-project');
      return { success: true, projectId: '11111111-1111-1111-1111-111111111111', projectName: 'Acme', warnings: [] };
    }),
  })),
}));

jest.mock('../../../services/EntityCreationService', () => ({
  EntityCreationService: jest.fn().mockImplementation(() => ({
    uploadFilesToSpe: jest.fn().mockImplementation(async (_e: string, _id: string, files: unknown[]) => {
      mockCalls.push('upload');
      return { uploadedFiles: files.map((_, i) => ({ id: `f${i}` })), errors: [] };
    }),
    createDocumentRecords: jest.fn().mockImplementation(async () => {
      mockCalls.push('document-records');
      return { warnings: [], createdDocumentIds: [] };
    }),
    indexUploadedFiles: jest.fn().mockResolvedValue([]),
    sendEmail: jest.fn().mockImplementation(async () => {
      mockCalls.push('email');
      return { success: true };
    }),
  })),
}));

jest.mock('../../CreateWorkAssignmentWizard/workAssignmentService', () => ({
  WorkAssignmentService: jest.fn().mockImplementation(() => ({
    createWorkAssignment: jest.fn().mockImplementation(async () => {
      mockCalls.push('work-assignment');
      return { status: 'success', warnings: [] };
    }),
  })),
}));

jest.mock('../../CreateEventWizard/eventService', () => ({
  EventService: jest.fn().mockImplementation(() => ({
    createEvent: jest.fn().mockImplementation(async () => {
      mockCalls.push('event');
      return { success: true, warnings: [] };
    }),
  })),
}));

const provisioned = {
  ok: true,
  status: 200,
  json: async () => ({
    businessUnitId: '22222222-2222-2222-2222-222222222222',
    businessUnitName: 'Secure Record',
    ownerTeamId: '33333333-3333-3333-3333-333333333333',
    ownerTeamName: 'Secure Record Owners',
    speContainerId: 'b!container',
    sharedToCreatorSystemUserId: '44444444-4444-4444-4444-444444444444',
    additionalPrincipalsShared: 0,
  }),
} as unknown as Response;

const refused = (reasonCode: string) =>
  ({ ok: false, status: 500, json: async () => ({ reasonCode, detail: 'operator text' }) }) as unknown as Response;

/** Asks for everything the wizard can add: a file, a work assignment, an event and an email. */
const everything: IFinishContext = {
  uploadedFiles: [{ id: 'u1', name: 'brief.pdf' } as unknown as IUploadedFile],
  speContainerId: '',
  selectedActions: ['assign-counsel', 'create-event', 'send-email'],
  followOn: {
    assignWorkName: 'Review',
    assignWorkDescription: '',
    createEventName: 'Kick-off',
    createEventDescription: '',
    emailTo: 'client@example.test',
    emailSubject: 'Hello',
    emailBody: 'Body',
  } as unknown as IFollowOnState,
  association: null,
  selectedExistingRecord: null,
};

const ADDS = ['work-assignment', 'event', 'upload', 'document-records', 'email'];

/**
 * The host's authenticated fetch, told apart by route: the provisioning call answers `provisionResponse`; task 142's
 * Assigned-To sync (`syncAssignedAccess`, which a secure-requested project gets right after provisioning) is logged as
 * its own entry. The sync writes no content and nothing to storage — it is not one of the ADDS this suite holds back.
 */
// authenticatedFetch THROWS ApiError for a non-2xx: the double turns a refused provision answer into that failure.
const bffFetch = (provisionResponse: Response) =>
  throwingAuthenticatedFetch(async (url: string) => {
    if (String(url).endsWith(ASSIGNED_ACCESS_SYNC_PATH)) {
      mockCalls.push('assigned-access-sync');
      return { ok: true, status: 200, json: async () => ({}) } as unknown as Response;
    }
    mockCalls.push('provision');
    return provisionResponse;
  });

async function finish(isSecure: boolean, authFetch?: jest.Mock) {
  renderWithProviders(
    <CreateProjectWizard
      open
      onClose={jest.fn()}
      dataService={{} as IDataService}
      authenticatedFetch={authFetch as never}
      bffBaseUrl={authFetch ? BFF : undefined}
      initialFormValues={{ projectName: 'Acme', isSecure }}
    />
  );
  return mockShell.config!.onFinish(everything);
}

describe('CreateProjectWizard — a secure-requested project whose provisioning did not succeed gets nothing added (task 150)', () => {
  let consoleSpies: jest.SpyInstance[];

  beforeEach(() => {
    mockShell.config = undefined;
    mockCalls.length = 0;
    consoleSpies = (['info', 'error', 'warn'] as const).map(m => jest.spyOn(console, m).mockImplementation(() => {}));
  });

  afterEach(() => consoleSpies.forEach(s => s.mockRestore()));

  it.each([
    ['an environment refusal (before the server marked it secure)', 'sdap.provision.secure_bu_not_found'],
    ['a retryable refusal', 'sdap.provision.creator_share_failed'],
    ['a failed flag write', 'sdap.provision.secure_flag_not_set'],
    ['a storage failure (marked secure, no container)', 'sdap.provision.container_creation_failed'],
  ])('%s: no file, no document record, no work assignment, no event, no email', async (_label, reasonCode) => {
    const authFetch = bffFetch(refused(reasonCode));

    const success = await finish(true, authFetch);

    expect(mockCalls).toEqual(['create-project', 'provision', 'assigned-access-sync']);
    expect(success.warnings).toContain(
      describeHeldBackForSecure(['the work assignment', 'the event', 'the files you attached', 'the email'])
    );
  });

  it('secure requested with no BFF to ask: nothing is added either', async () => {
    const success = await finish(true, undefined);

    expect(mockCalls).toEqual(['create-project']);
    expect((success.warnings ?? []).join(' ')).toMatch(/not added to it, so nothing reached shared storage/);
  });

  it('secure requested and provisioned: everything is added — AFTER provisioning', async () => {
    const authFetch = bffFetch(provisioned);

    const success = await finish(true, authFetch);

    expect(mockCalls[0]).toBe('create-project');
    expect(mockCalls[1]).toBe('provision');
    expect(mockCalls.filter(c => c === 'provision')).toHaveLength(1);
    expect(mockCalls.slice(2)).toEqual(expect.arrayContaining(ADDS));
    expect((success.warnings ?? []).join(' ')).not.toMatch(/nothing reached shared storage/);
  });

  it('secure not requested: provisioning is never called and everything is added', async () => {
    const authFetch = bffFetch(provisioned);

    await finish(false, authFetch);

    expect(mockCalls).not.toContain('provision');
    expect(mockCalls).toEqual(expect.arrayContaining(['create-project', ...ADDS]));
  });
});

describe('describeHeldBackForSecure (task 150; owner round 10 item 9, F6 row 1, option A)', () => {
  it('is silent when nothing was held back', () => {
    expect(describeHeldBackForSecure([])).toBeUndefined();
  });

  it('says exactly the copy the owner picked, in the singular and the plural', () => {
    expect(describeHeldBackForSecure(['the event'])).toBe(
      'Because securing the project did not finish, this was not added to it, so nothing reached shared storage: the event. Add it once the project is secured.'
    );
    expect(describeHeldBackForSecure(['the work assignment', 'the event', 'the files you attached'])).toBe(
      'Because securing the project did not finish, these were not added to it, so nothing reached shared storage: the work assignment, the event and the files you attached. Add them once the project is secured.'
    );
  });

  it('names one item in the singular and several in a list', () => {
    expect(describeHeldBackForSecure(['the event'])).toMatch(/this was not added to it.*: the event\. Add it once/);
    expect(describeHeldBackForSecure(['the event', 'the email'])).toMatch(
      /these were not added.*: the event and the email\. Add them/
    );
  });

  it('never advises trying again — the retry is the host action', () => {
    expect(describeHeldBackForSecure(['the event'])).not.toMatch(/try (securing )?(it )?again|retry/i);
  });
});
