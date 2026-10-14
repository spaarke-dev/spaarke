/**
 * NavMapClient over the BFF's NavMap error bodies.
 *
 * `NavMapEndpoints` used to answer 400/404 with `{ "error": "..." }`. It now answers with
 * `ProblemDetailsHelper.FromLegacyError` (ADR-019): `application/problem+json` with `type`, `title`,
 * `status`, `detail` AND the old `error` member carrying the same text. `NavMapClient` has its own
 * `fetch` (not `authenticatedFetch`) and reads `errorData.error`, so the message it shows the user must be
 * unchanged. The bodies below are the exact shape the endpoint now writes (pinned server-side by
 * `LegacyErrorBodiesProblemDetailsTests`).
 */

import { NavMapClient } from '../NavMapClient';
import type { ILogger } from '../types';

const silentLogger: ILogger = { info: () => undefined, warn: () => undefined, error: () => undefined };

/** The body `FromLegacyError(status, message)` writes, with the standard title for the status. */
function problemBody(status: number, title: string, message: string, includeLegacyMember = true) {
  return {
    type: `https://tools.ietf.org/html/rfc9110#section-15.5.${status === 404 ? 5 : 1}`,
    title,
    status,
    detail: message,
    ...(includeLegacyMember ? { error: message } : {}),
  };
}

function respondWith(status: number, body: unknown): void {
  (global as unknown as { fetch: jest.Mock }).fetch = jest.fn().mockResolvedValue({
    ok: false,
    status,
    statusText: status === 404 ? 'Not Found' : 'Bad Request',
    headers: { get: () => 'application/problem+json' },
    json: async () => body,
    text: async () => JSON.stringify(body),
  });
}

function client(): NavMapClient {
  return new NavMapClient({
    baseUrl: 'https://bff.example.com',
    getAccessToken: async () => 'token',
    logger: silentLogger,
  });
}

async function failureOf(call: Promise<unknown>): Promise<Error & { status?: number; originalMessage?: string }> {
  try {
    await call;
  } catch (e) {
    return e as Error & { status?: number; originalMessage?: string };
  }
  throw new Error('expected the NavMapClient call to reject');
}

describe('NavMapClient reads the problem+json body NavMapEndpoints now writes', () => {
  it('400: shows the server message from the kept `error` member (unchanged from the bare { error } body)', async () => {
    respondWith(400, problemBody(400, 'Bad Request', 'relationship cannot be empty'));
    const err = await failureOf(client().getLookupNavigation('sprk_document', 'x'));
    expect(err.status).toBe(400);
    expect(err.originalMessage).toBe('relationship cannot be empty');
    expect(err.message).toBe('Failed to get lookup navigation for sprk_document.x: relationship cannot be empty');
  });

  it('400 on the old bare body gave the identical message', async () => {
    respondWith(400, { error: 'relationship cannot be empty' });
    const err = await failureOf(client().getLookupNavigation('sprk_document', 'x'));
    expect(err.message).toBe('Failed to get lookup navigation for sprk_document.x: relationship cannot be empty');
  });

  it('404: carries the server text in originalMessage while showing the metadata-not-found copy', async () => {
    respondWith(404, problemBody(404, 'Not Found', "Entity 'sprk_unknown' not found in Dataverse metadata"));
    const err = await failureOf(client().getEntitySetName('sprk_unknown'));
    expect(err.status).toBe(404);
    expect(err.originalMessage).toBe("Entity 'sprk_unknown' not found in Dataverse metadata");
    expect(err.message).toBe(
      'Failed to get entity set name for sprk_unknown: Metadata not found. The entity or relationship may not exist in Dataverse.'
    );
  });

  it('why the legacy member is kept: without `error` the 400 message degrades to the HTTP status line', async () => {
    respondWith(400, problemBody(400, 'Bad Request', 'relationship cannot be empty', false));
    const err = await failureOf(client().getCollectionNavigation('sprk_matter', 'x'));
    expect(err.originalMessage).toBe('HTTP 400: Bad Request');
  });
});
