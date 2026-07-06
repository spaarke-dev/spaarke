/**
 * ConversationPane — Click-path chip wiring tests.
 *
 * ai-architecture-redesign-r1 task 023 / FR-P1-04 / ADR-039. Replaces the
 * deleted `ConversationPane.slash-nl-rewire.test.tsx` (whose premise —
 * client-side intent matching routing equivalence — is retired under
 * ADR-039's no-second-intent-mechanism rule; its subject modules
 * `executeSummarizeIntent` / `intentMatcher` were deleted, NFR-08).
 *
 * Covers the new Click entry path end-to-end at the component boundary:
 *   1. `consumer_chips` context_event (task-022 SSE contract, forwarded by
 *      SprkChat's onContextEvent) → chips render in the conversation surface,
 *      carrying binding_id.
 *   2. Chip click → the ONE shared dispatchConsumer(bindingId, args) helper
 *      with the chip's prefill_slots as args — no intent detection anywhere.
 *   3. Chip set is consumed on click (single dispatch decision per turn).
 *   4. A new chips event replaces the prior set.
 *   5. Malformed chip payloads render nothing (tolerant parse).
 *
 * Test-surface strategy mirrors the R5 task 020 precedent: mock ONLY the
 * heavy children (SprkChat prop-capture stub, useAiSession, useShellStage)
 * and the dispatcher factory; render the real ConversationPane.
 */

import '@testing-library/jest-dom';
import React, { act } from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';
import type { ISprkChatProps } from '@spaarke/ui-components';

// ---------------------------------------------------------------------------
// Mock SprkChat (prop capture) + createConsumerDispatcher (dispatch spy).
// ---------------------------------------------------------------------------

const sprkChatPropsRef: { current: ISprkChatProps | null } = { current: null };

const dispatchConsumerSpy = jest.fn(
  () => Promise.resolve({ streamId: 'stream-test', status: 'complete' as const })
);
const createConsumerDispatcherSpy = jest.fn((..._args: unknown[]) => dispatchConsumerSpy);

jest.mock('@spaarke/ui-components', () => {
  const actual = jest.requireActual('@spaarke/ui-components');
  return {
    ...actual,
    SprkChat: (props: ISprkChatProps) => {
      sprkChatPropsRef.current = props;
      // Render the host slot like the real component does (G-P2 round-1
      // finding 1: the consumer chip strip lives in transcriptFooterSlot,
      // inline at the end of the transcript).
      return <div data-testid="sprkchat-stub">{props.transcriptFooterSlot}</div>;
    },
    createConsumerDispatcher: (...args: unknown[]) =>
      (createConsumerDispatcherSpy as (...a: unknown[]) => unknown)(...args),
  };
});

// ---------------------------------------------------------------------------
// Mock @spaarke/ai-widgets useAiSession — minimal session stub with a non-null
// chatSessionId so the dispatch gate opens.
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
// Mock ThreePaneShell — ConversationPane calls `useShellStage()` which throws
// when not inside <ThreePaneShell>.
// ---------------------------------------------------------------------------

// FULL stub (no requireActual): loading the real module pulls
// ContextPaneController → runtimeConfig → @spaarke/auth
// createRuntimeConfigStore, which the shared auth test-mock does not provide
// (the pre-existing module-load failure that killed the deleted
// slash-nl-rewire suite). ConversationPane consumes exactly these three
// hooks; both context hooks tolerate null (render-in-isolation contract).
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

/** Deliver a `consumer_chips` context_event through SprkChat's callback seam. */
function deliverChips(chips: unknown): void {
  act(() => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (sprkChatPropsRef.current?.onContextEvent as any)?.({
      contextEventType: 'consumer_chips',
      contextChips: chips,
    });
  });
}

beforeEach(() => {
  sprkChatPropsRef.current = null;
  dispatchConsumerSpy.mockClear();
  createConsumerDispatcherSpy.mockClear();
});

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('Click path — chips carry binding_id; dispatchConsumer is the only dispatch entry (FR-P1-04)', () => {
  it('renders chips from a consumer_chips context_event, carrying binding_id', () => {
    renderPane();
    expect(screen.queryByTestId('consumer-chips')).not.toBeInTheDocument();

    deliverChips([
      { target_binding_id: 'b-summarize-all', chip_label: 'Summarize all?' },
      { target_binding_id: 'b-create-matter', chip_label: 'Create matter' },
    ]);

    const strip = screen.getByTestId('consumer-chips');
    expect(strip).toBeInTheDocument();
    const chip = screen.getByTestId('consumer-chip-b-summarize-all');
    expect(chip).toHaveTextContent('Summarize all?');
    expect(chip).toHaveAttribute('data-binding-id', 'b-summarize-all');
    expect(screen.getByTestId('consumer-chip-b-create-matter')).toBeInTheDocument();
  });

  it('chip click calls dispatchConsumer(bindingId, args) with the chip prefill_slots', async () => {
    renderPane();
    deliverChips([
      {
        target_binding_id: 'b-summarize-all',
        chip_label: 'Summarize all?',
        prefill_slots: { scope: 'all-files' },
      },
    ]);

    await act(async () => {
      fireEvent.click(screen.getByTestId('consumer-chip-b-summarize-all'));
    });

    expect(dispatchConsumerSpy).toHaveBeenCalledTimes(1);
    const [bindingId, args] = dispatchConsumerSpy.mock.calls[0] as unknown as [
      string,
      { slots?: Record<string, unknown>; attachmentCount?: number }
    ];
    expect(bindingId).toBe('b-summarize-all');
    expect(args.slots).toEqual({ scope: 'all-files' });
    expect(typeof args.attachmentCount).toBe('number');
  });

  it('consumes the chip set on click (single dispatch decision per turn)', async () => {
    renderPane();
    deliverChips([{ target_binding_id: 'b-1', chip_label: 'Do it' }]);

    await act(async () => {
      fireEvent.click(screen.getByTestId('consumer-chip-b-1'));
    });

    expect(screen.queryByTestId('consumer-chips')).not.toBeInTheDocument();
    expect(dispatchConsumerSpy).toHaveBeenCalledTimes(1);
  });

  it('a new consumer_chips event replaces the prior chip set', () => {
    renderPane();
    deliverChips([{ target_binding_id: 'b-old', chip_label: 'Old step' }]);
    expect(screen.getByTestId('consumer-chip-b-old')).toBeInTheDocument();

    deliverChips([{ target_binding_id: 'b-new', chip_label: 'New step' }]);
    expect(screen.queryByTestId('consumer-chip-b-old')).not.toBeInTheDocument();
    expect(screen.getByTestId('consumer-chip-b-new')).toBeInTheDocument();
  });

  it('malformed chip payloads render nothing (tolerant parse — never throws)', () => {
    renderPane();
    deliverChips('not-an-array');
    expect(screen.queryByTestId('consumer-chips')).not.toBeInTheDocument();

    deliverChips([{ chip_label: 'missing binding id' }]);
    expect(screen.queryByTestId('consumer-chips')).not.toBeInTheDocument();
  });

  it('binds the dispatcher through createConsumerDispatcher (the ONE helper — no local SSE handling)', () => {
    renderPane();
    expect(createConsumerDispatcherSpy).toHaveBeenCalled();
    const deps = createConsumerDispatcherSpy.mock.calls[0][0] as unknown as {
      bffBaseUrl: string;
      getSessionId: () => string | null;
    };
    expect(deps.bffBaseUrl).toBe('https://test-bff.example.com');
    expect(deps.getSessionId()).toBe(TEST_SESSION_ID);
  });
});
