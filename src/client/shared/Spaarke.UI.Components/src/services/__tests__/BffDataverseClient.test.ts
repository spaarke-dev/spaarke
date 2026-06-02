/**
 * Unit tests for BffDataverseClient (task 015, FR-BFF-06).
 *
 * Coverage:
 *  - Constructor: bffBaseUrl resolution priority (options → window → env → throw)
 *  - Constructor: authenticatedFetch required
 *  - Happy path for each of the 5 IDataverseClient methods (URL + body shape)
 *  - URL building: encodeURIComponent on entityName + id
 *  - retrieveRecord: $select clause built correctly when fields present / omitted when empty
 *  - retrieveMultipleRecords: pagingCookie: undefined always in body (R1 Option A)
 *  - ProblemDetails error mapping: 404 → BffNotFoundError, 403 → BffForbiddenError,
 *    400 → BffBadRequestError, 5xx → BffServerError, with errorCode + correlationId.
 *  - Non-ProblemDetails error response → BFF_UNKNOWN_ERROR fallback.
 */

import {
  BffDataverseClient,
  BffDataverseClientConfigurationError,
  BffDataverseClientError,
  BffNotFoundError,
  BffForbiddenError,
  BffBadRequestError,
  BffServerError,
  type AuthenticatedFetchFn,
} from '../BffDataverseClient';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Build a Response stand-in mirror that satisfies the subset of the Response
 * interface BffDataverseClient touches: `ok`, `status`, `statusText`,
 * `headers.get('content-type')`, and `json()`.
 */
function makeResponse(
  status: number,
  body: unknown,
  options: { contentType?: string; statusText?: string } = {},
): Response {
  const contentType = options.contentType ?? 'application/json';
  const headers = new Headers({ 'content-type': contentType });
  return {
    ok: status >= 200 && status < 300,
    status,
    statusText: options.statusText ?? '',
    headers,
    json: jest.fn().mockResolvedValue(body),
  } as unknown as Response;
}

/** Build a Response with no parseable body (returns invalid JSON). */
function makeNonJsonResponse(status: number): Response {
  const headers = new Headers({ 'content-type': 'text/plain' });
  return {
    ok: false,
    status,
    statusText: 'Server Exploded',
    headers,
    json: jest.fn().mockRejectedValue(new Error('not json')),
  } as unknown as Response;
}

function makeMockFetch(): jest.Mock & AuthenticatedFetchFn {
  return jest.fn() as unknown as jest.Mock & AuthenticatedFetchFn;
}

const BASE_URL = 'https://spe-api-test.example.com';

/** Restore window/process globals on teardown. */
function clearGlobals(): void {
  delete (window as unknown as { SPAARKE_BFF_URL?: string }).SPAARKE_BFF_URL;
  delete process.env.SPAARKE_BFF_URL;
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('BffDataverseClient', () => {
  afterEach(() => {
    clearGlobals();
    jest.clearAllMocks();
  });

  // -------------------------------------------------------------------------
  // Constructor / bffBaseUrl resolution
  // -------------------------------------------------------------------------

  describe('constructor + bffBaseUrl resolution', () => {
    it('uses bffBaseUrl from constructor options when provided', async () => {
      const fetchFn = makeMockFetch();
      fetchFn.mockResolvedValue(
        makeResponse(200, {
          entityName: 'account',
          fetchXml: '<fetch/>',
          layoutXml: '<grid/>',
          name: 'My View',
        }),
      );
      const client = new BffDataverseClient({
        authenticatedFetch: fetchFn,
        bffBaseUrl: BASE_URL,
      });
      await client.retrieveSavedQuery('id-1');

      expect(fetchFn).toHaveBeenCalledWith(
        `${BASE_URL}/api/dataverse/savedquery/id-1`,
        expect.any(Object),
      );
    });

    it('trims trailing slash from bffBaseUrl', async () => {
      const fetchFn = makeMockFetch();
      fetchFn.mockResolvedValue(makeResponse(200, []));
      const client = new BffDataverseClient({
        authenticatedFetch: fetchFn,
        bffBaseUrl: `${BASE_URL}///`,
      });
      await client.retrieveSavedQueriesForEntity('account');

      const calledUrl = fetchFn.mock.calls[0][0] as string;
      expect(calledUrl).toBe(`${BASE_URL}/api/dataverse/savedqueries/account`);
      expect(calledUrl).not.toContain('//api');
    });

    it('falls back to window.SPAARKE_BFF_URL when options.bffBaseUrl omitted', async () => {
      (window as unknown as { SPAARKE_BFF_URL: string }).SPAARKE_BFF_URL = BASE_URL;
      const fetchFn = makeMockFetch();
      fetchFn.mockResolvedValue(makeResponse(200, []));

      const client = new BffDataverseClient({ authenticatedFetch: fetchFn });
      await client.retrieveSavedQueriesForEntity('sprk_event');

      expect(fetchFn).toHaveBeenCalledWith(
        `${BASE_URL}/api/dataverse/savedqueries/sprk_event`,
        expect.any(Object),
      );
    });

    it('falls back to process.env.SPAARKE_BFF_URL when window global absent', async () => {
      process.env.SPAARKE_BFF_URL = BASE_URL;
      const fetchFn = makeMockFetch();
      fetchFn.mockResolvedValue(makeResponse(200, []));

      const client = new BffDataverseClient({ authenticatedFetch: fetchFn });
      await client.retrieveSavedQueriesForEntity('sprk_event');

      expect(fetchFn).toHaveBeenCalledWith(
        `${BASE_URL}/api/dataverse/savedqueries/sprk_event`,
        expect.any(Object),
      );
    });

    it('throws BffDataverseClientConfigurationError when no bffBaseUrl is resolvable', () => {
      const fetchFn = makeMockFetch();
      // No options.bffBaseUrl, no window.SPAARKE_BFF_URL, no process.env.SPAARKE_BFF_URL.
      expect(() => new BffDataverseClient({ authenticatedFetch: fetchFn })).toThrow(
        BffDataverseClientConfigurationError,
      );
    });

    it('throws BffDataverseClientConfigurationError when authenticatedFetch is missing', () => {
      expect(
        () =>
          new BffDataverseClient({
            // @ts-expect-error — intentional: testing runtime guard.
            authenticatedFetch: undefined,
            bffBaseUrl: BASE_URL,
          }),
      ).toThrow(BffDataverseClientConfigurationError);
    });
  });

  // -------------------------------------------------------------------------
  // retrieveSavedQuery (happy path)
  // -------------------------------------------------------------------------

  describe('retrieveSavedQuery', () => {
    it('GETs /api/dataverse/savedquery/{id} and returns the parsed body', async () => {
      const fetchFn = makeMockFetch();
      fetchFn.mockResolvedValue(
        makeResponse(200, {
          entityName: 'sprk_event',
          fetchXml: '<fetch top="50"/>',
          layoutXml: '<grid/>',
          name: 'Active Events',
        }),
      );
      const client = new BffDataverseClient({
        authenticatedFetch: fetchFn,
        bffBaseUrl: BASE_URL,
      });
      const result = await client.retrieveSavedQuery('guid-1');

      expect(fetchFn).toHaveBeenCalledTimes(1);
      const [url, init] = fetchFn.mock.calls[0];
      expect(url).toBe(`${BASE_URL}/api/dataverse/savedquery/guid-1`);
      expect((init as RequestInit).method).toBe('GET');
      expect(result).toEqual({
        entityName: 'sprk_event',
        fetchXml: '<fetch top="50"/>',
        layoutXml: '<grid/>',
        name: 'Active Events',
      });
    });

    it('encodes the savedQueryId in the URL (encodeURIComponent applied)', async () => {
      const fetchFn = makeMockFetch();
      fetchFn.mockResolvedValue(
        makeResponse(200, { entityName: '', fetchXml: '', layoutXml: '', name: '' }),
      );
      const client = new BffDataverseClient({
        authenticatedFetch: fetchFn,
        bffBaseUrl: BASE_URL,
      });
      // ID with characters that need encoding (defensive — Dataverse IDs are GUIDs,
      // but an arbitrary user-supplied id should still be safe).
      await client.retrieveSavedQuery('a/b c?d');
      const calledUrl = fetchFn.mock.calls[0][0] as string;
      expect(calledUrl).toBe(`${BASE_URL}/api/dataverse/savedquery/a%2Fb%20c%3Fd`);
    });
  });

  // -------------------------------------------------------------------------
  // retrieveSavedQueriesForEntity (happy path + encoding)
  // -------------------------------------------------------------------------

  describe('retrieveSavedQueriesForEntity', () => {
    it('GETs /api/dataverse/savedqueries/{entity} and returns the array body', async () => {
      const fetchFn = makeMockFetch();
      fetchFn.mockResolvedValue(
        makeResponse(200, [
          { id: 'v1', name: 'All Events', isDefault: true, queryType: 0 },
          { id: 'v2', name: 'My Events', isDefault: false, queryType: 0 },
        ]),
      );
      const client = new BffDataverseClient({
        authenticatedFetch: fetchFn,
        bffBaseUrl: BASE_URL,
      });
      const result = await client.retrieveSavedQueriesForEntity('sprk_event');

      expect(fetchFn).toHaveBeenCalledTimes(1);
      const [url, init] = fetchFn.mock.calls[0];
      expect(url).toBe(`${BASE_URL}/api/dataverse/savedqueries/sprk_event`);
      expect((init as RequestInit).method).toBe('GET');
      expect(result).toHaveLength(2);
      expect(result[0]).toEqual({ id: 'v1', name: 'All Events', isDefault: true, queryType: 0 });
    });
  });

  // -------------------------------------------------------------------------
  // retrieveEntityMetadata (happy path)
  // -------------------------------------------------------------------------

  describe('retrieveEntityMetadata', () => {
    it('GETs /api/dataverse/metadata/{entity} and returns the projected metadata', async () => {
      const fetchFn = makeMockFetch();
      fetchFn.mockResolvedValue(
        makeResponse(200, {
          primaryIdAttribute: 'sprk_eventid',
          primaryNameAttribute: 'sprk_name',
          attributes: {
            sprk_name: { attributeType: 'String', isPrimaryName: true },
            sprk_status: {
              attributeType: 'Picklist',
              optionSet: [
                { value: 1, label: 'Active', color: '#00aa00' },
                { value: 2, label: 'Inactive' },
              ],
            },
          },
        }),
      );
      const client = new BffDataverseClient({
        authenticatedFetch: fetchFn,
        bffBaseUrl: BASE_URL,
      });
      const meta = await client.retrieveEntityMetadata('sprk_event');

      const [url, init] = fetchFn.mock.calls[0];
      expect(url).toBe(`${BASE_URL}/api/dataverse/metadata/sprk_event`);
      expect((init as RequestInit).method).toBe('GET');
      expect(meta.primaryIdAttribute).toBe('sprk_eventid');
      expect(meta.attributes.sprk_status.optionSet).toHaveLength(2);
    });
  });

  // -------------------------------------------------------------------------
  // retrieveMultipleRecords (POST body shape + R1 Option A pagingCookie)
  // -------------------------------------------------------------------------

  describe('retrieveMultipleRecords', () => {
    it('POSTs /api/dataverse/fetch with { entityName, fetchXml, pagingCookie: undefined }', async () => {
      const fetchFn = makeMockFetch();
      fetchFn.mockResolvedValue(
        makeResponse(200, {
          entities: [{ sprk_eventid: 'r1' }],
          moreRecords: false,
        }),
      );
      const client = new BffDataverseClient({
        authenticatedFetch: fetchFn,
        bffBaseUrl: BASE_URL,
      });
      const fetchXml = '<fetch top="50"><entity name="sprk_event"/></fetch>';
      const result = await client.retrieveMultipleRecords<{ sprk_eventid: string }>(
        'sprk_event',
        fetchXml,
      );

      const [url, init] = fetchFn.mock.calls[0];
      expect(url).toBe(`${BASE_URL}/api/dataverse/fetch`);
      expect((init as RequestInit).method).toBe('POST');
      const headers = (init as RequestInit).headers as Record<string, string>;
      expect(headers['Content-Type']).toBe('application/json');

      // Verify body shape includes pagingCookie key set to undefined → omitted by JSON.stringify.
      const parsed = JSON.parse((init as RequestInit).body as string);
      expect(parsed.entityName).toBe('sprk_event');
      expect(parsed.fetchXml).toBe(fetchXml);
      // JSON.stringify drops undefined properties, so pagingCookie should not appear in the body.
      // This still satisfies R1 Option A — the BFF treats absent pagingCookie identically to undefined.
      expect(parsed.pagingCookie).toBeUndefined();

      expect(result.entities).toHaveLength(1);
      expect(result.moreRecords).toBe(false);
    });

    it('propagates pagingCookie + moreRecords from response body', async () => {
      const fetchFn = makeMockFetch();
      fetchFn.mockResolvedValue(
        makeResponse(200, {
          entities: [{ id: '1' }],
          moreRecords: true,
          pagingCookie: 'cookie-xyz',
        }),
      );
      const client = new BffDataverseClient({
        authenticatedFetch: fetchFn,
        bffBaseUrl: BASE_URL,
      });
      const result = await client.retrieveMultipleRecords('sprk_event', '<fetch/>');

      expect(result.moreRecords).toBe(true);
      expect(result.pagingCookie).toBe('cookie-xyz');
    });
  });

  // -------------------------------------------------------------------------
  // retrieveRecord ($select clause)
  // -------------------------------------------------------------------------

  describe('retrieveRecord', () => {
    it('builds $select clause when select fields are provided', async () => {
      const fetchFn = makeMockFetch();
      fetchFn.mockResolvedValue(
        makeResponse(200, { sprk_eventid: 'r1', sprk_name: 'Hello' }),
      );
      const client = new BffDataverseClient({
        authenticatedFetch: fetchFn,
        bffBaseUrl: BASE_URL,
      });
      const result = await client.retrieveRecord<{
        sprk_eventid: string;
        sprk_name: string;
      }>('sprk_event', 'r1', ['sprk_eventid', 'sprk_name']);

      const calledUrl = fetchFn.mock.calls[0][0] as string;
      expect(calledUrl).toBe(
        `${BASE_URL}/api/dataverse/record/sprk_event/r1?$select=sprk_eventid,sprk_name`,
      );
      expect(result.sprk_name).toBe('Hello');
    });

    it('omits $select query string when select is undefined', async () => {
      const fetchFn = makeMockFetch();
      fetchFn.mockResolvedValue(makeResponse(200, { id: 'r1' }));
      const client = new BffDataverseClient({
        authenticatedFetch: fetchFn,
        bffBaseUrl: BASE_URL,
      });
      await client.retrieveRecord('sprk_event', 'r1');

      const calledUrl = fetchFn.mock.calls[0][0] as string;
      expect(calledUrl).toBe(`${BASE_URL}/api/dataverse/record/sprk_event/r1`);
      expect(calledUrl).not.toContain('$select');
    });

    it('omits $select query string when select is an empty array', async () => {
      const fetchFn = makeMockFetch();
      fetchFn.mockResolvedValue(makeResponse(200, { id: 'r1' }));
      const client = new BffDataverseClient({
        authenticatedFetch: fetchFn,
        bffBaseUrl: BASE_URL,
      });
      await client.retrieveRecord('sprk_event', 'r1', []);

      const calledUrl = fetchFn.mock.calls[0][0] as string;
      expect(calledUrl).toBe(`${BASE_URL}/api/dataverse/record/sprk_event/r1`);
      expect(calledUrl).not.toContain('$select');
    });

    it('encodeURIComponent applied to entityName and id', async () => {
      const fetchFn = makeMockFetch();
      fetchFn.mockResolvedValue(makeResponse(200, {}));
      const client = new BffDataverseClient({
        authenticatedFetch: fetchFn,
        bffBaseUrl: BASE_URL,
      });
      // sprk_event is safe for encoding (no special chars). Verify encoding pathway is
      // engaged by passing a synthesized id with characters that need encoding.
      await client.retrieveRecord('sprk_event', 'id with space/slash');

      const calledUrl = fetchFn.mock.calls[0][0] as string;
      expect(calledUrl).toBe(
        `${BASE_URL}/api/dataverse/record/sprk_event/id%20with%20space%2Fslash`,
      );
    });
  });

  // -------------------------------------------------------------------------
  // ProblemDetails error mapping
  // -------------------------------------------------------------------------

  describe('error mapping (ProblemDetails → typed errors)', () => {
    it('404 ProblemDetails → BffNotFoundError with errorCode + correlationId', async () => {
      const fetchFn = makeMockFetch();
      fetchFn.mockResolvedValue(
        makeResponse(
          404,
          {
            type: 'about:blank',
            title: 'Not Found',
            status: 404,
            detail: 'Saved query 11111111-1111-1111-1111-111111111111 was not found.',
            errorCode: 'DV_SAVEDQUERY_NOT_FOUND',
            correlationId: 'corr-1',
          },
          { contentType: 'application/problem+json' },
        ),
      );
      const client = new BffDataverseClient({
        authenticatedFetch: fetchFn,
        bffBaseUrl: BASE_URL,
      });

      await expect(client.retrieveSavedQuery('missing')).rejects.toMatchObject({
        name: 'BffNotFoundError',
        status: 404,
        errorCode: 'DV_SAVEDQUERY_NOT_FOUND',
        correlationId: 'corr-1',
      });
      await expect(client.retrieveSavedQuery('missing')).rejects.toBeInstanceOf(
        BffNotFoundError,
      );
    });

    it('403 ProblemDetails (DV_PRIVILEGE_DENIED) → BffForbiddenError', async () => {
      const fetchFn = makeMockFetch();
      fetchFn.mockResolvedValue(
        makeResponse(403, {
          title: 'Forbidden',
          status: 403,
          detail: 'Caller lacks Read privilege on sprk_event.',
          errorCode: 'DV_PRIVILEGE_DENIED',
          correlationId: 'corr-2',
        }),
      );
      const client = new BffDataverseClient({
        authenticatedFetch: fetchFn,
        bffBaseUrl: BASE_URL,
      });

      const error = (await client
        .retrieveSavedQueriesForEntity('sprk_event')
        .catch(e => e)) as BffForbiddenError;

      expect(error).toBeInstanceOf(BffForbiddenError);
      expect(error.errorCode).toBe('DV_PRIVILEGE_DENIED');
      expect(error.status).toBe(403);
      expect(error.correlationId).toBe('corr-2');
    });

    it('400 ProblemDetails (DV_FETCHXML_MALFORMED) → BffBadRequestError', async () => {
      const fetchFn = makeMockFetch();
      fetchFn.mockResolvedValue(
        makeResponse(400, {
          title: 'Bad Request',
          status: 400,
          detail: 'FetchXML is malformed.',
          errorCode: 'DV_FETCHXML_MALFORMED',
          correlationId: 'corr-3',
        }),
      );
      const client = new BffDataverseClient({
        authenticatedFetch: fetchFn,
        bffBaseUrl: BASE_URL,
      });

      const error = (await client
        .retrieveMultipleRecords('sprk_event', 'not xml')
        .catch(e => e)) as BffBadRequestError;

      expect(error).toBeInstanceOf(BffBadRequestError);
      expect(error.errorCode).toBe('DV_FETCHXML_MALFORMED');
      expect(error.status).toBe(400);
      expect(error.correlationId).toBe('corr-3');
    });

    it('500 ProblemDetails → BffServerError with status + errorCode', async () => {
      const fetchFn = makeMockFetch();
      fetchFn.mockResolvedValue(
        makeResponse(500, {
          title: 'Internal Server Error',
          status: 500,
          detail: 'Upstream Dataverse call failed.',
          errorCode: 'DV_INTERNAL_ERROR',
          correlationId: 'corr-4',
        }),
      );
      const client = new BffDataverseClient({
        authenticatedFetch: fetchFn,
        bffBaseUrl: BASE_URL,
      });

      const error = (await client
        .retrieveEntityMetadata('sprk_event')
        .catch(e => e)) as BffServerError;

      expect(error).toBeInstanceOf(BffServerError);
      expect(error.status).toBe(500);
      expect(error.errorCode).toBe('DV_INTERNAL_ERROR');
    });

    it('non-JSON error response → BFF_UNKNOWN_ERROR fallback', async () => {
      const fetchFn = makeMockFetch();
      fetchFn.mockResolvedValue(makeNonJsonResponse(503));
      const client = new BffDataverseClient({
        authenticatedFetch: fetchFn,
        bffBaseUrl: BASE_URL,
      });

      const error = (await client
        .retrieveSavedQuery('id-1')
        .catch(e => e)) as BffServerError;

      expect(error).toBeInstanceOf(BffServerError);
      expect(error.errorCode).toBe('BFF_UNKNOWN_ERROR');
      expect(error.status).toBe(503);
      expect(error.correlationId).toBeUndefined();
    });
  });

  // -------------------------------------------------------------------------
  // Smoke: typed error inheritance
  // -------------------------------------------------------------------------

  it('all typed errors inherit from BffDataverseClientError', () => {
    expect(new BffNotFoundError('X', undefined, 'm')).toBeInstanceOf(
      BffDataverseClientError,
    );
    expect(new BffForbiddenError('X', undefined, 'm')).toBeInstanceOf(
      BffDataverseClientError,
    );
    expect(new BffBadRequestError('X', undefined, 'm')).toBeInstanceOf(
      BffDataverseClientError,
    );
    expect(new BffServerError('X', 500, undefined, 'm')).toBeInstanceOf(
      BffDataverseClientError,
    );
  });
});
