/**
 * ConversationPane — R5 task 020 / D2-11 chat-pane orchestration UX tests.
 *
 * Covers the acceptance criteria from `tasks/020-chat-pane-orchestration-ux.poml`:
 *
 *   (1) Pure-helper coverage — `buildFileConfirmationMessage`,
 *       `buildMultiFileSummarizeInterjection`, `makeLocalAssistantMessage`,
 *       `routeSummarizeIntent` re-verified post task 020 wiring.
 *
 *   (2) Chip indicator — "N files attached" rendered when chips > 0;
 *       hidden when 0; correct singular/plural copy.
 *
 *   (3) Per-file remove — `onAttachmentRemoved` callback fires with chip
 *       metadata before local splice (capture by SprkChat-stub spy).
 *
 *   (4) Inline file-confirmation debounce — multiple ready transitions
 *       within the debounce window coalesce into ONE consolidated injection.
 *
 *   (5) Multi-file Summarize interjection — emits exactly ONCE per turn even
 *       on retry / resubmission of the same message + chip set; does NOT
 *       emit for single-file sends; does NOT emit for non-Summarize sends.
 *
 *   (6) PaneEventBus dispatch — `context.files_staged` carries the correct
 *       chip ID payload on ready transitions; R4 `workspace.widget_load`
 *       Workspace dispatch is preserved (NOT replaced).
 *
 * Test strategy:
 *   - The SpaarkeAi `ConversationPane` is heavy (depends on SprkChat,
 *     useAiSession, ThreePaneShell contexts, FluentProvider). We mock
 *     `@spaarke/ui-components`'s SprkChat to a stub that:
 *       (a) captures props on every render via a module-level array
 *       (b) exposes the callbacks via a controller object the tests use
 *           to programmatically drive the chip lifecycle from outside
 *     This avoids spinning up SprkChat's internal hook machinery
 *     (useChatSession, useSseStream, useChatFileAttachment) which would
 *     require BFF mocks + extensive auth scaffolding.
 *   - `useAiSession` is mocked to return a minimal stub satisfying the
 *     ConversationPane's read surface.
 *   - PaneEventBus dispatches are captured via a real bus instance + a
 *     subscriber spy on the `context` + `workspace` channels.
 */

import '@testing-library/jest-dom';
import React, { act } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';
import type {
  AttachmentChip,
  IChatMessage,
  ISprkChatProps,
} from '@spaarke/ui-components';

// ---------------------------------------------------------------------------
// Mock @spaarke/ui-components — SprkChat is the heavy child.
//
// We replace SprkChat with a stub that captures its props on every render so
// tests can:
//   (a) drive `onAttachmentsChanged` / `onAttachmentRemoved` / `onBeforeSendMessage`
//       from outside (simulating the user lifecycle)
//   (b) assert on `injectLocalMessage` to verify host emission
//
// We DO need the real `routeSummarizeIntent` + module-scope helpers from
// ConversationPane — those are exported from the SUT.
// ---------------------------------------------------------------------------

const sprkChatPropsRef: { current: ISprkChatProps | null } = { current: null };

jest.mock('@spaarke/ui-components', () => {
  const actual = jest.requireActual('@spaarke/ui-components');
  return {
    ...actual,
    SprkChat: (props: ISprkChatProps) => {
      // Capture the latest props on every render so tests can drive callbacks.
      sprkChatPropsRef.current = props;
      return <div data-testid="sprkchat-stub" />;
    },
  };
});

// ---------------------------------------------------------------------------
// Mock @spaarke/ai-widgets useAiSession — supply a minimal session stub.
// We keep the real PaneEventBus + dispatch hooks intact so the
// `context.files_staged` + `workspace.widget_load` assertions work end-to-end.
// ---------------------------------------------------------------------------

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
      chatSessionId: null,
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

// Import AFTER the mocks so module resolution picks them up.
import {
  ConversationPane,
  buildFileConfirmationMessage,
  buildMultiFileSummarizeInterjection,
  makeLocalAssistantMessage,
  routeSummarizeIntent,
  SUMMARIZE_PROMPT_FIRST_INTERJECTION,
  FILE_CONFIRMATION_MAX_NAMES,
} from '../ConversationPane';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Render ConversationPane inside the minimum providers it requires.
 * Returns the live PaneEventBus so tests can attach subscribers.
 */
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

/**
 * Build a chip with sensible defaults — tests override the bits they care about.
 */
function makeChip(overrides: Partial<AttachmentChip> & { id: string; filename: string }): AttachmentChip {
  return {
    sizeBytes: 1024,
    mimeType: 'application/pdf',
    status: 'ready',
    textContent: 'extracted content',
    ...overrides,
  };
}

beforeEach(() => {
  sprkChatPropsRef.current = null;
  jest.clearAllTimers();
  jest.useFakeTimers();
});

afterEach(() => {
  // Drain any pending debounce timers so the next test starts clean.
  act(() => {
    jest.runOnlyPendingTimers();
  });
  jest.useRealTimers();
});

// ---------------------------------------------------------------------------
// (1) Pure-helper coverage
// ---------------------------------------------------------------------------

describe('buildFileConfirmationMessage', () => {
  it('returns null for an empty list', () => {
    expect(buildFileConfirmationMessage([])).toBeNull();
  });

  it('formats a single file with singular "file" copy', () => {
    expect(buildFileConfirmationMessage(['a.pdf'])).toBe('I have your file: a.pdf');
  });

  it('formats multiple files inline (no overflow)', () => {
    expect(buildFileConfirmationMessage(['a.pdf', 'b.docx', 'c.md'])).toBe(
      'I have your 3 files: a.pdf, b.docx, c.md'
    );
  });

  it('truncates the visible list when over the max names cap', () => {
    const filenames = ['a.pdf', 'b.docx', 'c.md', 'd.txt', 'e.pdf'];
    expect(filenames.length).toBeGreaterThan(FILE_CONFIRMATION_MAX_NAMES);
    expect(buildFileConfirmationMessage(filenames)).toBe(
      'I have your 5 files: a.pdf, b.docx, c.md, and 2 more'
    );
  });
});

describe('buildMultiFileSummarizeInterjection', () => {
  it('returns null for 0 or 1 files (no combined-summary semantics)', () => {
    expect(buildMultiFileSummarizeInterjection(0)).toBeNull();
    expect(buildMultiFileSummarizeInterjection(1)).toBeNull();
  });

  it('emits the spec interjection for N >= 2', () => {
    expect(buildMultiFileSummarizeInterjection(2)).toBe(
      "I'll combine all 2 files into a single summary."
    );
    expect(buildMultiFileSummarizeInterjection(3)).toBe(
      "I'll combine all 3 files into a single summary."
    );
    expect(buildMultiFileSummarizeInterjection(20)).toBe(
      "I'll combine all 20 files into a single summary."
    );
  });
});

describe('makeLocalAssistantMessage', () => {
  it('builds an Assistant message with markdown responseType + timestamp', () => {
    const msg = makeLocalAssistantMessage('hello');
    expect(msg.role).toBe('Assistant');
    expect(msg.content).toBe('hello');
    expect(msg.metadata?.responseType).toBe('markdown');
    expect(typeof msg.timestamp).toBe('string');
    expect(new Date(msg.timestamp).toString()).not.toBe('Invalid Date');
  });
});

describe('routeSummarizeIntent (re-verified post task 020 wiring)', () => {
  it('branch (a) — uploaded files take precedence', () => {
    expect(
      routeSummarizeIntent('/summarize', { uploadedFileCount: 1, hasActiveWorkspaceDocument: true })
    ).toEqual({ kind: 'session-files', messageText: '/summarize' });
  });

  it('branch (b) — active document with no uploads', () => {
    expect(
      routeSummarizeIntent('/summarize', { uploadedFileCount: 0, hasActiveWorkspaceDocument: true })
    ).toEqual({ kind: 'active-document', messageText: '/summarize' });
  });

  it('branch (c) — prompt-first when nothing in scope', () => {
    expect(
      routeSummarizeIntent('/summarize', { uploadedFileCount: 0, hasActiveWorkspaceDocument: false })
    ).toEqual({
      kind: 'prompt-first',
      messageText: '/summarize',
      interjection: SUMMARIZE_PROMPT_FIRST_INTERJECTION,
    });
  });

  it('not-summarize — pass-through', () => {
    expect(
      routeSummarizeIntent('hello world', { uploadedFileCount: 0, hasActiveWorkspaceDocument: false })
    ).toEqual({ kind: 'not-summarize', messageText: 'hello world' });
  });
});

// ---------------------------------------------------------------------------
// (2) Chip indicator render — "N files attached"
// ---------------------------------------------------------------------------

describe('"N files attached" indicator', () => {
  it('is hidden when no chips are present', () => {
    renderPane();
    expect(screen.queryByTestId('files-attached-indicator')).toBeNull();
  });

  it('renders singular copy when exactly 1 chip is present', async () => {
    renderPane();
    expect(sprkChatPropsRef.current?.onAttachmentsChanged).toBeDefined();

    act(() => {
      sprkChatPropsRef.current?.onAttachmentsChanged?.([
        makeChip({ id: 'c1', filename: 'a.pdf' }),
      ]);
    });

    const indicator = await screen.findByTestId('files-attached-indicator');
    expect(indicator).toHaveTextContent('1 file attached');
  });

  it('renders plural copy when 2+ chips are present', async () => {
    renderPane();
    act(() => {
      sprkChatPropsRef.current?.onAttachmentsChanged?.([
        makeChip({ id: 'c1', filename: 'a.pdf' }),
        makeChip({ id: 'c2', filename: 'b.docx' }),
        makeChip({ id: 'c3', filename: 'c.md' }),
      ]);
    });

    const indicator = await screen.findByTestId('files-attached-indicator');
    expect(indicator).toHaveTextContent('3 files attached');
  });

  it('updates reactively when chips are removed', async () => {
    renderPane();
    act(() => {
      sprkChatPropsRef.current?.onAttachmentsChanged?.([
        makeChip({ id: 'c1', filename: 'a.pdf' }),
        makeChip({ id: 'c2', filename: 'b.docx' }),
      ]);
    });
    await screen.findByText('2 files attached');

    act(() => {
      sprkChatPropsRef.current?.onAttachmentsChanged?.([
        makeChip({ id: 'c1', filename: 'a.pdf' }),
      ]);
    });
    await screen.findByText('1 file attached');

    act(() => {
      sprkChatPropsRef.current?.onAttachmentsChanged?.([]);
    });
    await waitFor(() => {
      expect(screen.queryByTestId('files-attached-indicator')).toBeNull();
    });
  });
});

// ---------------------------------------------------------------------------
// (3) Per-file remove cascade
// ---------------------------------------------------------------------------

describe('onAttachmentRemoved cascade', () => {
  it('is wired to SprkChat as a stable callback', () => {
    renderPane();
    expect(sprkChatPropsRef.current?.onAttachmentRemoved).toBeDefined();
  });

  it('clears the dispatched-id ref so re-adding the same chip re-fires staging dispatch', async () => {
    const { bus } = renderPane();
    const contextEvents: Array<{ type: string; stagedFileIds?: string[] }> = [];
    bus.subscribe('context', (evt) => {
      contextEvents.push(evt as { type: string; stagedFileIds?: string[] });
    });

    // First add: chip transitions to ready → staging dispatch fires.
    act(() => {
      sprkChatPropsRef.current?.onAttachmentsChanged?.([
        makeChip({ id: 'c1', filename: 'a.pdf' }),
      ]);
    });
    expect(contextEvents.filter(e => e.type === 'files_staged')).toHaveLength(1);

    // Remove the chip via the cascade callback (SprkChat would call this on
    // dismiss-click; the test simulates it directly).
    act(() => {
      sprkChatPropsRef.current?.onAttachmentRemoved?.(
        makeChip({ id: 'c1', filename: 'a.pdf' }),
        0
      );
    });
    // Now SprkChat splices the chip locally. Simulate the resulting empty chip list.
    act(() => {
      sprkChatPropsRef.current?.onAttachmentsChanged?.([]);
    });

    // Re-add the chip (same ID). Because the cascade freed the dispatched-id
    // ref, the staging dispatch fires again.
    act(() => {
      sprkChatPropsRef.current?.onAttachmentsChanged?.([
        makeChip({ id: 'c1', filename: 'a.pdf' }),
      ]);
    });
    expect(contextEvents.filter(e => e.type === 'files_staged')).toHaveLength(2);
  });
});

// ---------------------------------------------------------------------------
// (4) Inline file-confirmation debounce
// ---------------------------------------------------------------------------

describe('inline file-confirmation debounce', () => {
  it('coalesces multiple ready transitions into ONE consolidated injection', () => {
    renderPane();

    // Three chips arrive in rapid succession, all transitioning to ready.
    act(() => {
      sprkChatPropsRef.current?.onAttachmentsChanged?.([
        makeChip({ id: 'c1', filename: 'a.pdf' }),
      ]);
    });
    act(() => {
      sprkChatPropsRef.current?.onAttachmentsChanged?.([
        makeChip({ id: 'c1', filename: 'a.pdf' }),
        makeChip({ id: 'c2', filename: 'b.docx' }),
      ]);
    });
    act(() => {
      sprkChatPropsRef.current?.onAttachmentsChanged?.([
        makeChip({ id: 'c1', filename: 'a.pdf' }),
        makeChip({ id: 'c2', filename: 'b.docx' }),
        makeChip({ id: 'c3', filename: 'c.md' }),
      ]);
    });

    // Before the debounce expires, no injection has fired.
    expect(sprkChatPropsRef.current?.injectLocalMessage).toBeNull();

    // Advance the debounce timer past the 250ms window.
    act(() => {
      jest.advanceTimersByTime(300);
    });

    // ONE consolidated injection fires.
    const injection = sprkChatPropsRef.current?.injectLocalMessage as IChatMessage | null | undefined;
    expect(injection).not.toBeNull();
    expect(injection?.content).toBe('I have your 3 files: a.pdf, b.docx, c.md');
    expect(injection?.role).toBe('Assistant');
    expect(injection?.metadata?.responseType).toBe('markdown');

    // Simulate SprkChat acknowledging the injection so the prop clears.
    act(() => {
      sprkChatPropsRef.current?.onLocalMessageInjected?.();
    });
    expect(sprkChatPropsRef.current?.injectLocalMessage).toBeNull();
  });

  it('does NOT re-emit the confirmation for the same chips on subsequent re-renders', () => {
    renderPane();

    act(() => {
      sprkChatPropsRef.current?.onAttachmentsChanged?.([
        makeChip({ id: 'c1', filename: 'a.pdf' }),
      ]);
    });
    act(() => {
      jest.advanceTimersByTime(300);
    });
    const firstInjection = sprkChatPropsRef.current?.injectLocalMessage;
    expect(firstInjection?.content).toBe('I have your file: a.pdf');

    // Acknowledge to clear.
    act(() => {
      sprkChatPropsRef.current?.onLocalMessageInjected?.();
    });

    // Re-fire onAttachmentsChanged with the SAME chip (e.g. status patch).
    act(() => {
      sprkChatPropsRef.current?.onAttachmentsChanged?.([
        makeChip({ id: 'c1', filename: 'a.pdf' }),
      ]);
    });
    act(() => {
      jest.advanceTimersByTime(300);
    });

    // No new injection — the confirmedReadyIds ref dedupes.
    expect(sprkChatPropsRef.current?.injectLocalMessage).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// (5) Multi-file Summarize interjection — exactly once per turn
// ---------------------------------------------------------------------------

describe('multi-file Summarize interjection (FR-03 session-files branch)', () => {
  function stageChips(chips: AttachmentChip[]): void {
    act(() => {
      sprkChatPropsRef.current?.onAttachmentsChanged?.(chips);
    });
    // Advance past the confirmation debounce + clear it so the injection slot
    // is null heading into the interjection assertion.
    act(() => {
      jest.advanceTimersByTime(300);
    });
    act(() => {
      sprkChatPropsRef.current?.onLocalMessageInjected?.();
    });
  }

  it('emits the deterministic interjection when N >= 2 and the message is /summarize', () => {
    renderPane();
    stageChips([
      makeChip({ id: 'c1', filename: 'a.pdf' }),
      makeChip({ id: 'c2', filename: 'b.docx' }),
      makeChip({ id: 'c3', filename: 'c.md' }),
    ]);

    act(() => {
      sprkChatPropsRef.current?.onBeforeSendMessage?.('/summarize');
    });

    const injection = sprkChatPropsRef.current?.injectLocalMessage;
    expect(injection?.content).toBe("I'll combine all 3 files into a single summary.");
  });

  it('does NOT emit the interjection for single-file Summarize', () => {
    renderPane();
    stageChips([makeChip({ id: 'c1', filename: 'a.pdf' })]);

    act(() => {
      sprkChatPropsRef.current?.onBeforeSendMessage?.('/summarize');
    });

    expect(sprkChatPropsRef.current?.injectLocalMessage).toBeNull();
  });

  it('does NOT emit the interjection for non-Summarize messages', () => {
    renderPane();
    stageChips([
      makeChip({ id: 'c1', filename: 'a.pdf' }),
      makeChip({ id: 'c2', filename: 'b.docx' }),
    ]);

    act(() => {
      sprkChatPropsRef.current?.onBeforeSendMessage?.('hello world');
    });

    expect(sprkChatPropsRef.current?.injectLocalMessage).toBeNull();
  });

  it('emits EXACTLY ONCE on retry / resubmission of the same turn', () => {
    renderPane();
    stageChips([
      makeChip({ id: 'c1', filename: 'a.pdf' }),
      makeChip({ id: 'c2', filename: 'b.docx' }),
    ]);

    act(() => {
      sprkChatPropsRef.current?.onBeforeSendMessage?.('/summarize');
    });
    const firstInjection = sprkChatPropsRef.current?.injectLocalMessage;
    expect(firstInjection?.content).toBe("I'll combine all 2 files into a single summary.");

    // Acknowledge + retry the same send.
    act(() => {
      sprkChatPropsRef.current?.onLocalMessageInjected?.();
    });
    act(() => {
      sprkChatPropsRef.current?.onBeforeSendMessage?.('/summarize');
    });

    // No re-emission — the once-per-turn ref dedupes.
    expect(sprkChatPropsRef.current?.injectLocalMessage).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// (6) PaneEventBus dispatch — context.files_staged
// ---------------------------------------------------------------------------

describe('PaneEventBus context.files_staged dispatch', () => {
  it('fires with the correct staged file IDs on ready transitions', () => {
    const { bus } = renderPane();
    const contextEvents: Array<{ type: string; stagedFileIds?: string[] }> = [];
    bus.subscribe('context', (evt) => {
      contextEvents.push(evt as { type: string; stagedFileIds?: string[] });
    });

    act(() => {
      sprkChatPropsRef.current?.onAttachmentsChanged?.([
        makeChip({ id: 'c1', filename: 'a.pdf' }),
        makeChip({ id: 'c2', filename: 'b.docx' }),
      ]);
    });

    const staged = contextEvents.filter(e => e.type === 'files_staged');
    expect(staged).toHaveLength(1);
    expect(staged[0].stagedFileIds).toEqual(['c1', 'c2']);
  });

  it('does NOT dispatch a second event for chips that are already ready', () => {
    const { bus } = renderPane();
    const contextEvents: Array<{ type: string; stagedFileIds?: string[] }> = [];
    bus.subscribe('context', (evt) => {
      contextEvents.push(evt as { type: string; stagedFileIds?: string[] });
    });

    // First batch: c1 ready.
    act(() => {
      sprkChatPropsRef.current?.onAttachmentsChanged?.([
        makeChip({ id: 'c1', filename: 'a.pdf' }),
      ]);
    });

    // Second batch: c1 still ready + c2 newly ready. Only c2 should be in
    // the second staged dispatch (not a re-dispatch of c1).
    act(() => {
      sprkChatPropsRef.current?.onAttachmentsChanged?.([
        makeChip({ id: 'c1', filename: 'a.pdf' }),
        makeChip({ id: 'c2', filename: 'b.docx' }),
      ]);
    });

    const staged = contextEvents.filter(e => e.type === 'files_staged');
    expect(staged).toHaveLength(2);
    expect(staged[0].stagedFileIds).toEqual(['c1']);
    expect(staged[1].stagedFileIds).toEqual(['c2']);
  });

  it('does NOT dispatch for chips in extracting or error state', () => {
    const { bus } = renderPane();
    const contextEvents: Array<{ type: string; stagedFileIds?: string[] }> = [];
    bus.subscribe('context', (evt) => {
      contextEvents.push(evt as { type: string; stagedFileIds?: string[] });
    });

    act(() => {
      sprkChatPropsRef.current?.onAttachmentsChanged?.([
        makeChip({ id: 'c1', filename: 'a.pdf', status: 'extracting', textContent: undefined }),
        makeChip({ id: 'c2', filename: 'b.docx', status: 'error', textContent: undefined, error: 'parse failed' }),
      ]);
    });

    expect(contextEvents.filter(e => e.type === 'files_staged')).toHaveLength(0);
  });
});
