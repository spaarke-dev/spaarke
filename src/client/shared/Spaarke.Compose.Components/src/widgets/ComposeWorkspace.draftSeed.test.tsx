/**
 * ComposeWorkspace.draftSeed.test.tsx — DEF-08 (spaarkeai-compose-r2)
 *
 * Verifies the AI-drafted full-document SEED seam: when ComposeWorkspace is mounted with an
 * `initialDraftRef` (a chat "open as a document" draft, or an "Open in Compose" affordance), it
 * materializes the drafted body into the editor as an editable transient working draft — the
 * editor mounts POPULATED, NOT the blank Browse/Search entry-state (the DEF-08 defect).
 *
 * Two provenance shapes are covered:
 *   - Part A `{ ledgerRef, sessionId }` — resolve the body from GET /compose-outputs (the
 *     `compose-draft-document` ledger output). The editor's `initialHtml` prop == body_html.
 *   - Part B `{ html }` — inline body, no fetch. `initialHtml` == the supplied html.
 *
 * Heavy children mocked (same seam-isolation as ComposeWorkspace.upload.test.tsx): the mocked
 * ComposeEditor captures the `initialHtml` prop it receives (the editor-populated signal).
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

// FR-34 D-F3 (task 071): capture the render-ack signal ComposeWorkspace emits once the
// seeded draft actually renders. Prefixed `mock*` so the jest.mock factory may close over it.
const mockDispatchPaneEvent = jest.fn();
jest.mock('@spaarke/ai-widgets/events', () => ({
  useDispatchPaneEvent: () => mockDispatchPaneEvent,
  usePaneEvent: () => undefined,
}));

jest.mock('./hooks', () => ({
  useComposeBroadcastChannel: () => ({ postFocusMe: jest.fn(), postForceClosed: jest.fn() }),
  useComposeCheckoutLifecycle: () => ({ forceCloseAndAcquire: jest.fn(), discardAndCancel: jest.fn() }),
  useComposeHeartbeatGate: () => undefined,
}));

jest.mock('./ComposeToolbar', () => ({
  ComposeToolbar: () => <div data-testid="compose-toolbar-stub" />,
}));

// Capture BOTH the docxBytes and initialHtml the editor is handed — DEF-08 seeds via initialHtml.
const editorProps: { docxBytes: ArrayBuffer | null | undefined; initialHtml: string | null | undefined } = {
  docxBytes: undefined,
  initialHtml: undefined,
};
jest.mock('./ComposeEditor', () => {
  const ReactLib = require('react');
  return {
    ComposeEditor: ReactLib.forwardRef(
      (props: { docxBytes: ArrayBuffer | null; initialHtml?: string | null }, ref: React.Ref<unknown>) => {
        editorProps.docxBytes = props.docxBytes;
        editorProps.initialHtml = props.initialHtml;
        ReactLib.useImperativeHandle(ref, () => ({
          serialize: async () => new ArrayBuffer(0),
          getCounts: () => ({ characters: 0, words: 0 }),
          isDirty: () => true,
          materializeComposeDraft: () => undefined,
          materializePendingRedline: () => 'applied',
        }));
        // Render the initialHtml so the test can assert the editor is POPULATED (not blank).
        return <div data-testid="compose-editor-stub" dangerouslySetInnerHTML={{ __html: props.initialHtml ?? '' }} />;
      }
    ),
  };
});

// eslint-disable-next-line import/first
import { ComposeWorkspace } from './ComposeWorkspace';

function renderWorkspace(props: Partial<React.ComponentProps<typeof ComposeWorkspace>> = {}) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <ComposeWorkspace bffBaseUrl="https://bff.example.test" driveId="" tenantId="tenant-1" {...props} />
    </FluentProvider>
  );
}

const DRAFT_BODY = '<h1>Re: Acme NDA</h1><p>Dear Client, the confidentiality term runs five years.</p>';

beforeEach(() => {
  authenticatedFetchMock.mockReset();
  authenticatedFetchMock.mockResolvedValue({ ok: false, status: 404, json: async () => [] });
  editorProps.docxBytes = undefined;
  editorProps.initialHtml = undefined;
  mockDispatchPaneEvent.mockClear();
});

/** All `compose_content_rendered` render-ack signals emitted (FR-34 D-F3, task 071). */
const renderAckSignals = () =>
  mockDispatchPaneEvent.mock.calls.filter(
    ([channel, event]) =>
      channel === 'workspace' && (event as { type?: string } | undefined)?.type === 'compose_content_rendered'
  );

describe('ComposeWorkspace — DEF-08 AI-drafted full-document seed', () => {
  it('Part A: resolves the drafted body from the ledger and mounts the editor POPULATED (not blank)', async () => {
    // GET /compose-outputs → the compose-draft-document full-document output.
    authenticatedFetchMock.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => [
        {
          key: 'binding-1@t1',
          bindingId: 'binding-1',
          turn: 1,
          disposition: 'compose',
          payload: { title: 'Client Letter — Acme NDA', body_html: DRAFT_BODY },
        },
      ],
    });

    renderWorkspace({ initialDraftRef: { ledgerRef: 'binding-1@t1', sessionId: 'sess-1' } });

    // Editor mounts POPULATED — NOT the blank Browse/Search empty-state (the DEF-08 defect).
    await waitFor(() => expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument());
    expect(screen.queryByTestId('compose-empty-state')).not.toBeInTheDocument();
    await waitFor(() => expect(editorProps.initialHtml).toBe(DRAFT_BODY));
    // The seed is HTML, not a docx round-trip.
    expect(editorProps.docxBytes).toBeNull();

    // It resolved via the compose-outputs read endpoint keyed on the launch session id.
    const outputCalls = authenticatedFetchMock.mock.calls.filter(([u]) =>
      String(u).includes('/api/ai/chat/sessions/sess-1/compose-outputs')
    );
    expect(outputCalls.length).toBeGreaterThanOrEqual(1);

    // FR-34 D-F3 (task 071): once the seeded draft has rendered (editor populated), the
    // content-render ack signal is emitted — correlated by ledgerRef — so WorkspacePane's
    // deferred ack fires. This is the honest "content is on screen" moment.
    await waitFor(() => expect(renderAckSignals().length).toBeGreaterThan(0));
    expect(renderAckSignals()[0][1]).toEqual({
      type: 'compose_content_rendered',
      ledgerRef: 'binding-1@t1',
      sessionId: 'sess-1',
    });
  });

  it('Part B: mounts the inline-html draft directly with NO ledger fetch', async () => {
    renderWorkspace({ initialDraftRef: { html: '<p>Inline drafted letter body.</p>' } });

    await waitFor(() => expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument());
    expect(screen.queryByTestId('compose-empty-state')).not.toBeInTheDocument();
    expect(editorProps.initialHtml).toBe('<p>Inline drafted letter body.</p>');

    // No compose-outputs fetch on the inline path.
    const outputCalls = authenticatedFetchMock.mock.calls.filter(([u]) => String(u).includes('/compose-outputs'));
    expect(outputCalls).toHaveLength(0);
  });

  it('surfaces an error (not a silent blank tab) when the drafted output is missing from the ledger', async () => {
    authenticatedFetchMock.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => [], // no matching output
    });

    renderWorkspace({ initialDraftRef: { ledgerRef: 'binding-1@t1', sessionId: 'sess-1' } });

    await waitFor(() => expect(screen.getByTestId('compose-workspace-error-empty')).toBeInTheDocument());
    expect(screen.queryByTestId('compose-editor-stub')).not.toBeInTheDocument();

    // FR-34 D-F3 (task 071): the seed FAILED to render → NO content-render ack signal is
    // emitted → WorkspacePane never acks → the server's WaitForAckAsync times out (honest
    // failure). A false ack here would be the R2-D fabrication this task closes.
    expect(renderAckSignals()).toHaveLength(0);
  });

  it('yields to a stored-document ref (mutually exclusive — draft seed does not fetch)', async () => {
    renderWorkspace({
      initialDraftRef: { ledgerRef: 'binding-1@t1', sessionId: 'sess-1' },
      initialDocumentRef: { speDriveItemId: 'spe-1', fileName: 'stored.docx' },
      driveId: 'drive-1',
    });

    await waitFor(() => expect(authenticatedFetchMock).toHaveBeenCalled());
    const outputCalls = authenticatedFetchMock.mock.calls.filter(([u]) => String(u).includes('/compose-outputs'));
    // The stored-document Load path wins; the draft-seed effect must not fire its ledger fetch.
    expect(outputCalls).toHaveLength(0);
  });
});
