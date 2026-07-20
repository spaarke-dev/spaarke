/**
 * ComposeWorkspace.upload.test.tsx — FR-03 (spaarkeai-compose-r2 task 012)
 *
 * Verifies the transient upload-mount seam: when ComposeWorkspace is mounted with an
 * `initialUploadRef` (a chat "open in Compose" on an Assistant-UPLOADED file), it fetches
 * the retained bytes from `POST /api/compose/upload` and routes them into the editor's
 * `docxBytes` seam as a transient working draft — no empty tab, no `sprk_document` create.
 *
 * Heavy children are mocked to keep the test on the seam under test:
 *   - `./ComposeEditor`  — captured stub recording the `docxBytes` prop it receives.
 *   - `./ComposeToolbar` — pulls `@spaarke/document-operations` (auth-context runtime).
 *   - `./hooks`          — BroadcastChannel / checkout / heartbeat side-effects.
 *   - `@spaarke/ai-widgets/events` — PaneEventBus dispatch/subscribe.
 *   - `@spaarke/auth`    — `authenticatedFetch` is the fetch boundary we drive.
 */

import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

// Fluent's MessageBar (error/warning banners) uses ResizeObserver, which jsdom lacks.
// Minimal no-op polyfill so the error-state render doesn't throw.
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

// ── PaneEventBus (no-op in this test) ───────────────────────────────────────
// NOT `virtual`: jest.config `moduleNameMapper` maps `@spaarke/ai-widgets/events` to the real
// source, so a resolved (non-virtual) mock binds to the mapped path and applies deterministically
// (a virtual mock is keyed to the raw specifier and gets bypassed in a shared --runInBand registry).
jest.mock('@spaarke/ai-widgets/events', () => ({
  useDispatchPaneEvent: () => jest.fn(),
  usePaneEvent: () => undefined,
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

// ── ComposeEditor — capture the docxBytes it is handed ──────────────────────
const editorDocxBytes: { current: ArrayBuffer | null | undefined } = { current: undefined };
jest.mock('./ComposeEditor', () => {
  const ReactLib = require('react');
  return {
    ComposeEditor: ReactLib.forwardRef((props: { docxBytes: ArrayBuffer | null }, ref: React.Ref<unknown>) => {
      editorDocxBytes.current = props.docxBytes;
      ReactLib.useImperativeHandle(ref, () => ({
        serialize: async () => new ArrayBuffer(0),
        getCounts: () => ({ characters: 0, words: 0 }),
        isDirty: () => true,
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
        driveId=""
        tenantId="tenant-1"
        initialUploadRef={{ sessionId: 'sess-1', sessionFileId: 'file-abc', fileName: 'draft.docx' }}
        {...props}
      />
    </FluentProvider>
  );
}

beforeEach(() => {
  authenticatedFetchMock.mockReset();
  // Benign default for any secondary call (e.g. the FR-04 `compose-outputs` GET fired
  // when the editor reaches 'loaded' — no AI drafts for a fresh upload).
  authenticatedFetchMock.mockResolvedValue({ ok: false, status: 404, json: async () => [] });
  editorDocxBytes.current = undefined;
});

describe('ComposeWorkspace — FR-03 transient upload-mount', () => {
  it('mounts the uploaded file bytes into the editor (no empty tab)', async () => {
    // btoa('abc') = 'YWJj' — a 3-byte base64 payload (ASP.NET Core byte[] wire shape).
    authenticatedFetchMock.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({ content: 'YWJj', fileName: 'draft.docx', size: 3 }),
    });

    renderWorkspace();

    // Editor mounts with the decoded bytes; empty state is gone.
    await waitFor(() => expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument());
    expect(screen.queryByTestId('compose-empty-state')).not.toBeInTheDocument();

    expect(editorDocxBytes.current).toBeInstanceOf(ArrayBuffer);
    expect((editorDocxBytes.current as ArrayBuffer).byteLength).toBe(3);
  });

  it('POSTs to /api/compose/upload with the session + file ids, and issues no create/save on mount', async () => {
    authenticatedFetchMock.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({ content: 'YWJj', fileName: 'draft.docx', size: 3 }),
    });

    renderWorkspace();

    await waitFor(() => expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument());

    const urls = authenticatedFetchMock.mock.calls.map(([u]) => String(u));

    // Exactly one upload-serve fetch, with the session + file ids.
    const uploadCalls = authenticatedFetchMock.mock.calls.filter(([u]) => String(u).includes('/api/compose/upload'));
    expect(uploadCalls).toHaveLength(1);
    const [, init] = uploadCalls[0];
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body)).toEqual({ sessionId: 'sess-1', documentId: 'file-abc' });

    // No sprk_document create on mount — no save / promote calls (create-on-save is task 013).
    expect(urls.some(u => u.includes('/save') || u.includes('/promote'))).toBe(false);
  });

  it('surfaces an error banner when the retained bytes are gone (404)', async () => {
    authenticatedFetchMock.mockResolvedValueOnce({
      ok: false,
      status: 404,
      json: async () => ({}),
    });

    renderWorkspace();

    await waitFor(() => expect(screen.getByTestId('compose-workspace-error-empty')).toBeInTheDocument());
    expect(screen.queryByTestId('compose-editor-stub')).not.toBeInTheDocument();
  });

  it('does not fetch when a stored-document ref is also present (mutually exclusive)', async () => {
    renderWorkspace({
      initialDocumentRef: { speDriveItemId: 'spe-1', fileName: 'stored.docx' },
      driveId: 'drive-1',
    });

    // The upload effect must yield to the stored-document path — it must NOT call the
    // upload endpoint. (The stored-document Load path uses driveId 'drive-1'.)
    await waitFor(() => expect(authenticatedFetchMock).toHaveBeenCalled());
    const uploadCalls = authenticatedFetchMock.mock.calls.filter(([u]) => String(u).includes('/api/compose/upload'));
    expect(uploadCalls).toHaveLength(0);
  });
});
