/**
 * MultiFileUploadService — name-collision propagation.
 *
 * **What this protects.** A same-named upload is a RECOVERABLE outcome: the BFF uploads with
 * `conflictBehavior=fail` by default, so nothing is written and the user can be offered
 * "Keep both" / "Save as new version". That offer is only possible if the `nameConflict` marker
 * survives the trip from `FileUploadService` up to the wizard.
 *
 * **The bug this locks out.** This service used to wrap its whole result loop in `try/catch` and
 * re-throw `new Error(serviceResult.error)` on failure. That discarded `serviceResult.nameConflict`
 * — the only field distinguishing "a file of this name exists, choose one" from "the upload
 * broke". Downstream the collision arrived as an untyped error string and surfaced to the user as
 * "Unknown error occurred", after which (pre-`93d5e673e`) the server's `replace` default had
 * already overwritten the stored file. The overwrite is fixed server-side; these tests keep the
 * *signal* from being flattened again on the client.
 *
 * Deliberately tests the real `MultiFileUploadService` against a hand-written `FileUploadService`
 * stub — no HTTP is involved at this seam, so there is no `fetch`/handler mock here (ADR-038).
 */

import { MultiFileUploadService } from '../MultiFileUploadService';
import type { FileUploadService } from '../FileUploadService';
import type {
  ILogger,
  ServiceResult,
  SpeFileMetadata,
  FileUploadRequest,
  UploadProgress,
  UploadTarget,
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

function speMetadata(name: string): SpeFileMetadata {
  return {
    id: `item-${name}`,
    name,
    size: 10,
    createdDateTime: '2026-09-02T00:00:00Z',
    lastModifiedDateTime: '2026-09-02T00:00:00Z',
    isFolder: false,
  };
}

/**
 * Stub upload service driven by a per-file-name script, recording the requests it received so the
 * downward `conflictBehavior` plumbing can be asserted too.
 */
function stubUploadService(script: Record<string, ServiceResult<SpeFileMetadata>>): {
  service: FileUploadService;
  requests: FileUploadRequest[];
} {
  const requests: FileUploadRequest[] = [];
  const service = {
    uploadFile: (request: FileUploadRequest): Promise<ServiceResult<SpeFileMetadata>> => {
      requests.push(request);
      const scripted = script[request.file.name];
      if (!scripted) {
        throw new Error(`Test script has no entry for "${request.file.name}"`);
      }
      return Promise.resolve(scripted);
    },
  } as unknown as FileUploadService;
  return { service, requests };
}

function file(name: string): File {
  return new File(['x'], name, { type: 'text/plain' });
}

// Task 076: a batch names its OWNING RECORD, never a container. `MultiFileUploadService` only
// forwards this — the branch that turns it into a route lives in `FileUploadService` — but the
// forwarding is asserted below, because a target that silently defaulted to `no-record` would file
// a secure record's documents in the caller's business-unit container.
const TARGET: UploadTarget = { kind: 'record', entityLogicalName: 'sprk_matter', recordId: 'rec-1' };

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('MultiFileUploadService — name-collision propagation', () => {
  it('carries nameConflict onto the error entry instead of flattening it to a string', async () => {
    const { service } = stubUploadService({
      'contract.docx': {
        success: false,
        error: 'A file named "contract.docx" already exists in this location.',
        nameConflict: { fileName: 'contract.docx' },
      },
    });

    const result = await new MultiFileUploadService(service, silentLogger).uploadFiles({
      files: [file('contract.docx')],
      target: TARGET,
    });

    expect(result.errors).toHaveLength(1);
    expect(result.errors[0].nameConflict).toEqual({ fileName: 'contract.docx' });
    // The message is still present — a consumer that only reads `error` keeps working.
    expect(result.errors[0].error).toContain('already exists');
    expect(result.uploadedFiles).toHaveLength(0);
  });

  it('reports nameConflict on the progress callback so the row can offer a choice', async () => {
    const { service } = stubUploadService({
      'contract.docx': {
        success: false,
        error: 'already exists',
        nameConflict: { fileName: 'contract.docx' },
      },
    });

    const progress: UploadProgress[] = [];
    await new MultiFileUploadService(service, silentLogger).uploadFiles(
      { files: [file('contract.docx')], target: TARGET },
      p => progress.push(p)
    );

    const failed = progress.filter(p => p.status === 'failed');
    expect(failed).toHaveLength(1);
    expect(failed[0].nameConflict).toEqual({ fileName: 'contract.docx' });
  });

  it('leaves nameConflict unset for a genuine failure', async () => {
    const { service } = stubUploadService({
      'broken.docx': { success: false, error: 'File upload failed: HTTP 500' },
    });

    const result = await new MultiFileUploadService(service, silentLogger).uploadFiles({
      files: [file('broken.docx')],
      target: TARGET,
    });

    expect(result.errors).toHaveLength(1);
    expect(result.errors[0].nameConflict).toBeUndefined();
  });

  it('leaves nameConflict unset when the upload throws rather than returning a result', async () => {
    const service = {
      uploadFile: () => Promise.reject(new Error('Request timeout after 300000ms')),
    } as unknown as FileUploadService;

    const result = await new MultiFileUploadService(service, silentLogger).uploadFiles({
      files: [file('slow.docx')],
      target: TARGET,
    });

    expect(result.errors[0].error).toContain('timeout');
    expect(result.errors[0].nameConflict).toBeUndefined();
  });

  it('isolates a collision to its own file — the rest of the batch still uploads', async () => {
    const { service } = stubUploadService({
      'ok.docx': { success: true, data: speMetadata('ok.docx') },
      'clash.docx': {
        success: false,
        error: 'already exists',
        nameConflict: { fileName: 'clash.docx' },
      },
    });

    const result = await new MultiFileUploadService(service, silentLogger).uploadFiles({
      files: [file('ok.docx'), file('clash.docx')],
      target: TARGET,
    });

    expect(result.successCount).toBe(1);
    expect(result.failureCount).toBe(1);
    expect(result.uploadedFiles.map(f => f.name)).toEqual(['ok.docx']);
    expect(result.errors[0].nameConflict).toEqual({ fileName: 'clash.docx' });
  });

  it('forwards the upload target down to every file in the batch', async () => {
    const { service, requests } = stubUploadService({
      'a.docx': { success: true, data: speMetadata('a.docx') },
      'b.docx': { success: true, data: speMetadata('b.docx') },
    });

    await new MultiFileUploadService(service, silentLogger).uploadFiles({
      files: [file('a.docx'), file('b.docx')],
      target: TARGET,
    });

    expect(requests.map(r => r.target)).toEqual([TARGET, TARGET]);
  });

  it('forwards a record-LESS target verbatim rather than inventing a record', async () => {
    const { service, requests } = stubUploadService({
      'a.docx': { success: true, data: speMetadata('a.docx') },
    });

    await new MultiFileUploadService(service, silentLogger).uploadFiles({
      files: [file('a.docx')],
      target: { kind: 'no-record' },
    });

    expect(requests[0].target).toEqual({ kind: 'no-record' });
  });

  it('omits conflictBehavior by default so the server keeps its non-destructive `fail`', async () => {
    const { service, requests } = stubUploadService({
      'a.docx': { success: true, data: speMetadata('a.docx') },
    });

    await new MultiFileUploadService(service, silentLogger).uploadFiles({
      files: [file('a.docx')],
      target: TARGET,
    });

    expect(requests).toHaveLength(1);
    expect(requests[0].conflictBehavior).toBeUndefined();
  });

  it.each(['rename', 'replace'] as const)('passes an explicit conflictBehavior=%s down on a retry', async behavior => {
    const { service, requests } = stubUploadService({
      'a.docx': { success: true, data: speMetadata('a.docx') },
    });

    await new MultiFileUploadService(service, silentLogger).uploadFiles({
      files: [file('a.docx')],
      target: TARGET,
      conflictBehavior: behavior,
    });

    expect(requests[0].conflictBehavior).toBe(behavior);
  });
});
