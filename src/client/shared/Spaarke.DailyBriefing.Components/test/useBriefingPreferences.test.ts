/**
 * Unit tests for `useBriefingPreferences` — R2 task 019 / NFR-05.
 *
 * Contract under test (FR-06):
 *   - Returns `preferences`, `updatePreferences`, `isLoading`, `error`.
 *   - Starts with `DEFAULT_DAILY_DIGEST_PREFERENCES` until first fetch resolves.
 *   - Stays idle while webApi is null OR userId is empty.
 *   - Loads preferences via `fetchDigestPreferences` and applies on success.
 *   - Keeps defaults when fetch returns failure (non-fatal — opt-out model).
 *   - `updatePreferences()` optimistically merges + persists via
 *     `saveDigestPreferences`; reverts via error state on save failure.
 *
 * Each test creates fresh mocks — NO shared state across tests (per spec
 * constraint).
 */

import { renderHook, act, waitFor } from '@testing-library/react';
import { useBriefingPreferences } from '../src/hooks/useBriefingPreferences';
import {
  DEFAULT_DAILY_DIGEST_PREFERENCES,
  type DailyDigestPreferences,
  type IWebApi,
} from '../src/types/notifications';

// Mock the preferencesService module.
jest.mock('../src/services/preferencesService', () => ({
  fetchDigestPreferences: jest.fn(),
  saveDigestPreferences: jest.fn(),
}));

import { fetchDigestPreferences, saveDigestPreferences } from '../src/services/preferencesService';

function makeWebApi(): IWebApi {
  return {
    retrieveMultipleRecords: jest.fn(),
    retrieveRecord: jest.fn(),
    createRecord: jest.fn(),
    updateRecord: jest.fn(),
    deleteRecord: jest.fn(),
  };
}

const USER_ID = '00000000-0000-0000-0000-000000000001';

describe('useBriefingPreferences', () => {
  beforeEach(() => {
    (fetchDigestPreferences as jest.Mock).mockReset();
    (saveDigestPreferences as jest.Mock).mockReset();
  });

  it('stays idle and uses defaults when webApi is null', () => {
    const { result } = renderHook(() => useBriefingPreferences(null, USER_ID));
    expect(result.current.preferences).toEqual(DEFAULT_DAILY_DIGEST_PREFERENCES);
    expect(result.current.isLoading).toBe(false);
    expect(fetchDigestPreferences).not.toHaveBeenCalled();
  });

  it('stays idle when userId is empty', () => {
    const webApi = makeWebApi();
    const { result } = renderHook(() => useBriefingPreferences(webApi, ''));
    expect(result.current.preferences).toEqual(DEFAULT_DAILY_DIGEST_PREFERENCES);
    expect(fetchDigestPreferences).not.toHaveBeenCalled();
  });

  it('loads preferences from Dataverse on mount', async () => {
    const webApi = makeWebApi();
    const remotePrefs: DailyDigestPreferences = {
      ...DEFAULT_DAILY_DIGEST_PREFERENCES,
      disabledChannels: ['system'],
      dueWithinDays: 7,
    };
    (fetchDigestPreferences as jest.Mock).mockResolvedValue({
      success: true,
      data: { preferences: remotePrefs, recordId: 'rec-1' },
    });

    const { result } = renderHook(() => useBriefingPreferences(webApi, USER_ID));

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false);
    });
    expect(result.current.preferences).toEqual(remotePrefs);
    expect(fetchDigestPreferences).toHaveBeenCalledWith(webApi, USER_ID);
  });

  it('keeps defaults when fetch returns failure (non-fatal)', async () => {
    const webApi = makeWebApi();
    (fetchDigestPreferences as jest.Mock).mockResolvedValue({
      success: false,
      error: { code: 'X', message: 'no record' },
    });

    const { result } = renderHook(() => useBriefingPreferences(webApi, USER_ID));

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false);
    });
    expect(result.current.preferences).toEqual(DEFAULT_DAILY_DIGEST_PREFERENCES);
  });

  it('updatePreferences optimistically merges and persists', async () => {
    const webApi = makeWebApi();
    (fetchDigestPreferences as jest.Mock).mockResolvedValue({
      success: true,
      data: { preferences: DEFAULT_DAILY_DIGEST_PREFERENCES, recordId: 'rec-1' },
    });
    (saveDigestPreferences as jest.Mock).mockResolvedValue({
      success: true,
      data: 'rec-1',
    });

    const { result } = renderHook(() => useBriefingPreferences(webApi, USER_ID));

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false);
    });

    await act(async () => {
      await result.current.updatePreferences({ autoPopup: false });
    });

    expect(result.current.preferences.autoPopup).toBe(false);
    expect(saveDigestPreferences).toHaveBeenCalledWith(
      webApi,
      USER_ID,
      expect.objectContaining({ autoPopup: false }),
      'rec-1'
    );
  });

  it('updatePreferences surfaces error when save fails', async () => {
    const webApi = makeWebApi();
    (fetchDigestPreferences as jest.Mock).mockResolvedValue({
      success: true,
      data: { preferences: DEFAULT_DAILY_DIGEST_PREFERENCES, recordId: 'rec-1' },
    });
    (saveDigestPreferences as jest.Mock).mockResolvedValue({
      success: false,
      error: { code: 'Y', message: 'save failed' },
    });

    const { result } = renderHook(() => useBriefingPreferences(webApi, USER_ID));

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false);
    });

    await act(async () => {
      await result.current.updatePreferences({ dueWithinDays: 5 });
    });

    expect(result.current.error).toBe('save failed');
  });
});
