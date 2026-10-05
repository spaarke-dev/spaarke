/**
 * CreateEventWizard — the document rows of an event's uploaded files bind the event through `sprk_RelatedEvent`
 * (unified-access-control-r2 round 34 item 6; task 147 r1c).
 *
 * `sprk_document` has NO `sprk_event` column: its event lookup is `sprk_relatedevent`, navigation property
 * `sprk_RelatedEvent` (live metadata; `Spaarke.Dataverse.DocumentLinkFields`). The wizard used to bind
 * `sprk_Event@odata.bind`, which failed every event-filed document save. Task 147 moved the document create onto the BFF
 * (`POST /api/v1/child-records/sprk_document`), whose payload mapper refuses an unknown navigation property — so the bind
 * key is what decides whether the files of a new event are filed at all.
 *
 * Driven through the REAL `EntityCreationService.createDocumentRecords` over the fake BFF (ADR-038: assert on what
 * reaches the server, no framework internals mocked).
 */

import { bffChildWriteFetch, childWriteCalls } from '../../../__mocks__/bffChildWriteFake';
import { EntityCreationService, type ISpeFileMetadata } from '../../../services/EntityCreationService';
import type { IWebApiWithCreate } from '../../../types/WebApiLike';
import { createEventDocumentRecords, EVENT_DOCUMENT_NAV_PROP } from '../CreateEventWizard';

const EVENT_ID = '0b5c1a2e-0000-4000-8000-000000000e01';
const file: ISpeFileMetadata = {
  id: 'item-1',
  name: 'brief.pdf',
  size: 10,
  webUrl: 'https://example/brief.pdf',
  driveId: 'b!drive',
};

function harness() {
  const created: Array<{ table: string; payload: Record<string, unknown> }> = [];
  const target = {
    createRecord: jest.fn(async (table: string, payload: Record<string, unknown>) => {
      created.push({ table, payload });
      return { id: 'doc-1' };
    }),
  };
  const fetchMock = bffChildWriteFetch(
    target,
    async () => ({ ok: true, status: 200, json: async () => ({}) }) as Response
  );
  const webApi = {
    createRecord: jest.fn(() => Promise.reject(new Error('Xrm.WebApi must not create the document'))),
    retrieveRecord: jest.fn(),
    retrieveMultipleRecords: jest.fn(),
  } as unknown as IWebApiWithCreate;
  const service = new EntityCreationService(webApi, fetchMock, 'https://bff.test');
  return { created, fetchMock, webApi, service };
}

describe('CreateEventWizard — event documents bind sprk_RelatedEvent (round 34 item 6)', () => {
  it('names the document lookup by its real navigation property, sprk_RelatedEvent', () => {
    expect(EVENT_DOCUMENT_NAV_PROP).toBe('sprk_RelatedEvent');
  });

  it('creates the document through the BFF bound to the event via sprk_RelatedEvent, never sprk_Event', async () => {
    const { created, fetchMock, webApi, service } = harness();

    const result = await createEventDocumentRecords(service, EVENT_ID, 'Hearing', [file]);

    expect(result.warnings).toEqual([]);
    expect(result.createdDocumentIds).toEqual(['doc-1']);
    expect(childWriteCalls(fetchMock)).toEqual([['POST', '/api/v1/child-records/sprk_document']]);
    expect(webApi.createRecord).not.toHaveBeenCalled();

    expect(created).toHaveLength(1);
    const { table, payload } = created[0];
    expect(table).toBe('sprk_document');
    expect(payload['sprk_RelatedEvent@odata.bind']).toBe(`/sprk_events(${EVENT_ID})`);
    expect(Object.keys(payload).filter(k => /^sprk_event@odata\.bind$/i.test(k))).toEqual([]);
  });
});
