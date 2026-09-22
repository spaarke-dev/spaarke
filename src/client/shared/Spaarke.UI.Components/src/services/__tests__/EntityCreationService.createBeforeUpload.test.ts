/**
 * EntityCreationService — create-before-upload contract (task 093 close-out).
 *
 * Task 076 cut every wizard over from upload-then-create to create-then-upload: a record must exist
 * before its bytes can move. `uploadFilesToSpe` enforces this by requiring `recordId: string` as a
 * mandatory positional parameter and by resolving the upload to the record-KEYED BFF route
 * (`PUT /api/obo/records/{entity}/{recordId}/files/{name}`), never the record-less one
 * (`PUT /api/obo/me/files/{name}`).
 *
 * This file pins that ROUTING behaviour at runtime — perturbation-proven below (see
 * `notes/task-093-close-out.md` for the RED/GREEN evidence): swapping the record-keyed SDAP
 * operation for the record-less one inside `uploadFilesToSpe` turns this test red, with the exact
 * URL divergence shown in the failure output.
 *
 * ⚠️ **What this file deliberately does NOT attempt, and why.** The task-093 escalation trigger
 * anticipated needing a compile-fail fixture (`@ts-expect-error`) if the runtime form of "recordId
 * lost its required-ness" were unrepresentable. That was tried and abandoned after two empirical
 * checks against THIS package's actual toolchain, not assumed:
 *
 *   1. `jest.config.js`'s `ts-jest` transform resolves this package's `tsconfig.json`, which sets
 *      `"isolatedModules": true`. Confirmed by probe: an `@ts-expect-error` placed above a line with
 *      NO type error (which must fail to compile — TS2578 "Unused '@ts-expect-error' directive")
 *      instead **passed** under `npx jest`. ts-jest's isolatedModules mode transpiles each file
 *      independently (Babel-like) and does not perform cross-checked type diagnostics, so a
 *      compile-fail fixture inside a `.test.ts` file is silently inert here — it would pass whether
 *      the pinned contract holds or not, which is exactly the decorative-test failure mode this task
 *      exists to retire, not reintroduce.
 *   2. This package's `tsconfig.json` (the one the real `tsc` build in `npm run build` uses) has an
 *      `exclude` list that drops the `__tests__` directory and every `.test.ts` / `.test.tsx` file
 *      outright — so even the real compiler never type-checks this directory. There is no tool in
 *      this package's pipeline that would ever evaluate a type-only assertion placed in a
 *      `__tests__` file.
 *   3. A `Function.prototype.length`-based arity pin was also tried and abandoned: measured empirically
 *      at 3 for BOTH the real 4-parameter signature (`entityLogicalName, recordId, files, onProgress?`)
 *      AND a perturbed 3-parameter one with `recordId` removed (`entityLogicalName, files,
 *      onProgress?`) — this toolchain's compiled output does not vary arity with parameter count in a
 *      way this contract could distinguish, so it cannot serve as a pin either.
 *
 * The runtime routing test below is therefore the pin: it is the one mechanism in this package that
 * is both (a) reachable by `npm test` and (b) proven, by actual perturbation, to fail when the
 * create-before-upload contract breaks.
 */

import { EntityCreationService } from '../EntityCreationService';
import type { IWebApiWithCreate } from '../../types/WebApiLike';
import type { IUploadedFile } from '../../components/FileUpload/fileUploadTypes';

function stubWebApi(): IWebApiWithCreate {
  return {
    retrieveRecord: jest.fn(),
    retrieveMultipleRecords: jest.fn(),
    createRecord: jest.fn(),
  };
}

function uploadedFile(name = 'brief.docx'): IUploadedFile {
  return {
    id: `upload:${name}`,
    name,
    sizeBytes: 8,
    fileType: 'docx',
    file: new File(['contents'], name),
  };
}

/**
 * A successful upload response, hand-rolled rather than `new Response(...)`: this package's jest
 * environment is jsdom without the undici globals, so `Response` is not constructable here. Only
 * `ok` / `status` / `json()` are read by the path under test (mirrors
 * `document-upload/__tests__/FileUploadService.sharedClient.test.ts`'s `uploadResponse()`).
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
      createdDateTime: '2026-09-21T00:00:00Z',
      lastModifiedDateTime: '2026-09-21T00:00:00Z',
      isFolder: false,
      webUrl: 'https://sharepoint.example.com/brief.docx',
    }),
  } as unknown as Response;
}

describe('EntityCreationService.uploadFilesToSpe — create-before-upload contract (task 093)', () => {
  it('routes a record-keyed upload to the RECORD-keyed URL, never the record-less one', async () => {
    const authFetch = jest.fn(async (_url: string, _init?: RequestInit) => uploadResponse());
    const service = new EntityCreationService(stubWebApi(), authFetch, 'https://bff.example.com');

    const result = await service.uploadFilesToSpe('sprk_matter', '11111111-1111-1111-1111-111111111111', [
      uploadedFile(),
    ]);

    expect(result.successCount).toBe(1);
    expect(result.failureCount).toBe(0);

    // The seam that matters: if `uploadFilesToSpe` ever silently delegated to the record-LESS
    // operation (`uploadFileWithoutRecord`), the URL would flip to `/api/obo/me/files/...` while the
    // method signature — and every caller — stayed exactly the same. Asserted on the URL, not on a
    // mocked SDAP client method, so a regression inside the routing itself cannot hide behind a
    // double that already assumes the right route was picked. This is the assertion perturbed and
    // proven to redden — see notes/task-093-close-out.md for the exact before/after counts and the
    // captured failure output (URL flips to `https://bff.example.com/api/obo/me/files/brief.docx`).
    expect(authFetch).toHaveBeenCalledTimes(1);
    const url = authFetch.mock.calls[0][0] as string;
    expect(url).toContain('/api/obo/records/sprk_matter/11111111-1111-1111-1111-111111111111/files/brief.docx');
    expect(url).not.toContain('/api/obo/me/files/');
  });
});
