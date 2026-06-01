/**
 * ProgressTrackerWidget — unit tests
 *
 * Covers:
 * - Completed steps render CheckmarkCircle icon with green aria-label.
 * - Active step renders Spinner (aria-label "In progress").
 * - Pending steps render muted Circle icon (aria-label "Pending").
 * - Empty step list renders a Spinner only (not blank).
 * - Step list is fully replaced on each context_update event.
 * - All-completed state dispatches context_update to 'related-items' stage.
 * - isLoading renders Spinner.
 * - Detail text is hidden by default and shown on click.
 * - Footer shows "Step X of Y".
 *
 * Task: AIPU2-088
 */

import '@testing-library/jest-dom';
import React, { act } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { PaneEventBus } from '../../../events/PaneEventBus';
import { PaneEventBusProvider } from '../../../events/PaneEventBusContext';
import ProgressTrackerWidget from '../ProgressTrackerWidget';
import type { ProgressTrackerData } from '../ProgressTrackerWidget';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Wraps the widget in the required providers for rendering in tests. */
function renderWidget(
  data: ProgressTrackerData | unknown,
  bus: PaneEventBus,
  overrides: { isLoading?: boolean; className?: string } = {}
) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={bus}>
        <ProgressTrackerWidget
          data={data}
          widgetType="progress-tracker"
          isLoading={overrides.isLoading}
          className={overrides.className}
        />
      </PaneEventBusProvider>
    </FluentProvider>
  );
}

/** Minimal valid ProgressTrackerData for testing. */
function makeData(overrides: Partial<ProgressTrackerData> = {}): ProgressTrackerData {
  return {
    title: 'Contract Review Pipeline',
    steps: [
      { id: 'step-1', label: 'Extract Clauses', status: 'completed' },
      { id: 'step-2', label: 'Classify Risk', status: 'active' },
      { id: 'step-3', label: 'Generate Summary', status: 'pending' },
    ],
    currentStepIndex: 1,
    totalSteps: 3,
    ...overrides,
  };
}

// ---------------------------------------------------------------------------
// Icon rendering: step states
// ---------------------------------------------------------------------------

describe('ProgressTrackerWidget — step icon states', () => {
  it('renders CheckmarkCircle (Completed aria-label) for completed steps', () => {
    const bus = new PaneEventBus();
    renderWidget(makeData(), bus);

    // Step 1 is completed — its icon wrapper carries aria-label="Completed"
    const stepEl = screen.getByTestId('step-step-1');
    expect(stepEl.querySelector('[aria-label="Completed"]')).toBeInTheDocument();
  });

  it('renders Spinner for the active step', () => {
    const bus = new PaneEventBus();
    renderWidget(makeData(), bus);

    // Step 2 is active — its icon wrapper carries aria-label="In progress"
    const stepEl = screen.getByTestId('step-step-2');
    expect(stepEl.querySelector('[aria-label="In progress"]')).toBeInTheDocument();
  });

  it('renders Circle (Pending aria-label) for pending steps', () => {
    const bus = new PaneEventBus();
    renderWidget(makeData(), bus);

    // Step 3 is pending
    const stepEl = screen.getByTestId('step-step-3');
    expect(stepEl.querySelector('[aria-label="Pending"]')).toBeInTheDocument();
  });

  it('renders step labels in the DOM', () => {
    const bus = new PaneEventBus();
    renderWidget(makeData(), bus);

    expect(screen.getByText('Extract Clauses')).toBeInTheDocument();
    expect(screen.getByText('Classify Risk')).toBeInTheDocument();
    expect(screen.getByText('Generate Summary')).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// Empty step list
// ---------------------------------------------------------------------------

describe('ProgressTrackerWidget — empty step list', () => {
  it('renders a Spinner (not blank) when steps array is empty', () => {
    const bus = new PaneEventBus();
    renderWidget(makeData({ steps: [], currentStepIndex: 0, totalSteps: 0 }), bus);

    expect(screen.getByTestId('progress-tracker-empty')).toBeInTheDocument();
    // Spinner is present — not a blank pane
    expect(screen.getByRole('progressbar')).toBeInTheDocument();
  });

  it('renders a Spinner when data is null / unknown shape', () => {
    const bus = new PaneEventBus();
    renderWidget(null, bus);

    expect(screen.getByTestId('progress-tracker-empty')).toBeInTheDocument();
    expect(screen.getByRole('progressbar')).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// isLoading state
// ---------------------------------------------------------------------------

describe('ProgressTrackerWidget — isLoading', () => {
  it('renders a Spinner when isLoading is true', () => {
    const bus = new PaneEventBus();
    renderWidget(makeData(), bus, { isLoading: true });

    expect(screen.getByRole('progressbar')).toBeInTheDocument();
    // Step list must NOT be rendered while loading
    expect(screen.queryByTestId('progress-tracker-root')).not.toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// context_update event replaces step list
// ---------------------------------------------------------------------------

describe('ProgressTrackerWidget — context_update replaces step list', () => {
  it('fully replaces steps when a context_update event fires on the context channel', async () => {
    const bus = new PaneEventBus();

    renderWidget(makeData(), bus);

    // Initial state: Extract Clauses visible, Step 1 of 3 in footer
    expect(screen.getByText('Extract Clauses')).toBeInTheDocument();
    expect(screen.getByText('Step 2 of 3')).toBeInTheDocument();

    // Dispatch a replacement payload
    const updatedData: ProgressTrackerData = {
      title: 'Due Diligence Workflow',
      steps: [
        { id: 'dd-1', label: 'Gather Documents', status: 'completed' },
        { id: 'dd-2', label: 'Analyse Financials', status: 'completed' },
        { id: 'dd-3', label: 'Flag Issues', status: 'active' },
        { id: 'dd-4', label: 'Summarise Findings', status: 'pending' },
      ],
      currentStepIndex: 2,
      totalSteps: 4,
    };

    act(() => {
      bus.dispatch('context', {
        type: 'context_update',
        contextData: updatedData,
      });
    });

    // Old steps gone, new steps present
    expect(screen.queryByText('Extract Clauses')).not.toBeInTheDocument();
    expect(screen.getByText('Gather Documents')).toBeInTheDocument();
    expect(screen.getByText('Analyse Financials')).toBeInTheDocument();
    expect(screen.getByText('Flag Issues')).toBeInTheDocument();
    expect(screen.getByText('Summarise Findings')).toBeInTheDocument();

    // Footer updated
    expect(screen.getByText('Step 3 of 4')).toBeInTheDocument();
  });

  it('ignores context_update events that carry non-progress contextData', async () => {
    const bus = new PaneEventBus();
    renderWidget(makeData(), bus);

    act(() => {
      bus.dispatch('context', {
        type: 'context_update',
        contextData: { irrelevant: true }, // missing required fields
      });
    });

    // Original steps still rendered
    expect(screen.getByText('Extract Clauses')).toBeInTheDocument();
  });

  it('ignores context_highlight events (wrong event type)', async () => {
    const bus = new PaneEventBus();
    renderWidget(makeData(), bus);

    act(() => {
      bus.dispatch('context', {
        type: 'context_highlight',
        citationId: 'ref-999',
      });
    });

    expect(screen.getByText('Extract Clauses')).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// All-completed transition
// ---------------------------------------------------------------------------

describe('ProgressTrackerWidget — all-completed stage transition', () => {
  beforeEach(() => {
    jest.useFakeTimers();
  });

  afterEach(() => {
    jest.runOnlyPendingTimers();
    jest.useRealTimers();
  });

  it('dispatches context_update with contextType "related-items" after all steps complete', async () => {
    const bus = new PaneEventBus();
    const received: unknown[] = [];

    bus.subscribe('context', event => {
      if (event.type === 'context_update' && event.contextType === 'related-items') {
        received.push(event);
      }
    });

    // Start with all steps already completed
    const allDoneData: ProgressTrackerData = makeData({
      steps: [
        { id: 's1', label: 'Step One', status: 'completed' },
        { id: 's2', label: 'Step Two', status: 'completed' },
      ],
      currentStepIndex: 1,
      totalSteps: 2,
    });

    renderWidget(allDoneData, bus);

    // No dispatch yet
    expect(received).toHaveLength(0);

    // Advance timers by 1 500 ms (the delay defined in the widget)
    act(() => {
      jest.advanceTimersByTime(1500);
    });

    expect(received).toHaveLength(1);
    expect(received[0]).toMatchObject({
      type: 'context_update',
      contextType: 'related-items',
    });
  });

  it('dispatches after all steps transition to completed via context_update event', async () => {
    const bus = new PaneEventBus();
    const received: unknown[] = [];

    bus.subscribe('context', event => {
      if (event.type === 'context_update' && event.contextType === 'related-items') {
        received.push(event);
      }
    });

    // Start with one active step
    renderWidget(makeData(), bus);

    // Dispatch replacement where all steps are done
    act(() => {
      bus.dispatch('context', {
        type: 'context_update',
        contextData: makeData({
          steps: [
            { id: 'step-1', label: 'Extract Clauses', status: 'completed' },
            { id: 'step-2', label: 'Classify Risk', status: 'completed' },
            { id: 'step-3', label: 'Generate Summary', status: 'completed' },
          ],
          currentStepIndex: 2,
        }),
      });
    });

    expect(received).toHaveLength(0);

    act(() => {
      jest.advanceTimersByTime(1500);
    });

    expect(received).toHaveLength(1);
  });

  it('does NOT dispatch if not all steps are completed', () => {
    const bus = new PaneEventBus();
    const received: unknown[] = [];

    bus.subscribe('context', event => {
      if (event.type === 'context_update' && event.contextType === 'related-items') {
        received.push(event);
      }
    });

    renderWidget(makeData(), bus); // step-2 is 'active', step-3 is 'pending'

    act(() => {
      jest.advanceTimersByTime(5000);
    });

    expect(received).toHaveLength(0);
  });

  it('does NOT dispatch twice if the component re-renders while timer is running', () => {
    const bus = new PaneEventBus();
    const received: unknown[] = [];

    bus.subscribe('context', event => {
      if (event.type === 'context_update' && event.contextType === 'related-items') {
        received.push(event);
      }
    });

    const allDoneData: ProgressTrackerData = makeData({
      steps: [
        { id: 's1', label: 'Step One', status: 'completed' },
        { id: 's2', label: 'Step Two', status: 'completed' },
      ],
      currentStepIndex: 1,
      totalSteps: 2,
    });

    const { rerender } = renderWidget(allDoneData, bus);

    // Re-render with same all-done data mid-timer
    act(() => {
      rerender(
        <FluentProvider theme={webLightTheme}>
          <PaneEventBusProvider bus={bus}>
            <ProgressTrackerWidget data={allDoneData} widgetType="progress-tracker" />
          </PaneEventBusProvider>
        </FluentProvider>
      );
    });

    act(() => {
      jest.advanceTimersByTime(1500);
    });

    // Must dispatch exactly once
    expect(received).toHaveLength(1);
  });
});

// ---------------------------------------------------------------------------
// Detail expand / collapse
// ---------------------------------------------------------------------------

describe('ProgressTrackerWidget — step detail expand/collapse', () => {
  it('does not show detail text by default', () => {
    const bus = new PaneEventBus();
    const data = makeData({
      steps: [
        { id: 's1', label: 'Step One', status: 'active', detail: 'Processing 47 clauses…' },
        { id: 's2', label: 'Step Two', status: 'pending' },
      ],
    });

    renderWidget(data, bus);

    expect(screen.queryByText('Processing 47 clauses…')).not.toBeInTheDocument();
  });

  it('shows detail text after clicking the step row', async () => {
    const user = userEvent.setup();
    const bus = new PaneEventBus();
    const data = makeData({
      steps: [
        { id: 's1', label: 'Step One', status: 'active', detail: 'Processing 47 clauses…' },
        { id: 's2', label: 'Step Two', status: 'pending' },
      ],
    });

    renderWidget(data, bus);

    const stepRow = screen.getByRole('button', { name: /Step One/i });
    await user.click(stepRow);

    expect(screen.getByText('Processing 47 clauses…')).toBeInTheDocument();
  });

  it('collapses detail text on a second click', async () => {
    const user = userEvent.setup();
    const bus = new PaneEventBus();
    const data = makeData({
      steps: [{ id: 's1', label: 'Step One', status: 'active', detail: 'Processing 47 clauses…' }],
      totalSteps: 1,
    });

    renderWidget(data, bus);

    const stepRow = screen.getByRole('button', { name: /Step One/i });
    await user.click(stepRow);
    expect(screen.getByText('Processing 47 clauses…')).toBeInTheDocument();

    await user.click(stepRow);
    expect(screen.queryByText('Processing 47 clauses…')).not.toBeInTheDocument();
  });

  it('does not render a button role on steps with no detail', () => {
    const bus = new PaneEventBus();
    renderWidget(makeData(), bus); // default steps have no detail

    // No step row should have button role since none have detail
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// Footer summary
// ---------------------------------------------------------------------------

describe('ProgressTrackerWidget — footer summary', () => {
  it('shows "Step X of Y" in the footer', () => {
    const bus = new PaneEventBus();
    renderWidget(makeData({ currentStepIndex: 0, totalSteps: 5 }), bus);

    expect(screen.getByText('Step 1 of 5')).toBeInTheDocument();
  });

  it('clamps summaryStep to totalSteps when currentStepIndex equals totalSteps - 1', () => {
    const bus = new PaneEventBus();
    renderWidget(
      makeData({
        steps: [{ id: 's1', label: 'Final Step', status: 'active' }],
        currentStepIndex: 4,
        totalSteps: 5,
      }),
      bus
    );

    expect(screen.getByText('Step 5 of 5')).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// Header title
// ---------------------------------------------------------------------------

describe('ProgressTrackerWidget — header', () => {
  it('renders the title text in the header', () => {
    const bus = new PaneEventBus();
    renderWidget(makeData({ title: 'M&A Due Diligence' }), bus);

    expect(screen.getByText('M&A Due Diligence')).toBeInTheDocument();
  });
});
