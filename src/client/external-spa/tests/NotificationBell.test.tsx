/**
 * R3 task 055 (FR-08) - the notification bell: what the header shows, when it reads the feed, when it marks it seen, and
 * what a click opens.
 *
 * What runs for real: `NotificationBell`, `useNotificationFeed` (poll timer, back-off, visibility, seen), the
 * `notifications-client` (normalisation, failure classification, badge text), `RecordOpenerProvider`, and Fluent v9 (the
 * popover, tabster focus handling). What is substituted: the network seam (`bffApiCall`, which throws `ApiError` exactly as
 * bff-client builds it: the raw ProblemDetails body is the message) and the detail view (a stub that records which entity
 * and id it was asked to show; the real denied state is covered in RecordDeepLink.test.tsx).
 *
 * The SERVER decides what is in the feed (task 037). These tests prove the SPA shows the count it is given, reads at the
 * rates the project allows, never marks a feed seen that it did not read, and opens the record an item names.
 */
import * as React from 'react';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';

const h = vi.hoisted(() => ({ bffApiCall: vi.fn() }));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return { ...actual, bffApiCall: h.bffApiCall };
});

// The detail view, as far as this file cares: which entity and id it was asked to show, and a way to close it.
vi.mock('../src/components/detail/RecordDetailModal', () => ({
  RecordDetailModal: (props: {
    entityName: string;
    recordIds: readonly string[];
    index: number;
    onClose: () => void;
  }) =>
    React.createElement(
      'div',
      {
        role: 'dialog',
        'aria-label': 'Record detail',
        'data-testid': 'detail',
        'data-entity': props.entityName,
        'data-id': props.recordIds[props.index],
      },
      React.createElement('button', { onClick: props.onClose }, 'Close detail')
    ),
}));

import { ApiError } from '../src/types';
import { SignInRequiredError } from '../src/auth/msal-auth';
import { NotificationBell, bellLabel, notificationText, relativeTime } from '../src/components/shell/NotificationBell';
import { RecordOpenerProvider } from '../src/components/detail/RecordOpener';
import { MAX_BACKOFF_MS, POLL_INTERVAL_MS, nextPollDelay } from '../src/hooks/useNotificationFeed';
import { normaliseFeed, notificationFailureOf, unreadBadgeText } from '../src/api/notifications-client';

// ---------------------------------------------------------------------------
// Harness
// ---------------------------------------------------------------------------

const guid = (n: number): string => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const FEED = '/api/v1/external/notifications';
const SEEN = '/api/v1/external/notifications/seen';

interface WireItem {
  kind: 'grant' | 'message';
  id: string;
  recordType: string;
  recordId: string;
  recordTitle: string | null;
  occurredOn: string;
  senderName: string | null;
  isUnread: boolean;
  [extra: string]: unknown;
}

let counter = 0;
function item(over: Partial<WireItem> = {}): WireItem {
  counter += 1;
  return {
    kind: 'grant',
    id: guid(1000 + counter),
    recordType: 'matters',
    recordId: guid(2000 + counter),
    recordTitle: `Record ${counter}`,
    occurredOn: new Date(Date.UTC(2026, 9, 12, 10, 0, 0) - counter * 60_000).toISOString(),
    senderName: null,
    isUnread: true,
    ...over,
  };
}

function feedOf(items: WireItem[], extra: Record<string, unknown> = {}): Record<string, unknown> {
  return { value: items, unreadCount: items.filter(i => i.isUnread).length, lastSeenAt: null, ...extra };
}

function problem(status: number, errorCode: string | null): ApiError {
  return new ApiError(
    status,
    JSON.stringify({ title: 'x', detail: 'server detail', ...(errorCode ? { errorCode } : {}) })
  );
}

/** Every call the bell made, in order, as "GET /path" / "POST /path". */
let calls: string[] = [];
/** The GETs (by order) that were made as a background read: `bffApiCall(path, {}, { noInteraction: true })`. */
let backgroundGets: boolean[] = [];
let onGet: () => Promise<unknown>;
let onPost: () => Promise<unknown>;

function installNetwork(): void {
  calls = [];
  backgroundGets = [];
  onGet = async () => feedOf([]);
  onPost = async () => ({ lastSeenAt: '2026-10-12T10:00:00+00:00' });
  h.bffApiCall.mockReset();
  h.bffApiCall.mockImplementation((p: string, init?: RequestInit, auth?: { noInteraction?: boolean }) => {
    const method = (init?.method ?? 'GET').toUpperCase();
    calls.push(`${method} ${p}`);
    if (method === 'GET' && p === FEED) backgroundGets.push(auth?.noInteraction === true);
    if (method === 'GET' && p === FEED) return onGet();
    if (method === 'POST' && p === SEEN) return onPost();
    return Promise.reject(new Error(`unexpected call ${method} ${p}`));
  });
}

const gets = (): number => calls.filter(c => c === `GET ${FEED}`).length;
const posts = (): number => calls.filter(c => c === `POST ${SEEN}`).length;

function renderBell(theme = webLightTheme): ReturnType<typeof render> {
  return render(
    <FluentProvider theme={theme}>
      <RecordOpenerProvider>
        <NotificationBell />
      </RecordOpenerProvider>
    </FluentProvider>
  );
}

const bellButton = (): HTMLElement => screen.getByTestId('notification-bell');
const settle = (ms = 0): Promise<void> =>
  act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });

/**
 * jsdom does no layout: every element reports `offsetParent === null` and the body a 0x0 rect, so tabster (Fluent v9's focus
 * manager) treats everything as hidden and the popover finds no focusable child. The same two stubs DetailDocumentsDelete.test
 * uses give jsdom the geometry a browser has; nothing else is changed.
 */
const realOffsetParent = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'offsetParent');
function installLayout(): void {
  Object.defineProperty(HTMLElement.prototype, 'offsetParent', {
    configurable: true,
    get(this: HTMLElement) {
      return this.parentNode;
    },
  });
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({
    x: 0,
    y: 0,
    top: 0,
    left: 0,
    right: 1024,
    bottom: 768,
    width: 1024,
    height: 768,
    toJSON: () => ({}),
  });
}
function uninstallLayout(): void {
  if (realOffsetParent) Object.defineProperty(HTMLElement.prototype, 'offsetParent', realOffsetParent);
}

/** Escape pressed inside the popover, as a keyboard user does; Fluent closes the popover on it. */
async function pressEscapeInPopover(): Promise<void> {
  fireEvent.keyDown(await screen.findByRole('dialog', { name: 'Notifications' }), { key: 'Escape' });
}

let visibility: 'visible' | 'hidden' = 'visible';
function setVisibility(next: 'visible' | 'hidden'): void {
  visibility = next;
  document.dispatchEvent(new Event('visibilitychange'));
}

beforeEach(() => {
  visibility = 'visible';
  Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => visibility });
  installNetwork();
  installLayout();
});
afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
  uninstallLayout();
});

const SLOW = { timeout: 60000 };

// ---------------------------------------------------------------------------
// Badge
// ---------------------------------------------------------------------------

describe('NotificationBell - badge', () => {
  it('shows the feed unread count, and the bell button name includes it', SLOW, async () => {
    onGet = async () => feedOf([item(), item(), item({ isUnread: false })]);
    renderBell();
    expect((await screen.findByTestId('notification-badge')).textContent).toBe('2');
    expect(screen.getByRole('button', { name: 'Notifications, 2 unread' })).toBe(bellButton());
  });

  it('shows no badge, and the plain name, when nothing is unread', SLOW, async () => {
    onGet = async () => feedOf([item({ isUnread: false })]);
    renderBell();
    await waitFor(() => expect(gets()).toBe(1));
    await act(async () => undefined);
    expect(screen.queryByTestId('notification-badge')).toBeNull();
    expect(screen.getByRole('button', { name: 'Notifications' })).toBe(bellButton());
  });

  it('shows "100+" for a full page, and "N+" when the feed is truncated (the count is a floor)', SLOW, async () => {
    onGet = async () => feedOf([item()], { unreadCount: 100, truncated: true });
    const first = renderBell();
    expect((await screen.findByTestId('notification-badge')).textContent).toBe('100+');
    expect(bellButton().getAttribute('aria-label')).toBe('Notifications, 100 or more unread');
    first.unmount();

    installNetwork();
    onGet = async () => feedOf([item(), item(), item()], { truncated: true });
    renderBell();
    expect((await screen.findByTestId('notification-badge')).textContent).toBe('3+');
  });

  it('shows "100+" for a full page even if the server omitted truncated', SLOW, async () => {
    onGet = async () => feedOf([item()], { unreadCount: 100 });
    renderBell();
    expect((await screen.findByTestId('notification-badge')).textContent).toBe('100+');
  });

  it('badge helpers: a non-truncated count is exact, zero has no badge', () => {
    expect(unreadBadgeText({ unreadCount: 7, truncated: false })).toBe('7');
    expect(unreadBadgeText({ unreadCount: 0, truncated: true })).toBeNull();
    expect(unreadBadgeText(null)).toBeNull();
    expect(bellLabel('1')).toBe('Notifications, 1 unread');
    expect(bellLabel(null)).toBe('Notifications');
  });
});

// ---------------------------------------------------------------------------
// Open: list, seen
// ---------------------------------------------------------------------------

describe('NotificationBell - opening the bell', () => {
  it('re-reads the feed, then posts /seen exactly once, and the badge clears', SLOW, async () => {
    let seen = false;
    const items = [item(), item({ kind: 'message', senderName: 'Sam Rivera' })];
    onGet = async () => feedOf(items.map(i => ({ ...i, isUnread: !seen })));
    onPost = async () => {
      seen = true;
      return { lastSeenAt: '2026-10-12T10:00:00+00:00' };
    };
    renderBell();
    await screen.findByTestId('notification-badge');
    expect(calls).toEqual([`GET ${FEED}`]);

    fireEvent.click(bellButton());
    await waitFor(() => expect(posts()).toBe(1));
    // The /seen came AFTER the open's own read.
    expect(calls).toEqual([`GET ${FEED}`, `GET ${FEED}`, `POST ${SEEN}`]);
    await waitFor(() => expect(screen.queryByTestId('notification-badge')).toBeNull());
    expect(bellButton().getAttribute('aria-label')).toBe('Notifications');

    // The items that were new stay highlighted while the list is open.
    const list = await screen.findByRole('list', { name: 'Notifications' });
    expect(
      within(list)
        .getAllByRole('button')
        .every(b => b.getAttribute('aria-label')?.startsWith('Unread.'))
    ).toBe(true);

    // Closing and opening again reads again but the feed has nothing unread now: no second POST.
    await pressEscapeInPopover();
    await waitFor(() => expect(screen.queryByRole('list', { name: 'Notifications' })).toBeNull());
    fireEvent.click(bellButton());
    await waitFor(() => expect(gets()).toBe(3));
    await act(async () => undefined);
    expect(posts()).toBe(1);
  });

  it('does not post /seen when nothing in the feed is unread', SLOW, async () => {
    onGet = async () => feedOf([item({ isUnread: false })]);
    renderBell();
    await waitFor(() => expect(gets()).toBe(1));
    fireEvent.click(bellButton());
    await waitFor(() => expect(gets()).toBe(2));
    await screen.findByRole('list', { name: 'Notifications' });
    expect(posts()).toBe(0);
  });

  it(
    'a read superseded by a newer one never drives /seen, even if it arrives later with unread items',
    SLOW,
    async () => {
      const pending: Array<(feed: unknown) => void> = [];
      let n = 0;
      onGet = () => {
        n += 1;
        if (n === 2) return new Promise(resolve => pending.push(resolve)); // the first open's read: held back
        return Promise.resolve(feedOf([item({ isUnread: false })]));
      };
      renderBell();
      await waitFor(() => expect(gets()).toBe(1));

      fireEvent.click(bellButton()); // open #1: its read is held
      await waitFor(() => expect(gets()).toBe(2));
      await pressEscapeInPopover();
      await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Notifications' })).toBeNull());
      fireEvent.click(bellButton()); // open #2: its read answers at once, nothing unread
      await waitFor(() => expect(gets()).toBe(3));
      await screen.findByRole('list', { name: 'Notifications' });
      expect(posts()).toBe(0);

      // The held-back read finally arrives with unread items: it is stale, so it changes nothing and marks nothing seen.
      await act(async () => {
        pending[0](feedOf([item(), item()]));
      });
      await act(async () => new Promise(resolve => setTimeout(resolve, 50)));
      expect(posts()).toBe(0);
      expect(screen.queryByTestId('notification-badge')).toBeNull();
    }
  );

  it('lists the items newest first, whatever order they arrive in', SLOW, async () => {
    const older = item({ recordTitle: 'Older record', occurredOn: '2026-10-01T08:00:00.000Z' });
    const newer = item({ recordTitle: 'Newer record', occurredOn: '2026-10-11T08:00:00.000Z' });
    onGet = async () => feedOf([older, newer]);
    renderBell();
    await screen.findByTestId('notification-badge');
    fireEvent.click(bellButton());
    const list = await screen.findByRole('list', { name: 'Notifications' });
    const names = within(list)
      .getAllByRole('listitem')
      .map(li => li.textContent ?? '');
    expect(names[0]).toContain('Newer record');
    expect(names[1]).toContain('Older record');
  });

  it('shows the empty state for a feed that was read and is empty', SLOW, async () => {
    onGet = async () => feedOf([]);
    renderBell();
    await waitFor(() => expect(gets()).toBe(1));
    fireEvent.click(bellButton());
    expect(await screen.findByTestId('notifications-empty')).toBeTruthy();
    expect(posts()).toBe(0);
  });

  it('says so when the feed is truncated', SLOW, async () => {
    onGet = async () => feedOf([item({ isUnread: false })], { truncated: true });
    renderBell();
    await waitFor(() => expect(gets()).toBe(1));
    fireEvent.click(bellButton());
    expect(await screen.findByTestId('notifications-truncated')).toBeTruthy();
  });

  it('a failed /seen keeps the badge, says so, and is retried only on the next open', SLOW, async () => {
    vi.useFakeTimers();
    onGet = async () => feedOf([item(), item()]);
    onPost = async () => {
      throw problem(503, 'EXT_NOTIFICATIONS_SEEN_UNAVAILABLE');
    };
    renderBell();
    await settle(0);
    expect(screen.getByTestId('notification-badge').textContent).toBe('2');
    fireEvent.click(bellButton());
    await settle(0);
    expect(screen.getByTestId('notifications-seen-failed')).toBeTruthy();
    expect(posts()).toBe(1);
    expect(screen.getByTestId('notification-badge').textContent).toBe('2');

    // No retry loop on its own: well past the poll period AND the back-off cap, with the popover open, the /seen is not
    // tried again (and the open popover pauses the poll, so no read happens either) ...
    await settle(MAX_BACKOFF_MS + 5 * POLL_INTERVAL_MS);
    expect(posts()).toBe(1);
    expect(gets()).toBe(2);

    // ... nor after it closes and the poll resumes: polls read the feed, they never mark it seen.
    fireEvent.keyDown(screen.getByRole('dialog', { name: 'Notifications' }), { key: 'Escape' });
    await settle(MAX_BACKOFF_MS + 5 * POLL_INTERVAL_MS);
    expect(gets()).toBeGreaterThan(2);
    expect(posts()).toBe(1);

    // The next open tries again.
    fireEvent.click(bellButton());
    await settle(0);
    expect(posts()).toBe(2);
  });
});

// ---------------------------------------------------------------------------
// Wording: titles, senders, no body
// ---------------------------------------------------------------------------

describe('NotificationBell - what an item says', () => {
  it('falls back to the record kind when the server sent no title', () => {
    expect(notificationText(item({ recordType: 'matters', recordTitle: null }))).toBe(
      'You were given access to a matter'
    );
    expect(notificationText(item({ recordType: 'workassignments', recordTitle: null }))).toBe(
      'You were given access to a work assignment'
    );
    expect(
      notificationText(item({ kind: 'message', recordType: 'servicerequests', recordTitle: null, senderName: null }))
    ).toBe('Someone sent a message on a service request');
    expect(notificationText(item({ recordType: 'unheard-of', recordTitle: null }))).toBe(
      'You were given access to a record'
    );
  });

  it('shows the title and sender in the list, never a message body', SLOW, async () => {
    onGet = async () =>
      feedOf([
        item({
          kind: 'message',
          recordType: 'projects',
          recordTitle: 'Acme v. Widget',
          senderName: 'Sam Rivera',
          // A body the contract does not allow. If the server ever sent one, the client still must not show it.
          body: 'PRIVILEGED BODY TEXT',
          sprk_body: 'PRIVILEGED BODY TEXT',
        }),
        item({ recordTitle: null }),
      ]);
    renderBell();
    await screen.findByTestId('notification-badge');
    fireEvent.click(bellButton());
    const list = await screen.findByRole('list', { name: 'Notifications' });
    expect(within(list).getByText('Sam Rivera sent a message on Acme v. Widget')).toBeTruthy();
    expect(within(list).getByText('You were given access to a matter')).toBeTruthy();
    expect(document.body.textContent).not.toContain('PRIVILEGED BODY TEXT');
  });

  it('relative time is sensible', () => {
    const now = Date.UTC(2026, 9, 12, 12, 0, 0);
    expect(relativeTime(new Date(now - 10_000).toISOString(), now)).toBe('just now');
    expect(relativeTime(new Date(now - 5 * 60_000).toISOString(), now)).toBe('5 minutes ago');
    expect(relativeTime(new Date(now - 3 * 3_600_000).toISOString(), now)).toBe('3 hours ago');
    expect(relativeTime('not a date', now)).toBe('');
  });

  it('normalises: keeps only the allow-listed fields and drops rows it cannot show', () => {
    const feed = normaliseFeed({
      value: [{ ...item(), body: 'x', extra: 1 }, { kind: 'weird', id: 'a' }, null],
      unreadCount: 4,
    });
    expect(feed.value).toHaveLength(1);
    expect(Object.keys(feed.value[0]).sort()).toEqual(
      ['id', 'isUnread', 'kind', 'occurredOn', 'recordId', 'recordTitle', 'recordType', 'senderName'].sort()
    );
    expect(() => normaliseFeed({})).toThrow();
  });
});

// ---------------------------------------------------------------------------
// Click: the right record per type
// ---------------------------------------------------------------------------

describe('NotificationBell - clicking an item', () => {
  it.each([
    ['projects', 'sprk_project'],
    ['matters', 'sprk_matter'],
    ['workassignments', 'sprk_workassignment'],
    ['servicerequests', 'sprk_servicerequest'],
  ])('opens the %s record in the detail view (%s)', SLOW, async (recordType, entity) => {
    const target = item({ recordType, recordId: guid(77), recordTitle: 'Target record' });
    onGet = async () => feedOf([target, item()]);
    renderBell();
    await screen.findByTestId('notification-badge');
    fireEvent.click(bellButton());
    const list = await screen.findByRole('list', { name: 'Notifications' });
    fireEvent.click(within(list).getByRole('button', { name: /Target record/ }));

    const detail = await screen.findByTestId('detail');
    expect(detail.getAttribute('data-entity')).toBe(entity);
    expect(detail.getAttribute('data-id')).toBe(guid(77));
    // The popover closed.
    await waitFor(() => expect(screen.queryByRole('list', { name: 'Notifications' })).toBeNull());

    // Closing the detail view puts focus back on the bell.
    fireEvent.click(within(detail).getByRole('button', { name: 'Close detail' }));
    await waitFor(() => expect(screen.queryByTestId('detail')).toBeNull());
    await waitFor(() => expect(document.activeElement).toBe(bellButton()));
  });

  it('shows an item it cannot open (unknown type, malformed id) but makes it no link', SLOW, async () => {
    onGet = async () =>
      feedOf([
        item({ recordType: 'widgets', recordTitle: 'Unknown type' }),
        item({ recordType: 'matters', recordId: 'not-a-guid', recordTitle: 'Bad id' }),
      ]);
    renderBell();
    await screen.findByTestId('notification-badge');
    fireEvent.click(bellButton());
    const list = await screen.findByRole('list', { name: 'Notifications' });
    expect(within(list).getByText(/Unknown type/)).toBeTruthy();
    expect(within(list).getByText(/Bad id/)).toBeTruthy();
    expect(within(list).queryAllByRole('button')).toHaveLength(0);
    expect(screen.queryByTestId('detail')).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// Failures: a failed read is never an empty feed, and never reaches /seen
// ---------------------------------------------------------------------------

describe('NotificationBell - failures', () => {
  it('503 CONTACT_UNREADABLE: no badge, an error (not the empty state), and /seen is never called', SLOW, async () => {
    onGet = async () => {
      throw problem(503, 'EXT_NOTIFICATIONS_CONTACT_UNREADABLE');
    };
    renderBell();
    await waitFor(() => expect(gets()).toBe(1));
    expect(screen.queryByTestId('notification-badge')).toBeNull();

    fireEvent.click(bellButton());
    const error = await screen.findByTestId('notifications-error');
    expect(error.textContent).toContain("couldn't check your account");
    expect(screen.queryByTestId('notifications-empty')).toBeNull();
    await waitFor(() => expect(gets()).toBe(2));

    // "Try again" while it is still unreadable: another read, still no /seen.
    fireEvent.click(within(error).getByRole('button', { name: 'Try again' }));
    await waitFor(() => expect(gets()).toBe(3));
    await act(async () => undefined);
    expect(posts()).toBe(0);
    expect(screen.queryByTestId('notifications-empty')).toBeNull();
  });

  it('retries a 503 on request, and marks seen once the retry reads the feed', SLOW, async () => {
    let attempts = 0;
    onGet = async () => {
      attempts += 1;
      if (attempts <= 2) throw problem(503, 'EXT_NOTIFICATIONS_CONTACT_UNREADABLE');
      return feedOf([item(), item()]);
    };
    renderBell();
    await waitFor(() => expect(gets()).toBe(1));
    fireEvent.click(bellButton());
    const error = await screen.findByTestId('notifications-error');
    await waitFor(() => expect(gets()).toBe(2));
    expect(posts()).toBe(0);
    fireEvent.click(within(error).getByRole('button', { name: 'Try again' }));
    await waitFor(() => expect(posts()).toBe(1));
    expect(calls.slice(-2)).toEqual([`GET ${FEED}`, `POST ${SEEN}`]);
  });

  it('500: an error, no empty state, no /seen', SLOW, async () => {
    onGet = async () => {
      throw problem(500, 'EXT_NOTIFICATIONS_INTERNAL_ERROR');
    };
    renderBell();
    await waitFor(() => expect(gets()).toBe(1));
    fireEvent.click(bellButton());
    const error = await screen.findByTestId('notifications-error');
    expect(error.textContent).toContain("couldn't be loaded");
    expect(screen.queryByTestId('notifications-empty')).toBeNull();
    await waitFor(() => expect(gets()).toBe(2));
    expect(posts()).toBe(0);
  });

  it('a failed open does not mark seen even though an earlier read had unread items', SLOW, async () => {
    let attempts = 0;
    onGet = async () => {
      attempts += 1;
      if (attempts === 1) return feedOf([item(), item()]);
      throw problem(500, 'EXT_NOTIFICATIONS_INTERNAL_ERROR');
    };
    renderBell();
    await screen.findByTestId('notification-badge');
    fireEvent.click(bellButton());
    await screen.findByTestId('notifications-error');
    expect(posts()).toBe(0);
  });

  it('a network failure is an error too, not an empty feed', SLOW, async () => {
    onGet = async () => {
      throw new TypeError('Failed to fetch');
    };
    renderBell();
    await waitFor(() => expect(gets()).toBe(1));
    fireEvent.click(bellButton());
    await screen.findByTestId('notifications-error');
    expect(posts()).toBe(0);
  });

  it(
    '401 (after the forced-refresh retry) says to sign in again, not that the account has no notifications',
    SLOW,
    async () => {
      onGet = async () => {
        throw problem(401, null);
      };
      renderBell();
      await waitFor(() => expect(gets()).toBe(1));
      fireEvent.click(bellButton());
      const error = await screen.findByTestId('notifications-error');
      expect(error.textContent).toContain('Sign in again to see notifications.');
      expect(error.textContent).not.toContain("aren't available");
      expect(screen.queryByTestId('notifications-empty')).toBeNull();
      expect(posts()).toBe(0);
    }
  );

  it('403 keeps its own text: notifications are not available for this account', SLOW, async () => {
    onGet = async () => {
      throw problem(403, null);
    };
    renderBell();
    await waitFor(() => expect(gets()).toBe(1));
    fireEvent.click(bellButton());
    const error = await screen.findByTestId('notifications-error');
    expect(error.textContent).toContain("Notifications aren't available for your account.");
    expect(error.textContent).not.toContain('Sign in again');
    expect(posts()).toBe(0);
  });

  it(
    'a read that needs an interactive sign-in shows the same sign-in text, with no redirect from the bell',
    SLOW,
    async () => {
      onGet = async () => {
        throw new SignInRequiredError({ errorCode: 'interaction_required', aadsts: null });
      };
      renderBell();
      await waitFor(() => expect(gets()).toBe(1));
      fireEvent.click(bellButton());
      const error = await screen.findByTestId('notifications-error');
      expect(error.textContent).toContain('Sign in again to see notifications.');
      expect(posts()).toBe(0);
    }
  );

  it('classifies failures', () => {
    expect(notificationFailureOf(problem(429, null)).kind).toBe('rate-limited');
    // 401 means "sign in again" (as BffAccessState.accessFailureOf), 403 means this caller cannot read a feed.
    expect(notificationFailureOf(problem(401, null)).kind).toBe('signin');
    expect(notificationFailureOf(problem(403, null)).kind).toBe('unauthorized');
    // A background read that needed an interactive sign-in (no redirect was started) is the same sign-in state.
    expect(
      notificationFailureOf(new SignInRequiredError({ errorCode: 'interaction_required', aadsts: null })).kind
    ).toBe('signin');
    expect(notificationFailureOf(problem(503, 'EXT_NOTIFICATIONS_CONTACT_UNREADABLE')).kind).toBe('contact-unreadable');
    expect(notificationFailureOf(problem(503, 'EXT_NOTIFICATIONS_SEEN_UNAVAILABLE')).kind).toBe('seen-unavailable');
    expect(notificationFailureOf(problem(503, null)).kind).toBe('server');
    expect(notificationFailureOf(problem(500, 'EXT_NOTIFICATIONS_INTERNAL_ERROR')).kind).toBe('server');
    expect(notificationFailureOf(new Error('x')).kind).toBe('network');
  });
});

// ---------------------------------------------------------------------------
// Polling: rate, back-off, visibility, pause
// ---------------------------------------------------------------------------

describe('NotificationBell - polling', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  it('reads on mount, then at most once a minute', SLOW, async () => {
    onGet = async () => feedOf([item({ isUnread: false })]);
    renderBell();
    await settle(0);
    expect(gets()).toBe(1);
    await settle(POLL_INTERVAL_MS - 1);
    expect(gets()).toBe(1);
    await settle(1);
    expect(gets()).toBe(2);
    await settle(10 * POLL_INTERVAL_MS);
    // 10 more minutes: ten more reads, not more (never faster than one a minute).
    expect(gets()).toBe(12);
    expect(POLL_INTERVAL_MS).toBeGreaterThanOrEqual(60_000);
  });

  it('429 backs off: the next read is later than a minute, and it doubles while it keeps failing', SLOW, async () => {
    onGet = async () => {
      throw problem(429, null);
    };
    renderBell();
    await settle(0);
    expect(gets()).toBe(1);
    await settle(POLL_INTERVAL_MS);
    expect(gets()).toBe(1); // still waiting: the delay after one failure is two minutes
    await settle(POLL_INTERVAL_MS);
    expect(gets()).toBe(2);
    await settle(2 * POLL_INTERVAL_MS + 1000);
    expect(gets()).toBe(2); // two failures: four minutes
    await settle(2 * POLL_INTERVAL_MS);
    expect(gets()).toBe(3);
  });

  it('503 and 500 back off the same way, and a success returns to the normal rate', SLOW, async () => {
    let mode: 'fail' | 'ok' = 'fail';
    onGet = async () => {
      if (mode === 'fail') throw problem(503, 'EXT_NOTIFICATIONS_CONTACT_UNREADABLE');
      return feedOf([item({ isUnread: false })]);
    };
    renderBell();
    await settle(0);
    expect(gets()).toBe(1);
    await settle(POLL_INTERVAL_MS);
    expect(gets()).toBe(1);
    mode = 'ok';
    await settle(POLL_INTERVAL_MS);
    expect(gets()).toBe(2); // the retry succeeds
    await settle(POLL_INTERVAL_MS);
    expect(gets()).toBe(3); // and the rate is back to one a minute
  });

  it(
    'the timer poll reads in the background (never starts a sign-in); a click reads the normal way',
    SLOW,
    async () => {
      onGet = async () => feedOf([item({ isUnread: false })]);
      renderBell();
      await settle(0); // mount read
      await settle(POLL_INTERVAL_MS); // a poll
      expect(gets()).toBe(2);
      fireEvent.click(bellButton()); // the open: a user gesture
      await settle(0);
      expect(gets()).toBe(3);
      expect(backgroundGets).toEqual([true, true, false]);
    }
  );

  it('a poll that cannot get a token silently backs off and retries silently (bounded, no /seen)', SLOW, async () => {
    onGet = async () => {
      throw new SignInRequiredError({ errorCode: 'interaction_required', aadsts: null });
    };
    renderBell();
    await settle(0);
    expect(gets()).toBe(1);
    // Back-off gaps 2, 4, 8, 10, 10 minutes: four more tries in half an hour, not thirty.
    await settle(30 * POLL_INTERVAL_MS);
    expect(gets()).toBe(5);
    expect(posts()).toBe(0);
  });

  it(
    'a refused token (401 after the forced refresh) stops polling quietly (no second try, no /seen)',
    SLOW,
    async () => {
      onGet = async () => {
        throw problem(401, null);
      };
      renderBell();
      await settle(0);
      expect(gets()).toBe(1);
      await settle(30 * POLL_INTERVAL_MS);
      expect(gets()).toBe(1);
      expect(posts()).toBe(0);
    }
  );

  it('back-off is capped', () => {
    expect(nextPollDelay(0)).toBe(POLL_INTERVAL_MS);
    expect(nextPollDelay(1)).toBe(2 * POLL_INTERVAL_MS);
    expect(nextPollDelay(20)).toBe(MAX_BACKOFF_MS);
  });

  it.each([401, 403])('%i stops polling altogether', SLOW, async status => {
    onGet = async () => {
      throw problem(status, null);
    };
    renderBell();
    await settle(0);
    expect(gets()).toBe(1);
    await settle(30 * POLL_INTERVAL_MS);
    expect(gets()).toBe(1);
  });

  it(
    'makes no request in a hidden tab, and resumes (no sooner than a minute after the last one) when visible',
    SLOW,
    async () => {
      onGet = async () => feedOf([item({ isUnread: false })]);
      renderBell();
      await settle(0);
      expect(gets()).toBe(1);

      await settle(30_000);
      setVisibility('hidden');
      await settle(10 * POLL_INTERVAL_MS);
      expect(gets()).toBe(1); // nothing in the background

      setVisibility('visible');
      await settle(0);
      expect(gets()).toBe(2); // well over a minute since the last read: it reads at once
      await settle(POLL_INTERVAL_MS - 1);
      expect(gets()).toBe(2);
      await settle(1);
      expect(gets()).toBe(3);
    }
  );

  it('a tab that is hidden from the start reads nothing until it is shown', SLOW, async () => {
    visibility = 'hidden';
    onGet = async () => feedOf([item({ isUnread: false })]);
    renderBell();
    await settle(5 * POLL_INTERVAL_MS);
    expect(gets()).toBe(0);
    setVisibility('visible');
    await settle(0);
    expect(gets()).toBe(1);
  });

  it('shown again within a minute of the last read: waits out the rest of the minute', SLOW, async () => {
    onGet = async () => feedOf([item({ isUnread: false })]);
    renderBell();
    await settle(0);
    expect(gets()).toBe(1);
    await settle(10_000);
    setVisibility('hidden');
    setVisibility('visible');
    await settle(0);
    expect(gets()).toBe(1);
    await settle(POLL_INTERVAL_MS - 10_000);
    expect(gets()).toBe(2);
  });

  it(
    'pauses while the popover is open, and the read after it closes is still a minute after the last read',
    SLOW,
    async () => {
      onGet = async () => feedOf([item({ isUnread: false })]);
      renderBell();
      await settle(0);
      expect(gets()).toBe(1);

      fireEvent.click(bellButton());
      await settle(0);
      expect(gets()).toBe(2); // the open's own read
      await settle(10 * POLL_INTERVAL_MS);
      expect(gets()).toBe(2); // no polling while open

      fireEvent.keyDown(screen.getByRole('dialog', { name: 'Notifications' }), { key: 'Escape' }); // findBy* would wait on fake timers
      await settle(0);
      // Closing re-arms from the last read, which is long ago: one read, then back to one a minute.
      expect(gets()).toBe(3);
      await settle(POLL_INTERVAL_MS - 1);
      expect(gets()).toBe(3);
      await settle(1);
      expect(gets()).toBe(4);
    }
  );
});

// ---------------------------------------------------------------------------
// Accessibility
// ---------------------------------------------------------------------------

describe('NotificationBell - keyboard and focus', () => {
  it(
    'opens from the keyboard, arrow-navigates the items, and Escape closes and returns focus to the bell',
    SLOW,
    async () => {
      onGet = async () => feedOf([item({ recordTitle: 'First' }), item({ recordTitle: 'Second' })]);
      renderBell();
      await screen.findByTestId('notification-badge');

      fireEvent.click(bellButton()); // Enter / Space on a button is a click
      const list = await screen.findByRole('list', { name: 'Notifications' });
      // Every openable item is a native button (reachable by Tab / arrows), inside a vertical arrow-navigation group.
      const buttons = within(list).getAllByRole('button');
      expect(buttons).toHaveLength(2);
      expect(buttons.every(b => b.tagName === 'BUTTON')).toBe(true);
      expect(list.getAttribute('data-tabster')).toContain('mover');

      // Arrow keys move between the items (tabster's vertical mover), and wrap.
      await waitFor(() => {
        buttons[0].focus();
        expect(document.activeElement).toBe(buttons[0]);
      });
      fireEvent.keyDown(buttons[0], { key: 'ArrowDown', keyCode: 40 });
      await waitFor(() => expect(document.activeElement).toBe(buttons[1]));
      fireEvent.keyDown(buttons[1], { key: 'ArrowDown', keyCode: 40 });
      await waitFor(() => expect(document.activeElement).toBe(buttons[0]));
      fireEvent.keyDown(buttons[0], { key: 'ArrowUp', keyCode: 38 });
      await waitFor(() => expect(document.activeElement).toBe(buttons[1]));

      await pressEscapeInPopover();
      await waitFor(() => expect(screen.queryByRole('list', { name: 'Notifications' })).toBeNull());
      await waitFor(() => expect(document.activeElement).toBe(bellButton()));
    }
  );
});

// ---------------------------------------------------------------------------
// Theme (ADR-021)
// ---------------------------------------------------------------------------

describe('NotificationBell - theming', () => {
  it('renders in the dark theme', SLOW, async () => {
    onGet = async () => feedOf([item()]);
    renderBell(webDarkTheme);
    expect((await screen.findByTestId('notification-badge')).textContent).toBe('1');
  });

  it('uses semantic tokens only: no hard-coded colour in the bell, the opener, the deep link or the hook', () => {
    const files = [
      'src/components/shell/NotificationBell.tsx',
      'src/components/detail/RecordOpener.tsx',
      'src/components/detail/RecordDeepLink.tsx',
      'src/hooks/useNotificationFeed.ts',
      'src/api/notifications-client.ts',
    ];
    const NAMED = /\b(white|black|red|green|blue|gray|grey|orange|yellow|purple|pink)\b\s*['"`;,)]/i;
    for (const rel of files) {
      const source = fs.readFileSync(path.resolve(__dirname, '..', rel), 'utf8');
      // Drop comments: they describe colours without using them.
      const code = source.replace(/\/\*[\s\S]*?\*\//g, '').replace(/(^|[^:])\/\/.*$/gm, '$1');
      expect(code, rel).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
      expect(code, rel).not.toMatch(/\b(rgb|rgba|hsl|hsla)\(/i);
      expect(code, rel).not.toMatch(NAMED);
    }
  });
});
