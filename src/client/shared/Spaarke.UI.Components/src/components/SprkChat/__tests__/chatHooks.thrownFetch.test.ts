/**
 * SprkChat data hooks - the failure path of the injected `authenticatedFetch`.
 *
 * `authenticatedFetch` (`@spaarke/auth`) never RETURNS a non-2xx: it throws `ApiError` / `AuthError`.
 * `useChatContextMapping`, `useChatPlaybooks` and `useDynamicSlashCommands` handle that in their
 * `catch`; these tests throw the real classes and pin the user-visible outcome of each hook.
 */
import { renderHook, waitFor } from '@testing-library/react';
import { useChatContextMapping } from '../hooks/useChatContextMapping';
import { useChatPlaybooks } from '../hooks/useChatPlaybooks';
import { useDynamicSlashCommands } from '../hooks/useDynamicSlashCommands';
import { apiErrorFor, authExhausted } from '../../../__tests__/helpers/authenticatedFetchDouble';

const BASE = 'https://bff.example';

describe('useChatContextMapping - thrown failures', () => {
  it('surfaces a thrown ApiError as `error` and clears the mapping', async () => {
    const fetchMock = jest.fn().mockRejectedValue(apiErrorFor(404, { title: 'Not Found', status: 404 }));
    const { result } = renderHook(() =>
      useChatContextMapping({ analysisId: 'a-1', playbookId: 'p-1', apiBaseUrl: BASE, authenticatedFetch: fetchMock })
    );

    await waitFor(() => expect(result.current.error).not.toBeNull());
    expect(result.current.error?.message).toBe('Not Found');
    expect(result.current.contextMapping).toBeNull();
    expect(result.current.isLoading).toBe(false);
  });
});

describe('useChatPlaybooks - thrown failures', () => {
  it('surfaces a thrown ApiError as `error` with an empty list', async () => {
    const fetchMock = jest.fn().mockRejectedValue(apiErrorFor(500));
    const { result } = renderHook(() => useChatPlaybooks({ apiBaseUrl: BASE, authenticatedFetch: fetchMock }));

    await waitFor(() => expect(result.current.error).not.toBeNull());
    expect(result.current.error?.message).toBe('HTTP 500');
    expect(result.current.playbooks).toEqual([]);
  });

  it('surfaces an exhausted sign-in (AuthError) as `error`', async () => {
    const fetchMock = jest.fn().mockRejectedValue(authExhausted());
    const { result } = renderHook(() => useChatPlaybooks({ apiBaseUrl: BASE, authenticatedFetch: fetchMock }));

    await waitFor(() => expect(result.current.error).not.toBeNull());
    expect(result.current.playbooks).toEqual([]);
  });
});

describe('useDynamicSlashCommands - thrown failures', () => {
  beforeEach(() => {
    jest.spyOn(console, 'warn').mockImplementation(() => undefined);
  });
  afterEach(() => jest.restoreAllMocks());

  it('falls back to the system commands only (no dynamic commands, no throw) when the fetch throws', async () => {
    const fetchMock = jest.fn().mockRejectedValue(apiErrorFor(503));
    const { result } = renderHook(() =>
      useDynamicSlashCommands({ sessionId: 's-1', apiBaseUrl: BASE, authenticatedFetch: fetchMock })
    );

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(result.current.dynamicCommands).toEqual([]);
    expect(result.current.commands.length).toBeGreaterThan(0);
  });
});
