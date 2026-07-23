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
    listWorkOffices: jest.fn().mockResolvedValue([{ id: 'wo-1', name: 'Chicago' }]),
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

  it('cold-start (MA-1): incomplete profile → coldStart + needsProfile flagged, does NOT auto-open', async () => {
    const port = fakePort(null);
    const { result } = renderHook(() =>
      useMyAssistant({ port, getUserId: () => USER, getDisplayName: () => 'Ada' })
    );
    await waitFor(() => expect(result.current.coldStart).toBe(true));
    // MA-1: the questionnaire no longer auto-opens — the host shows a dismissible nudge instead.
    expect(result.current.open).toBe(false);
    expect(result.current.needsProfile).toBe(true);
    expect(result.current.available).toBe(true);
    expect(port.listPracticeAreas).toHaveBeenCalled();
    // MA-3: work offices load alongside practice areas.
    await waitFor(() => expect(result.current.workOffices).toEqual([{ id: 'wo-1', name: 'Chicago' }]));
    expect(port.listWorkOffices).toHaveBeenCalled();
  });

  it('completed profile → no cold-start, no needs-profile, no auto-open, values prefilled', async () => {
    const port = fakePort(completeRow);
    const { result } = renderHook(() =>
      useMyAssistant({ port, getUserId: () => USER })
    );
    await waitFor(() => expect(result.current.initialValues.primaryRole).toBe(100000001));
    expect(result.current.coldStart).toBe(false);
    expect(result.current.needsProfile).toBe(false);
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
    // MA-1: a successful save also clears the needs-profile nudge.
    expect(result.current.needsProfile).toBe(false);
  });

  it('onSubmit seeds User-scope memory via POST /api/memory/user/seed after the profile save', async () => {
    const port = fakePort(null);
    const authenticatedFetch = jest
      .fn()
      .mockResolvedValue({ ok: true, status: 200 } as Response);
    const { result } = renderHook(() =>
      useMyAssistant({
        port,
        getUserId: () => USER,
        getDisplayName: () => 'Ada',
        authenticatedFetch,
        bffBaseUrl: 'https://bff.example',
      })
    );
    await waitFor(() => expect(result.current.coldStart).toBe(true));

    await act(async () => {
      await result.current.onSubmit({
        primaryRole: 100000001,
        practiceAreaIds: ['pa-1'],
        focusAreas: 'M&A',
        officeLocation: 'London',
        assistantPreferences: 'Concise, no legalese',
      });
    });

    // Profile persisted first, then the best-effort BFF seed call.
    expect(port.upsertProfileByUser).toHaveBeenCalledTimes(1);
    expect(authenticatedFetch).toHaveBeenCalledTimes(1);
    const [url, init] = authenticatedFetch.mock.calls[0];
    expect(url).toBe('https://bff.example/api/memory/user/seed');
    expect(init.method).toBe('POST');
    const body = JSON.parse(init.body as string);
    expect(body).toMatchObject({
      factType: 'keyFact',
      key: 'Assistant preferences',
      value: 'Concise, no legalese',
    });
    // The subject/systemuserid is NEVER in the client body — the server resolves it from auth.
    expect(body).not.toHaveProperty('userId');
    expect(body).not.toHaveProperty('subjectId');
  });

  it('onSubmit does not call the seed endpoint when there are no free-text preferences', async () => {
    const port = fakePort(null);
    const authenticatedFetch = jest.fn().mockResolvedValue({ ok: true, status: 200 } as Response);
    const { result } = renderHook(() =>
      useMyAssistant({
        port,
        getUserId: () => USER,
        authenticatedFetch,
        bffBaseUrl: 'https://bff.example',
      })
    );
    await waitFor(() => expect(result.current.coldStart).toBe(true));

    await act(async () => {
      await result.current.onSubmit({
        primaryRole: 100000001,
        practiceAreaIds: ['pa-1'],
        focusAreas: '',
        officeLocation: '',
        assistantPreferences: '   ',
      });
    });

    expect(port.upsertProfileByUser).toHaveBeenCalledTimes(1);
    expect(authenticatedFetch).not.toHaveBeenCalled();
  });

  it('onSubmit: a failing seed call NEVER blocks the profile save', async () => {
    const port = fakePort(null);
    const authenticatedFetch = jest.fn().mockRejectedValue(new Error('bff down'));
    const { result } = renderHook(() =>
      useMyAssistant({
        port,
        getUserId: () => USER,
        authenticatedFetch,
        bffBaseUrl: 'https://bff.example',
      })
    );
    await waitFor(() => expect(result.current.coldStart).toBe(true));

    await act(async () => {
      // Must resolve (not throw) despite the seed call rejecting.
      await result.current.onSubmit({
        primaryRole: 100000001,
        practiceAreaIds: ['pa-1'],
        focusAreas: 'M&A',
        officeLocation: 'London',
        assistantPreferences: 'Concise',
      });
    });

    expect(port.upsertProfileByUser).toHaveBeenCalledTimes(1);
    expect(authenticatedFetch).toHaveBeenCalledTimes(1);
    // The profile save completed: the cold-start gate cleared even though the seed failed.
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
