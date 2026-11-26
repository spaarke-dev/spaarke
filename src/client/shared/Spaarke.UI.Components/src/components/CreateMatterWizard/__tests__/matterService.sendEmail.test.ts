/**
 * matterService.sendEmail.test.ts - spaarke-ontology-platform-r1 task 182, fix round 2.
 *
 * The Matter wizard's Finish sends the composed email (the Send Email step's `IComposedEmail`) through
 * `sendComposedEmail`, filed under the matter it just created. These tests fail if the composed email stops reaching
 * the send: To, Cc, Bcc, From and the body format are asserted on the request, and the association is the new matter.
 */
import { MatterService } from '../matterService';
import { _resetNavPropCacheForTests } from '../../../services/PolymorphicResolverService';
import { EMPTY_COMPOSED_EMAIL } from '../../EmailComposer/composeOnlySend';
import type { IComposedEmail } from '../../EmailComposer/composeOnlySend';
import type { ICreateMatterFormState } from '../formTypes';
import type { IDataService } from '../../../types/serviceInterfaces';

const BFF = 'https://example.test';
const MATTER_ID = 'created-matter-guid';

const FORM: ICreateMatterFormState = {
  matterTypeId: '',
  matterTypeName: '',
  practiceAreaId: '',
  practiceAreaName: '',
  matterName: 'Acme v. Widgets',
  assignedAttorneyId: '',
  assignedAttorneyName: '',
  assignedParalegalId: '',
  assignedParalegalName: '',
  assignedOutsideCounselId: '',
  assignedOutsideCounselName: '',
  summary: '',
};

const EMAIL: IComposedEmail = {
  ...EMPTY_COMPOSED_EMAIL,
  to: [{ email: 'client@acme.example', resolved: false }],
  cc: [{ email: 'partner@firm.example', resolved: false }],
  bcc: [{ email: 'records@firm.example', resolved: false }],
  subject: 'New matter: Acme v. Widgets',
  body: 'Dear Client,\n\nThe matter is open.',
  bodyFormat: 'PlainText',
  sendMode: 'user',
};

const dataService: IDataService = {
  createRecord: jest.fn().mockResolvedValue('unused'),
  retrieveRecord: jest.fn().mockResolvedValue({}),
  retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
  updateRecord: jest.fn().mockResolvedValue(undefined),
  deleteRecord: jest.fn().mockResolvedValue(undefined),
};

function bff(sendResponse: { ok: boolean; status: number; body: unknown }) {
  const sends: Array<Record<string, unknown>> = [];
  const authFetch = jest.fn(async (url: string, init?: RequestInit) => {
    if (String(url).startsWith(`${BFF}/api/v1/child-records/sprk_matter`)) {
      return { ok: true, status: 201, json: async () => ({ id: MATTER_ID }) } as unknown as Response;
    }
    if (String(url).includes('/api/communications/send')) {
      sends.push(JSON.parse(init?.body as string));
      return {
        ok: sendResponse.ok,
        status: sendResponse.status,
        headers: { get: () => 'application/json' },
        json: async () => sendResponse.body,
      } as unknown as Response;
    }
    return { ok: true, status: 200, json: async () => ({}) } as unknown as Response;
  });
  return { authFetch, sends };
}

describe('MatterService Finish send (task 182)', () => {
  const originalFetch = globalThis.fetch;
  beforeEach(() => {
    _resetNavPropCacheForTests();
    globalThis.fetch = jest.fn(async () => ({ ok: true, status: 200, json: async () => ({ value: [] }) })) as never;
  });
  afterEach(() => {
    globalThis.fetch = originalFetch;
    _resetNavPropCacheForTests();
  });

  it('sends the composed email from the chosen mailbox with Cc and Bcc, filed under the new matter', async () => {
    const { authFetch, sends } = bff({ ok: true, status: 200, body: { communicationId: 'c-1' } });
    const service = new MatterService(dataService, authFetch, BFF);

    const result = await service.createMatter(FORM, [], { sendEmail: { email: EMAIL } });

    expect(result.status).toBe('success');
    expect(sends).toHaveLength(1);
    expect(sends[0]).toEqual(
      expect.objectContaining({
        to: ['client@acme.example'],
        cc: ['partner@firm.example'],
        bcc: ['records@firm.example'],
        subject: 'New matter: Acme v. Widgets',
        body: 'Dear Client,\n\nThe matter is open.',
        bodyFormat: 'PlainText',
        sendMode: 'User',
      })
    );
    expect(sends[0].associations).toEqual([
      expect.objectContaining({ entityType: 'sprk_matter', entityId: MATTER_ID, entityName: 'Acme v. Widgets' }),
    ]);
  });

  it('a failed send keeps the matter and adds a readable warning (never "HTTP 500")', async () => {
    const { authFetch } = bff({ ok: false, status: 500, body: {} });
    const service = new MatterService(dataService, authFetch, BFF);

    const result = await service.createMatter(FORM, [], { sendEmail: { email: EMAIL } });

    expect(result.status).toBe('partial');
    expect(result.matterId).toBe(MATTER_ID);
    expect(result.warnings).toEqual(["The email was not sent. The email service couldn't send it right now."]);
    expect(result.warnings.join(' ')).not.toMatch(/HTTP 500/);
  });
});
