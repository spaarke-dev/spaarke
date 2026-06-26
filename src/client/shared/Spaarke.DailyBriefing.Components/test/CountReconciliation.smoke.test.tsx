/**
 * CountReconciliation smoke test — R4 task 048 / FR-20 / AC-20.
 *
 * Invariant under test (FR-20):
 *   "TL;DR's `totalNotificationCount` field equals number of items rendered
 *    in Activity Notes section. Reconcile via single source of truth:
 *    input payload to playbook is also displayed by widget."
 *
 * Operationalised as two assertions:
 *
 *   1. The `/narrate` REQUEST body's `totalNotificationCount` field equals
 *      the sum of `items.length` across all `channels` in the same body
 *      (single-source-of-truth invariant — the payload's own internal sum).
 *
 *   2. The number of rendered NarrativeBullet cards (one per overflow-menu
 *      trigger with `aria-label="More actions"`) equals the sum of
 *      `channelNarratives[*].bullets.length` from the mocked /narrate
 *      RESPONSE — and that sum equals the input-payload total when the
 *      mocked /narrate response itself is constructed 1-bullet-per-item.
 *
 * Filtered state (preference-applied) variant: when `disabledChannels`
 * includes one of the categories, both the count visible in the digest
 * header (`totalUnreadCount`) and the number of rendered cards decrement
 * together — the disabled channel never reaches the widget OR /narrate.
 *
 * R3 PR #451 file overlap: the fallback test + smoke test live in the same
 * `test/` directory; conflict-check verified no overlap with these specific
 * filenames.
 *
 * NFR-03: jest-environment-jsdom; mocks at service boundary; no Dataverse
 * dependency; no MSAL.
 */

import * as React from 'react';
import { render, waitFor, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

// ---- Mock data-layer services so the smoke test doesn't need Dataverse.
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
import { fetchDigestPreferences } from '../src/services/preferencesService';
import { authenticatedFetch } from '@spaarke/auth';
import type { ChannelFetchResult, NotificationItem, NotificationCategory } from '../src/types/notifications';

// ---------------------------------------------------------------------------
// Fixture builder — N items across 3 categories
// ---------------------------------------------------------------------------

interface BuildOptions {
  /** Number of items per category, in order [tasks-overdue, new-documents, new-events]. */
  perCategory: [number, number, number];
}

/**
 * Build N items across 3 categories. Each item has a unique id `${cat}-${idx}`
 * so we can assert per-item bullet counts deterministically. All items are
 * `isRead: false` so they count toward `unreadCount` (digest header).
 */
function buildFakeChannels(opts: BuildOptions): ChannelFetchResult[] {
  const [overdueCount, docsCount, eventsCount] = opts.perCategory;
  const nowIso = new Date().toISOString();

  function makeItems(category: NotificationCategory, count: number): NotificationItem[] {
    const items: NotificationItem[] = [];
    for (let i = 1; i <= count; i++) {
      items.push({
        id: `${category}-${i}`,
        title: `${category} item ${i}`,
        body: `Body for ${category} ${i}.`,
        category,
        priority: 'normal',
        actionUrl: `/main.aspx?etc=1&id=${category}-${i}`,
        regardingName: 'Acme Matter',
        regardingEntityType: 'sprk_matter',
        regardingId: '11111111-1111-1111-1111-111111111111',
        isRead: false,
        isAiGenerated: false,
        createdOn: nowIso,
        dueDate: null,
        ttlinseconds: 604800,
      });
    }
    return items;
  }

  return [
    {
      status: 'success',
      group: {
        meta: { category: 'tasks-overdue', label: 'Overdue Tasks', iconName: 'Warning', order: 1 },
        items: makeItems('tasks-overdue', overdueCount),
        unreadCount: overdueCount,
      },
    },
    {
      status: 'success',
      group: {
        meta: { category: 'new-documents', label: 'New Documents', iconName: 'Document', order: 2 },
        items: makeItems('new-documents', docsCount),
        unreadCount: docsCount,
      },
    },
    {
      status: 'success',
      group: {
        meta: { category: 'new-events', label: 'New Events', iconName: 'Calendar', order: 3 },
        items: makeItems('new-events', eventsCount),
        unreadCount: eventsCount,
      },
    },
  ];
}

/**
 * Build a /narrate RESPONSE body. One bullet per input item — preserves the
 * single-source-of-truth invariant (every input row gets a bullet in the
 * output). This is the contract the BFF playbook is expected to satisfy when
 * the LLM does not aggregate.
 */
function buildNarrateResponseBody(channels: ChannelFetchResult[]): string {
  const channelNarratives = channels
    .filter((ch): ch is Extract<ChannelFetchResult, { status: 'success' }> => ch.status === 'success')
    .map(ch => ({
      category: ch.group.meta.category,
      bullets: ch.group.items.map(item => ({
        narrative: `Narrative for ${item.title}.`,
        itemIds: [item.id],
        primaryEntityType: item.regardingEntityType,
        primaryEntityId: item.regardingId,
        primaryEntityName: item.regardingName,
      })),
    }));

  const totalCount = channels.reduce((sum, ch) => sum + (ch.status === 'success' ? ch.group.items.length : 0), 0);

  return JSON.stringify({
    tldr: {
      summary: `You have ${totalCount} notifications across ${channelNarratives.length} categories.`,
      keyTakeaways: channelNarratives.map(cn => `${cn.bullets.length} item(s) in ${cn.category}.`),
      topAction: 'Review your daily briefing.',
      categoryCount: channelNarratives.length,
      priorityItemCount: 0,
    },
    channelNarratives,
    generatedAtUtc: new Date().toISOString(),
  });
}

function installXrmGlobal(): void {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (window as any).Xrm = {
    WebApi: {
      retrieveMultipleRecords: jest.fn(),
      retrieveRecord: jest.fn(),
      createRecord: jest.fn(),
      updateRecord: jest.fn(),
      deleteRecord: jest.fn(),
    },
    Navigation: { navigateTo: jest.fn().mockResolvedValue(undefined) },
    Utility: {
      getGlobalContext: () => ({
        userSettings: { userId: '{00000000-0000-0000-0000-000000000001}' },
      }),
    },
  };
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

describe('FR-20 / AC-20 — TL;DR ↔ Activities count reconciliation (smoke)', () => {
  beforeEach(() => {
    (fetchAndGroupNotifications as jest.Mock).mockReset();
    (authenticatedFetch as jest.Mock).mockReset();
    (fetchDigestPreferences as jest.Mock).mockReset();
    (fetchDigestPreferences as jest.Mock).mockResolvedValue({
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
    });
    installXrmGlobal();
  });

  afterEach(() => {
    uninstallXrmGlobal();
    jest.clearAllMocks();
  });

  it('renders exactly N NarrativeBullet cards for N items in 3 categories (5+3+2=10) and /narrate request totalNotificationCount=10', async () => {
    // Arrange: 5 overdue + 3 documents + 2 events = 10 items across 3 categories.
    const channels = buildFakeChannels({ perCategory: [5, 3, 2] });
    const expectedTotal = 10;
    (fetchAndGroupNotifications as jest.Mock).mockResolvedValue(channels);

    const responseBody = buildNarrateResponseBody(channels);
    (authenticatedFetch as jest.Mock).mockImplementation(() =>
      Promise.resolve({
        status: 200,
        ok: true,
        headers: { get: (k: string) => (k.toLowerCase() === 'content-type' ? 'application/json' : null) },
        json: () => Promise.resolve(JSON.parse(responseBody)),
        text: () => Promise.resolve(responseBody),
      })
    );

    renderApp();

    // Act + Assert (1) — wait for /narrate fetch to fire and inspect its body.
    await waitFor(
      () => {
        expect(authenticatedFetch).toHaveBeenCalled();
      },
      { timeout: 5000 }
    );

    const calls = (authenticatedFetch as jest.Mock).mock.calls;
    const narrateCall = calls.find(c => typeof c[0] === 'string' && c[0].includes('/api/ai/daily-briefing/narrate'));
    expect(narrateCall).toBeDefined();

    const requestInit = narrateCall![1] as RequestInit;
    const requestBody = JSON.parse(requestInit.body as string);

    // FR-20 invariant #1: the request body's `totalNotificationCount` equals
    // the sum of `items.length` across the channels it carries — the payload
    // is internally consistent (single source of truth).
    const sumOfRequestChannelItems = (requestBody.channels as Array<{ items: unknown[] }>).reduce(
      (sum, ch) => sum + ch.items.length,
      0
    );
    expect(requestBody.totalNotificationCount).toBe(expectedTotal);
    expect(sumOfRequestChannelItems).toBe(expectedTotal);

    // Act + Assert (2) — wait for NarrativeBullet cards to render.
    // Each NarrativeBullet has a MenuButton with aria-label="More actions".
    // One trigger per rendered card → count triggers = count cards.
    await waitFor(
      () => {
        const triggers = screen.queryAllByRole('button', { name: /More actions/i });
        expect(triggers.length).toBe(expectedTotal);
      },
      { timeout: 5000 }
    );

    // FR-20 invariant #2: the request payload's total equals the rendered
    // card count — confirms the input-payload-to-widget-display
    // single-source-of-truth contract.
    const finalTriggers = screen.queryAllByRole('button', { name: /More actions/i });
    expect(finalTriggers.length).toBe(requestBody.totalNotificationCount);
  });

  it('preference-filtered state: disabling one channel decrements both the digest header count AND the visible card count together', async () => {
    // Arrange: 5+3+2=10 items, but `documents` channel is disabled by preference.
    // Expected: documents (3 items) never reach widget render or /narrate.
    // Visible cards should be 5 + 2 = 7. Digest header unread count should be 7.
    const channels = buildFakeChannels({ perCategory: [5, 3, 2] });
    const expectedAfterFilter = 7;

    // Pre-filter the channels at the service boundary to simulate the
    // server-side $filter (FR-17c): documents channel does NOT reach the
    // widget at all because preferences disable it.
    const filteredChannels = channels.filter(
      ch => ch.status === 'success' && ch.group.meta.category !== 'new-documents'
    );

    (fetchAndGroupNotifications as jest.Mock).mockResolvedValue(filteredChannels);

    // Override preferences with documents disabled. The consumer-side effect
    // (`Effect 1` in DailyBriefingApp) refetches when disabledChannels
    // changes — fetchAndGroupNotifications returns the already-filtered set,
    // mirroring the FR-17c server-side $filter.
    (fetchDigestPreferences as jest.Mock).mockResolvedValue({
      success: true,
      data: {
        preferences: {
          disabledChannels: ['new-documents'],
          dueWithinDays: 3,
          timeWindow: '24h',
          autoPopup: true,
        },
        recordId: 'rec-1',
      },
    });

    const responseBody = buildNarrateResponseBody(filteredChannels);
    (authenticatedFetch as jest.Mock).mockImplementation(() =>
      Promise.resolve({
        status: 200,
        ok: true,
        headers: { get: (k: string) => (k.toLowerCase() === 'content-type' ? 'application/json' : null) },
        json: () => Promise.resolve(JSON.parse(responseBody)),
        text: () => Promise.resolve(responseBody),
      })
    );

    renderApp();

    // Wait for the /narrate request to fire after the filtered fetch settles.
    await waitFor(
      () => {
        expect(authenticatedFetch).toHaveBeenCalled();
      },
      { timeout: 5000 }
    );

    // The /narrate request must carry only the non-disabled channels.
    // Find the most recent /narrate call (preferences-driven refetch may
    // produce multiple) and assert its totalNotificationCount.
    const calls = (authenticatedFetch as jest.Mock).mock.calls;
    const narrateCalls = calls.filter(c => typeof c[0] === 'string' && c[0].includes('/api/ai/daily-briefing/narrate'));
    expect(narrateCalls.length).toBeGreaterThan(0);
    const lastNarrateCall = narrateCalls[narrateCalls.length - 1];
    const lastRequest = JSON.parse((lastNarrateCall[1] as RequestInit).body as string);
    expect(lastRequest.totalNotificationCount).toBe(expectedAfterFilter);

    // Wait for cards to render. The card count must equal the
    // post-filter total (documents never made it to the widget).
    await waitFor(
      () => {
        const triggers = screen.queryAllByRole('button', { name: /More actions/i });
        expect(triggers.length).toBe(expectedAfterFilter);
      },
      { timeout: 5000 }
    );

    // Digest header — "{n} unread" text appears when totalUnreadCount > 0.
    // All fixture items are isRead:false, so totalUnreadCount === expectedAfterFilter.
    const unreadText = await screen.findByText(new RegExp(`${expectedAfterFilter} unread`));
    expect(unreadText).toBeInTheDocument();

    // FR-20 invariant under filtered state: rendered card count, /narrate
    // request totalNotificationCount, and digest header unread count all
    // decrement together when a channel is disabled.
    const finalTriggers = screen.queryAllByRole('button', { name: /More actions/i });
    expect(finalTriggers.length).toBe(lastRequest.totalNotificationCount);
  });
});
