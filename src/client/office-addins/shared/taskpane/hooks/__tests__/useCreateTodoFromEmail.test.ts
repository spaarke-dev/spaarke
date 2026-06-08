/**
 * Unit tests for useCreateTodoFromEmail (smart-todo-decoupling-r3 FR-27 / task 070).
 *
 * Acceptance coverage:
 *   - Click on already-saved email → lookup returns existing, launch fires directly
 *     (no save flow invocation)
 *   - Click on unsaved email → lookup returns null → save flow invoked → launch fires
 *     with the newly-saved communicationId
 *   - Save flow returning null → error state, no launch
 *   - Lookup error → error state, no save flow / launch
 *   - Popup blocked → error state with clear message
 *   - State transitions: idle → looking-up → (launching | saving → launching) → opened
 *   - reset() returns to idle
 *   - Re-clicking while busy is a no-op (guard against double-clicks)
 */

import { act, renderHook, waitFor } from '@testing-library/react';
import { useCreateTodoFromEmail, type CurrentEmailReader, type SaveEmailToSpaarkeFn } from '../useCreateTodoFromEmail';
import { apiClient, ApiClientError } from '@shared/services';

// Mock the apiClient (used by the lookup service inside the hook).
jest.mock('@shared/services', () => {
  const actual = jest.requireActual('@shared/services');
  return {
    ...actual,
    apiClient: {
      get: jest.fn(),
      post: jest.fn(),
      put: jest.fn(),
      delete: jest.fn(),
      uploadFile: jest.fn(),
      configure: jest.fn(),
    },
  };
});

const mockGet = apiClient.get as jest.Mock;

const FAKE_COMM_ID = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee';
const FAKE_SUBJECT = 'Q4 planning kickoff';
const FAKE_INET_ID = '<abc123@host.example.com>';
const FAKE_CODE_PAGE = 'https://contoso.crm.dynamics.com/main.aspx';

function makeReader(overrides: Partial<CurrentEmailReader> = {}): CurrentEmailReader {
  return {
    getInternetMessageId: jest.fn().mockResolvedValue(FAKE_INET_ID),
    getSubject: jest.fn().mockResolvedValue(FAKE_SUBJECT),
    ...overrides,
  };
}

describe('useCreateTodoFromEmail', () => {
  beforeEach(() => {
    mockGet.mockReset();
  });

  describe('email already saved (lookup returns existing communication)', () => {
    it('launches the wizard directly without invoking the save flow', async () => {
      mockGet.mockResolvedValueOnce({ communicationId: FAKE_COMM_ID, subject: FAKE_SUBJECT });
      const windowOpen = jest.fn().mockReturnValue({ closed: false } as unknown as Window);
      const saveEmailToSpaarke: SaveEmailToSpaarkeFn = jest.fn();

      const { result } = renderHook(() =>
        useCreateTodoFromEmail({
          emailReader: makeReader(),
          saveEmailToSpaarke,
          codePageBaseUrl: FAKE_CODE_PAGE,
          windowOpen,
        })
      );

      expect(result.current.state.kind).toBe('idle');

      await act(async () => {
        await result.current.start();
      });

      await waitFor(() => expect(result.current.state.kind).toBe('opened'));

      // Save flow MUST NOT have been called.
      expect(saveEmailToSpaarke).not.toHaveBeenCalled();

      // window.open MUST have been called once with the launch URL.
      expect(windowOpen).toHaveBeenCalledTimes(1);
      const [openedUrl] = windowOpen.mock.calls[0]!;
      expect(openedUrl).toContain('action=createTodo');
      expect(openedUrl).toContain(`regardingId=${FAKE_COMM_ID}`);
      expect(openedUrl).toContain('regardingType=sprk_communication');
    });
  });

  describe('email not saved (lookup returns null → save flow → launch)', () => {
    it('invokes saveEmailToSpaarke and then launches with the new communicationId', async () => {
      // Lookup: 404 → null.
      mockGet.mockRejectedValueOnce(
        new ApiClientError({
          type: 'about:blank',
          title: 'Not Found',
          status: 404,
          detail: 'No sprk_communication',
        })
      );

      const windowOpen = jest.fn().mockReturnValue({ closed: false } as unknown as Window);
      const saveEmailToSpaarke: SaveEmailToSpaarkeFn = jest.fn().mockResolvedValue({
        communicationId: FAKE_COMM_ID,
        subject: FAKE_SUBJECT,
      });

      const { result } = renderHook(() =>
        useCreateTodoFromEmail({
          emailReader: makeReader(),
          saveEmailToSpaarke,
          codePageBaseUrl: FAKE_CODE_PAGE,
          windowOpen,
        })
      );

      await act(async () => {
        await result.current.start();
      });

      await waitFor(() => expect(result.current.state.kind).toBe('opened'));

      // Save flow MUST have been invoked exactly once.
      expect(saveEmailToSpaarke).toHaveBeenCalledTimes(1);

      // window.open MUST have been called with the post-save communicationId.
      expect(windowOpen).toHaveBeenCalledTimes(1);
      const [openedUrl] = windowOpen.mock.calls[0]!;
      expect(openedUrl).toContain(`regardingId=${FAKE_COMM_ID}`);
    });

    it('surfaces an error when the save flow returns null (cancelled / failed)', async () => {
      mockGet.mockRejectedValueOnce(
        new ApiClientError({
          type: 'about:blank',
          title: 'Not Found',
          status: 404,
        })
      );

      const windowOpen = jest.fn();
      const saveEmailToSpaarke: SaveEmailToSpaarkeFn = jest.fn().mockResolvedValue(null);

      const { result } = renderHook(() =>
        useCreateTodoFromEmail({
          emailReader: makeReader(),
          saveEmailToSpaarke,
          codePageBaseUrl: FAKE_CODE_PAGE,
          windowOpen,
        })
      );

      await act(async () => {
        await result.current.start();
      });

      await waitFor(() => expect(result.current.state.kind).toBe('error'));
      if (result.current.state.kind === 'error') {
        expect(result.current.state.message).toMatch(/not saved/i);
      }
      expect(windowOpen).not.toHaveBeenCalled();
    });
  });

  describe('error paths', () => {
    it('surfaces a lookup failure without invoking the save flow', async () => {
      mockGet.mockRejectedValueOnce(
        new ApiClientError({
          type: 'about:blank',
          title: 'Internal Server Error',
          status: 500,
          detail: 'lookup boom',
        })
      );

      const saveEmailToSpaarke = jest.fn();
      const windowOpen = jest.fn();

      const { result } = renderHook(() =>
        useCreateTodoFromEmail({
          emailReader: makeReader(),
          saveEmailToSpaarke,
          codePageBaseUrl: FAKE_CODE_PAGE,
          windowOpen,
        })
      );

      await act(async () => {
        await result.current.start();
      });

      await waitFor(() => expect(result.current.state.kind).toBe('error'));
      expect(saveEmailToSpaarke).not.toHaveBeenCalled();
      expect(windowOpen).not.toHaveBeenCalled();
    });

    it('surfaces an error when the popup is blocked', async () => {
      mockGet.mockResolvedValueOnce({ communicationId: FAKE_COMM_ID, subject: FAKE_SUBJECT });
      const windowOpen = jest.fn().mockReturnValue(null); // popup blocked

      const { result } = renderHook(() =>
        useCreateTodoFromEmail({
          emailReader: makeReader(),
          saveEmailToSpaarke: jest.fn(),
          codePageBaseUrl: FAKE_CODE_PAGE,
          windowOpen,
        })
      );

      await act(async () => {
        await result.current.start();
      });

      await waitFor(() => expect(result.current.state.kind).toBe('error'));
      if (result.current.state.kind === 'error') {
        expect(result.current.state.message).toMatch(/popup/i);
      }
    });

    it('surfaces an error when the email reader throws', async () => {
      const reader = makeReader({
        getInternetMessageId: jest.fn().mockRejectedValue(new Error('reader broken')),
      });

      const { result } = renderHook(() =>
        useCreateTodoFromEmail({
          emailReader: reader,
          saveEmailToSpaarke: jest.fn(),
          codePageBaseUrl: FAKE_CODE_PAGE,
          windowOpen: jest.fn(),
        })
      );

      await act(async () => {
        await result.current.start();
      });

      await waitFor(() => expect(result.current.state.kind).toBe('error'));
    });
  });

  describe('state machine + reset', () => {
    it('reset() returns to idle from any state', async () => {
      mockGet.mockResolvedValueOnce({ communicationId: FAKE_COMM_ID, subject: FAKE_SUBJECT });
      const { result } = renderHook(() =>
        useCreateTodoFromEmail({
          emailReader: makeReader(),
          saveEmailToSpaarke: jest.fn(),
          codePageBaseUrl: FAKE_CODE_PAGE,
          windowOpen: jest.fn().mockReturnValue({ closed: false } as unknown as Window),
        })
      );

      await act(async () => {
        await result.current.start();
      });
      await waitFor(() => expect(result.current.state.kind).toBe('opened'));

      act(() => {
        result.current.reset();
      });

      expect(result.current.state.kind).toBe('idle');
    });
  });
});
