/**
 * ConversationPane — new-session vs add-to-current prompt on file attach
 * (ai-advanced-capabilities-analysis-hub-r1 task 024, spec FR-08).
 *
 * When a user attaches a file to a LIVE chat (prior messages already exist), a two-choice prompt
 * ("Start a new session" / "Add to current session") must appear — no silent default. "New
 * session" pairs the hook's file-carryover bookkeeping (useAttachments.prepareFilesForNewSession)
 * with the EXISTING "New session" seam (handleNewSession — clear + remount, R4-5); "add to current"
 * resumes the normal auto-promote path into the SAME session.
 *
 * Test-surface strategy mirrors ConversationPane.event-path.test.tsx / .new-session.test.tsx: mock
 * the heavy children (SprkChat prop-capture stub, useAiSession — STATEFUL here so a real session
 * switch is observable, ThreePaneShell) plus the Event-path SSE stream (DocumentUploadedEventStream);
 * render the real ConversationPane.
 */

import '@testing-library/jest-dom';
import { TextEncoder as NodeTextEncoder, TextDecoder as NodeTextDecoder } from 'util';
// eslint-disable-next-line @typescript-eslint/no-explicit-any
if (typeof (global as any).TextEncoder === 'undefined') (global as any).TextEncoder = NodeTextEncoder;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
if (typeof (global as any).TextDecoder === 'undefined') (global as any).TextDecoder = NodeTextDecoder;

import React, { act } from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';
import type { ISprkChatProps } from '@spaarke/ui-components';
import type { RunDocumentUploadedEventOptions } from '../DocumentUploadedEventStream';

const CHAT_SESSION = '00000000-0000-0000-0000-00000000f001';
const NEW_SESSION = '00000000-0000-0000-0000-00000000f002';
const BFF = 'https://test-bff.example.com';

// ---------------------------------------------------------------------------
// Mock the Event-path SSE stream — irrelevant to this prompt's contract.
// ---------------------------------------------------------------------------
jest.mock('../DocumentUploadedEventStream', () => {
  const actual = jest.requireActual('../DocumentUploadedEventStream');
  return {
    ...actual,
    runDocumentUploadedEvent: async (_opts: RunDocumentUploadedEventOptions) => undefined,
  };
});

// ---------------------------------------------------------------------------
// Mock SprkChat — prop capture + MOUNT counter (a real remount is part of the
// "new session" contract, same as ConversationPane.new-session.test.tsx).
// ---------------------------------------------------------------------------
const sprkChatPropsRef: { current: ISprkChatProps | null } = { current: null };
let sprkChatMounts = 0;

jest.mock('@spaarke/ui-components', () => {
  const actual = jest.requireActual('@spaarke/ui-components');
  const ReactActual = jest.requireActual('react') as typeof import('react');
  return {
    ...actual,
    SprkChat: (props: ISprkChatProps) => {
      sprkChatPropsRef.current = props;
      ReactActual.useEffect(() => {
        sprkChatMounts += 1;
      }, []);
      return <div data-testid="sprkchat-stub" />;
    },
  };
});

// ---------------------------------------------------------------------------
// Mock @spaarke/ai-widgets useAiSession — STATEFUL chatSessionId so
// clearChatSession()/setChatSessionId() (both real, existing ConversationPane
// seams) drive an observable session switch, exactly as they do in production.
// ---------------------------------------------------------------------------
const authenticatedFetchMock = jest.fn(async (url: string) => {
  if (url.includes('/documents')) {
    return { ok: true, status: 202, json: async () => ({ documentId: `doc-for-${url}` }) } as unknown as Response;
  }
  return { ok: false, status: 404, json: async () => ({}), text: async () => '' } as unknown as Response;
});

jest.mock('@spaarke/ai-widgets', () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const actual = jest.requireActual('@spaarke/ai-widgets') as any;
  const ReactActual = jest.requireActual('react') as typeof import('react');
  return {
    ...actual,
    useAiSession: () => {
      const [sessionId, setSessionId] = ReactActual.useState<string | null>(CHAT_SESSION);
      return {
        isAuthenticated: true,
        authenticatedFetch: authenticatedFetchMock,
        getAccessToken: jest.fn(async () => 'token'),
        bffBaseUrl: BFF,
        tenantId: 'test-tenant',
        chatSessionId: sessionId,
        setChatSessionId: setSessionId,
        clearChatSession: () => setSessionId(null),
        playbookId: undefined,
        setPlaybookId: jest.fn(),
        entityContext: null,
        contextMapping: null,
        isLoadingContextMapping: false,
        streaming: { onPaneEvent: null },
        streamingState: { isStreaming: false, tokenCount: 0 },
        turnCount: 0,
        isLoading: false,
      };
    },
  };
});

jest.mock('../../shell/ThreePaneShell', () => ({
  useShellStage: () => ({
    stage: 'active-chat' as const,
    toLoading: jest.fn(),
    toActiveChat: jest.fn(),
    toReview: jest.fn(),
    reset: jest.fn(),
  }),
  useRestoreContext: () => null,
  usePaneCollapseContext: () => null,
  useComposeLaunch: () => null,
}));

// Import AFTER the mocks.
import { ConversationPane } from '../ConversationPane';

function renderPane(theme: typeof webLightTheme = webLightTheme): void {
  render(
    <FluentProvider theme={theme}>
      <PaneEventBusProvider bus={new PaneEventBus()}>
        <ConversationPane />
      </PaneEventBusProvider>
    </FluentProvider>
  );
}

/** Simulate a chat that already has prior messages (a "live" chat). */
function markChatLive(): void {
  act(() => {
    sprkChatPropsRef.current?.onMessagesChange?.([
      { role: 'User', content: 'hello', timestamp: new Date().toISOString() },
      { role: 'Assistant', content: 'hi there', timestamp: new Date().toISOString() },
    ]);
  });
}

/** Attach one file and flush the ready tick. */
async function attachFile(fileName: string): Promise<void> {
  const bytes = new Uint8Array([1, 2, 3]);
  await act(async () => {
    sprkChatPropsRef.current?.onAttachmentReady?.({
      id: 'chip-1',
      filename: fileName,
      file: new File([bytes], fileName, { type: 'text/plain' }),
      contentType: 'text/plain',
      textContent: '',
    } as unknown as Parameters<NonNullable<ISprkChatProps['onAttachmentReady']>>[0]);
    sprkChatPropsRef.current?.onAttachmentsChanged?.([
      { id: 'chip-1', filename: fileName, sizeBytes: 3, mimeType: 'text/plain', status: 'ready' },
    ] as unknown as Parameters<NonNullable<ISprkChatProps['onAttachmentsChanged']>>[0]);
    for (let i = 0; i < 12; i++) await Promise.resolve();
  });
}

function documentsPostUrls(): string[] {
  return authenticatedFetchMock.mock.calls
    .map((c) => c[0] as string)
    .filter((url) => url.includes('/documents'));
}

beforeEach(() => {
  sprkChatPropsRef.current = null;
  sprkChatMounts = 0;
  authenticatedFetchMock.mockClear();
});

describe('ConversationPane — file-attach new-session/add-to-current prompt (task 024)', () => {
  it('shows the two-choice prompt when a file is attached to a chat with prior messages', async () => {
    renderPane();
    markChatLive();

    await attachFile('brief.docx');

    expect(screen.getByText('Attaching a file to this conversation')).toBeTruthy();
    expect(screen.getByRole('button', { name: /Start a new session/i })).toBeTruthy();
    expect(screen.getByRole('button', { name: /Add to current session/i })).toBeTruthy();
  });

  it('does NOT show the prompt when the chat is empty (nothing to preserve)', async () => {
    renderPane();
    // No markChatLive() — chatMessageCount stays 0.

    await attachFile('brief.docx');

    expect(screen.queryByText('Attaching a file to this conversation')).toBeNull();
    // Existing behavior unaffected: the file auto-promotes into the current session immediately.
    expect(documentsPostUrls().some((u) => u.includes(CHAT_SESSION))).toBe(true);
  });

  it('negative: does not silently pick a behavior — no /documents POST fires until a choice is made', async () => {
    renderPane();
    markChatLive();

    await attachFile('brief.docx');

    expect(screen.getByText('Attaching a file to this conversation')).toBeTruthy();
    expect(documentsPostUrls()).toHaveLength(0);
  });

  it('"Add to current session" keeps the file in THIS session (no session switch)', async () => {
    renderPane();
    markChatLive();
    await attachFile('brief.docx');

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: /Add to current session/i }));
      for (let i = 0; i < 12; i++) await Promise.resolve();
    });

    expect(screen.queryByText('Attaching a file to this conversation')).toBeNull();
    const urls = documentsPostUrls();
    expect(urls.length).toBeGreaterThan(0);
    expect(urls.every((u) => u.includes(CHAT_SESSION))).toBe(true);
    // No session switch — SprkChat never remounted.
    expect(sprkChatMounts).toBe(1);
  });

  it('"Start a new session" archives the current session and attaches the file to the fresh one', async () => {
    renderPane();
    markChatLive();
    await attachFile('brief.docx');

    // Dialog is showing; no promotion has happened yet against the OLD session.
    expect(documentsPostUrls()).toHaveLength(0);

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: /Start a new session/i }));
    });

    // The prompt closes and SprkChat remounts (clearChatSession + startNewSession — the SAME
    // seam the header "New session" button uses, task 021/R4-5).
    expect(screen.queryByText('Attaching a file to this conversation')).toBeNull();
    expect(sprkChatMounts).toBe(2);

    // Simulate the remounted SprkChat minting its own fresh session (production behavior —
    // SprkChat itself calls create-session on mount with sessionId=undefined).
    await act(async () => {
      sprkChatPropsRef.current?.onSessionCreated?.({ sessionId: NEW_SESSION } as unknown as Parameters<
        NonNullable<ISprkChatProps['onSessionCreated']>
      >[0]);
      for (let i = 0; i < 12; i++) await Promise.resolve();
    });

    // The held file is carried over into the NEW session — never the old (archived) one.
    const urls = documentsPostUrls();
    expect(urls.length).toBeGreaterThan(0);
    expect(urls.every((u) => u.includes(NEW_SESSION))).toBe(true);
    expect(urls.some((u) => u.includes(CHAT_SESSION))).toBe(false);
  });

  it('dismissing the prompt applies no default — the file stays attached but unpromoted', async () => {
    renderPane();
    markChatLive();
    await attachFile('brief.docx');

    await act(async () => {
      // ChoiceDialog's Cancel action → onDismiss.
      fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
      for (let i = 0; i < 6; i++) await Promise.resolve();
    });

    expect(screen.queryByText('Attaching a file to this conversation')).toBeNull();
    // No behavior was silently chosen — the file never promoted anywhere, and no session switch.
    expect(documentsPostUrls()).toHaveLength(0);
    expect(sprkChatMounts).toBe(1);
  });

  it('renders correctly in dark mode (ADR-021)', async () => {
    renderPane(webDarkTheme);
    markChatLive();

    await attachFile('brief.docx');

    expect(screen.getByText('Attaching a file to this conversation')).toBeTruthy();
    expect(screen.getByRole('button', { name: /Start a new session/i })).toBeTruthy();
    expect(screen.getByRole('button', { name: /Add to current session/i })).toBeTruthy();
  });
});
