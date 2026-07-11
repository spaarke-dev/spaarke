/**
 * WorkspacePane — FR-34 D-F3 honest CONTENT-render ack tests (task 071).
 *
 * The base D-F3 loop (WorkspacePane.ui-action-ack.test.tsx) acks a server
 * `workspace_open_tab` frame the moment the tab SHELL is materialized. That is the
 * R2-D fabrication gap for a CONTENT-bearing open: a chat "open as a document" frame
 * that carries a full-document draft SEED (widgetData.compose.draft.{ledgerRef,sessionId},
 * DEF-08) would ack tab-open even if the seeded draft NEVER renders — a false
 * "I pasted the draft into Compose" while the tab is blank.
 *
 * Task 071 defers the ack for a seeded frame: WorkspacePane stashes the server frame
 * id keyed by the draft's ledgerRef and fires the ack ONLY when ComposeWorkspace emits
 * `workspace.compose_content_rendered` for that ledgerRef (the editor actually rendered
 * the seeded body). These tests assert:
 *
 *   (1) a seeded-draft frame acks (POST /ack with the SAME frameId) ONLY AFTER the
 *       content render signal — NOT when the tab shell opens (the deferral); and
 *   (2) a render FAILURE (no `compose_content_rendered` ever emitted) does NOT ack —
 *       the honest-failure path (the server's WaitForAckAsync then times out).
 *
 * A regression that acks on tab-open-before-render fails assertion (1)'s
 * "no ack yet after the tab opened" step.
 *
 * Harness mirrors WorkspacePane.ui-action-ack.test.tsx. ComposeWorkspace is NOT
 * mounted here (the workspace widget is stubbed), so the render signal is simulated by
 * dispatching `compose_content_rendered` on the same bus — exactly what ComposeWorkspace
 * dispatches from its seed-render watcher effect.
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';

interface RecordedFetch {
  url: string;
  method: string;
  body: unknown;
}

const recordedFetches: RecordedFetch[] = [];

const authenticatedFetchMock = jest.fn(
  async (url: string, init?: RequestInit): Promise<Response> => {
    const method = init?.method ?? 'GET';
    recordedFetches.push({
      url,
      method,
      body: init?.body ? JSON.parse(String(init.body)) : undefined,
    });
    if (method === 'GET' && url.includes('/tabs')) {
      return {
        ok: true,
        status: 200,
        json: async () => ({ tabs: [], activeTabId: null }),
      } as Partial<Response> as Response;
    }
    return {
      ok: true,
      status: 200,
      json: async () => ({ acknowledged: true }),
    } as Partial<Response> as Response;
  },
);

jest.mock('@spaarke/ai-widgets', () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const actual = jest.requireActual('@spaarke/ai-widgets') as any;

  const makeStub = (widgetType: string): React.FC<{ data?: unknown }> => {
    return function StubWidget(): React.JSX.Element {
      return <div data-testid={`widget-stub-${widgetType}`}>{widgetType}</div>;
    };
  };

  return {
    ...actual,
    useAiSession: () => ({
      isAuthenticated: true,
      authenticatedFetch: authenticatedFetchMock,
      getAccessToken: jest.fn().mockResolvedValue('test-token'),
      bffBaseUrl: 'https://test-bff.example.com',
      tenantId: 'test-tenant',
      chatSessionId: 'session-render-ack',
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
    resolveWorkspaceWidget: jest.fn(async (widgetType: string) => makeStub(widgetType)),
    getWorkspaceWidgetMetadata: jest.fn(() => ({
      displayName: 'Workspace',
      category: 'workspace',
      defaultOrder: 100,
      allowMultiple: true,
    })),
  };
});

// No default layout available — keeps this test scoped to the explicit dispatch below.
jest.mock('../../../hooks/useWorkspaceLayouts', () => ({
  useWorkspaceLayouts: () => ({
    layouts: [],
    activeLayout: null,
    isLoading: false,
    refetch: jest.fn(),
    setActiveLayoutById: jest.fn(),
  }),
}));

jest.mock('../../../services/pinnedWorkspaces', () => ({
  getPinnedWorkspaces: jest.fn(() => []),
  prunePinnedToKnown: jest.fn(() => []),
  isPinned: jest.fn(() => false),
  pinWorkspace: jest.fn(),
  unpinWorkspace: jest.fn(),
  setPinnedWorkspacesOrder: jest.fn(),
  moveWorkspaceToTop: jest.fn(),
}));

jest.mock('@spaarke/ui-components', () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const actual = jest.requireActual('@spaarke/ui-components') as any;
  return {
    ...actual,
    PaneHeader: ({ title, rightSlot }: { title: string; rightSlot?: React.ReactNode }) => (
      <div data-testid="pane-header">
        <span>{title}</span>
        {rightSlot}
      </div>
    ),
  };
});

// Import AFTER mocks so module resolution picks them up.
import { WorkspacePane } from '../WorkspacePane';

function renderPane(): { bus: PaneEventBus } {
  const bus = new PaneEventBus();
  render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={bus}>
        <WorkspacePane />
      </PaneEventBusProvider>
    </FluentProvider>,
  );
  return { bus };
}

const FRAME_ID = 'server-frame-def08';
const LEDGER_REF = 'binding-1@t1';

/** A chat "open as a document" seeded-draft frame (DEF-08 Part A) — ack-gated. */
function seededDraftOpen() {
  return {
    type: 'widget_load' as const,
    widgetType: 'workspace',
    widgetData: {
      layoutId: 'layout-compose',
      layoutName: 'Compose',
      compose: { draft: { ledgerRef: LEDGER_REF, sessionId: 'session-render-ack' } },
    },
    displayName: 'Compose',
    frameId: FRAME_ID,
  };
}

const ackCalls = () => recordedFetches.filter((f) => f.url.includes('/ack'));

beforeEach(() => {
  recordedFetches.length = 0;
  authenticatedFetchMock.mockClear();
  if (!Element.prototype.scrollIntoView) {
    // eslint-disable-next-line @typescript-eslint/no-empty-function
    Element.prototype.scrollIntoView = function (): void {};
  }
});

describe('WorkspacePane — FR-34 D-F3 CONTENT-render ack (task 071)', () => {
  it('acks a seeded-draft frame ONLY AFTER the content renders — NOT on tab-shell open', async () => {
    const { bus } = renderPane();

    // Server-initiated seeded-draft open — the tab SHELL materializes...
    bus.dispatch('workspace', seededDraftOpen());
    await screen.findByRole('tab', { name: /compose/i });

    // ...but the seeded draft has NOT rendered yet, so NO ack must be sent (the deferral).
    // Give any (incorrect) tab-open ack a chance to fire before asserting its absence.
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(ackCalls()).toHaveLength(0);

    // ComposeWorkspace renders the seeded draft and emits the content-render signal.
    bus.dispatch('workspace', {
      type: 'compose_content_rendered',
      ledgerRef: LEDGER_REF,
      sessionId: 'session-render-ack',
    });

    // NOW — and only now — the ack POSTs, referencing the SAME server frame id.
    await waitFor(() => expect(ackCalls().length).toBeGreaterThan(0));
    const ackCall = ackCalls()[0];
    expect(ackCall.method).toBe('POST');
    expect(ackCall.url).toContain('/ai/chat/sessions/session-render-ack/ack');
    expect(ackCall.body).toEqual({ frameId: FRAME_ID });
  });

  it('does NOT ack a seeded-draft frame whose content never renders (honest failure)', async () => {
    const { bus } = renderPane();

    // Seeded-draft open — the tab shell opens...
    bus.dispatch('workspace', seededDraftOpen());
    await screen.findByRole('tab', { name: /compose/i });

    // ...but the seed FAILS to render (ComposeWorkspace never emits compose_content_rendered).
    // Give a generous window for any (incorrect) ack to fire.
    await new Promise((resolve) => setTimeout(resolve, 100));

    // No ack was ever sent → the server's WaitForAckAsync times out → honest failure.
    expect(ackCalls()).toHaveLength(0);
  });
});
