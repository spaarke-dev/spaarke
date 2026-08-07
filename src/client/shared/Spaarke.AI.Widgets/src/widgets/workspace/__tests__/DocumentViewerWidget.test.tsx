/**
 * DocumentViewerWidget — unit tests
 *
 * R5 task 022 (D2-08 follow-on) upgrade — the R4 monospace-text-preview shim
 * has been replaced with consumption of the canonical `RichFilePreview`
 * renderer from `@spaarke/ui-components`. These tests verify:
 *
 *   (a) The widget mounts `<RichFilePreview />` (via the mocked renderer).
 *   (b) Back-compat — R4-style text-only payloads (no `previewUrl`,
 *       no `fetchPreviewUrl`) still mount without crashing; the renderer
 *       receives a `fetchPreviewUrl` closure that resolves to null. The R4
 *       monospace `<pre>` shim is GONE (negative assertion).
 *   (c) R5 payload — when `previewUrl` is supplied, the renderer receives a
 *       `fetchPreviewUrl` closure that resolves to that URL.
 *   (d) R5 payload — when `fetchPreviewUrl` is supplied directly, it is
 *       passed through unchanged (caller-owned closure takes precedence).
 *   (e) Defensive narrowing — invalid `widgetData` does not crash; renderer
 *       mounts with `documentName = 'Unknown file'` + null-resolving fetch.
 *   (f) Envelope props — `isLoading` / `error` short-circuit before the
 *       renderer mounts (the surrounding host envelope, not the renderer's
 *       own loading/error states).
 *   (g) Payload mapping — sizeBytes / documentType / createdBy / createdAt
 *       propagate to the renderer props.
 *
 * Mocking strategy: `@spaarke/ui-components` is mocked at the module
 * boundary (same pattern as `WorkspaceLayoutWidget.test.tsx` per R4 task
 * 068 — Jest 30 + React 19 environment). We replace `RichFilePreview` with
 * a lightweight test stub that records the props it receives and renders a
 * stable test-id so the existence assertion is straightforward.
 *
 * Registry-resolution test lives in
 * `__tests__/register-document-viewer-widget.test.ts` so it can verify
 * the side-effect import path without depending on the React tree.
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen } from '@testing-library/react';
import type { IRichFilePreviewProps } from '@spaarke/ui-components';
import type { WorkspaceWidgetProps } from '../../../types/widget-types';

// ---------------------------------------------------------------------------
// Mock the shared library at the module boundary so we can capture the props
// the widget passes into the renderer. Keep the mock minimal — the renderer
// itself is covered by `Spaarke.UI.Components/__tests__/RichFilePreview.test.tsx`.
// ---------------------------------------------------------------------------

const mockRichFilePreview = jest.fn();

jest.mock('@spaarke/ui-components', () => ({
  RichFilePreview: (props: IRichFilePreviewProps) => {
    mockRichFilePreview(props);
    return (
      <div data-testid="rich-file-preview-mock" data-document-name={props.documentName}>
        RichFilePreview Mock — {props.documentName}
      </div>
    );
  },
  DEFAULT_RICH_FILE_PREVIEW_DISABLED_ACTIONS: Object.freeze(['preview', 'aiSummary', 'toggleWorkspace', 'rename']),
}));

// Mock the AiSessionProvider module to expose a real (lightweight) React
// context under the SAME import specifier the widget consumes, so tests can
// supply an ambient `{ bffBaseUrl, authenticatedFetch }` auth surface (or omit
// it) without evaluating the full provider tree. `require('react')` inside the
// factory keeps it hoist-safe.
jest.mock('../../../providers/AiSessionProvider', () => {
  // eslint-disable-next-line @typescript-eslint/no-var-requires
  const R = require('react');
  return { AiSessionContext: R.createContext(null) };
});

// Import widget AFTER mock so the mock is wired before module evaluation.
import DocumentViewerWidget, { type DocumentViewerWidgetData } from '../DocumentViewerWidget';
import { AiSessionContext } from '../../../providers/AiSessionProvider';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function renderWidget(
  data: WorkspaceWidgetProps<DocumentViewerWidgetData>['data'],
  overrides: Partial<WorkspaceWidgetProps<DocumentViewerWidgetData>> = {}
) {
  return render(<DocumentViewerWidget data={data} widgetType="document-viewer" {...overrides} />);
}

function lastRendererProps(): IRichFilePreviewProps {
  expect(mockRichFilePreview).toHaveBeenCalled();
  const calls = mockRichFilePreview.mock.calls;
  return calls[calls.length - 1][0] as IRichFilePreviewProps;
}

// Silence the dev-time "no handler supplied" console.warn from the default
// action callbacks — these are intentional signals to future R5 dispatch
// sites (tasks 020 / 021), not test failures.
let consoleWarnSpy: jest.SpyInstance;
beforeAll(() => {
  consoleWarnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
});
afterAll(() => {
  consoleWarnSpy.mockRestore();
});
beforeEach(() => {
  mockRichFilePreview.mockClear();
});

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('DocumentViewerWidget — RichFilePreview composition (R5 task 022)', () => {
  it('mounts <RichFilePreview /> inside the widget envelope', () => {
    renderWidget({
      filename: 'Contract.pdf',
      contentType: 'application/pdf',
      sizeBytes: 1024 * 1024,
      textContent: 'Hello world',
    });

    // Envelope is present
    expect(screen.getByTestId('document-viewer-widget')).toBeInTheDocument();

    // Renderer mock is mounted (proves the widget consumes the extracted
    // renderer rather than rebuilding preview chrome)
    expect(screen.getByTestId('rich-file-preview-mock')).toBeInTheDocument();
    expect(mockRichFilePreview).toHaveBeenCalledTimes(1);
  });

  it('does NOT regress to the R4 monospace <pre> shim', () => {
    // R4 shim used data-testid="document-viewer-preview" on the <pre>.
    // After R5 task 022 the renderer subtree replaces that element entirely.
    renderWidget({
      filename: 'Contract.pdf',
      contentType: 'application/pdf',
      textContent: 'monospace content that used to render in a <pre>',
    });

    expect(screen.queryByTestId('document-viewer-preview')).not.toBeInTheDocument();
    // The widget root still exists; no monospace shim.
    expect(screen.getByTestId('document-viewer-widget')).toBeInTheDocument();
  });

  it('forwards widgetType to the data-widget-type attribute', () => {
    renderWidget({
      filename: 'memo.docx',
      contentType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      textContent: 'x',
    });

    expect(screen.getByTestId('document-viewer-widget')).toHaveAttribute('data-widget-type', 'document-viewer');
  });
});

describe('DocumentViewerWidget — back-compat with R4 text-only payloads', () => {
  it('mounts the renderer with a null-resolving fetchPreviewUrl when no URL source is provided', async () => {
    renderWidget({
      filename: 'no-preview-source.pdf',
      contentType: 'application/pdf',
      sizeBytes: 1024,
      textContent: 'in-memory text only — R4 dispatch style',
    });

    const props = lastRendererProps();
    expect(props.documentName).toBe('no-preview-source.pdf');
    // Renderer received a callable closure; calling it resolves to null so
    // the renderer's empty/error state surfaces (per task 013 acceptance).
    await expect(props.fetchPreviewUrl()).resolves.toBeNull();
  });

  it('synthesizes a documentId from the filename when omitted (R4 payload)', () => {
    renderWidget({
      filename: 'r4-text-only.txt',
      contentType: 'text/plain',
      textContent: 'some text',
    });

    const props = lastRendererProps();
    expect(props.documentId).toBe('document-viewer:r4-text-only.txt');
  });
});

describe('DocumentViewerWidget — R5 payload with preview URL source', () => {
  it('wraps a static previewUrl into a fetchPreviewUrl closure', async () => {
    renderWidget({
      filename: 'rich.pdf',
      contentType: 'application/pdf',
      textContent: '',
      documentId: 'doc-123',
      previewUrl: 'https://example.test/preview/doc-123',
    });

    const props = lastRendererProps();
    expect(props.documentId).toBe('doc-123');
    await expect(props.fetchPreviewUrl()).resolves.toBe('https://example.test/preview/doc-123');
  });

  it('passes a caller-supplied fetchPreviewUrl closure through unchanged', async () => {
    const fetcher = jest.fn(async () => 'https://example.test/from-closure');
    renderWidget({
      filename: 'closure.pdf',
      contentType: 'application/pdf',
      textContent: '',
      documentId: 'doc-closure',
      fetchPreviewUrl: fetcher,
    });

    const props = lastRendererProps();
    // The widget should pass the same function reference through (not a new
    // wrapper) so the caller's async/auth semantics are preserved.
    expect(props.fetchPreviewUrl).toBe(fetcher);
    await expect(props.fetchPreviewUrl()).resolves.toBe('https://example.test/from-closure');
    expect(fetcher).toHaveBeenCalledTimes(1);
  });

  it('prefers fetchPreviewUrl over previewUrl when both are supplied', async () => {
    const fetcher = jest.fn(async () => 'https://example.test/from-closure-priority');
    renderWidget({
      filename: 'both.pdf',
      contentType: 'application/pdf',
      textContent: '',
      documentId: 'doc-both',
      previewUrl: 'https://example.test/static-should-be-ignored',
      fetchPreviewUrl: fetcher,
    });

    const props = lastRendererProps();
    expect(props.fetchPreviewUrl).toBe(fetcher);
    await expect(props.fetchPreviewUrl()).resolves.toBe('https://example.test/from-closure-priority');
  });

  it('propagates documentType / createdBy / createdAt / sizeBytes to renderer props', () => {
    renderWidget({
      filename: 'rich-meta.pdf',
      contentType: 'application/pdf',
      sizeBytes: 4096,
      textContent: '',
      documentId: 'doc-meta',
      documentType: 'Contract',
      createdBy: 'Alice',
      createdAt: '2026-01-15T00:00:00Z',
      previewUrl: 'https://example.test/meta',
    });

    const props = lastRendererProps();
    expect(props.documentType).toBe('Contract');
    expect(props.createdBy).toBe('Alice');
    expect(props.createdAt).toBe('2026-01-15T00:00:00Z');
    expect(props.fileSize).toBe(4096);
  });

  it('normalizes optional metadata fields when omitted', () => {
    renderWidget({
      filename: 'minimal.pdf',
      contentType: 'application/pdf',
      textContent: '',
    });

    const props = lastRendererProps();
    expect(props.documentType).toBeUndefined();
    expect(props.createdBy).toBeNull();
    expect(props.createdAt).toBeNull();
    expect(props.fileSize).toBeNull();
  });
});

describe('DocumentViewerWidget — envelope props (isLoading / error)', () => {
  it('renders the envelope loading state without mounting the renderer', () => {
    renderWidget(
      {
        filename: 'pending.pdf',
        contentType: 'application/pdf',
        textContent: 'will not render',
      },
      { isLoading: true }
    );

    expect(screen.getByText(/Loading preview/i)).toBeInTheDocument();
    expect(screen.queryByTestId('rich-file-preview-mock')).not.toBeInTheDocument();
    expect(mockRichFilePreview).not.toHaveBeenCalled();
  });

  it('renders the envelope error state without mounting the renderer', () => {
    renderWidget(
      {
        filename: 'broken.pdf',
        contentType: 'application/pdf',
        textContent: 'will not render',
      },
      { error: 'Dispatch payload failed to resolve' }
    );

    expect(screen.getByText('Dispatch payload failed to resolve')).toBeInTheDocument();
    expect(screen.queryByTestId('rich-file-preview-mock')).not.toBeInTheDocument();
    expect(mockRichFilePreview).not.toHaveBeenCalled();
  });
});

describe('DocumentViewerWidget — defensive narrowing', () => {
  it('renders without crashing when widgetData fails the type guard', async () => {
    // Cast through unknown to bypass the prop type and simulate an upstream
    // dispatcher sending a wrong-shaped payload (e.g. legacy widget_load
    // signal that hasn't been migrated). The widget should still mount the
    // renderer with synthesized fallback values.
    const result = renderWidget({ foo: 'bar' } as unknown as DocumentViewerWidgetData);
    expect(result.container).toBeTruthy();

    const props = lastRendererProps();
    expect(props.documentName).toBe('Unknown file');
    expect(props.documentId).toBe('document-viewer:unknown');
    await expect(props.fetchPreviewUrl()).resolves.toBeNull();
  });
});

// ---------------------------------------------------------------------------
// FR-D restore defect (owner UAT 2026-08-07) — a restored document tab must
// re-derive its preview from the STABLE documentId via the BFF, NEVER reuse a
// persisted/stale `blob:` object-URL (dead after reload → ERR_FILE_NOT_FOUND),
// and the load path must RESOLVE (never spin forever).
// ---------------------------------------------------------------------------

interface SessionStub {
  bffBaseUrl?: string;
  authenticatedFetch?: (url: string, init?: RequestInit) => Promise<Response>;
}

function renderWithSession(data: WorkspaceWidgetProps<DocumentViewerWidgetData>['data'], session: SessionStub | null) {
  const tree = (
    <AiSessionContext.Provider value={session as unknown as never}>
      <DocumentViewerWidget data={data} widgetType="document-viewer" />
    </AiSessionContext.Provider>
  );
  return render(tree);
}

describe('DocumentViewerWidget — FR-D restore: re-derive from documentId, never reuse a blob', () => {
  it('re-fetches a FRESH preview URL from documentId when the persisted previewUrl is a dead blob URL', async () => {
    const deadBlob = 'blob:https://spaarkedev1.crm.dynamics.com/1111-dead-uuid';
    const calledUrls: string[] = [];
    const authenticatedFetch = jest.fn(async (url: string) => {
      calledUrls.push(url);
      return {
        ok: true,
        json: async () => ({ previewUrl: 'https://bff.example/preview/fresh-doc-xyz' }),
      } as unknown as Response;
    });

    // Restored shape: live fetchPreviewUrl closure stripped by JSON; only the
    // stable id + a now-dead blob previewUrl survive.
    renderWithSession(
      {
        filename: 'nda.docx',
        contentType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
        textContent: '',
        documentId: 'doc-xyz',
        previewUrl: deadBlob,
      },
      { bffBaseUrl: 'https://bff.example', authenticatedFetch }
    );

    const props = lastRendererProps();
    const resolved = await props.fetchPreviewUrl();

    // Re-derived a fresh server URL — NOT the dead blob.
    expect(resolved).toBe('https://bff.example/preview/fresh-doc-xyz');
    expect(resolved).not.toBe(deadBlob);
    // Fetched via the stable documentId against the existing BFF surface.
    expect(authenticatedFetch).toHaveBeenCalledTimes(1);
    expect(calledUrls[0]).toContain('/api/documents/doc-xyz/preview-url');
    // The dead blob URL is never fetched.
    expect(calledUrls.some(u => u.startsWith('blob:'))).toBe(false);
  });

  it('re-fetches from documentId when the live fetchPreviewUrl closure is absent (restore)', async () => {
    const authenticatedFetch = jest.fn(
      async () =>
        ({
          ok: true,
          json: async () => ({ previewUrl: 'https://bff.example/preview/restored' }),
        }) as unknown as Response
    );

    renderWithSession(
      {
        filename: 'restored.pdf',
        contentType: 'application/pdf',
        textContent: '',
        documentId: 'doc-restored',
      },
      { bffBaseUrl: 'https://bff.example', authenticatedFetch }
    );

    const props = lastRendererProps();
    await expect(props.fetchPreviewUrl()).resolves.toBe('https://bff.example/preview/restored');
  });

  it('RESOLVES to null (renderer error state, no infinite spinner) when the re-fetch fails', async () => {
    const authenticatedFetch = jest.fn(async () => ({ ok: false, status: 404 }) as unknown as Response);

    renderWithSession(
      {
        filename: 'gone.pdf',
        contentType: 'application/pdf',
        textContent: '',
        documentId: 'doc-gone',
      },
      { bffBaseUrl: 'https://bff.example', authenticatedFetch }
    );

    const props = lastRendererProps();
    // Resolves (does not hang) → RichFilePreview surfaces "preview not available".
    await expect(props.fetchPreviewUrl()).resolves.toBeNull();
  });

  it('never returns a blob even if the server response itself is ephemeral (defensive)', async () => {
    const authenticatedFetch = jest.fn(
      async () =>
        ({
          ok: true,
          json: async () => ({ previewUrl: 'blob:https://spaarkedev1.crm.dynamics.com/2222' }),
        }) as unknown as Response
    );

    renderWithSession(
      { filename: 'x.pdf', contentType: 'application/pdf', textContent: '', documentId: 'doc-x' },
      { bffBaseUrl: 'https://bff.example', authenticatedFetch }
    );

    const props = lastRendererProps();
    await expect(props.fetchPreviewUrl()).resolves.toBeNull();
  });

  it('treats a blob previewUrl as absent even with NO session (resolves null, never reuses the blob)', async () => {
    // No AiSessionContext provider → no re-fetch possible → must fall back to
    // null (NOT the dead blob).
    renderWidget({
      filename: 'no-session.pdf',
      contentType: 'application/pdf',
      textContent: '',
      documentId: 'doc-no-session',
      previewUrl: 'blob:https://spaarkedev1.crm.dynamics.com/3333',
    });

    const props = lastRendererProps();
    await expect(props.fetchPreviewUrl()).resolves.toBeNull();
  });
});

describe('DocumentViewerWidget — default action callbacks', () => {
  it('supplies no-op callbacks for the 3-dot menu actions (renderer still mounts)', () => {
    renderWidget({
      filename: 'actions.pdf',
      contentType: 'application/pdf',
      textContent: '',
    });

    const props = lastRendererProps();
    expect(typeof props.onOpenFile).toBe('function');
    expect(typeof props.onOpenRecord).toBe('function');
    expect(typeof props.onEmailDocument).toBe('function');
    expect(typeof props.onCopyLink).toBe('function');

    // None should throw when invoked (back-compat — R4 dispatch sites do
    // not wire these; future R5 dispatch sites will).
    expect(() => props.onOpenFile('desktop')).not.toThrow();
    expect(() => props.onOpenRecord()).not.toThrow();
    expect(() => props.onEmailDocument()).not.toThrow();
    expect(() => props.onCopyLink()).not.toThrow();
  });
});
