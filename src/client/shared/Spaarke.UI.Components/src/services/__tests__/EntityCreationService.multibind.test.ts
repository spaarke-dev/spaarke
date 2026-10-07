/**
 * EntityCreationService.createDocumentRecords — multi-bind (additionalBinds) — Unit Tests
 *
 * Scope: visual-host-create-button-r1 task 013 (design.md §5.8 — File → document
 * dual-bind). `sprk_document` exposes separate typed lookups per parent type
 * (sprk_matter, sprk_project, sprk_invoice, sprk_event, …), so one document row
 * can natively bind to multiple parents. `createDocumentRecords` gains an
 * optional `options.additionalBinds` array; each entry emits an extra
 * `${navProp}@odata.bind` alongside the primary bind.
 *
 * Per ADR-038 (docs/adr/ADR-038-testing-strategy.md): no `Mock<HttpMessageHandler>`,
 * no DI-registration tests, no ctor-null-check tests — assert on the actual
 * payload shape produced by the service.
 *
 * Covered:
 *   - Omitting additionalBinds preserves the historical single-bind payload (no regression).
 *   - Providing additionalBinds emits both @odata.bind entries on the same create call.
 *   - GUID brace/case normalization applies to BOTH the primary bind and additionalBinds.
 *   - A duplicate nav-prop (colliding with the primary bind) is skipped + warned, not overwritten.
 *   - additionalBinds is applied identically across multiple uploaded files.
 */

// UAC-r2 task 147 r1: child creates go through the BFF; the fake answers its routes through the mock data service.
import { bffChildWriteFetch } from '../../__mocks__/bffChildWriteFake';
import { EntityCreationService, type ISpeFileMetadata, type AuthenticatedFetchFn } from '../EntityCreationService';
import type { IWebApiWithCreate } from '../../types/WebApiLike';

// ---------------------------------------------------------------------------
// Test helpers
// ---------------------------------------------------------------------------

/** Minimal WebApi stub — only createRecord is exercised by createDocumentRecords. */
function makeWebApi(createdIds: string[] = ['doc-guid-1']): IWebApiWithCreate {
  let call = 0;
  return {
    retrieveRecord: jest.fn(),
    retrieveMultipleRecords: jest.fn(),
    createRecord: jest.fn().mockImplementation(async () => {
      const id = createdIds[call] ?? `doc-guid-${call + 1}`;
      call++;
      return { id };
    }),
  } as unknown as IWebApiWithCreate;
}

/** authenticatedFetch stub for the non-fatal post-create "trigger analysis" call. */
const makeAuthFetch = (): AuthenticatedFetchFn =>
  jest.fn().mockResolvedValue({ ok: true, status: 200, json: async () => ({}) } as Response);

function lastCreatePayload(webApi: IWebApiWithCreate, callIndex = 0): Record<string, unknown> {
  const mock = webApi.createRecord as jest.Mock;
  return mock.mock.calls[callIndex][1];
}

const file1: ISpeFileMetadata = { id: 'item-1', name: 'file1.pdf', size: 100, webUrl: 'https://example/file1.pdf', driveId: 'b!drive-1' };
const file2: ISpeFileMetadata = { id: 'item-2', name: 'file2.pdf', size: 200, webUrl: 'https://example/file2.pdf', driveId: 'b!drive-1' };

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('EntityCreationService.createDocumentRecords — additionalBinds', () => {
  it('omitting additionalBinds preserves the single-bind payload shape (no regression)', async () => {
    const webApi = makeWebApi();
    const service = new EntityCreationService(
      webApi,
      bffChildWriteFetch(webApi, makeAuthFetch()),
      'https://bff.example'
    );

    const result = await service.createDocumentRecords('sprk_matters', 'matter-guid-1', 'sprk_Matter', [file1], {
      containerId: 'container-1',
    });

    expect(webApi.createRecord).toHaveBeenCalledTimes(1);
    const payload = lastCreatePayload(webApi);
    expect(payload['sprk_Matter@odata.bind']).toBe('/sprk_matters(matter-guid-1)');

    const bindKeys = Object.keys(payload).filter(k => k.endsWith('@odata.bind'));
    expect(bindKeys).toEqual(['sprk_Matter@odata.bind']);
    expect(result.linkedCount).toBe(1);
    expect(result.warnings).toEqual([]);
  });

  it('emits both @odata.bind entries on one create call when additionalBinds is provided', async () => {
    const webApi = makeWebApi();
    const service = new EntityCreationService(
      webApi,
      bffChildWriteFetch(webApi, makeAuthFetch()),
      'https://bff.example'
    );

    await service.createDocumentRecords('sprk_events', 'event-guid-1', 'sprk_RelatedEvent', [file1], {
      containerId: 'container-1',
      additionalBinds: [{ entitySet: 'sprk_matters', id: 'matter-guid-1', navProp: 'sprk_Matter' }],
    });

    expect(webApi.createRecord).toHaveBeenCalledTimes(1);
    const payload = lastCreatePayload(webApi);
    expect(payload['sprk_RelatedEvent@odata.bind']).toBe('/sprk_events(event-guid-1)');
    expect(payload['sprk_Matter@odata.bind']).toBe('/sprk_matters(matter-guid-1)');

    const bindKeys = Object.keys(payload).filter(k => k.endsWith('@odata.bind'));
    expect(bindKeys.sort()).toEqual(['sprk_Matter@odata.bind', 'sprk_RelatedEvent@odata.bind']);
  });

  it('supports more than one additional bind on the same document', async () => {
    const webApi = makeWebApi();
    const service = new EntityCreationService(
      webApi,
      bffChildWriteFetch(webApi, makeAuthFetch()),
      'https://bff.example'
    );

    await service.createDocumentRecords('sprk_events', 'event-guid-1', 'sprk_RelatedEvent', [file1], {
      additionalBinds: [
        { entitySet: 'sprk_matters', id: 'matter-guid-1', navProp: 'sprk_Matter' },
        { entitySet: 'sprk_projects', id: 'project-guid-1', navProp: 'sprk_Project' },
      ],
    });

    const payload = lastCreatePayload(webApi);
    expect(payload['sprk_RelatedEvent@odata.bind']).toBe('/sprk_events(event-guid-1)');
    expect(payload['sprk_Matter@odata.bind']).toBe('/sprk_matters(matter-guid-1)');
    expect(payload['sprk_Project@odata.bind']).toBe('/sprk_projects(project-guid-1)');
  });

  it('normalizes brace-wrapped + mixed-case GUIDs on both the primary bind and additionalBinds', async () => {
    const webApi = makeWebApi();
    const service = new EntityCreationService(
      webApi,
      bffChildWriteFetch(webApi, makeAuthFetch()),
      'https://bff.example'
    );

    await service.createDocumentRecords('sprk_events', '{EVENT-GUID-1}', 'sprk_RelatedEvent', [file1], {
      additionalBinds: [{ entitySet: 'sprk_matters', id: '{Matter-Guid-1}', navProp: 'sprk_Matter' }],
    });

    const payload = lastCreatePayload(webApi);
    expect(payload['sprk_RelatedEvent@odata.bind']).toBe('/sprk_events(event-guid-1)');
    expect(payload['sprk_Matter@odata.bind']).toBe('/sprk_matters(matter-guid-1)');
  });

  it('skips a duplicate additionalBinds nav-prop and warns instead of overwriting the primary bind', async () => {
    const webApi = makeWebApi();
    const service = new EntityCreationService(
      webApi,
      bffChildWriteFetch(webApi, makeAuthFetch()),
      'https://bff.example'
    );

    const result = await service.createDocumentRecords('sprk_events', 'event-guid-1', 'sprk_RelatedEvent', [file1], {
      // Deliberately collides with the primary bind's nav-prop.
      additionalBinds: [{ entitySet: 'sprk_matters', id: 'matter-guid-1', navProp: 'sprk_RelatedEvent' }],
    });

    const payload = lastCreatePayload(webApi);
    // Primary bind wins — not overwritten by the colliding additional bind.
    expect(payload['sprk_RelatedEvent@odata.bind']).toBe('/sprk_events(event-guid-1)');
    const bindKeys = Object.keys(payload).filter(k => k.endsWith('@odata.bind'));
    expect(bindKeys).toEqual(['sprk_RelatedEvent@odata.bind']);

    expect(result.warnings.some(w => w.includes('duplicate additionalBinds nav-prop'))).toBe(true);
    // Duplicate-bind warning is non-fatal — the document record still gets created and linked.
    expect(result.linkedCount).toBe(1);
  });

  it('skips a duplicate nav-prop across two additionalBinds entries (second is dropped)', async () => {
    const webApi = makeWebApi();
    const service = new EntityCreationService(
      webApi,
      bffChildWriteFetch(webApi, makeAuthFetch()),
      'https://bff.example'
    );

    const result = await service.createDocumentRecords('sprk_events', 'event-guid-1', 'sprk_RelatedEvent', [file1], {
      additionalBinds: [
        { entitySet: 'sprk_matters', id: 'matter-guid-1', navProp: 'sprk_Matter' },
        { entitySet: 'sprk_matters', id: 'matter-guid-2', navProp: 'sprk_Matter' }, // duplicate nav-prop
      ],
    });

    const payload = lastCreatePayload(webApi);
    // First entry wins.
    expect(payload['sprk_Matter@odata.bind']).toBe('/sprk_matters(matter-guid-1)');
    expect(result.warnings.some(w => w.includes('duplicate additionalBinds nav-prop'))).toBe(true);
  });

  it('applies additionalBinds identically across multiple uploaded files', async () => {
    const webApi = makeWebApi(['doc-guid-1', 'doc-guid-2']);
    const service = new EntityCreationService(
      webApi,
      bffChildWriteFetch(webApi, makeAuthFetch()),
      'https://bff.example'
    );

    const result = await service.createDocumentRecords(
      'sprk_events',
      'event-guid-1',
      'sprk_RelatedEvent',
      [file1, file2],
      {
        additionalBinds: [{ entitySet: 'sprk_matters', id: 'matter-guid-1', navProp: 'sprk_Matter' }],
      }
    );

    expect(webApi.createRecord).toHaveBeenCalledTimes(2);
    for (let i = 0; i < 2; i++) {
      const payload = lastCreatePayload(webApi, i);
      expect(payload['sprk_RelatedEvent@odata.bind']).toBe('/sprk_events(event-guid-1)');
      expect(payload['sprk_Matter@odata.bind']).toBe('/sprk_matters(matter-guid-1)');
    }
    expect(result.linkedCount).toBe(2);
    expect(result.createdDocumentIds).toEqual(['doc-guid-1', 'doc-guid-2']);
  });

  // unified-access-control-r2 task 166 f1 (owner round 21 item 1 (i)): the client never writes the SPE pointer — the
  // BFF attaches the uploaded file after verifying it.
  it('never writes the SPE pointer, and asks the BFF to attach each uploaded file to its new row', async () => {
    const webApi = makeWebApi(['doc-guid-1']);
    const authFetch = makeAuthFetch();
    // Batch-4 integration: the row is created through the BFF (task 147), then its file attached (task 166).
    const service = new EntityCreationService(webApi, bffChildWriteFetch(webApi, authFetch), 'https://bff.example');

    const result = await service.createDocumentRecords('sprk_matters', 'matter-guid-1', 'sprk_Matter', [file1]);

    const payload = lastCreatePayload(webApi);
    for (const column of ['sprk_graphdriveid', 'sprk_graphitemid', 'sprk_hasfile', 'sprk_filepath']) {
      expect(column in payload).toBe(false);
    }
    const attach = (authFetch as jest.Mock).mock.calls.find(([url]) => String(url).endsWith('/api/v1/documents/doc-guid-1/file'));
    expect(attach).toBeDefined();
    expect(attach![1].method).toBe('POST');
    expect(JSON.parse(attach![1].body)).toEqual({ driveId: 'b!drive-1', itemId: 'item-1' });
    expect(result.createdDocumentIds).toEqual(['doc-guid-1']);
  });

  it('removes a row whose file the BFF refuses to attach, reports it, and never profiles it', async () => {
    const webApi = makeWebApi(['doc-guid-1']);
    const deleteRecord = jest.fn().mockResolvedValue(undefined);
    (webApi as unknown as { deleteRecord: jest.Mock }).deleteRecord = deleteRecord;
    const authFetch = jest.fn().mockImplementation(async (url: string) =>
      String(url).endsWith('/file')
        ? ({
            ok: false,
            status: 409,
            statusText: 'Conflict',
            json: async () => ({ detail: 'The file is not stored where this document belongs.' }),
          } as unknown as Response)
        : ({ ok: true, status: 200, json: async () => ({}) } as Response)
    ) as unknown as AuthenticatedFetchFn;
    // Batch-4 integration: the row is created through the BFF (task 147), then its file attached (task 166).
    const service = new EntityCreationService(webApi, bffChildWriteFetch(webApi, authFetch), 'https://bff.example');

    const result = await service.createDocumentRecords('sprk_matters', 'matter-guid-1', 'sprk_Matter', [file1]);

    expect(deleteRecord).toHaveBeenCalledWith('sprk_document', 'doc-guid-1');
    expect(result.linkedCount).toBe(0);
    expect(result.createdDocumentIds).toEqual([]);
    expect(result.warnings.some(w => w.includes('file1.pdf'))).toBe(true);
    const profiled = (authFetch as unknown as jest.Mock).mock.calls.some(([url]) => String(url).includes('/analyze'));
    expect(profiled).toBe(false);
  });

  it('an empty additionalBinds array behaves identically to omitting the option', async () => {
    const webApi = makeWebApi();
    const service = new EntityCreationService(
      webApi,
      bffChildWriteFetch(webApi, makeAuthFetch()),
      'https://bff.example'
    );

    await service.createDocumentRecords('sprk_matters', 'matter-guid-1', 'sprk_Matter', [file1], {
      additionalBinds: [],
    });

    const payload = lastCreatePayload(webApi);
    const bindKeys = Object.keys(payload).filter(k => k.endsWith('@odata.bind'));
    expect(bindKeys).toEqual(['sprk_Matter@odata.bind']);
  });
});
