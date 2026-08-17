/**
 * ComposeWorkspace.reloadFromSource.reducer.test.ts — FR-09 (R6 D4 / task 071) reducer-level guard.
 *
 * ROOT CAUSE (R6 D4 "Reload from source blanks the page + asks for a new upload"): the BFF Load response
 * carries an authoritative `driveId` (where the doc lives), but `loadSucceeded` never stamped it onto
 * `documentRef`. So after a load, `documentRef.driveId` stayed undefined for any doc whose drive the host
 * `driveId` prop doesn't identify (a BU-container / PDF-sourced / born-in-editor doc). On Reload-from-source
 * (`requestLoad` with `state.documentRef`), the Load effect computes `loadDriveId = documentRef.driveId ??
 * effectiveDriveId`; with both falsy it hits the `!loadDriveId → dispatch({kind:'reset'})` branch →
 * INITIAL_STATE → the "pick another document or re-upload this one" empty state. That is the blank.
 *
 * FIX (pure client mount-state — no server round-trip, no mount-contract change): `loadSucceeded` now
 * stamps the load response's `driveId` onto `documentRef`, mirroring the shipped `saveSucceeded` re-target
 * stamp. A subsequent Reload-from-source then always carries the correct drive → never resets to blank.
 *
 * This reducer test proves the stamp + its retention through the reload's `requestLoad`. The full-component
 * flow (click Reload → GET succeeds → editable) is the CI-only ui-test (ComposeWorkspace mounts @spaarke/*).
 */

import { composeWorkspaceReducer, INITIAL_STATE, type ComposeWorkspaceState } from './ComposeWorkspace.types';

function bytes(): ArrayBuffer {
  return new Uint8Array([0x50, 0x4b, 0x03, 0x04]).buffer;
}

/** A doc whose drive the host prop does NOT identify (BU-container) — the D4 repro shape. */
function loadedFromDrive(driveId: string | undefined): ComposeWorkspaceState {
  let state = composeWorkspaceReducer(INITIAL_STATE, {
    kind: 'requestLoad',
    // No driveId on the ref yet (the pre-load state) — exactly the door that used to lose it.
    documentRef: { speDriveItemId: 'spe-bu-1', sprkDocumentId: 'doc-bu-1', fileName: 'matter.docx' },
    sessionId: 'session-71',
  });
  state = composeWorkspaceReducer(state, {
    kind: 'loadSucceeded',
    docxBytes: bytes(),
    etag: 'etag-71',
    versionId: 'v-71',
    sessionId: 'session-71',
    driveId,
  });
  return state;
}

describe('composeWorkspaceReducer — Reload-from-source drive retention (FR-09 / R6 D4)', () => {
  it('loadSucceeded stamps the authoritative load-time driveId onto documentRef', () => {
    const state = loadedFromDrive('b!bu-container-drive');
    expect(state.status).toBe('loaded');
    expect(state.documentRef?.driveId).toBe('b!bu-container-drive');
  });

  it('a subsequent Reload-from-source (requestLoad) RETAINS the drive — so the Load effect never hits the !loadDriveId reset', () => {
    const loaded = loadedFromDrive('b!bu-container-drive');
    // Reload-from-source dispatches requestLoad with the CURRENT documentRef (onReloadFromSource).
    const reloading = composeWorkspaceReducer(loaded, {
      kind: 'requestLoad',
      documentRef: loaded.documentRef!,
      sessionId: loaded.sessionId,
      externalChange: true,
    });
    expect(reloading.status).toBe('loading');
    // The drive survives the remount → loadDriveId is truthy → the reload fetches instead of resetting.
    expect(reloading.documentRef?.driveId).toBe('b!bu-container-drive');
  });

  it('defensive: loadSucceeded with driveId omitted preserves any existing documentRef.driveId (no regression)', () => {
    // Seed a state whose documentRef already carries a drive (e.g. a create-on-save re-target).
    const seeded = loadedFromDrive('b!existing-drive');
    // A later loadSucceeded that omits driveId (older BFF) must NOT wipe it.
    const reloaded = composeWorkspaceReducer(
      composeWorkspaceReducer(seeded, {
        kind: 'requestLoad',
        documentRef: seeded.documentRef!,
        sessionId: seeded.sessionId,
      }),
      {
        kind: 'loadSucceeded',
        docxBytes: bytes(),
        etag: null,
        versionId: null,
        sessionId: seeded.sessionId,
        // driveId omitted
      }
    );
    expect(reloaded.documentRef?.driveId).toBe('b!existing-drive');
  });
});
