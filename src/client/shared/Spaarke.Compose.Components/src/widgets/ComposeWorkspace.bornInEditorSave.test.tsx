/**
 * ComposeWorkspace.bornInEditorSave.test.tsx — spaarkeai-compose-r4 task 039 (BUG A, born-in-editor 2nd save).
 *
 * THE NO-ERRORS BAR: a BORN-IN-EDITOR document (blank page / AI-draft — `initialDraftRef.html`, NO retained
 * `.docx` bytes) saved fine the FIRST time (create-on-save sends the full `contentModel`), but its SECOND
 * in-session save took the op-log/replace path with NO valid baseline and the server rejected it with a hard
 * 400 ("Provide the retained-original 'content' bytes, or … 'baselineVersionId' …"). Each failed save also
 * risked minting a duplicate record.
 *
 * THE FIX (this test guards it): `triggerSave`'s replace branch now uses the SAME `!state.docxBytes`
 * discriminant as the create path — a born-in-editor doc RE-AUTHORS via `{ contentModel }` on EVERY
 * in-session save, never entering the baseline-less op-log path. So we assert:
 *   1. the FIRST save is create-on-save carrying `contentModel` (no op-log);
 *   2. after the server rebinds the minted SPE id, the SECOND save hits the REPLACE url ({id}/save) and
 *      STILL carries `contentModel` — and carries NO `operationLog` / `baselineVersionId` (the exact
 *      combination that produced the 400).
 *
 * A LOADED doc's op-log/tracked path is unchanged — that REQ-2 invariant is guarded by the sibling
 * `ComposeWorkspace.saveOpLogPreservation.test.tsx` (a loaded doc still POSTs `operationLog`).
 *
 * ADR-038: a client behavior test at the save-orchestration seam; mocks the network at the `@spaarke/auth`
 * boundary + stubs a heavy child — NOT a banned `Mock<HttpMessageHandler>`/DI-registration/ctor test.
 */

import * as React from 'react';
import { render, screen, waitFor, act } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

// Fluent's MessageBar (the saveFailed error banner) uses ResizeObserver, which jsdom lacks.
if (typeof (globalThis as { ResizeObserver?: unknown }).ResizeObserver === 'undefined') {
  (globalThis as { ResizeObserver?: unknown }).ResizeObserver = class {
    observe(): void {}
    unobserve(): void {}
    disconnect(): void {}
  };
}

const CREATED_SPE_ID = 'spe-born-in-editor-1';
const CREATED_DRIVE_ID = 'bu-container-drive-1';
/** The distinctive content model the stubbed editor "builds" — the client sends it verbatim. */
const CONTENT_MODEL = { blocks: [{ kind: 'paragraph', runs: [{ text: 'Second in-session save.' }] }] };

// ── Fetch boundary (ADR-028): create-on-save → 200 (mints the SPE id); replace /save → 200 ──
const saveRequests: Array<{ url: string; body: Record<string, unknown> }> = [];
const authenticatedFetchMock = jest.fn(async (url: string, init?: RequestInit): Promise<Response> => {
  // First Save of a born-in-editor draft: the transient create-on-save route.
  if (url.includes('/api/compose/documents/create-on-save')) {
    saveRequests.push({ url, body: JSON.parse(String(init?.body ?? '{}')) as Record<string, unknown> });
    return {
      ok: true,
      status: 200,
      json: async () => ({
        documentSpeId: CREATED_SPE_ID,
        documentRecordId: 'sprk-doc-born-1',
        driveId: CREATED_DRIVE_ID,
        // create-on-save returns the drive-ITEM id as VersionId (not a real SPE version) — the exact
        // condition that made the op-log/baseline replace path unusable for a born-in-editor 2nd save.
        versionId: CREATED_SPE_ID,
        eTag: 'etag-1',
        size: 500,
        wasPromotedThisSave: true,
      }),
    } as unknown as Response;
  }
  // Second (and later) Save: the replace route, now carrying the minted SPE id in the path.
  if (url.includes('/api/compose/documents/') && url.includes('/save')) {
    saveRequests.push({ url, body: JSON.parse(String(init?.body ?? '{}')) as Record<string, unknown> });
    return {
      ok: true,
      status: 200,
      json: async () => ({
        documentSpeId: CREATED_SPE_ID,
        documentRecordId: 'sprk-doc-born-1',
        driveId: CREATED_DRIVE_ID,
        versionId: 'v-second',
        eTag: 'etag-2',
        size: 520,
        wasPromotedThisSave: false,
      }),
    } as unknown as Response;
  }
  // Session-ledger compose-outputs probe + everything else → benign 404.
  return { ok: false, status: 404, json: async () => [], text: async () => '' } as unknown as Response;
});

jest.mock('@spaarke/auth', () => ({
  authenticatedFetch: (...args: unknown[]) => authenticatedFetchMock(...(args as [string, RequestInit?])),
  useAuth: () => ({
    isAuthenticated: true,
    getAccessToken: async () => 'test-token',
    authenticatedFetch: (...args: unknown[]) => authenticatedFetchMock(...(args as [string, RequestInit?])),
    tenantId: 'test-tenant',
    logout: jest.fn(),
  }),
}));

jest.mock('@spaarke/ui-components', () => ({
  createXrmNavigationService: () => ({ openLookup: jest.fn() }),
  createXrmDataService: () => ({ retrieveRecord: jest.fn() }),
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

// ── ComposeEditor stub — a born-in-editor handle: dirty, no retained bytes, buildContentModel returns the
//    distinctive CONTENT_MODEL the client forwards on both saves. ──
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
          serializeOperationLog: () => ({ orderedOps: [], baseVersion: null }),
          commitSaved: jest.fn(),
          getBaselineParaIdMap: () => [],
          getCommentThreadAnnotations: () => [],
          getRedlineAnnotations: () => [],
          hasPendingRedlines: () => false,
          buildContentModel: () => CONTENT_MODEL,
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

function renderWorkspace() {
  return render(
    <FluentProvider theme={webLightTheme}>
      <ComposeWorkspace
        // Born-in-editor: inline draft html, NO stored-doc / upload ref → mountDraftHtml (no docxBytes).
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
  authenticatedFetchMock.mockClear();
  saveRequests.length = 0;
  editorProps.current = {};
});

async function clickSave(): Promise<void> {
  await act(async () => {
    editorProps.current.onSave?.();
    await Promise.resolve();
    await Promise.resolve();
    await Promise.resolve();
  });
}

describe('ComposeWorkspace — born-in-editor 2nd save re-authors via contentModel (task 039 BUG A)', () => {
  it('first save is create-on-save with contentModel; second save hits replace and STILL sends contentModel (no op-log/baseline)', async () => {
    renderWorkspace();

    await waitFor(() => expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument());
    await waitFor(() => expect(editorProps.current.canSave).toBe(true));

    // ---- First save → create-on-save ----
    await clickSave();
    await waitFor(() => expect(saveRequests).toHaveLength(1));

    expect(saveRequests[0].url).toContain('/api/compose/documents/create-on-save');
    expect(saveRequests[0].body.contentModel).toEqual(CONTENT_MODEL);
    expect(saveRequests[0].body.operationLog).toBeUndefined();

    // ---- Second save → replace path (the minted SPE id is now in the url). The first save cleared the
    //      dirty flag; invoking onSave() directly re-runs triggerSave (the editor handle still reports
    //      dirty) so we exercise the born-in-editor REPLACE branch. ----
    await clickSave();
    await waitFor(() => expect(saveRequests).toHaveLength(2));

    // It UPDATES the same item (replace url carries the minted SPE id — never a second create-on-save).
    expect(saveRequests[1].url).toContain(`/api/compose/documents/${CREATED_SPE_ID}/save`);
    expect(saveRequests[1].url).not.toContain('/create-on-save');
    // It re-authors via contentModel — NOT the baseline-less op-log path that produced the 400.
    expect(saveRequests[1].body.contentModel).toEqual(CONTENT_MODEL);
    expect(saveRequests[1].body.operationLog).toBeUndefined();
    expect(saveRequests[1].body.baselineVersionId).toBeUndefined();
  });
});
