/**
 * SSE Event Routing Tests
 *
 * Tests the AiSessionProvider's `routePaneEvent` function for each SSE pane
 * event type. Verifies correct channel routing through the PaneEventBus:
 *
 *   output_pane      -> workspace channel (widget_load)
 *   source_pane      -> context channel   (context_update)
 *   source_highlight -> context channel   (context_highlight)
 *   unknown          -> dropped gracefully (no crash, no dispatch)
 *
 * Also validates that:
 *   - Field mapping is correct (widgetType -> widgetType, payload -> widgetData, etc.)
 *   - Multiple events dispatch independently
 *   - Pane events do not leak across channels
 *
 * @see AiSessionProvider.tsx — routePaneEvent implementation
 * @see PaneEventTypes.ts — channel and event type definitions
 * @see useSseStream.ts — parsePaneEvent() that feeds these events
 */

import '@testing-library/jest-dom';
import React, { act } from 'react';
import { renderHook } from '@testing-library/react';

import { PaneEventBus } from '../events/PaneEventBus';
import { PaneEventBusProvider } from '../events/PaneEventBusContext';
import { AiSessionProvider } from '../providers/AiSessionProvider';
import { useAiSession } from '../providers/useAiSession';
import type {
  WorkspacePaneEvent,
  ContextPaneEvent,
  SafetyPaneEvent,
  ConversationPaneEvent,
} from '../events/PaneEventTypes';
import type { AiPaneEvent } from '@spaarke/ai-context';

// ---------------------------------------------------------------------------
// Mock @spaarke/auth — AiSessionProvider imports buildBffApiUrl + useAuth and
// renders nothing token-related itself; pane routing tests never trigger the
// BFF fetch (entityContext is null), so a static authenticated stub suffices.
// ---------------------------------------------------------------------------

jest.mock('@spaarke/auth', () => {
  const stubFetch = jest.fn().mockResolvedValue({
    ok: false,
    status: 404,
    json: async () => ({}),
  });
  return {
    buildBffApiUrl: (base: string, path: string) => `${base}${path}`,
    authenticatedFetch: stubFetch,
    useAuth: jest.fn(() => ({
      isAuthenticated: true,
      getAccessToken: jest.fn().mockResolvedValue('test-token'),
      authenticatedFetch: stubFetch,
      tenantId: 'tenant-guid-from-mock',
      logout: jest.fn(),
    })),
  };
});

// ---------------------------------------------------------------------------
// Test helpers
// ---------------------------------------------------------------------------

/**
 * Creates a test wrapper with PaneEventBusProvider + AiSessionProvider.
 * Uses the provided bus instance for inspection.
 *
 * Auth is supplied via the mocked useAuth() inside AiSessionProvider — the
 * provider no longer takes token / isAuthenticated as props (Spaarke Auth v2
 * function-based contract; see AUDIT-FINDINGS-AUTH-SYSTEM §H-4).
 */
function makeWrapper(bus: PaneEventBus) {
  return function Wrapper({ children }: { children: React.ReactNode }): React.JSX.Element {
    return (
      <PaneEventBusProvider bus={bus}>
        <AiSessionProvider
          bffBaseUrl="https://test-bff.example.com"
          entityContext={null}
        >
          {children}
        </AiSessionProvider>
      </PaneEventBusProvider>
    );
  };
}

/**
 * Subscribes to all four PaneEventBus channels and returns arrays
 * that collect all events dispatched to each channel.
 */
function subscribeAll(bus: PaneEventBus) {
  const workspace: WorkspacePaneEvent[] = [];
  const context: ContextPaneEvent[] = [];
  const conversation: ConversationPaneEvent[] = [];
  const safety: SafetyPaneEvent[] = [];

  bus.subscribe('workspace', (e) => workspace.push(e));
  bus.subscribe('context', (e) => context.push(e));
  bus.subscribe('conversation', (e) => conversation.push(e));
  bus.subscribe('safety', (e) => safety.push(e));

  return { workspace, context, conversation, safety };
}

// ---------------------------------------------------------------------------
// output_pane -> workspace channel
// ---------------------------------------------------------------------------

describe('SSE pane event routing: output_pane -> workspace', () => {
  it('routes output_pane to workspace channel as widget_load', () => {
    const bus = new PaneEventBus();
    const channels = subscribeAll(bus);

    const { result } = renderHook(() => useAiSession(), { wrapper: makeWrapper(bus) });

    const sseEvent: AiPaneEvent = {
      event: 'output_pane',
      widgetType: 'AnalysisEditor',
      payload: { documentId: 'doc-1', content: '<html>analysis</html>' },
    };

    act(() => {
      result.current.streaming.onPaneEvent!(sseEvent);
    });

    expect(channels.workspace).toHaveLength(1);
    expect(channels.workspace[0].type).toBe('widget_load');
    expect(channels.workspace[0].widgetType).toBe('AnalysisEditor');
    expect(channels.workspace[0].widgetData).toEqual({
      documentId: 'doc-1',
      content: '<html>analysis</html>',
    });

    // Must not leak to other channels
    expect(channels.context).toHaveLength(0);
    expect(channels.conversation).toHaveLength(0);
    expect(channels.safety).toHaveLength(0);
  });

  it('maps widgetType and payload correctly for different widget types', () => {
    const bus = new PaneEventBus();
    const channels = subscribeAll(bus);

    const { result } = renderHook(() => useAiSession(), { wrapper: makeWrapper(bus) });

    const widgets = [
      { widgetType: 'clause-list', payload: { clauses: ['c1', 'c2'] } },
      { widgetType: 'timeline', payload: { events: [{ date: '2026-01-01' }] } },
      { widgetType: 'document-diff', payload: { diffCount: 5 } },
    ];

    act(() => {
      for (const w of widgets) {
        result.current.streaming.onPaneEvent!({
          event: 'output_pane',
          widgetType: w.widgetType,
          payload: w.payload,
        });
      }
    });

    expect(channels.workspace).toHaveLength(3);
    expect(channels.workspace[0].widgetType).toBe('clause-list');
    expect(channels.workspace[0].widgetData).toEqual({ clauses: ['c1', 'c2'] });
    expect(channels.workspace[1].widgetType).toBe('timeline');
    expect(channels.workspace[2].widgetType).toBe('document-diff');
  });

  it('handles output_pane with undefined payload gracefully', () => {
    const bus = new PaneEventBus();
    const channels = subscribeAll(bus);

    const { result } = renderHook(() => useAiSession(), { wrapper: makeWrapper(bus) });

    act(() => {
      result.current.streaming.onPaneEvent!({
        event: 'output_pane',
        widgetType: 'empty-widget',
        // payload intentionally omitted
      });
    });

    expect(channels.workspace).toHaveLength(1);
    expect(channels.workspace[0].widgetType).toBe('empty-widget');
    expect(channels.workspace[0].widgetData).toBeUndefined();
  });
});

// ---------------------------------------------------------------------------
// source_pane -> context channel
// ---------------------------------------------------------------------------

describe('SSE pane event routing: source_pane -> context', () => {
  it('routes source_pane to context channel as context_update', () => {
    const bus = new PaneEventBus();
    const channels = subscribeAll(bus);

    const { result } = renderHook(() => useAiSession(), { wrapper: makeWrapper(bus) });

    const sseEvent: AiPaneEvent = {
      event: 'source_pane',
      widgetType: 'DocumentViewer',
      payload: { documentId: 'doc-42', documentName: 'Agreement.pdf' },
    };

    act(() => {
      result.current.streaming.onPaneEvent!(sseEvent);
    });

    expect(channels.context).toHaveLength(1);
    expect(channels.context[0].type).toBe('context_update');
    expect(channels.context[0].contextType).toBe('DocumentViewer');
    expect(channels.context[0].contextData).toEqual({
      documentId: 'doc-42',
      documentName: 'Agreement.pdf',
    });

    // Must not leak to workspace or other channels
    expect(channels.workspace).toHaveLength(0);
    expect(channels.conversation).toHaveLength(0);
    expect(channels.safety).toHaveLength(0);
  });

  it('maps widgetType to contextType for different source types', () => {
    const bus = new PaneEventBus();
    const channels = subscribeAll(bus);

    const { result } = renderHook(() => useAiSession(), { wrapper: makeWrapper(bus) });

    act(() => {
      result.current.streaming.onPaneEvent!({
        event: 'source_pane',
        widgetType: 'pdf_viewer',
        payload: { url: 'https://example.com/doc.pdf' },
      });
      result.current.streaming.onPaneEvent!({
        event: 'source_pane',
        widgetType: 'web_reference',
        payload: { url: 'https://example.com/article' },
      });
    });

    expect(channels.context).toHaveLength(2);
    expect(channels.context[0].contextType).toBe('pdf_viewer');
    expect(channels.context[1].contextType).toBe('web_reference');
  });
});

// ---------------------------------------------------------------------------
// source_highlight -> context channel
// ---------------------------------------------------------------------------

describe('SSE pane event routing: source_highlight -> context', () => {
  it('routes source_highlight to context channel as context_highlight', () => {
    const bus = new PaneEventBus();
    const channels = subscribeAll(bus);

    const { result } = renderHook(() => useAiSession(), { wrapper: makeWrapper(bus) });

    const sseEvent: AiPaneEvent = {
      event: 'source_highlight',
      sourceRef: 'citation-7',
      selectionRef: 'char:1024-1200',
    };

    act(() => {
      result.current.streaming.onPaneEvent!(sseEvent);
    });

    expect(channels.context).toHaveLength(1);
    expect(channels.context[0].type).toBe('context_highlight');
    expect(channels.context[0].citationId).toBe('citation-7');
    expect(channels.context[0].selectionRef).toBe('char:1024-1200');

    // Must not leak to workspace
    expect(channels.workspace).toHaveLength(0);
  });

  it('handles source_highlight with undefined selectionRef', () => {
    const bus = new PaneEventBus();
    const channels = subscribeAll(bus);

    const { result } = renderHook(() => useAiSession(), { wrapper: makeWrapper(bus) });

    act(() => {
      result.current.streaming.onPaneEvent!({
        event: 'source_highlight',
        sourceRef: 'cit-99',
        // selectionRef intentionally omitted
      });
    });

    expect(channels.context).toHaveLength(1);
    expect(channels.context[0].citationId).toBe('cit-99');
    expect(channels.context[0].selectionRef).toBeUndefined();
  });
});

// ---------------------------------------------------------------------------
// Unknown event types — dropped gracefully
// ---------------------------------------------------------------------------

describe('SSE pane event routing: unknown events', () => {
  it('does not throw when receiving an unknown event type', () => {
    const bus = new PaneEventBus();
    const channels = subscribeAll(bus);

    const { result } = renderHook(() => useAiSession(), { wrapper: makeWrapper(bus) });

    const unknownEvent = { event: 'future_event_type' as AiPaneEvent['event'] };

    expect(() => {
      act(() => {
        result.current.streaming.onPaneEvent!(unknownEvent as AiPaneEvent);
      });
    }).not.toThrow();

    // Nothing dispatched to any channel
    expect(channels.workspace).toHaveLength(0);
    expect(channels.context).toHaveLength(0);
    expect(channels.conversation).toHaveLength(0);
    expect(channels.safety).toHaveLength(0);
  });

  it('logs a warning for unknown event types', () => {
    const bus = new PaneEventBus();
    const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});

    const { result } = renderHook(() => useAiSession(), { wrapper: makeWrapper(bus) });

    act(() => {
      result.current.streaming.onPaneEvent!({
        event: 'not_a_real_event' as AiPaneEvent['event'],
      } as AiPaneEvent);
    });

    expect(warnSpy).toHaveBeenCalledWith(
      expect.stringContaining('Unknown SSE pane event type')
    );

    warnSpy.mockRestore();
  });

  it('drops safety_annotation event (routed via separate path, not AiPaneEvent union)', () => {
    const bus = new PaneEventBus();
    const channels = subscribeAll(bus);

    const { result } = renderHook(() => useAiSession(), { wrapper: makeWrapper(bus) });

    // safety_annotation is NOT in the AiPaneEvent union — it hits the default branch
    const safetyEvent = { event: 'safety_annotation' as AiPaneEvent['event'] };

    act(() => {
      result.current.streaming.onPaneEvent!(safetyEvent as AiPaneEvent);
    });

    // Dropped — not routed to safety channel via this path
    expect(channels.safety).toHaveLength(0);
    expect(channels.workspace).toHaveLength(0);
    expect(channels.context).toHaveLength(0);
  });
});

// ---------------------------------------------------------------------------
// Cross-channel isolation
// ---------------------------------------------------------------------------

describe('SSE pane event routing: cross-channel isolation', () => {
  it('multiple event types in sequence route to correct channels independently', () => {
    const bus = new PaneEventBus();
    const channels = subscribeAll(bus);

    const { result } = renderHook(() => useAiSession(), { wrapper: makeWrapper(bus) });

    act(() => {
      // First: workspace widget
      result.current.streaming.onPaneEvent!({
        event: 'output_pane',
        widgetType: 'table',
        payload: { rows: 10 },
      });

      // Second: source document
      result.current.streaming.onPaneEvent!({
        event: 'source_pane',
        widgetType: 'pdf_viewer',
        payload: { documentId: 'doc-7' },
      });

      // Third: citation highlight
      result.current.streaming.onPaneEvent!({
        event: 'source_highlight',
        sourceRef: 'cit-3',
        selectionRef: 'char:200-300',
      });

      // Fourth: another workspace widget
      result.current.streaming.onPaneEvent!({
        event: 'output_pane',
        widgetType: 'chart',
        payload: { chartType: 'bar' },
      });
    });

    // workspace: 2 events (output_pane dispatches)
    expect(channels.workspace).toHaveLength(2);
    expect(channels.workspace[0].widgetType).toBe('table');
    expect(channels.workspace[1].widgetType).toBe('chart');

    // context: 2 events (source_pane + source_highlight)
    expect(channels.context).toHaveLength(2);
    expect(channels.context[0].type).toBe('context_update');
    expect(channels.context[1].type).toBe('context_highlight');

    // conversation and safety: untouched
    expect(channels.conversation).toHaveLength(0);
    expect(channels.safety).toHaveLength(0);
  });

  it('multi-subscriber independence: two workspace subscribers receive all events', () => {
    const bus = new PaneEventBus();

    const subscriberA: WorkspacePaneEvent[] = [];
    const subscriberB: WorkspacePaneEvent[] = [];

    bus.subscribe('workspace', (e) => subscriberA.push(e));
    bus.subscribe('workspace', (e) => subscriberB.push(e));

    const { result } = renderHook(() => useAiSession(), { wrapper: makeWrapper(bus) });

    act(() => {
      result.current.streaming.onPaneEvent!({
        event: 'output_pane',
        widgetType: 'findings',
        payload: { count: 3 },
      });
      result.current.streaming.onPaneEvent!({
        event: 'output_pane',
        widgetType: 'summary',
        payload: { text: 'Overview' },
      });
    });

    // Both subscribers receive both events
    expect(subscriberA).toHaveLength(2);
    expect(subscriberB).toHaveLength(2);
    expect(subscriberA[0].widgetType).toBe('findings');
    expect(subscriberA[1].widgetType).toBe('summary');
    expect(subscriberB[0].widgetType).toBe('findings');
    expect(subscriberB[1].widgetType).toBe('summary');
  });

  it('unsubscribing one context subscriber does not affect the other', () => {
    const bus = new PaneEventBus();

    const subscriberA: ContextPaneEvent[] = [];
    const subscriberB: ContextPaneEvent[] = [];

    const unsubA = bus.subscribe('context', (e) => subscriberA.push(e));
    bus.subscribe('context', (e) => subscriberB.push(e));

    const { result } = renderHook(() => useAiSession(), { wrapper: makeWrapper(bus) });

    // First event: both receive
    act(() => {
      result.current.streaming.onPaneEvent!({
        event: 'source_pane',
        widgetType: 'doc',
        payload: { id: '1' },
      });
    });

    expect(subscriberA).toHaveLength(1);
    expect(subscriberB).toHaveLength(1);

    // Unsubscribe A
    unsubA();

    // Second event: only B receives
    act(() => {
      result.current.streaming.onPaneEvent!({
        event: 'source_pane',
        widgetType: 'doc',
        payload: { id: '2' },
      });
    });

    expect(subscriberA).toHaveLength(1); // unchanged
    expect(subscriberB).toHaveLength(2); // received second event
  });
});

// ---------------------------------------------------------------------------
// Edge cases
// ---------------------------------------------------------------------------

describe('SSE pane event routing: edge cases', () => {
  it('handles rapid sequential events without loss', () => {
    const bus = new PaneEventBus();
    const channels = subscribeAll(bus);

    const { result } = renderHook(() => useAiSession(), { wrapper: makeWrapper(bus) });

    act(() => {
      for (let i = 0; i < 50; i++) {
        result.current.streaming.onPaneEvent!({
          event: 'output_pane',
          widgetType: `widget-${i}`,
          payload: { index: i },
        });
      }
    });

    expect(channels.workspace).toHaveLength(50);
    expect(channels.workspace[49].widgetType).toBe('widget-49');
  });

  it('handles interleaved known and unknown events', () => {
    const bus = new PaneEventBus();
    const channels = subscribeAll(bus);
    const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});

    const { result } = renderHook(() => useAiSession(), { wrapper: makeWrapper(bus) });

    act(() => {
      result.current.streaming.onPaneEvent!({
        event: 'output_pane',
        widgetType: 'w1',
        payload: {},
      });
      result.current.streaming.onPaneEvent!({
        event: 'unknown_v2' as AiPaneEvent['event'],
      } as AiPaneEvent);
      result.current.streaming.onPaneEvent!({
        event: 'source_pane',
        widgetType: 'doc',
        payload: {},
      });
    });

    // Known events routed correctly despite interleaved unknown
    expect(channels.workspace).toHaveLength(1);
    expect(channels.context).toHaveLength(1);
    expect(warnSpy).toHaveBeenCalledTimes(1);

    warnSpy.mockRestore();
  });

  it('empty bus (no subscribers) does not throw on dispatch', () => {
    const bus = new PaneEventBus();
    // No subscribers — bus is empty

    const { result } = renderHook(() => useAiSession(), { wrapper: makeWrapper(bus) });

    expect(() => {
      act(() => {
        result.current.streaming.onPaneEvent!({
          event: 'output_pane',
          widgetType: 'orphan',
          payload: {},
        });
      });
    }).not.toThrow();
  });
});
