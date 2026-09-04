/**
 * FileUploadService over the SHARED `@spaarke/sdap-client` client.
 *
 * This is the seam that changed on 2026-09-03: `FileUploadService` used to hold this package's own
 * parallel `SdapApiClient` (deleted) and now holds the shared one. The seam matters because the
 * name-collision signal crosses it — a collision has to arrive here as `UploadNameConflictError`
 * and leave as `ServiceResult.nameConflict`, or the wizard's "Keep both / Save as new version"
 * dialog silently degrades into an opaque failure.
 *
 * The client is REAL, not a double, and the fake sits one layer lower at `authenticatedFetch` —
 * deliberately, and in its THROWING shape (`@spaarke/auth`, what production injects). A double at
 * the client boundary would assert that this file re-packages an error it was handed, which was
 * never in doubt; the thing worth pinning is that a 409 on the wire still becomes a user-resolvable
 * choice after crossing three layers of translation.
 */

import { SdapApiClient } from '@spaarke/sdap-client';
import { FileUploadService } from '../FileUploadService';
import type { ILogger, UploadTarget } from '../types';

const silentLogger: ILogger = {
  info: () => undefined,
  warn: () => undefined,
  error: () => undefined,
};

/** Stand-in for `@spaarke/auth`'s ApiError — carries a numeric `status`, body already consumed. */
class FakeApiError extends Error {
  constructor(
    message: string,
    public readonly status: number
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

function serviceOver(authenticatedFetch: (url: string, init?: RequestInit) => Promise<Response>) {
  const client = new SdapApiClient({ baseUrl: 'https://bff.example.com', authenticatedFetch });
  return new FileUploadService(client, silentLogger);
}

const file = () => new File(['contents'], 'brief.docx');

/** The record-keyed target every record-bearing caller now uses (task 076). */
const RECORD_TARGET: UploadTarget = {
  kind: 'record',
  entityLogicalName: 'sprk_matter',
  recordId: '11111111-1111-1111-1111-111111111111',
};

describe('FileUploadService over @spaarke/sdap-client', () => {
  it('reports a 409 as a recoverable nameConflict, not as a plain error', async () => {
    const service = serviceOver(async () => {
      throw new FakeApiError('nameAlreadyExists', 409);
    });

    const result = await service.uploadFile({ file: file(), target: RECORD_TARGET });

    expect(result.success).toBe(false);
    expect(result.nameConflict).toEqual({ fileName: 'brief.docx' });
    // The message must read as a choice, not a failure — this text reaches the user verbatim.
    expect(result.error).toContain('already exists');
  });

  it('reports a real failure with NO nameConflict, so the UI does not offer a bogus choice', async () => {
    const service = serviceOver(async () => {
      throw new FakeApiError('forbidden', 403);
    });

    const result = await service.uploadFile({ file: file(), target: RECORD_TARGET });

    expect(result.success).toBe(false);
    expect(result.nameConflict).toBeUndefined();
    expect(result.error).toContain('Access denied');
  });

  it('forwards conflictBehavior on a retry after the user has chosen', async () => {
    const authFetch = jest.fn(async (_url: string, _init?: RequestInit) => uploadResponse());
    const service = serviceOver(authFetch);

    await service.uploadFile({ file: file(), target: RECORD_TARGET });
    // Omitted first time so the BFF's non-destructive `fail` default applies.
    expect(authFetch.mock.calls[0][0]).not.toContain('conflictBehavior');

    await service.uploadFile({ file: file(), target: RECORD_TARGET, conflictBehavior: 'replace' });
    expect(authFetch.mock.calls[1][0]).toContain('conflictBehavior=replace');
  });

  it('maps the DriveItem onto SpeFileMetadata including the fields consumers persist', async () => {
    const service = serviceOver(async () => uploadResponse());

    const result = await service.uploadFile({ file: file(), target: RECORD_TARGET });

    expect(result.success).toBe(true);
    // `webUrl` -> sprk_filepath, `parentId` -> parent folder, and `driveId` -> sprk_graphdriveid.
    // All three were absent or misnamed on the shared DriveItem type until 2026-09-02/03; a mapping
    // that compiles is not evidence that the field arrives.
    expect(result.data?.webUrl).toBe('https://sharepoint.example.com/brief.docx');
    expect(result.data?.sharePointUrl).toBe('https://sharepoint.example.com/brief.docx');
    expect(result.data?.parentId).toBe('parent-1');
    expect(result.data?.driveItemId).toBe('item-1');
    expect(result.data?.fileSize).toBe(8);
    // 🔴 The one the record-keyed contract makes load-bearing: `sprk_document.sprk_graphdriveid`
    // is now sourced from HERE, not from any container the client resolved. If this drops, every
    // uploaded document gets a null drive pointer and later downloads 404.
    expect(result.data?.driveId).toBe('drive-1');
  });

  // ── Route selection — the whole point of task 076 ─────────────────────────────────────────────
  //
  // Asserted on the URL rather than on a client double: the failure being locked out is a
  // record-bearing upload silently taking the record-LESS route, which files a secure record's
  // documents in the CALLER's business-unit container. That is invisible at the client boundary and
  // only shows up in the path.

  it('sends a record-bearing upload to the RECORD-keyed route, naming no container', async () => {
    const authFetch = jest.fn(async (_url: string, _init?: RequestInit) => uploadResponse());
    const service = serviceOver(authFetch);

    await service.uploadFile({ file: file(), target: RECORD_TARGET });

    const url = authFetch.mock.calls[0][0];
    expect(url).toContain('/api/obo/records/sprk_matter/11111111-1111-1111-1111-111111111111/files/brief.docx');
    expect(url).not.toContain('/api/obo/containers/');
    expect(url).not.toContain('/api/obo/me/');
  });

  it('sends a parentless upload to the record-LESS route', async () => {
    const authFetch = jest.fn(async (_url: string, _init?: RequestInit) => uploadResponse());
    const service = serviceOver(authFetch);

    await service.uploadFile({ file: file(), target: { kind: 'no-record' } });

    const url = authFetch.mock.calls[0][0];
    expect(url).toContain('/api/obo/me/files/brief.docx');
    expect(url).not.toContain('/api/obo/containers/');
  });

  it('refuses a record target missing its identifiers instead of downgrading to the /me route', async () => {
    const authFetch = jest.fn(async () => uploadResponse());
    const service = serviceOver(authFetch);

    for (const target of [
      { kind: 'record', entityLogicalName: '', recordId: 'rec-1' },
      { kind: 'record', entityLogicalName: 'sprk_matter', recordId: '' },
    ] as UploadTarget[]) {
      const result = await service.uploadFile({ file: file(), target });

      expect(result.success).toBe(false);
      expect(result.error).toContain('owning record');
    }

    // Fail CLOSED: no request at all. Falling back to `/api/obo/me/files` here would put a secure
    // record's document in the caller's business-unit container, which cannot be undone.
    expect(authFetch).not.toHaveBeenCalled();
  });
});

/**
 * A successful upload response.
 *
 * Hand-rolled rather than `new Response(...)`: this package's jest environment is jsdom without the
 * undici globals, so `Response` is not defined here. Only `ok` / `status` / `json()` are read by
 * the path under test.
 */
function uploadResponse(): Response {
  return {
    ok: true,
    status: 200,
    json: async () => ({
      id: 'item-1',
      name: 'brief.docx',
      size: 8,
      driveId: 'drive-1',
      parentId: 'parent-1',
      createdDateTime: '2026-09-03T00:00:00Z',
      lastModifiedDateTime: '2026-09-03T00:00:00Z',
      isFolder: false,
      webUrl: 'https://sharepoint.example.com/brief.docx',
    }),
  } as unknown as Response;
}
