/**
 * CreateMatterWizard — associateToRecord() invoice-association tests
 * (spaarkeai-assistant-enhancements-r1 task 014 / FR-A5)
 *
 * Scope: the association picker offers Project / Account / Invoice / none.
 * Project + Account were pre-existing; this file covers the NEW Invoice
 * branch (reverse-direction update: the SELECTED invoice's own `sprk_Matter`
 * lookup is set to the newly created matter) plus a light regression check
 * that Project/Account/unsupported behavior is unchanged.
 *
 * @see ../CreateMatterWizard.tsx (associateToRecord — exported for testing)
 */
import { associateToRecord } from '../CreateMatterWizard';
import type { IDataService } from '../../../types/serviceInterfaces';
import type { AssociationResult } from '../../AssociateToStep/types';

const MATTER_ID = '11111111-1111-1111-1111-111111111111';
const INVOICE_ID_RAW = '{22222222-2222-2222-2222-222222222222}';
const INVOICE_ID_CLEAN = '22222222-2222-2222-2222-222222222222';

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

describe('associateToRecord — sprk_invoice (task 014 / FR-A5)', () => {
  it("updates the SELECTED invoice's sprk_Matter lookup to point at the new matter", async () => {
    const updateRecord = jest.fn().mockResolvedValue(undefined);
    const dataService = makeDataService({ updateRecord });
    const association: AssociationResult = {
      entityType: 'sprk_invoice',
      recordId: INVOICE_ID_RAW,
      recordName: 'INV-2026-0007',
    };

    const result = await associateToRecord(dataService, MATTER_ID, association);

    expect(result.success).toBe(true);
    expect(updateRecord).toHaveBeenCalledWith('sprk_invoice', INVOICE_ID_CLEAN, {
      'sprk_Matter@odata.bind': `/sprk_matters(${MATTER_ID})`,
    });
  });

  it('normalizes braced GUIDs on both the invoice and matter id', async () => {
    const updateRecord = jest.fn().mockResolvedValue(undefined);
    const dataService = makeDataService({ updateRecord });
    const association: AssociationResult = {
      entityType: 'sprk_invoice',
      recordId: '{AAAA1111-BBBB-2222-CCCC-333344445555}',
      recordName: 'INV-2026-0008',
    };

    await associateToRecord(dataService, `{${MATTER_ID.toUpperCase()}}`, association);

    expect(updateRecord).toHaveBeenCalledWith(
      'sprk_invoice',
      'aaaa1111-bbbb-2222-cccc-333344445555',
      expect.objectContaining({ 'sprk_Matter@odata.bind': `/sprk_matters(${MATTER_ID})` })
    );
  });

  it('degrades gracefully (returns success: false, never throws) when the invoice update fails', async () => {
    const updateRecord = jest.fn().mockRejectedValue(new Error('403 Forbidden'));
    const dataService = makeDataService({ updateRecord });
    const association: AssociationResult = {
      entityType: 'sprk_invoice',
      recordId: INVOICE_ID_RAW,
      recordName: 'INV-2026-0007',
    };

    const result = await associateToRecord(dataService, MATTER_ID, association);

    expect(result.success).toBe(false);
  });
});

describe('associateToRecord — existing Project/Account behavior unchanged', () => {
  it('still uses N:N $ref for sprk_project (unaffected by the Invoice addition)', async () => {
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
