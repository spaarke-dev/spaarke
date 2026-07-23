/**
 * ComposeTraceHost — reachability + D-F4 wiring test (compose-r2 FR-32 / task 064).
 *
 * E2E DoD (project CLAUDE.md): a client component meant to be mounted needs
 * evidence it is MOUNTED + REACHABLE in the running host, not just built in
 * isolation. This test renders the REAL `ContextPaneController` (the host),
 * selects the real `execution-trace` context tool, and asserts:
 *
 *   1. Reachability — selecting the tool mounts the core ExecutionTraceWidget in
 *      the Context pane (`context-pane-execution-trace` → `execution-trace-widget`).
 *   2. D-F4 wiring — the host's `restoreTrace` fires on mount and hits the BFF
 *      decision-traceability read surface
 *      `GET /api/ai/chat/sessions/{sessionId}/trace` via `@spaarke/auth`
 *      `authenticatedFetch` (ADR-028), scoped to the active chat session id.
 *      This proves the widget is hosted as the D-F4 view over TraceEvent v1 —
 *      not a Compose-local trace rendering (charter §3.4).
 *   3. Audit-only invariant — the Context pane exposes NO input control
 *      (textbox / searchbox / spinbutton) in the trace tool; it is a read-only
 *      provenance/audit surface (project CLAUDE.md).
 *
 * Mock strategy mirrors the SpaarkeAi convention (ConversationPane suites):
 * `jest.requireActual('@spaarke/ai-widgets')` keeps the REAL widget + registry +
 * bus, overriding only `useAiSession` to supply a spy `authenticatedFetch` and a
 * non-null chat session id.
 */

import '@testing-library/jest-dom';
import React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';
import { ContextPaneController } from '../ContextPaneController';
import type { ShellStageContextValue } from '../../shell/ThreePaneShell';
import { ShellStageContext } from '../../shell/ThreePaneShell';

const TEST_SESSION_ID = '00000000-0000-0000-0000-000000000abc';
const TEST_BFF = 'https://test-bff.example.com';

// `mock`-prefixed so jest's out-of-scope guard permits referencing it inside the
// mock factory. Read only when useAiSession() is CALLED (render time), so the
// hoisting order is safe.
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

function renderController() {
  const bus = new PaneEventBus();
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
  // Select the execution-trace context tool (useContextTool reads sessionStorage
  // first — JSON-encoded value under this key).
  window.sessionStorage.setItem(
    'spaarke:context:selected-tool',
    JSON.stringify('execution-trace')
  );
});

afterEach(() => {
  window.sessionStorage.clear();
  window.localStorage.clear();
});

describe('ComposeTraceHost — hosted in the Compose Context pane (FR-32 / D-F4)', () => {
  it('mounts the core ExecutionTraceWidget when the execution-trace tool is selected (reachable in host)', async () => {
    renderController();

    // The host branch renders.
    expect(
      await screen.findByTestId('context-pane-execution-trace')
    ).toBeInTheDocument();
    // The CORE widget (consumed as-is) is actually mounted inside it.
    expect(await screen.findByTestId('execution-trace-widget')).toBeInTheDocument();
  });

  it('wires the D-F4 restoreTrace to GET /sessions/{id}/trace via @spaarke/auth on mount', async () => {
    renderController();

    await waitFor(() => {
      expect(mockAuthenticatedFetch).toHaveBeenCalled();
    });

    const calledUrls = mockAuthenticatedFetch.mock.calls.map((c) => String(c[0]));
    const traceUrl = calledUrls.find((u) => u.includes('/trace'));
    expect(traceUrl).toBeDefined();
    // Session-scoped read against the BFF host (ISessionTraceReader surface).
    expect(traceUrl).toContain(`${TEST_BFF}/api/ai/chat/sessions/`);
    expect(traceUrl).toContain(encodeURIComponent(TEST_SESSION_ID));
    expect(traceUrl).toContain('/trace');
    // GET (read-only projection — never a write).
    const traceCall = mockAuthenticatedFetch.mock.calls.find((c) =>
      String(c[0]).includes('/trace')
    );
    expect((traceCall?.[1] as RequestInit | undefined)?.method).toBe('GET');
  });

  it('keeps the Context pane AUDIT-ONLY — no input control in the trace tool', async () => {
    renderController();

    // Widget mounted...
    expect(await screen.findByTestId('execution-trace-widget')).toBeInTheDocument();
    // ...and the pane exposes no decision-capturing input affordance.
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
    expect(screen.queryByRole('searchbox')).not.toBeInTheDocument();
    expect(screen.queryByRole('spinbutton')).not.toBeInTheDocument();
  });
});
