/**
 * SprkChat Component Tests
 *
 * Integration tests for the main SprkChat orchestrator component.
 * Tests session initialization, message rendering, input interaction,
 * context selector display, predefined prompts, and error handling.
 *
 * @see ADR-012 - Shared Component Library
 * @see ADR-021 - Fluent UI v9 (dark mode compliance)
 * @see ADR-022 - React 16 APIs only
 */

import * as React from 'react';
import { screen, waitFor, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { SprkChat } from '../SprkChat';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';

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

// Auth v2 (D-AUTH-1): SprkChat takes authenticatedFetch + getAccessToken instead of
// a snapshotted accessToken string. The test wires both to the mocked fetch so
// existing fetch-assertions remain valid.
const mockAuthenticatedFetch = (url: string, init?: RequestInit) =>
  mockFetch(url, {
    ...init,
    headers: {
      ...(init?.headers ?? {}),
      Authorization: 'Bearer test-access-token',
    },
  });
const mockGetAccessToken = () => Promise.resolve('test-access-token');

const defaultProps = {
  playbookId: 'test-playbook-id',
  apiBaseUrl: 'https://api.example.com',
  authenticatedFetch: mockAuthenticatedFetch,
  getAccessToken: mockGetAccessToken,
};

describe('SprkChat', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    // Default: successful session creation
    mockFetch.mockResolvedValue(
      createFetchResponse({
        sessionId: 'session-123',
        createdAt: '2026-02-23T10:00:00Z',
      })
    );
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  describe('Rendering', () => {
    it('should render the chat root element', async () => {
      await act(async () => {
        renderWithProviders(<SprkChat {...defaultProps} />);
      });

      expect(screen.getByTestId('sprkchat-root')).toBeInTheDocument();
    });

    it('should render the message list area', async () => {
      await act(async () => {
        renderWithProviders(<SprkChat {...defaultProps} />);
      });

      expect(screen.getByTestId('chat-message-list')).toBeInTheDocument();
    });

    it('should render empty state message when no messages', async () => {
      await act(async () => {
        renderWithProviders(<SprkChat {...defaultProps} />);
      });

      await waitFor(() => {
        expect(screen.getByText('No messages yet')).toBeInTheDocument();
      });
    });

    it('should apply custom className to root', async () => {
      await act(async () => {
        renderWithProviders(<SprkChat {...defaultProps} className="my-custom-class" />);
      });

      const root = screen.getByTestId('sprkchat-root');
      expect(root.className).toContain('my-custom-class');
    });
  });

  describe('Session Initialization', () => {
    it('should create a new session on mount', async () => {
      await act(async () => {
        renderWithProviders(<SprkChat {...defaultProps} />);
      });

      await waitFor(() => {
        expect(mockFetch).toHaveBeenCalledWith(
          'https://api.example.com/api/ai/chat/sessions',
          expect.objectContaining({
            method: 'POST',
          })
        );
      });
    });

    it('should call onSessionCreated callback when session is created', async () => {
      const onSessionCreated = jest.fn();

      await act(async () => {
        renderWithProviders(<SprkChat {...defaultProps} onSessionCreated={onSessionCreated} />);
      });

      await waitFor(() => {
        expect(onSessionCreated).toHaveBeenCalledWith(
          expect.objectContaining({
            sessionId: 'session-123',
          })
        );
      });
    });

    it('should show error banner on session creation failure', async () => {
      // Task 071: SprkChat now uses useChatPlaybooks + useChatContextMapping +
      // useDynamicSlashCommands which all fetch on mount BEFORE createSession.
      // The single `mockResolvedValueOnce` was being consumed by useChatPlaybooks,
      // letting createSession hit the default 200 success. Reset and make all
      // mounted fetches fail so we definitely exercise the session-error path.
      mockFetch.mockReset();
      mockFetch.mockResolvedValue(createFetchResponse('Unauthorized', 401));

      await act(async () => {
        renderWithProviders(<SprkChat {...defaultProps} />);
      });

      // React 19 effects + RTL v16 batching: flush microtasks before assertion.
      await act(async () => {
        await new Promise(r => setTimeout(r, 0));
      });

      await waitFor(
        () => {
          expect(screen.getByTestId('chat-error-banner')).toBeInTheDocument();
        },
        { timeout: 3000 },
      );
    });
  });

  describe('Context Selector', () => {
    it('should render context selector when documents are provided', async () => {
      const documents = [
        { id: 'doc-1', name: 'Contract.pdf' },
        { id: 'doc-2', name: 'Agreement.docx' },
      ];

      await act(async () => {
        renderWithProviders(<SprkChat {...defaultProps} documents={documents} />);
      });

      expect(screen.getByText('Document:')).toBeInTheDocument();
    });

    it('should render context selector when playbooks are provided', async () => {
      const playbooks = [{ id: 'pb-1', name: 'Legal Review' }];

      await act(async () => {
        renderWithProviders(<SprkChat {...defaultProps} playbooks={playbooks} />);
      });

      expect(screen.getByText('Playbook:')).toBeInTheDocument();
    });

    it('should not render context selector when no documents or playbooks', async () => {
      await act(async () => {
        renderWithProviders(<SprkChat {...defaultProps} />);
      });

      expect(screen.queryByText('Document:')).not.toBeInTheDocument();
      expect(screen.queryByText('Playbook:')).not.toBeInTheDocument();
    });
  });

  describe('Predefined Prompts', () => {
    const prompts = [
      {
        key: 'summary',
        label: 'Summarize',
        prompt: 'Summarize this document.',
      },
      { key: 'review', label: 'Review', prompt: 'Review for issues.' },
    ];

    it('should render predefined prompts when no messages exist', async () => {
      await act(async () => {
        renderWithProviders(<SprkChat {...defaultProps} predefinedPrompts={prompts} />);
      });

      await waitFor(() => {
        // Task 071: "Summarize" / "Review" labels now appear in multiple places
        // (predefined prompt + default slash command). Assert that at least one
        // instance exists.
        expect(screen.getAllByText('Summarize').length).toBeGreaterThan(0);
        expect(screen.getAllByText('Review').length).toBeGreaterThan(0);
      });
    });

    it('should not render empty state when predefined prompts are shown', async () => {
      await act(async () => {
        renderWithProviders(<SprkChat {...defaultProps} predefinedPrompts={prompts} />);
      });

      await waitFor(() => {
        // Task 071: see note on previous test — use getAllByText.
        expect(screen.getAllByText('Summarize').length).toBeGreaterThan(0);
      });

      expect(screen.queryByText('No messages yet')).not.toBeInTheDocument();
    });
  });

  describe('Input Behavior', () => {
    it('should render the chat input', async () => {
      await act(async () => {
        renderWithProviders(<SprkChat {...defaultProps} />);
      });

      expect(screen.getByTestId('chat-input-textarea')).toBeInTheDocument();
      expect(screen.getByTestId('chat-send-button')).toBeInTheDocument();
    });

    it('should keep input editable on cold load while session is being created (FR-06)', async () => {
      // FR-06: input is editable on cold load (no session yet). Previously
      // disabled while `useChatSession` was loading. Now only `isStreaming`
      // disables the input — typing is allowed during the mount-time
      // session-creation window and the message is sent after the session
      // becomes available.
      let resolveCreate: (value: any) => void;
      mockFetch.mockImplementationOnce(
        () =>
          new Promise(resolve => {
            resolveCreate = resolve;
          })
      );

      await act(async () => {
        renderWithProviders(<SprkChat {...defaultProps} />);
      });

      // Input should NOT be disabled while session is being created (FR-06)
      const textarea = screen.getByTestId('chat-input-textarea');
      const nativeTextarea = textarea.querySelector('textarea') || textarea;
      expect(nativeTextarea).not.toBeDisabled();

      // Resolve the session creation so cleanup proceeds cleanly
      await act(async () => {
        resolveCreate!(
          createFetchResponse({
            sessionId: 's1',
            createdAt: '2026-02-23T10:00:00Z',
          })
        );
      });
    });

    it('should create session on mount so first send routes to a ready session (FR-06)', async () => {
      // FR-06 acceptance: the existing mount-time createSession flow is
      // preserved. After mount, the session is available and the input
      // remains enabled (only `isStreaming` would disable it).
      await act(async () => {
        renderWithProviders(<SprkChat {...defaultProps} />);
      });

      // Confirm POST /sessions was issued — session is created so first
      // send routes through `handleSend` to a ready session.
      await waitFor(() => {
        expect(mockFetch).toHaveBeenCalledWith(
          'https://api.example.com/api/ai/chat/sessions',
          expect.objectContaining({ method: 'POST' })
        );
      });

      // Input remains enabled after session is established (no streaming).
      const textarea = screen.getByTestId('chat-input-textarea');
      const nativeTextarea = textarea.querySelector('textarea') || textarea;
      expect(nativeTextarea).not.toBeDisabled();
    });

    it('should accept custom maxCharCount', async () => {
      await act(async () => {
        renderWithProviders(<SprkChat {...defaultProps} maxCharCount={500} />);
      });

      expect(screen.getByText('0/500')).toBeInTheDocument();
    });
  });

  describe('Dark Mode Compliance', () => {
    it('should not have inline hardcoded color styles on root', async () => {
      await act(async () => {
        renderWithProviders(<SprkChat {...defaultProps} />);
      });

      const root = screen.getByTestId('sprkchat-root');
      // All colors should come from Fluent tokens via CSS classes, not inline
      expect(root.style.color).toBe('');
      expect(root.style.backgroundColor).toBe('');
    });
  });

  describe('Accessibility', () => {
    it('should have proper role on message list', async () => {
      await act(async () => {
        renderWithProviders(<SprkChat {...defaultProps} />);
      });

      expect(screen.getByRole('list')).toBeInTheDocument();
    });

    it('should have aria-label on message list', async () => {
      await act(async () => {
        renderWithProviders(<SprkChat {...defaultProps} />);
      });

      expect(screen.getByLabelText('Chat messages')).toBeInTheDocument();
    });
  });

  describe('Error Handling', () => {
    it('should display error banner when session error occurs', async () => {
      // Task 071: see "should show error banner" test — useChatPlaybooks &c
      // consume queued mocks before createSession runs. Make every mounted
      // fetch reject so session creation definitely hits the error path.
      mockFetch.mockReset();
      mockFetch.mockRejectedValue(new Error('Network failure'));

      await act(async () => {
        renderWithProviders(<SprkChat {...defaultProps} />);
      });

      // React 19 effect-flush requirement.
      await act(async () => {
        await new Promise(r => setTimeout(r, 0));
      });

      await waitFor(
        () => {
          const errorBanner = screen.getByTestId('chat-error-banner');
          expect(errorBanner).toBeInTheDocument();
          expect(errorBanner.textContent).toContain('Network failure');
        },
        { timeout: 3000 },
      );
    });
  });
});
