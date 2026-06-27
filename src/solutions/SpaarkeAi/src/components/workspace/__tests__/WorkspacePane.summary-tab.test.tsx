/**
 * WorkspacePane — Summary-tab DEFERRED install + per-run tab tests
 *
 * R6 Hotfix Wave B-G9c2 (B7 + B8 combined; 2026-06-10).
 *
 * The R5 task 038 eager auto-install (Summary tab prepended on mount) was
 * removed. Each summarize run now dispatches its own `workspace.widget_load`
 * with a unique `correlationId` and a tab title that includes the source
 * filename. This file's old assertions (1) Summary mounts on mount,
 * (2) Summary is leftmost, (3) Summary auto-focuses on `streaming_started`
 * — no longer apply. The replacement assertions below cover the new model:
 *
 *   (B7-1) WorkspacePane does NOT install a Summary tab on mount — when
 *          tabs.length === 0 in the steady-state cold-load the pane shows
 *          its first-paint placeholder.
 *
 *   (B7-2) WorkspacePane installs a Summary tab on the FIRST
 *          `workspace.widget_load` event carrying the structured-output-
 *          stream widget type (i.e., when a summarize run starts).
 *
 *   (B8-1) Two consecutive `widget_load` dispatches (consecutive summarize
 *          runs) create TWO Summary tabs (NOT one replaced tab). Each tab
 *          carries its own correlationId.
 *
 *   (B8-2) The dispatched `displayName` (`Summary: <filename>`) is used as
 *          the tab title.
 *
 * Test strategy mirrors the prior R5 task 038 suite: mock `useAiSession` to
 * a minimal authenticated stub; mock `useWorkspaceLayouts` to return no
 * active layout (so the default-workspace effect doesn't add a layout tab);
 * use the REAL `PaneEventBus` + `PaneEventBusProvider`; mock
 * `resolveWorkspaceWidget` to return a synchronous stub component.
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { act, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import {
  PaneEventBus,
  PaneEventBusProvider,
  STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE,
} from '@spaarke/ai-widgets';

// ---------------------------------------------------------------------------
// Mock `@spaarke/ai-widgets` — keep the real bus + provider + types, but
// override `useAiSession` (minimal auth stub) and `resolveWorkspaceWidget`
// (stub component for the structured-output-stream widget — the real
// widget's internal PaneEventBus subscriptions would otherwise interfere
// with focus tests).
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
// workspace auto-install effect inside WorkspacePane doesn't add a tab.
// This keeps tests focused on Summary-tab behaviour.
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
 * Drain microtasks + a macrotask so deferred `setTimeout(..., 0)` dispatches
 * (e.g. default-workspace auto-install) AND the `resolveWorkspaceWidget`
 * promise chain have a chance to resolve before assertions run.
 */
async function flushAsyncWork(): Promise<void> {
  await act(async () => {
    for (let i = 0; i < 4; i++) {
      await Promise.resolve();
    }
    await new Promise(resolve => setTimeout(resolve, 0));
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
// (B7-1) Summary tab MUST NOT install on mount
// ---------------------------------------------------------------------------

describe('WorkspacePane — Summary tab DEFERRED install (B-G9c2 B7)', () => {
  it('does NOT install a Summary tab on mount (no structured-output-stream tab initially)', async () => {
    renderPane();
    await flushAsyncWork();

    // No tab with the structured-output-stream widget exists.
    expect(
      screen.queryByTestId(`widget-stub-${STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE}`),
    ).not.toBeInTheDocument();

    // No tab with the label "Summary" exists either.
    expect(screen.queryByRole('tab', { name: /^summary/i })).not.toBeInTheDocument();

    // With no layout + no pin + no summary, WorkspacePane shows the first-
    // paint placeholder (steady-state empty pane).
    expect(screen.getByTestId('workspace-first-paint')).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// (B7-2) Summary tab installs on first widget_load event
// ---------------------------------------------------------------------------

describe('WorkspacePane — Summary tab installs on widget_load (B-G9c2 B7)', () => {
  it('installs a Summary tab when a `widget_load` for structured-output-stream is dispatched', async () => {
    const { bus } = renderPane();
    await flushAsyncWork();

    // Pre-condition: no Summary tab.
    expect(
      screen.queryByTestId(`widget-stub-${STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE}`),
    ).not.toBeInTheDocument();

    // Dispatch a `widget_load` simulating an in-flight summarize run.
    act(() => {
      bus.dispatch('workspace', {
        type: 'widget_load',
        widgetType: STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE,
        widgetData: {
          mode: 'streaming',
          correlationId: 'stream-1',
          title: 'Summary: contract.pdf',
        },
        displayName: 'Summary: contract.pdf',
      });
    });
    await flushAsyncWork();

    // The Summary tab is present and active.
    const summaryTab = await screen.findByRole('tab', { name: /summary: contract\.pdf/i });
    expect(summaryTab).toBeInTheDocument();
    await waitFor(() => {
      expect(summaryTab.getAttribute('aria-selected')).toBe('true');
    });

    // The widget stub renders inside the active tab.
    expect(
      screen.getByTestId(`widget-stub-${STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE}`),
    ).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// (B8-1) Two consecutive widget_load events create TWO tabs (no replace)
// ---------------------------------------------------------------------------

describe('WorkspacePane — new tab per summarize run (B-G9c2 B8)', () => {
  it('creates two distinct Summary tabs for two consecutive summarize runs', async () => {
    const { bus } = renderPane();
    await flushAsyncWork();

    // Run A — file: contract.pdf, correlationId = stream-a
    act(() => {
      bus.dispatch('workspace', {
        type: 'widget_load',
        widgetType: STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE,
        widgetData: {
          mode: 'streaming',
          correlationId: 'stream-a',
          title: 'Summary: contract.pdf',
        },
        displayName: 'Summary: contract.pdf',
      });
    });
    await flushAsyncWork();

    // Run B — file: brief.docx, correlationId = stream-b
    act(() => {
      bus.dispatch('workspace', {
        type: 'widget_load',
        widgetType: STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE,
        widgetData: {
          mode: 'streaming',
          correlationId: 'stream-b',
          title: 'Summary: brief.docx',
        },
        displayName: 'Summary: brief.docx',
      });
    });
    await flushAsyncWork();

    // BOTH tabs MUST be present — not replaced.
    const tabA = screen.getByRole('tab', { name: /summary: contract\.pdf/i });
    const tabB = screen.getByRole('tab', { name: /summary: brief\.docx/i });
    expect(tabA).toBeInTheDocument();
    expect(tabB).toBeInTheDocument();

    // The most-recently-opened tab (run B) is active (addTab auto-activates).
    await waitFor(() => {
      expect(tabB.getAttribute('aria-selected')).toBe('true');
    });
    expect(tabA.getAttribute('aria-selected')).toBe('false');
  });

  it('uses the dispatched displayName as the tab title (Summary: <filename>)', async () => {
    const { bus } = renderPane();
    await flushAsyncWork();

    act(() => {
      bus.dispatch('workspace', {
        type: 'widget_load',
        widgetType: STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE,
        widgetData: {
          mode: 'streaming',
          correlationId: 'stream-x',
          title: 'Summary: deposition-transcript.pdf',
        },
        displayName: 'Summary: deposition-transcript.pdf',
      });
    });
    await flushAsyncWork();

    const summaryTab = screen.getByRole('tab', {
      name: /summary: deposition-transcript\.pdf/i,
    });
    expect(summaryTab).toBeInTheDocument();
  });
});
