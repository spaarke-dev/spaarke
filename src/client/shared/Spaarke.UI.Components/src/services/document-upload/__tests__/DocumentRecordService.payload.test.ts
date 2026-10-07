/**
 * DocumentRecordService.buildRecordPayload — Unit tests for FR-WIZ-07.
 *
 * Scope: verifies that the `sprk_searchindexname` field flows through the Document
 * create payload when the caller resolves a non-empty value via the FR-WIZ-06
 * 3-step chain (parent record → parent's BU → empty), and is OMITTED when the
 * caller passes empty / undefined so the BFF tenant-default chain (FR-BFF-04)
 * takes over server-side.
 *
 * **Critical INV check** (design.md): The canonical Document container field is
 * `sprk_graphdriveid`. `sprk_containerid` MUST stay NULL on `sprk_document` —
 * Phase F backfill audit depends on this. Tests guard against accidental
 * regression by asserting `sprk_containerid` is NOT in the payload under any
 * code path (associated, unassociated, with index, without index).
 *
 * **Pointer guard (unified-access-control-r2 task 166 f1, owner round 21 item 1)**: the create
 * payload carries NO SPE pointer (`sprk_graphdriveid` / `sprk_graphitemid`), and no `sprk_hasfile`
 * / `sprk_filepath` either. Those columns are field-secured and writable by the BFF identity only:
 * the service hands the SERVER's upload answer (task 076 — `SpeFileMetadata.driveId` + `id`) to the
 * BFF attach (`attachFile`), which verifies it and stamps the pointer. A row whose file the BFF
 * refuses is removed and reported.
 *
 * @see spec.md FR-WIZ-07
 * @see design.md INV (Document container field)
 * @see projects/spaarke-multi-container-multi-index-r1/notes/handoffs/026-docupload-resolver.md
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
// FR-WIZ-07 — associated mode (parent context present)
// ---------------------------------------------------------------------------

describe('DocumentRecordService.buildRecordPayload — FR-WIZ-07 (associated mode)', () => {
  it('includes sprk_searchindexname in payload when caller passes a non-empty value', async () => {
    const client = makeCapturingClient();
    const svc = makeService(client);

    const results = await svc.createDocuments(
      [makeFile()],
      makeParentContext(),
      makeFormData(),
      'spaarke-knowledge-index-v2'
    );

    expect(results).toHaveLength(1);
    expect(results[0].success).toBe(true);
    expect(client.createCalls).toHaveLength(1);
    const payload = client.createCalls[0].data;
    expect(payload.sprk_searchindexname).toBe('spaarke-knowledge-index-v2');
  });

  it('OMITS sprk_searchindexname when caller passes undefined (BFF tenant default applies server-side)', async () => {
    const client = makeCapturingClient();
    const svc = makeService(client);

    await svc.createDocuments([makeFile()], makeParentContext(), makeFormData(), undefined);

    expect(client.createCalls).toHaveLength(1);
    expect('sprk_searchindexname' in client.createCalls[0].data).toBe(false);
  });

  it('OMITS sprk_searchindexname when caller passes empty string', async () => {
    const client = makeCapturingClient();
    const svc = makeService(client);

    await svc.createDocuments([makeFile()], makeParentContext(), makeFormData(), '');

    expect(client.createCalls).toHaveLength(1);
    expect('sprk_searchindexname' in client.createCalls[0].data).toBe(false);
  });

  it('OMITS sprk_searchindexname when caller passes whitespace-only string (FR-WIZ-06 empty semantics)', async () => {
    const client = makeCapturingClient();
    const svc = makeService(client);

    await svc.createDocuments([makeFile()], makeParentContext(), makeFormData(), '   ');

    expect(client.createCalls).toHaveLength(1);
    expect('sprk_searchindexname' in client.createCalls[0].data).toBe(false);
  });

  it('OMITS sprk_searchindexname when called without the searchIndexName argument at all (backward compat)', async () => {
    const client = makeCapturingClient();
    const svc = makeService(client);

    // Call the 3-arg overload — proves existing callers that have not migrated yet still work.
    await svc.createDocuments([makeFile()], makeParentContext(), makeFormData());

    expect(client.createCalls).toHaveLength(1);
    expect('sprk_searchindexname' in client.createCalls[0].data).toBe(false);
  });

  it('DOES NOT add sprk_containerid to the Document payload (design.md INV — canonical container field is sprk_graphdriveid)', async () => {
    const client = makeCapturingClient();
    const svc = makeService(client);

    await svc.createDocuments([makeFile()], makeParentContext(), makeFormData(), 'spaarke-knowledge-index-v2');

    expect(client.createCalls).toHaveLength(1);
    const payload = client.createCalls[0].data;
    // The binding INV: sprk_document MUST NOT carry sprk_containerid. Phase F backfill audit
    // depends on this universally being NULL on sprk_document.
    expect('sprk_containerid' in payload).toBe(false);
    expect(payload.sprk_containerid).toBeUndefined();
  });

  it('DOES NOT add sprk_containerid even when searchIndexName is omitted (INV check on every code path)', async () => {
    const client = makeCapturingClient();
    const svc = makeService(client);

    await svc.createDocuments([makeFile()], makeParentContext(), makeFormData());

    expect(client.createCalls).toHaveLength(1);
    expect('sprk_containerid' in client.createCalls[0].data).toBe(false);
  });

  it('hands the SERVER-reported file to the BFF attach, and never writes the pointer itself (task 166 f1)', async () => {
    const client = makeCapturingClient();
    const attacher = makeAttacher();
    const svc = makeService(client, makeMatterConfig(), attacher);

    const results = await svc.createDocuments(
      // A secure record's own container — the value only the server knows (task 076).
      [makeFile('contract.pdf', 12345, 'b!secure-record-own-container')],
      makeParentContext(),
      makeFormData(),
      'spaarke-knowledge-index-v2'
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

  it('preserves all pre-existing Document fields alongside the new sprk_searchindexname', async () => {
    const client = makeCapturingClient();
    const svc = makeService(client);

    await svc.createDocuments(
      [makeFile('report.docx', 9876)],
      makeParentContext({ parentRecordId: '{22222222-2222-2222-2222-222222222222}' }),
      { documentName: 'Q1 Report', description: 'Final' },
      'spaarke-file-index'
    );

    const payload = client.createCalls[0].data;
    expect(payload.sprk_documentname).toBe('Q1 Report');
    expect(payload.sprk_filename).toBe('report.docx');
    expect(payload.sprk_filesize).toBe(9876);
    expect(payload.sprk_documentdescription).toBe('Final');
    expect(payload.sprk_searchindexname).toBe('spaarke-file-index');
    // The pointer, the file flag and the path are the BFF's to write (task 166 f1).
    for (const column of BFF_ONLY_COLUMNS) {
      expect(column in payload).toBe(false);
    }
    // Parent @odata.bind — sanitized GUID (braces stripped, lowercased)
    expect(payload['sprk_Matter@odata.bind']).toBe('/sprk_matters(22222222-2222-2222-2222-222222222222)');
    // INV
    expect('sprk_containerid' in payload).toBe(false);
  });

  it('applies the same searchIndexName to every Document in a multi-file batch', async () => {
    const client = makeCapturingClient();
    const svc = makeService(client);

    await svc.createDocuments(
      [makeFile('a.pdf'), makeFile('b.pdf'), makeFile('c.pdf')],
      makeParentContext(),
      makeFormData(),
      'spaarke-knowledge-index-v2'
    );

    expect(client.createCalls).toHaveLength(3);
    for (const call of client.createCalls) {
      expect(call.data.sprk_searchindexname).toBe('spaarke-knowledge-index-v2');
      expect('sprk_containerid' in call.data).toBe(false);
      expect('sprk_graphdriveid' in call.data).toBe(false);
    }
  });
});

// ---------------------------------------------------------------------------
// FR-WIZ-07 — unassociated mode (no parent record bound)
// ---------------------------------------------------------------------------

describe('DocumentRecordService — FR-WIZ-07 (unassociated mode)', () => {
  function unassociatedParent(): ParentContext {
    return {
      parentEntityName: '',
      parentRecordId: '',
      parentDisplayName: '',
    };
  }

  it('includes sprk_searchindexname in unassociated payload when value provided', async () => {
    const client = makeCapturingClient();
    const svc = makeService(client);

    await svc.createDocuments([makeFile()], unassociatedParent(), makeFormData(), 'spaarke-knowledge-index-v2');

    expect(client.createCalls).toHaveLength(1);
    expect(client.createCalls[0].data.sprk_searchindexname).toBe('spaarke-knowledge-index-v2');
  });

  it('OMITS sprk_searchindexname in unassociated payload when value empty / undefined', async () => {
    const client = makeCapturingClient();
    const svc = makeService(client);

    await svc.createDocuments([makeFile()], unassociatedParent(), makeFormData());

    expect(client.createCalls).toHaveLength(1);
    expect('sprk_searchindexname' in client.createCalls[0].data).toBe(false);
  });

  it('NEVER adds sprk_containerid in unassociated payload (INV — same as associated mode)', async () => {
    const client = makeCapturingClient();
    const svc = makeService(client);

    await svc.createDocuments([makeFile()], unassociatedParent(), makeFormData(), 'spaarke-knowledge-index-v2');

    expect('sprk_containerid' in client.createCalls[0].data).toBe(false);
  });

  it("attaches the unassociated document's file through the BFF too, from file.driveId", async () => {
    const client = makeCapturingClient();
    const attacher = makeAttacher();
    const svc = makeService(client, makeMatterConfig(), attacher);

    await svc.createDocuments(
      [makeFile('contract.pdf', 12345, 'drive-id-unassoc')],
      unassociatedParent(),
      makeFormData(),
      'spaarke-knowledge-index-v2'
    );

    // Both code paths take the SAME route. They diverged historically, and a divergence here is how
    // "unassociated documents carry a client-written pointer" would come back.
    for (const column of BFF_ONLY_COLUMNS) {
      expect(column in client.createCalls[0].data).toBe(false);
    }
    expect(attacher.calls[0].file.driveId).toBe('drive-id-unassoc');
  });
});
