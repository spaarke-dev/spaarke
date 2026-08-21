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
  // FR-S09 sweep (r8 task 016): the REAL failure shape. `authenticatedFetch` returns only when
  // `response.ok` and THROWS a typed `ApiError` on every non-2xx (ADR-028).
  ApiError: class ApiError extends Error {
    public readonly status: number;
    public readonly problemDetails: Record<string, unknown> | null;
    constructor(message: string, status: number, problemDetails: Record<string, unknown> | null = null) {
      super(message);
      this.name = 'ApiError';
      this.status = status;
      this.problemDetails = problemDetails;
    }
  },
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

// ── ComposeEditor — capture the docxBytes + projection it is handed ────────
const editorDocxBytes: { current: ArrayBuffer | null | undefined } = { current: undefined };
// FR-02 regression guard (task 012): captures the `projection` prop so a future change that
// silently drops the server projection on the transient upload-mount door (reintroducing
// `projection: null` even when the BFF response carries one) fails a test instead of shipping
// unnoticed. See the "hydrates a non-null projection" test below.
// eslint-disable-next-line @typescript-eslint/no-explicit-any
const editorProjection: { current: any } = { current: undefined };
jest.mock('./ComposeEditor', () => {
  const ReactLib = require('react');
  return {
    ComposeEditor: ReactLib.forwardRef(
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (props: { docxBytes: ArrayBuffer | null; projection?: any }, ref: React.Ref<unknown>) => {
        editorDocxBytes.current = props.docxBytes;
        editorProjection.current = props.projection;
        ReactLib.useImperativeHandle(ref, () => ({
          serialize: async () => new ArrayBuffer(0),
          getCounts: () => ({ characters: 0, words: 0 }),
          isDirty: () => true,
          materializeComposeDraft: () => undefined,
          materializePendingRedline: () => 'applied',
        }));
        return <div data-testid="compose-editor-stub" />;
      }
    ),
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
  // Benign default for any secondary call (e.g. the FR-04 `compose-outputs` GET fired when the editor
  // reaches 'loaded' — no AI drafts for a fresh upload). FR-S09 sweep (r8 task 016): a REJECTION, not
  // a `{ ok: false }` Response — the latter is a shape `authenticatedFetch` never produces, and mocking
  // it is how unreachable branches keep passing their tests.
  authenticatedFetchMock.mockImplementation(async () => {
    const { ApiError } = jest.requireMock('@spaarke/auth') as { ApiError: new (m: string, s: number) => Error };
    throw new ApiError('HTTP 404', 404);
  });
  editorDocxBytes.current = undefined;
  editorProjection.current = undefined;
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

  // FR-02 regression guard (task 012 audit finding): task 010 already wires this door to hydrate
  // `projection` from POST /api/compose/upload's response instead of hardcoding `null`. This test
  // proves that end-to-end at the CLIENT level (not just the server seam) — if a future change to
  // `mountTransient`'s dispatch, the reducer, or this effect's `normalizeProjection` call silently
  // drops a present server projection back to `null`, this test fails instead of the regression
  // shipping unnoticed. Mirrors task 010/011's server-side seam proof
  // (`ComposeUploadProjectionSeamTests.cs`) at the client boundary those tests cannot reach.
  it('hydrates a non-null projection from the upload response (FR-02 one-reader regression guard)', async () => {
    authenticatedFetchMock.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({
        content: 'YWJj',
        fileName: 'draft.docx',
        size: 3,
        projection: {
          status: 'success',
          canEdit: true,
          html: '<p>Hello</p>',
          warnings: [],
          schemaVersion: 'compose-html-v1',
        },
      }),
    });

    renderWorkspace();

    await waitFor(() => expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument());

    // The editor must take the projection branch (never `projection: null`, which would force
    // the deleted-per-F-2 `mammoth` fallback).
    expect(editorProjection.current).not.toBeNull();
    expect(editorProjection.current).not.toBeUndefined();
    expect(editorProjection.current?.status).toBe('success');
    expect(editorProjection.current?.canEdit).toBe(true);
    expect(editorProjection.current?.html).toBe('<p>Hello</p>');
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
    // FR-S09 sweep (r8 task 016): this used to mock `{ ok: false, status: 404 }` — a shape
    // `authenticatedFetch` CANNOT return. It passed only because the workspace still carried an
    // unreachable `if (!response.ok)` block; delete the dead code and the test goes red, which is
    // exactly what a test validating unreachable behaviour should do. A real 404 is a THROWN ApiError.
    const { ApiError } = jest.requireMock('@spaarke/auth') as {
      ApiError: new (m: string, s: number) => Error;
    };
    authenticatedFetchMock.mockRejectedValueOnce(new ApiError('HTTP 404', 404));

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

describe('ComposeWorkspace — FR-S09 sweep: the LOAD path routes on the THROWN status', () => {
  // The load path carried the same dead-`!response.ok` defect FR-S01 removed from the save path and
  // FR-S09 item 4 removed from the checkout path. Its 404 and 403 copy — the two messages that
  // actually tell a user what happened — sat inside a branch that could not execute, so every failed
  // load rendered `Failed to load document: HTTP 404`.
  const apiError = (status: number): Error => {
    const { ApiError } = jest.requireMock('@spaarke/auth') as { ApiError: new (m: string, s: number) => Error };
    return new ApiError(`HTTP ${status}`, status);
  };

  const renderStoredDoc = () =>
    renderWorkspace({
      initialUploadRef: undefined,
      initialDocumentRef: { speDriveItemId: 'spe-1', fileName: 'contract.docx' },
      driveId: 'drive-1',
    });

  it('a 404 says the document was deleted or moved — not "HTTP 404"', async () => {
    authenticatedFetchMock.mockRejectedValue(apiError(404));
    renderStoredDoc();
    const banner = await screen.findByTestId('compose-workspace-error-empty');
    expect(banner.textContent ?? '').toMatch(/deleted or moved/i);
    expect(banner.textContent ?? '').not.toMatch(/Failed to load document: HTTP/);
  });

  it('a 403 says you lack permission — the distinction the generic message erased', async () => {
    authenticatedFetchMock.mockRejectedValue(apiError(403));
    renderStoredDoc();
    const banner = await screen.findByTestId('compose-workspace-error-empty');
    expect(banner.textContent ?? '').toMatch(/do not have permission/i);
  });

  it('an unrecognised status still names it, and a transport failure says so instead', async () => {
    authenticatedFetchMock.mockRejectedValue(apiError(500));
    const { unmount } = renderStoredDoc();
    let banner = await screen.findByTestId('compose-workspace-error-empty');
    expect(banner.textContent ?? '').toMatch(/HTTP 500/);
    unmount();

    // No HTTP exchange at all (offline / DNS / CORS) — a different thing, said differently.
    authenticatedFetchMock.mockRejectedValue(new Error('Failed to fetch'));
    renderStoredDoc();
    banner = await screen.findByTestId('compose-workspace-error-empty');
    expect(banner.textContent ?? '').toMatch(/Failed to fetch/);
  });
});
