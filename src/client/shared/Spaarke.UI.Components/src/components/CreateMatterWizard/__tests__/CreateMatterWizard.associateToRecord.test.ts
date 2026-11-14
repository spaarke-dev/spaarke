/**
 * CreateMatterWizard — associateToRecord() tests
 *
 * Scope: the association picker offers Project / Account / none. An invoice is
 * not offered and is never associated: its matter, project or work assignment
 * is fixed when it is created, and a wrongly filed invoice is returned to the
 * firm and resubmitted on the right record (EB-D-231;
 * field-mapping-server-write-path-r1 task 045g). Project/Account behavior is
 * pinned unchanged.
 *
 * @see ../CreateMatterWizard.tsx (associateToRecord — exported for testing)
 */
import { associateToRecord } from '../CreateMatterWizard';
import type { IDataService } from '../../../types/serviceInterfaces';
import type { AssociationResult } from '../../AssociateToStep/types';

const MATTER_ID = '11111111-1111-1111-1111-111111111111';
const INVOICE_ID_RAW = '{22222222-2222-2222-2222-222222222222}';

function makeDataService(overrides?: Partial<IDataService>): IDataService {
  return {
    createRecord: jest.fn(),
    retrieveRecord: jest.fn(),
    retrieveMultipleRecords: jest.fn(),
    updateRecord: jest.fn().mockResolvedValue(undefined),
    deleteRecord: jest.fn(),
    ...overrides,
  } as unknown as IDataService;
}

describe('associateToRecord — an invoice is never re-filed under the new matter (EB-D-231, task 045g)', () => {
  it('writes nothing for an invoice association (no update, no fetch)', async () => {
    const updateRecord = jest.fn().mockResolvedValue(undefined);
    const fetchMock = jest.fn();
    const realFetch = global.fetch;
    (global as unknown as { fetch: unknown }).fetch = fetchMock;
    const spy = jest.spyOn(console, 'warn').mockImplementation(() => {});
    const association: AssociationResult = {
      entityType: 'sprk_invoice',
      recordId: INVOICE_ID_RAW,
      recordName: 'INV-2026-0007',
    };

    try {
      await associateToRecord(makeDataService({ updateRecord }), MATTER_ID, association);

      expect(updateRecord).not.toHaveBeenCalled();
      expect(fetchMock).not.toHaveBeenCalled();
    } finally {
      spy.mockRestore();
      (global as unknown as { fetch: unknown }).fetch = realFetch;
    }
  });
});

describe('associateToRecord — existing Project/Account behavior unchanged', () => {
  it('still uses N:N $ref for sprk_project', async () => {
    (global as unknown as { fetch: unknown }).fetch = jest.fn().mockResolvedValue({ ok: true } as Response);
    const dataService = makeDataService();
    const association: AssociationResult = {
      entityType: 'sprk_project',
      recordId: '33333333-3333-3333-3333-333333333333',
      recordName: 'Acme Renewal',
    };

    const result = await associateToRecord(dataService, MATTER_ID, association);

    expect(result.success).toBe(true);
    expect(dataService.updateRecord).not.toHaveBeenCalled();
  });

  it('still binds sprk_Account@odata.bind directly on the matter for account', async () => {
    const updateRecord = jest.fn().mockResolvedValue(undefined);
    const dataService = makeDataService({ updateRecord });
    const association: AssociationResult = {
      entityType: 'account',
      recordId: '44444444-4444-4444-4444-444444444444',
      recordName: 'Acme Corp',
    };

    const result = await associateToRecord(dataService, MATTER_ID, association);

    expect(result.success).toBe(true);
    expect(updateRecord).toHaveBeenCalledWith('sprk_matter', MATTER_ID, {
      'sprk_Account@odata.bind': `/accounts(44444444-4444-4444-4444-444444444444)`,
    });
  });
});
