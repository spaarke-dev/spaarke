/**
 * WorkspacePane — R5 task 038 Summary-tab registration + auto-focus tests
 *
 * Covers the acceptance criteria from
 * `tasks/038-workspace-pane-summary-tab-registration.poml`:
 *
 *   (1) Workspace pane installs a "Summary" tab on mount that hosts the
 *       existing `StructuredOutputStreamWidget` (`structured-output-stream`
 *       widget type) with `correlationId = chatSessionId` + `SUMMARIZE_SCHEMA`.
 *
 *   (2) Summary tab is the FIRST tab (leftmost) and is default-active on
 *       mount. When a workspace layout is auto-installed later in the same
 *       render cycle, Summary remains LEFT of the layout.
 *
 *   (3) `workspace.streaming_started` event with `streamId === chatSessionId`
 *       auto-focuses the Summary tab (even when another tab is active).
 *
 *   (4) Mismatched-correlationId `streaming_started` events do NOT trigger
 *       auto-focus (session isolation per FR-06).
 *
 *   (5) Manual click on a different tab during a stream is RESPECTED —
 *       subsequent `streaming_started` events within the same cycle do NOT
 *       refocus (override semantic). `streaming_complete` resets the
 *       override so the next stream can again auto-focus.
 *
 *   (6) `field_delta` events do NOT change focus (those flow into whatever
 *       tab is currently active; this is intentional).
 *
 * Test strategy:
 *
 *   - WorkspacePane depends on many hooks (`useAiSession`,
 *     `useWorkspaceLayouts`, `usePaneCollapseContext`). We mock
 *     `@spaarke/ai-widgets`'s `useAiSession` to a minimal authenticated stub
 *     so the network paths (tab restore, layout fetch) are not exercised.
 *   - We mock `useWorkspaceLayouts` to return no active layout so the
 *     default-workspace effect doesn't auto-install a layout tab (keeps the
 *     test simple — we focus on Summary + a manually-injected widget tab).
 *   - We use the REAL `PaneEventBus` + `PaneEventBusProvider` so events
 *     flow through the same machinery the production code uses. Tests
 *     dispatch events synchronously via the bus.
 *   - We mock `resolveWorkspaceWidget` to return a tiny synchronous stub
 *     component so we can assert on the rendered tab without spinning up
 *     the real `StructuredOutputStreamWidget`.
 *
 * All test invariants are stable across Jest + jsdom; no real timers, no
 * real fetch.
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import {
  PaneEventBus,
  PaneEventBusProvider,
  STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE,
} from '@spaarke/ai-widgets';

// ---------------------------------------------------------------------------
// Mock `@spaarke/ai-widgets` — keep the real bus + provider + types, but
// override `useAiSession` (minimal auth stub) and `resolveWorkspaceWidget`
// (stub component for the Summary widget — the real widget's internal
// PaneEventBus subscriptions would otherwise interfere with focus tests).
// ---------------------------------------------------------------------------

const stubWidgetRenderCounts: Record<string, number> = {};

jest.mock('@spaarke/ai-widgets', () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const actual = jest.requireActual('@spaarke/ai-widgets') as any;

  // Stub widget — renders `data-testid="widget-stub-{widgetType}"` so tests
  // can assert which widget is mounted. Bumps a render counter so tests can
  // distinguish "tab mounted" from "tab unmounted".
  const makeStub = (widgetType: string): React.FC<{ data?: unknown }> => {
    return function StubWidget(): React.JSX.Element {
      stubWidgetRenderCounts[widgetType] = (stubWidgetRenderCounts[widgetType] ?? 0) + 1;
      return <div data-testid={`widget-stub-${widgetType}`}>{widgetType}</div>;
    };
  };

  return {
    ...actual,
    useAiSession: () => ({
      isAuthenticated: true,
      // Always return 404 so the tab-restore effect's `if (response.status
      // === 404) return;` path triggers (treats as "no tabs to restore" —
      // benign). Without this default response shape, the restore effect
      // throws and logs telemetry, which is fine but noisy in test output.
      authenticatedFetch: jest.fn().mockResolvedValue({
        ok: false,
        status: 404,
        json: async () => ({}),
      } as Partial<Response> as Response),
      getAccessToken: jest.fn().mockResolvedValue('test-token'),
      bffBaseUrl: 'https://test-bff.example.com',
      tenantId: 'test-tenant',
      chatSessionId: 'session-aaa',
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
    // Resolve to a stub component synchronously. The production registry
    // resolves via dynamic `import()` — our stub bypasses that so jsdom
    // doesn't have to evaluate the real widget's `usePaneEvent` subscription.
    resolveWorkspaceWidget: jest.fn(async (widgetType: string) => {
      return makeStub(widgetType);
    }),
    getWorkspaceWidgetMetadata: jest.fn(() => ({
      displayName: 'Stubbed Widget',
      category: 'analysis',
      defaultOrder: 100,
      allowMultiple: true,
    })),
  };
});

// ---------------------------------------------------------------------------
// Mock `useWorkspaceLayouts` — return no active layout so the default-
// workspace auto-install effect inside WorkspacePane doesn't add a second
// tab. This keeps tests focused on Summary-tab behaviour.
// ---------------------------------------------------------------------------

jest.mock('../../../hooks/useWorkspaceLayouts', () => ({
  useWorkspaceLayouts: () => ({
    layouts: [],
    activeLayout: null,
    isLoading: false,
    refetch: jest.fn(),
    setActiveLayoutById: jest.fn(),
  }),
}));

// ---------------------------------------------------------------------------
// Mock pinnedWorkspaces — no pinned layouts so the pin-auto-open effect
// doesn't dispatch anything.
// ---------------------------------------------------------------------------

jest.mock('../../../services/pinnedWorkspaces', () => ({
  getPinnedWorkspaces: jest.fn(() => []),
  pinWorkspace: jest.fn(),
  unpinWorkspace: jest.fn(),
}));

// ---------------------------------------------------------------------------
// Mock pane-header from @spaarke/ui-components — minimal pass-through that
// renders the title + rightSlot so the test renders without depending on the
// real PaneHeader's styling internals.
// ---------------------------------------------------------------------------

// We intentionally do NOT call jest.requireActual — the @spaarke/ui-components
// barrel pulls in ESM-only deps (marked, d3-force, dompurify) that crash
// ts-jest. WorkspacePane only consumes `PaneHeader` from this package; nothing
// else is referenced via the test render path.
jest.mock('@spaarke/ui-components', () => ({
  PaneHeader: ({ title, rightSlot }: { title: string; rightSlot?: React.ReactNode }) => (
    <div data-testid="pane-header">
      <span>{title}</span>
      {rightSlot}
    </div>
  ),
}));

// Import AFTER mocks so module resolution picks them up.
import { WorkspacePane } from '../WorkspacePane';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

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

/**
 * Wait for the Summary tab to mount (its stub widget appears) — we await
 * the dynamic-import promise resolution by flushing microtasks AND macrotasks.
 *
 * `usePaneEvent` registers via useEffect (a passive effect), and the
 * default-layout auto-install effect defers its dispatch via `setTimeout(0)`.
 * We need to flush both so all pending async work resolves before assertions.
 */
async function waitForSummaryTab(): Promise<void> {
  await act(async () => {
    // Flush microtasks (resolveWorkspaceWidget promise chain).
    for (let i = 0; i < 4; i++) {
      await Promise.resolve();
    }
    // Flush a macrotask so deferred setTimeout(..., 0) dispatches (auto-
    // install default layout) have a chance to run.
    await new Promise(resolve => setTimeout(resolve, 0));
    // Drain any micro/macro tasks queued by the above.
    for (let i = 0; i < 4; i++) {
      await Promise.resolve();
    }
  });
}

beforeEach(() => {
  for (const key of Object.keys(stubWidgetRenderCounts)) {
    delete stubWidgetRenderCounts[key];
  }
  // jsdom does not implement Element.scrollIntoView; WorkspaceTabManagerComponent
  // calls it inside a requestAnimationFrame callback after each active-tab
  // change. Stub so the rAF callback doesn't throw.
  if (!Element.prototype.scrollIntoView) {
    // eslint-disable-next-line @typescript-eslint/no-empty-function
    Element.prototype.scrollIntoView = function (): void {};
  }
});

// ---------------------------------------------------------------------------
// (1) Summary tab installs on mount + is FIRST + is default-active
// ---------------------------------------------------------------------------

describe('WorkspacePane — Summary tab registration (R5 task 038)', () => {
  it('mounts a Summary tab using StructuredOutputStreamWidget', async () => {
    renderPane();
    await waitForSummaryTab();

    // Tab strip should show a "Summary" tab.
    expect(screen.getByRole('tab', { name: /summary/i })).toBeInTheDocument();

    // The Summary widget stub should render (default-active).
    expect(
      screen.getByTestId(`widget-stub-${STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE}`),
    ).toBeInTheDocument();
  });

  it('places the Summary tab FIRST (leftmost) — before any auto-installed layout', async () => {
    const { bus } = renderPane();
    await waitForSummaryTab();

    // Inject a SECOND tab via the standard `widget_load` dispatch (simulates
    // the default-layout auto-install OR a pinned workspace open). The
    // standard `addTab` path appends; Summary should stay leftmost.
    act(() => {
      bus.dispatch('workspace', {
        type: 'widget_load',
        widgetType: 'workspace',
        widgetData: { layoutId: 'corp', layoutName: 'Corporate' },
        displayName: 'Corporate',
      });
    });
    await waitForSummaryTab();

    const tabs = screen.getAllByRole('tab');
    expect(tabs.length).toBeGreaterThanOrEqual(2);
    expect(tabs[0]).toHaveTextContent(/summary/i);
    expect(tabs[1]).toHaveTextContent(/corporate/i);
  });

  it('makes Summary the default-active tab on mount', async () => {
    renderPane();
    await waitForSummaryTab();

    // Verify the Summary tab is mounted as the ACTIVE tab by checking that
    // its widget stub is rendered in the content area (only the active tab's
    // component is mounted per WorkspaceTabManagerComponent's ActiveWidgetContent
    // semantics).
    expect(
      screen.getByTestId(`widget-stub-${STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE}`),
    ).toBeInTheDocument();

    // Wait for the Fluent v9 TabList's internal context to reflect the
    // controlled selectedValue — context updates settle after the initial
    // render commit, hence the waitFor.
    await waitFor(() => {
      const summaryTab = screen.getByRole('tab', { name: /summary/i });
      expect(summaryTab.getAttribute('aria-selected')).toBe('true');
    });
  });
});

// ---------------------------------------------------------------------------
// (2) Auto-focus on workspace.streaming_started for matching correlationId
// ---------------------------------------------------------------------------

describe('WorkspacePane — Summary tab auto-focus on streaming_started', () => {
  it('auto-focuses Summary when streaming_started fires with the active sessionId', async () => {
    const { bus } = renderPane();
    await waitForSummaryTab();

    // Add a second tab and switch to it so Summary is NOT active.
    act(() => {
      bus.dispatch('workspace', {
        type: 'widget_load',
        widgetType: 'workspace',
        widgetData: { layoutId: 'corp' },
        displayName: 'Corporate',
      });
    });
    await waitForSummaryTab();

    // Verify Corporate (the newly added tab) is now active (addTab auto-activates).
    const corpTab = screen.getByRole('tab', { name: /corporate/i });
    expect(corpTab.getAttribute('aria-selected')).toBe('true');

    // Fire streaming_started for the active session — Summary should refocus.
    act(() => {
      bus.dispatch('workspace', {
        type: 'streaming_started',
        streamId: 'session-aaa', // matches the mocked chatSessionId
      });
    });

    const summaryTab = screen.getByRole('tab', { name: /summary/i });
    expect(summaryTab.getAttribute('aria-selected')).toBe('true');
  });

  it('does NOT auto-focus when streaming_started carries a mismatched streamId', async () => {
    const { bus } = renderPane();
    await waitForSummaryTab();

    // Add + activate a second tab (Corporate).
    act(() => {
      bus.dispatch('workspace', {
        type: 'widget_load',
        widgetType: 'workspace',
        widgetData: { layoutId: 'corp' },
        displayName: 'Corporate',
      });
    });
    await waitForSummaryTab();

    const corpTab = screen.getByRole('tab', { name: /corporate/i });
    expect(corpTab.getAttribute('aria-selected')).toBe('true');

    // streaming_started for a DIFFERENT session — must NOT refocus Summary.
    act(() => {
      bus.dispatch('workspace', {
        type: 'streaming_started',
        streamId: 'session-other-bbb', // does NOT match 'session-aaa'
      });
    });

    expect(corpTab.getAttribute('aria-selected')).toBe('true');
    const summaryTab = screen.getByRole('tab', { name: /summary/i });
    expect(summaryTab.getAttribute('aria-selected')).toBe('false');
  });
});

// ---------------------------------------------------------------------------
// (3) Manual override + reset on streaming_complete
// ---------------------------------------------------------------------------

describe('WorkspacePane — manual override during stream', () => {
  it('respects user override during a stream cycle (no refocus until streaming_complete)', async () => {
    const user = userEvent.setup();
    const { bus } = renderPane();
    await waitForSummaryTab();

    // Add a Corporate tab.
    act(() => {
      bus.dispatch('workspace', {
        type: 'widget_load',
        widgetType: 'workspace',
        widgetData: { layoutId: 'corp' },
        displayName: 'Corporate',
      });
    });
    await waitForSummaryTab();

    // streaming_started focuses Summary.
    act(() => {
      bus.dispatch('workspace', {
        type: 'streaming_started',
        streamId: 'session-aaa',
      });
    });
    const summaryTab = screen.getByRole('tab', { name: /summary/i });
    expect(summaryTab.getAttribute('aria-selected')).toBe('true');

    // User manually clicks Corporate during the stream — override engaged.
    const corpTab = screen.getByRole('tab', { name: /corporate/i });
    await user.click(corpTab);
    expect(corpTab.getAttribute('aria-selected')).toBe('true');

    // A SECOND streaming_started (e.g. concurrent summarize) MUST respect the
    // override and NOT pull focus back.
    act(() => {
      bus.dispatch('workspace', {
        type: 'streaming_started',
        streamId: 'session-aaa',
      });
    });
    expect(corpTab.getAttribute('aria-selected')).toBe('true');
    expect(summaryTab.getAttribute('aria-selected')).toBe('false');

    // streaming_complete clears the override.
    act(() => {
      bus.dispatch('workspace', {
        type: 'streaming_complete',
        streamId: 'session-aaa',
        completionStatus: 'complete',
      });
    });
    // Corporate is still active (we don't yank focus on complete).
    expect(corpTab.getAttribute('aria-selected')).toBe('true');

    // The NEXT streaming_started (after complete) should again auto-focus.
    act(() => {
      bus.dispatch('workspace', {
        type: 'streaming_started',
        streamId: 'session-aaa',
      });
    });
    expect(summaryTab.getAttribute('aria-selected')).toBe('true');
  });
});

// ---------------------------------------------------------------------------
// (4) field_delta events MUST NOT change focus
// ---------------------------------------------------------------------------

describe('WorkspacePane — field_delta never changes focus', () => {
  it('field_delta events do not pull focus to Summary', async () => {
    const { bus } = renderPane();
    await waitForSummaryTab();

    // Add a Corporate tab and let addTab auto-activate it.
    act(() => {
      bus.dispatch('workspace', {
        type: 'widget_load',
        widgetType: 'workspace',
        widgetData: { layoutId: 'corp' },
        displayName: 'Corporate',
      });
    });
    await waitForSummaryTab();

    const corpTab = screen.getByRole('tab', { name: /corporate/i });
    expect(corpTab.getAttribute('aria-selected')).toBe('true');

    // Fire several field_delta events — focus must stay on Corporate.
    for (let i = 0; i < 5; i++) {
      act(() => {
        bus.dispatch('workspace', {
          type: 'field_delta',
          streamId: 'session-aaa',
          fieldPath: 'tldr',
          fieldContent: `chunk-${i}`,
          sequence: i,
        });
      });
    }

    expect(corpTab.getAttribute('aria-selected')).toBe('true');
    const summaryTab = screen.getByRole('tab', { name: /summary/i });
    expect(summaryTab.getAttribute('aria-selected')).toBe('false');
  });
});
