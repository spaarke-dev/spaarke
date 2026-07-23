/**
 * CommunicationsWorkspaceWidget — upgrade-in-place tests (messaging-communication-app-r3
 * task 031, FR-14a).
 *
 * The widget's body was swapped from the Pattern D filter-chip/card-strip/
 * DataGrid content (messaging-communication-app-r2 task 030 — see git history
 * for the prior version of this file, which unit-tested the
 * `aggregateChannelCounts`/`buildCommunicationsHostFilters` pure helpers that
 * were removed with the DataGrid body) to the shared two-pane conversation
 * shell (`<ConversationWorkspace>` + `<ConversationView>`, tasks 011/012).
 *
 * These tests verify the upgrade-in-place contract (NFR-06):
 *   - the widget mounts the REAL shared `<ConversationWorkspace>` — record-less
 *     / all mode (no `regarding` prop) — not a re-implementation;
 *   - the all-mode FR-16 endpoint is the ONLY thread-list endpoint the widget
 *     ever calls (never `by-regarding`) — confirms no forced regarding filter;
 *   - the widget survives both Fluent v9 themes (ADR-021);
 *   - the deprecated `configId` prop is still accepted so
 *     `communications.registration.ts`'s existing dual-use call site keeps
 *     compiling/rendering unchanged.
 *
 * The registry-identity assertions (type string `communications-list`
 * unchanged, section id `communications` unchanged, no second registry entry)
 * live in `@spaarke/ai-widgets`'s
 * `src/widgets/workspace/__tests__/register-workspace-widgets.test.ts` — this
 * file only covers the widget's own rendering contract.
 *
 * `@spaarke/auth` is jest-mocked — this package never calls `initAuth()`
 * itself (that's the HOST app's job, e.g. SpaarkeAi's `main.tsx`), so the real
 * `authenticatedFetch` would throw (no auth provider configured) if imported
 * unmocked in a test harness with no Xrm/MSAL context.
 */

import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';

// ---------------------------------------------------------------------------
// Mock @spaarke/auth — the widget imports the free-function `authenticatedFetch`
// directly (ADR-028); mock it so tests never attempt a real MSAL token
// acquisition. Declared before importing the widget under test.
// ---------------------------------------------------------------------------

// jsdom has no global `Response` constructor — build a minimal Response-shaped
// object instead (mirrors `ConversationWorkspace.test.tsx`'s `jsonResponse` helper).
function jsonResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: { get: () => 'application/json' },
    json: async () => body,
    text: async () => JSON.stringify(body),
  } as unknown as Response;
}

const mockAuthenticatedFetch = jest.fn(async (url: string) => {
  if (/\/api\/communications\/threads(\?|$)/.test(url)) {
    return jsonResponse({ threads: [], count: 0, nextPageToken: null, hasMore: false });
  }
  return jsonResponse({ title: 'Not Found' }, 404);
});

jest.mock('@spaarke/auth', () => ({
  __esModule: true,
  authenticatedFetch: (...args: [string, RequestInit?]) => mockAuthenticatedFetch(...args),
}));

import { CommunicationsWorkspaceWidget } from './CommunicationsWorkspaceWidget';

function renderWidget(theme: typeof webLightTheme = webLightTheme, props: { configId?: string } = {}) {
  return render(
    React.createElement(FluentProvider, { theme }, React.createElement(CommunicationsWorkspaceWidget, props))
  );
}

beforeEach(() => {
  mockAuthenticatedFetch.mockClear();
});

describe('CommunicationsWorkspaceWidget — upgrade in place (FR-14a, NFR-06)', () => {
  it('mounts the shared ConversationWorkspace two-pane shell (thread list + conversation pane)', async () => {
    renderWidget();

    // ThreadList's "New thread" create control is the shared component's own
    // chrome — its presence confirms ConversationWorkspace mounted, not a
    // re-implemented list. (The thread text-filter input was removed in the
    // task-062 Teams-style redesign — no longer asserted here.)
    await waitFor(() => expect(screen.getByRole('button', { name: 'New thread' })).toBeInTheDocument());
  });

  it('renders record-less / all mode — calls the FR-16 all-mode endpoint, never by-regarding', async () => {
    renderWidget();

    await waitFor(() => expect(mockAuthenticatedFetch).toHaveBeenCalled());

    const calls = mockAuthenticatedFetch.mock.calls.map(([u]) => u as string);
    expect(calls.some(u => /\/api\/communications\/threads(\?|$)/.test(u))).toBe(true);
    expect(calls.some(u => u.includes('/by-regarding/'))).toBe(false);
  });

  it('renders under both Fluent v9 themes without crashing (ADR-021)', async () => {
    const { unmount } = renderWidget(webLightTheme);
    await waitFor(() => expect(screen.getByRole('button', { name: 'New thread' })).toBeInTheDocument());
    unmount();

    renderWidget(webDarkTheme);
    await waitFor(() => expect(screen.getByRole('button', { name: 'New thread' })).toBeInTheDocument());
  });

  it('still accepts the deprecated configId prop (communications.registration.ts dual-use call site)', async () => {
    expect(() => renderWidget(webLightTheme, { configId: 'e1826c4c-9575-f111-ab0e-7ced8ddc4a05' })).not.toThrow();
    await waitFor(() => expect(screen.getByRole('button', { name: 'New thread' })).toBeInTheDocument());
  });
});
