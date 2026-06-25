/**
 * Unit tests for `notificationService` — R3 task 020 / FRs 3, 4, 5, 6.
 *
 * Contract under test:
 *   - `toNotificationItem` derives `isRead` from `sprk_briefingstate` (NOT `toasttype`).
 *   - `toNotificationItem` null-coalesces `sprk_briefingstate` to Unread (FR-3 AC-3c).
 *   - `fetchNotifications` ALWAYS includes the Removed-exclusion filter
 *     (FR-3 AC-3b), with the `unreadOnly` predicate AND-joined when requested.
 *   - `markBriefingChecked` writes `{ sprk_briefingstate: 1 }` (FR-4 AC-4).
 *   - `markBriefingRemoved` writes `{ sprk_briefingstate: 2 }` (FR-5 AC-5).
 *   - `extendBriefingTtl(id, current)` writes `{ ttlinseconds: current + 604800 }`
 *     and returns the new TTL (FR-6 AC-6).
 *   - Error paths surface as `IResult.success === false` with an error code.
 *
 * Independent mocks (per spec constraint): each test creates a fresh
 * `IWebApi` mock with `jest.fn()` methods — no shared state across tests.
 *
 * NOTE on `toNotificationItem`:
 *   The function is an internal helper (not exported). It is exercised
 *   indirectly through `fetchNotifications`, which is the public API. All
 *   `isRead` derivation assertions therefore go through `fetchNotifications`
 *   mocking `retrieveMultipleRecords` to return a controlled entities list.
 */

import {
  fetchNotifications,
  markBriefingChecked,
  markAllBriefingsChecked,
  markBriefingRemoved,
  extendBriefingTtl,
} from '../src/services/notificationService';
import type { IWebApi, RetrieveMultipleResult, WebApiEntity } from '../src/types/notifications';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeWebApi(): IWebApi {
  return {
    retrieveMultipleRecords: jest.fn(),
    retrieveRecord: jest.fn(),
    createRecord: jest.fn(),
    updateRecord: jest.fn(),
    deleteRecord: jest.fn(),
  };
}

function makeEntity(overrides: Partial<WebApiEntity> = {}): WebApiEntity {
  return {
    appnotificationid: '00000000-0000-0000-0000-000000000001',
    title: 'Sample',
    body: 'body',
    data: JSON.stringify({
      customData: {
        category: 'new-emails',
        priority: 'normal',
        actionUrl: '/main.aspx',
        regardingName: 'Acme',
        regardingEntityType: 'sprk_matter',
        regardingId: '00000000-0000-0000-0000-000000000099',
        isAiGenerated: false,
      },
    }),
    toasttype: 200000000, // Microsoft "Timed" — display behavior, NOT a read marker
    createdon: '2026-06-24T10:00:00Z',
    ...overrides,
  };
}

function makeMultiResult(entities: WebApiEntity[]): RetrieveMultipleResult {
  return { entities };
}

// ---------------------------------------------------------------------------
// FR-3: read-state derivation + filter
// ---------------------------------------------------------------------------

describe('toNotificationItem (via fetchNotifications) — FR-3 read-state derivation', () => {
  it('derives isRead = true when sprk_briefingstate === 1 (Checked)', async () => {
    const webApi = makeWebApi();
    (webApi.retrieveMultipleRecords as jest.Mock).mockResolvedValue(
      makeMultiResult([makeEntity({ sprk_briefingstate: 1 })])
    );

    const result = await fetchNotifications(webApi);

    expect(result.success).toBe(true);
    if (result.success) {
      expect(result.data).toHaveLength(1);
      expect(result.data[0].isRead).toBe(true);
    }
  });

  it('derives isRead = false when sprk_briefingstate === 0 (Unread)', async () => {
    const webApi = makeWebApi();
    (webApi.retrieveMultipleRecords as jest.Mock).mockResolvedValue(
      makeMultiResult([makeEntity({ sprk_briefingstate: 0 })])
    );

    const result = await fetchNotifications(webApi);

    expect(result.success).toBe(true);
    if (result.success) {
      expect(result.data[0].isRead).toBe(false);
    }
  });

  it('derives isRead = false when sprk_briefingstate === 2 (Removed, but Removed items are filtered server-side so this guards against a misconfigured filter)', async () => {
    const webApi = makeWebApi();
    (webApi.retrieveMultipleRecords as jest.Mock).mockResolvedValue(
      makeMultiResult([makeEntity({ sprk_briefingstate: 2 })])
    );

    const result = await fetchNotifications(webApi);

    expect(result.success).toBe(true);
    if (result.success) {
      expect(result.data[0].isRead).toBe(false);
    }
  });

  it('FR-3 AC-3c: derives isRead = false when sprk_briefingstate is undefined (pre-rollout existing row)', async () => {
    const webApi = makeWebApi();
    // No sprk_briefingstate field on entity at all → undefined → null-coalesce to 0 (Unread)
    const entity = makeEntity();
    delete (entity as Record<string, unknown>).sprk_briefingstate;
    (webApi.retrieveMultipleRecords as jest.Mock).mockResolvedValue(makeMultiResult([entity]));

    const result = await fetchNotifications(webApi);

    expect(result.success).toBe(true);
    if (result.success) {
      expect(result.data[0].isRead).toBe(false);
    }
  });

  it('FR-3 AC-3c: derives isRead = false when sprk_briefingstate is explicitly null', async () => {
    const webApi = makeWebApi();
    (webApi.retrieveMultipleRecords as jest.Mock).mockResolvedValue(
      // Explicit null mimics what OData null-values can surface as on the wire
      makeMultiResult([makeEntity({ sprk_briefingstate: null as unknown as number })])
    );

    const result = await fetchNotifications(webApi);

    expect(result.success).toBe(true);
    if (result.success) {
      expect(result.data[0].isRead).toBe(false);
    }
  });

  it('FR-6 follow-up: surfaces ttlinseconds from entity to NotificationItem', async () => {
    const webApi = makeWebApi();
    (webApi.retrieveMultipleRecords as jest.Mock).mockResolvedValue(
      makeMultiResult([makeEntity({ sprk_briefingstate: 0, ttlinseconds: 604800 })])
    );

    const result = await fetchNotifications(webApi);

    expect(result.success).toBe(true);
    if (result.success) {
      expect(result.data[0].ttlinseconds).toBe(604800);
    }
  });

  it('FR-6 follow-up: ttlinseconds is undefined when entity has none (pre-rollout row)', async () => {
    const webApi = makeWebApi();
    const entity = makeEntity({ sprk_briefingstate: 0 });
    delete (entity as Record<string, unknown>).ttlinseconds;
    (webApi.retrieveMultipleRecords as jest.Mock).mockResolvedValue(makeMultiResult([entity]));

    const result = await fetchNotifications(webApi);

    expect(result.success).toBe(true);
    if (result.success) {
      expect(result.data[0].ttlinseconds).toBeUndefined();
    }
  });

  it('FR-7 invariant: does NOT derive isRead from toasttype (toasttype=200000000 alone is Unread)', async () => {
    const webApi = makeWebApi();
    // toasttype=200000000 (Microsoft "Timed") + sprk_briefingstate=0 (Unread)
    // → isRead must be false (this was the R3 root-cause bug — every notification
    //   arrived pre-marked read because toasttype was the read source)
    (webApi.retrieveMultipleRecords as jest.Mock).mockResolvedValue(
      makeMultiResult([makeEntity({ toasttype: 200000000, sprk_briefingstate: 0 })])
    );

    const result = await fetchNotifications(webApi);

    expect(result.success).toBe(true);
    if (result.success) {
      expect(result.data[0].isRead).toBe(false);
    }
  });
});

describe('fetchNotifications — FR-3 AC-3b server-side filter', () => {
  it('always includes the Removed-exclusion filter (no `unreadOnly`)', async () => {
    const webApi = makeWebApi();
    (webApi.retrieveMultipleRecords as jest.Mock).mockResolvedValue(makeMultiResult([]));

    await fetchNotifications(webApi);

    const [, query] = (webApi.retrieveMultipleRecords as jest.Mock).mock.calls[0];
    expect(query).toContain('$filter=');
    expect(query).toContain('(sprk_briefingstate ne 2 or sprk_briefingstate eq null)');
  });

  it('AND-joins the unreadOnly predicate with the Removed-exclusion filter', async () => {
    const webApi = makeWebApi();
    (webApi.retrieveMultipleRecords as jest.Mock).mockResolvedValue(makeMultiResult([]));

    await fetchNotifications(webApi, { unreadOnly: true });

    const [, query] = (webApi.retrieveMultipleRecords as jest.Mock).mock.calls[0];
    expect(query).toContain('(sprk_briefingstate ne 2 or sprk_briefingstate eq null)');
    expect(query).toContain('(sprk_briefingstate ne 1 or sprk_briefingstate eq null)');
    expect(query).toContain(' and ');
  });

  it('selects sprk_briefingstate column', async () => {
    const webApi = makeWebApi();
    (webApi.retrieveMultipleRecords as jest.Mock).mockResolvedValue(makeMultiResult([]));

    await fetchNotifications(webApi);

    const [, query] = (webApi.retrieveMultipleRecords as jest.Mock).mock.calls[0];
    expect(query).toContain('$select=');
    expect(query).toContain('sprk_briefingstate');
  });

  it('FR-6 follow-up: selects ttlinseconds column', async () => {
    const webApi = makeWebApi();
    (webApi.retrieveMultipleRecords as jest.Mock).mockResolvedValue(makeMultiResult([]));

    await fetchNotifications(webApi);

    const [, query] = (webApi.retrieveMultipleRecords as jest.Mock).mock.calls[0];
    expect(query).toContain('ttlinseconds');
  });

  it('FR-7 invariant: does NOT filter on `toasttype` (no toasttype ne 200000000 predicate)', async () => {
    const webApi = makeWebApi();
    (webApi.retrieveMultipleRecords as jest.Mock).mockResolvedValue(makeMultiResult([]));

    await fetchNotifications(webApi, { unreadOnly: true });

    const [, query] = (webApi.retrieveMultipleRecords as jest.Mock).mock.calls[0];
    expect(query).not.toContain('toasttype ne 200000000');
  });

  it('respects the top option', async () => {
    const webApi = makeWebApi();
    (webApi.retrieveMultipleRecords as jest.Mock).mockResolvedValue(makeMultiResult([]));

    await fetchNotifications(webApi, { top: 25 });

    const [, query] = (webApi.retrieveMultipleRecords as jest.Mock).mock.calls[0];
    expect(query).toContain('$top=25');
  });

  it('surfaces upstream errors as IResult failure', async () => {
    const webApi = makeWebApi();
    (webApi.retrieveMultipleRecords as jest.Mock).mockRejectedValue(new Error('Dataverse down'));

    const result = await fetchNotifications(webApi);

    expect(result.success).toBe(false);
    if (!result.success) {
      expect(result.error.code).toBe('NOTIFICATIONS_FETCH_ERROR');
      expect(result.error.message).toBe('Dataverse down');
    }
  });
});

// ---------------------------------------------------------------------------
// FR-4: markBriefingChecked
// ---------------------------------------------------------------------------

describe('markBriefingChecked — FR-4 AC-4', () => {
  it('writes { sprk_briefingstate: 1 } for the given id', async () => {
    const webApi = makeWebApi();
    (webApi.updateRecord as jest.Mock).mockResolvedValue({ id: 'n-1' });

    const result = await markBriefingChecked(webApi, 'n-1');

    expect(result.success).toBe(true);
    expect(webApi.updateRecord).toHaveBeenCalledTimes(1);
    expect(webApi.updateRecord).toHaveBeenCalledWith('appnotification', 'n-1', {
      sprk_briefingstate: 1,
    });
  });

  it('FR-7 invariant: does NOT write toasttype', async () => {
    const webApi = makeWebApi();
    (webApi.updateRecord as jest.Mock).mockResolvedValue({ id: 'n-1' });

    await markBriefingChecked(webApi, 'n-1');

    const [, , payload] = (webApi.updateRecord as jest.Mock).mock.calls[0];
    expect(Object.keys(payload as Record<string, unknown>)).not.toContain('toasttype');
    expect(Object.keys(payload as Record<string, unknown>)).not.toContain('isread');
  });

  it('surfaces upstream errors as IResult failure', async () => {
    const webApi = makeWebApi();
    (webApi.updateRecord as jest.Mock).mockRejectedValue(new Error('forbidden'));

    const result = await markBriefingChecked(webApi, 'n-1');

    expect(result.success).toBe(false);
    if (!result.success) {
      expect(result.error.code).toBe('BRIEFING_MARK_CHECKED_ERROR');
      expect(result.error.message).toBe('forbidden');
    }
  });
});

// ---------------------------------------------------------------------------
// FR-4 (bulk): markAllBriefingsChecked
// ---------------------------------------------------------------------------

describe('markAllBriefingsChecked — FR-4 bulk', () => {
  it('writes { sprk_briefingstate: 1 } for each unread item', async () => {
    const webApi = makeWebApi();
    // Stage retrieveMultipleRecords for the inner unread fetch
    (webApi.retrieveMultipleRecords as jest.Mock).mockResolvedValue(
      makeMultiResult([
        makeEntity({ appnotificationid: 'n-1', sprk_briefingstate: 0 }),
        makeEntity({ appnotificationid: 'n-2', sprk_briefingstate: 0 }),
      ])
    );
    (webApi.updateRecord as jest.Mock).mockResolvedValue({ id: 'ok' });

    const result = await markAllBriefingsChecked(webApi);

    expect(result.success).toBe(true);
    if (result.success) {
      expect(result.data.succeeded).toBe(2);
      expect(result.data.failed).toBe(0);
    }
    expect(webApi.updateRecord).toHaveBeenCalledTimes(2);
    for (const call of (webApi.updateRecord as jest.Mock).mock.calls) {
      expect(call[2]).toEqual({ sprk_briefingstate: 1 });
    }
  });

  it('counts per-item failures via Promise.allSettled', async () => {
    const webApi = makeWebApi();
    (webApi.retrieveMultipleRecords as jest.Mock).mockResolvedValue(
      makeMultiResult([
        makeEntity({ appnotificationid: 'n-1', sprk_briefingstate: 0 }),
        makeEntity({ appnotificationid: 'n-2', sprk_briefingstate: 0 }),
      ])
    );
    (webApi.updateRecord as jest.Mock).mockResolvedValueOnce({ id: 'n-1' }).mockRejectedValueOnce(new Error('quota'));

    const result = await markAllBriefingsChecked(webApi);

    expect(result.success).toBe(true);
    if (result.success) {
      expect(result.data.succeeded).toBe(1);
      expect(result.data.failed).toBe(1);
    }
  });
});

// ---------------------------------------------------------------------------
// FR-5: markBriefingRemoved
// ---------------------------------------------------------------------------

describe('markBriefingRemoved — FR-5 AC-5', () => {
  it('writes { sprk_briefingstate: 2 } for the given id', async () => {
    const webApi = makeWebApi();
    (webApi.updateRecord as jest.Mock).mockResolvedValue({ id: 'n-1' });

    const result = await markBriefingRemoved(webApi, 'n-1');

    expect(result.success).toBe(true);
    expect(webApi.updateRecord).toHaveBeenCalledWith('appnotification', 'n-1', {
      sprk_briefingstate: 2,
    });
  });

  it('FR-7 invariant: does NOT write toasttype or isread', async () => {
    const webApi = makeWebApi();
    (webApi.updateRecord as jest.Mock).mockResolvedValue({ id: 'n-1' });

    await markBriefingRemoved(webApi, 'n-1');

    const [, , payload] = (webApi.updateRecord as jest.Mock).mock.calls[0];
    const keys = Object.keys(payload as Record<string, unknown>);
    expect(keys).not.toContain('toasttype');
    expect(keys).not.toContain('isread');
  });

  it('surfaces upstream errors as IResult failure', async () => {
    const webApi = makeWebApi();
    (webApi.updateRecord as jest.Mock).mockRejectedValue(new Error('conflict'));

    const result = await markBriefingRemoved(webApi, 'n-1');

    expect(result.success).toBe(false);
    if (!result.success) {
      expect(result.error.code).toBe('BRIEFING_MARK_REMOVED_ERROR');
      expect(result.error.message).toBe('conflict');
    }
  });
});

// ---------------------------------------------------------------------------
// FR-6: extendBriefingTtl
// ---------------------------------------------------------------------------

describe('extendBriefingTtl — FR-6 AC-6', () => {
  it('writes { ttlinseconds: current + 604800 } and returns the new TTL', async () => {
    const webApi = makeWebApi();
    (webApi.updateRecord as jest.Mock).mockResolvedValue({ id: 'n-1' });
    const currentTtl = 604800; // 7 days remaining

    const result = await extendBriefingTtl(webApi, 'n-1', currentTtl);

    expect(result.success).toBe(true);
    if (result.success) {
      expect(result.data).toBe(currentTtl + 604800);
    }
    expect(webApi.updateRecord).toHaveBeenCalledWith('appnotification', 'n-1', {
      ttlinseconds: currentTtl + 604800,
    });
  });

  it('handles zero / expired-but-still-present TTL by writing exactly 604800', async () => {
    const webApi = makeWebApi();
    (webApi.updateRecord as jest.Mock).mockResolvedValue({ id: 'n-1' });

    const result = await extendBriefingTtl(webApi, 'n-1', 0);

    expect(result.success).toBe(true);
    if (result.success) {
      expect(result.data).toBe(604800);
    }
    expect(webApi.updateRecord).toHaveBeenCalledWith('appnotification', 'n-1', {
      ttlinseconds: 604800,
    });
  });

  it('FR-7 invariant: does NOT write toasttype or sprk_briefingstate', async () => {
    const webApi = makeWebApi();
    (webApi.updateRecord as jest.Mock).mockResolvedValue({ id: 'n-1' });

    await extendBriefingTtl(webApi, 'n-1', 1000);

    const [, , payload] = (webApi.updateRecord as jest.Mock).mock.calls[0];
    const keys = Object.keys(payload as Record<string, unknown>);
    expect(keys).toEqual(['ttlinseconds']);
  });

  it('surfaces upstream errors as IResult failure', async () => {
    const webApi = makeWebApi();
    (webApi.updateRecord as jest.Mock).mockRejectedValue(new Error('throttled'));

    const result = await extendBriefingTtl(webApi, 'n-1', 1000);

    expect(result.success).toBe(false);
    if (!result.success) {
      expect(result.error.code).toBe('BRIEFING_EXTEND_TTL_ERROR');
      expect(result.error.message).toBe('throttled');
    }
  });
});
