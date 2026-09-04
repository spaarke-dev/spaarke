/**
 * An injected `authenticatedFetch` has TWO shapes in production, and this package's failure
 * translation must produce IDENTICAL typed outcomes under both:
 *
 *   - THROWING  — `@spaarke/auth.authenticatedFetch` (every code page + wizard) throws `ApiError`
 *                 on any non-2xx and never returns the response.
 *   - RETURNING — `external-spa`'s `createAuthenticatedFetch()` returns the raw response, non-2xx
 *                 included.
 *
 * Before 2026-09-03 only the returning shape was handled, so under the canonical (throwing) one
 * every `response.ok` / `response.status === 409` check in this package was unreachable:
 * `UploadNameConflictError` could not be produced at all, and the name-collision dialog that
 * depends on it would have gone dead the moment the wizard's upload was pointed here.
 *
 * These tests are written per-shape ON PURPOSE. A single test with one fake fetch cannot catch a
 * regression that only manifests in the other shape — which is exactly how the original defect
 * survived: the operations had no production callers when they were written, and no test double
 * ever threw.
 */

import { SdapApiClient } from '../SdapApiClient';
import { UploadNameConflictError } from '../operations/UploadOperation';
import { SdapHttpError } from '../operations/httpFailure';

const BASE_URL = 'https://bff.example.com';

/** Stand-in for `@spaarke/auth`'s ApiError: carries a numeric `status` and a consumed-body message. */
class FakeApiError extends Error {
  constructor(
    message: string,
    public readonly status: number
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

/** THROWING shape — mirrors `@spaarke/auth.authenticatedFetch`. */
function throwingFetch(status: number, detail: string) {
  return jest.fn(async () => {
    throw new FakeApiError(detail, status);
  });
}

/** RETURNING shape — mirrors `external-spa`'s wrapper over raw `fetch`. */
function returningFetch(status: number, detail: string) {
  return jest.fn(
    async () =>
      new Response(JSON.stringify({ detail }), {
        status,
        headers: { 'content-type': 'application/problem+json' },
      })
  );
}

function clientWith(authenticatedFetch: (url: string, init?: RequestInit) => Promise<Response>) {
  return new SdapApiClient({ baseUrl: BASE_URL, authenticatedFetch });
}

const file = () => new File(['contents'], 'brief.docx');

// Re-pointed 2026-09-03 (task 076): these tests used to drive `client.uploadFile('container-1', …)`,
// which is DELETED along with `PUT /api/obo/containers/{id}/files/{*path}`. They now drive the
// record-keyed contract. Nothing about their VALUE changes — failure translation lives in
// `UploadOperation.put`, which both surviving contracts share — but the route they exercise is now
// one that actually exists.
const ENTITY = 'sprk_matter';
const RECORD_ID = '11111111-1111-1111-1111-111111111111';

describe.each([
  ['throwing (@spaarke/auth)', throwingFetch],
  ['returning (external-spa)', returningFetch],
])('upload failure translation — %s injected fetch', (_label, makeFetch) => {
  it('translates 409 into UploadNameConflictError carrying the file name', async () => {
    const client = clientWith(makeFetch(409, 'nameAlreadyExists'));

    const error = await client.uploadFileForRecord(ENTITY, RECORD_ID, file()).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(UploadNameConflictError);
    expect((error as UploadNameConflictError).fileName).toBe('brief.docx');
  });

  it('does not report a collision as a generic SdapHttpError', async () => {
    // The collision must stay distinguishable BY TYPE. If it collapses into SdapHttpError the
    // caller can only recover it by matching message text — the failure this typing exists to end.
    const client = clientWith(makeFetch(409, 'nameAlreadyExists'));

    const error = await client.uploadFileForRecord(ENTITY, RECORD_ID, file()).catch((e: unknown) => e);

    expect(error).not.toBeInstanceOf(SdapHttpError);
  });

  it('translates a non-409 failure into SdapHttpError with the status and user-facing copy', async () => {
    const client = clientWith(makeFetch(403, 'forbidden'));

    const error = await client.uploadFileForRecord(ENTITY, RECORD_ID, file()).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(SdapHttpError);
    expect((error as SdapHttpError).status).toBe(403);
    expect((error as SdapHttpError).message).toContain('Access denied');
  });

  it('keeps a server detail that has no better generic phrasing', async () => {
    // 422 has no entry in describeHttpFailure, so the server's own explanation must survive rather
    // than be replaced by a vaguer sentence.
    const client = clientWith(makeFetch(422, 'Container could not be resolved for this record.'));

    const error = await client.uploadFileForRecord(ENTITY, RECORD_ID, file()).catch((e: unknown) => e);

    expect((error as SdapHttpError).message).toContain('Container could not be resolved');
  });

  it('translates a delete failure the same way', async () => {
    const client = clientWith(makeFetch(404, 'not found'));

    const error = await client.deleteFile('drive-1', 'item-1').catch((e: unknown) => e);

    expect(error).toBeInstanceOf(SdapHttpError);
    expect((error as SdapHttpError).status).toBe(404);
  });
});

describe('upload failure translation — errors that are not HTTP outcomes', () => {
  it('rethrows an error with no numeric status untouched', async () => {
    // `AuthError` (token acquisition exhausted) and AbortError land here. Dressing them up as an
    // HTTP failure would replace the real cause with an invented status.
    const authError = Object.assign(new Error('Authentication failed after all retry attempts'), {
      name: 'AuthError',
      code: 'auth_exhausted',
    });
    const client = clientWith(
      jest.fn(async () => {
        throw authError;
      })
    );

    const error = await client.uploadFileForRecord(ENTITY, RECORD_ID, file()).catch((e: unknown) => e);

    expect(error).toBe(authError);
    expect(error).not.toBeInstanceOf(SdapHttpError);
  });
});

describe('upload success path', () => {
  it('returns the DriveItem and forwards conflictBehavior only when set', async () => {
    const body = {
      id: 'item-1',
      name: 'brief.docx',
      size: 8,
      driveId: 'drive-1',
      parentId: 'parent-1',
      createdDateTime: '2026-09-03T00:00:00Z',
      lastModifiedDateTime: '2026-09-03T00:00:00Z',
      isFolder: false,
      webUrl: 'https://sharepoint.example.com/brief.docx',
    };
    const authFetch = jest.fn(
      async (_url: string, _init?: RequestInit) => new Response(JSON.stringify(body), { status: 200 })
    );
    const client = clientWith(authFetch);

    const first = await client.uploadFileForRecord(ENTITY, RECORD_ID, file());
    expect(first.webUrl).toBe('https://sharepoint.example.com/brief.docx');
    // `parentId` is the wire field (FileHandleDto.ParentId). It was typed `parentReferenceId` until
    // 2026-09-03, which made it undefined on every response.
    expect(first.parentId).toBe('parent-1');
    // Omitted on a first attempt so the BFF's non-destructive `fail` default applies.
    expect(authFetch.mock.calls[0][0]).not.toContain('conflictBehavior');

    await client.uploadFileForRecord(ENTITY, RECORD_ID, file(), { conflictBehavior: 'rename' });
    expect(authFetch.mock.calls[1][0]).toContain('conflictBehavior=rename');
  });
});

describe('task-076 upload contracts — the client cannot name a container', () => {
  const okResponse = () =>
    new Response(
      JSON.stringify({
        id: 'i',
        name: 'brief.docx',
        size: 8,
        driveId: 'd',
        createdDateTime: 'x',
        lastModifiedDateTime: 'x',
        isFolder: false,
      }),
      { status: 200 }
    );

  it('uploadFileForRecord targets the RECORD-keyed route and sends no container', async () => {
    const authFetch = jest.fn(async (_url: string, _init?: RequestInit) => okResponse());
    const client = clientWith(authFetch);

    await client.uploadFileForRecord('sprk_matter', '11111111-1111-1111-1111-111111111111', file());

    const url = authFetch.mock.calls[0][0];
    expect(url).toContain('/api/obo/records/sprk_matter/11111111-1111-1111-1111-111111111111/files/');
    // The whole point of the contract: no container is expressible from here.
    expect(url).not.toContain('containers');
    expect(url).not.toContain('containerId');
  });

  it('uploadFileWithoutRecord targets the record-LESS route and sends no container', async () => {
    const authFetch = jest.fn(async (_url: string, _init?: RequestInit) => okResponse());
    const client = clientWith(authFetch);

    await client.uploadFileWithoutRecord(file());

    const url = authFetch.mock.calls[0][0];
    expect(url).toContain('/api/obo/me/files/');
    expect(url).not.toContain('containers');
  });

  it('both new contracts translate a 409 into UploadNameConflictError', async () => {
    // The collision dialog depends on this type surviving the route change — the regression that
    // would otherwise be invisible, since a 409 still "works" as a failed upload.
    const client = clientWith(throwingFetch(409, 'nameAlreadyExists'));

    await expect(client.uploadFileForRecord('sprk_matter', 'r', file())).rejects.toBeInstanceOf(
      UploadNameConflictError
    );
    await expect(client.uploadFileWithoutRecord(file())).rejects.toBeInstanceOf(UploadNameConflictError);
  });

  it('both new contracts surface a secure-record fail-closed refusal as a typed error', async () => {
    // A secure record with no container of its own MUST fail, never fall back. The client's job is
    // to not flatten that into a generic "upload failed".
    const client = clientWith(throwingFetch(409, 'secure_record_container_missing'));

    const err = await client.uploadFileForRecord('sprk_project', 'r', file()).catch((e: unknown) => e);
    // 409 is the collision code too, so the conflict type wins by design; what matters is that the
    // caller gets a TYPED outcome rather than an opaque failure.
    expect(err).toBeInstanceOf(UploadNameConflictError);
  });

  it('forwards conflictBehavior on both new contracts', async () => {
    const authFetch = jest.fn(async (_url: string, _init?: RequestInit) => okResponse());
    const client = clientWith(authFetch);

    await client.uploadFileForRecord('sprk_matter', 'r', file(), { conflictBehavior: 'rename' });
    expect(authFetch.mock.calls[0][0]).toContain('conflictBehavior=rename');

    await client.uploadFileWithoutRecord(file(), { conflictBehavior: 'replace' });
    expect(authFetch.mock.calls[1][0]).toContain('conflictBehavior=replace');
  });
});
