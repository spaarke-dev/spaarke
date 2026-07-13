/**
 * ComposeWorkspace.chatBridge.test.tsx — spaarkeai-compose-r2 R3 + R4.
 *
 * R3 ("Visible to assistant" toggle → active-document conduit): the toggle lives on the WORKSPACE
 * tab strip, but the loaded document bytes live in ComposeWorkspace. Toggling ON drives the editor's
 * visibility handler → the SAME active-document registrar the Browse/stored/upload paths use, with
 * `visible: true` (register the doc into chat context). Toggling OFF passes `visible: false`
 * (withdraw). Proven by capturing the handler ComposeWorkspace registers on the bridge and asserting
 * the active-document registrar receives the doc bytes + session + the right `visible` flag.
 *
 * R4 ("Insert into document"): an Assistant suggestion routed through the bridge materializes as a
 * pending redline via the EXISTING `materializeComposeDraft` — a live selection → strike+replace
 * (`target_text`), no selection → insert at cursor (no `target_text`). Proven by capturing the
 * insert handler ComposeWorkspace registers and asserting the editor's materialize spy.
 *
 * Mirrors ComposeWorkspace.tabScopedActiveDoc.test.tsx (registrar-spy + channel-keyed usePaneEvent).
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

// ── PaneEventBus — channel-keyed usePaneEvent so we can fire BOTH 'context' + 'workspace' ─────
type PaneHandler = (event: { type: string; [k: string]: unknown }) => void;
const handlersByChannel: Record<string, PaneHandler> = {};
jest.mock(
  '@spaarke/ai-widgets/events',
  () => ({
    useDispatchPaneEvent: () => jest.fn(),
    usePaneEvent: (channel: string, handler: PaneHandler) => {
      handlersByChannel[channel] = handler;
    },
  }),
  { virtual: true }
);
function fire(channel: string, event: { type: string; [k: string]: unknown }): void {
  handlersByChannel[channel]?.(event);
}

// ── Bridge — capture the visibility + insert handlers ComposeWorkspace registers ─────────────
const registerActiveDocumentSpy = jest.fn();
let capturedVisibility: ((visible: boolean) => void) | null = null;
let capturedInsert: ((content: string, messageId?: string) => void) | null = null;
jest.mock('../context/composeActionBridge', () => ({
  useComposeActiveDocumentRegistration: () => registerActiveDocumentSpy,
  useRegisterComposeRedlineAcceptHandler: () => undefined,
  useRegisterComposeVisibilityHandler: (h: (visible: boolean) => void) => {
    capturedVisibility = h;
  },
  useRegisterComposeInsertSuggestionHandler: (h: (content: string, messageId?: string) => void) => {
    capturedInsert = h;
  },
  // spaarkeai-compose-r2 FIX #1b/#7a — Save trigger + save-completed conduits; inert doubles here.
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

// Editor mock — materializeComposeDraft is the assertion surface for R4.
const materializeComposeDraftSpy = jest.fn();
jest.mock('./ComposeEditor', () => {
  const ReactLib = require('react');
  return {
    ComposeEditor: ReactLib.forwardRef((_props: unknown, ref: React.Ref<unknown>) => {
      ReactLib.useImperativeHandle(ref, () => ({
        serialize: async () => new ArrayBuffer(0),
        getCounts: () => ({ characters: 0, words: 0 }),
        isDirty: () => false,
        materializeComposeDraft: (draft: unknown, provenance: unknown) =>
          materializeComposeDraftSpy(draft, provenance),
        materializePendingRedline: () => 'applied',
        materializeComposeEdits: () => [],
        acceptPendingRedline: () => undefined,
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
  materializeComposeDraftSpy.mockReset();
  capturedVisibility = null;
  capturedInsert = null;
  for (const k of Object.keys(handlersByChannel)) delete handlersByChannel[k];
});

describe('R3 — "Visible to assistant" toggle drives the active-document conduit', () => {
  it('ON registers the loaded document into chat context with visible:true', async () => {
    stubStoredDocumentLoad();
    renderStoredDoc();

    await waitFor(() => expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument());
    // The DEF-10 auto-register fires once on load; clear it so we assert only the toggle-driven call.
    await waitFor(() => expect(registerActiveDocumentSpy).toHaveBeenCalled());
    registerActiveDocumentSpy.mockClear();

    expect(capturedVisibility).toBeInstanceOf(Function);
    capturedVisibility!(true);

    expect(registerActiveDocumentSpy).toHaveBeenCalledTimes(1);
    const arg = registerActiveDocumentSpy.mock.calls[0][0];
    expect(arg.visible).toBe(true);
    expect(arg.documentSessionId).toBe('sess-doc-1');
    expect(arg.fileName).toBe('CIPO.docx');
    expect(arg.docxBytes).toBeInstanceOf(ArrayBuffer);
  });

  it('OFF withdraws the document with visible:false', async () => {
    stubStoredDocumentLoad();
    renderStoredDoc();

    await waitFor(() => expect(registerActiveDocumentSpy).toHaveBeenCalled());
    registerActiveDocumentSpy.mockClear();

    capturedVisibility!(false);

    expect(registerActiveDocumentSpy).toHaveBeenCalledTimes(1);
    expect(registerActiveDocumentSpy.mock.calls[0][0].visible).toBe(false);
  });
});

describe('R4 — "Insert into document" materializes an Assistant suggestion as a redline', () => {
  it('with NO live selection → insert at cursor (no target_text)', async () => {
    stubStoredDocumentLoad();
    renderStoredDoc();

    await waitFor(() => expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument());
    await waitFor(() => expect(capturedInsert).toBeInstanceOf(Function));

    capturedInsert!('A helpful suggestion.');

    expect(materializeComposeDraftSpy).toHaveBeenCalledTimes(1);
    const [draft, provenance] = materializeComposeDraftSpy.mock.calls[0];
    expect(draft).toEqual({ new_text: 'A helpful suggestion.' });
    expect(draft.target_text).toBeUndefined();
    expect(provenance.ledgerRef).toMatch(/^chat-insert:/);
    expect(provenance.turn).toBe(0);
  });

  it('with a live selection → strike+replace at selection (target_text = selected text)', async () => {
    stubStoredDocumentLoad();
    renderStoredDoc();

    await waitFor(() => expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument());
    await waitFor(() => expect(capturedInsert).toBeInstanceOf(Function));

    // Editor publishes the current selection on the 'context' channel (Flow 1).
    fire('context', {
      type: 'compose_selection_changed',
      selection: { from: 5, to: 24, selectionText: 'the clause to revise' },
    });

    capturedInsert!('The revised clause.');

    expect(materializeComposeDraftSpy).toHaveBeenCalledTimes(1);
    const [draft] = materializeComposeDraftSpy.mock.calls[0];
    expect(draft.new_text).toBe('The revised clause.');
    expect(draft.target_text).toBe('the clause to revise');
  });

  it('a COLLAPSED selection (from === to) is treated as no selection → insert at cursor', async () => {
    stubStoredDocumentLoad();
    renderStoredDoc();

    await waitFor(() => expect(capturedInsert).toBeInstanceOf(Function));

    fire('context', {
      type: 'compose_selection_changed',
      selection: { from: 10, to: 10, selectionText: '' },
    });

    capturedInsert!('Inserted text.');

    const [draft] = materializeComposeDraftSpy.mock.calls[0];
    expect(draft).toEqual({ new_text: 'Inserted text.' });
  });
});
