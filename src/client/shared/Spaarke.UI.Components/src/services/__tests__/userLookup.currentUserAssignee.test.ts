/**
 * userLookup.currentUserAssignee.test.ts
 *
 * Unit tests for the "Assign to me" current-user resolution helpers added by
 * spaarkeai-assistant-enhancements-r1 task 014 / FR-A4:
 *   - getCurrentUserAsLookupItem  (systemuser identity, straight from Xrm)
 *   - resolveCurrentUserAsContactAssignee (systemuser -> email -> contact chain)
 *
 * @see services/userLookup.ts
 */
import { getCurrentUserAsLookupItem, resolveCurrentUserAsContactAssignee } from '../userLookup';
import type { IDataService } from '../../types/serviceInterfaces';

// ---------------------------------------------------------------------------
// Test helpers
// ---------------------------------------------------------------------------

function makeDataService(overrides?: Partial<IDataService>): IDataService {
  return {
    createRecord: jest.fn(),
    retrieveRecord: jest.fn(),
    retrieveMultipleRecords: jest.fn(),
    updateRecord: jest.fn(),
    deleteRecord: jest.fn(),
    ...overrides,
  } as unknown as IDataService;
}

const USER_GUID = '12345678-1234-1234-1234-123456789012';
const CONTACT_ID = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee';

describe('getCurrentUserAsLookupItem', () => {
  const originalXrm = (window as any).Xrm;

  afterEach(() => {
    if (originalXrm) {
      (window as any).Xrm = originalXrm;
    } else {
      delete (window as any).Xrm;
    }
  });

  it('returns the current user id + name from Xrm context', () => {
    (window as any).Xrm = {
      WebApi: { retrieveMultipleRecords: jest.fn() },
      Utility: {
        getGlobalContext: () => ({
          userSettings: { userId: USER_GUID, userName: 'Jane Attorney', languageId: 1033 },
          getClientUrl: () => 'https://test.crm.dynamics.com',
          getCurrentAppUrl: () => 'https://test.crm.dynamics.com',
          getVersion: () => '9.2.0',
        }),
      },
    };

    expect(getCurrentUserAsLookupItem()).toEqual({ id: USER_GUID, name: 'Jane Attorney' });
  });

  it('returns null when no Xrm host context is reachable', () => {
    delete (window as any).Xrm;
    expect(getCurrentUserAsLookupItem()).toBeNull();
  });
});

describe('resolveCurrentUserAsContactAssignee', () => {
  it('returns null when the current user id cannot be resolved', async () => {
    const dataService = makeDataService();
    const result = await resolveCurrentUserAsContactAssignee(dataService, { getCurrentUserId: () => undefined });

    expect(result).toBeNull();
    expect(dataService.retrieveRecord).not.toHaveBeenCalled();
  });

  it('resolves the current user to their contact record via email match', async () => {
    const retrieveRecord = jest.fn().mockResolvedValue({ internalemailaddress: 'jane@example.com' });
    const retrieveMultipleRecords = jest.fn().mockResolvedValue({
      entities: [{ contactid: CONTACT_ID, fullname: 'Jane Attorney' }],
    });
    const dataService = makeDataService({ retrieveRecord, retrieveMultipleRecords });

    const result = await resolveCurrentUserAsContactAssignee(dataService, {
      getCurrentUserId: () => USER_GUID,
    });

    expect(result).toEqual({ id: CONTACT_ID, name: 'Jane Attorney' });
    expect(retrieveRecord).toHaveBeenCalledWith('systemuser', USER_GUID, '?$select=internalemailaddress,fullname');
    expect(retrieveMultipleRecords).toHaveBeenCalledWith(
      'contact',
      expect.stringContaining("emailaddress1 eq 'jane@example.com'")
    );
  });

  it('returns null when the systemuser record has no email', async () => {
    const retrieveRecord = jest.fn().mockResolvedValue({});
    const retrieveMultipleRecords = jest.fn();
    const dataService = makeDataService({ retrieveRecord, retrieveMultipleRecords });

    const result = await resolveCurrentUserAsContactAssignee(dataService, { getCurrentUserId: () => USER_GUID });

    expect(result).toBeNull();
    expect(retrieveMultipleRecords).not.toHaveBeenCalled();
  });

  it('returns null when no contact matches the resolved email', async () => {
    const retrieveRecord = jest.fn().mockResolvedValue({ internalemailaddress: 'jane@example.com' });
    const retrieveMultipleRecords = jest.fn().mockResolvedValue({ entities: [] });
    const dataService = makeDataService({ retrieveRecord, retrieveMultipleRecords });

    const result = await resolveCurrentUserAsContactAssignee(dataService, { getCurrentUserId: () => USER_GUID });

    expect(result).toBeNull();
  });

  it('degrades gracefully (returns null) when the data service throws', async () => {
    const retrieveRecord = jest.fn().mockRejectedValue(new Error('network error'));
    const dataService = makeDataService({ retrieveRecord });

    const result = await resolveCurrentUserAsContactAssignee(dataService, { getCurrentUserId: () => USER_GUID });

    expect(result).toBeNull();
  });

  it('strips braces from a braced current-user GUID before querying systemuser', async () => {
    const retrieveRecord = jest.fn().mockResolvedValue({ internalemailaddress: 'jane@example.com' });
    const retrieveMultipleRecords = jest.fn().mockResolvedValue({
      entities: [{ contactid: CONTACT_ID, fullname: 'Jane Attorney' }],
    });
    const dataService = makeDataService({ retrieveRecord, retrieveMultipleRecords });

    await resolveCurrentUserAsContactAssignee(dataService, {
      getCurrentUserId: () => `{${USER_GUID}}`,
    });

    expect(retrieveRecord).toHaveBeenCalledWith('systemuser', USER_GUID, '?$select=internalemailaddress,fullname');
  });
});
