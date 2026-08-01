/**
 * ComposeWorkspace.saveOpLogPreservation.test.tsx — spaarkeai-compose-r4 task 038 (zero-error guardrails).
 *
 * THE DATA-INTEGRITY BAR: a rejected (422) save MUST leave the document dirty with its op-log INTACT so a
 * retry re-sends the SAME edits — no valid text edit is lost on reload. Before task 038, `triggerSave`
 * serialized the op-log AND reset it (+ cleared the dirty flag) BEFORE the POST; on a 422 the log was
 * already gone, so a retry sent an empty log and every edit in that batch vanished. The fix reorders the
 * clear to happen ONLY after a confirmed 200 (via the editor handle's new `commitSaved()`), and leaves the
 * doc dirty with the op-log intact on failure.
 *
 * This drives the REAL `ComposeWorkspace.triggerSave` flow with a stubbed `ComposeEditor` handle (the
 * op-log capture itself is unit-tested against the real `RebasedOperationLog` in
 * `stepOperationInterceptor.test.ts`). The `@spaarke/auth` fetch boundary is mocked: the Load serves a
 * stored doc, the first `/save` returns 422, the second returns 200. We assert:
 *   1. both saves POST the SAME `operationLog.operations` (the retry re-sends the batch — no loss),
 *   2. the editor's `commitSaved()` is NOT called on the 422 (op-log preserved) and IS called after the 200,
 *   3. the workspace stays saveable (dirty) after the 422.
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

const SPE_ID = 'drive-item-doc-1';
const DRIVE_ID = 'drive-1';
const DOC_SESSION = '00000000-0000-0000-0000-0000000d0c07';
// A minimal valid base64 of the ZIP magic `PK\x03\x04` — the workspace only base64-decodes it into the
// `docxBytes` prop, which the STUB editor ignores (mammoth parsing lives inside the real editor).
const CONTENT_B64 = 'UEsDBA==';

/** The fixed op the stubbed editor "captured" this dirty session — a single interior text insert. */
const CAPTURED_OP = { type: 'insertText', paraId: 'AAAA0001', at: { runIndex: 0, offset: 3 }, text: 'x' };

// ── Fetch boundary (ADR-028): Load → stored doc; first /save → 422; second /save → 200 ──
let saveCallCount = 0;
const saveRequestBodies: Array<Record<string, unknown>> = [];
const authenticatedFetchMock = jest.fn(async (url: string, init?: RequestInit): Promise<Response> => {
  // Replace-path save (checked BEFORE the generic documents Load route).
  if (url.includes('/api/compose/documents/') && url.includes('/save')) {
    saveCallCount += 1;
    saveRequestBodies.push(JSON.parse(String(init?.body ?? '{}')) as Record<string, unknown>);
    if (saveCallCount === 1) {
      // 422 — a refused op. The workspace reads `response.clone().json()` for the detail.
      return {
        ok: false,
        status: 422,
        clone: () => ({ json: async () => ({ detail: 'A tracked edit could not be applied.' }) }),
        json: async () => ({ detail: 'A tracked edit could not be applied.' }),
        text: async () => 'A tracked edit could not be applied.',
      } as unknown as Response;
    }
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

// `@spaarke/ui-components` + `@spaarke/document-operations` resolve to built `dist/` bundles in this jest
// project whose own transitive `@spaarke/auth` require is unresolvable without a full sibling-package
// build (the same pre-existing condition that stops the other ComposeWorkspace.*.test suites from running
// here). Mock them to the tiny surface ComposeWorkspace actually consumes so this test is self-contained.
jest.mock('@spaarke/ui-components', () => ({
  createXrmNavigationService: () => ({ openLookup: jest.fn() }),
  createXrmDataService: () => ({ retrieveRecord: jest.fn() }),
  // FR-14 (task 051) — ComposeWorkspace mounts <SendEmailDialog/> unconditionally (controlled via its
  // own `open` prop, mirroring ComposeConflictDialog); a no-op stub keeps this mock complete.
  SendEmailDialog: () => null,
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
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  ComposeToolbar: () => <div data-testid="compose-toolbar-stub" />,
}));

// ── ComposeEditor stub — a controlled op-log handle. `serializeOperationLog` is NON-DESTRUCTIVE by
//    construction (returns a fresh snapshot with the SAME op each call, mirroring the real editor after
//    task 038); `commitSaved` is a spy so the test can prove it fires ONLY after the 200. ──
const commitSavedMock = jest.fn();
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
        // The real editor reports dirty via onDirtyChange once the user edits; simulate a single edit on
        // mount so the workspace's Save becomes enabled (canSave) and the op-log dirty-save path is taken.
        ReactLib.useEffect(() => {
          props.onDirtyChange?.(true);
        }, []);
        ReactLib.useImperativeHandle(ref, () => ({
          isDirty: () => true,
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
  editorProps.current = {};
});

/** Fire the workspace Save (the toolbar's onSave → triggerSave) and let its async flow settle. */
async function clickSave(): Promise<void> {
  await act(async () => {
    editorProps.current.onSave?.();
    // Let the fetch promise + dispatch microtasks flush.
    await Promise.resolve();
    await Promise.resolve();
  });
}

describe('ComposeWorkspace — op-log survives a rejected save; cleared only on 200 (task 038)', () => {
  it('a 422 preserves the op-log + dirty; the retry re-sends the SAME edits and only the 200 commits', async () => {
    renderWorkspace();

    // Wait for the stored-document Load to resolve into the editor.
    await waitFor(() => expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument());
    await waitFor(() => expect(editorProps.current.canSave).toBe(true));

    // ---- First save → 422 (rejected) ----
    await clickSave();
    await waitFor(() => expect(saveCallCount).toBe(1));

    // The batch was sent, but the failed save did NOT commit (clear) the op-log.
    expect(commitSavedMock).not.toHaveBeenCalled();
    // The document stays dirty/saveable — a retry is possible (op-log intact behind the editor handle).
    await waitFor(() => expect(editorProps.current.canSave).toBe(true));

    // ---- Retry → 200 (confirmed) ----
    await clickSave();
    await waitFor(() => expect(saveCallCount).toBe(2));

    // The retry re-sent the SAME operations — no valid text edit was lost by the failed save.
    const firstOps = (saveRequestBodies[0].operationLog as { operations: unknown[] }).operations;
    const secondOps = (saveRequestBodies[1].operationLog as { operations: unknown[] }).operations;
    expect(firstOps).toEqual([CAPTURED_OP]);
    expect(secondOps).toEqual(firstOps);

    // The op-log is committed (cleared) ONLY now, on the confirmed 200.
    await waitFor(() => expect(commitSavedMock).toHaveBeenCalledTimes(1));
  });
});
