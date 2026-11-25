/**
 * useSearchFilterOptions (#3131): loading / error state and refresh around the shared option
 * loader. Moved from the SemanticSearch code page's `useFilterOptions` tests.
 */

import { renderHook, act, waitFor } from '@testing-library/react';
import type { SearchFilterOptions } from '../searchFilterOptions';

const mockLoad = jest.fn<Promise<SearchFilterOptions>, []>();
const mockClearCache = jest.fn<void, []>();

jest.mock('../searchFilterOptions', () => ({
  loadSearchFilterOptions: () => mockLoad(),
  clearSearchFilterOptionsCache: () => mockClearCache(),
}));

import { useSearchFilterOptions } from '../useSearchFilterOptions';

const loaded: SearchFilterOptions = {
  documentTypes: [{ value: '1', label: 'Contract' }],
  fileTypes: [{ value: 'pdf', label: 'PDF' }],
  matterTypes: [{ value: 'mt-1', label: 'Litigation' }],
};

beforeEach(() => {
  jest.clearAllMocks();
  mockLoad.mockResolvedValue(loaded);
  jest.spyOn(console, 'error').mockImplementation(() => undefined);
});

afterEach(() => {
  jest.restoreAllMocks();
});

it('is loading with empty lists until the options arrive', () => {
  mockLoad.mockReturnValue(new Promise(() => undefined));
  const { result } = renderHook(() => useSearchFilterOptions());

  expect(result.current.isLoading).toBe(true);
  expect(result.current.error).toBeNull();
  expect(result.current.documentTypes).toEqual([]);
  expect(result.current.fileTypes).toEqual([]);
  expect(result.current.matterTypes).toEqual([]);
});

it('exposes the loaded lists', async () => {
  const { result } = renderHook(() => useSearchFilterOptions());

  await waitFor(() => expect(result.current.isLoading).toBe(false));
  expect(result.current.documentTypes).toEqual(loaded.documentTypes);
  expect(result.current.fileTypes).toEqual(loaded.fileTypes);
  expect(result.current.matterTypes).toEqual(loaded.matterTypes);
});

it('reports a failed load', async () => {
  mockLoad.mockRejectedValue(new Error('down'));
  const { result } = renderHook(() => useSearchFilterOptions());

  await waitFor(() => expect(result.current.isLoading).toBe(false));
  expect(result.current.error).toBe('Failed to load filter options');
});

it('refresh() clears the cache, loads again and clears a previous error', async () => {
  mockLoad.mockRejectedValueOnce(new Error('down'));
  const { result } = renderHook(() => useSearchFilterOptions());
  await waitFor(() => expect(result.current.error).not.toBeNull());

  act(() => result.current.refresh());

  expect(mockClearCache).toHaveBeenCalledTimes(1);
  await waitFor(() => expect(result.current.documentTypes).toEqual(loaded.documentTypes));
  expect(result.current.error).toBeNull();
  expect(result.current.isLoading).toBe(false);
  expect(mockLoad).toHaveBeenCalledTimes(2);
});

it('does not update state after unmount', async () => {
  let resolve!: (value: SearchFilterOptions) => void;
  mockLoad.mockReturnValue(new Promise(r => (resolve = r)));
  const { unmount } = renderHook(() => useSearchFilterOptions());

  unmount();
  await act(async () => resolve(loaded));
});
