/**
 * ConversationPane — Compose AI action result PROSE formatting (task 112,
 * UAT-R3 defect #3b/#3c).
 *
 * Non-waivable project E2E DoD (spec + task 112 acceptance criteria): a
 * REAL-PaneEventBus test that drives a Compose action's terminal result
 * through `dispatchComposeAction` (the ACTUAL injection seam
 * ComposeAiToolbar/ComposeWorkspace reach via the shared `composeActionBridge`
 * — NOT a formatter-only unit test) and asserts the injected Assistant
 * message is PROSE, not a ```json``` fence.
 *
 * Test-surface strategy mirrors ConversationPane.consumer-chips.test.tsx /
 * ConversationPane.event-path.test.tsx: mock ONLY the heavy children
 * (SprkChat prop-capture stub extended with the injection-lifecycle
 * simulation, useAiSession, ThreePaneShell) and the `createConsumerDispatcher`
 * factory (its own SSE→bus bridging is covered by dispatchConsumer.test.ts,
 * including the task-112 `suppressWorkspaceSectionBridge` suite); render the
 * REAL `ConversationPane` under a REAL `PaneEventBus` AND the REAL
 * `ComposeActionBridgeProvider` — the exact cross-pane conduit
 * `ComposeAiToolbar`'s `enqueueComposeAction` uses in the running host.
 *
 * Covers:
 *   1. compose-explain-clause `{explanation, keyConcepts}` → prose (not JSON).
 *   2. compose-defined-terms `{terms, inconsistencies}` → prose (not JSON).
 *   3. A genuinely unknown shape → the ```json``` fence fallback still fires
 *      (the branch is preserved, not deleted).
 *   4. The bridge rejects when no host dispatcher is registered — sanity
 *      check that the harness is exercising the real bridge, not a stub.
 */

import '@testing-library/jest-dom';
import React, { act } from 'react';
import { render } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';
import type { ISprkChatProps, IChatMessage } from '@spaarke/ui-components';
import {
  ComposeActionBridgeProvider,
  useComposeActionBridge,
  type ComposeActionBridgeValue,
} from '@spaarke/compose-components/context/composeActionBridge';

// ---------------------------------------------------------------------------
// Mock SprkChat (prop capture + injection-lifecycle simulation),
// createConsumerDispatcher (dispatch spy) — mirrors ConversationPane.event-path.test.tsx.
// ---------------------------------------------------------------------------

const sprkChatPropsRef: { current: ISprkChatProps | null } = { current: null };
/** Messages the (stubbed) SprkChat appended via injectLocalMessage, in order. */
const injectedMessages: IChatMessage[] = [];

const dispatchConsumerSpy = jest.fn(
  () => Promise.resolve({ streamId: 'stream-test', status: 'complete' as const })
);
const createConsumerDispatcherSpy = jest.fn((..._args: unknown[]) => dispatchConsumerSpy);

jest.mock('@spaarke/ui-components', () => {
  const actual = jest.requireActual('@spaarke/ui-components');
  const ReactActual = jest.requireActual('react') as typeof import('react');
  return {
    ...actual,
    SprkChat: (props: ISprkChatProps) => {
      sprkChatPropsRef.current = props;
      const lastInjectedRef = ReactActual.useRef<IChatMessage | null>(null);
      ReactActual.useEffect(() => {
        const message = props.injectLocalMessage ?? null;
        if (!message) {
          lastInjectedRef.current = null;
          return;
        }
        if (lastInjectedRef.current === message) return;
        lastInjectedRef.current = message;
        injectedMessages.push(message);
        props.onLocalMessageInjected?.();
      });
      return <div data-testid="sprkchat-stub">{props.transcriptFooterSlot}</div>;
    },
    createConsumerDispatcher: (...args: unknown[]) =>
      (createConsumerDispatcherSpy as (...a: unknown[]) => unknown)(...args),
  };
});

// ---------------------------------------------------------------------------
// Mock @spaarke/ai-widgets useAiSession — non-null session id so the dispatch
// gate opens; authenticatedFetch is a bare jest.fn() — the Flow-5 apply-leg
// fire-and-forget call (emitComposeApplyLeg) tolerates its rejection silently.
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
      getAccessToken: jest.fn(async () => 'token'),
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
// Harness
// ---------------------------------------------------------------------------

/** Captures the real ComposeActionBridge value so tests can call `enqueue`
 *  exactly like `ComposeAiToolbar`'s `enqueueComposeAction` prop would. */
const bridgeRef: { current: ComposeActionBridgeValue | null } = { current: null };
function BridgeCapture(): null {
  bridgeRef.current = useComposeActionBridge();
  return null;
}

function renderPane(): { bus: PaneEventBus } {
  const bus = new PaneEventBus();
  render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={bus}>
        <ComposeActionBridgeProvider>
          <ConversationPane />
          <BridgeCapture />
        </ComposeActionBridgeProvider>
      </PaneEventBusProvider>
    </FluentProvider>
  );
  return { bus };
}

beforeEach(() => {
  sprkChatPropsRef.current = null;
  injectedMessages.length = 0;
  dispatchConsumerSpy.mockClear();
  createConsumerDispatcherSpy.mockClear();
  bridgeRef.current = null;
});

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('Compose AI action result formatting — real PaneEventBus + real composeActionBridge (task 112)', () => {
  it('E2E DoD: an explain-clause terminal result injects PROSE (explanation + key concepts), not a ```json``` fence', async () => {
    renderPane();
    expect(bridgeRef.current?.hasDispatcher).toBe(true);

    dispatchConsumerSpy.mockResolvedValueOnce({
      streamId: 'stream-explain',
      status: 'complete' as const,
      result: {
        explanation: 'This clause caps the indemnifying party liability at the contract value.',
        keyConcepts: ['indemnification', 'liability cap'],
        relatedPlaybookIds: [],
      },
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
    } as any);

    await act(async () => {
      await bridgeRef.current!.enqueue({ id: 'req-1', bindingId: 'binding-explain-clause-guid' });
      // Flush the .then() chain (format + inject + fire-and-forget apply leg).
      for (let i = 0; i < 6; i++) await Promise.resolve();
    });

    expect(dispatchConsumerSpy).toHaveBeenCalledWith('binding-explain-clause-guid', undefined);

    const contents = injectedMessages.map((m) => m.content);
    const prose = contents.find((c) => c.includes('This clause caps the indemnifying party liability'));
    expect(prose).toBeDefined();
    expect(prose).toContain('**Explanation:**');
    expect(prose).toContain('**Key concepts:**');
    expect(prose).toContain('indemnification');
    expect(prose).toContain('liability cap');
    // NOT the JSON-fence fallback.
    expect(prose).not.toContain('```json');
    expect(injectedMessages.every((m) => m.role === 'Assistant')).toBe(true);
  });

  it('a defined-terms terminal result injects PROSE (terms + inconsistencies), not a ```json``` fence', async () => {
    renderPane();

    dispatchConsumerSpy.mockResolvedValueOnce({
      streamId: 'stream-defined-terms',
      status: 'complete' as const,
      result: {
        terms: [
          {
            term: 'Confidential Information',
            definitionSpan: '"Confidential Information" means any non-public business information...',
            usages: [{ span: 'the Confidential Information', consistent: true, note: '' }],
          },
        ],
        inconsistencies: [{ term: 'Affiliate', detail: 'Used before it is defined in Section 4.' }],
      },
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
    } as any);

    await act(async () => {
      await bridgeRef.current!.enqueue({ id: 'req-2', bindingId: 'binding-defined-terms-guid' });
      for (let i = 0; i < 6; i++) await Promise.resolve();
    });

    const contents = injectedMessages.map((m) => m.content);
    const prose = contents.find((c) => c.includes('Confidential Information'));
    expect(prose).toBeDefined();
    expect(prose).toContain('**Defined terms:**');
    expect(prose).toContain('**Inconsistencies:**');
    expect(prose).toContain('Affiliate');
    expect(prose).not.toContain('```json');
  });

  it('(negative) a genuinely unknown result shape still falls back to the ```json``` fence', async () => {
    renderPane();

    dispatchConsumerSpy.mockResolvedValueOnce({
      streamId: 'stream-unknown',
      status: 'complete' as const,
      result: { someFutureField: 'unrecognized shape', anotherField: 42 },
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
    } as any);

    await act(async () => {
      await bridgeRef.current!.enqueue({ id: 'req-3', bindingId: 'binding-unknown-guid' });
      for (let i = 0; i < 6; i++) await Promise.resolve();
    });

    const contents = injectedMessages.map((m) => m.content);
    const fenced = contents.find((c) => c.includes('```json'));
    expect(fenced).toBeDefined();
    expect(fenced).toContain('someFutureField');
    expect(fenced).toContain('unrecognized shape');
  });

  it('sanity: the real bridge rejects enqueue when no host dispatcher is registered (proves this is the real conduit, not a stub)', async () => {
    // Render the bridge WITHOUT ConversationPane mounted — no dispatcher ever registers.
    render(
      <FluentProvider theme={webLightTheme}>
        <PaneEventBusProvider bus={new PaneEventBus()}>
          <ComposeActionBridgeProvider>
            <BridgeCapture />
          </ComposeActionBridgeProvider>
        </PaneEventBusProvider>
      </FluentProvider>
    );

    expect(bridgeRef.current?.hasDispatcher).toBe(false);
    await expect(bridgeRef.current!.enqueue({ id: 'req-4', bindingId: 'binding-x' })).rejects.toThrow(
      /no host dispatcher registered/
    );
  });
});
