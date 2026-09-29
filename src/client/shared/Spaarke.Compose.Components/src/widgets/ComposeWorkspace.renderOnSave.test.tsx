/**
 * ComposeWorkspace.renderOnSave.test.tsx — spaarkeai-compose-r6 task 012 (save-routing flip).
 *
 * Drives the REAL `ComposeWorkspace.triggerSave` with a stubbed `ComposeEditor` handle and the
 * `@spaarke/auth` fetch boundary mocked, and asserts the render-on-save cutover:
 *   1. MODEL PATH — a DIRTY loaded/imported doc whose Load response carried `contentModel` saves by
 *      posting `{ contentModel: built.model, content }` with NO operationLog / paraIdMap / comments /
 *      baselineVersionId; `buildImportedContentModel` receives the LOADED model + the BINDING
 *      trackChanges default (null origin → imported → true); `adoptBaselineSnapshot(built.snapshot)`
 *      (sibling F4 — the BUILD-TIME snapshot, never a live-doc recapture that would mask mid-flight
 *      edits) + `commitSaved` fire exactly once after the 200, with `recaptureBaselineSnapshot` as
 *      the older-editor-build fallback only.
 *   1b. CLEAN GATE (review F3, CRITICAL) — a CLEAN imported save (editor not dirty) keeps the
 *      pre-012 byte-identical content-only passthrough (FR-06a): no contentModel, mapper not called.
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
 * 7. NAME GATE (UAT-03, r8 task 018) — every create-on-save of a never-persisted document (born-in-
 *    editor AND assistant-upload) is gated by the first-save name modal: `requestSave` posts NOTHING
 *    until it is confirmed, and the confirmed name threads into `displayName`. Added when this suite's
 *    two create-on-save tests were found red on HEAD — commit `cdb1dbcb4` (2026-08-18) widened
 *    `saveNeedsName` to every never-persisted doc and did not update them. A second save on the
 *    now-persisted doc must NOT re-prompt.
 *
 * ADR-038: a client behavior test at the save-orchestration seam; mocks the network at the
 * `@spaarke/auth` boundary + stubs a heavy child — NOT a banned Mock<HttpMessageHandler>/DI test.
 */

import * as React from 'react';
import { fireEvent, render, screen, waitFor, act } from '@testing-library/react';
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

// Sibling F4: the BUILD-TIME {paraId → rejectText} snapshot the posted model was derived from —
// the workspace must round-trip it VERBATIM into adoptBaselineSnapshot on save-200 (never a
// live-doc recapture, which would mask edits typed during the in-flight save).
const BUILT_SNAPSHOT = { AAAA0001: 'Loaded clause.' };

// ── Mutable per-test config the fetch mock + editor stub read ──
const config: {
  loadContentModel: unknown; // undefined = older BFF (field omitted)
  /** task 013 (review F7): the mount door's canonical-model projection flatten warnings. */
  loadContentModelWarnings: Array<{ code: string; count: number }> | undefined;
  loadOrigin: 'authored' | 'imported' | undefined;
  saveDegradationWarnings: Array<{ code: string; count: number }> | undefined;
  saveContentModel: unknown;
  builtResult: { model: unknown; warnings: Array<{ code: string; count: number }>; snapshot?: unknown } | null;
  /** Review F3: whether the stubbed editor reports unsaved changes (drives the model-path dirty gate). */
  editorDirty: boolean;
  /** Sibling F4: expose adoptBaselineSnapshot on the handle (false = older editor build → the
   * workspace must fall back to recaptureBaselineSnapshot). */
  exposeAdoptBaseline: boolean;
  /** Task 042 (FR-06): the Load response's sourceFormat marker ('pdf' = PDF-sourced mount). */
  loadSourceFormat: string | undefined;
  /** Task 042: the Load response's fileName (drives the .pdf → .docx displayName swap). */
  loadFileName: string;
  /** Task 042 (042-review MED-3): fail the NEXT create-on-save with a 500 (retry-flow coverage). */
  failNextCreateOnSave: boolean;
} = {
  loadContentModel: undefined,
  loadContentModelWarnings: undefined,
  loadOrigin: undefined,
  saveDegradationWarnings: undefined,
  saveContentModel: undefined,
  builtResult: { model: BUILT_MODEL, warnings: [], snapshot: BUILT_SNAPSHOT },
  editorDirty: true,
  exposeAdoptBaseline: true,
  loadSourceFormat: undefined,
  loadFileName: 'contract.docx',
  failNextCreateOnSave: false,
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
    if (config.failNextCreateOnSave) {
      config.failNextCreateOnSave = false; // one-shot: the retry succeeds
      // FR-S01 (r8 task 010): a failure is THROWN, never resolved as a non-ok Response —
      // `authenticatedFetch` returns only when `response.ok` (see Spaarke.Auth/src/authenticatedFetch.ts).
      // Thrown lazily via requireMock so the class is the same one ComposeWorkspace imports.
      const { ApiError } = jest.requireMock('@spaarke/auth') as {
        ApiError: new (message: string, status: number) => Error;
      };
      throw new ApiError('transient create failure', 500);
    }
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
        contentModelWarnings: config.loadContentModelWarnings,
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
        // Task 042: a PDF-sourced load suppresses the version id (server MEDIUM-3 contract).
        versionId: config.loadSourceFormat === 'pdf' ? null : 'v-load',
        fileName: config.loadFileName,
        size: 500,
        anchoredAnnotations: [],
        definedTermsTracking: [],
        actionHistory: [],
        projection: { status: 'success', canEdit: true, html: '<p data-paraid="AAAA0001">Loaded clause.</p>' },
        contentModel: config.loadContentModel,
        contentModelWarnings: config.loadContentModelWarnings,
        origin: config.loadOrigin,
        sourceFormat: config.loadSourceFormat,
      }),
    } as unknown as Response;
  }
  // Session-ledger compose-outputs probe + everything else → benign 404, expressed the way
  // `authenticatedFetch` actually expresses one (a thrown ApiError, not a resolved non-ok Response —
  // FR-S01, r8 task 010). Verified behaviour-neutral for this suite.
  const { ApiError } = jest.requireMock('@spaarke/auth') as {
    ApiError: new (message: string, status: number) => Error;
  };
  throw new ApiError('Not found', 404);
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
}));

jest.mock('@spaarke/ui-components', () => ({
  // r8 task 052 (FR-C05) — ComposeWorkspace also mounts <ConfirmModal/> unconditionally for the
  // stale-target "apply anyway?" question (controlled via its own `open` prop, same pattern as
  // SprkModal/SendEmailDialog above). A no-op stub keeps this mock complete.
  ConfirmModal: () => null,
  createXrmNavigationService: () => ({ openLookup: jest.fn() }),
  createXrmDataService: () => ({ retrieveRecord: jest.fn() }),
  SendEmailDialog: () => null,
  SprkModal: () => null,
  RichFilePreviewDialog: () => null,
  // FR-02 (task 030): ComposeWorkspace now mounts <ComposeSaveNameDialog/> (a FormModal preset) for
  // the first create-on-save of an unnamed draft AND every Save As. Behavioral stub mirroring the
  // preset contract (open gate, children, submit gated by submitDisabled/busy) so the fork test can
  // drive the name modal to completion. Closed → renders nothing (the dialog itself returns <></>).
  FormModal: (props: {
    open: boolean;
    onClose: () => void;
    onSubmit: () => void;
    title?: string;
    submitLabel?: string;
    submitDisabled?: boolean;
    busy?: boolean;
    children?: React.ReactNode;
  }) =>
    props.open ? (
      <div role="dialog" aria-label={props.title} data-testid="mock-form-modal">
        {props.children}
        <button
          onClick={props.onSubmit}
          disabled={props.busy || props.submitDisabled}
          data-testid="mock-form-modal-submit"
        >
          {props.submitLabel ?? 'Save'}
        </button>
      </div>
    ) : null,
}));
jest.mock('@spaarke/document-operations', () => ({
  useDocumentActions: () => ({ openInWeb: jest.fn(), openInDesktop: jest.fn(), isActing: false }),
}));
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

// ── ComposeEditor stub — imported handle with the task-012 model-mapper members. Dirty state and
//    adoptBaselineSnapshot exposure are config-driven (F3 clean-gate + F4 fallback coverage). ──
const commitSavedMock = jest.fn();
const recaptureBaselineSnapshotMock = jest.fn();
const adoptBaselineSnapshotMock = jest.fn();
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
          props.onDirtyChange?.(config.editorDirty);
        }, []);
        ReactLib.useImperativeHandle(ref, () => ({
          // Read live from config so a clean-save test drives the F3 gate through the editor's OWN
          // authoritative dirty flag (the one triggerSave reads), not just the toolbar state.
          isDirty: () => config.editorDirty,
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
          // Sibling F4: conditionally exposed so one test exercises the older-editor-build fallback
          // (workspace must degrade to recaptureBaselineSnapshot when this member is absent).
          ...(config.exposeAdoptBaseline ? { adoptBaselineSnapshot: adoptBaselineSnapshotMock } : {}),
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
  adoptBaselineSnapshotMock.mockClear();
  serializeOperationLogMock.mockClear();
  buildImportedContentModelMock.mockClear();
  saveRequests.length = 0;
  editorProps.current = {};
  config.loadContentModel = undefined;
  config.loadContentModelWarnings = undefined;
  config.loadOrigin = undefined;
  config.saveDegradationWarnings = undefined;
  config.saveContentModel = undefined;
  config.builtResult = { model: BUILT_MODEL, warnings: [], snapshot: BUILT_SNAPSHOT };
  config.editorDirty = true;
  config.exposeAdoptBaseline = true;
  config.loadSourceFormat = undefined;
  config.loadFileName = 'contract.docx';
  config.failNextCreateOnSave = false;
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

/**
 * UAT-03 (owner 2026-08-18, commit cdb1dbcb4) — the name-modal gate on a FIRST create-on-save.
 *
 * `requestSave` (what the toolbar's onSave is wired to) opens `ComposeSaveNameDialog` and returns
 * WITHOUT posting whenever the document has never been persisted (no `speDriveItemId` AND no
 * `sprkDocumentId`) — that is every born-in-editor draft and every assistant-upload mount. The POST
 * only happens when the modal submits. The modal seeds its field with the document's current file
 * name, so `submitDisabled` is already false and confirming needs no typing.
 *
 * The PDF suite's forkNew test performs this same drive inline; this is that drive, named.
 */
async function confirmSaveName(): Promise<void> {
  const submit = await screen.findByTestId('mock-form-modal-submit');
  await act(async () => {
    fireEvent.click(submit);
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
    // Post-200 (sibling F4): the BUILD-TIME snapshot the posted model was derived from is adopted
    // verbatim — NEVER a live-doc recapture (which would mask edits typed mid-flight) — + exactly
    // ONE commit.
    await waitFor(() => expect(commitSavedMock).toHaveBeenCalledTimes(1));
    expect(adoptBaselineSnapshotMock).toHaveBeenCalledTimes(1);
    expect(adoptBaselineSnapshotMock).toHaveBeenCalledWith(BUILT_SNAPSHOT);
    expect(recaptureBaselineSnapshotMock).not.toHaveBeenCalled();
  });

  it('CLEAN imported save (review F3): keeps the byte-identical content-only passthrough — no contentModel, mapper never called', async () => {
    config.loadContentModel = LOADED_MODEL; // model IS retained…
    config.editorDirty = false; // …but the editor has NO unsaved changes (zero-edit Ctrl+S)
    renderStoredDoc();
    // canSave stays false for a clean stored doc — wait for the load/mount only, then drive
    // triggerSave directly via onSave (the Ctrl+S / bridge entry points do the same).
    await waitFor(() => expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument());

    await clickSave();
    await waitFor(() => expect(saveRequests).toHaveLength(1));

    const body = saveRequests[0].body;
    // Pre-012 FR-06a shape, unchanged: retained bytes byte-identical, NOTHING that would make the
    // server re-render the body from the flatten-tier model (which drops e.g. signature text-boxes).
    expect(body.contentModel).toBeUndefined();
    expect(body.content).toBe(CONTENT_B64);
    expect(body.operationLog).toBeUndefined(); // clean → no op-log
    expect(body.baselineVersionId).toBe('v-load');
    expect(body.comments).toEqual([STUB_COMMENT]);
    expect(body.paraIdMap).toEqual([{ paraId: 'AAAA0001', text: 'Loaded clause.' }]);
    expect(buildImportedContentModelMock).not.toHaveBeenCalled();
    // No model path → no baseline adoption; and no op-log sent → no commit either.
    expect(adoptBaselineSnapshotMock).not.toHaveBeenCalled();
    expect(recaptureBaselineSnapshotMock).not.toHaveBeenCalled();
    expect(commitSavedMock).not.toHaveBeenCalled();
  });

  it('falls back to recaptureBaselineSnapshot when the editor build lacks adoptBaselineSnapshot (older handle)', async () => {
    config.loadContentModel = LOADED_MODEL;
    config.exposeAdoptBaseline = false;
    renderStoredDoc();
    await waitForEditor();

    await clickSave();
    await waitFor(() => expect(saveRequests).toHaveLength(1));
    expect(saveRequests[0].body.contentModel).toEqual(BUILT_MODEL); // model path still taken

    await waitFor(() => expect(commitSavedMock).toHaveBeenCalledTimes(1));
    expect(adoptBaselineSnapshotMock).not.toHaveBeenCalled();
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
    // UAT round 1 #2: the copy moved behind the collapsed row's popover — open it, then read the notice.
    fireEvent.click(await screen.findByTestId('compose-workspace-formatting-notices-open'));
    const banner = await screen.findByTestId('compose-workspace-save-degradation-notice');
    // Duplicate codes merged with summed counts (2 mapper + 1 server = ×3) + friendly copy.
    expect(banner.textContent).toContain('A text box was converted to regular text. (×3)');
    expect(banner.textContent).toContain("A comment's anchor could not be placed; the comment text was kept.");
    // hideImportWarnings suppresses the IMPORT family only; the save family must survive it (026-F5).
    // With both collapsed behind one row, that is now asserted on the notice inside the popover rather
    // than on a second top-level banner.
    expect(screen.queryByTestId('compose-workspace-import-warning-notice')).not.toBeInTheDocument();

    // A clean second save (no server warnings, no mapper warnings) CLEARS the stale banner.
    config.builtResult = { model: BUILT_MODEL, warnings: [] };
    config.saveDegradationWarnings = undefined;
    await clickSave();
    await waitFor(() => expect(saveRequests).toHaveLength(2));
    await waitFor(() => expect(screen.queryByTestId('compose-workspace-formatting-notices')).not.toBeInTheDocument());
  });

  // Task 044 (r8) SUPERSEDES task 013's F7 fold. The old test asserted that a model-path save folds the
  // mount-time projection flatten warnings into the save banner, on the reasoning that "the loss they
  // describe materializes on the first save that renders from the flatten-tier model". That was true when
  // every save rebuilt the whole body. Since task 040 the merge CLONES untouched blocks verbatim, so a text
  // box on a block the user did not touch loses nothing — and the banner was reporting a loss that did not
  // happen. The server now reports what was ACTUALLY lost, per re-rendered block, so the fold is both
  // unnecessary and false. The `pdf-intake-*` exception (a reflow that really did happen at LOAD) is
  // asserted below and is unchanged.
  it('task 044: a model-path save does NOT fold the mount-time docx flatten warnings — the merge clones those blocks, so nothing was simplified', async () => {
    config.loadContentModel = LOADED_MODEL;
    // The mount door surfaced projection flatten warnings. Under the merge these describe blocks that are
    // now cloned verbatim on save.
    config.loadContentModelWarnings = [
      { code: 'text-box-flattened', count: 2 },
      { code: 'complex-object-dropped', count: 1 },
    ];
    config.builtResult = { model: BUILT_MODEL, warnings: [] };
    config.saveDegradationWarnings = undefined;
    renderStoredDoc();
    await waitForEditor();

    await clickSave();
    await waitFor(() => expect(saveRequests).toHaveLength(1));

    // No banner at all: nothing the server reported, nothing the mapper reported, and the held flatten
    // warnings are no longer folded. A false "Some formatting was simplified when saving" here is exactly
    // what trains a reader to ignore the true ones.
    await waitFor(() => expect(screen.queryByTestId('compose-workspace-formatting-notices')).not.toBeInTheDocument());
  });

  it('task 044: the SERVER remains authoritative — a real loss it reports still reaches the banner', async () => {
    config.loadContentModel = LOADED_MODEL;
    config.loadContentModelWarnings = [{ code: 'text-box-flattened', count: 2 }];
    config.builtResult = { model: BUILT_MODEL, warnings: [] };
    // The merge's shortfall report: the edited block genuinely lost soft breaks.
    config.saveDegradationWarnings = [{ code: 'edited-paragraph-line-break-dropped', count: 2 }];
    renderStoredDoc();
    await waitForEditor();

    await clickSave();
    await waitFor(() => expect(saveRequests).toHaveLength(1));

    // UAT round 1 #2: the copy moved behind the collapsed row's popover — open it, then read the notice.
    fireEvent.click(await screen.findByTestId('compose-workspace-formatting-notices-open'));
    const banner = await screen.findByTestId('compose-workspace-save-degradation-notice');
    // The server's real finding is shown...
    expect(banner.textContent).toContain('A line break inside an edited paragraph was dropped.');
    // ...and the stale mount-time flatten warning is NOT, because that block was cloned.
    expect(banner.textContent).not.toContain('A text box was converted to regular text.');
  });

  it('task 044: pdf-intake facts are STILL folded — that reflow happened at LOAD, before any save', async () => {
    config.loadContentModel = LOADED_MODEL;
    config.loadContentModelWarnings = [
      { code: 'pdf-intake-fixed-layout-reflowed', count: 1 },
      { code: 'text-box-flattened', count: 2 },
    ];
    config.builtResult = { model: BUILT_MODEL, warnings: [] };
    config.saveDegradationWarnings = undefined;
    renderStoredDoc();
    await waitForEditor();

    await clickSave();
    await waitFor(() => expect(saveRequests).toHaveLength(1));

    // UAT round 1 #2: the copy moved behind the collapsed row's popover — open it, then read the notice.
    fireEvent.click(await screen.findByTestId('compose-workspace-formatting-notices-open'));
    const banner = await screen.findByTestId('compose-workspace-save-degradation-notice');
    // The PDF intake already reflowed the source into the synthesized carrier — that loss is real
    // whatever the save does, so it must still surface.
    expect(banner.textContent.length).toBeGreaterThan(0);
    // The docx flatten warning alongside it is still not folded.
    expect(banner.textContent).not.toContain('A text box was converted to regular text.');
  });

  it('task 013 (F7): an OP-LOG-path save does NOT fold the projection flatten warnings (the loss does not materialize on the byte-identical path)', async () => {
    config.loadContentModel = LOADED_MODEL;
    config.loadContentModelWarnings = [{ code: 'text-box-flattened', count: 2 }];
    // Mapper unavailable → op-log fallback shape (byte-identical baseline + ops; nothing flattened).
    config.builtResult = null;
    renderStoredDoc();
    await waitForEditor();

    await clickSave();
    await waitFor(() => expect(saveRequests).toHaveLength(1));
    expect(saveRequests[0].body.contentModel).toBeUndefined(); // op-log shape confirmed

    // No banner: the retained flatten warnings were NOT folded (and stay retained for a later
    // model-path save, per the reducer lifecycle guarded in the reducer suite).
    expect(screen.queryByTestId('compose-workspace-formatting-notices')).not.toBeInTheDocument();
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
    // UAT-03: an uploaded file has no sprk_document row yet, so this first save is name-gated —
    // nothing is posted until the modal is confirmed.
    expect(screen.getByTestId('compose-save-name-dialog')).toBeInTheDocument();
    expect(saveRequests).toHaveLength(0);
    await confirmSaveName();
    await waitFor(() => expect(saveRequests).toHaveLength(1));

    const body = saveRequests[0].body;
    expect(saveRequests[0].url).toContain('/api/compose/documents/create-on-save');
    // The confirmed name threads into displayName (→ server ResolveFileName + sprk_documentname).
    expect(body.displayName).toBe('uploaded.docx');
    expect(body.contentModel).toEqual(BUILT_MODEL);
    expect(body.content).toBe(CONTENT_B64);
    // #858 (2026-09-01): the create-on-save body MUST NOT carry a container. The server derives it
    // from the session-bound matter (authorized first) or the acting user's business unit, and
    // `SaveComposeDocumentRequest.ContainerId` no longer exists. This assertion is INVERTED from what
    // it was — it used to pin `'bu-container-1'`, i.e. it pinned the defect. The host still passes a
    // `containerId` PROP (it feeds client state); what must not happen is it reaching the wire.
    expect(body.containerId).toBeUndefined();
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
    // UAT-03: a born-in-editor draft is never-persisted → the first save is name-gated.
    expect(screen.getByTestId('compose-save-name-dialog')).toBeInTheDocument();
    expect(saveRequests).toHaveLength(0);
    await confirmSaveName();
    await waitFor(() => expect(saveRequests).toHaveLength(1));

    const body = saveRequests[0].body;
    expect(saveRequests[0].url).toContain('/api/compose/documents/create-on-save');
    expect(body.displayName).toBe('draft.docx');
    expect(body.contentModel).toEqual(BORN_MODEL);
    // Amendment: buildContentModel folds threads into the model — the separate field is GONE even
    // though the editor handle reports a non-empty anchored-comment set.
    expect(body.comments).toBeUndefined();
    expect(body.operationLog).toBeUndefined();
    expect(body.baselineVersionId).toBeUndefined();
    expect(body.content).toBeUndefined();
    // The imported-model mapper is NEVER consulted for a born-in-editor doc.
    expect(buildImportedContentModelMock).not.toHaveBeenCalled();

    // Second save → replace route, still contentModel-only, still no comments/baseline. The doc now
    // carries an SPE id, so `saveNeedsName` is false and the modal does NOT re-open (UAT-03 gates the
    // FIRST save only — a name prompt on every save would be the regression).
    await clickSave();
    expect(screen.queryByTestId('compose-save-name-dialog')).not.toBeInTheDocument();
    await waitFor(() => expect(saveRequests).toHaveLength(2));
    expect(saveRequests[1].url).toContain('/api/compose/documents/spe-created-1/save');
    expect(saveRequests[1].body.contentModel).toEqual(BORN_MODEL);
    expect(saveRequests[1].body.comments).toBeUndefined();
    expect(saveRequests[1].body.operationLog).toBeUndefined();
    expect(saveRequests[1].body.baselineVersionId).toBeUndefined();
  });
});

// ---------------------------------------------------------------------------
// Task 042 (FR-06, PDF intake) — the PDF-sourced save ROUTING (041-review binding test plan):
// every save while sourceFormat==='pdf' takes the create-on-save route (a NEW Word document; the
// .pdf item is NEVER the replace target), with the .docx-swapped displayName, the load-minted
// transient dedup key, and the B-MED-3 sourceDocumentRecordId; after the first success the doc
// re-targets and the SECOND save is a normal replace onto the NEW item.
// ---------------------------------------------------------------------------

function renderStoredPdf() {
  return render(
    <FluentProvider theme={webLightTheme}>
      <ComposeWorkspace
        initialDocumentRef={{ speDriveItemId: SPE_ID, sprkDocumentId: 'sprk-doc-1', fileName: 'Corteva NDA.pdf' }}
        initialSessionId={DOC_SESSION}
        bffBaseUrl="https://bff.example.test"
        driveId={DRIVE_ID}
        tenantId="tenant-1"
        containerId="bu-container-1"
      />
    </FluentProvider>
  );
}

describe('ComposeWorkspace — PDF-sourced save routing (task 042 / FR-06)', () => {
  beforeEach(() => {
    config.loadSourceFormat = 'pdf';
    config.loadFileName = 'Corteva NDA.pdf';
    config.loadContentModel = LOADED_MODEL;
  });

  it('a dirty PDF-sourced save posts CREATE-ON-SAVE (never the replace route) with the .docx name, the dedup key, and the source record id', async () => {
    renderStoredPdf();
    await waitForEditor();

    // 042-review LOW-1: the end-to-end lossiness-UX wire — the workspace RENDERS the "Opened from
    // PDF" banner from the load payload's sourceFormat (asserted BEFORE the save, which clears the
    // marker and deliberately retires the banner).
    expect(screen.getByTestId('compose-workspace-pdf-source-banner')).toBeInTheDocument();

    await clickSave();
    await waitFor(() => expect(saveRequests).toHaveLength(1));

    const { url, body } = saveRequests[0];
    // The route: a NEW Word document — the .pdf item is never the replace target.
    expect(url).toContain('/api/compose/documents/create-on-save');
    expect(url).not.toContain(`/documents/${SPE_ID}/save`);
    // The 041 contract: .docx-swapped display name, load-minted dedup key, B-MED-3 inheritance id.
    expect(body.displayName).toBe('Corteva NDA.docx');
    expect(typeof body.transientKey).toBe('string');
    expect((body.transientKey as string).length).toBeGreaterThan(0);
    expect(body.sourceDocumentRecordId).toBe('sprk-doc-1');
    expect(body.forkNew).toBe(false);
    // Model shape (dirty imported create): merged model + retained synthesized bytes as carrier.
    expect(body.contentModel).toEqual(BUILT_MODEL);
    expect(body.content).toBe(CONTENT_B64);
    // #858 (2026-09-01): the create-on-save body MUST NOT carry a container. The server derives it
    // from the session-bound matter (authorized first) or the acting user's business unit, and
    // `SaveComposeDocumentRequest.ContainerId` no longer exists. This assertion is INVERTED from what
    // it was — it used to pin `'bu-container-1'`, i.e. it pinned the defect. The host still passes a
    // `containerId` PROP (it feeds client state); what must not happen is it reaching the wire.
    expect(body.containerId).toBeUndefined();

    // …and after the successful save the banner RETIRES (the doc is a native docx now).
    expect(screen.queryByTestId('compose-workspace-pdf-source-banner')).not.toBeInTheDocument();
  });

  it('a FAILED create retries with the SAME transient key — one PDF never dedups into two Word documents (042-review MED-3)', async () => {
    config.failNextCreateOnSave = true;
    renderStoredPdf();
    await waitForEditor();

    await clickSave(); // fails (500) — sourceFormat + key retained by the reducer
    await waitFor(() => expect(saveRequests).toHaveLength(1));
    await clickSave(); // retry — succeeds
    await waitFor(() => expect(saveRequests).toHaveLength(2));

    expect(saveRequests[0].url).toContain('/create-on-save');
    expect(saveRequests[1].url).toContain('/create-on-save');
    // The G7 dedup contract: the retry POSTs the SAME load-minted key, so the server's
    // transient-key alt-key resolves ONE record — never a duplicate Word document.
    expect(saveRequests[1].body.transientKey).toBe(saveRequests[0].body.transientKey);
  });

  it("'Save New Document' (forkNew) on a PDF doc mints a FRESH key — a deliberate second document gets its own dedup identity", async () => {
    renderStoredPdf();
    await waitForEditor();

    await clickSave(); // the normal PDF create — uses the load-minted key
    await waitFor(() => expect(saveRequests).toHaveLength(1));
    const loadMintedKey = saveRequests[0].body.transientKey;

    // Drive the Save split-button's 'new' fork exactly as the real UI does — the consolidated
    // toolbar lives inside ComposeEditor, whose onSave prop threads the mode into requestSave.
    // FR-02 (task 030): Save As now opens the name modal FIRST — confirm the seeded name to run the
    // fork save (the fork identity contract below is unchanged: forkNew + a fresh transient key).
    await act(async () => {
      (editorProps.current.onSave as unknown as (mode?: 'version' | 'new') => void)?.('new');
      await Promise.resolve();
    });
    const forkSubmit = await screen.findByTestId('mock-form-modal-submit');
    await act(async () => {
      fireEvent.click(forkSubmit);
      await Promise.resolve();
      await Promise.resolve();
      await Promise.resolve();
    });
    await waitFor(() => expect(saveRequests).toHaveLength(2));

    expect(saveRequests[1].url).toContain('/create-on-save');
    expect(saveRequests[1].body.forkNew).toBe(true);
    expect(typeof saveRequests[1].body.transientKey).toBe('string');
    expect(saveRequests[1].body.transientKey).not.toBe(loadMintedKey);
  });

  it('a CLEAN PDF-sourced save still creates (Shape-3 byte passthrough) — never the replace route', async () => {
    config.editorDirty = false;
    renderStoredPdf();
    await waitFor(() => expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument());

    await clickSave();
    await waitFor(() => expect(saveRequests).toHaveLength(1));

    const { url, body } = saveRequests[0];
    expect(url).toContain('/api/compose/documents/create-on-save');
    expect(body.contentModel).toBeUndefined(); // clean → no model render, byte passthrough
    expect(body.content).toBe(CONTENT_B64);
    expect(body.displayName).toBe('Corteva NDA.docx');
    expect(body.sourceDocumentRecordId).toBe('sprk-doc-1');
  });

  it('after the first successful save the doc re-targets: the SECOND save replaces onto the NEW docx item (sourceFormat cleared)', async () => {
    renderStoredPdf();
    await waitForEditor();

    await clickSave();
    await waitFor(() => expect(saveRequests).toHaveLength(1));
    expect(saveRequests[0].url).toContain('/api/compose/documents/create-on-save');

    // The create response minted 'spe-created-1' — saveSucceeded re-targeted documentRef and
    // cleared sourceFormat, so the next save is a NORMAL replace on the NEW item.
    await clickSave();
    await waitFor(() => expect(saveRequests).toHaveLength(2));
    expect(saveRequests[1].url).toContain('/api/compose/documents/spe-created-1/save');
    expect(saveRequests[1].body.sourceDocumentRecordId).toBeUndefined();
  });
});
