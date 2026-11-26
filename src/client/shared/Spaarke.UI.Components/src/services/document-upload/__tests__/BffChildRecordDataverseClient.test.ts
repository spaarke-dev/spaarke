/**
 * withBffChildCreates — the document create goes through the BFF child-record route, naming the filing parent as the
 * mapping source in the QUERY (field-mapping-server-write-path-r1 task 029, spec FR-15, FR-16).
 *
 * The route infers a mapping source only for a matter or project create (`InferMappingSourceTables`), so a `sprk_document`
 * create that names none maps nothing. The upload pipeline names the parent the documents are filed under; the body stays
 * the caller's own payload.
 *
 * Classification (ADR-038 §7): MAINTAIN — the wire contract of the document create.
 */
import { withBffChildCreates } from '../BffChildRecordDataverseClient';
import type { IDataverseClient } from '../types';

type Call = { url: string; init: RequestInit };

function makeFetch() {
  const calls: Call[] = [];
  const fetchFn = jest.fn(async (url: string, init?: RequestInit) => {
    calls.push({ url, init: init ?? {} });
    const body = { id: '{AAAAAAAA-0000-0000-0000-000000000001}' };
    return {
      ok: true,
      status: 201,
      text: async () => JSON.stringify(body),
      json: async () => body,
    } as unknown as Response;
  });
  return { fetchFn, calls };
}

function makeInner() {
  const created: Array<{ entity: string; data: Record<string, unknown> }> = [];
  const inner: IDataverseClient = {
    createRecord: async (entity, data) => {
      created.push({ entity, data });
      return { id: 'inner-id' };
    },
    updateRecord: async () => undefined,
  };
  return { inner, created };
}

const PAYLOAD = {
  sprk_documentname: 'Contract.pdf',
  'sprk_Matter@odata.bind': '/sprk_matters(11111111-1111-1111-1111-111111111111)',
};

describe('withBffChildCreates', () => {
  it('creates a sprk_document through POST /api/v1/child-records/sprk_document with the payload unchanged', async () => {
    const { fetchFn, calls } = makeFetch();
    const { inner, created } = makeInner();

    const result = await withBffChildCreates(inner, fetchFn, 'https://bff.example').createRecord(
      'sprk_document',
      PAYLOAD
    );

    expect(result).toEqual({ id: 'aaaaaaaa-0000-0000-0000-000000000001' });
    expect(calls).toHaveLength(1);
    expect(calls[0].url).toBe('https://bff.example/api/v1/child-records/sprk_document');
    expect(calls[0].init.method).toBe('POST');
    expect(JSON.parse(String(calls[0].init.body))).toEqual(PAYLOAD);
    // Not created as the user.
    expect(created).toEqual([]);
  });

  it('names the filing parent as the mapping source in the query, and adds nothing to the body', async () => {
    const { fetchFn, calls } = makeFetch();
    const { inner } = makeInner();

    await withBffChildCreates(inner, fetchFn, 'https://bff.example', {
      entity: 'SPRK_Matter',
      id: '{11111111-1111-1111-1111-111111111111}',
    }).createRecord('sprk_document', PAYLOAD);

    expect(calls[0].url).toBe(
      'https://bff.example/api/v1/child-records/sprk_document' +
        '?mappingSourceEntity=sprk_matter&mappingSourceId=11111111-1111-1111-1111-111111111111'
    );
    expect(JSON.parse(String(calls[0].init.body))).toEqual(PAYLOAD);
  });

  it('sends no query string for an unfiled upload (no mapping source)', async () => {
    const { fetchFn, calls } = makeFetch();
    const { inner } = makeInner();

    await withBffChildCreates(inner, fetchFn, 'https://bff.example', undefined).createRecord('sprk_document', {
      sprk_documentname: 'Contract.pdf',
    });

    expect(calls[0].url).toBe('https://bff.example/api/v1/child-records/sprk_document');
  });

  it('passes a table the BFF route does not create straight through to the inner client', async () => {
    const { fetchFn, calls } = makeFetch();
    const { inner, created } = makeInner();

    const result = await withBffChildCreates(inner, fetchFn, 'https://bff.example', {
      entity: 'sprk_matter',
      id: '11111111-1111-1111-1111-111111111111',
    }).createRecord('sprk_unrelatedtable', { a: 1 });

    expect(result).toEqual({ id: 'inner-id' });
    expect(created).toEqual([{ entity: 'sprk_unrelatedtable', data: { a: 1 } }]);
    expect(calls).toEqual([]);
  });
});
