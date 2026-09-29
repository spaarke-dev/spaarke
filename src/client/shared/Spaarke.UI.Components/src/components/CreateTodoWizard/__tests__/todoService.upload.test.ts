/**
 * todoService.upload.test.ts
 *
 * Unit tests for `TodoService.createTodo`'s file-upload → link sequence
 * (ISS-027 / GitHub #1007 / task 119).
 *
 * Prior to this task, the Create To Do wizard showed a file-upload step,
 * captioned it as associating the files with the to do, and then silently
 * discarded `context.uploadedFiles` — the user saw "To Do created!" and the
 * attached files were simply gone. These tests pin the fix:
 *
 *   - Files are uploaded to SPE via `EntityCreationService.uploadFilesToSpe`
 *     ONLY after the sprk_todo record exists (create-first — task 076's
 *     record-keyed upload contract requires the id).
 *   - Each uploaded file becomes one `sprk_document` row bound to the to do
 *     through the discovered `sprk_relatedtodo` nav-prop (`sprk_RelatedToDo`),
 *     against the `sprk_todos` entity set.
 *   - Zero files is a byte-for-byte no-op (no upload call, no document create,
 *     no warnings) — the pre-existing behaviour is preserved exactly.
 *   - A failed upload, a failed document-record create, or an unresolvable
 *     storage container (the live-reachable 409 path) all degrade to entries
 *     in `result.warnings` — never a thrown error, never a discarded to do.
 *
 * Mirrors the reference sequence in
 * `../../CreateMatterWizard/matterService.ts:349-418`.
 *
 * @see ../todoService.ts
 */

import { TodoService, _resetTodoServiceNavPropCacheForTests } from '../todoService';
import { EMPTY_TODO_FORM, type ICreateTodoFormState } from '../formTypes';
import type { IUploadedFile } from '../../FileUpload/fileUploadTypes';
import { createMockDataService } from '../../../__mocks__/mockDataService';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const BFF_BASE_URL = 'https://bff.example.com';

const FORM_VALUES: ICreateTodoFormState = {
  ...EMPTY_TODO_FORM,
  title: 'Review Discovery Motion',
};

function makeUploadedFile(name: string): IUploadedFile {
  return {
    id: `local-${name}`,
    name,
    sizeBytes: 12,
    fileType: 'pdf',
    file: new File(['contents'], name, { type: 'application/pdf' }),
  };
}

/**
 * No sprk_document-specific entries — `discoverNavProps('sprk_document')` returns `[]`, so
 * `todoService.ts` must fall back to the literal `sprk_RelatedToDo` nav-prop name.
 */
function installGlobalFetchMockWithoutDocumentNavProps(): jest.Mock {
  const mockFetch = jest.fn().mockResolvedValue({
    ok: true,
    status: 200,
    json: jest.fn().mockResolvedValue({ value: [] }),
  });
  (globalThis as unknown as { fetch: typeof fetch }).fetch = mockFetch as unknown as typeof fetch;
  return mockFetch;
}

/** Includes the real sprk_document -> sprk_todo relationship, for the discovery-path test. */
function installGlobalFetchMockWithDocumentNavProps(): jest.Mock {
  const mockFetch = jest.fn().mockResolvedValue({
    ok: true,
    status: 200,
    json: jest.fn().mockResolvedValue({
      value: [
        {
          ReferencingAttribute: 'sprk_relatedtodo',
          ReferencingEntityNavigationPropertyName: 'sprk_RelatedToDo',
          ReferencedEntity: 'sprk_todo',
        },
      ],
    }),
  });
  (globalThis as unknown as { fetch: typeof fetch }).fetch = mockFetch as unknown as typeof fetch;
  return mockFetch;
}

/**
 * Fake `authenticatedFetch` (injected into `TodoService` -> `EntityCreationService` ->
 * `SdapApiClient`) — completely separate from `globalThis.fetch`, which only backs
 * `discoverNavProps`. Handles two routes:
 *   - `PUT .../files/<name>`             -> SPE upload (per-call outcome from `uploadOutcomes`)
 *   - `POST .../api/documents/<id>/analyze` -> Document Profile queue (always succeeds; the
 *     result is ignored by `EntityCreationService._triggerDocumentAnalysis`, which never throws)
 */
function makeAuthenticatedFetch(uploadOutcomes: Array<{ status: number; body: Record<string, unknown> }>): jest.Mock {
  // Plain Response-like objects, not `new Response(...)` — this package's jsdom test
  // environment does not provide a global `Response` (the same reason `discoverNavProps`
  // tests patch `globalThis.fetch` with a plain-object mock rather than real Response
  // instances; see EntityCreationService.multibind.test.ts's `makeAuthFetch`).
  let uploadCallIndex = 0;
  return jest.fn(async (url: string) => {
    if (url.includes('/analyze')) {
      return { ok: true, status: 200, statusText: 'OK', json: async () => ({}) };
    }
    const outcome = uploadOutcomes[uploadCallIndex] ?? uploadOutcomes[uploadOutcomes.length - 1];
    uploadCallIndex++;
    return {
      ok: outcome.status >= 200 && outcome.status < 300,
      status: outcome.status,
      statusText: String(outcome.status),
      json: async () => outcome.body,
    };
  });
}

const UPLOAD_OK = (name: string, driveItemId: string) => ({
  status: 200,
  body: { id: driveItemId, name, size: 12, driveId: 'drive-1', webUrl: `https://spe.example.com/${name}` },
});

const UPLOAD_FAILED = (status: number, detail: string) => ({
  status,
  body: { detail },
});

// ---------------------------------------------------------------------------
// Suite
// ---------------------------------------------------------------------------

describe('TodoService.createTodo -- file upload + document link (ISS-027 / task 119)', () => {
  beforeEach(() => {
    _resetTodoServiceNavPropCacheForTests();
    jest.clearAllMocks();
  });

  // -----------------------------------------------------------------
  // Zero-files path (NEGATIVE): unchanged behaviour
  // -----------------------------------------------------------------

  it('zeroFiles_callsNeitherUploadNorDocumentCreate_andEmitsNoWarnings', async () => {
    installGlobalFetchMockWithoutDocumentNavProps();
    const dataService = createMockDataService();
    const authenticatedFetch = makeAuthenticatedFetch([]);

    const service = new TodoService(dataService, authenticatedFetch, BFF_BASE_URL);
    const result = await service.createTodo(FORM_VALUES, undefined, []);

    expect(result.success).toBe(true);
    expect(result.warnings ?? []).toEqual([]);
    // Only the sprk_todo create -- no sprk_document rows.
    expect(dataService.createRecord).toHaveBeenCalledTimes(1);
    expect(dataService.createRecord).toHaveBeenCalledWith('sprk_todo', expect.any(Object));
    expect(authenticatedFetch).not.toHaveBeenCalled();
  });

  it('zeroFiles_isIdenticalWhenUploadedFilesArgumentIsOmittedEntirely', async () => {
    // Existing call sites (createTodoRegardingChild) call createTodo(formValues, regarding)
    // with no third argument at all -- confirms the default parameter preserves that contract.
    installGlobalFetchMockWithoutDocumentNavProps();
    const dataService = createMockDataService();

    const service = new TodoService(dataService); // no authenticatedFetch / bffBaseUrl either
    const result = await service.createTodo(FORM_VALUES);

    expect(result.success).toBe(true);
    expect(result.warnings ?? []).toEqual([]);
    expect(dataService.createRecord).toHaveBeenCalledTimes(1);
  });

  // -----------------------------------------------------------------
  // Happy path: create -> upload -> link
  // -----------------------------------------------------------------

  it('withFiles_uploadsAfterCreate_withTheRealTodoId_neverBeforeOrEmpty', async () => {
    installGlobalFetchMockWithoutDocumentNavProps();
    const dataService = createMockDataService();
    dataService.createRecord.mockResolvedValueOnce('todo-guid-123').mockResolvedValue('doc-guid-x');
    const authenticatedFetch = makeAuthenticatedFetch([UPLOAD_OK('brief.pdf', 'drive-item-1')]);

    const service = new TodoService(dataService, authenticatedFetch, BFF_BASE_URL);
    const result = await service.createTodo(FORM_VALUES, undefined, [makeUploadedFile('brief.pdf')]);

    expect(result.success).toBe(true);
    expect(result.todoId).toBe('todo-guid-123');

    // The upload PUT must target the real todo id, not an empty string.
    const uploadCall = authenticatedFetch.mock.calls.find(([url]: [string]) => !url.includes('/analyze'));
    expect(uploadCall).toBeDefined();
    const [uploadUrl] = uploadCall as [string];
    expect(uploadUrl).toContain('/api/obo/records/sprk_todo/todo-guid-123/files/');

    // Ordering: sprk_todo create call happens before the upload fetch call.
    const todoCreateOrder = dataService.createRecord.mock.invocationCallOrder[0];
    const uploadOrder = authenticatedFetch.mock.invocationCallOrder[0];
    expect(todoCreateOrder).toBeLessThan(uploadOrder);
  });

  it('withFiles_createsOneDocumentPerUploadedFile_boundThroughDiscoveredNavProp_onSprkTodosSet', async () => {
    installGlobalFetchMockWithDocumentNavProps();
    const dataService = createMockDataService();
    dataService.createRecord.mockResolvedValueOnce('todo-guid-123').mockResolvedValue('doc-guid-x');
    const authenticatedFetch = makeAuthenticatedFetch([UPLOAD_OK('brief.pdf', 'drive-item-1')]);

    const service = new TodoService(dataService, authenticatedFetch, BFF_BASE_URL);
    const result = await service.createTodo(FORM_VALUES, undefined, [makeUploadedFile('brief.pdf')]);

    expect(result.success).toBe(true);
    expect(result.warnings ?? []).toEqual([]);

    // 2 createRecord calls total: 1 sprk_todo + 1 sprk_document.
    expect(dataService.createRecord).toHaveBeenCalledTimes(2);
    const [docEntityName, docPayload] = dataService.createRecord.mock.calls[1] as [string, Record<string, unknown>];
    expect(docEntityName).toBe('sprk_document');

    // Bound through the DISCOVERED nav-prop (sprk_RelatedToDo, per the mocked metadata),
    // targeting the sprk_todos entity set with the real todo id.
    expect(docPayload['sprk_RelatedToDo@odata.bind']).toBe('/sprk_todos(todo-guid-123)');

    // Carries the upload result's identity fields.
    expect(docPayload['sprk_graphitemid']).toBe('drive-item-1');
    expect(docPayload['sprk_graphdriveid']).toBe('drive-1');
    expect(docPayload['sprk_hasfile']).toBe(true);
    expect(docPayload['sprk_documentname']).toBe('brief.pdf');
  });

  it('withFiles_fallsBackToLiteralNavProp_whenDiscoveryReturnsNoEntries', async () => {
    // No sprk_document nav-prop entries in the mocked metadata -- must fall back to the
    // literal 'sprk_RelatedToDo' (Models.cs DocumentLinkFields.All), not fail or use the
    // bare (nonexistent) column name.
    installGlobalFetchMockWithoutDocumentNavProps();
    const dataService = createMockDataService();
    dataService.createRecord.mockResolvedValueOnce('todo-guid-123').mockResolvedValue('doc-guid-x');
    const authenticatedFetch = makeAuthenticatedFetch([UPLOAD_OK('brief.pdf', 'drive-item-1')]);

    const service = new TodoService(dataService, authenticatedFetch, BFF_BASE_URL);
    await service.createTodo(FORM_VALUES, undefined, [makeUploadedFile('brief.pdf')]);

    const [, docPayload] = dataService.createRecord.mock.calls[1] as [string, Record<string, unknown>];
    expect(docPayload['sprk_RelatedToDo@odata.bind']).toBe('/sprk_todos(todo-guid-123)');
  });

  // -----------------------------------------------------------------
  // NEGATIVE: upload fails entirely -- warning, not a throw; to do survives
  // -----------------------------------------------------------------

  it('uploadFailsEntirely_warnsInsteadOfThrowing_todoStillCreated_noDocumentRecordAttempted', async () => {
    installGlobalFetchMockWithoutDocumentNavProps();
    const dataService = createMockDataService();
    dataService.createRecord.mockResolvedValueOnce('todo-guid-123');
    const authenticatedFetch = makeAuthenticatedFetch([UPLOAD_FAILED(500, 'Server error')]);

    const service = new TodoService(dataService, authenticatedFetch, BFF_BASE_URL);
    const result = await service.createTodo(FORM_VALUES, undefined, [makeUploadedFile('brief.pdf')]);

    expect(result.success).toBe(true); // the to do itself was created
    expect(result.todoId).toBe('todo-guid-123');
    expect(result.warnings).toEqual(expect.arrayContaining([expect.stringContaining('File upload failed')]));
    // No document record attempted -- nothing was uploaded to link.
    expect(dataService.createRecord).toHaveBeenCalledTimes(1);
  });

  // -----------------------------------------------------------------
  // NEGATIVE: no container could be resolved (live-reachable 409 path)
  // -----------------------------------------------------------------

  it('noContainerResolvable_surfacesAsWarning_notAnUnhandledRejection_todoSurvives', async () => {
    installGlobalFetchMockWithoutDocumentNavProps();
    const dataService = createMockDataService();
    dataService.createRecord.mockResolvedValueOnce('todo-guid-123');
    // OBOEndpoints.cs:121-136 -- 409 "No storage container is configured" is a live, reachable
    // outcome for a to-do whose owning business unit has no sprk_containerid set.
    const authenticatedFetch = makeAuthenticatedFetch([UPLOAD_FAILED(409, 'No storage container is configured')]);

    const service = new TodoService(dataService, authenticatedFetch, BFF_BASE_URL);

    await expect(service.createTodo(FORM_VALUES, undefined, [makeUploadedFile('brief.pdf')])).resolves.toEqual(
      expect.objectContaining({
        success: true,
        todoId: 'todo-guid-123',
        warnings: expect.arrayContaining([expect.stringContaining('File upload failed')]),
      })
    );
  });

  // -----------------------------------------------------------------
  // NEGATIVE: document-record create fails after a successful upload
  // -----------------------------------------------------------------

  it('documentRecordCreateFails_afterSuccessfulUpload_warningReachesResult_todoStillCreated', async () => {
    installGlobalFetchMockWithoutDocumentNavProps();
    const dataService = createMockDataService();
    dataService.createRecord
      .mockResolvedValueOnce('todo-guid-123') // sprk_todo create
      .mockRejectedValueOnce(new Error('Duplicate Record')); // sprk_document create fails
    const authenticatedFetch = makeAuthenticatedFetch([UPLOAD_OK('brief.pdf', 'drive-item-1')]);

    const service = new TodoService(dataService, authenticatedFetch, BFF_BASE_URL);
    const result = await service.createTodo(FORM_VALUES, undefined, [makeUploadedFile('brief.pdf')]);

    expect(result.success).toBe(true);
    expect(result.todoId).toBe('todo-guid-123');
    expect(result.warnings).toEqual(
      expect.arrayContaining([expect.stringContaining('Failed to create document record for "brief.pdf"')])
    );
  });

  // -----------------------------------------------------------------
  // Partial failure: 2 files, one succeeds, one fails
  // -----------------------------------------------------------------

  it('partialUploadFailure_linksTheSuccessAndWarnsAboutTheFailure', async () => {
    installGlobalFetchMockWithoutDocumentNavProps();
    const dataService = createMockDataService();
    dataService.createRecord.mockResolvedValueOnce('todo-guid-123').mockResolvedValue('doc-guid-x');
    const authenticatedFetch = makeAuthenticatedFetch([
      UPLOAD_OK('good.pdf', 'drive-item-1'),
      UPLOAD_FAILED(403, 'forbidden'),
    ]);

    const service = new TodoService(dataService, authenticatedFetch, BFF_BASE_URL);
    const result = await service.createTodo(FORM_VALUES, undefined, [
      makeUploadedFile('good.pdf'),
      makeUploadedFile('bad.pdf'),
    ]);

    expect(result.success).toBe(true);
    // 1 sprk_todo + 1 sprk_document (only the successful upload is linked)
    expect(dataService.createRecord).toHaveBeenCalledTimes(2);
    const [, docPayload] = dataService.createRecord.mock.calls[1] as [string, Record<string, unknown>];
    expect(docPayload['sprk_documentname']).toBe('good.pdf');

    expect(result.warnings).toEqual(
      expect.arrayContaining([expect.stringContaining('1 file(s) failed to upload: bad.pdf')])
    );
  });

  // -----------------------------------------------------------------
  // Graceful no-op: uploadedFiles supplied but this TodoService instance
  // was never given authenticatedFetch/bffBaseUrl (e.g. the
  // createTodoRegardingChild follow-on path)
  // -----------------------------------------------------------------

  it('uploadNotConfigured_warnsAndSkips_ratherThanThrowing_whenFilesSuppliedWithoutDeps', async () => {
    installGlobalFetchMockWithoutDocumentNavProps();
    const dataService = createMockDataService();
    dataService.createRecord.mockResolvedValueOnce('todo-guid-123');

    const service = new TodoService(dataService); // no authenticatedFetch / bffBaseUrl
    const result = await service.createTodo(FORM_VALUES, undefined, [makeUploadedFile('brief.pdf')]);

    expect(result.success).toBe(true);
    expect(result.todoId).toBe('todo-guid-123');
    expect(result.warnings).toEqual(expect.arrayContaining([expect.stringContaining('upload is not configured')]));
    // Only the sprk_todo create -- no attempt to create a document record.
    expect(dataService.createRecord).toHaveBeenCalledTimes(1);
  });
});
