/**
 * ExecutionTraceWidget — unit tests (ADR-040 ledger ToolChain source)
 *
 * ai-architecture-redesign-r1 task 046 / FR-P3-07: the widget renders the
 * session ledger's PERSISTED ToolChain entries delivered via
 * `context.tool_chain` events — it no longer consumes the legacy R6
 * live-telemetry trace events.
 *
 * Covers:
 *  - Empty state renders the "No execution trace yet" hint.
 *  - A tool_chain event renders one row per persisted call with the ledger
 *    projection fields (toolId, argsSummary, resultCount, citationCount,
 *    durationMs) and a turn group header.
 *  - Multiple segments render in arrival order (OLDEST first).
 *  - Legacy live-telemetry trace events (tool_call_started etc.) are IGNORED
 *    — the ledger is the only data source.
 *  - NFR-07 leak guard: extra free-form fields on the event/calls do NOT
 *    render anywhere in the DOM (explicit per-field copy, never a spread).
 *  - FIFO cap (`MAX_TRACE_ENTRIES`) enforced across segments.
 *  - Calls without a toolId are dropped; empty-call events are dropped.
 *  - Session filter drops mismatched-session events (when both sides carry
 *    a session id) and accepts events without one.
 */

import '@testing-library/jest-dom';
import React from 'react';
import { act, render, screen, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { PaneEventBus } from '../../../events/PaneEventBus';
import { PaneEventBusProvider } from '../../../events/PaneEventBusContext';
import ExecutionTraceWidget, { MAX_TRACE_ENTRIES, type ExecutionTraceData } from '../ExecutionTraceWidget';
import {
  recordExecutionTraceEvent,
  clearExecutionTraceBuffer,
  type BufferedTraceEvent,
} from '../executionTraceBuffer';
import type { ContextPaneEvent, TraceToolCallSummary } from '../../../events/PaneEventTypes';
import type { ContextWidgetProps } from '../../../types/widget-types';

// The module-scoped replay buffer persists across tests — every test starts clean.
beforeEach(() => {
  clearExecutionTraceBuffer();
});

// ---------------------------------------------------------------------------
// Mock scrollIntoView (jsdom does not implement it)
// ---------------------------------------------------------------------------

beforeAll(() => {
  if (typeof Element !== 'undefined') {
    (Element.prototype as unknown as { scrollIntoView: () => void }).scrollIntoView = (): void => {};
  }
});

// ---------------------------------------------------------------------------
// Test helpers
// ---------------------------------------------------------------------------

function Wrapper({ bus, children }: { bus: PaneEventBus; children: React.ReactNode }): React.JSX.Element {
  return (
    <PaneEventBusProvider bus={bus}>
      <FluentProvider theme={webLightTheme}>{children}</FluentProvider>
    </PaneEventBusProvider>
  );
}

function renderWidget(
  props: Partial<ContextWidgetProps<ExecutionTraceData>> = {},
  bus: PaneEventBus = new PaneEventBus()
) {
  const finalProps: ContextWidgetProps<ExecutionTraceData> = {
    data: { sessionId: '' },
    widgetType: 'execution-trace',
    isLoading: false,
    ...props,
  };
  const result = render(
    <Wrapper bus={bus}>
      <ExecutionTraceWidget {...finalProps} />
    </Wrapper>
  );
  return { ...result, bus };
}

/** Build a `tool_chain` event carrying persisted ledger calls. */
function makeToolChainEvent(
  calls: ReadonlyArray<Partial<TraceToolCallSummary>>,
  overrides: Partial<ContextPaneEvent> = {}
): ContextPaneEvent {
  return {
    type: 'tool_chain',
    timestamp: '2026-07-06T10:30:15.000Z',
    turn: 1,
    toolChainCalls: calls as ReadonlyArray<TraceToolCallSummary>,
    ...overrides,
  } as ContextPaneEvent;
}

// ---------------------------------------------------------------------------
// Empty state
// ---------------------------------------------------------------------------

describe('ExecutionTraceWidget — empty state', () => {
  it('renders the "No execution trace yet" hint when no events have arrived', () => {
    renderWidget();
    expect(screen.getByText('No execution trace yet')).toBeInTheDocument();
    expect(screen.getByText(/read from the session ledger/i)).toBeInTheDocument();
  });

  it('renders the widget region with the correct accessible name', () => {
    renderWidget();
    expect(screen.getByRole('region', { name: 'Execution trace' })).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// Replay-on-mount (G-P3 UAT round-5 R5-D, 2026-07-07)
// ---------------------------------------------------------------------------

describe('ExecutionTraceWidget — replay-on-mount from the trace buffer (R5-D)', () => {
  it('backfills entries recorded BEFORE the widget mounted (the real-app ordering)', () => {
    // The round-5 defect: tool_chain events fire during the streaming turn while
    // the widget is UNMOUNTED (default Context tool is quick-start) — PaneEventBus
    // drops them and the widget mounted empty forever. The always-mounted bridge
    // now records each event; the widget replays the buffer on mount.
    recordExecutionTraceEvent(
      makeToolChainEvent([
        { toolId: 'dataverse.search_data', resultCount: 3, durationMs: 210 },
        { toolId: 'dataverse.create_record', durationMs: 890 },
      ]) as unknown as BufferedTraceEvent
    );
    recordExecutionTraceEvent(
      makeToolChainEvent([{ toolId: 'SYS-Email_Draft', durationMs: 300 }], {
        turn: 2,
      } as Partial<ContextPaneEvent>) as unknown as BufferedTraceEvent
    );

    renderWidget();

    const rows = screen.getAllByTestId('execution-trace-row');
    expect(rows).toHaveLength(3);
    expect(rows[0]).toHaveAttribute('data-tool-id', 'dataverse.search_data');
    expect(rows[1]).toHaveAttribute('data-tool-id', 'dataverse.create_record');
    expect(rows[2]).toHaveAttribute('data-tool-id', 'SYS-Email_Draft');
    expect(screen.queryByText('No execution trace yet')).not.toBeInTheDocument();
  });

  it('live events after mount append AFTER the replayed entries (no duplication)', () => {
    recordExecutionTraceEvent(
      makeToolChainEvent([{ toolId: 'buffered.tool' }]) as unknown as BufferedTraceEvent
    );

    const bus = new PaneEventBus();
    renderWidget({}, bus);

    act(() => {
      bus.dispatch('context', makeToolChainEvent([{ toolId: 'live.tool' }], { turn: 2 } as Partial<ContextPaneEvent>));
    });

    const rows = screen.getAllByTestId('execution-trace-row');
    expect(rows).toHaveLength(2);
    expect(rows[0]).toHaveAttribute('data-tool-id', 'buffered.tool');
    expect(rows[1]).toHaveAttribute('data-tool-id', 'live.tool');
  });
});

// ---------------------------------------------------------------------------
// Ledger ToolChain rendering
// ---------------------------------------------------------------------------

describe('ExecutionTraceWidget — ledger ToolChain rendering', () => {
  it('renders one row per persisted call with toolId + timestamp', () => {
    const { bus } = renderWidget();
    act(() => {
      bus.dispatch(
        'context',
        makeToolChainEvent([
          { toolId: 'dataverse.read', argsSummary: 'entity=sprk_matter; top=5', resultCount: 5, durationMs: 124 },
          { toolId: 'session.recall', durationMs: 1500 },
        ])
      );
    });
    const rows = screen.getAllByTestId('execution-trace-row');
    expect(rows).toHaveLength(2);
    expect(within(rows[0]).getByText('dataverse.read')).toBeInTheDocument();
    expect(within(rows[0]).getByText('10:30:15')).toBeInTheDocument();
    expect(within(rows[1]).getByText('session.recall')).toBeInTheDocument();
  });

  it('renders the ledger projection detail: argsSummary + result/citation counts + duration', () => {
    const { bus } = renderWidget();
    act(() => {
      bus.dispatch(
        'context',
        makeToolChainEvent([
          {
            toolId: 'knowledge.search',
            argsSummary: 'query=<redacted:42>; top=3',
            resultCount: 3,
            citationCount: 2,
            durationMs: 250,
          },
        ])
      );
    });
    const row = screen.getByTestId('execution-trace-row');
    expect(within(row).getByTestId('execution-trace-args')).toHaveTextContent('query=<redacted:42>; top=3');
    expect(within(row).getByTestId('execution-trace-meta')).toHaveTextContent('3 results · 2 citations · 250 ms');
  });

  it('renders a turn group header carrying the ledger turn ordinal', () => {
    const { bus } = renderWidget();
    act(() => {
      bus.dispatch('context', makeToolChainEvent([{ toolId: 'a.tool' }], { turn: 3 }));
    });
    const turnHeader = screen.getByTestId('execution-trace-turn');
    expect(turnHeader).toHaveAttribute('data-turn', '3');
    expect(within(turnHeader).getByText('Turn 3')).toBeInTheDocument();
  });

  it('renders multiple segments in arrival order (OLDEST first) with per-turn grouping', () => {
    const { bus } = renderWidget();
    act(() => {
      bus.dispatch('context', makeToolChainEvent([{ toolId: 'first.tool' }], { turn: 1 }));
      bus.dispatch('context', makeToolChainEvent([{ toolId: 'second.tool' }, { toolId: 'third.tool' }], { turn: 2 }));
    });
    const rows = screen.getAllByTestId('execution-trace-row');
    expect(rows.map(r => r.getAttribute('data-tool-id'))).toEqual(['first.tool', 'second.tool', 'third.tool']);
    // Two turn headers (turn 1 + turn 2); the third row shares turn 2's header.
    expect(screen.getAllByTestId('execution-trace-turn')).toHaveLength(2);
    expect(screen.getByText(/3 tool calls from the session ledger/)).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// Legacy live-telemetry events are IGNORED (ledger is the only source)
// ---------------------------------------------------------------------------

describe('ExecutionTraceWidget — legacy trace source removed', () => {
  it.each([
    'tool_call_started',
    'tool_call_completed',
    'knowledge_retrieved',
    'playbook_node_executing',
    'playbook_node_completed',
    'decision_made',
    'context_update',
  ] as const)('ignores %s events', eventType => {
    const { bus } = renderWidget();
    act(() => {
      bus.dispatch('context', {
        type: eventType,
        timestamp: '2026-07-06T10:30:15.000Z',
        toolName: 'should-not-render',
        decision: 'should-not-render-either',
      } as ContextPaneEvent);
    });
    expect(screen.queryAllByTestId('execution-trace-row')).toHaveLength(0);
    expect(screen.getByText('No execution trace yet')).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// NFR-07 / ADR-015 leak guard
// ---------------------------------------------------------------------------

describe('ExecutionTraceWidget — NFR-07 leak guard', () => {
  it('does not render unexpected free-form fields smuggled onto the event or calls', () => {
    const { bus } = renderWidget();
    const smuggledEvent = {
      type: 'tool_chain',
      timestamp: '2026-07-06T10:30:15.000Z',
      turn: 1,
      contextData: 'SMUGGLED-USER-CONTENT',
      toolChainCalls: [
        {
          toolId: 'safe.tool',
          rawArguments: 'SMUGGLED-VERBATIM-ARGS',
          resultBody: 'SMUGGLED-RESULT-TEXT',
        },
      ],
    } as unknown as ContextPaneEvent;
    act(() => {
      bus.dispatch('context', smuggledEvent);
    });
    expect(screen.getAllByTestId('execution-trace-row')).toHaveLength(1);
    expect(screen.queryByText(/SMUGGLED/)).not.toBeInTheDocument();
    expect(document.body.textContent).not.toContain('SMUGGLED');
  });
});

// ---------------------------------------------------------------------------
// Defensive handling
// ---------------------------------------------------------------------------

describe('ExecutionTraceWidget — defensive handling', () => {
  it('drops calls without a toolId and events with zero valid calls', () => {
    const { bus } = renderWidget();
    act(() => {
      bus.dispatch('context', makeToolChainEvent([{ argsSummary: 'no-tool-id' } as Partial<TraceToolCallSummary>]));
      bus.dispatch('context', makeToolChainEvent([]));
    });
    expect(screen.queryAllByTestId('execution-trace-row')).toHaveLength(0);
  });

  it('enforces the FIFO cap across segments', () => {
    const { bus } = renderWidget();
    act(() => {
      // Two segments totalling MAX + 5 calls.
      const firstBatch = Array.from({ length: MAX_TRACE_ENTRIES }, (_, i) => ({ toolId: `tool-${i}` }));
      bus.dispatch('context', makeToolChainEvent(firstBatch, { turn: 1 }));
      bus.dispatch(
        'context',
        makeToolChainEvent(
          Array.from({ length: 5 }, (_, i) => ({ toolId: `overflow-${i}` })),
          { turn: 2 }
        )
      );
    });
    const rows = screen.getAllByTestId('execution-trace-row');
    expect(rows).toHaveLength(MAX_TRACE_ENTRIES);
    // Oldest 5 evicted; newest 5 present at the bottom.
    expect(rows[0].getAttribute('data-tool-id')).toBe('tool-5');
    expect(rows[rows.length - 1].getAttribute('data-tool-id')).toBe('overflow-4');
  });

  it('applies the session filter only when both sides carry a session id', () => {
    const { bus } = renderWidget({ data: { sessionId: 'session-A' } });
    act(() => {
      // Mismatched session — dropped.
      bus.dispatch('context', makeToolChainEvent([{ toolId: 'other.session' }], { sessionId: 'session-B' }));
      // No session id on the event — accepted (transport is per-session).
      bus.dispatch('context', makeToolChainEvent([{ toolId: 'no.session.id' }]));
      // Matching session — accepted.
      bus.dispatch('context', makeToolChainEvent([{ toolId: 'same.session' }], { sessionId: 'session-A' }));
    });
    const rows = screen.getAllByTestId('execution-trace-row');
    expect(rows.map(r => r.getAttribute('data-tool-id'))).toEqual(['no.session.id', 'same.session']);
  });

  it('honours isLoading with a spinner state', () => {
    renderWidget({ isLoading: true });
    expect(screen.getByRole('status', { name: 'Loading' })).toBeInTheDocument();
    expect(screen.queryByText('No execution trace yet')).not.toBeInTheDocument();
  });
});
