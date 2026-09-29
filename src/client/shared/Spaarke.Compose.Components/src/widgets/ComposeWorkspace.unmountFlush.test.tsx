/**
 * ComposeWorkspace.unmountFlush.test.tsx — spaarkeai-assistant-enhancements-r2 DI-02 regression guard.
 *
 * DI-02 (notes/defer-issues.md): every path that closes/removes a compose tab
 * (`WorkspaceTabManager.closeTab`, `clearAllTabs` on a History switch or an
 * exclusive-playbook reset) unmounts `ComposeWorkspace` with NO dirty-check.
 * Un-flushed keystrokes since the last SERVER save were silently dropped when the tab
 * unmounted.
 *
 * NOTE (spaarkeai-compose-r7 FR-03, tasks 040/041): the workspace now ALSO has a
 * CLIENT-ONLY draft autosave (a ~15s dirty-only localStorage snapshot) — the prior
 * "no autosave anywhere" invariant was deliberately reversed (spec ADR-Tensions path
 * A). That does NOT change this test: the client draft path never calls `triggerSave`
 * and never POSTs, so the SERVER-save flush-on-unmount is STILL the only path that
 * POSTs without an explicit Ctrl+S / toolbar-Save / bridge save. These assertions
 * (dirty unmount → exactly one `/save` POST; clean unmount → zero) remain the DI-02
 * contract, unchanged.
 *
 * The fix (see `hasUnsavedWorkRef` + the unmount effect in `ComposeWorkspace.tsx`,
 * just after `handleDirtyChange`) best-effort flushes through the SAME
 * `triggerSave` path a manual Save uses when the component unmounts while dirty.
 *
 * This test drives the REAL `ComposeWorkspace` (same stubbed-`ComposeEditor`-handle
 * harness as `ComposeWorkspace.saveOpLogPreservation.test.tsx`) and asserts:
 *   1. a DIRTY workspace flushes a `/save` POST carrying the captured op-log on
 *      `unmount()` — no explicit Save/Ctrl+S was ever fired,
 *   2. a CLEAN (never-edited) workspace does NOT fire a spurious save on unmount.
 *
 * ADR-038: a client behavior test at the save-orchestration seam; mocks the
 * network at the `@spaarke/auth` boundary + stubs a heavy child — NOT a banned
 * `Mock<HttpMessageHandler>`/DI-registration/ctor test.
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

const SPE_ID = 'drive-item-doc-1';
const DRIVE_ID = 'drive-1';
const DOC_SESSION = '00000000-0000-0000-0000-0000000d0c07';
// A minimal valid base64 of the ZIP magic `PK\x03\x04` — the workspace only base64-decodes it into the
// `docxBytes` prop, which the STUB editor ignores (mammoth parsing lives inside the real editor).
const CONTENT_B64 = 'UEsDBA==';

/** The fixed op the stubbed editor "captured" this dirty session — a single interior text insert. */
const CAPTURED_OP = { type: 'insertText', paraId: 'AAAA0001', at: { runIndex: 0, offset: 3 }, text: 'x' };

// ── Fetch boundary (ADR-028): Load → stored doc; /save → 200 ──
let saveCallCount = 0;
const saveRequestBodies: Array<Record<string, unknown>> = [];
const authenticatedFetchMock = jest.fn(async (url: string, init?: RequestInit): Promise<Response> => {
  if (url.includes('/api/compose/documents/') && url.includes('/save')) {
    saveCallCount += 1;
    saveRequestBodies.push(JSON.parse(String(init?.body ?? '{}')) as Record<string, unknown>);
    return {
      ok: true,
      status: 200,
      json: async () => ({
        documentSpeId: SPE_ID,
        documentRecordId: 'sprk-doc-1',
        driveId: DRIVE_ID,
        versionId: 'v-after-save',
        eTag: 'etag-2',
        size: 500,
        wasPromotedThisSave: false,
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
  // r8 task 052 (FR-C05) — ComposeWorkspace also mounts <ConfirmModal/> unconditionally for the
  // stale-target "apply anyway?" question (controlled via its own `open` prop, same pattern as
  // SprkModal/SendEmailDialog above). A no-op stub keeps this mock complete.
  ConfirmModal: () => null,
  createXrmNavigationService: () => ({ openLookup: jest.fn() }),
  createXrmDataService: () => ({ retrieveRecord: jest.fn() }),
  SendEmailDialog: () => null,
  SprkModal: () => null,
  // "Open Document" preview modal — mounted unconditionally once the doc is promoted
  // (canPreviewDocument). This test's fixture doc IS promoted (sprkDocumentId set), so
  // the stub must exist or the component errors with "Element type is invalid".
  RichFilePreviewDialog: () => null,
}));
jest.mock('@spaarke/document-operations', () => ({
  useDocumentActions: () => ({
    openInWeb: jest.fn(),
    openInDesktop: jest.fn(),
    isActing: false,
  }),
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

// ── ComposeEditor stub — a controlled op-log handle, parameterized by module-level
//    `shouldStartDirty` so each test controls whether the mounted editor reports a
//    live edit (mirrors the "user typed, no explicit Save yet" DI-02 scenario) or
//    stays clean (the "nothing to flush" control case). ──
let shouldStartDirty = true;
const commitSavedMock = jest.fn();
const editorProps: { current: { canSave?: boolean } } = { current: {} };
jest.mock('./ComposeEditor', () => {
  const ReactLib = require('react');
  return {
    ComposeEditor: ReactLib.forwardRef(
      (props: { onDirtyChange?: (d: boolean) => void; canSave?: boolean }, ref: React.Ref<unknown>) => {
        editorProps.current = props;
        ReactLib.useEffect(() => {
          if (shouldStartDirty) {
            props.onDirtyChange?.(true);
          }
        }, []);
        ReactLib.useImperativeHandle(ref, () => ({
          isDirty: () => shouldStartDirty,
          serializeOperationLog: () => ({
            orderedOps: [{ operation: { ...CAPTURED_OP }, deletedContentFlag: false }],
            baseVersion: 'v-load',
          }),
          commitSaved: commitSavedMock,
          getBaselineParaIdMap: () => [],
          getAnchoredComments: () => [],
          getRedlineAnnotations: () => [],
          hasPendingRedlines: () => false,
          buildContentModel: () => ({ paragraphs: [] }),
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
        initialDocumentRef={{ speDriveItemId: SPE_ID, sprkDocumentId: 'sprk-doc-1', fileName: 'contract.docx' }}
        initialSessionId={DOC_SESSION}
        bffBaseUrl="https://bff.example.test"
        driveId={DRIVE_ID}
        tenantId="tenant-1"
      />
    </FluentProvider>
  );
}

beforeEach(() => {
  authenticatedFetchMock.mockClear();
  commitSavedMock.mockClear();
  saveCallCount = 0;
  saveRequestBodies.length = 0;
  shouldStartDirty = true;
  editorProps.current = {};
});

describe('ComposeWorkspace — DI-02 flush-on-unmount', () => {
  it('flushes the captured op-log via triggerSave when a DIRTY workspace unmounts with no explicit Save', async () => {
    shouldStartDirty = true;
    const { unmount } = renderWorkspace();

    // Wait for the stored-document Load to resolve into the (dirty) editor, AND for the
    // dirty signal to have propagated up to the workspace's own `isDirty` state (reflected
    // in the `canSave` prop threaded back down) — otherwise the unmount below could race
    // ahead of the child's mount-time `onDirtyChange(true)` effect.
    await waitFor(() => expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument());
    await waitFor(() => expect(editorProps.current.canSave).toBe(true));
    // No explicit Save/Ctrl+S was fired — confirm nothing has POSTed to /save yet.
    expect(saveCallCount).toBe(0);

    // Simulate the tab being closed/cleared — WorkspaceTabManager.closeTab and
    // clearAllTabs both simply stop rendering this tab's widget tree, which is
    // exactly what `unmount()` reproduces here.
    await act(async () => {
      unmount();
      // Let the fire-and-forget triggerSave's fetch + dispatch microtasks flush.
      await Promise.resolve();
      await Promise.resolve();
      await Promise.resolve();
    });

    await waitFor(() => expect(saveCallCount).toBe(1));
    const ops = (saveRequestBodies[0].operationLog as { operations: unknown[] }).operations;
    expect(ops).toEqual([CAPTURED_OP]);
  });

  it('does NOT fire a spurious save on unmount when the workspace was never edited', async () => {
    shouldStartDirty = false;
    const { unmount } = renderWorkspace();

    await waitFor(() => expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument());
    expect(saveCallCount).toBe(0);

    await act(async () => {
      unmount();
      await Promise.resolve();
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(saveCallCount).toBe(0);
  });
});
