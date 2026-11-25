/**
 * useSearchFilterOptions (#3131): loading / error state and refresh around the shared option
 * loader. Moved from the SemanticSearch code page's `useFilterOptions` tests.
 */

import { renderHook, act, waitFor } from '@testing-library/react';
import type { SearchFilterOptions } from '../searchFilterOptions';

const mockLoad = jest.fn<Promise<SearchFilterOptions>, []>();
const mockClearCache = jest.fn<void, []>();
const FILE_TYPES = [{ value: 'pdf', label: 'PDF' }];

jest.mock('../searchFilterOptions', () => ({
  loadSearchFilterOptions: () => mockLoad(),
  clearSearchFilterOptionsCache: () => mockClearCache(),
  SEARCH_FILE_TYPE_OPTIONS: [{ value: 'pdf', label: 'PDF' }],
}));

import { useSearchFilterOptions } from '../useSearchFilterOptions';

const loaded: SearchFilterOptions = {
  documentTypes: [{ value: '1', label: 'Contract' }],
  fileTypes: FILE_TYPES,
  matterTypes: [{ value: 'mt-1', label: 'Litigation' }],
  failed: [],
};

beforeEach(() => {
  jest.clearAllMocks();
  mockLoad.mockResolvedValue(loaded);
});

it('is loading with empty Dataverse lists until they arrive; file types are there from the start', () => {
  mockLoad.mockReturnValue(new Promise(() => undefined));
  const { result } = renderHook(() => useSearchFilterOptions());

  expect(result.current.isLoading).toBe(true);
  expect(result.current.error).toBeNull();
  expect(result.current.documentTypes).toEqual([]);
  expect(result.current.matterTypes).toEqual([]);
  expect(result.current.fileTypes).toEqual(FILE_TYPES);
});

it('exposes the loaded lists', async () => {
  const { result } = renderHook(() => useSearchFilterOptions());

  await waitFor(() => expect(result.current.isLoading).toBe(false));
  expect(result.current.documentTypes).toEqual(loaded.documentTypes);
  expect(result.current.matterTypes).toEqual(loaded.matterTypes);
  expect(result.current.error).toBeNull();
});

it('reports a list that failed to load, and keeps the one that loaded', async () => {
  mockLoad.mockResolvedValue({ ...loaded, documentTypes: [], failed: ['documentTypes'] });
  const { result } = renderHook(() => useSearchFilterOptions());

  await waitFor(() => expect(result.current.isLoading).toBe(false));
  expect(result.current.error).toBe('Failed to load filter options');
  expect(result.current.matterTypes).toEqual(loaded.matterTypes);
});

it('refresh() clears the cache, loads again and clears a previous error', async () => {
  mockLoad.mockResolvedValueOnce({ ...loaded, failed: ['matterTypes'] });
  const { result } = renderHook(() => useSearchFilterOptions());
  await waitFor(() => expect(result.current.error).not.toBeNull());

  act(() => result.current.refresh());

  expect(mockClearCache).toHaveBeenCalledTimes(1);
  await waitFor(() => expect(result.current.isLoading).toBe(false));
  expect(result.current.error).toBeNull();
  expect(mockLoad).toHaveBeenCalledTimes(2);
});

it('does not update state after unmount', async () => {
  let resolve!: (value: SearchFilterOptions) => void;
  mockLoad.mockReturnValue(new Promise(r => (resolve = r)));
  const { unmount } = renderHook(() => useSearchFilterOptions());

  unmount();
  await act(async () => resolve(loaded));
});
