/**
 * ComposeWorkspace.compose-outputs-404.test.tsx — UAT defect (spaarkeai-compose-r2)
 *
 * A recent SERVER change made `GET /api/ai/chat/sessions/{id}/compose-outputs` return a clean
 * 404 (instead of a 500) when the session has no compose outputs yet — the normal case for a
 * plain file/upload load where nothing has been drafted. The CLIENT defect was that the
 * draft-materialization path treated that 404 as a hard failure and surfaced
 * "Could not insert AI draft — Failed to insert drafted content: HTTP 404".
 *
 * Root cause: `authenticatedFetch` (@spaarke/auth) THROWS `ApiError` on any non-2xx — it never
 * returns a non-ok `Response`. The `if (!response.ok)` 404 guards in ComposeWorkspace were
 * therefore dead code; the 404 fell through to the catch block and became an error card. Every
 * prior ComposeWorkspace test mocked `authenticatedFetch` to RESOLVE a `{ ok:false, status:404 }`
 * Response, which is NOT how the real client behaves — that mock-fidelity gap is why the defect
 * shipped. This file mocks the FAITHFUL throw behaviour.
 *
 * Two cases:
 *   1. Non-draft mount (plain upload load): a 404 from compose-outputs is a silent no-op —
 *      NO error card, NO "Failed to insert drafted content" message. The editor stays mounted.
 *   2. Draft-SEED mount (a draft IS expected via `initialDraftRef.ledgerRef`): a 404 surfaces a
 *      SOFT, non-scary "no longer available" message and does NOT crash or leak the raw HTTP text.
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

// ── Fetch boundary (ADR-028) — FAITHFUL throw-on-non-2xx behaviour ──────────────
// `ApiError` mirrors the real @spaarke/auth class (message + numeric `status`) so the
// component's `err instanceof ApiError && err.status === 404` branch is exercised for real.
const mockApiError = class ApiError extends Error {
  public readonly status: number;
  constructor(message: string, status: number) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
  }
};
const authenticatedFetchMock = jest.fn();
jest.mock('@spaarke/auth', () => ({
  authenticatedFetch: (...args: unknown[]) => authenticatedFetchMock(...args),
  ApiError: mockApiError,
  useAuth: () => ({
    isAuthenticated: true,
    getAccessToken: async () => 'test-token',
    authenticatedFetch: (...args: unknown[]) => authenticatedFetchMock(...args),
    tenantId: 'test-tenant',
    logout: jest.fn(),
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

jest.mock('./ComposeToolbar', () => ({
  ComposeToolbar: () => <div data-testid="compose-toolbar-stub" />,
}));

jest.mock('./ComposeEditor', () => {
  const ReactLib = require('react');
  return {
    ComposeEditor: ReactLib.forwardRef(
      (props: { docxBytes: ArrayBuffer | null; initialHtml?: string | null }, ref: React.Ref<unknown>) => {
        ReactLib.useImperativeHandle(ref, () => ({
          serialize: async () => new ArrayBuffer(0),
          getCounts: () => ({ characters: 0, words: 0 }),
          isDirty: () => true,
          // The materialize handle MUST exist — otherwise the callback bails with a different
          // (handle-missing) message and would mask the 404-path assertion.
          materializeComposeDraft: () => undefined,
          materializeComposeEdits: () => undefined,
          materializePendingRedline: () => 'applied',
        }));
        return <div data-testid="compose-editor-stub" dangerouslySetInnerHTML={{ __html: props.initialHtml ?? '' }} />;
      }
    ),
  };
});

// eslint-disable-next-line import/first
import { ComposeWorkspace } from './ComposeWorkspace';
// The mocked ApiError class — throw instances of THIS so `instanceof` matches in the component.
// eslint-disable-next-line import/first
import { ApiError } from '@spaarke/auth';

function renderWorkspace(props: Partial<React.ComponentProps<typeof ComposeWorkspace>> = {}) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <ComposeWorkspace bffBaseUrl="https://bff.example.test" driveId="" tenantId="tenant-1" {...props} />
    </FluentProvider>
  );
}

beforeEach(() => {
  authenticatedFetchMock.mockReset();
});

describe('ComposeWorkspace — compose-outputs 404 is non-fatal', () => {
  it('non-draft (upload) mount: a 404 from compose-outputs is a silent no-op (no error card)', async () => {
    // Upload-serve succeeds → editor reaches 'loaded'; the refresh-durability effect then GETs
    // compose-outputs, which 404s (no AI draft on a plain file load) → authenticatedFetch throws.
    authenticatedFetchMock.mockImplementation(async (url: string) => {
      if (String(url).includes('/api/compose/upload')) {
        return { ok: true, status: 200, json: async () => ({ content: 'YWJj', fileName: 'draft.docx', size: 3 }) };
      }
      if (String(url).includes('/compose-outputs')) {
        throw new ApiError('HTTP 404', 404);
      }
      throw new ApiError('HTTP 404', 404);
    });

    renderWorkspace({ initialUploadRef: { sessionId: 'sess-1', sessionFileId: 'file-abc', fileName: 'draft.docx' } });

    // Editor mounts and stays mounted.
    await waitFor(() => expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument());

    // The compose-outputs GET must have actually fired (and thrown) — otherwise the assertion below
    // would be vacuously true.
    await waitFor(() =>
      expect(authenticatedFetchMock.mock.calls.some(([u]) => String(u).includes('/compose-outputs'))).toBe(true)
    );

    // The defect: this error card must NOT appear on a plain load.
    expect(screen.queryByTestId('compose-workspace-draft-error')).not.toBeInTheDocument();
    expect(screen.queryByText('Could not insert AI draft')).not.toBeInTheDocument();
    expect(screen.queryByText(/Failed to insert drafted content/)).not.toBeInTheDocument();
  });

  it('draft-seed mount: a 404 surfaces a SOFT message, not a raw HTTP crash', async () => {
    // A draft IS expected here (initialDraftRef.ledgerRef), but the ledger 404s.
    authenticatedFetchMock.mockImplementation(async (url: string) => {
      if (String(url).includes('/compose-outputs')) {
        throw new ApiError('HTTP 404', 404);
      }
      throw new ApiError('HTTP 404', 404);
    });

    renderWorkspace({ initialDraftRef: { ledgerRef: 'binding-1@t1', sessionId: 'sess-1' } });

    // Soft, non-scary loadFailed message — not the raw "HTTP 404" text, and no editor.
    await waitFor(() => expect(screen.getByTestId('compose-workspace-error-empty')).toBeInTheDocument());
    expect(screen.getByText(/no longer available/i)).toBeInTheDocument();
    expect(screen.queryByText(/HTTP 404/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Failed to load the drafted document: HTTP/)).not.toBeInTheDocument();
    expect(screen.queryByTestId('compose-editor-stub')).not.toBeInTheDocument();
  });
});
