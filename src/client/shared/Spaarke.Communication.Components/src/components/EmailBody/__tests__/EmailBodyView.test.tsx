/**
 * EmailBodyView.test.tsx
 *
 * Unit/RTL tests for `<EmailBodyView />` (email-communication-solution-r5 task
 * 033). Covers the closed acceptance set from the task POML:
 *   - `.eml` path renders the server HTML in a `sandbox=""` iframe (no
 *     `allow-scripts`, no `allow-same-origin`) — the NFR-03 boundary;
 *   - archive-less email degrades to client-sanitized `sprk_body` + a
 *     "full history unavailable" note with NO error banner;
 *   - malicious `sprk_body` (script / onerror / javascript:) executes nothing;
 *   - a non-2xx `eml-render` fetch fails soft to the degradation path;
 *   - header-first paint: the body shows a loading skeleton while the render is
 *     in flight (never blocks);
 *   - record-load failure shows an error + retry affordance;
 *   - dark-mode chrome (ADR-021) renders with no console errors.
 *
 * `@spaarke/auth` is jest-mocked (this package never calls `initAuth()`), so the
 * default `authenticatedFetch` import is a controllable spy — mirroring
 * `CommunicationsWorkspaceWidget.test.ts`.
 */
import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';

// Mock @spaarke/auth — the component imports the free-function `authenticatedFetch`
// directly (ADR-028). This package never initialises the auth provider, so the
// real function would throw; a module-level spy lets each test drive the response.
const mockAuthenticatedFetch = jest.fn();
jest.mock('@spaarke/auth', () => ({
  authenticatedFetch: (...args: [string, RequestInit?]) => mockAuthenticatedFetch(...args),
}));

import { EmailBodyView } from '../EmailBodyView';
import type { EmailBodyViewProps } from '../EmailBodyView.types';

function renderWithProvider(ui: React.ReactElement, theme = webLightTheme) {
  return render(<FluentProvider theme={theme}>{ui}</FluentProvider>);
}

function renderBody(overrides: Partial<EmailBodyViewProps> = {}, theme = webLightTheme) {
  return renderWithProvider(<EmailBodyView selectedId="c1" {...overrides} />, theme);
}

/** Minimal `Response`-like object for the `authenticatedFetch` mock. */
function okHtml(html: string): Partial<Response> {
  return { ok: true, status: 200, text: async () => html };
}

beforeEach(() => {
  mockAuthenticatedFetch.mockReset();
});

describe('EmailBodyView — .eml render branch (sandboxed iframe)', () => {
  it('fetches eml-render for the archive doc and renders the returned HTML in a sandbox="" iframe', async () => {
    mockAuthenticatedFetch.mockResolvedValue(
      okHtml('<html><body><blockquote>Quoted history</blockquote><img src="data:image/png;base64,AAAA"></body></html>')
    );

    renderBody({ emlDocumentId: 'eml-doc-1' });

    const iframe = await screen.findByTestId('email-body-iframe');
    // NFR-03: sandbox present and EMPTY — no allow-scripts, no allow-same-origin.
    expect(iframe).toHaveAttribute('sandbox', '');
    const sandbox = iframe.getAttribute('sandbox') ?? '';
    expect(sandbox).not.toContain('allow-scripts');
    expect(sandbox).not.toContain('allow-same-origin');
    // Server HTML injected via srcDoc (not dangerouslySetInnerHTML).
    expect(iframe.getAttribute('srcdoc')).toContain('Quoted history');
    expect(iframe.getAttribute('srcdoc')).toContain('data:image/png');

    // Called the relative eml-render path for the resolved archive doc id.
    expect(mockAuthenticatedFetch).toHaveBeenCalledWith('/documents/eml-doc-1/eml-render');
    // No degradation note on the happy .eml path.
    expect(screen.queryByTestId('email-body-fallback-note')).not.toBeInTheDocument();
  });

  it('shows a loading skeleton while the render is in flight, then swaps in the iframe (header-first, NFR-02)', async () => {
    let resolveFetch: (r: Partial<Response>) => void = () => {};
    mockAuthenticatedFetch.mockReturnValue(
      new Promise<Partial<Response>>(resolve => {
        resolveFetch = resolve;
      })
    );

    renderBody({ emlDocumentId: 'eml-doc-1' });

    // Body skeleton visible while awaiting the render — the body does not block.
    expect(screen.getByTestId('email-body-loading')).toBeInTheDocument();
    expect(screen.queryByTestId('email-body-iframe')).not.toBeInTheDocument();

    resolveFetch(okHtml('<p>rendered</p>'));

    expect(await screen.findByTestId('email-body-iframe')).toBeInTheDocument();
    expect(screen.queryByTestId('email-body-loading')).not.toBeInTheDocument();
  });

  it('the .eml iframe never carries allow-scripts or allow-same-origin even for a sanitizer-bypass payload', async () => {
    // Even if the SERVER HTML contained a script, the sandbox neutralises it —
    // we assert the boundary attribute, the second line of defense (NFR-03).
    mockAuthenticatedFetch.mockResolvedValue(okHtml('<script>window.__evil = 1;</script><p>hi</p>'));

    renderBody({ emlDocumentId: 'eml-doc-1' });

    const iframe = await screen.findByTestId('email-body-iframe');
    const sandbox = iframe.getAttribute('sandbox') ?? 'MISSING';
    expect(sandbox).toBe('');
    expect(sandbox).not.toMatch(/allow-scripts|allow-same-origin/);
    // A `sandbox=""` iframe with no allow-scripts cannot run the script.
    expect((window as unknown as { __evil?: number }).__evil).toBeUndefined();
  });
});

describe('EmailBodyView — degradation branch (sprk_body, no archive)', () => {
  it('degrades to client-sanitized sprk_body with a "full history unavailable" note and NO error, without fetching', () => {
    renderBody({ emlDocumentId: null, body: '<p>Latest message body</p>' });

    // Degradation is a normal state: note shown, no error, no iframe, no fetch.
    expect(screen.getByTestId('email-body-fallback-note')).toBeInTheDocument();
    expect(screen.getByText('Full history unavailable')).toBeInTheDocument();
    expect(screen.getByText('Latest message body')).toBeInTheDocument();
    expect(screen.queryByTestId('email-body-error')).not.toBeInTheDocument();
    expect(screen.queryByTestId('email-body-iframe')).not.toBeInTheDocument();
    expect(mockAuthenticatedFetch).not.toHaveBeenCalled();
  });

  it('neutralises malicious HTML in sprk_body (script / onerror / javascript:) — no script executes', () => {
    const malicious =
      '<p>safe text</p>' +
      '<script>window.__xss = 1;</script>' +
      '<img src="x" onerror="window.__xss2 = 1">' +
      '<a href="javascript:window.__xss3 = 1">click me</a>';

    const { container } = renderBody({ emlDocumentId: undefined, body: malicious });

    const content = screen.getByTestId('email-body-fallback-content');
    // Visible text survives; the payload is stripped by sanitizeEmailHtml.
    expect(content).toHaveTextContent('safe text');
    expect(container.querySelector('script')).toBeNull();
    expect(content.querySelector('[onerror]')).toBeNull();
    const anchor = content.querySelector('a');
    if (anchor) {
      expect(anchor.getAttribute('href') ?? '').not.toMatch(/javascript:/i);
    }
    // Nothing executed.
    const w = window as unknown as Record<string, unknown>;
    expect(w.__xss).toBeUndefined();
    expect(w.__xss2).toBeUndefined();
    expect(w.__xss3).toBeUndefined();
  });

  it('renders the note even when sprk_body is empty', () => {
    renderBody({ emlDocumentId: null, body: '' });
    expect(screen.getByTestId('email-body-fallback-note')).toBeInTheDocument();
    expect(screen.queryByTestId('email-body-error')).not.toBeInTheDocument();
  });
});

describe('EmailBodyView — fail-soft on eml-render failure', () => {
  it('degrades to the sprk_body path when the eml-render fetch throws (non-2xx / network)', async () => {
    // authenticatedFetch throws ApiError on non-2xx; simulate a rejection.
    mockAuthenticatedFetch.mockRejectedValue(new Error('HTTP 500'));

    renderBody({ emlDocumentId: 'eml-doc-1', body: '<p>fallback body</p>' });

    expect(await screen.findByTestId('email-body-fallback-note')).toBeInTheDocument();
    expect(screen.getByText('fallback body')).toBeInTheDocument();
    // Fail-soft to fallback, not a crash and not an error banner.
    expect(screen.queryByTestId('email-body-error')).not.toBeInTheDocument();
    expect(screen.queryByTestId('email-body-iframe')).not.toBeInTheDocument();
  });

  it('degrades when the response is defensively non-ok', async () => {
    mockAuthenticatedFetch.mockResolvedValue({ ok: false, status: 404, text: async () => '' });

    renderBody({ emlDocumentId: 'eml-doc-1', body: '<p>fallback body</p>' });

    expect(await screen.findByTestId('email-body-fallback-note')).toBeInTheDocument();
  });
});

describe('EmailBodyView — record-load error (retry)', () => {
  it('shows an error with a retry affordance ONLY for a record-load failure', () => {
    const onRetryRecord = jest.fn();
    renderBody({ recordLoadError: true, onRetryRecord });

    expect(screen.getByTestId('email-body-error')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(onRetryRecord).toHaveBeenCalledTimes(1);
    // Not a fetch and not a degradation.
    expect(mockAuthenticatedFetch).not.toHaveBeenCalled();
    expect(screen.queryByTestId('email-body-fallback-note')).not.toBeInTheDocument();
  });
});

describe('EmailBodyView — dark mode (ADR-021)', () => {
  it('renders the note + skeleton + error chrome under a dark FluentProvider with no console errors', async () => {
    const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});

    // Degradation note in dark mode.
    const fallback = renderBody({ emlDocumentId: null, body: '<p>x</p>' }, webDarkTheme);
    expect(screen.getByTestId('email-body-fallback-note')).toBeInTheDocument();
    fallback.unmount();

    // Error/retry chrome in dark mode.
    const err = renderBody({ recordLoadError: true, onRetryRecord: jest.fn() }, webDarkTheme);
    expect(screen.getByTestId('email-body-error')).toBeInTheDocument();
    err.unmount();

    expect(errorSpy).not.toHaveBeenCalled();
    errorSpy.mockRestore();
  });
});
