/**
 * ComposeWorkspace.tabScopedActiveDoc.test.tsx — Wave 3 Parts 2 + 3.
 *
 * Part 2 (DEF-11 TEXT-path close): when ComposeWorkspace registers its active document with the host,
 * it threads the tab's `documentSessionId` (state.sessionId) so the server sets
 * ActiveDocument.DocumentSessionId → BindingCapabilityTool routes a typed revise/draft into THIS doc
 * session. Proven for the stored-host-document registration (the loaded sessionId).
 *
 * Part 3 (tab-scoped ActiveDocument): when a Compose tab becomes the ACTIVE workspace tab, the widget
 * re-registers its active document (most-recent-active-wins) via the SAME conduit — reusing the tab's
 * document session id. A non-Compose tab activation does NOT re-register.
 *
 * Mirrors ComposeWorkspace.activeDocShare.test.tsx (the registrar-spy harness); adds a controllable
 * `usePaneEvent` mock so a tab_change can be injected deterministically.
 */

import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

if (typeof (globalThis as { ResizeObserver?: unknown }).ResizeObserver === 'undefined') {
  (globalThis as { ResizeObserver?: unknown }).ResizeObserver = class {
    observe(): void {}
    unobserve(): void {}
    disconnect(): void {}
  };
}

// ── Fetch boundary ──────────────────────────────────────────────────────────
const authenticatedFetchMock = jest.fn();
jest.mock('@spaarke/auth', () => ({
  authenticatedFetch: (...args: unknown[]) => authenticatedFetchMock(...args),
  useAuth: () => ({
    isAuthenticated: true,
    getAccessToken: async () => 'test-token',
    authenticatedFetch: (...args: unknown[]) => authenticatedFetchMock(...args),
    tenantId: 'test-tenant',
    logout: jest.fn(),
  }),
}));

// ── PaneEventBus — controllable usePaneEvent (capture the workspace handler) ──
type WorkspaceHandler = (event: { type: string; widgetData?: unknown }) => void;
const workspaceHandlers: WorkspaceHandler[] = [];
jest.mock(
  '@spaarke/ai-widgets/events',
  () => ({
    useDispatchPaneEvent: () => jest.fn(),
    usePaneEvent: (_channel: string, handler: WorkspaceHandler) => {
      // Register once per render; tests invoke the latest handler via fireTabChange().
      workspaceHandlers[0] = handler;
    },
  }),
  { virtual: true }
);
function fireTabChange(event: { type: string; widgetType?: string; widgetData?: unknown }): void {
  workspaceHandlers[0]?.(event);
}

// ── Active-document registrar bridge — SPY ──────────────────────────────────
const registerActiveDocumentSpy = jest.fn();
jest.mock('../context/composeActionBridge', () => ({
  useComposeActiveDocumentRegistration: () => registerActiveDocumentSpy,
  useRegisterComposeRedlineAcceptHandler: () => undefined,
  // spaarkeai-compose-r2 R3/R4 — ComposeWorkspace now registers a visibility + insert-suggestion
  // handler on the bridge; these inert doubles keep this harness (which mocks the whole bridge) green.
  useRegisterComposeVisibilityHandler: () => undefined,
  useRegisterComposeInsertSuggestionHandler: () => undefined,
  // spaarkeai-compose-r2 FIX #1b/#7a — Save trigger + save-completed conduits; inert doubles.
  useRegisterComposeSaveHandler: () => undefined,
  useComposeSaveCompleted: () => null,
}));

// ── Heavy workspace hooks — inert doubles ───────────────────────────────────
jest.mock('./hooks', () => ({
  useComposeBroadcastChannel: () => ({ postFocusMe: jest.fn(), postForceClosed: jest.fn() }),
  useComposeCheckoutLifecycle: () => ({ forceCloseAndAcquire: jest.fn(), discardAndCancel: jest.fn() }),
  useComposeHeartbeatGate: () => undefined,
}));

jest.mock('./ComposeToolbar', () => ({
  ComposeToolbar: () => <div data-testid="compose-toolbar-stub" />,
}));

jest.mock('./ComposeEditor', () => {
  const ReactLib = require('react');
  return {
    ComposeEditor: ReactLib.forwardRef((_props: unknown, ref: React.Ref<unknown>) => {
      ReactLib.useImperativeHandle(ref, () => ({
        serialize: async () => new ArrayBuffer(0),
        getCounts: () => ({ characters: 0, words: 0 }),
        isDirty: () => false,
        materializeComposeDraft: () => undefined,
        materializePendingRedline: () => 'applied',
      }));
      return <div data-testid="compose-editor-stub" />;
    }),
  };
});

// eslint-disable-next-line import/first
import { ComposeWorkspace } from './ComposeWorkspace';

function renderStoredDoc() {
  return render(
    <FluentProvider theme={webLightTheme}>
      <ComposeWorkspace
        bffBaseUrl="https://bff.example.test"
        driveId="drive-1"
        tenantId="tenant-1"
        initialSessionId="sess-doc-1"
        initialDocumentRef={{ speDriveItemId: 'spe-item-1', sprkDocumentId: 'doc-guid-1', fileName: 'CIPO.docx' }}
      />
    </FluentProvider>
  );
}

function stubStoredDocumentLoad(): void {
  authenticatedFetchMock.mockImplementation((url: string) => {
    if (String(url).includes('/api/compose/documents/') && !String(url).includes('/save')) {
      return Promise.resolve({
        ok: true,
        status: 200,
        json: async () => ({
          documentSpeId: 'spe-item-1',
          driveId: 'drive-1',
          sessionId: 'sess-doc-1',
          documentRecordId: 'doc-guid-1',
          content: 'YWJj',
          eTag: 'etag-1',
          fileName: 'CIPO.docx',
          size: 3,
        }),
      });
    }
    return Promise.resolve({ ok: false, status: 404, json: async () => [] });
  });
}

beforeEach(() => {
  authenticatedFetchMock.mockReset();
  registerActiveDocumentSpy.mockReset();
  workspaceHandlers.length = 0;
});

describe('ComposeWorkspace — Wave 3 Part 2: registration threads the tab document session id', () => {
  it('passes documentSessionId (the loaded session id) to the active-document registrar', async () => {
    stubStoredDocumentLoad();
    renderStoredDoc();

    await waitFor(() => expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument());
    await waitFor(() => expect(registerActiveDocumentSpy).toHaveBeenCalledTimes(1));

    const arg = registerActiveDocumentSpy.mock.calls[0][0];
    expect(arg.fileName).toBe('CIPO.docx');
    expect(arg.documentSessionId).toBe('sess-doc-1');
  });
});

describe('ComposeWorkspace — Wave 3 Part 3: tab-scoped re-registration on tab_change', () => {
  it('re-registers this document when its Compose tab becomes active (most-recent-active-wins)', async () => {
    stubStoredDocumentLoad();
    renderStoredDoc();

    await waitFor(() => expect(registerActiveDocumentSpy).toHaveBeenCalledTimes(1));

    // A Compose tab becomes active → re-register with the same document session id.
    fireTabChange({ type: 'tab_change', widgetData: { layoutName: 'Compose', compose: { upload: {} } } });

    await waitFor(() => expect(registerActiveDocumentSpy).toHaveBeenCalledTimes(2));
    expect(registerActiveDocumentSpy.mock.calls[1][0].documentSessionId).toBe('sess-doc-1');
  });

  it('re-registers when the DIRECT "compose" widget tab becomes active by widgetType alone (spaarkeai-compose-r2)', async () => {
    stubStoredDocumentLoad();
    renderStoredDoc();

    await waitFor(() => expect(registerActiveDocumentSpy).toHaveBeenCalledTimes(1));

    // The first-class DIRECT Compose tab is recognized by widgetType — even when
    // the re-activation carries no compose seed / layoutName in widgetData.
    fireTabChange({ type: 'tab_change', widgetType: 'compose', widgetData: { source: 'compose-add-to-dms' } });

    await waitFor(() => expect(registerActiveDocumentSpy).toHaveBeenCalledTimes(2));
    expect(registerActiveDocumentSpy.mock.calls[1][0].documentSessionId).toBe('sess-doc-1');
  });

  it('does NOT re-register when a NON-Compose tab becomes active', async () => {
    stubStoredDocumentLoad();
    renderStoredDoc();

    await waitFor(() => expect(registerActiveDocumentSpy).toHaveBeenCalledTimes(1));

    fireTabChange({ type: 'tab_change', widgetData: { layoutName: 'Daily Briefing' } });

    // Give any effects a chance to (not) fire.
    await new Promise((r) => setTimeout(r, 0));
    expect(registerActiveDocumentSpy).toHaveBeenCalledTimes(1);
  });
});
