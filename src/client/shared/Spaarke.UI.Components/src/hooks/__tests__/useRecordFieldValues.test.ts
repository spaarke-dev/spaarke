/**
 * useRecordFieldValues Hook Unit Tests
 *
 * FR-05 (record-header-and-notepad-r1): validates the primitive Xrm.WebApi
 * read hook — $select construction, loading / values transitions, error
 * surfacing, refetch on recordId change, and stable dep-key behavior when the
 * fields array reference changes without content change.
 */

import { act, renderHook, waitFor } from '@testing-library/react';
import { useRecordFieldValues } from '../useRecordFieldValues';

// ─────────────────────────────────────────────────────────────────────────────
// Xrm mocks
// ─────────────────────────────────────────────────────────────────────────────

type XrmWebApiLike = {
  retrieveRecord: jest.Mock;
};

type XrmLike = {
  WebApi: XrmWebApiLike;
};

// Global mutable reference the tests reassign per scenario.
let mockRetrieveRecord: jest.Mock;

function installXrm(): void {
  mockRetrieveRecord = jest.fn();
  const xrm: XrmLike = { WebApi: { retrieveRecord: mockRetrieveRecord } };
  // getXrm() (utils/xrmContext.ts) walks globalThis → window.parent → window.top.
  // Set both to be safe across jsdom variants.
  (globalThis as unknown as { Xrm?: XrmLike }).Xrm = xrm;
  (window as unknown as { Xrm?: XrmLike }).Xrm = xrm;
}

function uninstallXrm(): void {
  delete (globalThis as unknown as { Xrm?: XrmLike }).Xrm;
  delete (window as unknown as { Xrm?: XrmLike }).Xrm;
}

const MATTER_RECORD = {
  sprk_matternumber: 'M-1',
  sprk_mattername: 'Test Matter',
  sprk_matterdescription: 'Description body',
  sprk_recordsummary: 'AI summary body',
};

// ─────────────────────────────────────────────────────────────────────────────
// Tests
// ─────────────────────────────────────────────────────────────────────────────

describe('useRecordFieldValues', () => {
  afterEach(() => {
    uninstallXrm();
    jest.clearAllMocks();
  });

  it('builds correct ?$select= query from the fields array', async () => {
    installXrm();
    mockRetrieveRecord.mockResolvedValue(MATTER_RECORD);

    renderHook(() =>
      useRecordFieldValues('sprk_matter', 'record-guid-1', [
        'sprk_matternumber',
        'sprk_mattername',
        'sprk_recordsummary',
      ])
    );

    await waitFor(() => expect(mockRetrieveRecord).toHaveBeenCalledTimes(1));

    expect(mockRetrieveRecord).toHaveBeenCalledWith(
      'sprk_matter',
      'record-guid-1',
      '?$select=sprk_matternumber,sprk_mattername,sprk_recordsummary'
    );
  });

  it('transitions loading true → false and populates values on success', async () => {
    installXrm();
    let resolveFetch: (r: Record<string, unknown>) => void = () => {
      /* set below */
    };
    mockRetrieveRecord.mockImplementation(
      () =>
        new Promise<Record<string, unknown>>(resolve => {
          resolveFetch = resolve;
        })
    );

    const { result } = renderHook(() => useRecordFieldValues('sprk_matter', 'record-guid-1', ['sprk_matternumber']));

    // Loading true after mount; values still null.
    await waitFor(() => expect(result.current.loading).toBe(true));
    expect(result.current.values).toBeNull();
    expect(result.current.error).toBeNull();

    // Resolve the pending promise inside act() so React flushes updates.
    await act(async () => {
      resolveFetch(MATTER_RECORD);
    });

    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(result.current.values).toEqual(MATTER_RECORD);
    expect(result.current.error).toBeNull();
  });

  it('sets error state and clears values when retrieveRecord rejects', async () => {
    installXrm();
    const failure = new Error('Dataverse retrieve failed');
    mockRetrieveRecord.mockRejectedValue(failure);

    const { result } = renderHook(() => useRecordFieldValues('sprk_matter', 'record-guid-1', ['sprk_matternumber']));

    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(result.current.values).toBeNull();
    expect(result.current.error).toBe(failure);
  });

  it('refetches when recordId changes', async () => {
    installXrm();
    mockRetrieveRecord.mockResolvedValue(MATTER_RECORD);

    const { rerender } = renderHook(
      ({ recordId }: { recordId: string }) => useRecordFieldValues('sprk_matter', recordId, ['sprk_matternumber']),
      { initialProps: { recordId: 'record-guid-1' } }
    );

    await waitFor(() => expect(mockRetrieveRecord).toHaveBeenCalledTimes(1));
    expect(mockRetrieveRecord).toHaveBeenLastCalledWith('sprk_matter', 'record-guid-1', '?$select=sprk_matternumber');

    rerender({ recordId: 'record-guid-2' });

    await waitFor(() => expect(mockRetrieveRecord).toHaveBeenCalledTimes(2));
    expect(mockRetrieveRecord).toHaveBeenLastCalledWith('sprk_matter', 'record-guid-2', '?$select=sprk_matternumber');
  });

  it('does NOT refetch when fields array reference changes but contents are identical', async () => {
    installXrm();
    mockRetrieveRecord.mockResolvedValue(MATTER_RECORD);

    const { rerender } = renderHook(
      ({ fields }: { fields: string[] }) => useRecordFieldValues('sprk_matter', 'record-guid-1', fields),
      { initialProps: { fields: ['sprk_matternumber', 'sprk_mattername'] } }
    );

    await waitFor(() => expect(mockRetrieveRecord).toHaveBeenCalledTimes(1));

    // NEW array reference, IDENTICAL contents — stable dep key must suppress refetch.
    rerender({ fields: ['sprk_matternumber', 'sprk_mattername'] });

    // Give React a tick to run any effects (none should run).
    await act(async () => {
      await Promise.resolve();
    });

    expect(mockRetrieveRecord).toHaveBeenCalledTimes(1);
  });

  it('DOES refetch when fields array contents change', async () => {
    installXrm();
    mockRetrieveRecord.mockResolvedValue(MATTER_RECORD);

    const { rerender } = renderHook(
      ({ fields }: { fields: string[] }) => useRecordFieldValues('sprk_matter', 'record-guid-1', fields),
      { initialProps: { fields: ['sprk_matternumber'] } }
    );

    await waitFor(() => expect(mockRetrieveRecord).toHaveBeenCalledTimes(1));

    // Content DIFFERS → dep key differs → refetch.
    rerender({ fields: ['sprk_matternumber', 'sprk_mattername'] });

    await waitFor(() => expect(mockRetrieveRecord).toHaveBeenCalledTimes(2));
    expect(mockRetrieveRecord).toHaveBeenLastCalledWith(
      'sprk_matter',
      'record-guid-1',
      '?$select=sprk_matternumber,sprk_mattername'
    );
  });

  it('surfaces "Xrm.WebApi not available" error when Xrm is undefined', async () => {
    // Do NOT install Xrm — simulate the unit-test / non-MDA host case.
    uninstallXrm();

    const { result } = renderHook(() => useRecordFieldValues('sprk_matter', 'record-guid-1', ['sprk_matternumber']));

    await waitFor(() => expect(result.current.error).not.toBeNull());
    expect(result.current.error).toBeInstanceOf(Error);
    expect(result.current.error?.message).toBe('Xrm.WebApi not available');
    expect(result.current.values).toBeNull();
    expect(result.current.loading).toBe(false);
  });

  it('does not fetch when recordId is null and returns resting state', async () => {
    installXrm();
    mockRetrieveRecord.mockResolvedValue(MATTER_RECORD);

    const { result } = renderHook(() => useRecordFieldValues('sprk_matter', null, ['sprk_matternumber']));

    // Give effects a tick.
    await act(async () => {
      await Promise.resolve();
    });

    expect(mockRetrieveRecord).not.toHaveBeenCalled();
    expect(result.current.values).toBeNull();
    expect(result.current.loading).toBe(false);
    expect(result.current.error).toBeNull();
  });

  it('does not fetch when recordId is an empty string', async () => {
    installXrm();
    mockRetrieveRecord.mockResolvedValue(MATTER_RECORD);

    const { result } = renderHook(() => useRecordFieldValues('sprk_matter', '', ['sprk_matternumber']));

    await act(async () => {
      await Promise.resolve();
    });

    expect(mockRetrieveRecord).not.toHaveBeenCalled();
    expect(result.current.values).toBeNull();
    expect(result.current.loading).toBe(false);
    expect(result.current.error).toBeNull();
  });

  it('clears state and skips fetch when recordId transitions from a value back to null', async () => {
    installXrm();
    mockRetrieveRecord.mockResolvedValue(MATTER_RECORD);

    const { result, rerender } = renderHook(
      ({ recordId }: { recordId: string | null }) =>
        useRecordFieldValues('sprk_matter', recordId, ['sprk_matternumber']),
      { initialProps: { recordId: 'record-guid-1' as string | null } }
    );

    await waitFor(() => expect(result.current.values).toEqual(MATTER_RECORD));
    expect(mockRetrieveRecord).toHaveBeenCalledTimes(1);

    rerender({ recordId: null });

    await waitFor(() => expect(result.current.values).toBeNull());
    expect(result.current.loading).toBe(false);
    expect(result.current.error).toBeNull();
    // No additional call issued for the null recordId.
    expect(mockRetrieveRecord).toHaveBeenCalledTimes(1);
  });
});
