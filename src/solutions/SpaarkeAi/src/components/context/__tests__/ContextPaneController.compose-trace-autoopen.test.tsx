/**
 * ContextPaneController — Compose-tab → Execution Trace auto-open (Wave 4 / FIX D).
 *
 * Owner decision: activating a Compose workspace tab auto-opens the Execution
 * Trace in the Context pane (the transparency surface showing which
 * tools/knowledge/skills the AI deploys). This is a ONE-SHOT select-on-activate,
 * NOT a lock — a subsequent non-Compose tab change (or a manual tool pick) does
 * NOT force it back.
 *
 * These tests render the REAL ContextPaneController (host) and drive the real
 * `workspace` PaneEventBus `tab_change` event, asserting:
 *   1. A Compose tab (widgetData.compose set) auto-selects Execution Trace —
 *      the `context-pane-execution-trace` host branch + core `execution-trace-widget`
 *      mount.
 *   2. The `layoutName === "Compose"` discriminant variant also triggers it.
 *   3. A NON-Compose tab_change does NOT force Execution Trace (stays at the
 *      Quick Start default).
 *
 * Mock strategy mirrors ComposeTraceHost.reachable.test.tsx: keep the REAL
 * @spaarke/ai-widgets (registry + bus + widget), overriding only `useAiSession`
 * so ComposeTraceHost can mount with a non-null chat session id + spy fetch.
 */

import '@testing-library/jest-dom';
import React, { act } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';
import { ContextPaneController } from '../ContextPaneController';
import type { ShellStageContextValue } from '../../shell/ThreePaneShell';
import { ShellStageContext } from '../../shell/ThreePaneShell';

const TEST_SESSION_ID = '00000000-0000-0000-0000-000000000abc';
const TEST_BFF = 'https://test-bff.example.com';

const mockAuthenticatedFetch = jest.fn(
  async (_url: string, _init?: RequestInit) => ({
    ok: true,
    json: async () => [] as unknown[],
  })
);

jest.mock('@spaarke/ai-widgets', () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const actual = jest.requireActual('@spaarke/ai-widgets') as any;
  return {
    ...actual,
    useAiSession: () => ({
      isAuthenticated: true,
      authenticatedFetch: mockAuthenticatedFetch,
      getAccessToken: jest.fn(),
      bffBaseUrl: TEST_BFF,
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

function makeStageContext(
  overrides: Partial<ShellStageContextValue> = {}
): ShellStageContextValue {
  return {
    currentStage: 'active-chat',
    toLoading: jest.fn(),
    toActiveChat: jest.fn(),
    toReview: jest.fn(),
    toActiveWork: jest.fn(),
    reset: jest.fn(),
    ...overrides,
  };
}

function renderController(bus: PaneEventBus) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={bus}>
        <ShellStageContext.Provider value={makeStageContext()}>
          <ContextPaneController />
        </ShellStageContext.Provider>
      </PaneEventBusProvider>
    </FluentProvider>
  );
}

beforeEach(() => {
  mockAuthenticatedFetch.mockClear();
  // Start from the hardcoded default tool (quick-start) — no seeded selection.
  window.sessionStorage.clear();
  window.localStorage.clear();
});

afterEach(() => {
  window.sessionStorage.clear();
  window.localStorage.clear();
});

describe('ContextPaneController — Compose tab auto-opens Execution Trace (FIX D)', () => {
  it('shows Execution Trace at rest (the default tool — decision-1, 2026-07-19)', () => {
    const bus = new PaneEventBus();
    renderController(bus);

    // decision-1: execution-trace is now the AT-REST default (Quick Start removed).
    expect(screen.getByTestId('context-pane-execution-trace')).toBeInTheDocument();
    expect(screen.queryByTestId('context-pane-welcome')).not.toBeInTheDocument();
  });

  it('auto-selects Execution Trace when a Compose tab (widgetData.compose) becomes active', async () => {
    const bus = new PaneEventBus();
    renderController(bus);

    act(() => {
      bus.dispatch('workspace', {
        type: 'tab_change',
        widgetType: 'workspace',
        widgetData: {
          layoutId: 'layout-compose-1',
          layoutName: 'Compose',
          compose: { upload: { sessionId: 's1', sessionFileId: 'f1' } },
        },
      });
    });

    // Host branch renders + the core widget is mounted (reachable).
    expect(
      await screen.findByTestId('context-pane-execution-trace')
    ).toBeInTheDocument();
    expect(await screen.findByTestId('execution-trace-widget')).toBeInTheDocument();
  });

  it('auto-selects Execution Trace when the DIRECT "compose" widget tab becomes active (spaarkeai-compose-r2)', async () => {
    const bus = new PaneEventBus();
    renderController(bus);

    act(() => {
      bus.dispatch('workspace', {
        type: 'tab_change',
        // The first-class DIRECT Compose widget — recognized by widgetType alone,
        // regardless of the widgetData seed shape (here: an upload seed).
        widgetType: 'compose',
        widgetData: {
          compose: { upload: { sessionId: 's1', sessionFileId: 'f1' } },
        },
      });
    });

    expect(
      await screen.findByTestId('context-pane-execution-trace')
    ).toBeInTheDocument();
    expect(await screen.findByTestId('execution-trace-widget')).toBeInTheDocument();
  });

  it('auto-selects Execution Trace via the layoutName === "Compose" discriminant (no compose seed)', async () => {
    const bus = new PaneEventBus();
    renderController(bus);

    act(() => {
      bus.dispatch('workspace', {
        type: 'tab_change',
        widgetType: 'workspace',
        widgetData: {
          layoutId: 'layout-compose-2',
          layoutName: 'Compose',
        },
      });
    });

    expect(
      await screen.findByTestId('context-pane-execution-trace')
    ).toBeInTheDocument();
  });

  it('does NOT force Execution Trace for a non-Compose tab_change (leaves the current tool)', async () => {
    // decision-1: execution-trace is now the DEFAULT, so "doesn't force" is verified by seeding a
    // DIFFERENT tool first (semantic-search) and confirming a non-Compose tab_change leaves it there.
    window.sessionStorage.setItem('spaarke:context:selected-tool', JSON.stringify('semantic-search'));
    const bus = new PaneEventBus();
    renderController(bus);

    expect(screen.getByTestId('context-pane-semantic-search')).toBeInTheDocument();

    act(() => {
      bus.dispatch('workspace', {
        type: 'tab_change',
        widgetType: 'workspace',
        widgetData: {
          layoutId: 'layout-calendar-1',
          layoutName: 'Calendar',
        },
      });
    });

    // A non-Compose tab must NOT one-shot-switch to Execution Trace — the user's
    // semantic-search selection stays put.
    await waitFor(() => {
      expect(screen.getByTestId('context-pane-controller')).toBeInTheDocument();
    });
    expect(screen.getByTestId('context-pane-semantic-search')).toBeInTheDocument();
    expect(screen.queryByTestId('context-pane-execution-trace')).not.toBeInTheDocument();
  });
});
