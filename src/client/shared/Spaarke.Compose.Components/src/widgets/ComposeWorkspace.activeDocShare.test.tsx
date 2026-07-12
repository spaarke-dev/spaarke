/**
 * ComposeWorkspace.activeDocShare.test.tsx — DEF-10 / DEF-UAT-1 part 2 (2026-07-12)
 *
 * Proves the headline cross-pane fix: when a HOST `sprk_document` is opened in Compose
 * ("Open in Compose"), the loaded DOCX bytes are handed to the SAME host registrar the
 * Browse path uses — i.e. `ConversationPane.registerComposeActiveDocument`, which lands
 * them as a `ChatSessionFile` (ExtractedText) via the EXISTING chat upload endpoint so the
 * Assistant can "summarize this document". Before the fix the stored-document Load path
 * never reached the chat session (the two-session split), so the Assistant answered
 * "no document uploaded in this session".
 *
 * This is the CLIENT half of the vertical slice: it asserts the actual loaded bytes cross
 * into the chat-upload conduit (not a flag). The SERVER half — those bytes becoming a
 * ChatSessionFile with ExtractedText that the chat context resolves — is the EXISTING chat
 * upload endpoint (ChatDocumentEndpoints) + `ActiveDocumentBridgeSeamTests` (which seeds a
 * session whose active ChatSessionFile carries ExtractedText). No new BFF surface (§11).
 *
 * Mocks mirror ComposeWorkspace.upload.test.tsx (the seam-under-test harness), plus a spy
 * for the active-document registrar so we can observe the hand-off.
 */

import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

// Fluent's MessageBar uses ResizeObserver, which jsdom lacks.
if (typeof (globalThis as { ResizeObserver?: unknown }).ResizeObserver === 'undefined') {
  (globalThis as { ResizeObserver?: unknown }).ResizeObserver = class {
    observe(): void {}
    unobserve(): void {}
    disconnect(): void {}
  };
}

// ── Fetch boundary (ADR-028) ────────────────────────────────────────────────
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

// ── PaneEventBus (no-op) ────────────────────────────────────────────────────
jest.mock(
  '@spaarke/ai-widgets/events',
  () => ({
    useDispatchPaneEvent: () => jest.fn(),
    usePaneEvent: () => undefined,
  }),
  { virtual: true }
);

// ── Active-document registrar bridge — SPY (the hand-off under test) ─────────
const registerActiveDocumentSpy = jest.fn();
jest.mock('../context/composeActionBridge', () => ({
  useComposeActiveDocumentRegistration: () => registerActiveDocumentSpy,
  // DEF-12: ComposeWorkspace now also registers a redline-accept handler; stub it as a no-op here.
  useRegisterComposeRedlineAcceptHandler: () => undefined,
}));

// ── Heavy workspace hooks — inert doubles ───────────────────────────────────
jest.mock('./hooks', () => ({
  useComposeBroadcastChannel: () => ({ postFocusMe: jest.fn(), postForceClosed: jest.fn() }),
  useComposeCheckoutLifecycle: () => ({ forceCloseAndAcquire: jest.fn(), discardAndCancel: jest.fn() }),
  useComposeHeartbeatGate: () => undefined,
}));

// ── ComposeToolbar — avoid @spaarke/document-operations runtime auth ────────
jest.mock('./ComposeToolbar', () => ({
  ComposeToolbar: () => <div data-testid="compose-toolbar-stub" />,
}));

// ── ComposeEditor — inert stub ──────────────────────────────────────────────
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

// Import AFTER mocks are registered.
// eslint-disable-next-line import/first
import { ComposeWorkspace } from './ComposeWorkspace';

function renderWorkspace(props: Partial<React.ComponentProps<typeof ComposeWorkspace>> = {}) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <ComposeWorkspace
        bffBaseUrl="https://bff.example.test"
        driveId="drive-1"
        tenantId="tenant-1"
        initialSessionId="sess-1"
        initialDocumentRef={{ speDriveItemId: 'spe-item-1', sprkDocumentId: 'doc-guid-1', fileName: 'CIPO.docx' }}
        {...props}
      />
    </FluentProvider>
  );
}

// Load response — ASP.NET Core serializes byte[] as base64. btoa('abc') = 'YWJj' (3 bytes).
function stubStoredDocumentLoad(): void {
  authenticatedFetchMock.mockImplementation((url: string) => {
    if (String(url).includes('/api/compose/documents/') && !String(url).includes('/save')) {
      return Promise.resolve({
        ok: true,
        status: 200,
        json: async () => ({
          documentSpeId: 'spe-item-1',
          driveId: 'drive-1',
          sessionId: 'sess-1',
          documentRecordId: 'doc-guid-1',
          content: 'YWJj',
          eTag: 'etag-1',
          fileName: 'CIPO.docx',
          size: 3,
        }),
      });
    }
    // FR-04 compose-outputs GET (fired on 'loaded') + any other call — benign empty.
    return Promise.resolve({ ok: false, status: 404, json: async () => [] });
  });
}

beforeEach(() => {
  authenticatedFetchMock.mockReset();
  registerActiveDocumentSpy.mockReset();
});

describe('ComposeWorkspace — DEF-10 host document is shared with the Assistant chat session', () => {
  it('hands the loaded host document bytes to the chat-upload registrar (default visible-to-assistant ON)', async () => {
    stubStoredDocumentLoad();

    renderWorkspace();

    // The stored document loads into the editor...
    await waitFor(() => expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument());

    // ...and its bytes are handed to the SAME registrar the Browse path uses (which POSTs to
    // the chat upload endpoint → ChatSessionFile with ExtractedText). This is the doc TEXT
    // reaching the chat-context path — not a mere flag.
    await waitFor(() => expect(registerActiveDocumentSpy).toHaveBeenCalledTimes(1));
    const arg = registerActiveDocumentSpy.mock.calls[0][0];
    expect(arg.fileName).toBe('CIPO.docx');
    expect(arg.docxBytes).toBeInstanceOf(ArrayBuffer);
    expect((arg.docxBytes as ArrayBuffer).byteLength).toBe(3); // the loaded DOCX bytes, not empty
  });

  it('registers the active document exactly once per stored document (idempotent on re-render)', async () => {
    stubStoredDocumentLoad();

    const { rerender } = renderWorkspace();
    await waitFor(() => expect(registerActiveDocumentSpy).toHaveBeenCalledTimes(1));

    // A benign re-render must not re-register (ref-guarded by speDriveItemId).
    rerender(
      <FluentProvider theme={webLightTheme}>
        <ComposeWorkspace
          bffBaseUrl="https://bff.example.test"
          driveId="drive-1"
          tenantId="tenant-1"
          initialSessionId="sess-1"
          initialDocumentRef={{ speDriveItemId: 'spe-item-1', sprkDocumentId: 'doc-guid-1', fileName: 'CIPO.docx' }}
        />
      </FluentProvider>
    );

    // Give any effects a chance to (not) fire again.
    await new Promise((r) => setTimeout(r, 0));
    expect(registerActiveDocumentSpy).toHaveBeenCalledTimes(1);
  });
});
