/**
 * ComposeWorkspace.renderOnSave.reducer.test.ts — spaarkeai-compose-r6 task 012 (reducer-level).
 *
 * Guards the two NEW state families the render-on-save cutover added:
 *   1. `loadedContentModel` — the retained canonical model (the imported save mapper's merge base).
 *      Set atomically wherever `projection` is set (loadSucceeded / mountTransient), cleared on every
 *      mount door that has no canonical model (mountDraftHtml, and the clear-rather-than-inherit rule
 *      for a follow-on mount), reset by requestLoad/requestUploadMount/reset (INITIAL_STATE spread),
 *      and REPLACED by `saveSucceeded.contentModel` after a successful model-path save (never
 *      regressed to null when the action omits it).
 *   2. `saveDegradationWarnings` (026-F5) — the SAVE-time warning family, REPLACED wholesale by the
 *      `saveDegradationWarnings` action (null = a clean save clears the banner) and cleared by every
 *      fresh mount.
 *   3. `loadedContentModelWarnings` (task 013, review F7) — the mount-time projection flatten
 *      warnings: same set/clear lifecycle as the model, and cleared by `saveSucceeded` ONLY when the
 *      action adopted a model (a model-path save materialized the loss); an op-log save keeps them.
 */

import { composeWorkspaceReducer, INITIAL_STATE, type ComposeWorkspaceState } from './ComposeWorkspace.types';
import type { ComposeContentModel } from '../types/compose-contracts';

function bytes(): ArrayBuffer {
  return new Uint8Array([0x50, 0x4b, 0x03, 0x04]).buffer;
}

const LOADED_MODEL: ComposeContentModel = {
  blocks: [{ kind: 'Paragraph', paraId: 'AAAA0001', runs: [{ text: 'Loaded clause.' }] }],
};
const SAVED_MODEL: ComposeContentModel = {
  blocks: [{ kind: 'Paragraph', paraId: 'AAAA0001', runs: [{ text: 'Saved clause.' }] }],
};

function loadedState(model: ComposeContentModel | null = LOADED_MODEL): ComposeWorkspaceState {
  let state = composeWorkspaceReducer(INITIAL_STATE, {
    kind: 'requestLoad',
    documentRef: { speDriveItemId: 'spe-1', sprkDocumentId: 'doc-1', fileName: 'contract.docx' },
    sessionId: 'session-1',
  });
  state = composeWorkspaceReducer(state, {
    kind: 'loadSucceeded',
    docxBytes: bytes(),
    etag: 'etag-1',
    versionId: 'v-load',
    sessionId: 'session-1',
    contentModel: model,
  });
  return state;
}

describe('composeWorkspaceReducer — loadedContentModel retention (task 012)', () => {
  it('loadSucceeded retains the canonical model; an omitted field (older BFF) normalizes to null', () => {
    expect(loadedState().loadedContentModel).toEqual(LOADED_MODEL);

    let state = composeWorkspaceReducer(INITIAL_STATE, {
      kind: 'requestLoad',
      documentRef: { speDriveItemId: 'spe-1' },
      sessionId: 's',
    });
    state = composeWorkspaceReducer(state, {
      kind: 'loadSucceeded',
      docxBytes: bytes(),
      etag: null,
      versionId: null,
      sessionId: 's',
      // contentModel omitted — older BFF
    });
    expect(state.loadedContentModel).toBeNull();
  });

  it('mountTransient retains the Upload/Project model and CLEARS a prior document model when omitted', () => {
    // Prior loaded doc left a model behind; a new transient mount WITHOUT one must not inherit it.
    let state = loadedState();
    state = composeWorkspaceReducer(state, {
      kind: 'mountTransient',
      docxBytes: bytes(),
      fileName: 'local.docx',
      sessionId: 'browse-session',
    });
    expect(state.loadedContentModel).toBeNull();

    // And a transient mount WITH one retains it.
    state = composeWorkspaceReducer(state, {
      kind: 'mountTransient',
      docxBytes: bytes(),
      fileName: 'local2.docx',
      sessionId: 'browse-session-2',
      contentModel: LOADED_MODEL,
    });
    expect(state.loadedContentModel).toEqual(LOADED_MODEL);
  });

  it('mountDraftHtml (born-in-editor) clears the model — its saves author via buildContentModel', () => {
    let state = loadedState();
    state = composeWorkspaceReducer(state, { kind: 'mountDraftHtml', html: '<p></p>', sessionId: 'draft-1' });
    expect(state.loadedContentModel).toBeNull();
  });

  it('requestLoad / requestUploadMount / reset all reset the model to null (INITIAL_STATE spread)', () => {
    const base = loadedState();
    expect(
      composeWorkspaceReducer(base, {
        kind: 'requestLoad',
        documentRef: { speDriveItemId: 'spe-2' },
        sessionId: 's2',
      }).loadedContentModel
    ).toBeNull();
    expect(
      composeWorkspaceReducer(base, { kind: 'requestUploadMount', sessionId: 's3' }).loadedContentModel
    ).toBeNull();
    expect(composeWorkspaceReducer(base, { kind: 'reset' }).loadedContentModel).toBeNull();
  });

  it('saveSucceeded REPLACES the model when the action carries one and KEEPS it when omitted (never regress)', () => {
    let state = loadedState();
    state = composeWorkspaceReducer(state, {
      kind: 'saveSucceeded',
      documentSpeId: 'spe-1',
      etag: 'etag-2',
      contentModel: SAVED_MODEL,
    });
    expect(state.loadedContentModel).toEqual(SAVED_MODEL);

    // An op-log-path save (no contentModel on the action) keeps the adopted base.
    state = composeWorkspaceReducer(state, {
      kind: 'saveSucceeded',
      documentSpeId: 'spe-1',
      etag: 'etag-3',
    });
    expect(state.loadedContentModel).toEqual(SAVED_MODEL);
  });
});

describe('composeWorkspaceReducer — saveDegradationWarnings family (026-F5, task 012)', () => {
  const WARNINGS = [{ code: 'text-box-flattened', count: 2 }];

  it('the saveDegradationWarnings action REPLACES the set; null clears it (a clean save clears the banner)', () => {
    let state = loadedState();
    state = composeWorkspaceReducer(state, { kind: 'saveDegradationWarnings', warnings: WARNINGS });
    expect(state.saveDegradationWarnings).toEqual(WARNINGS);

    const NEXT = [{ code: 'comment-anchor-dropped', count: 1 }];
    state = composeWorkspaceReducer(state, { kind: 'saveDegradationWarnings', warnings: NEXT });
    expect(state.saveDegradationWarnings).toEqual(NEXT); // replaced, not merged

    state = composeWorkspaceReducer(state, { kind: 'saveDegradationWarnings', warnings: null });
    expect(state.saveDegradationWarnings).toBeNull();
  });

  it('does NOT touch importWarnings (separate families — the 026-F5 bug was save warnings clobbering imports)', () => {
    let state = loadedState();
    state = composeWorkspaceReducer(state, {
      kind: 'importWarnings',
      warnings: [{ type: 'style', message: 'Import-time simplification.' }],
    });
    state = composeWorkspaceReducer(state, { kind: 'saveDegradationWarnings', warnings: WARNINGS });
    expect(state.importWarnings).toEqual([{ type: 'style', message: 'Import-time simplification.' }]);
    expect(state.saveDegradationWarnings).toEqual(WARNINGS);
  });

  it('fresh mounts clear stale save warnings (mountTransient / mountDraftHtml / requestLoad)', () => {
    let state = loadedState();
    state = composeWorkspaceReducer(state, { kind: 'saveDegradationWarnings', warnings: WARNINGS });

    expect(
      composeWorkspaceReducer(state, { kind: 'mountTransient', docxBytes: bytes(), sessionId: 'b1' })
        .saveDegradationWarnings
    ).toBeNull();
    expect(
      composeWorkspaceReducer(state, { kind: 'mountDraftHtml', html: '<p></p>', sessionId: 'd1' })
        .saveDegradationWarnings
    ).toBeNull();
    expect(
      composeWorkspaceReducer(state, {
        kind: 'requestLoad',
        documentRef: { speDriveItemId: 'spe-9' },
        sessionId: 's9',
      }).saveDegradationWarnings
    ).toBeNull();
  });
});

describe('composeWorkspaceReducer — loadedContentModelWarnings lifecycle (task 013, review F7)', () => {
  const FLATTEN_WARNINGS = [
    { code: 'text-box-flattened', count: 2 },
    { code: 'complex-object-dropped', count: 1 },
  ];

  function loadedStateWithWarnings(): ComposeWorkspaceState {
    let state = composeWorkspaceReducer(INITIAL_STATE, {
      kind: 'requestLoad',
      documentRef: { speDriveItemId: 'spe-1', sprkDocumentId: 'doc-1', fileName: 'contract.docx' },
      sessionId: 'session-1',
    });
    state = composeWorkspaceReducer(state, {
      kind: 'loadSucceeded',
      docxBytes: bytes(),
      etag: 'etag-1',
      versionId: 'v-load',
      sessionId: 'session-1',
      contentModel: LOADED_MODEL,
      contentModelWarnings: FLATTEN_WARNINGS,
    });
    return state;
  }

  it('loadSucceeded retains the flatten warnings alongside the model; omitted (older BFF) → null', () => {
    expect(loadedStateWithWarnings().loadedContentModelWarnings).toEqual(FLATTEN_WARNINGS);
    // Omitted → null (the existing loadedState helper omits the field).
    expect(loadedState().loadedContentModelWarnings).toBeNull();
  });

  it('mountTransient retains them from Upload/Project and CLEARS a prior mount set when omitted', () => {
    let state = loadedStateWithWarnings();
    // A follow-on transient mount WITHOUT warnings must not inherit the prior document's set.
    state = composeWorkspaceReducer(state, {
      kind: 'mountTransient',
      docxBytes: bytes(),
      fileName: 'local.docx',
      sessionId: 'browse-1',
    });
    expect(state.loadedContentModelWarnings).toBeNull();

    state = composeWorkspaceReducer(state, {
      kind: 'mountTransient',
      docxBytes: bytes(),
      fileName: 'local2.docx',
      sessionId: 'browse-2',
      contentModel: LOADED_MODEL,
      contentModelWarnings: FLATTEN_WARNINGS,
    });
    expect(state.loadedContentModelWarnings).toEqual(FLATTEN_WARNINGS);
  });

  it('mountDraftHtml + INITIAL_STATE spreads clear them', () => {
    const base = loadedStateWithWarnings();
    expect(
      composeWorkspaceReducer(base, { kind: 'mountDraftHtml', html: '<p></p>', sessionId: 'd1' })
        .loadedContentModelWarnings
    ).toBeNull();
    expect(
      composeWorkspaceReducer(base, { kind: 'requestUploadMount', sessionId: 's3' }).loadedContentModelWarnings
    ).toBeNull();
    expect(composeWorkspaceReducer(base, { kind: 'reset' }).loadedContentModelWarnings).toBeNull();
  });

  it('a MODEL-PATH saveSucceeded (action carries contentModel) CLEARS them — the loss materialized once', () => {
    let state = loadedStateWithWarnings();
    state = composeWorkspaceReducer(state, {
      kind: 'saveSucceeded',
      documentSpeId: 'spe-1',
      etag: 'etag-2',
      contentModel: SAVED_MODEL,
    });
    expect(state.loadedContentModelWarnings).toBeNull();
    // And they stay cleared on the next model save (no repeat source).
    state = composeWorkspaceReducer(state, {
      kind: 'saveSucceeded',
      documentSpeId: 'spe-1',
      etag: 'etag-3',
      contentModel: SAVED_MODEL,
    });
    expect(state.loadedContentModelWarnings).toBeNull();
  });

  it('an OP-LOG-path saveSucceeded (no contentModel on the action) KEEPS them — the loss has not materialized', () => {
    let state = loadedStateWithWarnings();
    state = composeWorkspaceReducer(state, {
      kind: 'saveSucceeded',
      documentSpeId: 'spe-1',
      etag: 'etag-2',
      // no contentModel — byte-identical / op-log save
    });
    expect(state.loadedContentModelWarnings).toEqual(FLATTEN_WARNINGS);
  });
});
