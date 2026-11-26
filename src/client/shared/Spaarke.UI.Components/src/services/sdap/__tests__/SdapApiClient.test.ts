import { SdapApiClient } from '../SdapApiClient';

describe('SdapApiClient', () => {
  describe('constructor', () => {
    it('should accept valid config', () => {
      const client = new SdapApiClient({
        baseUrl: 'https://api.example.com',
      });

      expect(client).toBeDefined();
    });

    it('should throw on missing baseUrl', () => {
      expect(() => {
        new SdapApiClient({ baseUrl: '' });
      }).toThrow('baseUrl is required');
    });

    it('should throw on invalid baseUrl', () => {
      expect(() => {
        new SdapApiClient({ baseUrl: 'not-a-url' });
      }).toThrow('baseUrl must be a valid URL');
    });

    it('should throw on negative timeout', () => {
      expect(() => {
        new SdapApiClient({
          baseUrl: 'https://api.example.com',
          timeout: -1,
        });
      }).toThrow('timeout must be >= 0');
    });

    it('should use default timeout if not specified', () => {
      const client = new SdapApiClient({
        baseUrl: 'https://api.example.com',
      });

      expect(client).toBeDefined();
      // @ts-expect-error - accessing private property for testing
      expect(client.timeout).toBe(300000);
    });

    it('should remove trailing slash from baseUrl', () => {
      const client = new SdapApiClient({
        baseUrl: 'https://api.example.com/',
      });

      // @ts-expect-error - accessing private property for testing
      expect(client.baseUrl).toBe('https://api.example.com');
    });
  });

  // `describe('uploadFile')` was re-pointed 2026-09-03 (task 076): `uploadFile(containerId, …)` is
  // DELETED along with the route it called. The two surviving contracts are asserted here instead —
  // and, more usefully, that the container-keyed one has NOT quietly come back.
  describe('upload contracts', () => {
    it('exposes the record-keyed and record-less methods, and no container-keyed one', () => {
      const client = new SdapApiClient({
        baseUrl: 'https://api.example.com',
      });

      expect(client.uploadFileForRecord).toBeDefined();
      expect(client.uploadFileWithoutRecord).toBeDefined();
      expect((client as unknown as Record<string, unknown>).uploadFile).toBeUndefined();
    });
  });

  // DEF-001 (2026-11-24, owner decision on review finding P-2): downloadFile, deleteFile and
  // getFileMetadata are DELETED. They called `/api/obo/drives/{driveId}/items/{itemId}[/content]`,
  // which the BFF retired on 2026-08-26 (task 071, OboDriveKeyedRouteRetirementTests) or never mapped.
  // Reads and deletes go through the document-id-keyed routes, which authorize per document.
  describe('drive-keyed methods', () => {
    it('exposes no download, delete or metadata method keyed by drive and item id', () => {
      const client = new SdapApiClient({
        baseUrl: 'https://api.example.com',
      });
      const surface = client as unknown as Record<string, unknown>;

      expect(surface.downloadFile).toBeUndefined();
      expect(surface.deleteFile).toBeUndefined();
      expect(surface.getFileMetadata).toBeUndefined();
    });
  });

  // unified-access-control-r2 task 166 f1 (owner round 21 item 1 (i)): the client never writes a document's SPE
  // pointer — it asks the BFF to attach the file it uploaded, and the BFF stamps the pointer after verifying it.
  describe('attachDocumentFile', () => {
    const okResponse = (body: unknown) => ({ ok: true, status: 200, json: async () => body }) as unknown as Response;

    it("POSTs the uploaded file's drive and item ids to the document's attach route", async () => {
      const calls: Array<{ url: string; init?: RequestInit }> = [];
      const client = new SdapApiClient({
        baseUrl: 'https://api.example.com/',
        authenticatedFetch: async (url: string, init?: RequestInit) => {
          calls.push({ url, init });
          return okResponse({ documentId: 'd', driveId: 'b!drive', itemId: '01ITEM', alreadyAttached: false });
        },
      });

      const result = await client.attachDocumentFile('{ABCDEF00-0000-4000-8000-000000000166}', {
        id: '01ITEM',
        driveId: 'b!drive',
      });

      expect(calls).toHaveLength(1);
      expect(calls[0].url).toBe('https://api.example.com/api/v1/documents/abcdef00-0000-4000-8000-000000000166/file');
      expect(calls[0].init?.method).toBe('POST');
      expect(JSON.parse(String(calls[0].init?.body))).toEqual({ driveId: 'b!drive', itemId: '01ITEM' });
      expect(result.alreadyAttached).toBe(false);
    });

    it('refuses, without a request, when the upload response carried no drive id', async () => {
      const fetchSpy = jest.fn();
      const client = new SdapApiClient({ baseUrl: 'https://api.example.com', authenticatedFetch: fetchSpy });

      await expect(client.attachDocumentFile('doc', { id: '01ITEM' })).rejects.toThrow(/drive id/);
      expect(fetchSpy).not.toHaveBeenCalled();
    });

    it('surfaces the server refusal as an SdapHttpError carrying its status', async () => {
      const client = new SdapApiClient({
        baseUrl: 'https://api.example.com',
        authenticatedFetch: async () =>
          ({
            ok: false,
            status: 409,
            statusText: 'Conflict',
            json: async () => ({ detail: "The file is not stored where this document's files belong." }),
          }) as unknown as Response,
      });

      await expect(client.attachDocumentFile('doc', { id: '01ITEM', driveId: 'b!drive' })).rejects.toMatchObject({
        name: 'SdapHttpError',
        status: 409,
      });
    });
  });

  // field-mapping-server-write-path-r1 task 035 (FR-40): the server's index-file request has no index member; the index
  // follows the document's content type. The client never names one, even when a caller hands it a stale extra member.
  describe('indexFile', () => {
    it('posts a body with no searchIndexName key, even when the request object carries one', async () => {
      const bodies: Array<Record<string, unknown>> = [];
      const client = new SdapApiClient({
        baseUrl: 'https://api.example.com',
        authenticatedFetch: async (_url: string, init?: RequestInit) => {
          bodies.push(JSON.parse(String(init?.body)));
          return {
            ok: true,
            status: 200,
            json: async () => ({ success: true, chunksIndexed: 1 }),
          } as unknown as Response;
        },
      });

      const request = {
        driveId: 'b!drive',
        itemId: '01ITEM',
        fileName: 'a.docx',
        tenantId: 'tenant-1',
        searchIndexName: 'a-stale-index',
      } as Parameters<SdapApiClient['indexFile']>[0];
      const result = await client.indexFile(request);

      expect(result.success).toBe(true);
      expect(Object.keys(bodies[0])).not.toContain('searchIndexName');
      expect(JSON.stringify(bodies[0])).not.toContain('a-stale-index');
    });
  });
});
