/**
 * DocumentRecordService create payload — user-entered values plus the filing parent, nothing the server derives
 * (field-mapping-server-write-path-r1 task 029, spec FR-16, FR-40).
 *
 * The payload is closed: the document name, the file's own name and size, the description and the parent bind. It carries
 * no search index (`sprk_searchindexname`, the `sprk_AI_Search_Index@odata.bind` lookup): the index follows the content
 * type and the server owns it, and the mapped fields, the owner and the I-1 stamp are applied by the child-records route.
 * A caller written before task 029 that still passes an index name and id as extra arguments has them ignored.
 *
 * **Critical INV check** (design.md): The canonical Document container field is
 * `sprk_graphdriveid`. `sprk_containerid` MUST stay NULL on `sprk_document` —
 * Phase F backfill audit depends on this. Tests guard against accidental
 * regression by asserting `sprk_containerid` is NOT in the payload under any
 * code path (associated, unassociated).
 *
 * **Pointer guard (unified-access-control-r2 task 166 f1, owner round 21 item 1)**: the create
 * payload carries NO SPE pointer (`sprk_graphdriveid` / `sprk_graphitemid`), and no `sprk_hasfile`
 * / `sprk_filepath` either. Those columns are field-secured and writable by the BFF identity only:
 * the service hands the SERVER's upload answer (task 076 — `SpeFileMetadata.driveId` + `id`) to the
 * BFF attach (`attachFile`), which verifies it and stamps the pointer. A row whose file the BFF
 * refuses is removed and reported.
 */

import { DocumentRecordService } from '../DocumentRecordService';
import { NavMapClient } from '../NavMapClient';
import type {
  IDataverseClient,
  DataverseRecordRef,
  SpeFileMetadata,
  ParentContext,
  DocumentFormData,
  EntityDocumentConfig,
  LookupNavigationResponse,
  ILogger,
} from '../types';

// ---------------------------------------------------------------------------
// Test doubles
// ---------------------------------------------------------------------------

const silentLogger: ILogger = {
  info: () => undefined,
  warn: () => undefined,
  error: () => undefined,
  debug: () => undefined,
};

/** Captures every create/update/delete call so tests can assert on the exact payload. */
interface CapturingDataverseClient extends IDataverseClient {
  createCalls: Array<{ entityLogicalName: string; data: Record<string, unknown> }>;
  updateCalls: Array<{ entityLogicalName: string; id: string; data: Record<string, unknown> }>;
  deleteCalls: Array<{ entityLogicalName: string; id: string }>;
}

function makeCapturingClient(): CapturingDataverseClient {
  const createCalls: CapturingDataverseClient['createCalls'] = [];
  const updateCalls: CapturingDataverseClient['updateCalls'] = [];
  const deleteCalls: CapturingDataverseClient['deleteCalls'] = [];
  return {
    createCalls,
    updateCalls,
    deleteCalls,
    createRecord: async (entityLogicalName, data): Promise<DataverseRecordRef> => {
      createCalls.push({ entityLogicalName, data });
      return { id: 'created-doc-id-' + createCalls.length };
    },
    updateRecord: async (entityLogicalName, id, data): Promise<void> => {
      updateCalls.push({ entityLogicalName, id, data });
    },
    deleteRecord: async (entityLogicalName, id): Promise<void> => {
      deleteCalls.push({ entityLogicalName, id });
    },
  };
}

/** The pointer columns only the BFF writes (task 166 f1) — never in a client create payload. */
const BFF_ONLY_COLUMNS = ['sprk_graphdriveid', 'sprk_graphitemid', 'sprk_hasfile', 'sprk_filepath'];

/** Records every attach the service asks the BFF for; `refuse` makes the BFF refuse. */
function makeAttacher(refuse?: Error) {
  const calls: Array<{ documentId: string; file: SpeFileMetadata }> = [];
  const attach = async (documentId: string, file: SpeFileMetadata): Promise<void> => {
    calls.push({ documentId, file });
    if (refuse) throw refuse;
  };
  return { calls, attach };
}

/** NavMapClient stub — returns a deterministic navigation property + entity set name. */
function makeNavMapStub(navProp = 'sprk_Matter', targetEntity = 'sprk_matter'): NavMapClient {
  return {
    getLookupNavigation: async (_childEntity: string, _relationship: string): Promise<LookupNavigationResponse> => ({
      childEntity: _childEntity,
      relationship: _relationship,
      logicalName: navProp.toLowerCase(),
      schemaName: navProp,
      navigationPropertyName: navProp,
      targetEntity,
      source: 'hardcoded',
    }),
    // Other NavMapClient methods are unused by createDocuments; satisfy the type via cast.
  } as unknown as NavMapClient;
}

function makeMatterConfig(): EntityDocumentConfig {
  return {
    entityName: 'sprk_matter',
    lookupFieldName: 'sprk_matter',
    relationshipSchemaName: 'sprk_matter_document',
    navigationPropertyName: 'sprk_Matter',
    containerIdField: 'sprk_containerid',
    displayNameField: 'sprk_mattername',
    entitySetName: 'sprk_matters',
  };
}

function makeFile(name = 'contract.pdf', size = 12345, driveId = 'drive-id-abc'): SpeFileMetadata {
  return {
    id: 'graph-item-id-001',
    name,
    size,
    // The SERVER's answer for where these bytes went (task 076). This, not the parent context,
    // is what `sprk_graphdriveid` must be built from.
    driveId,
    createdDateTime: '2026-06-07T00:00:00Z',
    lastModifiedDateTime: '2026-06-07T00:00:00Z',
    isFolder: false,
    webUrl: 'https://example.sharepoint.com/Documents/' + name,
  };
}

function makeParentContext(overrides: Partial<ParentContext> = {}): ParentContext {
  return {
    parentEntityName: 'sprk_matter',
    parentRecordId: '11111111-1111-1111-1111-111111111111',
    parentDisplayName: 'MAT-2026-001',
    ...overrides,
  };
}

function makeFormData(): DocumentFormData {
  return {
    documentName: 'Contract.pdf',
    description: 'Initial draft',
  };
}

function makeService(
  client: IDataverseClient,
  config: EntityDocumentConfig | null = makeMatterConfig(),
  attacher = makeAttacher()
): DocumentRecordService {
  return new DocumentRecordService({
    dataverseClient: client,
    navMapClient: makeNavMapStub(config?.navigationPropertyName, 'sprk_matter'),
    getEntityConfig: _name => config,
    attachFile: attacher.attach,
    logger: silentLogger,
  });
}

// ---------------------------------------------------------------------------
// The payload is user-entered values + the filing parent (FR-16, FR-40) — associated mode
// ---------------------------------------------------------------------------

/**
 * Calls `createDocuments` the way a caller written before task 029 did: with a search index name and id as the 4th and 5th
 * arguments. The signature no longer takes them, so the call goes through an untyped view of the method; the service
 * must ignore what it was never meant to receive.
 */
function createWithRemovedIndexArgs(
  svc: DocumentRecordService,
  parent: ParentContext,
  indexName: string,
  indexId: string,
  file: SpeFileMetadata = makeFile()
): Promise<unknown> {
  const legacy = svc.createDocuments as unknown as (...args: unknown[]) => Promise<unknown>;
  return legacy.call(svc, [file], parent, makeFormData(), indexName, indexId);
}

/** Every payload key carrying a search index (the column or the lookup bind), whatever its casing. */
function indexKeysOf(payload: Record<string, unknown>): string[] {
  return Object.keys(payload).filter(k => /searchindex|search_index/i.test(k));
}

describe('DocumentRecordService payload — associated mode', () => {
  it('sends exactly the user-entered values, the file metadata and the parent bind (no index, no mapped value)', async () => {
    const client = makeCapturingClient();
    const svc = makeService(client);

    const results = await svc.createDocuments([makeFile('report.docx', 9876)], makeParentContext(), {
      documentName: 'Q1 Report',
      description: 'Final',
    });

    expect(results).toHaveLength(1);
    expect(results[0].success).toBe(true);
    expect(client.createCalls).toHaveLength(1);
    expect(client.createCalls[0].entityLogicalName).toBe('sprk_document');
    // Closed set: a key the user did not enter, and the wizard did not take from the file or the parent, fails this.
    expect(client.createCalls[0].data).toEqual({
      sprk_documentname: 'Q1 Report',
      sprk_filename: 'report.docx',
      sprk_filesize: 9876,
      sprk_documentdescription: 'Final',
      'sprk_Matter@odata.bind': '/sprk_matters(11111111-1111-1111-1111-111111111111)',
    });
  });

  it('sends no search index even when a caller still passes one (sprk_searchindexname, sprk_AI_Search_Index bind)', async () => {
    const client = makeCapturingClient();
    const svc = makeService(client);

    await createWithRemovedIndexArgs(
      svc,
      makeParentContext(),
      'spaarke-knowledge-index-v2',
      '33333333-3333-3333-3333-333333333333'
    );

    expect(client.createCalls).toHaveLength(1);
    const payload = client.createCalls[0].data;
    expect(indexKeysOf(payload)).toEqual([]);
    expect('sprk_searchindexname' in payload).toBe(false);
    expect('sprk_AI_Search_Index@odata.bind' in payload).toBe(false);
    expect(JSON.stringify(payload)).not.toContain('spaarke-knowledge-index-v2');
    expect(JSON.stringify(payload)).not.toContain('sprk_aisearchindexes');
  });

  it('sends no owner, team, mapped-field or business-unit key (the server decides them, FR-02, FR-09)', async () => {
    const client = makeCapturingClient();
    const svc = makeService(client);

    await svc.createDocuments([makeFile()], makeParentContext(), makeFormData());

    const keys = Object.keys(client.createCalls[0].data);
    expect(
      keys.filter(k => /^(ownerid|owningteam|owninguser|owningbusinessunit|sprk_createdbyperson)/i.test(k))
    ).toEqual([]);
    // Only the five keys of the closed set above (name, file name, size, description, parent bind).
    expect(keys.sort()).toEqual(
      [
        'sprk_Matter@odata.bind',
        'sprk_documentdescription',
        'sprk_documentname',
        'sprk_filename',
        'sprk_filesize',
      ].sort()
    );
  });

  it('DOES NOT add sprk_containerid to the Document payload (design.md INV — canonical container field is sprk_graphdriveid)', async () => {
    const client = makeCapturingClient();
    const svc = makeService(client);

    await svc.createDocuments([makeFile()], makeParentContext(), makeFormData());

    expect(client.createCalls).toHaveLength(1);
    const payload = client.createCalls[0].data;
    // The binding INV: sprk_document MUST NOT carry sprk_containerid. Phase F backfill audit
    // depends on this universally being NULL on sprk_document.
    expect('sprk_containerid' in payload).toBe(false);
    expect(payload.sprk_containerid).toBeUndefined();
  });

  it('hands the SERVER-reported file to the BFF attach, and never writes the pointer itself (task 166 f1)', async () => {
    const client = makeCapturingClient();
    const attacher = makeAttacher();
    const svc = makeService(client, makeMatterConfig(), attacher);

    const results = await svc.createDocuments(
      // A secure record's own container — the value only the server knows (task 076).
      [makeFile('contract.pdf', 12345, 'b!secure-record-own-container')],
      makeParentContext(),
      makeFormData()
    );

    expect(results[0].success).toBe(true);
    const payload = client.createCalls[0].data;
    for (const column of BFF_ONLY_COLUMNS) {
      expect(column in payload).toBe(false);
    }
    expect(attacher.calls).toHaveLength(1);
    expect(attacher.calls[0].documentId).toBe('created-doc-id-1');
    expect(attacher.calls[0].file.driveId).toBe('b!secure-record-own-container');
    expect(attacher.calls[0].file.id).toBe('graph-item-id-001');
  });

  it('removes the row and reports the file when the BFF refuses to attach it', async () => {
    const client = makeCapturingClient();
    const attacher = makeAttacher(new Error("The file is not stored where this document's files belong."));
    const svc = makeService(client, makeMatterConfig(), attacher);

    const results = await svc.createDocuments([makeFile()], makeParentContext(), makeFormData());

    expect(results[0].success).toBe(false);
    expect(results[0].error).toContain('not stored where');
    expect(client.deleteCalls).toEqual([{ entityLogicalName: 'sprk_document', id: 'created-doc-id-1' }]);
  });

  it('normalises the parent id in the bind (braces stripped, lowercased) and keeps every user value', async () => {
    const client = makeCapturingClient();
    const svc = makeService(client);

    await svc.createDocuments(
      [makeFile('report.docx', 9876)],
      makeParentContext({ parentRecordId: '{22222222-2222-2222-2222-222222222222}' }),
      { documentName: 'Q1 Report', description: 'Final' }
    );

    const payload = client.createCalls[0].data;
    expect(payload.sprk_documentname).toBe('Q1 Report');
    expect(payload.sprk_filename).toBe('report.docx');
    expect(payload.sprk_filesize).toBe(9876);
    expect(payload.sprk_documentdescription).toBe('Final');
    // The pointer, the file flag and the path are the BFF's to write (task 166 f1).
    for (const column of BFF_ONLY_COLUMNS) {
      expect(column in payload).toBe(false);
    }
    expect(payload['sprk_Matter@odata.bind']).toBe('/sprk_matters(22222222-2222-2222-2222-222222222222)');
    expect('sprk_containerid' in payload).toBe(false);
  });

  it('sends the same closed set for every Document in a multi-file batch', async () => {
    const client = makeCapturingClient();
    const svc = makeService(client);

    await svc.createDocuments(
      [makeFile('a.pdf'), makeFile('b.pdf'), makeFile('c.pdf')],
      makeParentContext(),
      makeFormData()
    );

    expect(client.createCalls).toHaveLength(3);
    for (const call of client.createCalls) {
      expect(indexKeysOf(call.data)).toEqual([]);
      expect('sprk_containerid' in call.data).toBe(false);
      expect('sprk_graphdriveid' in call.data).toBe(false);
      expect(Object.keys(call.data).sort()).toEqual(
        [
          'sprk_Matter@odata.bind',
          'sprk_documentdescription',
          'sprk_documentname',
          'sprk_filename',
          'sprk_filesize',
        ].sort()
      );
    }
  });
});

// ---------------------------------------------------------------------------
// The same closed set — unassociated mode (no parent record bound)
// ---------------------------------------------------------------------------

describe('DocumentRecordService payload — unassociated mode', () => {
  function unassociatedParent(): ParentContext {
    return {
      parentEntityName: '',
      parentRecordId: '',
      parentDisplayName: '',
    };
  }

  it('sends only the user-entered values and the file metadata (no parent bind, no index)', async () => {
    const client = makeCapturingClient();
    const svc = makeService(client);

    await svc.createDocuments([makeFile('contract.pdf', 12345)], unassociatedParent(), makeFormData());

    expect(client.createCalls).toHaveLength(1);
    expect(client.createCalls[0].data).toEqual({
      sprk_documentname: 'Contract.pdf',
      sprk_filename: 'contract.pdf',
      sprk_filesize: 12345,
      sprk_documentdescription: 'Initial draft',
    });
  });

  it('sends no search index even when a caller still passes one', async () => {
    const client = makeCapturingClient();
    const svc = makeService(client);

    await createWithRemovedIndexArgs(
      svc,
      unassociatedParent(),
      'spaarke-knowledge-index-v2',
      '33333333-3333-3333-3333-333333333333'
    );

    expect(client.createCalls).toHaveLength(1);
    expect(indexKeysOf(client.createCalls[0].data)).toEqual([]);
    expect(JSON.stringify(client.createCalls[0].data)).not.toContain('spaarke-knowledge-index-v2');
  });

  it('NEVER adds sprk_containerid in unassociated payload (INV — same as associated mode)', async () => {
    const client = makeCapturingClient();
    const svc = makeService(client);

    await svc.createDocuments([makeFile()], unassociatedParent(), makeFormData());

    expect('sprk_containerid' in client.createCalls[0].data).toBe(false);
  });

  it("attaches the unassociated document's file through the BFF too, from file.driveId", async () => {
    const client = makeCapturingClient();
    const attacher = makeAttacher();
    const svc = makeService(client, makeMatterConfig(), attacher);

    await svc.createDocuments(
      [makeFile('contract.pdf', 12345, 'drive-id-unassoc')],
      unassociatedParent(),
      makeFormData()
    );

    // Both code paths take the SAME route. They diverged historically, and a divergence here is how
    // "unassociated documents carry a client-written pointer" would come back.
    for (const column of BFF_ONLY_COLUMNS) {
      expect(column in client.createCalls[0].data).toBe(false);
    }
    expect(attacher.calls[0].file.driveId).toBe('drive-id-unassoc');
  });
});
