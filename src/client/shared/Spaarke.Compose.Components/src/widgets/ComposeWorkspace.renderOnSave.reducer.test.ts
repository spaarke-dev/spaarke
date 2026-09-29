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

// ---------------------------------------------------------------------------
// Task 042 (FR-06, PDF intake) — the `sourceFormat` lifecycle (041-review binding test plan):
// set + transient-key mint at loadSucceeded; cleared + re-targeted + fileName-swapped + versionId
// re-baselined by saveSucceeded; RETAINED by saveFailed (retry dedups to the same new record);
// cleared by every fresh mount; older-BFF omission → null (bit-identical prior behavior).
// ---------------------------------------------------------------------------

describe('composeWorkspaceReducer — sourceFormat lifecycle (task 042 / FR-06 PDF intake)', () => {
  // null = the mount carries NO fileName at all (the undefined-name fallback case; an explicit
  // `undefined` argument would trigger the default parameter instead).
  function pdfLoadedState(fileName: string | null = 'Corteva NDA.pdf'): ComposeWorkspaceState {
    let state = composeWorkspaceReducer(INITIAL_STATE, {
      kind: 'requestLoad',
      documentRef: {
        speDriveItemId: 'spe-pdf-1',
        sprkDocumentId: 'source-pdf-record',
        fileName: fileName ?? undefined,
      },
      sessionId: 'session-pdf',
    });
    state = composeWorkspaceReducer(state, {
      kind: 'loadSucceeded',
      docxBytes: bytes(),
      etag: 'etag-pdf',
      versionId: null, // MEDIUM-3: the server suppresses the .pdf item's version id
      sessionId: 'session-pdf',
      contentModel: LOADED_MODEL,
      sourceFormat: 'pdf',
      transientKey: 'pdf-dedup-key-1',
    });
    return state;
  }

  it('loadSucceeded sets sourceFormat and carries the minted transient dedup key onto documentRef', () => {
    const state = pdfLoadedState();
    expect(state.sourceFormat).toBe('pdf');
    expect(state.documentRef?.transientKey).toBe('pdf-dedup-key-1');
  });

  // FR-A09 (r8 task 044) — the REFRESH case. A PDF that has already been saved as a Word document
  // resolves, on re-open, to that document instead of being projected again. The identity we save
  // against therefore has to come from the RESPONSE. Getting this wrong is not a cosmetic slip: the
  // client would hold docx content while still pointing at the .pdf item, and the save path refuses
  // that outright (docx bytes must never be written over a PDF) — so the user's save would fail with a
  // 422 they cannot act on.
  it('loadSucceeded adopts the SERVED drive-item id when the server resumes a PDF on the docx it already became', () => {
    let state = composeWorkspaceReducer(INITIAL_STATE, {
      kind: 'requestLoad',
      documentRef: { speDriveItemId: 'spe-pdf-1', sprkDocumentId: 'source-pdf-record' },
      sessionId: 'session-pdf',
    });
    state = composeWorkspaceReducer(state, {
      kind: 'loadSucceeded',
      docxBytes: bytes(),
      etag: 'etag-docx-v1',
      // The resumed .docx has REAL version coordinates — unlike the .pdf item, whose version id the
      // server suppresses. This is what lets the next save resolve a baseline and clone.
      versionId: 'v-docx-1',
      sessionId: 'session-pdf',
      contentModel: LOADED_MODEL,
      // We asked for the PDF; the server served the Word document it already became.
      speDriveItemId: 'spe-new-docx-1',
      driveId: 'drive-new-docx',
      sprkDocumentId: 'new-docx-record',
      // NOT 'pdf' — nothing was projected from a PDF on this load; it is an ordinary docx load.
      sourceFormat: null,
    });

    expect(state.documentRef?.speDriveItemId).toBe('spe-new-docx-1');
    expect(state.documentRef?.driveId).toBe('drive-new-docx');
    expect(state.documentRef?.sprkDocumentId).toBe('new-docx-record');
    expect(state.versionId).toBe('v-docx-1');
    expect(state.sourceFormat).toBeNull();
  });

  it('loadSucceeded WITHOUT a served id (older BFF) keeps the requested drive-item id — no regression', () => {
    // The re-target must be additive: an older BFF omits the field entirely, and the identity the
    // client asked with has to survive that untouched.
    let state = composeWorkspaceReducer(INITIAL_STATE, {
      kind: 'requestLoad',
      documentRef: { speDriveItemId: 'spe-docx-existing', sprkDocumentId: 'record-1' },
      sessionId: 'session-1',
    });
    state = composeWorkspaceReducer(state, {
      kind: 'loadSucceeded',
      docxBytes: bytes(),
      etag: 'etag-1',
      versionId: 'v-load',
      sessionId: 'session-1',
      contentModel: LOADED_MODEL,
    });

    expect(state.documentRef?.speDriveItemId).toBe('spe-docx-existing');
  });

  it('older-BFF omission (no sourceFormat field) normalizes to null — prior behavior bit-identical', () => {
    const state = loadedState();
    expect(state.sourceFormat).toBeNull();
    expect(state.documentRef?.transientKey).toBeUndefined();
  });

  it('saveSucceeded clears sourceFormat, re-targets documentRef to the NEW docx identity, swaps the fileName, and re-baselines versionId from the response (B-LOW-3)', () => {
    let state = pdfLoadedState();
    state = composeWorkspaceReducer(state, {
      kind: 'saveSucceeded',
      sprkDocumentId: 'new-docx-record',
      documentSpeId: 'spe-new-docx-1',
      driveId: 'drive-new-docx',
      etag: 'etag-docx-v1',
      versionId: 'v-docx-1',
    });
    expect(state.sourceFormat).toBeNull();
    expect(state.documentRef?.speDriveItemId).toBe('spe-new-docx-1');
    expect(state.documentRef?.driveId).toBe('drive-new-docx');
    expect(state.documentRef?.sprkDocumentId).toBe('new-docx-record');
    expect(state.documentRef?.fileName).toBe('Corteva NDA.docx');
    // B-LOW-3: the load-time versionId was null (the .pdf item's version is meaningless); the save
    // response's id becomes the new doc's baseline.
    expect(state.versionId).toBe('v-docx-1');
  });

  it('fileName swap handles the uppercase .PDF extension and the undefined-name fallback (B-LOW-4 mirror)', () => {
    let upper = pdfLoadedState('SIGNED AGREEMENT.PDF');
    upper = composeWorkspaceReducer(upper, {
      kind: 'saveSucceeded',
      documentSpeId: 'spe-x',
      etag: null,
    });
    expect(upper.documentRef?.fileName).toBe('SIGNED AGREEMENT.docx');

    // 042-review LOW-3: a fileName with NO extension — the .pdf-strip regex no-ops and .docx appends.
    let bare = pdfLoadedState('Corteva NDA');
    bare = composeWorkspaceReducer(bare, {
      kind: 'saveSucceeded',
      documentSpeId: 'spe-z',
      etag: null,
    });
    expect(bare.documentRef?.fileName).toBe('Corteva NDA.docx');

    let noName = pdfLoadedState(null);
    noName = composeWorkspaceReducer(noName, {
      kind: 'saveSucceeded',
      documentSpeId: 'spe-y',
      etag: null,
    });
    // Mirrors triggerSave's ('document.pdf' → 'document.docx') fallback so local state never
    // diverges from the server record.
    expect(noName.documentRef?.fileName).toBe('document.docx');
  });

  it('saveFailed RETAINS sourceFormat and the transient key — a retry create-on-saves onto the SAME new record', () => {
    let state = pdfLoadedState();
    state = composeWorkspaceReducer(state, { kind: 'saveFailed', errorMessage: 'network blip' });
    expect(state.sourceFormat).toBe('pdf');
    expect(state.documentRef?.transientKey).toBe('pdf-dedup-key-1');
    expect(state.documentRef?.speDriveItemId).toBe('spe-pdf-1');
  });

  it('every fresh mount clears sourceFormat (clear-rather-than-inherit)', () => {
    const base = pdfLoadedState();
    expect(
      composeWorkspaceReducer(base, {
        kind: 'requestLoad',
        documentRef: { speDriveItemId: 'spe-other' },
        sessionId: 's-next',
      }).sourceFormat
    ).toBeNull();
    expect(
      composeWorkspaceReducer(base, {
        kind: 'mountTransient',
        docxBytes: bytes(),
        fileName: 'local.docx',
        sessionId: 'b1',
      }).sourceFormat
    ).toBeNull();
    expect(
      composeWorkspaceReducer(base, { kind: 'mountDraftHtml', html: '<p></p>', sessionId: 'd1' }).sourceFormat
    ).toBeNull();
    expect(composeWorkspaceReducer(base, { kind: 'reset' }).sourceFormat).toBeNull();
  });

  it('task 051 (FR-06 parity): a mountTransient carrying sourceFormat="pdf" (Browse/Upload PDF fork) sets state.sourceFormat', () => {
    // Task 050 gave the mount doors the PDF fork, so a Browse-project / Assistant-upload mount CAN now be
    // PDF-sourced. The reducer must carry the marker (drives the editor's editable admission + the PDF
    // create-on-save routing) — previously mountTransient hardcoded sourceFormat:null.
    const state = composeWorkspaceReducer(INITIAL_STATE, {
      kind: 'mountTransient',
      docxBytes: bytes(),
      fileName: 'Corteva NDA.pdf',
      sessionId: 'browse-pdf-1',
      transientKey: 'pdf-browse-key',
      sourceFormat: 'pdf',
    });
    expect(state.sourceFormat).toBe('pdf');
    expect(state.documentRef?.fileName).toBe('Corteva NDA.pdf');
    expect(state.documentRef?.transientKey).toBe('pdf-browse-key');
    // A transient PDF mount has no SPE pointer yet — first Save runs create-on-save (routed by sourceFormat).
    expect(state.documentRef?.speDriveItemId).toBe('');
  });

  it('a NON-pdf save keeps the pre-041 versionId adopt-only-when-null semantics (no regression)', () => {
    let state = loadedState(); // versionId 'v-load' retained from load
    state = composeWorkspaceReducer(state, {
      kind: 'saveSucceeded',
      documentSpeId: 'spe-1',
      etag: 'e2',
      versionId: 'v-new',
    });
    // A stored doc's versionId stays FIXED across saves (delta-vs-load-time-original contract).
    expect(state.versionId).toBe('v-load');
  });
});
