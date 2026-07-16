/**
 * WorkspacePane — action-outcome truthfulness invariant (task 020 / FR-C1 + FR-C2).
 *
 * Two existential invariants for a grounded dispatcher (spec P5; design §4):
 *
 *   UC-5 (FR-C1) — no fabricated success. Every server-initiated UI-action claim
 *   ("opened X") is ack-gated on the action GENUINELY materializing. The ack for a
 *   plain (layout/other-widget) open was previously fired at bare tab-SHELL creation
 *   (manager.addTab), i.e. BEFORE the widget component resolved + attached — an
 *   optimistic claim. Task 020 moves it to fire ONLY after resolveWorkspaceWidget()
 *   resolves and the component is attached. If resolution never completes, NO ack is
 *   sent → the server's WaitForAckAsync times out → honest failure, never a fabricated
 *   success.
 *
 *   UC-4 (FR-C2) — no collateral teardown. An orchestrated action (here: an exclusive
 *   playbook selection, standing in for the R2-UAT "a delete closed an unrelated Compose
 *   tab") must NOT tear down an unrelated live Compose tab. The teardown is scoped to
 *   preserve 'compose' work-product surfaces.
 *
 * Harness mirrors WorkspacePane.ui-action-ack.test.tsx. resolveWorkspaceWidget is a
 * per-test controllable mock so we can hold a resolution pending (the incomplete-action
 * case) or resolve it (the completed-action case).
 *
 * Test category per ADR-038: Component Test (KEEP — SpaarkeAi presenter render + ack
 * contract observable by the parent + end user).
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

// Per-test controllable widget resolver. Default: resolve a stub immediately.
type StubResolver = (widgetType: string) => Promise<React.FC<{ data?: unknown }>>;
const makeStub = (widgetType: string): React.FC<{ data?: unknown }> =>
  function StubWidget(): React.JSX.Element {
    return <div data-testid={`widget-stub-${widgetType}`}>{widgetType}</div>;
  };
let resolveImpl: StubResolver = async (t: string) => makeStub(t);

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
  return {
    ...actual,
    useAiSession: () => ({
      isAuthenticated: true,
      authenticatedFetch: authenticatedFetchMock,
      getAccessToken: jest.fn().mockResolvedValue('test-token'),
      bffBaseUrl: 'https://test-bff.example.com',
      tenantId: 'test-tenant',
      chatSessionId: 'session-truth',
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
    resolveWorkspaceWidget: jest.fn((widgetType: string) => resolveImpl(widgetType)),
    getWorkspaceWidgetMetadata: jest.fn(() => ({
      displayName: 'Workspace',
      category: 'workspace',
      defaultOrder: 100,
      allowMultiple: true,
    })),
  };
});

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

const ackCalls = () => recordedFetches.filter((f) => f.url.includes('/ack'));

beforeEach(() => {
  recordedFetches.length = 0;
  authenticatedFetchMock.mockClear();
  resolveImpl = async (t: string) => makeStub(t);
  if (!Element.prototype.scrollIntoView) {
    // eslint-disable-next-line @typescript-eslint/no-empty-function
    Element.prototype.scrollIntoView = function (): void {};
  }
});

describe('WorkspacePane — UC-5 no fabricated success (task 020 / FR-C1)', () => {
  it('does NOT ack a plain widget open whose widget never resolves (honest failure)', async () => {
    // The action does NOT complete: hold the widget resolution pending forever.
    resolveImpl = () => new Promise(() => {
      /* never resolves — the widget fails to materialize */
    });

    const { bus } = renderPane();

    // Server-initiated open of a plain (non-compose, non-email) widget with a
    // frame id — an ack-gated tool call is waiting on genuine materialization.
    bus.dispatch('workspace', {
      type: 'widget_load',
      widgetType: 'daily-briefing',
      widgetData: { layoutId: 'db-1' },
      displayName: 'Daily Briefing',
      frameId: 'frame-never-renders',
    });

    // The tab SHELL is added (the pane reacts) — but the widget never resolves,
    // so the claim must NOT be made. Give any optimistic (tab-shell) ack a
    // generous window to (incorrectly) fire.
    await new Promise((resolve) => setTimeout(resolve, 80));

    // No ack was ever sent → the server times out → honest failure, never a
    // fabricated "opened Daily Briefing".
    expect(ackCalls()).toHaveLength(0);
  });

  it('acks a plain widget open ONLY AFTER the widget resolves + attaches (completed action)', async () => {
    // Deferred resolution we control, so we can prove the ack is gated on it.
    let resolveWidget!: (c: React.FC<{ data?: unknown }>) => void;
    resolveImpl = () =>
      new Promise<React.FC<{ data?: unknown }>>((res) => {
        resolveWidget = res;
      });

    const { bus } = renderPane();

    bus.dispatch('workspace', {
      type: 'widget_load',
      widgetType: 'daily-briefing',
      widgetData: { layoutId: 'db-1' },
      displayName: 'Daily Briefing',
      frameId: 'frame-will-render',
    });

    // Before the widget resolves: no ack yet (the claim is withheld).
    await new Promise((resolve) => setTimeout(resolve, 40));
    expect(ackCalls()).toHaveLength(0);

    // The widget resolves + attaches — the action is now genuinely complete.
    resolveWidget(makeStub('daily-briefing'));

    // NOW the ack POSTs, referencing the exact server frame id.
    await waitFor(() => expect(ackCalls().length).toBeGreaterThan(0));
    const ack = ackCalls()[0];
    expect(ack.method).toBe('POST');
    expect(ack.url).toContain('/ai/chat/sessions/session-truth/ack');
    expect(ack.body).toEqual({ frameId: 'frame-will-render' });
  });
});

describe('WorkspacePane — UC-4 no collateral teardown (task 020 / FR-C2)', () => {
  it('keeps an unrelated Compose tab open when an orchestrated action tears down the workspace, while removing the acted-on tab', async () => {
    const { bus } = renderPane();

    // The user has a live Compose tab open (holds an unsaved draft)...
    bus.dispatch('workspace', {
      type: 'widget_load',
      widgetType: 'compose',
      widgetData: { compose: { draft: { ledgerRef: 'draft-1@t1' } } },
      displayName: 'Compose',
    });
    await screen.findByRole('tab', { name: /compose/i });

    // ...alongside an unrelated non-compose widget tab (the surface an
    // orchestrated action — e.g. a record delete — targets).
    bus.dispatch('workspace', {
      type: 'widget_load',
      widgetType: 'documents',
      widgetData: { layoutId: 'docs' },
      displayName: 'Documents',
    });
    await screen.findByRole('tab', { name: /documents/i });

    // An orchestrated action fires the shipped blanket-teardown path (exclusive
    // playbook selection — stands in for the UC-4 "a delete closed an unrelated
    // Compose tab"): it clears the workspace "stage".
    bus.dispatch('conversation', {
      type: 'playbook-selected',
      isExclusive: true,
      defaultWidgets: [],
    });

    // The non-compose "stage" tab is torn down (the teardown did happen)...
    await waitFor(() =>
      expect(screen.queryByRole('tab', { name: /documents/i })).not.toBeInTheDocument(),
    );

    // ...but the UNRELATED Compose tab is STILL open — no collateral teardown.
    expect(screen.getByRole('tab', { name: /compose/i })).toBeInTheDocument();
  });
});
