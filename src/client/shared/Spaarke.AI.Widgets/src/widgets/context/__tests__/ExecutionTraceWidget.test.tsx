/**
 * ExecutionTraceWidget — unit tests
 *
 * Covers:
 *  - Empty state renders the "No execution trace yet" hint when no events
 *    have arrived.
 *  - A single dispatched event is rendered as a single row with the typed
 *    fields (tool name + timestamp).
 *  - Multiple dispatched events render in chronological dispatch order
 *    (OLDEST first, NEWEST at bottom).
 *  - ADR-015 leak guard: events carrying extra free-form fields
 *    (`contextData`, `contextType`, `selectionRef`, etc.) do NOT render
 *    those fields anywhere in the DOM.
 *  - FIFO cap (`MAX_TRACE_ENTRIES`): the 51st event drops the oldest
 *    entry — the cap is enforced.
 *  - Events without a `timestamp` are dropped (defense in depth).
 *  - Non-trace `context.*` events (e.g. `context_update`) are ignored.
 *  - All six R6 task 059 event types render with a per-type label.
 *
 * Task: R6-061 (D-C-14, Pillar 6c).
 */

import '@testing-library/jest-dom';
import React from 'react';
import { act, render, screen, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { PaneEventBus } from '../../../events/PaneEventBus';
import { PaneEventBusProvider } from '../../../events/PaneEventBusContext';
import ExecutionTraceWidget, {
  MAX_TRACE_ENTRIES,
  type ExecutionTraceData,
} from '../ExecutionTraceWidget';
import type { ContextPaneEvent } from '../../../events/PaneEventTypes';
import type { ContextWidgetProps } from '../../../types/widget-types';

// ---------------------------------------------------------------------------
// Mock scrollIntoView (jsdom does not implement it)
// ---------------------------------------------------------------------------

beforeAll(() => {
  if (typeof Element !== 'undefined') {
    // jsdom does not implement scrollIntoView — install a no-op so the
    // widget's auto-scroll effect does not throw during tests.
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

/** Render the widget inside required providers, returning the bus + utils. */
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

/** Build a base trace event with safe defaults; merge overrides on top. */
function makeEvent(overrides: Partial<ContextPaneEvent> & { type: ContextPaneEvent['type'] }): ContextPaneEvent {
  return {
    timestamp: '2026-06-11T12:00:00.000Z',
    sessionId: 'session-test-1',
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
    expect(
      screen.getByText(/Agent activity will appear here/i)
    ).toBeInTheDocument();
  });

  it('renders the widget region with the correct accessible name', () => {
    renderWidget();
    expect(screen.getByRole('region', { name: 'Execution trace' })).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// Single event rendering
// ---------------------------------------------------------------------------

describe('ExecutionTraceWidget — single event', () => {
  it('renders a tool_call_started event as a row with the tool name', () => {
    const { bus } = renderWidget();
    act(() => {
      bus.dispatch(
        'context',
        makeEvent({
          type: 'tool_call_started',
          toolName: 'send_workspace_artifact',
          timestamp: '2026-06-11T10:30:15.000Z',
        })
      );
    });
    const rows = screen.getAllByTestId('execution-trace-row');
    expect(rows).toHaveLength(1);
    expect(within(rows[0]).getByText('Tool: send_workspace_artifact')).toBeInTheDocument();
    // Timestamp rendered as HH:mm:ss (UTC) — formatTimestamp impl.
    expect(within(rows[0]).getByText('10:30:15')).toBeInTheDocument();
  });

  it('renders a tool_call_completed event with success indicator and duration', () => {
    const { bus } = renderWidget();
    act(() => {
      bus.dispatch(
        'context',
        makeEvent({
          type: 'tool_call_completed',
          toolName: 'send_workspace_artifact',
          durationMs: 124,
          success: true,
        })
      );
    });
    const row = screen.getByTestId('execution-trace-row');
    expect(within(row).getByText('Tool: send_workspace_artifact')).toBeInTheDocument();
    expect(within(row).getByText('124 ms')).toBeInTheDocument();
  });

  it('renders a knowledge_retrieved event with source ID and relevance score', () => {
    const { bus } = renderWidget();
    act(() => {
      bus.dispatch(
        'context',
        makeEvent({
          type: 'knowledge_retrieved',
          knowledgeSourceId: 'kb-corp-policy-en',
          relevanceScore: 0.87,
        })
      );
    });
    const row = screen.getByTestId('execution-trace-row');
    expect(within(row).getByText('Knowledge: kb-corp-policy-en')).toBeInTheDocument();
    expect(within(row).getByText('Relevance: 0.87')).toBeInTheDocument();
  });

  it('renders a playbook_node_executing event with node ID + playbook ID detail', () => {
    const { bus } = renderWidget();
    act(() => {
      bus.dispatch(
        'context',
        makeEvent({
          type: 'playbook_node_executing',
          playbookId: 'summarize-document-for-chat@v1',
          nodeId: 'extract-entities',
        })
      );
    });
    const row = screen.getByTestId('execution-trace-row');
    expect(within(row).getByText('Node: extract-entities')).toBeInTheDocument();
    expect(
      within(row).getByText('Playbook: summarize-document-for-chat@v1')
    ).toBeInTheDocument();
  });

  it('renders a playbook_node_completed event with success + duration detail', () => {
    const { bus } = renderWidget();
    act(() => {
      bus.dispatch(
        'context',
        makeEvent({
          type: 'playbook_node_completed',
          playbookId: 'p1',
          nodeId: 'deliver-output',
          durationMs: 1450,
          success: true,
        })
      );
    });
    const row = screen.getByTestId('execution-trace-row');
    expect(within(row).getByText('Node: deliver-output')).toBeInTheDocument();
    // Detail combines playbook + duration.
    expect(within(row).getByText('Playbook: p1 · 1.45 s')).toBeInTheDocument();
  });

  it('renders a decision_made event with decision + decisionReason', () => {
    const { bus } = renderWidget();
    act(() => {
      bus.dispatch(
        'context',
        makeEvent({
          type: 'decision_made',
          decision: 'route:summarize',
          decisionReason: 'capability-router:summarize-intent-matched',
        })
      );
    });
    const row = screen.getByTestId('execution-trace-row');
    expect(within(row).getByText('Decision: route:summarize')).toBeInTheDocument();
    expect(
      within(row).getByText('capability-router:summarize-intent-matched')
    ).toBeInTheDocument();
  });

  it('renders a failed tool_call_completed event with (failed) suffix', () => {
    const { bus } = renderWidget();
    act(() => {
      bus.dispatch(
        'context',
        makeEvent({
          type: 'tool_call_completed',
          toolName: 'create_workspace_tab',
          success: false,
          durationMs: 42,
        })
      );
    });
    const row = screen.getByTestId('execution-trace-row');
    expect(within(row).getByText('Tool: create_workspace_tab (failed)')).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// Ordering
// ---------------------------------------------------------------------------

describe('ExecutionTraceWidget — chronological order', () => {
  it('renders dispatched events in dispatch order (oldest first, newest at bottom)', () => {
    const { bus } = renderWidget();
    act(() => {
      bus.dispatch(
        'context',
        makeEvent({ type: 'tool_call_started', toolName: 'alpha' })
      );
      bus.dispatch(
        'context',
        makeEvent({ type: 'tool_call_started', toolName: 'bravo' })
      );
      bus.dispatch(
        'context',
        makeEvent({ type: 'tool_call_started', toolName: 'charlie' })
      );
    });
    const rows = screen.getAllByTestId('execution-trace-row');
    expect(rows).toHaveLength(3);
    expect(within(rows[0]).getByText('Tool: alpha')).toBeInTheDocument();
    expect(within(rows[1]).getByText('Tool: bravo')).toBeInTheDocument();
    expect(within(rows[2]).getByText('Tool: charlie')).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// ADR-015 leak guard
// ---------------------------------------------------------------------------

describe('ExecutionTraceWidget — ADR-015 leak guard', () => {
  it('does NOT render contextData, contextType, selectionRef, or selectedFileId fields', () => {
    const { bus, container } = renderWidget();
    // Note: we cast through unknown because the event payload deliberately
    // attaches fields a misbehaving emitter might smuggle. The widget's
    // ADR-015 contract is that NONE of these reach the DOM.
    const leakyEvent: ContextPaneEvent = {
      type: 'tool_call_started',
      timestamp: '2026-06-11T12:00:00.000Z',
      sessionId: 'session-test-1',
      toolName: 'send_workspace_artifact',
      // SENSITIVE: these would be ADR-015 violations if rendered.
      contextType: 'LEAK-context-type-VALUE',
      contextData: { secret: 'LEAK-context-data-USERTEXT' } as unknown,
      selectionRef: 'LEAK-selection-ref-VALUE',
      selectedFileId: 'LEAK-selected-file-VALUE',
      citationId: 'LEAK-citation-id-VALUE',
      stagedFileIds: ['LEAK-staged-file-VALUE'],
    } as ContextPaneEvent;

    act(() => {
      bus.dispatch('context', leakyEvent);
    });

    // The typed tool name MUST render.
    expect(screen.getByText('Tool: send_workspace_artifact')).toBeInTheDocument();
    // None of the smuggled values appear anywhere in the DOM.
    expect(container.textContent ?? '').not.toContain('LEAK-context-type-VALUE');
    expect(container.textContent ?? '').not.toContain('LEAK-context-data-USERTEXT');
    expect(container.textContent ?? '').not.toContain('LEAK-selection-ref-VALUE');
    expect(container.textContent ?? '').not.toContain('LEAK-selected-file-VALUE');
    expect(container.textContent ?? '').not.toContain('LEAK-citation-id-VALUE');
    expect(container.textContent ?? '').not.toContain('LEAK-staged-file-VALUE');
  });
});

// ---------------------------------------------------------------------------
// FIFO cap
// ---------------------------------------------------------------------------

describe('ExecutionTraceWidget — FIFO cap', () => {
  it('drops the oldest entry when the cap is exceeded (51st event drops the 1st)', () => {
    const { bus } = renderWidget();
    // Dispatch MAX_TRACE_ENTRIES + 1 events. The first one (`tool-0`) should
    // be evicted, so the visible rows are `tool-1` .. `tool-50`.
    act(() => {
      for (let i = 0; i <= MAX_TRACE_ENTRIES; i++) {
        bus.dispatch(
          'context',
          makeEvent({ type: 'tool_call_started', toolName: `tool-${i}` })
        );
      }
    });
    const rows = screen.getAllByTestId('execution-trace-row');
    expect(rows).toHaveLength(MAX_TRACE_ENTRIES);
    // Oldest visible should be `tool-1`; the eviction took `tool-0`.
    expect(within(rows[0]).getByText('Tool: tool-1')).toBeInTheDocument();
    // Newest should be `tool-50`.
    expect(within(rows[rows.length - 1]).getByText(`Tool: tool-${MAX_TRACE_ENTRIES}`)).toBeInTheDocument();
    // The evicted entry should be gone.
    expect(screen.queryByText('Tool: tool-0')).not.toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// Filtering
// ---------------------------------------------------------------------------

describe('ExecutionTraceWidget — event filtering', () => {
  it('ignores non-trace context.* events (e.g. context_update)', () => {
    const { bus } = renderWidget();
    act(() => {
      bus.dispatch(
        'context',
        // context_update is a legacy discriminant — not in TRACE_EVENT_TYPES.
        makeEvent({ type: 'context_update', contextType: 'document' })
      );
    });
    expect(screen.getByText('No execution trace yet')).toBeInTheDocument();
    expect(screen.queryAllByTestId('execution-trace-row')).toHaveLength(0);
  });

  it('drops events that arrive without a timestamp (defense in depth)', () => {
    const { bus } = renderWidget();
    act(() => {
      bus.dispatch(
        'context',
        {
          type: 'tool_call_started',
          toolName: 'no-ts',
          sessionId: 'session-test-1',
          // timestamp intentionally absent.
        } as ContextPaneEvent
      );
    });
    expect(screen.queryAllByTestId('execution-trace-row')).toHaveLength(0);
  });

  it('filters by sessionId when the host passes data.sessionId', () => {
    const { bus } = renderWidget({ data: { sessionId: 'session-A' } });
    act(() => {
      bus.dispatch(
        'context',
        makeEvent({
          type: 'tool_call_started',
          toolName: 'in-session',
          sessionId: 'session-A',
        })
      );
      bus.dispatch(
        'context',
        makeEvent({
          type: 'tool_call_started',
          toolName: 'out-of-session',
          sessionId: 'session-B',
        })
      );
    });
    const rows = screen.getAllByTestId('execution-trace-row');
    expect(rows).toHaveLength(1);
    expect(within(rows[0]).getByText('Tool: in-session')).toBeInTheDocument();
    expect(screen.queryByText('Tool: out-of-session')).not.toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// Loading state
// ---------------------------------------------------------------------------

describe('ExecutionTraceWidget — loading state', () => {
  it('renders a Spinner when isLoading is true', () => {
    renderWidget({ isLoading: true });
    // The widget shows a status region while loading; the empty hint is
    // suppressed.
    expect(screen.queryByText('No execution trace yet')).not.toBeInTheDocument();
    expect(screen.getByRole('status', { name: 'Loading' })).toBeInTheDocument();
  });
});
