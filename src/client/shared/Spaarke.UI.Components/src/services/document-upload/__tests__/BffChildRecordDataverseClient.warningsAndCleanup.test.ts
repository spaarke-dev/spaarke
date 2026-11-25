/**
 * withBffChildCreates — the server's create warnings are carried back, and a document whose file was refused is removed
 * through the BFF (field-mapping-server-write-path-r1 task 029 review, findings F1 and F2).
 *
 * F1: `createChildRecordViaBff` returned only the id, so a mapping warning (a source the caller cannot read, mapped values
 * stripped because the source is secure) was dropped. The decorator now returns them with the created record.
 *
 * F2: `DocumentRecordService.createAndAttach` removes the created row when the attach fails, but only when the client has a
 * `deleteRecord`; the decorator had none, so production left a fileless `sprk_document` filed under the parent. These tests
 * drive the REAL decorator (not a capturing client) through the REAL service.
 *
 * Classification (ADR-038 §7): MAINTAIN — the wire contract of the document create and its cleanup.
 */
import { withBffChildCreates } from '../BffChildRecordDataverseClient';
import { DocumentRecordService } from '../DocumentRecordService';
import type { NavMapClient } from '../NavMapClient';
import type {
  IDataverseClient,
  ILogger,
  SpeFileMetadata,
  ParentContext,
  EntityDocumentConfig,
  LookupNavigationResponse,
} from '../types';

type Call = { url: string; init: RequestInit };

const DOC_ID = 'aaaaaaaa-0000-0000-0000-000000000001';

/** A BFF stub: the create answers `createBody`; a DELETE answers `deleteStatus`. */
function makeFetch(createBody: Record<string, unknown> = { id: DOC_ID }, deleteStatus = 204) {
  const calls: Call[] = [];
  const fetchFn = jest.fn(async (url: string, init?: RequestInit) => {
    calls.push({ url, init: init ?? {} });
    if (init?.method === 'DELETE') {
      return {
        ok: deleteStatus >= 200 && deleteStatus < 300,
        status: deleteStatus,
        text: async () => '',
        json: async () => ({}),
      } as unknown as Response;
    }
    return {
      ok: true,
      status: 201,
      text: async () => JSON.stringify(createBody),
      json: async () => createBody,
    } as unknown as Response;
  });
  return { fetchFn, calls };
}

function makeInner(withDelete = true) {
  const deleted: Array<{ entity: string; id: string }> = [];
  const inner: IDataverseClient = {
    createRecord: async () => ({ id: 'inner-id' }),
    updateRecord: async () => undefined,
    ...(withDelete
      ? {
          deleteRecord: async (entity: string, id: string) => {
            deleted.push({ entity, id });
          },
        }
      : {}),
  };
  return { inner, deleted };
}

const PAYLOAD = { sprk_documentname: 'Contract.pdf' };
const MATTER = { entity: 'sprk_matter', id: '11111111-1111-1111-1111-111111111111' };

describe('withBffChildCreates — create warnings (F1)', () => {
  it("returns the server's warnings beside the id", async () => {
    const { fetchFn } = makeFetch({ id: DOC_ID, warnings: ['w1'] });
    const result = await withBffChildCreates(makeInner().inner, fetchFn, 'https://bff.example', MATTER).createRecord(
      'sprk_document',
      PAYLOAD
    );
    expect(result).toEqual({ id: DOC_ID, warnings: ['w1'] });
  });

  it('has no warnings property when the response has no warnings key', async () => {
    const { fetchFn } = makeFetch({ id: DOC_ID });
    const result = await withBffChildCreates(makeInner().inner, fetchFn, 'https://bff.example', MATTER).createRecord(
      'sprk_document',
      PAYLOAD
    );
    expect(result).toEqual({ id: DOC_ID });
    expect('warnings' in result).toBe(false);
  });

  it('has no warnings property when the warnings array is empty', async () => {
    const { fetchFn } = makeFetch({ id: DOC_ID, warnings: [] });
    const result = await withBffChildCreates(makeInner().inner, fetchFn, 'https://bff.example', MATTER).createRecord(
      'sprk_document',
      PAYLOAD
    );
    expect('warnings' in result).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// DocumentRecordService over the REAL decorator
// ---------------------------------------------------------------------------

const silentLogger: ILogger = {
  info: () => undefined,
  warn: () => undefined,
  error: () => undefined,
  debug: () => undefined,
};

const CONFIG: EntityDocumentConfig = {
  entityName: 'sprk_matter',
  lookupFieldName: 'sprk_matter',
  relationshipSchemaName: 'sprk_matter_document',
  navigationPropertyName: 'sprk_Matter',
  containerIdField: 'sprk_containerid',
  displayNameField: 'sprk_mattername',
  entitySetName: 'sprk_matters',
};

const navMap = {
  getLookupNavigation: async (childEntity: string, relationship: string): Promise<LookupNavigationResponse> => ({
    childEntity,
    relationship,
    logicalName: 'sprk_matter',
    schemaName: 'sprk_Matter',
    navigationPropertyName: 'sprk_Matter',
    targetEntity: 'sprk_matter',
    source: 'hardcoded',
  }),
} as unknown as NavMapClient;

const FILE: SpeFileMetadata = {
  id: 'graph-item-id-001',
  name: 'contract.pdf',
  size: 12345,
  driveId: 'drive-id-abc',
  createdDateTime: '2026-06-07T00:00:00Z',
  lastModifiedDateTime: '2026-06-07T00:00:00Z',
  isFolder: false,
  webUrl: 'https://example.sharepoint.com/Documents/contract.pdf',
};

const PARENT: ParentContext = {
  parentEntityName: 'sprk_matter',
  parentRecordId: MATTER.id,
  parentDisplayName: 'MAT-1',
};

function makeService(client: IDataverseClient, attachFile: (id: string, file: SpeFileMetadata) => Promise<unknown>) {
  return new DocumentRecordService({
    dataverseClient: client,
    navMapClient: navMap,
    getEntityConfig: () => CONFIG,
    attachFile,
    logger: silentLogger,
  });
}

describe('DocumentRecordService over withBffChildCreates — warnings (F1)', () => {
  it("copies the create's warnings into the per-file result", async () => {
    const { fetchFn } = makeFetch({ id: DOC_ID, warnings: ['w1', 'w2'] });
    const client = withBffChildCreates(makeInner().inner, fetchFn, 'https://bff.example', MATTER);
    const results = await makeService(client, async () => undefined).createDocuments([FILE], PARENT, {
      documentName: '',
    });
    expect(results[0].success).toBe(true);
    expect(results[0].warnings).toEqual(['w1', 'w2']);
  });

  it('has no warnings key on the result when the create had none', async () => {
    const { fetchFn } = makeFetch({ id: DOC_ID, warnings: [] });
    const client = withBffChildCreates(makeInner().inner, fetchFn, 'https://bff.example', MATTER);
    const results = await makeService(client, async () => undefined).createDocuments([FILE], PARENT, {
      documentName: '',
    });
    expect(results[0].success).toBe(true);
    expect('warnings' in results[0]).toBe(false);
  });

  it('has no warnings key on an unassociated upload with no warnings', async () => {
    const { fetchFn } = makeFetch({ id: DOC_ID });
    const client = withBffChildCreates(makeInner().inner, fetchFn, 'https://bff.example');
    const results = await makeService(client, async () => undefined).createDocuments(
      [FILE],
      { parentEntityName: '', parentRecordId: '', parentDisplayName: '' },
      { documentName: '' }
    );
    expect(results[0].success).toBe(true);
    expect('warnings' in results[0]).toBe(false);
  });
});

describe('withBffChildCreates — removing a document whose file was refused (F2)', () => {
  const deletes = (calls: Call[]) => calls.filter(c => c.init.method === 'DELETE');

  it('sends exactly one DELETE /api/v1/documents/{id} when the attach is refused, and reports the refusal', async () => {
    const { fetchFn, calls } = makeFetch();
    const { inner, deleted } = makeInner();
    const client = withBffChildCreates(inner, fetchFn, 'https://bff.example', MATTER);
    const svc = makeService(client, async () => {
      throw new Error('The file is not stored where this document’s files belong.');
    });

    const results = await svc.createDocuments([FILE], PARENT, { documentName: '' });

    expect(results[0].success).toBe(false);
    expect(results[0].error).toContain('not stored where');
    expect(deletes(calls)).toHaveLength(1);
    expect(deletes(calls)[0].url).toBe(`https://bff.example/api/v1/documents/${DOC_ID}`);
    // Not removed as the user through the inner client.
    expect(deleted).toEqual([]);
  });

  it('sends no DELETE when the attach succeeds', async () => {
    const { fetchFn, calls } = makeFetch();
    const client = withBffChildCreates(makeInner().inner, fetchFn, 'https://bff.example', MATTER);

    const results = await makeService(client, async () => undefined).createDocuments([FILE], PARENT, {
      documentName: '',
    });

    expect(results[0].success).toBe(true);
    expect(deletes(calls)).toEqual([]);
  });

  it('a non-2xx delete throws from deleteRecord (so the service logs it) and the attach refusal still reaches the caller', async () => {
    const { fetchFn, calls } = makeFetch({ id: DOC_ID }, 500);
    const warn = jest.fn();
    const client = withBffChildCreates(makeInner().inner, fetchFn, 'https://bff.example', MATTER);

    await expect(client.deleteRecord!('sprk_document', DOC_ID)).rejects.toThrow(/HTTP 500/);

    const svc = new DocumentRecordService({
      dataverseClient: client,
      navMapClient: navMap,
      getEntityConfig: () => CONFIG,
      attachFile: async () => {
        throw new Error('attach refused');
      },
      logger: { ...silentLogger, warn },
    });
    const results = await svc.createDocuments([FILE], PARENT, { documentName: '' });

    expect(results[0].error).toBe('attach refused');
    expect(warn).toHaveBeenCalledTimes(1);
    expect(deletes(calls)).toHaveLength(2);
  });

  it('delegates a table other than sprk_document to the inner client, sending no request', async () => {
    const { fetchFn, calls } = makeFetch();
    const { inner, deleted } = makeInner();
    const client = withBffChildCreates(inner, fetchFn, 'https://bff.example', MATTER);

    await client.deleteRecord!('sprk_unrelatedtable', 'row-1');

    expect(deleted).toEqual([{ entity: 'sprk_unrelatedtable', id: 'row-1' }]);
    expect(calls).toEqual([]);
  });

  it('does nothing for another table when the inner client cannot delete', async () => {
    const { fetchFn, calls } = makeFetch();
    const client = withBffChildCreates(makeInner(false).inner, fetchFn, 'https://bff.example', MATTER);

    await expect(client.deleteRecord!('sprk_unrelatedtable', 'row-1')).resolves.toBeUndefined();
    expect(calls).toEqual([]);
  });

  it('normalises the id in the route (braces stripped, lowercased) and tolerates a trailing slash on the base URL', async () => {
    const { fetchFn, calls } = makeFetch();
    const client = withBffChildCreates(makeInner().inner, fetchFn, 'https://bff.example/', MATTER);

    await client.deleteRecord!('sprk_document', '{AAAAAAAA-0000-0000-0000-000000000009}');

    expect(calls[0].url).toBe('https://bff.example/api/v1/documents/aaaaaaaa-0000-0000-0000-000000000009');
    expect(calls[0].init.method).toBe('DELETE');
  });
});
