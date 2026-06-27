/**
 * DailyBriefingApp smoke test — R2 task 019 / NFR-05.
 *
 * Asserts:
 *   1. `DailyBriefingApp` mounts cleanly with a mocked `Xrm` global.
 *   2. After channels load, the BFF `/narrate` fetch fires (`authenticatedFetch`
 *      is called with `/api/ai/daily-briefing/narrate` + a NON-empty JSON body
 *      containing `categories` / `channels`).
 *   3. At least one channel (the "Overdue Tasks" group) renders in the DOM.
 *
 * This is intentionally a SMOKE test — not a full integration test. It uses
 * Jest module mocks at the service boundary so the test doesn't depend on
 * Dataverse / MSAL / Fluent v9 internals.
 *
 * Independent mocks (per spec constraint): no module-level state is shared
 * across tests. Each `it` block resets its own mocks via `beforeEach`.
 */

import * as React from 'react';
import { render, waitFor, screen, fireEvent, act } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

// ---- Mock the @spaarke/auth peer dep (routed by jest.config moduleNameMapper)
// ----  test/__mocks__/spaarke-auth.ts exports a jest.fn() authenticatedFetch.

// ---- Mock the data-layer services so the smoke test doesn't need Dataverse.
// R3 task 031: the canonical service names are `markBriefingChecked` /
// `markAllBriefingsChecked` (replaced the transitional aliases removed by
// task 030). Additional R3 actions `markBriefingRemoved` + `extendBriefingTtl`
// are mocked here so `useBriefingActions` does not pull a real import at
// module-resolution time.
jest.mock('../src/services/notificationService', () => ({
  fetchAndGroupNotifications: jest.fn(),
  markBriefingChecked: jest.fn(() => Promise.resolve({ success: true, data: undefined })),
  markAllBriefingsChecked: jest.fn(() => Promise.resolve({ success: true, data: { succeeded: 0, failed: 0 } })),
  markBriefingRemoved: jest.fn(() => Promise.resolve({ success: true, data: undefined })),
  extendBriefingTtl: jest.fn(() => Promise.resolve({ success: true, data: 604800 })),
}));

jest.mock('../src/services/preferencesService', () => ({
  fetchDigestPreferences: jest.fn(() =>
    Promise.resolve({
      success: true,
      data: {
        preferences: {
          disabledChannels: [],
          dueWithinDays: 3,
          timeWindow: '24h',
          autoPopup: true,
        },
        recordId: 'rec-1',
      },
    })
  ),
  saveDigestPreferences: jest.fn(() => Promise.resolve({ success: true, data: 'rec-1' })),
}));

import { DailyBriefingApp } from '../src/components/DailyBriefingApp';
import { fetchAndGroupNotifications } from '../src/services/notificationService';
// Imported AFTER the mock so we get the mocked authenticatedFetch jest.fn().
import { authenticatedFetch } from '@spaarke/auth';
import type { ChannelFetchResult } from '../src/types/notifications';

// ---------------------------------------------------------------------------
// Test helpers
// ---------------------------------------------------------------------------

/**
 * Two-item fixture covering both R3 `sprk_briefingstate` Choice values:
 *   - n-1: Unread  (sprk_briefingstate = 0 → `isRead: false`)
 *   - n-2: Checked (sprk_briefingstate = 1 → `isRead: true`)
 *
 * The smoke test mocks `fetchAndGroupNotifications` which returns parsed
 * `NotificationItem[]` (not raw `appnotification` entities), so the Choice
 * value is materialized into the `isRead` derived property by
 * `toNotificationItem` in the real service. We mirror both states here so the
 * R3 widget logic (`unreadCount` calc, "Mark as read" idempotence) renders
 * against a representative dual-state set.
 */
const fakeChannels: ChannelFetchResult[] = [
  {
    status: 'success',
    group: {
      meta: {
        category: 'tasks-overdue',
        label: 'Overdue Tasks',
        iconName: 'Warning',
        order: 1,
      },
      items: [
        {
          // sprk_briefingstate = 0 (Unread) → isRead: false
          id: 'n-1',
          title: 'Review motion to dismiss',
          body: 'Motion is overdue.',
          category: 'tasks-overdue',
          priority: 'high',
          actionUrl: '/main.aspx?etc=1&id=abc',
          regardingName: 'Acme Matter',
          regardingEntityType: 'sprk_matter',
          regardingId: '11111111-1111-1111-1111-111111111111',
          isRead: false,
          isAiGenerated: false,
          createdOn: new Date().toISOString(),
          dueDate: null,
          // R3 FR-6 follow-up: post-task-010 producer writes ttlinseconds=604800
          ttlinseconds: 604800,
        },
        {
          // sprk_briefingstate = 1 (Checked) → isRead: true
          id: 'n-2',
          title: 'Discovery deadline approaching',
          body: 'Discovery deadline is on the calendar.',
          category: 'tasks-overdue',
          priority: 'normal',
          actionUrl: '/main.aspx?etc=1&id=def',
          regardingName: 'Acme Matter',
          regardingEntityType: 'sprk_matter',
          regardingId: '11111111-1111-1111-1111-111111111111',
          isRead: true,
          isAiGenerated: false,
          createdOn: new Date().toISOString(),
          dueDate: null,
          // R3 FR-6 follow-up: pre-rollout row with no stored TTL (undefined)
          ttlinseconds: undefined,
        },
      ],
      unreadCount: 1,
    },
  },
];

function installXrmGlobal(navigateToImpl?: jest.Mock): jest.Mock {
  const navigateTo = navigateToImpl ?? jest.fn().mockResolvedValue(undefined);
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (window as any).Xrm = {
    WebApi: {
      retrieveMultipleRecords: jest.fn(),
      retrieveRecord: jest.fn(),
      createRecord: jest.fn(),
      updateRecord: jest.fn(),
      deleteRecord: jest.fn(),
    },
    Navigation: { navigateTo },
    Utility: {
      getGlobalContext: () => ({
        userSettings: {
          userId: '{00000000-0000-0000-0000-000000000001}',
        },
      }),
    },
  };
  return navigateTo;
}

function uninstallXrmGlobal(): void {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  delete (window as any).Xrm;
}

function renderApp(): ReturnType<typeof render> {
  return render(
    <FluentProvider theme={webLightTheme}>
      <DailyBriefingApp params={{}} />
    </FluentProvider>
  );
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('DailyBriefingApp (smoke)', () => {
  beforeEach(() => {
    (fetchAndGroupNotifications as jest.Mock).mockReset();
    (fetchAndGroupNotifications as jest.Mock).mockResolvedValue(fakeChannels);
    (authenticatedFetch as jest.Mock).mockReset();
    // R3 task 031 — return a duck-typed Response shim instead of `new Response(...)`.
    // jest-environment-jsdom v30 does NOT expose the WHATWG `Response`
    // constructor as a global, so the original `new Response(...)` call in this
    // mock factory threw a `ReferenceError: Response is not defined` at
    // resolve-time. That swallowed exception caused `briefingService.fetchBriefingNarration`
    // to enter its `catch` branch and return `status: 'error'`, leaving the
    // narrative-bullet branch unrendered. Returning a plain object with
    // `.status` / `.ok` / `.headers` / `.json()` (the only shape consumed by
    // `briefingService.ts:325`) keeps the existing /narrate assertions intact
    // AND lets the new "3 R3 buttons" test exercise the bullet render path.
    const buildNarrateBody = (): string =>
      JSON.stringify({
        tldr: {
          summary: 'You have 1 overdue motion that needs immediate review.',
          keyTakeaways: ['Acme Matter motion to dismiss is overdue.'],
          topAction: 'Review the Acme Matter motion to dismiss.',
          categoryCount: 1,
          priorityItemCount: 1,
        },
        channelNarratives: [
          {
            category: 'tasks-overdue',
            bullets: [
              {
                narrative: 'Review motion to dismiss for Acme Matter.',
                itemIds: ['n-1'],
                primaryEntityType: 'sprk_matter',
                primaryEntityId: '11111111-1111-1111-1111-111111111111',
                primaryEntityName: 'Acme Matter',
              },
            ],
          },
        ],
        generatedAtUtc: new Date().toISOString(),
      });
    (authenticatedFetch as jest.Mock).mockImplementation(() => {
      const body = buildNarrateBody();
      return Promise.resolve({
        status: 200,
        ok: true,
        headers: { get: (k: string) => (k.toLowerCase() === 'content-type' ? 'application/json' : null) },
        json: () => Promise.resolve(JSON.parse(body)),
        text: () => Promise.resolve(body),
      });
    });
    installXrmGlobal();
  });

  afterEach(() => {
    uninstallXrmGlobal();
  });

  it('mounts and fires the /narrate fetch with a non-empty payload', async () => {
    renderApp();

    // Wait for the channels effect → narration effect chain to settle.
    await waitFor(
      () => {
        expect(authenticatedFetch).toHaveBeenCalled();
      },
      { timeout: 3000 }
    );

    // Assert /narrate was the endpoint
    const calls = (authenticatedFetch as jest.Mock).mock.calls;
    const narrateCall = calls.find(c => typeof c[0] === 'string' && c[0].includes('/api/ai/daily-briefing/narrate'));
    expect(narrateCall).toBeDefined();

    // Assert non-empty JSON body with `categories` + `channels`
    const init = narrateCall![1] as RequestInit;
    expect(init.method).toBe('POST');
    expect(init.body).toBeTruthy();
    const parsed = JSON.parse(init.body as string);
    expect(parsed.categories).toBeDefined();
    expect(parsed.channels).toBeDefined();
    expect(Array.isArray(parsed.categories)).toBe(true);
    expect(Array.isArray(parsed.channels)).toBe(true);
    expect(parsed.categories.length + parsed.channels.length).toBeGreaterThan(0);
    expect(parsed.totalNotificationCount).toBeGreaterThan(0);
  });

  it('renders at least one channel after the digest loads', async () => {
    renderApp();

    // The Overdue Tasks channel meta should appear in the rendered DOM once
    // narratives resolve. We assert against the channel meta label rather
    // than channel-internal text so this test is resilient to bullet
    // re-arrangement.
    await waitFor(
      () => {
        const overdueMatches = screen.queryAllByText(/overdue tasks/i);
        expect(overdueMatches.length).toBeGreaterThan(0);
      },
      { timeout: 3000 }
    );
  });

  it('renders the 3 new R3 per-item action buttons + preserves Add to To Do (ADR-024)', async () => {
    renderApp();

    // R4 task 045 — the inline 5-icon action row was REPLACED by a three-dot
    // overflow menu (FR-18). The 3 R3 actions + Add to To Do now live inside
    // a Fluent v9 MenuPopover that mounts ONLY while the menu is open. To
    // assert their presence + their canonical labels, we wait for the
    // MenuButton trigger (aria-label="More actions") then click to open it
    // and assert against the rendered MenuItems.
    await waitFor(
      () => {
        expect(screen.queryByRole('button', { name: /More actions/i })).toBeInTheDocument();
      },
      { timeout: 5000 }
    );

    const trigger = screen.getByRole('button', { name: /More actions/i });
    act(() => {
      fireEvent.click(trigger);
    });

    // R3 task 031 — 3 new per-item menu items with owner-specified labels:
    //   1. "Mark as read"                          (CheckmarkRegular)
    //   2. "Remove from briefing"                  (DismissRegular)
    //   3. "Keep on briefing for 7 more days"      (CalendarAddRegular)
    expect(screen.getByRole('menuitem', { name: /^Mark as read$/i })).toBeInTheDocument();
    expect(screen.getByRole('menuitem', { name: /^Remove from briefing$/i })).toBeInTheDocument();
    expect(screen.getByRole('menuitem', { name: /^Keep on briefing for 7 more days$/i })).toBeInTheDocument();

    // ADR-024 regression-free: existing "Add to To Do" menu item still renders.
    expect(screen.getByRole('menuitem', { name: /^Add to To Do$/i })).toBeInTheDocument();
  });

  it('FR-19 link-click happy path: Open record menu item → Xrm.Navigation.navigateTo({pageType, entityName, entityId}, {target:2, 80%×80%}) (AC-19a)', async () => {
    // Override Xrm with a navigateTo that resolves (success case = no toast).
    uninstallXrmGlobal();
    const navigateTo = installXrmGlobal(jest.fn().mockResolvedValue(undefined));
    renderApp();

    await waitFor(
      () => {
        expect(screen.queryByRole('button', { name: /More actions/i })).toBeInTheDocument();
      },
      { timeout: 5000 }
    );

    const trigger = screen.getByRole('button', { name: /More actions/i });
    act(() => {
      fireEvent.click(trigger);
    });
    act(() => {
      fireEvent.click(screen.getByRole('menuitem', { name: /^Open record$/i }));
    });

    expect(navigateTo).toHaveBeenCalledTimes(1);
    const [page, options] = navigateTo.mock.calls[0];
    expect(page).toEqual({
      pageType: 'entityrecord',
      entityName: 'sprk_matter',
      entityId: '11111111-1111-1111-1111-111111111111',
    });
    expect(options).toMatchObject({
      target: 2,
      width: { value: 80, unit: '%' },
      height: { value: 80, unit: '%' },
    });
  });

  it('FR-19 / AC-19b: rejected navigateTo (e.g., 403) → non-blocking Toaster toast "Cannot open record" rendered', async () => {
    // Override Xrm with a navigateTo that REJECTS — simulates Dataverse 403.
    uninstallXrmGlobal();
    installXrmGlobal(jest.fn().mockRejectedValue(new Error('403 Forbidden')));
    renderApp();

    await waitFor(
      () => {
        expect(screen.queryByRole('button', { name: /More actions/i })).toBeInTheDocument();
      },
      { timeout: 5000 }
    );

    // Open the overflow menu and click "Open record" — triggers the rejected
    // navigateTo path → DailyBriefingApp.handleOpenRecord dispatches a toast.
    const trigger = screen.getByRole('button', { name: /More actions/i });
    await act(async () => {
      fireEvent.click(trigger);
    });
    await act(async () => {
      fireEvent.click(screen.getByRole('menuitem', { name: /^Open record$/i }));
      // Flush the rejected promise so the .catch handler dispatches the toast.
      await Promise.resolve();
      await Promise.resolve();
    });

    // The Toaster (mounted at app root) renders the toast title in the DOM
    // once dispatched. We assert the canonical FR-19 user-facing copy.
    await waitFor(
      () => {
        expect(screen.getByText(/Cannot open record/i)).toBeInTheDocument();
      },
      { timeout: 2000 }
    );
    // Body cue: "You may not have access." matches the AC-19b copy.
    expect(screen.getByText(/You may not have access\./i)).toBeInTheDocument();
  });
});
