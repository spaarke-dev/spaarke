/**
 * ComposeWorkspace.renderOnSave.test.tsx — spaarkeai-compose-r6 task 012 (save-routing flip).
 *
 * Drives the REAL `ComposeWorkspace.triggerSave` with a stubbed `ComposeEditor` handle and the
 * `@spaarke/auth` fetch boundary mocked, and asserts the render-on-save cutover:
 *   1. MODEL PATH — a loaded/imported doc whose Load response carried `contentModel` saves by
 *      posting `{ contentModel: built.model, content }` with NO operationLog / paraIdMap / comments /
 *      baselineVersionId; `buildImportedContentModel` receives the LOADED model + the BINDING
 *      trackChanges default (null origin → imported → true); `recaptureBaselineSnapshot` +
 *      `commitSaved` fire exactly once after the 200.
 *   2. WARNING FAMILY (026-F5) — server `degradationWarnings` merge with the mapper's warnings
 *      (counts summed per code) into the SEPARATE save-degradation banner, which renders even though
 *      the workspace passes `hideImportWarnings`; a subsequent CLEAN save clears the banner.
 *   3. FALLBACKS — a null mapper return, or a Load response without `contentModel`, keeps the
 *      transitional op-log shape byte-for-byte (operationLog + comments + paraIdMap still sent).
 *   4. ORIGIN — a durable 'authored' marker flips trackChanges to false.
 *   5. UPLOAD DOOR — an assistant-upload mount whose upload response carried `contentModel`
 *      create-on-saves via the model shape (contentModel + content + transientKey/forkNew, no
 *      comments/paraIdMap/operationLog).
 *   6. BORN-IN-EDITOR (scope amendment) — the create-on-save body carries `contentModel` from
 *      `buildContentModel()` and NO separate `comments` field (the editor folds threads into the
 *      model now).
 *
 * ADR-038: a client behavior test at the save-orchestration seam; mocks the network at the
 * `@spaarke/auth` boundary + stubs a heavy child — NOT a banned Mock<HttpMessageHandler>/DI test.
 */

import * as React from 'react';
import { render, screen, waitFor, act } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

// Fluent's MessageBar uses ResizeObserver, which jsdom lacks.
if (typeof (globalThis as { ResizeObserver?: unknown }).ResizeObserver === 'undefined') {
  (globalThis as { ResizeObserver?: unknown }).ResizeObserver = class {
    observe(): void {}
    unobserve(): void {}
    disconnect(): void {}
  };
}

const SPE_ID = 'drive-item-doc-1';
const DRIVE_ID = 'drive-1';
const DOC_SESSION = '00000000-0000-0000-0000-0000000d0c07';
// Base64 of the ZIP magic `PK\x03\x04` — the retained mount bytes round-trip byte-identical.
const CONTENT_B64 = 'UEsDBA==';

const LOADED_MODEL = { blocks: [{ kind: 'Paragraph', paraId: 'AAAA0001', runs: [{ text: 'Loaded clause.' }] }] };
const BUILT_MODEL = { blocks: [{ kind: 'Paragraph', paraId: 'AAAA0001', runs: [{ text: 'Merged clause.' }] }] };
const BORN_MODEL = { blocks: [{ kind: 'Paragraph', runs: [{ text: 'Born in editor.' }] }] };
const CAPTURED_OP = { type: 'insertText', paraId: 'AAAA0001', at: { runIndex: 0, offset: 3 }, text: 'x' };
const STUB_COMMENT = { paraId: 'AAAA0001', start: 0, end: 4, text: 'a session comment' };

// ── Mutable per-test config the fetch mock + editor stub read ──
const config: {
  loadContentModel: unknown; // undefined = older BFF (field omitted)
  loadOrigin: 'authored' | 'imported' | undefined;
  saveDegradationWarnings: Array<{ code: string; count: number }> | undefined;
  saveContentModel: unknown;
  builtResult: { model: unknown; warnings: Array<{ code: string; count: number }> } | null;
} = {
  loadContentModel: undefined,
  loadOrigin: undefined,
  saveDegradationWarnings: undefined,
  saveContentModel: undefined,
  builtResult: { model: BUILT_MODEL, warnings: [] },
};

const saveRequests: Array<{ url: string; body: Record<string, unknown> }> = [];

const authenticatedFetchMock = jest.fn(async (url: string, init?: RequestInit): Promise<Response> => {
  const saveResponse = (documentSpeId: string) =>
    ({
      ok: true,
      status: 200,
      json: async () => ({
        documentSpeId,
        documentRecordId: 'sprk-doc-1',
        driveId: DRIVE_ID,
        versionId: 'v-after-save',
        eTag: 'etag-2',
        size: 500,
        wasPromotedThisSave: false,
        degradationWarnings: config.saveDegradationWarnings,
        contentModel: config.saveContentModel,
      }),
    }) as unknown as Response;

  if (url.includes('/api/compose/documents/create-on-save')) {
    saveRequests.push({ url, body: JSON.parse(String(init?.body ?? '{}')) as Record<string, unknown> });
    return saveResponse('spe-created-1');
  }
  if (url.includes('/api/compose/documents/') && url.includes('/save')) {
    saveRequests.push({ url, body: JSON.parse(String(init?.body ?? '{}')) as Record<string, unknown> });
    return saveResponse(SPE_ID);
  }
  if (url.includes('/api/compose/upload')) {
    return {
      ok: true,
      status: 200,
      json: async () => ({
        content: CONTENT_B64,
        fileName: 'uploaded.docx',
        projection: { status: 'success', canEdit: true, html: '<p data-paraid="AAAA0001">Loaded clause.</p>' },
        contentModel: config.loadContentModel,
      }),
    } as unknown as Response;
  }
  // Stored-document Load.
  if (url.includes('/api/compose/documents/')) {
    return {
      ok: true,
      status: 200,
      json: async () => ({
        documentSpeId: SPE_ID,
        driveId: DRIVE_ID,
        sessionId: DOC_SESSION,
        documentRecordId: 'sprk-doc-1',
        content: CONTENT_B64,
        eTag: 'etag-1',
        versionId: 'v-load',
        fileName: 'contract.docx',
        size: 500,
        anchoredAnnotations: [],
        definedTermsTracking: [],
        actionHistory: [],
        projection: { status: 'success', canEdit: true, html: '<p data-paraid="AAAA0001">Loaded clause.</p>' },
        contentModel: config.loadContentModel,
        origin: config.loadOrigin,
      }),
    } as unknown as Response;
  }
  return { ok: false, status: 404, json: async () => [], text: async () => '' } as unknown as Response;
});

jest.mock('@spaarke/auth', () => ({
  authenticatedFetch: (...args: unknown[]) => authenticatedFetchMock(...(args as [string, RequestInit?])),
  ApiError: class ApiError extends Error {
    status: number;
    constructor(message: string, status: number) {
      super(message);
      this.status = status;
    }
  },
  useAuth: () => ({
    isAuthenticated: true,
    getAccessToken: async () => 'test-token',
    authenticatedFetch: (...args: unknown[]) => authenticatedFetchMock(...(args as [string, RequestInit?])),
    tenantId: 'test-tenant',
    logout: jest.fn(),
  }),
  // `virtual` lets this suite run even when the sibling lib's `dist/` is not built locally (the
  // pre-existing condition that stops the other ComposeWorkspace.*.test suites in a fresh clone);
  // with dist built (CI) the mock behaves identically.
}), { virtual: true });

jest.mock('@spaarke/ui-components', () => ({
  createXrmNavigationService: () => ({ openLookup: jest.fn() }),
  createXrmDataService: () => ({ retrieveRecord: jest.fn() }),
  SendEmailDialog: () => null,
  SprkModal: () => null,
  RichFilePreviewDialog: () => null,
}), { virtual: true });
jest.mock('@spaarke/document-operations', () => ({
  useDocumentActions: () => ({ openInWeb: jest.fn(), openInDesktop: jest.fn(), isActing: false }),
}), { virtual: true });
jest.mock('@spaarke/ai-widgets/events', () => ({
  useDispatchPaneEvent: () => jest.fn(),
  usePaneEvent: () => undefined,
}));
jest.mock('./hooks', () => ({
  useComposeBroadcastChannel: () => ({ postFocusMe: jest.fn(), postForceClosed: jest.fn() }),
  useComposeCheckoutLifecycle: () => ({ forceCloseAndAcquire: jest.fn(), discardAndCancel: jest.fn() }),
  useComposeHeartbeatGate: () => undefined,
}));
jest.mock('./useComposeWordShuttle', () => ({
  useComposePullAnnotations: () => ({ pull: jest.fn() }),
  useComposeCheckChanges: () => ({ checkChanges: jest.fn() }),
  anchoredAnnotationsToPriorAnchors: () => [],
  anchoredAnnotationsToDocxAnnotations: () => [],
}));
jest.mock('./useComposeReanchor', () => ({
  useComposeReanchor: () => ({ summary: null, reanchor: jest.fn(), reset: jest.fn() }),
}));
jest.mock('./ComposeToolbar', () => ({
  ComposeToolbar: () => <div data-testid="compose-toolbar-stub" />,
}));

// ── ComposeEditor stub — dirty imported handle with the task-012 model-mapper members. ──
const commitSavedMock = jest.fn();
const recaptureBaselineSnapshotMock = jest.fn();
const serializeOperationLogMock = jest.fn(() => ({
  orderedOps: [{ operation: { ...CAPTURED_OP }, deletedContentFlag: false }],
  baseVersion: 'v-load',
}));
const buildImportedContentModelMock = jest.fn(
  (_loadedModel: unknown, _opts: { trackChanges: boolean }) => config.builtResult
);
const editorProps: { current: { onSave?: () => void; canSave?: boolean } } = { current: {} };
jest.mock('./ComposeEditor', () => {
  const ReactLib = require('react');
  return {
    ComposeEditor: ReactLib.forwardRef(
      (
        props: { onSave?: () => void; canSave?: boolean; onDirtyChange?: (d: boolean) => void },
        ref: React.Ref<unknown>
      ) => {
        editorProps.current = props;
        ReactLib.useEffect(() => {
          props.onDirtyChange?.(true);
        }, []);
        ReactLib.useImperativeHandle(ref, () => ({
          isDirty: () => true,
          serializeOperationLog: serializeOperationLogMock,
          commitSaved: commitSavedMock,
          getBaselineParaIdMap: () => [{ paraId: 'AAAA0001', text: 'Loaded clause.' }],
          // Non-empty on purpose: proves the model shapes OMIT `comments` deliberately while the
          // op-log fallback still carries them.
          getAnchoredComments: () => [STUB_COMMENT],
          getRedlineAnnotations: () => [],
          hasPendingRedlines: () => false,
          buildContentModel: () => BORN_MODEL,
          buildImportedContentModel: buildImportedContentModelMock,
          recaptureBaselineSnapshot: recaptureBaselineSnapshotMock,
          getCounts: () => ({ characters: 0, words: 0 }),
        }));
        return <div data-testid="compose-editor-stub" />;
      }
    ),
  };
});

// Import AFTER mocks.
// eslint-disable-next-line import/first
import { ComposeWorkspace } from './ComposeWorkspace';

function renderStoredDoc() {
  return render(
    <FluentProvider theme={webLightTheme}>
      <ComposeWorkspace
        initialDocumentRef={{ speDriveItemId: SPE_ID, sprkDocumentId: 'sprk-doc-1', fileName: 'contract.docx' }}
        initialSessionId={DOC_SESSION}
        bffBaseUrl="https://bff.example.test"
        driveId={DRIVE_ID}
        tenantId="tenant-1"
      />
    </FluentProvider>
  );
}

function renderUploadMount() {
  return render(
    <FluentProvider theme={webLightTheme}>
      <ComposeWorkspace
        initialUploadRef={{ sessionId: DOC_SESSION, sessionFileId: 'file-1', fileName: 'uploaded.docx' }}
        bffBaseUrl="https://bff.example.test"
        driveId=""
        tenantId="tenant-1"
        containerId="bu-container-1"
      />
    </FluentProvider>
  );
}

function renderBornInEditor() {
  return render(
    <FluentProvider theme={webLightTheme}>
      <ComposeWorkspace
        initialDraftRef={{ html: '<p>Born-in-editor draft.</p>', fileName: 'draft.docx' }}
        bffBaseUrl="https://bff.example.test"
        driveId=""
        tenantId="tenant-1"
        containerId="bu-container-1"
      />
    </FluentProvider>
  );
}

beforeEach(() => {
  window.sessionStorage.clear();
  authenticatedFetchMock.mockClear();
  commitSavedMock.mockClear();
  recaptureBaselineSnapshotMock.mockClear();
  serializeOperationLogMock.mockClear();
  buildImportedContentModelMock.mockClear();
  saveRequests.length = 0;
  editorProps.current = {};
  config.loadContentModel = undefined;
  config.loadOrigin = undefined;
  config.saveDegradationWarnings = undefined;
  config.saveContentModel = undefined;
  config.builtResult = { model: BUILT_MODEL, warnings: [] };
});

async function waitForEditor(): Promise<void> {
  await waitFor(() => expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument());
  await waitFor(() => expect(editorProps.current.canSave).toBe(true));
}

async function clickSave(): Promise<void> {
  await act(async () => {
    editorProps.current.onSave?.();
    await Promise.resolve();
    await Promise.resolve();
    await Promise.resolve();
  });
}

describe('ComposeWorkspace — imported save-routing flip (task 012)', () => {
  it('posts the MODEL shape when the Load carried contentModel: contentModel + content, NO op-log/paraIdMap/comments/baselineVersionId', async () => {
    config.loadContentModel = LOADED_MODEL;
    renderStoredDoc();
    await waitForEditor();

    await clickSave();
    await waitFor(() => expect(saveRequests).toHaveLength(1));

    const body = saveRequests[0].body;
    expect(saveRequests[0].url).toContain(`/api/compose/documents/${SPE_ID}/save`);
    expect(body.contentModel).toEqual(BUILT_MODEL);
    expect(body.content).toBe(CONTENT_B64); // retained bytes as the render carrier
    expect(body.operationLog).toBeUndefined();
    expect(body.paraIdMap).toBeUndefined();
    expect(body.comments).toBeUndefined(); // folded into the model by the editor mapper
    expect(body.baselineVersionId).toBeUndefined(); // content present → no version fallback needed
    expect(body.documentRecordId).toBe('sprk-doc-1');

    // The mapper received the LOADED model + the BINDING trackChanges default (null origin → true).
    expect(buildImportedContentModelMock).toHaveBeenCalledTimes(1);
    expect(buildImportedContentModelMock).toHaveBeenCalledWith(LOADED_MODEL, { trackChanges: true });
    // The op-log high-water mark was recorded BEFORE the mapper ran (commitSaved drops the batch).
    expect(serializeOperationLogMock.mock.invocationCallOrder[0]).toBeLessThan(
      buildImportedContentModelMock.mock.invocationCallOrder[0]
    );
    // Post-200: baseline recapture + exactly ONE commit.
    await waitFor(() => expect(commitSavedMock).toHaveBeenCalledTimes(1));
    expect(recaptureBaselineSnapshotMock).toHaveBeenCalledTimes(1);
  });

  it("a durable 'authored' origin flips trackChanges to false", async () => {
    config.loadContentModel = LOADED_MODEL;
    config.loadOrigin = 'authored';
    renderStoredDoc();
    await waitForEditor();

    await clickSave();
    await waitFor(() => expect(saveRequests).toHaveLength(1));
    expect(buildImportedContentModelMock).toHaveBeenCalledWith(LOADED_MODEL, { trackChanges: false });
  });

  it('merges server + mapper degradation warnings into the save-degradation banner (despite hideImportWarnings), and a clean save clears it', async () => {
    config.loadContentModel = LOADED_MODEL;
    config.builtResult = {
      model: BUILT_MODEL,
      warnings: [
        { code: 'text-box-flattened', count: 2 },
        { code: 'comment-anchor-dropped', count: 1 },
      ],
    };
    config.saveDegradationWarnings = [{ code: 'text-box-flattened', count: 1 }];
    renderStoredDoc();
    await waitForEditor();

    await clickSave();
    await waitFor(() => expect(saveRequests).toHaveLength(1));

    // The SEPARATE save-warning banner renders (the workspace passes hideImportWarnings, which
    // suppresses only the load-time import banner — the 026-F5 bug was save warnings dying there).
    const banner = await screen.findByTestId('compose-workspace-save-degradation-banner');
    // Duplicate codes merged with summed counts (2 mapper + 1 server = ×3) + friendly copy.
    expect(banner.textContent).toContain('A text box was converted to regular text. (×3)');
    expect(banner.textContent).toContain("A comment's anchor could not be placed; the comment text was kept.");
    expect(screen.queryByTestId('compose-workspace-import-warning-banner')).not.toBeInTheDocument();

    // A clean second save (no server warnings, no mapper warnings) CLEARS the stale banner.
    config.builtResult = { model: BUILT_MODEL, warnings: [] };
    config.saveDegradationWarnings = undefined;
    await clickSave();
    await waitFor(() => expect(saveRequests).toHaveLength(2));
    await waitFor(() =>
      expect(screen.queryByTestId('compose-workspace-save-degradation-banner')).not.toBeInTheDocument()
    );
  });

  it('falls back to the op-log shape when the mapper returns null (editor unavailable)', async () => {
    config.loadContentModel = LOADED_MODEL;
    config.builtResult = null;
    renderStoredDoc();
    await waitForEditor();

    await clickSave();
    await waitFor(() => expect(saveRequests).toHaveLength(1));

    const body = saveRequests[0].body;
    expect(body.contentModel).toBeUndefined();
    expect((body.operationLog as { operations: unknown[] }).operations).toEqual([CAPTURED_OP]);
    expect(body.comments).toEqual([STUB_COMMENT]);
    expect(body.paraIdMap).toEqual([{ paraId: 'AAAA0001', text: 'Loaded clause.' }]);
    expect(body.baselineVersionId).toBe('v-load');
    expect(body.content).toBe(CONTENT_B64);
    // Op-log path commit; no baseline recapture (that is a model-path concern).
    await waitFor(() => expect(commitSavedMock).toHaveBeenCalledTimes(1));
    expect(recaptureBaselineSnapshotMock).not.toHaveBeenCalled();
  });

  it('keeps the op-log shape untouched when the Load carried NO contentModel (legacy session / older BFF)', async () => {
    config.loadContentModel = undefined;
    renderStoredDoc();
    await waitForEditor();

    await clickSave();
    await waitFor(() => expect(saveRequests).toHaveLength(1));

    expect(buildImportedContentModelMock).not.toHaveBeenCalled();
    const body = saveRequests[0].body;
    expect(body.contentModel).toBeUndefined();
    expect((body.operationLog as { operations: unknown[] }).operations).toEqual([CAPTURED_OP]);
    expect(body.comments).toEqual([STUB_COMMENT]);
    expect(body.baselineVersionId).toBe('v-load');
  });

  it('an assistant-upload mount whose upload response carried contentModel create-on-saves via the MODEL shape', async () => {
    config.loadContentModel = LOADED_MODEL;
    renderUploadMount();
    await waitForEditor();

    await clickSave();
    await waitFor(() => expect(saveRequests).toHaveLength(1));

    const body = saveRequests[0].body;
    expect(saveRequests[0].url).toContain('/api/compose/documents/create-on-save');
    expect(body.contentModel).toEqual(BUILT_MODEL);
    expect(body.content).toBe(CONTENT_B64);
    expect(body.containerId).toBe('bu-container-1');
    expect(typeof body.transientKey).toBe('string');
    expect(body.forkNew).toBe(false);
    expect(body.operationLog).toBeUndefined();
    expect(body.paraIdMap).toBeUndefined();
    expect(body.comments).toBeUndefined();
    expect(body.baselineVersionId).toBeUndefined();
  });
});

describe('ComposeWorkspace — born-in-editor branches unchanged except the comments amendment (task 012)', () => {
  it('create-on-save still posts contentModel-only (no baselineVersionId, no op-log) and now NO separate comments field', async () => {
    renderBornInEditor();
    await waitForEditor();

    await clickSave();
    await waitFor(() => expect(saveRequests).toHaveLength(1));

    const body = saveRequests[0].body;
    expect(saveRequests[0].url).toContain('/api/compose/documents/create-on-save');
    expect(body.contentModel).toEqual(BORN_MODEL);
    // Amendment: buildContentModel folds threads into the model — the separate field is GONE even
    // though the editor handle reports a non-empty anchored-comment set.
    expect(body.comments).toBeUndefined();
    expect(body.operationLog).toBeUndefined();
    expect(body.baselineVersionId).toBeUndefined();
    expect(body.content).toBeUndefined();
    // The imported-model mapper is NEVER consulted for a born-in-editor doc.
    expect(buildImportedContentModelMock).not.toHaveBeenCalled();

    // Second save → replace route, still contentModel-only, still no comments/baseline.
    await clickSave();
    await waitFor(() => expect(saveRequests).toHaveLength(2));
    expect(saveRequests[1].url).toContain('/api/compose/documents/spe-created-1/save');
    expect(saveRequests[1].body.contentModel).toEqual(BORN_MODEL);
    expect(saveRequests[1].body.comments).toBeUndefined();
    expect(saveRequests[1].body.operationLog).toBeUndefined();
    expect(saveRequests[1].body.baselineVersionId).toBeUndefined();
  });
});
