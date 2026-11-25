/**
 * createXrmEmailComposeHandlers.attachmentCreate.test.ts
 *
 * field-mapping-server-write-path-r1 task 028 (FR-40, task 041): the `sprk_document` the composer creates for a locally
 * picked attachment carries no search-index value. The BFF stamps the index when it creates the document, so the client
 * sends neither `sprk_searchindexname` nor the `sprk_AI_Search_Index` bind, and no longer reads the user's
 * business-unit defaults to compute them.
 */
import { createXrmEmailComposeHandlers } from '../createXrmEmailComposeHandlers';

const mockCreateEntityRecord = jest.fn();
const mockResolveUserBuDefaults = jest.fn();

jest.mock('../../../services/EntityCreationService', () => ({
  EntityCreationService: class {
    static resolveUserBuDefaults = (...args: unknown[]) => mockResolveUserBuDefaults(...args);
    uploadFilesWithoutRecord = jest.fn().mockResolvedValue({
      uploadedFiles: [{ id: 'drive-item-1', name: 'terms.pdf', size: 3, webUrl: 'https://files.example/terms.pdf' }],
      errors: [],
    });
    createEntityRecord = (...args: unknown[]) => mockCreateEntityRecord(...args);
    attachUploadedFile = jest.fn().mockResolvedValue(undefined);
  },
}));

type XrmShim = { WebApi: { deleteRecord: jest.Mock }; Utility: { getGlobalContext: () => unknown } };

function installXrm(): void {
  const xrm: XrmShim = {
    WebApi: { deleteRecord: jest.fn() },
    Utility: { getGlobalContext: () => ({ userSettings: { userId: '{11111111-1111-1111-1111-111111111111}' } }) },
  };
  (globalThis as unknown as { Xrm?: XrmShim }).Xrm = xrm;
  (window as unknown as { Xrm?: XrmShim }).Xrm = xrm;
}

describe('createXrmEmailComposeHandlers - attachment sprk_document create (FR-40)', () => {
  beforeEach(() => {
    mockCreateEntityRecord.mockReset().mockResolvedValue('22222222-2222-2222-2222-222222222222');
    mockResolveUserBuDefaults.mockReset().mockResolvedValue({
      searchIndexName: 'legacy-index',
      searchIndexId: '33333333-3333-3333-3333-333333333333',
    });
    installXrm();
  });

  afterEach(() => {
    delete (globalThis as unknown as { Xrm?: XrmShim }).Xrm;
    delete (window as unknown as { Xrm?: XrmShim }).Xrm;
  });

  it('creates the document with its name, file name and size only - no search index name, no index bind', async () => {
    const handlers = createXrmEmailComposeHandlers({
      authenticatedFetch: jest.fn(),
      bffBaseUrl: 'https://bff.example.com',
    });

    const file = new File(['abc'], 'terms.pdf', { type: 'application/pdf' });
    const result = await handlers.onUploadLocalAttachment!(file);

    expect(result.documentId).toBe('22222222-2222-2222-2222-222222222222');
    expect(mockCreateEntityRecord).toHaveBeenCalledTimes(1);
    const [table, payload] = mockCreateEntityRecord.mock.calls[0] as [string, Record<string, unknown>];
    expect(table).toBe('sprk_document');
    expect(payload).toEqual({ sprk_documentname: 'terms.pdf', sprk_filename: 'terms.pdf', sprk_filesize: 3 });
    expect(Object.keys(payload)).not.toContain('sprk_searchindexname');
    expect(Object.keys(payload)).not.toContain('sprk_AI_Search_Index@odata.bind');
  });

  it("does not read the user's business-unit defaults for the document", async () => {
    const handlers = createXrmEmailComposeHandlers({
      authenticatedFetch: jest.fn(),
      bffBaseUrl: 'https://bff.example.com',
    });

    await handlers.onUploadLocalAttachment!(new File(['abc'], 'terms.pdf'));

    expect(mockResolveUserBuDefaults).not.toHaveBeenCalled();
  });
});
