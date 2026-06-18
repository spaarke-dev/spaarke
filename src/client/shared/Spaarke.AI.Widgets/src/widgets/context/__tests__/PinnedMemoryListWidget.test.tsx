/**
 * PinnedMemoryListWidget — unit tests
 *
 * Covers POML UI-tests #1–#7:
 *   1. Render with 6 mock items (2 of each pinType); verify grouping.
 *   2. Filter by pinType; verify only matching items shown.
 *   3. Search by title; verify case-insensitive matching.
 *   4. Open EditDialog; create new item; verify BFF POST + list update.
 *   5. Edit existing item; verify BFF PUT + persistence.
 *   6. Delete item; verify confirmation shown + BFF DELETE + item removed.
 *   7. Render in dark mode; verify ADR-021 token usage (no throw, content
 *      visible).
 *
 * Mocking strategy:
 *   - `@spaarke/auth` is auto-mocked via the package's `__mocks__/@spaarke/auth.ts`
 *     stub (per jest config moduleNameMapper). We override `authenticatedFetch`
 *     per test to control BFF responses.
 *   - `useAiSession` from the provider is mocked inline so the widget gets a
 *     stable `bffBaseUrl` + `authenticatedFetch` pair without dragging the real
 *     provider tree (which requires Auth + PaneEventBus).
 *
 * Task: R6-070 PART B.
 */

import '@testing-library/jest-dom';
import React from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import {
  FluentProvider,
  webDarkTheme,
  webLightTheme,
} from '@fluentui/react-components';

import type { PinDto } from '../../../components/memory/pinned-memory-contracts';

// ---------------------------------------------------------------------------
// Inline mocks — useAiSession + authenticatedFetch
// ---------------------------------------------------------------------------

const mockAuthenticatedFetch = jest.fn<
  Promise<Response>,
  Parameters<(url: string, init?: RequestInit) => Promise<Response>>
>();

jest.mock('../../../providers/useAiSession', () => ({
  useAiSession: () => ({
    isAuthenticated: true,
    bffBaseUrl: 'https://bff.test',
    authenticatedFetch: mockAuthenticatedFetch,
    tenantId: 'test-tenant',
    chatSessionId: 'sess-1',
    setChatSessionId: () => undefined,
    playbookId: null,
    setPlaybookId: () => undefined,
    entityContext: null,
    contextMapping: null,
    streaming: {},
    streamingState: { isStreaming: false },
    turnCount: 0,
    isLoading: false,
    getAccessToken: jest.fn().mockResolvedValue('token'),
  }),
}));

// Import the widget AFTER mocks so the mocked useAiSession is in scope.
// eslint-disable-next-line import/first
import PinnedMemoryListWidget from '../PinnedMemoryListWidget';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function renderWithTheme(ui: React.ReactElement, dark = false): void {
  render(<FluentProvider theme={dark ? webDarkTheme : webLightTheme}>{ui}</FluentProvider>);
}

function makePin(overrides: Partial<PinDto> & { pinId: string; pinType: PinDto['pinType']; title: string }): PinDto {
  return {
    pinId: overrides.pinId,
    pinType: overrides.pinType,
    title: overrides.title,
    content: overrides.content ?? `content of ${overrides.title}`,
    matterId: overrides.matterId ?? null,
    createdAt: overrides.createdAt ?? '2026-06-18T00:00:00Z',
    updatedAt: overrides.updatedAt ?? '2026-06-18T00:00:00Z',
    createdBy: overrides.createdBy ?? 'oid-1',
  };
}

const SIX_PINS: PinDto[] = [
  makePin({ pinId: 'p1', pinType: 'user-preference', title: 'Short sentences' }),
  makePin({ pinId: 'p2', pinType: 'user-preference', title: 'Bullet point bias' }),
  makePin({ pinId: 'p3', pinType: 'system-rule', title: 'No emojis' }),
  makePin({ pinId: 'p4', pinType: 'system-rule', title: 'Cite sources verbatim' }),
  makePin({
    pinId: 'p5',
    pinType: 'matter-fact',
    title: 'Acme is a Delaware LLC',
    matterId: 'matter-acme',
  }),
  makePin({
    pinId: 'p6',
    pinType: 'matter-fact',
    title: 'Acme PoC ends Q4',
    matterId: 'matter-acme',
  }),
];

function jsonResponse(status: number, body: unknown): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
  } as Response;
}

function noContentResponse(): Response {
  return {
    ok: true,
    status: 204,
    json: async () => ({}),
  } as Response;
}

// ---------------------------------------------------------------------------
// Setup / teardown
// ---------------------------------------------------------------------------

beforeEach(() => {
  mockAuthenticatedFetch.mockReset();
});

// ===========================================================================
// Test 1 — Render + grouping
// ===========================================================================

describe('PinnedMemoryListWidget — render + grouping (POML test #1)', () => {
  it('renders 6 mock items grouped by pinType (2 per group)', async () => {
    mockAuthenticatedFetch.mockResolvedValueOnce(
      jsonResponse(200, { items: SIX_PINS, count: SIX_PINS.length })
    );

    renderWithTheme(
      <PinnedMemoryListWidget data={{}} widgetType="pinned-memory-list" />
    );

    // Wait for the initial GET to settle.
    await screen.findByTestId('pinned-memory-groups');

    const groups = screen.getAllByTestId('pinned-memory-group');
    expect(groups).toHaveLength(3);

    const groupTypes = groups.map(g => g.getAttribute('data-group-type'));
    expect(groupTypes).toEqual(['user-preference', 'system-rule', 'matter-fact']);

    // Each group has 2 items.
    for (const group of groups) {
      const items = within(group).getAllByTestId('pinned-memory-item');
      expect(items).toHaveLength(2);
    }

    // Total items rendered.
    const allItems = screen.getAllByTestId('pinned-memory-item');
    expect(allItems).toHaveLength(6);
  });
});

// ===========================================================================
// Test 2 — Filter by pinType
// ===========================================================================

describe('PinnedMemoryListWidget — filter by pinType (POML test #2)', () => {
  it('shows only system-rule pins when the filter is set to system-rule', async () => {
    const user = userEvent.setup();
    mockAuthenticatedFetch.mockResolvedValueOnce(
      jsonResponse(200, { items: SIX_PINS, count: SIX_PINS.length })
    );

    renderWithTheme(
      <PinnedMemoryListWidget data={{}} widgetType="pinned-memory-list" />
    );

    await screen.findByTestId('pinned-memory-groups');

    // Open dropdown and pick "System rules".
    const dropdown = screen.getByTestId('pinned-memory-filter');
    await user.click(dropdown);
    // The option label for system-rule.
    const option = await screen.findByRole('option', { name: 'System rules' });
    await user.click(option);

    // Only the system-rule group should remain.
    await waitFor(() => {
      const groups = screen.getAllByTestId('pinned-memory-group');
      expect(groups).toHaveLength(1);
      expect(groups[0].getAttribute('data-group-type')).toBe('system-rule');
    });

    const allItems = screen.getAllByTestId('pinned-memory-item');
    expect(allItems).toHaveLength(2);
    for (const item of allItems) {
      expect(item.getAttribute('data-pin-type')).toBe('system-rule');
    }
  });
});

// ===========================================================================
// Test 3 — Search by title (case-insensitive)
// ===========================================================================

describe('PinnedMemoryListWidget — search by title (POML test #3)', () => {
  it('matches title text case-insensitively', async () => {
    const user = userEvent.setup();
    mockAuthenticatedFetch.mockResolvedValueOnce(
      jsonResponse(200, { items: SIX_PINS, count: SIX_PINS.length })
    );

    renderWithTheme(
      <PinnedMemoryListWidget data={{}} widgetType="pinned-memory-list" />
    );

    await screen.findByTestId('pinned-memory-groups');

    const search = screen.getByTestId('pinned-memory-search') as HTMLInputElement;
    // Use a mixed-case search to verify case-insensitivity. fireEvent.change
    // pushes the value in one shot (no user.type race under Fluent v9 + jsdom).
    fireEvent.change(search, { target: { value: 'aCmE' } });

    await waitFor(() => {
      const items = screen.getAllByTestId('pinned-memory-item');
      // The two matter-facts both contain "Acme".
      expect(items).toHaveLength(2);
      for (const item of items) {
        expect(item.getAttribute('data-pin-type')).toBe('matter-fact');
      }
    });
  });
});

// ===========================================================================
// Test 4 — Create flow (POST)
// ===========================================================================

describe('PinnedMemoryListWidget — create flow (POML test #4)', () => {
  it('opens the edit dialog, POSTs a new pin, and prepends it to the list', async () => {
    const user = userEvent.setup();
    // Initial GET returns one pin so we can verify the create result is prepended.
    const existing = [SIX_PINS[0]];
    mockAuthenticatedFetch.mockResolvedValueOnce(
      jsonResponse(200, { items: existing, count: 1 })
    );

    renderWithTheme(
      <PinnedMemoryListWidget data={{}} widgetType="pinned-memory-list" />
    );

    await screen.findByTestId('pinned-memory-groups');

    const newPin: PinDto = makePin({
      pinId: 'new-1',
      pinType: 'user-preference',
      title: 'New created pin',
    });
    // Stage the POST response.
    mockAuthenticatedFetch.mockResolvedValueOnce(jsonResponse(201, { item: newPin }));

    // Click "New pin" button.
    await user.click(screen.getByTestId('pinned-memory-create-button'));

    // Fill the form and submit (fireEvent.change avoids the Fluent v9 +
    // jsdom user.type race that drops keystrokes — see EditDialog test for
    // the rationale).
    fireEvent.change(screen.getByTestId('pinned-memory-edit-title'), {
      target: { value: newPin.title },
    });
    fireEvent.change(screen.getByTestId('pinned-memory-edit-content'), {
      target: { value: newPin.content },
    });
    await user.click(screen.getByTestId('pinned-memory-edit-submit'));

    // Wait for the new pin to appear in the list.
    await screen.findByText(newPin.title);

    // Verify the POST call shape.
    const postCalls = mockAuthenticatedFetch.mock.calls.filter(c => c[1]?.method === 'POST');
    expect(postCalls).toHaveLength(1);
    expect(postCalls[0][0]).toBe('https://bff.test/api/memory/pins');
    const body = JSON.parse(postCalls[0][1]!.body as string);
    expect(body).toEqual({
      title: newPin.title,
      content: newPin.content,
      pinType: 'user-preference',
    });
  });
});

// ===========================================================================
// Test 5 — Edit flow (PUT)
// ===========================================================================

describe('PinnedMemoryListWidget — edit flow (POML test #5)', () => {
  it('updates a pin via PUT and reflects the result in the list', async () => {
    const user = userEvent.setup();
    mockAuthenticatedFetch.mockResolvedValueOnce(
      jsonResponse(200, { items: [SIX_PINS[0]], count: 1 })
    );

    renderWithTheme(
      <PinnedMemoryListWidget data={{}} widgetType="pinned-memory-list" />
    );

    await screen.findByText(SIX_PINS[0].title);

    const updated: PinDto = {
      ...SIX_PINS[0],
      title: 'Updated short sentences',
      updatedAt: '2026-06-18T01:00:00Z',
    };
    mockAuthenticatedFetch.mockResolvedValueOnce(jsonResponse(200, { item: updated }));

    // Click the edit button on the first item.
    const item = screen.getAllByTestId('pinned-memory-item')[0];
    await user.click(within(item).getByTestId('pinned-memory-edit-button'));

    // Edit the title.
    const title = screen.getByTestId('pinned-memory-edit-title') as HTMLInputElement;
    fireEvent.change(title, { target: { value: updated.title } });
    await user.click(screen.getByTestId('pinned-memory-edit-submit'));

    await screen.findByText(updated.title);

    const putCalls = mockAuthenticatedFetch.mock.calls.filter(c => c[1]?.method === 'PUT');
    expect(putCalls).toHaveLength(1);
    expect(putCalls[0][0]).toBe(`https://bff.test/api/memory/pins/${SIX_PINS[0].pinId}`);
  });
});

// ===========================================================================
// Test 6 — Delete flow
// ===========================================================================

describe('PinnedMemoryListWidget — delete flow (POML test #6)', () => {
  it('shows confirmation, DELETEs the pin, and removes it from the list', async () => {
    const user = userEvent.setup();
    mockAuthenticatedFetch.mockResolvedValueOnce(
      jsonResponse(200, { items: [SIX_PINS[0], SIX_PINS[1]], count: 2 })
    );

    renderWithTheme(
      <PinnedMemoryListWidget data={{}} widgetType="pinned-memory-list" />
    );

    await screen.findByText(SIX_PINS[0].title);

    // Click the delete button on the first item.
    const firstItem = screen.getAllByTestId('pinned-memory-item')[0];
    await user.click(within(firstItem).getByTestId('pinned-memory-delete-button'));

    // Confirmation surfaces with cross-session impact warning.
    await screen.findByTestId('pinned-memory-delete-confirmation');
    expect(screen.getByTestId('pinned-memory-delete-impact')).toBeInTheDocument();

    // Stage the DELETE 204.
    mockAuthenticatedFetch.mockResolvedValueOnce(noContentResponse());

    await user.click(screen.getByTestId('pinned-memory-delete-confirm'));

    // The deleted pin disappears.
    await waitFor(() => {
      expect(screen.queryByText(SIX_PINS[0].title)).not.toBeInTheDocument();
    });
    // The other pin remains.
    expect(screen.getByText(SIX_PINS[1].title)).toBeInTheDocument();

    const deleteCalls = mockAuthenticatedFetch.mock.calls.filter(c => c[1]?.method === 'DELETE');
    expect(deleteCalls).toHaveLength(1);
    expect(deleteCalls[0][0]).toBe(`https://bff.test/api/memory/pins/${SIX_PINS[0].pinId}`);
  });
});

// ===========================================================================
// Test 7 — Dark mode (ADR-021)
// ===========================================================================

describe('PinnedMemoryListWidget — dark mode (POML test #7, ADR-021)', () => {
  it('renders inside webDarkTheme without throwing and content is visible', async () => {
    mockAuthenticatedFetch.mockResolvedValueOnce(
      jsonResponse(200, { items: [SIX_PINS[0]], count: 1 })
    );

    renderWithTheme(
      <PinnedMemoryListWidget data={{}} widgetType="pinned-memory-list" />,
      true
    );

    await screen.findByText(SIX_PINS[0].title);
    // Header + ARIA region are present.
    expect(screen.getByText('Pinned Memory')).toBeInTheDocument();
    expect(screen.getByTestId('pinned-memory-list-widget')).toBeInTheDocument();
  });
});
