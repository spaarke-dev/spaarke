/**
 * createXrmEmailComposeHandlers.discard.test.ts — spaarke-ontology-platform-r1 task 220 (#2813 DI-154).
 *
 * The Xrm upload handler hands back a `discard` that deletes exactly the `sprk_document` row it created, through the
 * delete it already uses when the attach step fails (`Xrm.WebApi.deleteRecord`). Nothing is deleted until the composer
 * calls it.
 */
import { createXrmEmailComposeHandlers } from '../createXrmEmailComposeHandlers';

const mockGetXrm = jest.fn();
jest.mock('../../../utils/xrmContext', () => ({
  ...jest.requireActual('../../../utils/xrmContext'),
  getXrm: () => mockGetXrm(),
}));

const CREATED = 'aaaaaaaa-1111-2222-3333-444444444444';
jest.mock('../../../services/EntityCreationService', () => ({
  EntityCreationService: class {
    static async resolveUserBuDefaults() {
      return {};
    }
    async uploadFilesWithoutRecord() {
      return {
        uploadedFiles: [{ id: 'drive-item-1', name: 'fees.pdf', size: 8, webUrl: 'https://spe.example.com/f' }],
        errors: [],
      };
    }
    async createEntityRecord() {
      return CREATED;
    }
    async attachUploadedFile() {
      return undefined;
    }
  },
}));

function xrmWith(deleteRecord: jest.Mock) {
  return {
    WebApi: { deleteRecord },
    Utility: { getGlobalContext: () => ({ userSettings: { userId: '{bbbbbbbb-1111-2222-3333-444444444444}' } }) },
  };
}

describe('createXrmEmailComposeHandlers — onUploadLocalAttachment discard (task 220)', () => {
  beforeEach(() => mockGetXrm.mockReset());

  it('returns a discard that deletes the sprk_document it created, and only when called', async () => {
    const deleteRecord = jest.fn().mockResolvedValue({ id: CREATED });
    mockGetXrm.mockReturnValue(xrmWith(deleteRecord));
    const handlers = createXrmEmailComposeHandlers({
      authenticatedFetch: jest.fn(),
      bffBaseUrl: 'https://bff.example.com',
    });

    const uploaded = await handlers.onUploadLocalAttachment!(
      new File(['%PDF'], 'fees.pdf', { type: 'application/pdf' })
    );

    expect(uploaded.documentId).toBe(CREATED);
    expect(deleteRecord).not.toHaveBeenCalled();
    expect(typeof uploaded.discard).toBe('function');

    await uploaded.discard!();

    expect(deleteRecord).toHaveBeenCalledTimes(1);
    expect(deleteRecord).toHaveBeenCalledWith('sprk_document', CREATED);
  });

  it('a delete Dataverse refuses rejects the discard (the composer logs it and leaves the document)', async () => {
    const deleteRecord = jest
      .fn()
      .mockRejectedValue(new Error('The object you tried to delete is associated with another object'));
    mockGetXrm.mockReturnValue(xrmWith(deleteRecord));
    const handlers = createXrmEmailComposeHandlers({
      authenticatedFetch: jest.fn(),
      bffBaseUrl: 'https://bff.example.com',
    });

    const uploaded = await handlers.onUploadLocalAttachment!(
      new File(['%PDF'], 'fees.pdf', { type: 'application/pdf' })
    );

    await expect(uploaded.discard!()).rejects.toThrow(/associated with another object/);
  });
});
