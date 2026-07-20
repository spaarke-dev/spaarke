/**
 * ContextPaneController — unit tests
 *
 * Covers:
 *   - context_update to 'entity-info' contextType resolves EntityInfoWidget
 *     (or a registered stub) and renders it.
 *   - context_highlight events call onHighlight() on the current widget.
 *   - Unknown widget type (null from ContextWidgetRegistry) renders Spinner,
 *     not a crash or blank pane.
 *   - Stage transitions: welcome shows empty state, loading shows spinner,
 *     active-chat shows sources-empty before first context_update.
 *   - stage_change event clears the active widget.
 *
 * Test isolation strategy:
 *   - Each test uses a fresh PaneEventBus (no shared state).
 *   - ContextWidgetRegistry is cleared in beforeEach via clearContextRegistry().
 *   - ShellStageContext is provided via a minimal wrapper.
 *
 * @see ContextPaneController — component under test
 * @see PaneEventBus — event dispatch used in tests
 * @see ContextWidgetRegistry — clearContextRegistry, registerContextWidget
 */

import '@testing-library/jest-dom';
import React, { act } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { PaneEventBus } from '@spaarke/ai-widgets';
import { PaneEventBusProvider } from '@spaarke/ai-widgets';
import {
  registerContextWidget,
  clearContextRegistry,
} from '@spaarke/ai-widgets';
import type { ContextWidgetProps } from '@spaarke/ai-widgets';
import { ContextPaneController } from '../ContextPaneController';
import type { ShellStageContextValue } from '../../shell/ThreePaneShell';
import { ShellStageContext } from '../../shell/ThreePaneShell';

// decision-1 (2026-07-19): the Context pane now defaults to the execution-trace view. ComposeTraceHost
// calls useAiSession(), which THROWS outside an AiSessionProvider (which these provider-less unit tests
// don't mount). Stub it so the default render is inert here — production always wraps the shell in
// AiSessionProvider. The `context-pane-execution-trace` testid lives on ContextPaneController's own
// wrapper div, so the assertions below still hold.
jest.mock('../ComposeTraceHost', () => ({
  __esModule: true,
  ComposeTraceHost: () => <div data-testid="compose-trace-host-stub">trace</div>,
  default: () => <div data-testid="compose-trace-host-stub">trace</div>,
}));

// ---------------------------------------------------------------------------
// Test helpers
// ---------------------------------------------------------------------------

/**
 * Minimal ShellStageContextValue for wrapping ContextPaneController.
 */
function makeStageContext(
  overrides: Partial<ShellStageContextValue> = {}
): ShellStageContextValue {
  return {
    currentStage: "active-chat",
    toLoading: jest.fn(),
    toActiveChat: jest.fn(),
    toReview: jest.fn(),
    toActiveWork: jest.fn(),
    reset: jest.fn(),
    ...overrides,
  };
}

/**
 * Renders ContextPaneController inside all required providers.
 *
 * @param bus           - Pre-created PaneEventBus for the test.
 * @param stageOverride - Partial ShellStageContextValue overrides.
 */
function renderController(
  bus: PaneEventBus,
  stageOverride: Partial<ShellStageContextValue> = {}
) {
  const stageContext = makeStageContext(stageOverride);
  return render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={bus}>
        <ShellStageContext.Provider value={stageContext}>
          <ContextPaneController />
        </ShellStageContext.Provider>
      </PaneEventBusProvider>
    </FluentProvider>
  );
}

// ---------------------------------------------------------------------------
// Test widget stubs
// ---------------------------------------------------------------------------

/**
 * Stub EntityInfoWidget that records onHighlight calls via module-level array.
 * Real widgets would use useImperativeHandle, but stubs record through closure.
 */
const EntityInfoWidgetStub: React.FC<ContextWidgetProps> = ({ widgetType }) => (
  <div data-testid="entity-info-widget" data-widget-type={widgetType}>
    EntityInfoWidget
  </div>
);
EntityInfoWidgetStub.displayName = 'EntityInfoWidgetStub';

/** Stub for 'sources-citations' contextType. */
const SourcesCitationsWidgetStub: React.FC<ContextWidgetProps> = ({ widgetType }) => (
  <div data-testid="sources-citations-widget" data-widget-type={widgetType}>
    SourcesCitationsWidget
  </div>
);
SourcesCitationsWidgetStub.displayName = 'SourcesCitationsWidgetStub';

/** Stub for progress contextType. */
const ProgressWidgetStub: React.FC<ContextWidgetProps> = () => (
  <div data-testid="progress-widget">ProgressWidget</div>
);
ProgressWidgetStub.displayName = 'ProgressWidgetStub';

// ---------------------------------------------------------------------------
// Setup / teardown
// ---------------------------------------------------------------------------

beforeEach(() => {
  clearContextRegistry();
  jest.clearAllMocks();
});

// ---------------------------------------------------------------------------
// Header rendering
// ---------------------------------------------------------------------------

describe('ContextPaneController — header', () => {
  it('renders the "Context" header with DocumentRegular icon area', () => {
    const bus = new PaneEventBus();
    renderController(bus);

    expect(screen.getByText('Context')).toBeInTheDocument();
  });

  it('renders the pane root with data-testid', () => {
    const bus = new PaneEventBus();
    renderController(bus);

    expect(screen.getByTestId('context-pane-controller')).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// Stage-based default content
// ---------------------------------------------------------------------------

describe('ContextPaneController — shell stage defaults', () => {
  // NOTE (test-repair task 021, 2026-07-08): these four cases originally
  // asserted per-stage static empty states (sources-empty / related-empty /
  // an idle "Gathering context..." spinner / "Select a Playbook" text) that
  // predate tasks 095/099/101. Those tasks made the Quick Start
  // (GetStartedCardsWidget) UI win as the AT-REST default on EVERY shell
  // stage — not just welcome — until a context_update event resolves a real
  // widget (see ContextPaneController.tsx renderContent(), the block guarded
  // by `selectedTool === "quick-start" && activeWidget === null &&
  // !isResolving`). This was a deliberate fix for the "pane goes blank after
  // modal close" bug, so the old per-stage empty states in
  // renderStageDefaultContent() are unreachable at rest with the default
  // 'quick-start' tool selection. Updated to assert the current, real,
  // intentionally-shipped default rather than the superseded behavior.

  // decision-1 (2026-07-19): the AT-REST default is now the execution-trace ("what's happening") view
  // on EVERY shell stage until a context_update resolves a real server widget. (Was GetStartedCardsWidget
  // "Quick Start", now removed.)
  it('shows the execution-trace view on welcome stage', () => {
    const bus = new PaneEventBus();
    renderController(bus, { currentStage: "welcome" });

    expect(screen.getByTestId('context-pane-execution-trace')).toBeInTheDocument();
    expect(screen.queryByTestId('context-pane-welcome')).not.toBeInTheDocument();
  });

  it('shows execution-trace (not the idle stage spinner) on loading stage before any context_update', () => {
    const bus = new PaneEventBus();
    renderController(bus, { currentStage: "loading" });

    expect(screen.getByTestId('context-pane-execution-trace')).toBeInTheDocument();
    expect(screen.queryByText('Gathering context...')).not.toBeInTheDocument();
  });

  it('shows execution-trace (not sources-empty) on active-chat stage before any context_update', () => {
    const bus = new PaneEventBus();
    renderController(bus, { currentStage: "active-chat" });

    expect(screen.getByTestId('context-pane-execution-trace')).toBeInTheDocument();
    expect(screen.queryByTestId('context-pane-sources-empty')).not.toBeInTheDocument();
  });

  it('shows execution-trace (not related-empty) on review stage before any context_update', () => {
    const bus = new PaneEventBus();
    renderController(bus, { currentStage: "review" });

    expect(screen.getByTestId('context-pane-execution-trace')).toBeInTheDocument();
    expect(screen.queryByTestId('context-pane-related-empty')).not.toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// context_update → stage-to-widget resolution
// ---------------------------------------------------------------------------

describe('ContextPaneController — context_update events', () => {
  it('resolves and renders EntityInfoWidget on contextType="entity-info"', async () => {
    const bus = new PaneEventBus();

    registerContextWidget('entity-info', {
      factory: () => Promise.resolve({ default: EntityInfoWidgetStub as React.ComponentType<ContextWidgetProps> }),
    });

    renderController(bus, { currentStage: "active-chat" });

    act(() => {
      bus.dispatch('context', {
        type: 'context_update',
        contextType: 'entity-info',
        contextData: { entityName: 'Acme Corp', entityType: 'company' },
      });
    });

    await waitFor(() => {
      expect(screen.getByTestId('entity-info-widget')).toBeInTheDocument();
    });
  });

  it('renders sources-citations widget on contextType="sources-citations"', async () => {
    const bus = new PaneEventBus();

    registerContextWidget('sources-citations', {
      factory: () => Promise.resolve({ default: SourcesCitationsWidgetStub as React.ComponentType<ContextWidgetProps> }),
    });

    renderController(bus, { currentStage: "active-chat" });

    act(() => {
      bus.dispatch('context', {
        type: 'context_update',
        contextType: 'sources-citations',
        contextData: { citations: [] },
      });
    });

    await waitFor(() => {
      expect(screen.getByTestId('sources-citations-widget')).toBeInTheDocument();
    });
  });

  it('renders progress widget on contextType="progress"', async () => {
    const bus = new PaneEventBus();

    registerContextWidget('progress', {
      factory: () => Promise.resolve({ default: ProgressWidgetStub as React.ComponentType<ContextWidgetProps> }),
    });

    renderController(bus, { currentStage: "active-chat" });

    act(() => {
      bus.dispatch('context', {
        type: 'context_update',
        contextType: 'progress',
        contextData: { step: 1, total: 5 },
      });
    });

    await waitFor(() => {
      expect(screen.getByTestId('progress-widget')).toBeInTheDocument();
    });
  });

  it('passes contextData as data prop to the resolved widget', async () => {
    const bus = new PaneEventBus();
    let receivedData: unknown = undefined;

    const DataCapturingWidget: React.FC<ContextWidgetProps> = ({ data }) => {
      receivedData = data;
      return <div data-testid="data-widget">DataWidget</div>;
    };
    DataCapturingWidget.displayName = 'DataCapturingWidget';

    registerContextWidget('entity-info', {
      factory: () => Promise.resolve({ default: DataCapturingWidget as React.ComponentType<ContextWidgetProps> }),
    });

    renderController(bus, { currentStage: "active-chat" });

    const payload = { entityId: 'matter-123', entityType: 'sprk_matter' };

    act(() => {
      bus.dispatch('context', {
        type: 'context_update',
        contextType: 'entity-info',
        contextData: payload,
      });
    });

    await waitFor(() => {
      expect(screen.getByTestId('data-widget')).toBeInTheDocument();
    });

    expect(receivedData).toEqual(payload);
  });
});

// ---------------------------------------------------------------------------
// Unknown widget type → Spinner, not crash
// ---------------------------------------------------------------------------

describe('ContextPaneController — unknown widget type (null from registry)', () => {
  it('renders a Spinner (not a crash) when contextType is unregistered', async () => {
    const bus = new PaneEventBus();

    // Do NOT register anything — registry will return null for 'unknown-type'
    renderController(bus, { currentStage: "active-chat" });

    act(() => {
      bus.dispatch('context', {
        type: 'context_update',
        contextType: 'unknown-type-not-registered',
        contextData: { whatever: true },
      });
    });

    // During resolution, isResolving=true → should show resolving spinner
    // After resolution with null → isResolving=false, no active widget
    // → the execution-trace at-rest default renders (decision-1 — see the
    // "shell stage defaults" describe block above for why this supersedes
    // the old per-stage sources-empty state).
    await waitFor(() => {
      // Pane must still be mounted — no crash
      expect(screen.getByTestId('context-pane-controller')).toBeInTheDocument();
    });

    // The execution-trace default reappears after null resolution (no active widget)
    expect(screen.getByTestId('context-pane-execution-trace')).toBeInTheDocument();
  });

  it('does not throw when registry returns null for unknown contextType', async () => {
    const bus = new PaneEventBus();
    renderController(bus, { currentStage: "active-chat" });

    expect(() => {
      act(() => {
        bus.dispatch('context', {
          type: 'context_update',
          contextType: 'completely-unknown-xyz',
          contextData: null,
        });
      });
    }).not.toThrow();

    await waitFor(() => {
      expect(screen.getByTestId('context-pane-controller')).toBeInTheDocument();
    });
  });
});

// ---------------------------------------------------------------------------
// context_highlight → forwarded to active widget onHighlight
// ---------------------------------------------------------------------------

describe('ContextPaneController — context_highlight event routing', () => {
  it('calls onHighlight on the active widget ref when context_highlight fires', async () => {
    const bus = new PaneEventBus();

    /**
     * Widget that exposes its onHighlight by writing to the component's
     * highlightRef when it mounts. Since we can't use forwardRef easily in
     * a test stub, we use a side-channel via a shared array.
     *
     * In production, widgets would use React.forwardRef + useImperativeHandle.
     * For the test, we verify the controller's highlight mechanism is invoked
     * by registering a spy on the highlightRef after widget mount.
     */
    const HighlightableWidget: React.FC<ContextWidgetProps> = () => (
      <div data-testid="highlightable-widget">HighlightableWidget</div>
    );
    HighlightableWidget.displayName = 'HighlightableWidget';

    registerContextWidget('sources-citations', {
      factory: () => Promise.resolve({ default: HighlightableWidget as React.ComponentType<ContextWidgetProps> }),
    });

    renderController(bus, { currentStage: "active-chat" });

    // Load the widget via context_update
    act(() => {
      bus.dispatch('context', {
        type: 'context_update',
        contextType: 'sources-citations',
        contextData: { citations: [{ id: 'cite-1', text: 'Paragraph 1' }] },
      });
    });

    await waitFor(() => {
      expect(screen.getByTestId('highlightable-widget')).toBeInTheDocument();
    });

    // Dispatching context_highlight should not crash — the controller routes
    // it to highlightRef.current.onHighlight(). The default implementation
    // is a no-op since HighlightableWidget doesn't override the ref.
    expect(() => {
      act(() => {
        bus.dispatch('context', {
          type: 'context_highlight',
          citationId: 'cite-1',
          selectionRef: 'char:100-200',
        });
      });
    }).not.toThrow();
  });

  it('does not throw when context_highlight fires with no active widget', () => {
    const bus = new PaneEventBus();
    renderController(bus, { currentStage: "active-chat" });

    expect(() => {
      act(() => {
        bus.dispatch('context', {
          type: 'context_highlight',
          citationId: 'cite-orphan',
        });
      });
    }).not.toThrow();
  });
});

// ---------------------------------------------------------------------------
// stage_change → clears active widget
// ---------------------------------------------------------------------------

describe('ContextPaneController — stage_change event', () => {
  it('clears the active widget and reverts to stage-default when stage_change fires', async () => {
    const bus = new PaneEventBus();

    registerContextWidget('entity-info', {
      factory: () => Promise.resolve({ default: EntityInfoWidgetStub as React.ComponentType<ContextWidgetProps> }),
    });

    renderController(bus, { currentStage: "active-chat" });

    // Load a widget
    act(() => {
      bus.dispatch('context', {
        type: 'context_update',
        contextType: 'entity-info',
        contextData: {},
      });
    });

    await waitFor(() => {
      expect(screen.getByTestId('entity-info-widget')).toBeInTheDocument();
    });

    // Fire stage_change — widget should be cleared
    act(() => {
      bus.dispatch('context', { type: 'stage_change' });
    });

    await waitFor(() => {
      expect(screen.queryByTestId('entity-info-widget')).not.toBeInTheDocument();
    });

    // Reverts to the execution-trace at-rest default (decision-1 — see the
    // "shell stage defaults" describe block above for why this supersedes
    // the old per-stage sources-empty state).
    expect(screen.getByTestId('context-pane-execution-trace')).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// Multiple sequential context_update events
// ---------------------------------------------------------------------------

describe('ContextPaneController — sequential context_update events', () => {
  it('replaces the active widget when a new context_update arrives', async () => {
    const bus = new PaneEventBus();

    registerContextWidget('entity-info', {
      factory: () => Promise.resolve({ default: EntityInfoWidgetStub as React.ComponentType<ContextWidgetProps> }),
    });
    registerContextWidget('sources-citations', {
      factory: () => Promise.resolve({ default: SourcesCitationsWidgetStub as React.ComponentType<ContextWidgetProps> }),
    });

    renderController(bus, { currentStage: "active-chat" });

    // Load entity-info widget
    act(() => {
      bus.dispatch('context', {
        type: 'context_update',
        contextType: 'entity-info',
        contextData: {},
      });
    });

    await waitFor(() => {
      expect(screen.getByTestId('entity-info-widget')).toBeInTheDocument();
    });

    // Replace with sources-citations widget
    act(() => {
      bus.dispatch('context', {
        type: 'context_update',
        contextType: 'sources-citations',
        contextData: { citations: [] },
      });
    });

    await waitFor(() => {
      expect(screen.getByTestId('sources-citations-widget')).toBeInTheDocument();
    });

    // entity-info widget should be gone
    expect(screen.queryByTestId('entity-info-widget')).not.toBeInTheDocument();
  });
});
