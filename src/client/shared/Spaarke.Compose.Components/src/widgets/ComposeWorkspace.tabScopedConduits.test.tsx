/**
 * ComposeWorkspace.tabScopedConduits.test.tsx — spaarkeai-compose-r2 (multi-Compose-tab correctness).
 *
 * The cross-pane bridge holds ONE slot per conduit (Save / redline-Accept / Insert-suggestion); the
 * LAST-mounted Compose instance wins each slot. With 2+ Compose tabs open (each mounted, only the
 * active one visible) a chat-issued Save / Accept / Insert must service the tab the user is VIEWING,
 * never a hidden background tab. This test proves the three bridge-facing handlers are gated by
 * `isActiveTab`: an INACTIVE tab's instance NO-OPS the bridge action; an ACTIVE tab's instance (the
 * default for standalone / single-instance mounts) services it.
 *
 * Mirrors ComposeWorkspace.chatBridge.test.tsx — captures the handlers ComposeWorkspace registers on
 * the bridge and drives them directly.
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

// ── PaneEventBus — inert (this test drives bridge handlers directly) ─────────
jest.mock('@spaarke/ai-widgets/events', () => ({
  useDispatchPaneEvent: () => jest.fn(),
  usePaneEvent: () => undefined,
}));

// ── Bridge — capture the Save / Accept / Insert handlers ComposeWorkspace registers ──────────
const registerActiveDocumentSpy = jest.fn();
let capturedSave: (() => void | Promise<void>) | null = null;
let capturedAccept: ((ledgerRef: string) => void) | null = null;
let capturedInsert: ((content: string, messageId?: string) => void) | null = null;
jest.mock('../context/composeActionBridge', () => ({
  useComposeActiveDocumentRegistration: () => registerActiveDocumentSpy,
  useRegisterComposeRedlineAcceptHandler: (h: (ledgerRef: string) => void) => {
    capturedAccept = h;
  },
  useRegisterComposeVisibilityHandler: () => undefined,
  useRegisterComposeInsertSuggestionHandler: (h: (content: string, messageId?: string) => void) => {
    capturedInsert = h;
  },
  useRegisterComposeSaveHandler: (h: () => void | Promise<void>) => {
    capturedSave = h;
  },
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

// Editor mock — acceptPendingRedline + materializeComposeDraft are the assertion surfaces.
const acceptPendingRedlineSpy = jest.fn();
const materializeComposeDraftSpy = jest.fn();
jest.mock('./ComposeEditor', () => {
  const ReactLib = require('react');
  return {
    ComposeEditor: ReactLib.forwardRef((_props: unknown, ref: React.Ref<unknown>) => {
      ReactLib.useImperativeHandle(ref, () => ({
        serialize: async () => new ArrayBuffer(3),
        getCounts: () => ({ characters: 0, words: 0 }),
        isDirty: () => false,
        hasPendingRedlines: () => false,
        materializeComposeDraft: (draft: unknown, provenance: unknown) => materializeComposeDraftSpy(draft, provenance),
        materializePendingRedline: () => 'applied',
        materializeComposeEdits: () => [],
        acceptPendingRedline: (ledgerRef: string) => acceptPendingRedlineSpy(ledgerRef),
        rejectPendingRedline: () => undefined,
        highlightCitedSpan: () => 'noop',
        clearCitedHighlight: () => undefined,
      }));
      return <div data-testid="compose-editor-stub" />;
    }),
  };
});

// eslint-disable-next-line import/first
import { ComposeWorkspace } from './ComposeWorkspace';

function renderStoredDoc(isActiveTab: boolean) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <ComposeWorkspace
        bffBaseUrl="https://bff.example.test"
        driveId="drive-1"
        tenantId="tenant-1"
        isActiveTab={isActiveTab}
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
    // Save route (replace path ends in /save).
    return Promise.resolve({
      ok: true,
      status: 200,
      json: async () => ({
        documentSpeId: 'spe-item-1',
        documentRecordId: 'doc-guid-1',
        eTag: 'etag-2',
        size: 3,
        wasPromotedThisSave: false,
      }),
    });
  });
}

function saveFetchCount(): number {
  return authenticatedFetchMock.mock.calls.filter(c => String(c[0]).includes('/save')).length;
}

beforeEach(() => {
  authenticatedFetchMock.mockReset();
  registerActiveDocumentSpy.mockReset();
  acceptPendingRedlineSpy.mockReset();
  materializeComposeDraftSpy.mockReset();
  capturedSave = null;
  capturedAccept = null;
  capturedInsert = null;
});

describe('ComposeWorkspace — bridge conduits are active-tab-scoped (multi-Compose-tab)', () => {
  it('the ACTIVE tab services Save / Accept / Insert from the bridge', async () => {
    stubStoredDocumentLoad();
    renderStoredDoc(true);

    await waitFor(() => expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument());
    await waitFor(() => expect(capturedSave).toBeInstanceOf(Function));

    capturedInsert!('A helpful suggestion.');
    expect(materializeComposeDraftSpy).toHaveBeenCalledTimes(1);

    capturedAccept!('chat-insert:1');
    expect(acceptPendingRedlineSpy).toHaveBeenCalledWith('chat-insert:1');

    await capturedSave!();
    await waitFor(() => expect(saveFetchCount()).toBe(1));
  });

  it('an INACTIVE tab NO-OPS Save / Accept / Insert from the bridge (does not touch the hidden doc)', async () => {
    stubStoredDocumentLoad();
    renderStoredDoc(false);

    await waitFor(() => expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument());
    await waitFor(() => expect(capturedSave).toBeInstanceOf(Function));

    capturedInsert!('A helpful suggestion.');
    capturedAccept!('chat-insert:1');
    await capturedSave!();

    // Give any async save a chance to (not) fire.
    await new Promise(r => setTimeout(r, 0));

    expect(materializeComposeDraftSpy).not.toHaveBeenCalled();
    expect(acceptPendingRedlineSpy).not.toHaveBeenCalled();
    expect(saveFetchCount()).toBe(0);
  });
});
