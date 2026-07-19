/**
 * ComposeWorkspace.saveBaseline.test.ts — UAT 2026-07-19 P2 regression guard (reducer-level).
 *
 * THE BUG: a BORN-IN-EDITOR doc (mountDraftHtml — AI-drafted / no retained bytes, no load-time
 * versionId) saved fine the FIRST time (create-on-save sends the full contentModel), but its
 * SECOND Save failed with the server's replace-path guard:
 *   "Provide the retained-original 'content' bytes, or 'editedParagraphs' + 'baselineVersionId'…"
 * (the user paraphrased this as "content is required and must be non-empty"). Root cause: the
 * save-response's `driveId` + `versionId` were discarded, so `saveSucceeded` populated
 * `speDriveItemId` (→ the second Save takes the replace path) but left `state.versionId` null and
 * `documentRef.driveId` undefined — so the replace body sent content=undefined +
 * baselineVersionId=undefined and the server could not resolve any baseline.
 *
 * THE FIX (this reducer + triggerSave): `saveSucceeded` now retains the response's driveId +
 * versionId. The version is adopted ONLY on the first persist (adopt-only-when-null) so a STORED
 * doc's LOAD-TIME baseline is never advanced across saves (FR-01 delta-vs-original).
 */

import { composeWorkspaceReducer, INITIAL_STATE, type ComposeWorkspaceState } from './ComposeWorkspace.types';

function bytes(): ArrayBuffer {
  return new Uint8Array([0x50, 0x4b, 0x03, 0x04]).buffer;
}

describe('composeWorkspaceReducer — P2 save-baseline retention (saveSucceeded)', () => {
  it('BORN-IN-EDITOR: first save adopts the response driveId + versionId so a second save can resolve a baseline', () => {
    // mountDraftHtml → transient, no versionId, no driveId (the pre-first-save state).
    let state: ComposeWorkspaceState = composeWorkspaceReducer(INITIAL_STATE, {
      kind: 'mountDraftHtml',
      html: '<p>AI-drafted engagement letter.</p>',
      fileName: 'Test New Matter via Workspace.docx',
      containerId: 'bu-container-1',
    });
    expect(state.versionId).toBeNull();
    expect(state.documentRef?.speDriveItemId).toBe(''); // transient → create-on-save
    expect(state.documentRef?.driveId).toBeUndefined();

    // First create-on-save succeeds: server returns the minted SPE id + its drive + the new version.
    state = composeWorkspaceReducer(state, {
      kind: 'saveSucceeded',
      sprkDocumentId: 'doc-guid-1',
      documentSpeId: 'spe-item-1',
      etag: 'etag-1',
      driveId: 'bu-container-drive-1',
      versionId: 'v-first-save',
    });

    // The mount is no longer transient AND carries everything the replace-path second save needs:
    expect(state.documentRef?.speDriveItemId).toBe('spe-item-1'); // → replace path next time
    expect(state.documentRef?.driveId).toBe('bu-container-drive-1'); // correct drive for re-fetch
    expect(state.versionId).toBe('v-first-save'); // baselineVersionId for the delta
  });

  it('BORN-IN-EDITOR: the first-save version is FIXED — a second save does NOT advance the baseline', () => {
    let state = composeWorkspaceReducer(INITIAL_STATE, {
      kind: 'mountDraftHtml',
      html: '<p>Draft.</p>',
      containerId: 'bu-container-1',
    });
    state = composeWorkspaceReducer(state, {
      kind: 'saveSucceeded',
      documentSpeId: 'spe-item-1',
      etag: 'etag-1',
      driveId: 'drive-1',
      versionId: 'v-first-save',
    });
    // Second save returns a NEW version — the baseline must stay the FIRST-save version so the
    // delta is always computed against a single fixed baseline (mirrors the FR-01 stored-doc rule).
    state = composeWorkspaceReducer(state, {
      kind: 'saveSucceeded',
      documentSpeId: 'spe-item-1',
      etag: 'etag-2',
      driveId: 'drive-1',
      versionId: 'v-second-save',
    });
    expect(state.versionId).toBe('v-first-save');
  });

  it('STORED doc: saveSucceeded does NOT advance the LOAD-TIME versionId (FR-01 delta-vs-original)', () => {
    // requestLoad establishes the documentRef; loadSucceeded then carries the LOAD-TIME versionId.
    let state = composeWorkspaceReducer(INITIAL_STATE, {
      kind: 'requestLoad',
      documentRef: { speDriveItemId: 'spe-item-2', sprkDocumentId: 'doc-guid-2', fileName: 'Opened matter.docx' },
      sessionId: 'session-1',
    });
    state = composeWorkspaceReducer(state, {
      kind: 'loadSucceeded',
      docxBytes: bytes(),
      etag: 'etag-load',
      versionId: 'v-load-time',
      sessionId: 'session-1',
      sprkDocumentId: 'doc-guid-2',
      fileName: 'Opened matter.docx',
    });
    expect(state.versionId).toBe('v-load-time');

    // A save returns a fresh version; the baseline must remain the load-time original.
    state = composeWorkspaceReducer(state, {
      kind: 'saveSucceeded',
      documentSpeId: state.documentRef!.speDriveItemId,
      etag: 'etag-save',
      driveId: 'drive-x',
      versionId: 'v-after-save',
    });
    expect(state.versionId).toBe('v-load-time');
  });

  it('OLDER BFF (no driveId/versionId in the response): degrades gracefully, no baseline fabricated', () => {
    let state = composeWorkspaceReducer(INITIAL_STATE, {
      kind: 'mountDraftHtml',
      html: '<p>Draft.</p>',
      containerId: 'bu-container-1',
    });
    state = composeWorkspaceReducer(state, {
      kind: 'saveSucceeded',
      documentSpeId: 'spe-item-1',
      etag: 'etag-1',
      // driveId + versionId omitted (older BFF response shape)
    });
    expect(state.documentRef?.speDriveItemId).toBe('spe-item-1');
    expect(state.versionId).toBeNull();
    expect(state.documentRef?.driveId).toBeUndefined();
  });
});
