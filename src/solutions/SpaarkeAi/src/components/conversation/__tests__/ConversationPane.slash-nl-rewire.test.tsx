/**
 * ConversationPane — R6 Hotfix Wave B-G9c3 (B9) slash → NL rewire tests.
 *
 * User decision (Wave B-G9c3, 2026-06-10):
 *   "/summarize slash command launched from the Assistant chat" must produce
 *   the SAME output as natural-language "summarize this document" — both flow
 *   through the NL primitives (SprkChatAgent → invoke_playbook →
 *   InvokePlaybookHandler → IPlaybookOrchestrationService.ExecuteAsync) rather
 *   than the JPS-template structured streaming path (executeSummarizeIntent →
 *   POST /api/ai/chat/sessions/{id}/summarize → ExecuteChatSummarizeAsync).
 *
 * These tests assert the routing equivalence behavior, NOT byte-for-byte LLM
 * output (which is a non-deterministic Azure OpenAI concern).
 *
 * Acceptance:
 *   1. Slash `/summarize` with held files → executeSummarizeIntent NOT invoked.
 *      The message flows to SprkChat unchanged so the LLM agent handles it as
 *      natural language.
 *   2. NL pattern "summarize" with held files → executeSummarizeIntent IS
 *      invoked (preserves R5 task 036 / P2-CLOSEOUT-05 operator-UX contract).
 *   3. NL pattern "please summarize" with held files → executeSummarizeIntent
 *      IS invoked (same operator-UX contract via pattern match).
 *   4. Button-id `action:summarize` is not exercised here (no host-side
 *      invocation; the matchIntent unit tests already cover it).
 *
 * The test surface mocks `executeSummarizeIntent` to detect invocation; it
 * does NOT need the SSE stream machinery from the R5 task-036 test file.
 */

import '@testing-library/jest-dom';
import React, { act } from 'react';
import { render } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';
import type {
  AttachmentChip,
  ISprkChatProps,
} from '@spaarke/ui-components';

// ---------------------------------------------------------------------------
// Mock SprkChat — capture props each render so tests drive callbacks.
// ---------------------------------------------------------------------------

const sprkChatPropsRef: { current: ISprkChatProps | null } = { current: null };

jest.mock('@spaarke/ui-components', () => {
  const actual = jest.requireActual('@spaarke/ui-components');
  return {
    ...actual,
    SprkChat: (props: ISprkChatProps) => {
      sprkChatPropsRef.current = props;
      return <div data-testid="sprkchat-stub" />;
    },
  };
});

// ---------------------------------------------------------------------------
// Mock @spaarke/ai-widgets useAiSession — supply a minimal session stub with a
// non-null chatSessionId so the slash + pattern dispatch gates open.
// ---------------------------------------------------------------------------

const TEST_SESSION_ID = '00000000-0000-0000-0000-000000000001';

jest.mock('@spaarke/ai-widgets', () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const actual = jest.requireActual('@spaarke/ai-widgets') as any;
  return {
    ...actual,
    useAiSession: () => ({
      isAuthenticated: true,
      authenticatedFetch: jest.fn(),
      getAccessToken: jest.fn(),
      bffBaseUrl: 'https://test-bff.example.com',
      tenantId: 'test-tenant',
      chatSessionId: TEST_SESSION_ID,
      setChatSessionId: jest.fn(),
      playbookId: undefined,
      setPlaybookId: jest.fn(),
      entityContext: null,
      contextMapping: null,
      isLoadingContextMapping: false,
      streaming: { onPaneEvent: null },
      streamingState: { isStreaming: false, tokenCount: 0 },
      turnCount: 0,
      isLoading: false,
    }),
  };
});

// ---------------------------------------------------------------------------
// Mock executeSummarizeIntent — capture invocations. The real implementation
// makes HTTP calls; we only assert whether it was called and with which intent.
// ---------------------------------------------------------------------------

const executeSummarizeIntentSpy = jest.fn(
  () =>
    Promise.resolve({
      streamId: 'stream-test',
      documentIds: ['doc-1'],
      filenames: ['a.pdf'],
    }) as Promise<{ streamId: string; documentIds: string[]; filenames: string[] }>
);

jest.mock('../executeSummarizeIntent', () => ({
  __esModule: true,
  executeSummarizeIntent: (...args: unknown[]) =>
    (executeSummarizeIntentSpy as (...a: unknown[]) => unknown)(...args),
}));

// ---------------------------------------------------------------------------
// Mock ThreePaneShell — ConversationPane calls `useShellStage()` which throws
// when not inside <ThreePaneShell>. Replace with a stable no-op implementation
// so tests render the component in isolation (matches the R5 task 020 test
// strategy of mocking only the heavy children).
// ---------------------------------------------------------------------------

jest.mock('../../shell/ThreePaneShell', () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const actual = jest.requireActual('../../shell/ThreePaneShell') as any;
  return {
    ...actual,
    useShellStage: () => ({
      stage: 'active-chat' as const,
      toLoading: jest.fn(),
      toActiveChat: jest.fn(),
      toReview: jest.fn(),
      reset: jest.fn(),
    }),
  };
});

// Import AFTER the mocks.
import { ConversationPane } from '../ConversationPane';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function renderPane(): { bus: PaneEventBus } {
  const bus = new PaneEventBus();
  render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={bus}>
        <ConversationPane />
      </PaneEventBusProvider>
    </FluentProvider>
  );
  return { bus };
}

function makeChip(overrides: Partial<AttachmentChip> & { id: string; filename: string }): AttachmentChip {
  return {
    sizeBytes: 1024,
    mimeType: 'application/pdf',
    status: 'ready',
    textContent: 'extracted content',
    ...overrides,
  };
}

function stageReadyChip(chip: AttachmentChip): void {
  // Fire onAttachmentsChanged AND onAttachmentReady so the host's
  // `heldFilesRef` (populated in handleAttachmentReady — line ~1435 of
  // ConversationPane) carries the File the orchestrator promotes. Without
  // the ready callback, the held-files map is empty and
  // `executeSummarizeIntent` never fires even when the intent matches.
  act(() => {
    sprkChatPropsRef.current?.onAttachmentsChanged?.([chip]);
    sprkChatPropsRef.current?.onAttachmentReady?.({
      filename: chip.filename,
      contentType: chip.mimeType,
      textContent: chip.textContent ?? '',
      file: new File(['stub'], chip.filename, { type: chip.mimeType }),
    });
  });
}

beforeEach(() => {
  sprkChatPropsRef.current = null;
  executeSummarizeIntentSpy.mockClear();
  jest.useFakeTimers();
});

afterEach(() => {
  act(() => {
    jest.runOnlyPendingTimers();
  });
  jest.useRealTimers();
});

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('R6 Hotfix Wave B-G9c3 (B9) — /summarize slash → NL rewire', () => {
  it('slash /summarize with held files does NOT invoke executeSummarizeIntent (flows to NL chat agent)', () => {
    renderPane();
    stageReadyChip(makeChip({ id: 'c1', filename: 'a.pdf' }));

    act(() => {
      sprkChatPropsRef.current?.onBeforeSendMessage?.('/summarize');
    });

    // The slash command bypasses the deterministic orchestrator — the message
    // flows through SprkChat to the LLM agent which handles it as natural
    // language. This matches the user's B9 decision.
    expect(executeSummarizeIntentSpy).not.toHaveBeenCalled();
  });

  it('slash /summarize with trailing args still bypasses executeSummarizeIntent', () => {
    renderPane();
    stageReadyChip(makeChip({ id: 'c1', filename: 'a.pdf' }));

    act(() => {
      sprkChatPropsRef.current?.onBeforeSendMessage?.('/summarize the key terms');
    });

    expect(executeSummarizeIntentSpy).not.toHaveBeenCalled();
  });

  it('NL pattern "summarize this document" with held files DOES invoke executeSummarizeIntent (R5 task 036 preserved)', () => {
    renderPane();
    stageReadyChip(makeChip({ id: 'c1', filename: 'a.pdf' }));

    act(() => {
      sprkChatPropsRef.current?.onBeforeSendMessage?.('summarize this document');
    });

    // The NL pattern match still fires the deterministic dispatch per the R5
    // task 036 / P2-CLOSEOUT-05 operator-UX contract. Only the slash command
    // is rewired through the NL chat-agent path.
    expect(executeSummarizeIntentSpy).toHaveBeenCalledTimes(1);
  });

  it('NL pattern "please summarize" with held files DOES invoke executeSummarizeIntent', () => {
    renderPane();
    stageReadyChip(makeChip({ id: 'c1', filename: 'a.pdf' }));

    act(() => {
      sprkChatPropsRef.current?.onBeforeSendMessage?.('please summarize that');
    });

    expect(executeSummarizeIntentSpy).toHaveBeenCalledTimes(1);
  });

  it('non-summarize message ("hello world") never invokes executeSummarizeIntent', () => {
    renderPane();
    stageReadyChip(makeChip({ id: 'c1', filename: 'a.pdf' }));

    act(() => {
      sprkChatPropsRef.current?.onBeforeSendMessage?.('hello world');
    });

    expect(executeSummarizeIntentSpy).not.toHaveBeenCalled();
  });
});
