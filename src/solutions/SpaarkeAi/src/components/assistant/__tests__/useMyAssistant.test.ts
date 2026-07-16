/**
 * useMyAssistant.test.ts — task 042 cold-start gate + orchestration (FR-F3 / F5).
 *
 * Injects the port + user-id resolver (the hook's test seams) so no Xrm/barrel mock is needed.
 */

import { renderHook, act, waitFor } from '@testing-library/react';
import { useMyAssistant } from '../useMyAssistant';
import type { IUserProfilePort, UserProfileRow } from '../userProfileService';

const USER = '11111111-1111-1111-1111-111111111111';
const PROFILE_ID = '22222222-2222-2222-2222-222222222222';

function fakePort(existing: UserProfileRow | null): jest.Mocked<IUserProfilePort> {
  return {
    getProfileByUser: jest.fn().mockResolvedValue(existing),
    listPracticeAreas: jest
      .fn()
      .mockResolvedValue([{ id: 'pa-1', name: 'Appellate', code: 'APPL' }]),
    upsertProfileByUser: jest.fn().mockResolvedValue(PROFILE_ID),
    associatePracticeArea: jest.fn().mockResolvedValue(undefined),
    disassociatePracticeArea: jest.fn().mockResolvedValue(undefined),
    deleteProfile: jest.fn().mockResolvedValue(undefined),
  };
}

const completeRow: UserProfileRow = {
  id: PROFILE_ID,
  profileCompletedOn: '2026-07-16T00:00:00Z',
  primaryRole: 100000001,
  focusAreas: 'M&A',
  officeLocation: 'London',
  assistantPreferences: 'Concise',
  profileVersion: 1,
  practiceAreaIds: ['pa-1'],
};

describe('useMyAssistant', () => {
  it('is inert when no Dataverse user id is available (jsdom / non-MDA)', async () => {
    const port = fakePort(null);
    const { result } = renderHook(() =>
      useMyAssistant({ port, getUserId: () => undefined })
    );
    expect(result.current.available).toBe(false);
    expect(result.current.open).toBe(false);
    // No prefetch happens without a user id.
    expect(port.getProfileByUser).not.toHaveBeenCalled();
  });

  it('cold-start: no completed profile → coldStart flagged + auto-opens once', async () => {
    const port = fakePort(null);
    const { result } = renderHook(() =>
      useMyAssistant({ port, getUserId: () => USER, getDisplayName: () => 'Ada' })
    );
    await waitFor(() => expect(result.current.coldStart).toBe(true));
    expect(result.current.open).toBe(true);
    expect(result.current.available).toBe(true);
    expect(port.listPracticeAreas).toHaveBeenCalled();
  });

  it('completed profile → no cold-start, no auto-open, values prefilled', async () => {
    const port = fakePort(completeRow);
    const { result } = renderHook(() =>
      useMyAssistant({ port, getUserId: () => USER })
    );
    await waitFor(() => expect(result.current.initialValues.primaryRole).toBe(100000001));
    expect(result.current.coldStart).toBe(false);
    expect(result.current.open).toBe(false);
    expect(result.current.initialValues.practiceAreaIds).toEqual(['pa-1']);
  });

  it('onSubmit persists and clears the cold-start gate', async () => {
    const port = fakePort(null);
    const { result } = renderHook(() =>
      useMyAssistant({ port, getUserId: () => USER, getDisplayName: () => 'Ada' })
    );
    await waitFor(() => expect(result.current.coldStart).toBe(true));

    await act(async () => {
      await result.current.onSubmit({
        primaryRole: 100000001,
        practiceAreaIds: ['pa-1'],
        focusAreas: 'M&A',
        officeLocation: 'London',
        assistantPreferences: 'Concise',
      });
    });

    expect(port.upsertProfileByUser).toHaveBeenCalledTimes(1);
    expect(result.current.coldStart).toBe(false);
  });

  it('onErase deletes the profile and calls the existing DELETE /api/memory/user endpoint', async () => {
    const port = fakePort(completeRow);
    const authenticatedFetch = jest
      .fn()
      .mockResolvedValue({ ok: true, status: 204 } as Response);
    const { result } = renderHook(() =>
      useMyAssistant({
        port,
        getUserId: () => USER,
        authenticatedFetch,
        bffBaseUrl: 'https://bff.example',
      })
    );
    await waitFor(() => expect(port.getProfileByUser).toHaveBeenCalled());

    await act(async () => {
      await result.current.onErase();
    });

    expect(port.deleteProfile).toHaveBeenCalledWith(PROFILE_ID);
    expect(authenticatedFetch).toHaveBeenCalledWith('https://bff.example/api/memory/user', {
      method: 'DELETE',
    });
    expect(result.current.coldStart).toBe(true);
  });
});
