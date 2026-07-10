/**
 * ConversationPane — OutcomeCard next-step chip wiring tests.
 *
 * F-4 (e2e-completion-audit 2026-07-10): task 062 threaded `TargetBindingId`
 * C#→SSE→TS to SprkChat's `onNextStep`, but the shipped host never passed the
 * callback, so `OutcomeCard.tsx` disabled every `invoke_capability` chip
 * (`disabled={!onNextStep}`) — they rendered visible-but-dead. These tests lock
 * the activation: ConversationPane MUST pass an `onNextStep` that routes an
 * `invoke_capability` chip's `targetBindingId` through the ONE shared
 * dispatchConsumer path (ADR-039), and a real OutcomeCard rendered with that
 * callback must dispatch on click (and stay disabled with no callback).
 *
 * Test-surface strategy mirrors ConversationPane.consumer-chips.test.tsx: mock
 * ONLY the heavy children (SprkChat prop-capture stub, useAiSession,
 * useShellStage) and the dispatcher factory; render the real ConversationPane +
 * the real OutcomeCard.
 */

import '@testing-library/jest-dom';
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';
import type { ISprkChatProps, IOutcomeCard } from '@spaarke/ui-components';
// The REAL OutcomeCard (mock below spreads `...actual`, so this is genuine).
import { OutcomeCard } from '@spaarke/ui-components';

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
      return <div data-testid="sprkchat-stub">{props.transcriptFooterSlot}</div>;
    },
    createConsumerDispatcher: (...args: unknown[]) =>
      (createConsumerDispatcherSpy as (...a: unknown[]) => unknown)(...args),
  };
});

// ---------------------------------------------------------------------------
// Mock useAiSession — minimal session stub with a non-null chatSessionId.
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
// Mock ThreePaneShell (see consumer-chips suite for the module-load rationale).
// ---------------------------------------------------------------------------

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

const renderWithProviders = (ui: React.ReactElement) =>
  render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);

function renderPane(): void {
  const bus = new PaneEventBus();
  renderWithProviders(
    <PaneEventBusProvider bus={bus}>
      <ConversationPane />
    </PaneEventBusProvider>
  );
}

function outcomeCardWithChips(): IOutcomeCard {
  return {
    status: 'succeeded',
    summary: { userFacing: 'Task “Review NDA” was created.' },
    nextSteps: [
      { label: 'Analyze the document', actionKind: 'invoke_capability', targetBindingId: 'binding-guid-1' },
      { label: 'Open the library', actionKind: 'navigate', targetUrl: 'https://example.test/lib' },
    ],
  };
}

beforeEach(() => {
  sprkChatPropsRef.current = null;
  dispatchConsumerSpy.mockClear();
  createConsumerDispatcherSpy.mockClear();
});

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('OutcomeCard next-step chips — host activation (F-4)', () => {
  it('ConversationPane supplies onNextStep to SprkChat (chips are not dead)', () => {
    renderPane();
    expect(typeof sprkChatPropsRef.current?.onNextStep).toBe('function');
  });

  it('a rendered invoke_capability chip dispatches its targetBindingId on click', async () => {
    renderPane();
    const onNextStep = sprkChatPropsRef.current?.onNextStep;
    expect(onNextStep).toBeDefined();

    // Render the REAL OutcomeCard with the host-supplied callback and click a chip.
    renderWithProviders(<OutcomeCard card={outcomeCardWithChips()} onNextStep={onNextStep} />);

    const chip = screen.getByRole('button', { name: /analyze the document/i });
    expect(chip).not.toBeDisabled();
    fireEvent.click(chip);

    expect(dispatchConsumerSpy).toHaveBeenCalledTimes(1);
    const [bindingId] = dispatchConsumerSpy.mock.calls[0] as unknown as [string, unknown];
    expect(bindingId).toBe('binding-guid-1');
  });

  it('a navigate chip opens its targetUrl and does NOT dispatch a capability', () => {
    renderPane();
    const onNextStep = sprkChatPropsRef.current?.onNextStep;
    const openSpy = jest.spyOn(window, 'open').mockImplementation(() => null);

    renderWithProviders(<OutcomeCard card={outcomeCardWithChips()} onNextStep={onNextStep} />);
    fireEvent.click(screen.getByRole('button', { name: /open the library/i }));

    expect(openSpy).toHaveBeenCalledWith('https://example.test/lib', '_blank', 'noopener,noreferrer');
    expect(dispatchConsumerSpy).not.toHaveBeenCalled();
    openSpy.mockRestore();
  });

  it('renders chips inert (disabled) when no onNextStep handler is supplied (defensive baseline)', () => {
    // The host-absent contract OutcomeCard degrades to — proves the chip is only
    // live BECAUSE the host wires onNextStep.
    renderWithProviders(<OutcomeCard card={outcomeCardWithChips()} />);
    expect(screen.getByRole('button', { name: /analyze the document/i })).toBeDisabled();
  });
});
