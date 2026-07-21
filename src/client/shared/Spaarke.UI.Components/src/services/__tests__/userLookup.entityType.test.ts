/**
 * userLookup.entityType.test.ts (task 060, FR-10)
 *
 * `searchUsersAsLookup`/`searchContactsAsLookup`/`searchUsersAndContacts` tag
 * each result's `ILookupItem.entityType` at the source table so downstream
 * consumers (`RecipientField` → `IRecipient.entityType`, task 060) can tell a
 * directory-resolved `systemuser` from a `contact` without a second lookup.
 *
 * @see services/userLookup.ts
 */
import { searchUsersAsLookup, searchContactsAsLookup, searchUsersAndContacts } from '../userLookup';
import type { IDataService } from '../../types/serviceInterfaces';

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

describe('searchUsersAsLookup — entityType tagging', () => {
  it('tags every result entityType: "systemuser"', async () => {
    const retrieveMultipleRecords = jest.fn().mockResolvedValue({
      entities: [{ systemuserid: 'user-1', fullname: 'Jane Attorney', internalemailaddress: 'jane@example.com' }],
    });
    const dataService = makeDataService({ retrieveMultipleRecords });

    const result = await searchUsersAsLookup(dataService, 'jane');

    expect(result).toEqual([
      { id: 'user-1', name: 'Jane Attorney (jane@example.com)', entityType: 'systemuser' },
    ]);
  });
});

describe('searchContactsAsLookup — entityType tagging', () => {
  it('tags every result entityType: "contact"', async () => {
    const retrieveMultipleRecords = jest.fn().mockResolvedValue({
      entities: [{ contactid: 'contact-1', fullname: 'Erin Example', emailaddress1: 'erin@example.com' }],
    });
    const dataService = makeDataService({ retrieveMultipleRecords });

    const result = await searchContactsAsLookup(dataService, 'erin');

    expect(result).toEqual([{ id: 'contact-1', name: 'Erin Example (erin@example.com)', entityType: 'contact' }]);
  });
});

describe('searchUsersAndContacts — merged results preserve per-source entityType', () => {
  it('preserves entityType through the merge + de-dupe pass', async () => {
    const retrieveMultipleRecords = jest.fn().mockImplementation((table: string) => {
      if (table === 'systemuser') {
        return Promise.resolve({
          entities: [{ systemuserid: 'user-1', fullname: 'Jane Attorney', internalemailaddress: 'jane@example.com' }],
        });
      }
      return Promise.resolve({
        entities: [{ contactid: 'contact-1', fullname: 'Erin Example', emailaddress1: 'erin@example.com' }],
      });
    });
    const dataService = makeDataService({ retrieveMultipleRecords });

    const result = await searchUsersAndContacts(dataService, 'ex');

    expect(result).toEqual(
      expect.arrayContaining([
        expect.objectContaining({ id: 'user-1', entityType: 'systemuser' }),
        expect.objectContaining({ id: 'contact-1', entityType: 'contact' }),
      ])
    );
  });
});
