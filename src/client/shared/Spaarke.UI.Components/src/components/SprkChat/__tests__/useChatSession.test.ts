/**
 * useChatSession Hook Tests
 *
 * Tests session creation, history loading, context switching, and deletion.
 *
 * Auth v2 (D-AUTH-1): the hook now takes `authenticatedFetch` as a prop. Tests
 * mock it as a wrapper around the global `fetch` that attaches a fixed Bearer
 * header, mirroring the production `@spaarke/auth` implementation.
 *
 * @see ADR-022 - React 16 APIs only
 */

import { renderHook, act } from '@testing-library/react';
import { useChatSession } from '../hooks/useChatSession';
import { throwingAuthenticatedFetch } from '../../../__tests__/helpers/authenticatedFetchDouble';

// ---------------------------------------------------------------------------
// Mock fetch
// ---------------------------------------------------------------------------

const mockFetch = jest.fn();
(global as any).fetch = mockFetch;

function createFetchResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    text: jest.fn().mockResolvedValue(JSON.stringify(body)),
    json: jest.fn().mockResolvedValue(body),
    headers: new Headers(),
  } as unknown as Response;
}

// Test-only authenticatedFetch impl — wraps the mocked global fetch and
// attaches a fixed Bearer header, the same surface real authenticatedFetch
// presents to caller hooks: like it, a non-2xx is THROWN (AuthError for 401,
// ApiError otherwise), never returned.
const mockAuthenticatedFetch = throwingAuthenticatedFetch((url: string, init?: RequestInit) =>
  mockFetch(url, {
    ...init,
    headers: {
      ...(init?.headers ?? {}),
      Authorization: 'Bearer test-token',
    },
  })
);

const DEFAULT_OPTIONS = {
  apiBaseUrl: 'https://api.example.com',
  authenticatedFetch: mockAuthenticatedFetch,
};

beforeEach(() => {
  jest.clearAllMocks();
});

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('useChatSession', () => {
  describe('initial state', () => {
    it('should have null session and empty messages initially', () => {
      const { result } = renderHook(() => useChatSession(DEFAULT_OPTIONS));

      expect(result.current.session).toBeNull();
      expect(result.current.messages).toEqual([]);
      expect(result.current.isLoading).toBe(false);
      expect(result.current.error).toBeNull();
    });
  });

  describe('createSession', () => {
    it('should create a new session', async () => {
      const sessionResponse = {
        sessionId: 'session-123',
        createdAt: '2026-01-01T00:00:00Z',
      };
      mockFetch.mockResolvedValueOnce(createFetchResponse(sessionResponse));

      const { result } = renderHook(() => useChatSession(DEFAULT_OPTIONS));

      let createdSession: any;
      await act(async () => {
        createdSession = await result.current.createSession('doc-1', 'playbook-1');
      });

      expect(createdSession).toEqual(sessionResponse);
      expect(result.current.session).toEqual(sessionResponse);
      expect(result.current.messages).toEqual([]);
      expect(result.current.isLoading).toBe(false);
      // Task 071: useChatSession.createSession now always serialises `hostContext`
      // in the body (defaults to null when not provided). Updated body assertion
      // to match the explicit-null contract.
      expect(mockFetch).toHaveBeenCalledWith(
        'https://api.example.com/api/ai/chat/sessions',
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify({
            documentId: 'doc-1',
            playbookId: 'playbook-1',
            hostContext: null,
          }),
        })
      );
    });

    it('should send Authorization header', async () => {
      mockFetch.mockResolvedValueOnce(
        createFetchResponse({
          sessionId: 's1',
          createdAt: '2026-01-01T00:00:00Z',
        })
      );

      const { result } = renderHook(() => useChatSession(DEFAULT_OPTIONS));
      await act(async () => {
        await result.current.createSession();
      });

      const headers = mockFetch.mock.calls[0][1].headers;
      expect(headers['Authorization']).toBe('Bearer test-token');
    });

    it('should handle API errors gracefully', async () => {
      mockFetch.mockResolvedValueOnce(createFetchResponse('Unauthorized', 401));

      const { result } = renderHook(() => useChatSession(DEFAULT_OPTIONS));
      let created: any;
      await act(async () => {
        created = await result.current.createSession();
      });

      expect(created).toBeNull();
      expect(result.current.error).not.toBeNull();
      expect(result.current.isLoading).toBe(false);
    });

    it('should handle network errors', async () => {
      mockFetch.mockRejectedValueOnce(new Error('Network error'));

      const { result } = renderHook(() => useChatSession(DEFAULT_OPTIONS));
      await act(async () => {
        await result.current.createSession();
      });

      expect(result.current.error).not.toBeNull();
      expect(result.current.error!.message).toBe('Network error');
    });

    it('should strip trailing slashes from apiBaseUrl', async () => {
      mockFetch.mockResolvedValueOnce(
        createFetchResponse({
          sessionId: 's1',
          createdAt: '2026-01-01T00:00:00Z',
        })
      );

      const { result } = renderHook(() =>
        useChatSession({
          ...DEFAULT_OPTIONS,
          apiBaseUrl: 'https://api.example.com///',
        })
      );
      await act(async () => {
        await result.current.createSession();
      });

      expect(mockFetch.mock.calls[0][0]).toBe('https://api.example.com/api/ai/chat/sessions');
    });
  });

  describe('loadHistory', () => {
    it('should load message history for the current session', async () => {
      const sessionResponse = {
        sessionId: 'session-123',
        createdAt: '2026-01-01T00:00:00Z',
      };
      const historyResponse = {
        messages: [
          { role: 'User', content: 'Hello', timestamp: '2026-01-01T00:00:01Z' },
          {
            role: 'Assistant',
            content: 'Hi!',
            timestamp: '2026-01-01T00:00:02Z',
          },
        ],
      };

      mockFetch
        .mockResolvedValueOnce(createFetchResponse(sessionResponse))
        .mockResolvedValueOnce(createFetchResponse(historyResponse));

      const { result } = renderHook(() => useChatSession(DEFAULT_OPTIONS));

      await act(async () => {
        await result.current.createSession();
      });

      await act(async () => {
        await result.current.loadHistory();
      });

      expect(result.current.messages).toHaveLength(2);
      expect(result.current.messages[0].role).toBe('User');
      expect(result.current.messages[1].role).toBe('Assistant');
    });

    it('should do nothing if no session exists', async () => {
      const { result } = renderHook(() => useChatSession(DEFAULT_OPTIONS));

      await act(async () => {
        await result.current.loadHistory();
      });

      expect(mockFetch).not.toHaveBeenCalled();
    });

    it('reports staleSession when the server no longer has the session (404 thrown as ApiError)', async () => {
      const { result } = renderHook(() => useChatSession(DEFAULT_OPTIONS));
      act(() => result.current.resumeSession('expired-session'));
      mockFetch.mockResolvedValueOnce(createFetchResponse({ title: 'Not Found', status: 404 }, 404));

      let outcome: { ok: boolean; staleSession?: boolean } | undefined;
      await act(async () => {
        outcome = await result.current.loadHistory();
      });

      expect(outcome).toEqual({ ok: false, staleSession: true });
      // A stale session is a signal for the host, not an error to show.
      expect(result.current.error).toBeNull();
      expect(result.current.isLoading).toBe(false);
    });

    it('reports a non-404 failure as an error, not a stale session', async () => {
      const { result } = renderHook(() => useChatSession(DEFAULT_OPTIONS));
      act(() => result.current.resumeSession('session-1'));
      mockFetch.mockResolvedValueOnce(createFetchResponse({ title: 'Server error', status: 500 }, 500));

      let outcome: { ok: boolean; staleSession?: boolean } | undefined;
      await act(async () => {
        outcome = await result.current.loadHistory();
      });

      expect(outcome).toEqual({ ok: false });
      expect(result.current.error).not.toBeNull();
      expect(result.current.error!.message).toBe('Failed to load history (500): Server error');
    });
  });

  // The `!response.ok` branches build "<action> (<status>): <reason>"; under the throwing
  // authenticatedFetch that message has to be rebuilt in the catch (before: the bare ApiError text).
  describe('error messages for thrown failures', () => {
    it('createSession: "(status): <ProblemDetails detail>"', async () => {
      mockFetch.mockResolvedValueOnce(
        createFetchResponse({ title: 'Service Unavailable', status: 503, detail: 'AI is unavailable.' }, 503)
      );
      const { result } = renderHook(() => useChatSession(DEFAULT_OPTIONS));
      await act(async () => {
        await result.current.createSession();
      });

      expect(result.current.error!.message).toBe('Failed to create session (503): AI is unavailable.');
    });

    it('createSession: an exhausted sign-in is reported as 401', async () => {
      mockFetch.mockResolvedValueOnce(createFetchResponse('Unauthorized', 401));
      const { result } = renderHook(() => useChatSession(DEFAULT_OPTIONS));
      await act(async () => {
        await result.current.createSession();
      });

      expect(result.current.error!.message).toBe(
        'Failed to create session (401): Authentication failed after all retry attempts'
      );
    });

    it('switchContext: "(status): <reason>"', async () => {
      const { result } = renderHook(() => useChatSession(DEFAULT_OPTIONS));
      act(() => result.current.resumeSession('session-1'));
      mockFetch.mockResolvedValueOnce(
        createFetchResponse({ title: 'Bad Request', status: 400, detail: 'Too many documents.' }, 400)
      );
      await act(async () => {
        await result.current.switchContext('doc-1');
      });

      expect(result.current.error!.message).toBe('Failed to switch context (400): Too many documents.');
    });

    it('deleteSession: "(status): <reason>" and the session is kept', async () => {
      const { result } = renderHook(() => useChatSession(DEFAULT_OPTIONS));
      act(() => result.current.resumeSession('session-1'));
      mockFetch.mockResolvedValueOnce(createFetchResponse({}, 500));
      await act(async () => {
        await result.current.deleteSession();
      });

      expect(result.current.error!.message).toBe('Failed to delete session (500): HTTP 500');
      expect(result.current.session).not.toBeNull();
    });
  });

  describe('deleteSession', () => {
    it('should delete the current session', async () => {
      mockFetch
        .mockResolvedValueOnce(
          createFetchResponse({
            sessionId: 's1',
            createdAt: '2026-01-01T00:00:00Z',
          })
        )
        .mockResolvedValueOnce(createFetchResponse(null, 204));

      const { result } = renderHook(() => useChatSession(DEFAULT_OPTIONS));

      await act(async () => {
        await result.current.createSession();
      });

      await act(async () => {
        await result.current.deleteSession();
      });

      expect(result.current.session).toBeNull();
      expect(result.current.messages).toEqual([]);
    });

    it('should do nothing if no session exists', async () => {
      const { result } = renderHook(() => useChatSession(DEFAULT_OPTIONS));

      await act(async () => {
        await result.current.deleteSession();
      });

      expect(mockFetch).not.toHaveBeenCalled();
    });
  });

  describe('addMessage', () => {
    it('should add a message to local history', () => {
      const { result } = renderHook(() => useChatSession(DEFAULT_OPTIONS));

      act(() => {
        result.current.addMessage({
          role: 'User',
          content: 'Test message',
          timestamp: '2026-01-01T00:00:00Z',
        });
      });

      expect(result.current.messages).toHaveLength(1);
      expect(result.current.messages[0].content).toBe('Test message');
    });
  });

  describe('updateLastMessage', () => {
    it('should update the content of the last message', () => {
      const { result } = renderHook(() => useChatSession(DEFAULT_OPTIONS));

      act(() => {
        result.current.addMessage({
          role: 'Assistant',
          content: 'Hello',
          timestamp: '2026-01-01T00:00:00Z',
        });
      });

      act(() => {
        result.current.updateLastMessage('Hello world!');
      });

      expect(result.current.messages[0].content).toBe('Hello world!');
    });

    it('should do nothing if messages array is empty', () => {
      const { result } = renderHook(() => useChatSession(DEFAULT_OPTIONS));

      act(() => {
        result.current.updateLastMessage('No messages');
      });

      expect(result.current.messages).toHaveLength(0);
    });
  });

  describe('switchContext', () => {
    it('should call PATCH endpoint for context switch', async () => {
      mockFetch
        .mockResolvedValueOnce(
          createFetchResponse({
            sessionId: 's1',
            createdAt: '2026-01-01T00:00:00Z',
          })
        )
        .mockResolvedValueOnce(createFetchResponse(null, 200));

      const { result } = renderHook(() => useChatSession(DEFAULT_OPTIONS));

      await act(async () => {
        await result.current.createSession();
      });

      await act(async () => {
        await result.current.switchContext('new-doc', 'new-playbook');
      });

      expect(mockFetch).toHaveBeenCalledTimes(2);
      const switchCall = mockFetch.mock.calls[1];
      expect(switchCall[0]).toBe('https://api.example.com/api/ai/chat/sessions/s1/context');
      expect(switchCall[1].method).toBe('PATCH');
    });
  });
});
