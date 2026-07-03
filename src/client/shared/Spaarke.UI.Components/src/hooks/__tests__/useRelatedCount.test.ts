/**
 * useRelatedCount Hook Unit Tests
 *
 * FR-06 / FR-11 (record-header-and-notepad-r1): validates the filter-agnostic
 * $count query hook — correct URL construction for both memo (ADR-024
 * entity-specific lookup) and todo (standard regarding lookup), @odata.count
 * extraction, error surfacing, filter=null skip, window-focus refresh, and
 * unmount cleanup of the focus listener.
 */

import { act, renderHook, waitFor } from '@testing-library/react';
import { useRelatedCount } from '../useRelatedCount';

// ─────────────────────────────────────────────────────────────────────────────
// Xrm mocks
// ─────────────────────────────────────────────────────────────────────────────

type XrmWebApiLike = {
  retrieveMultipleRecords: jest.Mock;
};

type XrmLike = {
  WebApi: XrmWebApiLike;
};

let mockRetrieveMultipleRecords: jest.Mock;

function installXrm(): void {
  mockRetrieveMultipleRecords = jest.fn();
  const xrm: XrmLike = {
    WebApi: { retrieveMultipleRecords: mockRetrieveMultipleRecords },
  };
  // getXrm() walks globalThis → window.parent → window.top. Set both to be
  // safe across jsdom variants (mirrors useRecordFieldValues.test.ts).
  (globalThis as unknown as { Xrm?: XrmLike }).Xrm = xrm;
  (window as unknown as { Xrm?: XrmLike }).Xrm = xrm;
}

function uninstallXrm(): void {
  delete (globalThis as unknown as { Xrm?: XrmLike }).Xrm;
  delete (window as unknown as { Xrm?: XrmLike }).Xrm;
}

// ─────────────────────────────────────────────────────────────────────────────
// Fixtures
// ─────────────────────────────────────────────────────────────────────────────

const MATTER_GUID = '00000000-0000-0000-0000-000000000001';
const TODO_PARENT_GUID = '11111111-1111-1111-1111-111111111111';

// Filter shapes match the two consumer patterns in R1:
//  - memo: ADR-024 entity-specific lookup for a Matter parent
//  - todo: standard polymorphic regarding lookup
const MEMO_FILTER_FOR_MATTER = `_sprk_regardingmatter_value eq ${MATTER_GUID}`;
const TODO_FILTER = `_regardingobjectid_value eq ${TODO_PARENT_GUID}`;

// ─────────────────────────────────────────────────────────────────────────────
// Tests
// ─────────────────────────────────────────────────────────────────────────────

describe('useRelatedCount', () => {
  afterEach(() => {
    uninstallXrm();
    jest.clearAllMocks();
  });

  it('builds correct $count=true&$top=0 URL for sprk_memo with entity-specific lookup filter', async () => {
    installXrm();
    mockRetrieveMultipleRecords.mockResolvedValue({
      '@odata.count': 3,
      entities: [],
    });

    renderHook(() => useRelatedCount('sprk_memo', MEMO_FILTER_FOR_MATTER));

    await waitFor(() =>
      expect(mockRetrieveMultipleRecords).toHaveBeenCalledTimes(1)
    );

    expect(mockRetrieveMultipleRecords).toHaveBeenCalledWith(
      'sprk_memo',
      `?$filter=${MEMO_FILTER_FOR_MATTER}&$count=true&$top=0`
    );
  });

  it('builds correct $count=true&$top=0 URL for sprk_todo with standard regarding filter', async () => {
    installXrm();
    mockRetrieveMultipleRecords.mockResolvedValue({
      '@odata.count': 7,
      entities: [],
    });

    renderHook(() => useRelatedCount('sprk_todo', TODO_FILTER));

    await waitFor(() =>
      expect(mockRetrieveMultipleRecords).toHaveBeenCalledTimes(1)
    );

    expect(mockRetrieveMultipleRecords).toHaveBeenCalledWith(
      'sprk_todo',
      `?$filter=${TODO_FILTER}&$count=true&$top=0`
    );
  });

  it('populates count from the @odata.count annotation on success', async () => {
    installXrm();
    mockRetrieveMultipleRecords.mockResolvedValue({
      '@odata.count': 42,
      entities: [],
    });

    const { result } = renderHook(() =>
      useRelatedCount('sprk_memo', MEMO_FILTER_FOR_MATTER)
    );

    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(result.current.count).toBe(42);
    expect(result.current.error).toBeNull();
  });

  it('coerces missing @odata.count to 0 (defensive default)', async () => {
    installXrm();
    // Response without @odata.count — real Xrm always sends it when
    // $count=true, but we defend against unusual host behavior.
    mockRetrieveMultipleRecords.mockResolvedValue({ entities: [] });

    const { result } = renderHook(() =>
      useRelatedCount('sprk_todo', TODO_FILTER)
    );

    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(result.current.count).toBe(0);
    expect(result.current.error).toBeNull();
  });

  it('skips fetch and returns resting state when filter is null (unsupported surface)', async () => {
    installXrm();
    mockRetrieveMultipleRecords.mockResolvedValue({
      '@odata.count': 5,
      entities: [],
    });

    const { result } = renderHook(() => useRelatedCount('sprk_memo', null));

    // Give React effects a tick to run (none should trigger the mock).
    await act(async () => {
      await Promise.resolve();
    });

    expect(mockRetrieveMultipleRecords).not.toHaveBeenCalled();
    expect(result.current.count).toBe(0);
    expect(result.current.loading).toBe(false);
    expect(result.current.error).toBeNull();
  });

  it('refetches when a window focus event fires', async () => {
    installXrm();
    mockRetrieveMultipleRecords.mockResolvedValue({
      '@odata.count': 3,
      entities: [],
    });

    renderHook(() => useRelatedCount('sprk_memo', MEMO_FILTER_FOR_MATTER));

    // Initial mount fetch.
    await waitFor(() =>
      expect(mockRetrieveMultipleRecords).toHaveBeenCalledTimes(1)
    );

    // Simulate the user returning to the browser tab.
    act(() => {
      window.dispatchEvent(new Event('focus'));
    });

    await waitFor(() =>
      expect(mockRetrieveMultipleRecords).toHaveBeenCalledTimes(2)
    );
    // Same URL — focus refresh replays the current query.
    expect(mockRetrieveMultipleRecords).toHaveBeenLastCalledWith(
      'sprk_memo',
      `?$filter=${MEMO_FILTER_FOR_MATTER}&$count=true&$top=0`
    );
  });

  it('sets error state and forces count=0 when retrieveMultipleRecords rejects', async () => {
    installXrm();
    const failure = new Error('Dataverse count query failed');
    mockRetrieveMultipleRecords.mockRejectedValue(failure);

    const { result } = renderHook(() =>
      useRelatedCount('sprk_memo', MEMO_FILTER_FOR_MATTER)
    );

    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(result.current.error).toBe(failure);
    expect(result.current.count).toBe(0);
  });

  it('surfaces "Xrm.WebApi not available" error when Xrm is undefined', async () => {
    // Do NOT install Xrm — simulates unit-test / non-MDA host.
    uninstallXrm();

    const { result } = renderHook(() =>
      useRelatedCount('sprk_memo', MEMO_FILTER_FOR_MATTER)
    );

    await waitFor(() => expect(result.current.error).not.toBeNull());
    expect(result.current.error).toBeInstanceOf(Error);
    expect(result.current.error?.message).toBe('Xrm.WebApi not available');
    expect(result.current.count).toBe(0);
    expect(result.current.loading).toBe(false);
  });

  it('refetches when the filter string changes (e.g. parent record swap)', async () => {
    installXrm();
    mockRetrieveMultipleRecords.mockResolvedValue({
      '@odata.count': 1,
      entities: [],
    });

    const OTHER_MATTER_GUID = '22222222-2222-2222-2222-222222222222';
    const OTHER_FILTER = `_sprk_regardingmatter_value eq ${OTHER_MATTER_GUID}`;

    const { rerender } = renderHook(
      ({ filter }: { filter: string }) => useRelatedCount('sprk_memo', filter),
      { initialProps: { filter: MEMO_FILTER_FOR_MATTER } }
    );

    await waitFor(() =>
      expect(mockRetrieveMultipleRecords).toHaveBeenCalledTimes(1)
    );
    expect(mockRetrieveMultipleRecords).toHaveBeenLastCalledWith(
      'sprk_memo',
      `?$filter=${MEMO_FILTER_FOR_MATTER}&$count=true&$top=0`
    );

    rerender({ filter: OTHER_FILTER });

    await waitFor(() =>
      expect(mockRetrieveMultipleRecords).toHaveBeenCalledTimes(2)
    );
    expect(mockRetrieveMultipleRecords).toHaveBeenLastCalledWith(
      'sprk_memo',
      `?$filter=${OTHER_FILTER}&$count=true&$top=0`
    );
  });

  it('transitions filter from a value to null → skips fetch and clears state', async () => {
    installXrm();
    mockRetrieveMultipleRecords.mockResolvedValue({
      '@odata.count': 4,
      entities: [],
    });

    const { result, rerender } = renderHook(
      ({ filter }: { filter: string | null }) =>
        useRelatedCount('sprk_memo', filter),
      { initialProps: { filter: MEMO_FILTER_FOR_MATTER as string | null } }
    );

    await waitFor(() => expect(result.current.count).toBe(4));
    expect(mockRetrieveMultipleRecords).toHaveBeenCalledTimes(1);

    rerender({ filter: null });

    await waitFor(() => expect(result.current.count).toBe(0));
    expect(result.current.loading).toBe(false);
    expect(result.current.error).toBeNull();
    // No additional call issued after the null transition.
    expect(mockRetrieveMultipleRecords).toHaveBeenCalledTimes(1);
  });

  it('removes the window focus listener on unmount (no refetch fires after unmount)', async () => {
    installXrm();
    mockRetrieveMultipleRecords.mockResolvedValue({
      '@odata.count': 2,
      entities: [],
    });

    // Spy on window.addEventListener / removeEventListener to verify balanced
    // registration + cleanup. This is a stricter assertion than "no refetch
    // fires after unmount" because it catches leaks that would otherwise only
    // manifest under later focus events.
    const addSpy = jest.spyOn(window, 'addEventListener');
    const removeSpy = jest.spyOn(window, 'removeEventListener');

    const { unmount } = renderHook(() =>
      useRelatedCount('sprk_memo', MEMO_FILTER_FOR_MATTER)
    );

    await waitFor(() =>
      expect(mockRetrieveMultipleRecords).toHaveBeenCalledTimes(1)
    );

    // Exactly one focus listener was registered.
    const focusAddCalls = addSpy.mock.calls.filter((c) => c[0] === 'focus');
    expect(focusAddCalls.length).toBe(1);
    const registeredHandler = focusAddCalls[0][1];

    unmount();

    // Verify the same handler was removed.
    const focusRemoveCalls = removeSpy.mock.calls.filter(
      (c) => c[0] === 'focus'
    );
    expect(focusRemoveCalls.length).toBe(1);
    expect(focusRemoveCalls[0][1]).toBe(registeredHandler);

    // Belt-and-braces: firing focus after unmount MUST NOT trigger a new call.
    act(() => {
      window.dispatchEvent(new Event('focus'));
    });
    await act(async () => {
      await Promise.resolve();
    });
    expect(mockRetrieveMultipleRecords).toHaveBeenCalledTimes(1);

    addSpy.mockRestore();
    removeSpy.mockRestore();
  });
});
