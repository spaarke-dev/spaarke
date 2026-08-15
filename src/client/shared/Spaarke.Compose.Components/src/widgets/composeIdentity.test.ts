// Tests for FR-07(b) (spaarkeai-compose-r7, task 010): the non-rotating logical document id,
// its client-only persistence, the identity accessor, and reducer persist-through.
import {
  COMPOSE_ACTIVE_DRAFT_ID_KEY,
  mintComposeLogicalId,
  startNewComposeLogicalId,
  recoverActiveComposeLogicalId,
  persistActiveComposeLogicalId,
  clearActiveComposeLogicalId,
} from './composeIdentity';
import { getComposeLogicalIdentity } from '../types/compose-contracts';
import { composeWorkspaceReducer, INITIAL_STATE } from './ComposeWorkspace.types';

describe('composeIdentity — mint + persistence (FR-07b)', () => {
  beforeEach(() => {
    try {
      window.localStorage.clear();
    } catch {
      /* jsdom always provides localStorage */
    }
  });

  it('mints distinct, non-empty ids', () => {
    const a = mintComposeLogicalId();
    const b = mintComposeLogicalId();
    expect(a).toBeTruthy();
    expect(b).toBeTruthy();
    expect(a).not.toEqual(b);
  });

  it('startNewComposeLogicalId persists the id to the active-draft slot', () => {
    const id = startNewComposeLogicalId();
    expect(window.localStorage.getItem(COMPOSE_ACTIVE_DRAFT_ID_KEY)).toEqual(id);
    expect(recoverActiveComposeLogicalId()).toEqual(id);
  });

  it('the logical id is STABLE across a simulated re-mount / reload (no fresh mint)', () => {
    // First mount: user starts a new document.
    const minted = startNewComposeLogicalId();
    // Simulate widget unmount + page reload: in-memory state is gone, but the storage slot
    // persists. The recovery path reads the SAME id back — no fresh mint.
    const recovered = recoverActiveComposeLogicalId();
    expect(recovered).toEqual(minted);
  });

  it('a NEW document replaces the slot (a distinct logical id)', () => {
    const first = startNewComposeLogicalId();
    const second = startNewComposeLogicalId();
    expect(second).not.toEqual(first);
    expect(recoverActiveComposeLogicalId()).toEqual(second);
  });

  it('clearActiveComposeLogicalId empties the slot (promotion → no recovery)', () => {
    startNewComposeLogicalId();
    clearActiveComposeLogicalId();
    expect(recoverActiveComposeLogicalId()).toBeNull();
  });

  it('persistActiveComposeLogicalId round-trips an explicit id (recovery re-mount)', () => {
    persistActiveComposeLogicalId('explicit-logical-id');
    expect(recoverActiveComposeLogicalId()).toEqual('explicit-logical-id');
  });

  it('helpers never throw when storage is unavailable', () => {
    const original = Object.getOwnPropertyDescriptor(window, 'localStorage');
    Object.defineProperty(window, 'localStorage', {
      configurable: true,
      get() {
        throw new Error('localStorage blocked');
      },
    });
    try {
      expect(() => startNewComposeLogicalId()).not.toThrow();
      expect(recoverActiveComposeLogicalId()).toBeNull();
      expect(() => clearActiveComposeLogicalId()).not.toThrow();
    } finally {
      if (original) Object.defineProperty(window, 'localStorage', original);
    }
  });
});

describe('getComposeLogicalIdentity — accessor derivation (FR-07b)', () => {
  it('prefers sprkDocumentId', () => {
    expect(
      getComposeLogicalIdentity({ sprkDocumentId: 'doc-1', speDriveItemId: 'drive-1', composeLogicalId: 'lid-1' })
    ).toEqual('doc-1');
  });

  it('falls to speDriveItemId when sprkDocumentId is absent', () => {
    expect(getComposeLogicalIdentity({ speDriveItemId: 'drive-1', composeLogicalId: 'lid-1' })).toEqual('drive-1');
  });

  it('guards the transient "" speDriveItemId sentinel and falls to composeLogicalId', () => {
    // Transient mounts set speDriveItemId:'' — a bare ?? would wrongly return ''.
    expect(getComposeLogicalIdentity({ speDriveItemId: '', composeLogicalId: 'lid-1' })).toEqual('lid-1');
  });

  it('returns undefined when no identity is present', () => {
    expect(getComposeLogicalIdentity({ speDriveItemId: '' })).toBeUndefined();
    expect(getComposeLogicalIdentity(null)).toBeUndefined();
    expect(getComposeLogicalIdentity(undefined)).toBeUndefined();
  });
});

describe('reducer persist-through of composeLogicalId (FR-07b)', () => {
  it('mountTransient stamps composeLogicalId onto documentRef', () => {
    const next = composeWorkspaceReducer(INITIAL_STATE, {
      kind: 'mountTransient',
      docxBytes: new ArrayBuffer(8),
      fileName: 'x.docx',
      transientKey: 'tk-1',
      composeLogicalId: 'lid-1',
    });
    expect(next.documentRef?.composeLogicalId).toEqual('lid-1');
    expect(next.documentRef?.speDriveItemId).toEqual(''); // still transient
    expect(getComposeLogicalIdentity(next.documentRef)).toEqual('lid-1');
  });

  it('mountDraftHtml stamps composeLogicalId onto documentRef', () => {
    const next = composeWorkspaceReducer(INITIAL_STATE, {
      kind: 'mountDraftHtml',
      html: '<p>hi</p>',
      fileName: 'x.docx',
      transientKey: 'tk-1',
      composeLogicalId: 'lid-2',
    });
    expect(next.documentRef?.composeLogicalId).toEqual('lid-2');
    expect(getComposeLogicalIdentity(next.documentRef)).toEqual('lid-2');
  });

  it('FR-07c: a pendingAssistantInsert carrying an id-less-but-logical ref keeps a dedup identity', () => {
    // The task-011 call site replaces the empty `{ speDriveItemId: '' }` sentinel with a ref that
    // carries a composeLogicalId (inherited from the mounted doc, or freshly minted). Once staged
    // through the reducer, that ref still yields a non-empty dedup identity via the accessor — so the
    // id-less assistant-insert door no longer enters the save path with an empty identity.
    const staged = composeWorkspaceReducer(INITIAL_STATE, {
      kind: 'pendingAssistantInsert',
      payload: {
        type: 'compose_assistant_insert',
        documentRef: { speDriveItemId: '', composeLogicalId: 'lid-idless' },
        sourceNodeId: '',
        sourcePlaybookId: '',
        contentHtml: '<p>x</p>',
        format: 'html',
        insertMode: 'insert-at-cursor',
        requireUserConfirm: true,
        sessionId: '',
        timestamp: '2026-08-15T00:00:00.000Z',
      },
    });
    expect(staged.pendingAssistantInsert?.documentRef.composeLogicalId).toEqual('lid-idless');
    expect(getComposeLogicalIdentity(staged.pendingAssistantInsert?.documentRef)).toEqual('lid-idless');
  });

  it('saveSucceeded preserves composeLogicalId and the accessor promotes to sprkDocumentId', () => {
    const mounted = composeWorkspaceReducer(INITIAL_STATE, {
      kind: 'mountDraftHtml',
      html: '<p>hi</p>',
      fileName: 'x.docx',
      transientKey: 'tk-1',
      composeLogicalId: 'lid-3',
    });
    const saved = composeWorkspaceReducer(mounted, {
      kind: 'saveSucceeded',
      sprkDocumentId: 'doc-99',
      documentSpeId: 'spe-99',
      etag: 'etag-1',
      driveId: 'drive-9',
      versionId: 'v1',
    });
    // composeLogicalId is retained on the ref (record of prior transient identity)...
    expect(saved.documentRef?.composeLogicalId).toEqual('lid-3');
    // ...but the accessor now prefers the promoted sprkDocumentId.
    expect(getComposeLogicalIdentity(saved.documentRef)).toEqual('doc-99');
  });
});
