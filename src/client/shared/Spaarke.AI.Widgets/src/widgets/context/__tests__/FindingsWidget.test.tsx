/**
 * FindingsWidget — unit tests
 *
 * Covers:
 * - Findings render with title, risk level Badge (correct Fluent v9 colour per
 *   level), description, and citation links.
 * - Clicking a citation link dispatches context_highlight to the 'context'
 *   PaneEventBus channel with the correct citationId.
 * - Empty findings list renders "No findings identified" message — not a blank pane.
 * - Error state renders the error message and suppresses finding content.
 * - Loading state renders loading text and suppresses finding content.
 * - Detail expand/collapse toggle works correctly.
 * - Findings with no citations render without citation links.
 *
 * Task: AIPU2-089
 */

import '@testing-library/jest-dom';
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { PaneEventBus } from '../../../events/PaneEventBus';
import { PaneEventBusProvider } from '../../../events/PaneEventBusContext';
import FindingsWidget from '../FindingsWidget';
import type { FindingsData, Finding } from '../FindingsWidget';
import type { ContextWidgetProps } from '../../../types/widget-types';
import type { ContextPaneEvent } from '../../../events/PaneEventTypes';

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

/** Build a Finding fixture with safe defaults, allowing overrides. */
function makeFinding(overrides: Partial<Finding> = {}): Finding {
  return {
    id: 'finding-001',
    title: 'Indemnification Clause',
    description: 'The indemnification clause lacks mutual obligation language.',
    riskLevel: 'high',
    citations: [
      { citationId: 'cite-001', displayLabel: '§ 12.3, p. 4' },
      { citationId: 'cite-002', displayLabel: '§ 15.1, p. 7' },
    ],
    detail: 'Detailed analysis: the clause places full indemnification burden on one party only.',
    ...overrides,
  };
}

/** Build a FindingsData fixture with safe defaults. */
function makeData(overrides: Partial<FindingsData> = {}): FindingsData {
  return {
    title: 'Contract Risk Analysis',
    findings: [makeFinding()],
    ...overrides,
  };
}

/** Render FindingsWidget inside required providers. */
function renderWidget(
  props: Partial<ContextWidgetProps<FindingsData>> & { data?: FindingsData } = {},
  bus: PaneEventBus = new PaneEventBus()
) {
  const finalProps: ContextWidgetProps<FindingsData> = {
    data: makeData(),
    widgetType: 'findings',
    isLoading: false,
    ...props,
  };

  const result = render(
    <Wrapper bus={bus}>
      <FindingsWidget {...finalProps} />
    </Wrapper>
  );

  return { ...result, bus };
}

// ---------------------------------------------------------------------------
// Basic rendering
// ---------------------------------------------------------------------------

describe('FindingsWidget — basic rendering', () => {
  it('renders the widget title', () => {
    renderWidget({ data: makeData({ title: 'Contract Risk Analysis' }) });
    expect(screen.getByText('Contract Risk Analysis')).toBeInTheDocument();
  });

  it('renders the default title when title is absent from data', () => {
    const { title: _removed, ...noTitle } = makeData();
    renderWidget({ data: noTitle as FindingsData });
    expect(screen.getByText('Analysis Findings')).toBeInTheDocument();
  });

  it('renders a finding title', () => {
    renderWidget({ data: makeData({ findings: [makeFinding({ title: 'Limitation of Liability' })] }) });
    expect(screen.getByText('Limitation of Liability')).toBeInTheDocument();
  });

  it('renders a finding description', () => {
    renderWidget({
      data: makeData({
        findings: [makeFinding({ description: 'Liability cap is below industry standard.' })],
      }),
    });
    expect(screen.getByText('Liability cap is below industry standard.')).toBeInTheDocument();
  });

  it('renders multiple findings', () => {
    renderWidget({
      data: makeData({
        findings: [
          makeFinding({ id: 'f1', title: 'Finding One' }),
          makeFinding({ id: 'f2', title: 'Finding Two' }),
          makeFinding({ id: 'f3', title: 'Finding Three' }),
        ],
      }),
    });
    expect(screen.getByText('Finding One')).toBeInTheDocument();
    expect(screen.getByText('Finding Two')).toBeInTheDocument();
    expect(screen.getByText('Finding Three')).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// Risk level Badge rendering
// ---------------------------------------------------------------------------

describe('FindingsWidget — risk level Badge colours', () => {
  it('renders a "High Risk" badge for riskLevel high', () => {
    renderWidget({ data: makeData({ findings: [makeFinding({ riskLevel: 'high' })] }) });
    expect(screen.getByText('High Risk')).toBeInTheDocument();
  });

  it('renders a "Medium Risk" badge for riskLevel medium', () => {
    renderWidget({ data: makeData({ findings: [makeFinding({ riskLevel: 'medium' })] }) });
    expect(screen.getByText('Medium Risk')).toBeInTheDocument();
  });

  it('renders a "Low Risk" badge for riskLevel low', () => {
    renderWidget({ data: makeData({ findings: [makeFinding({ riskLevel: 'low' })] }) });
    expect(screen.getByText('Low Risk')).toBeInTheDocument();
  });

  it('renders an "Info" badge for riskLevel info', () => {
    renderWidget({ data: makeData({ findings: [makeFinding({ riskLevel: 'info' })] }) });
    expect(screen.getByText('Info')).toBeInTheDocument();
  });

  it('renders badges for all four risk levels in a multi-finding list', () => {
    renderWidget({
      data: makeData({
        findings: [
          makeFinding({ id: 'f-high', riskLevel: 'high' }),
          makeFinding({ id: 'f-medium', riskLevel: 'medium' }),
          makeFinding({ id: 'f-low', riskLevel: 'low' }),
          makeFinding({ id: 'f-info', riskLevel: 'info' }),
        ],
      }),
    });
    expect(screen.getByText('High Risk')).toBeInTheDocument();
    expect(screen.getByText('Medium Risk')).toBeInTheDocument();
    expect(screen.getByText('Low Risk')).toBeInTheDocument();
    expect(screen.getByText('Info')).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// Citation links
// ---------------------------------------------------------------------------

describe('FindingsWidget — citation links', () => {
  it('renders citation display labels as buttons', () => {
    renderWidget({
      data: makeData({
        findings: [
          makeFinding({
            citations: [
              { citationId: 'cite-001', displayLabel: '§ 12.3, p. 4' },
              { citationId: 'cite-002', displayLabel: '§ 15.1, p. 7' },
            ],
          }),
        ],
      }),
    });
    expect(screen.getByText('§ 12.3, p. 4')).toBeInTheDocument();
    expect(screen.getByText('§ 15.1, p. 7')).toBeInTheDocument();
  });

  it('does not render a citation list when citations is absent', () => {
    renderWidget({
      data: makeData({
        findings: [makeFinding({ citations: undefined })],
      }),
    });
    expect(screen.queryByLabelText('Citations')).not.toBeInTheDocument();
  });

  it('does not render a citation list when citations is an empty array', () => {
    renderWidget({
      data: makeData({
        findings: [makeFinding({ citations: [] })],
      }),
    });
    expect(screen.queryByLabelText('Citations')).not.toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// Citation dispatch — clicking a citation dispatches context_highlight
// ---------------------------------------------------------------------------

describe('FindingsWidget — citation dispatch', () => {
  it('dispatches context_highlight to the context channel when a citation is clicked', () => {
    const bus = new PaneEventBus();
    const captured: ContextPaneEvent[] = [];
    bus.subscribe('context', event => captured.push(event));

    renderWidget(
      {
        data: makeData({
          findings: [
            makeFinding({
              citations: [{ citationId: 'cite-001', displayLabel: '§ 12.3, p. 4' }],
            }),
          ],
        }),
      },
      bus
    );

    fireEvent.click(screen.getByTestId('citation-link-cite-001'));

    expect(captured).toHaveLength(1);
    expect(captured[0].type).toBe('context_highlight');
    expect(captured[0].citationId).toBe('cite-001');
  });

  it('dispatches the correct citationId when multiple citations exist and the second is clicked', () => {
    const bus = new PaneEventBus();
    const captured: ContextPaneEvent[] = [];
    bus.subscribe('context', event => captured.push(event));

    renderWidget(
      {
        data: makeData({
          findings: [
            makeFinding({
              citations: [
                { citationId: 'cite-001', displayLabel: '§ 12.3, p. 4' },
                { citationId: 'cite-002', displayLabel: '§ 15.1, p. 7' },
              ],
            }),
          ],
        }),
      },
      bus
    );

    fireEvent.click(screen.getByTestId('citation-link-cite-002'));

    expect(captured).toHaveLength(1);
    expect(captured[0].citationId).toBe('cite-002');
  });

  it('dispatches separate context_highlight events for each citation in separate findings', () => {
    const bus = new PaneEventBus();
    const captured: ContextPaneEvent[] = [];
    bus.subscribe('context', event => captured.push(event));

    renderWidget(
      {
        data: makeData({
          findings: [
            makeFinding({
              id: 'f1',
              citations: [{ citationId: 'cite-A', displayLabel: 'Citation A' }],
            }),
            makeFinding({
              id: 'f2',
              citations: [{ citationId: 'cite-B', displayLabel: 'Citation B' }],
            }),
          ],
        }),
      },
      bus
    );

    fireEvent.click(screen.getByTestId('citation-link-cite-A'));
    fireEvent.click(screen.getByTestId('citation-link-cite-B'));

    expect(captured).toHaveLength(2);
    expect(captured[0].citationId).toBe('cite-A');
    expect(captured[1].citationId).toBe('cite-B');
  });

  it('dispatches to the context channel (not workspace or conversation)', () => {
    const bus = new PaneEventBus();
    const workspaceEvents: unknown[] = [];
    const conversationEvents: unknown[] = [];
    const contextEvents: ContextPaneEvent[] = [];

    bus.subscribe('workspace', e => workspaceEvents.push(e));
    bus.subscribe('conversation', e => conversationEvents.push(e));
    bus.subscribe('context', e => contextEvents.push(e));

    renderWidget(
      {
        data: makeData({
          findings: [
            makeFinding({
              citations: [{ citationId: 'cite-001', displayLabel: '§ 12.3' }],
            }),
          ],
        }),
      },
      bus
    );

    fireEvent.click(screen.getByTestId('citation-link-cite-001'));

    expect(workspaceEvents).toHaveLength(0);
    expect(conversationEvents).toHaveLength(0);
    expect(contextEvents).toHaveLength(1);
    expect(contextEvents[0].type).toBe('context_highlight');
  });
});

// ---------------------------------------------------------------------------
// Empty findings list
// ---------------------------------------------------------------------------

describe('FindingsWidget — empty findings list', () => {
  it('renders "No findings identified" when findings array is empty', () => {
    renderWidget({ data: makeData({ findings: [] }) });
    expect(screen.getByText('No findings identified')).toBeInTheDocument();
  });

  it('does not render any finding cards when findings is empty', () => {
    renderWidget({ data: makeData({ findings: [] }) });
    expect(screen.queryByTestId(/finding-card-/)).not.toBeInTheDocument();
  });

  it('still renders the widget title when findings is empty', () => {
    renderWidget({ data: makeData({ findings: [], title: 'Risk Report' }) });
    expect(screen.getByText('Risk Report')).toBeInTheDocument();
    expect(screen.getByText('No findings identified')).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// Error state
// ---------------------------------------------------------------------------

describe('FindingsWidget — error state', () => {
  it('renders the error message when error prop is set', () => {
    renderWidget({ error: 'Failed to load findings data.' });
    expect(screen.getByText('Failed to load findings data.')).toBeInTheDocument();
  });

  it('does not render finding content when error is set', () => {
    renderWidget({
      data: makeData({ findings: [makeFinding({ title: 'Should Not Appear' })] }),
      error: 'Something went wrong.',
    });
    expect(screen.queryByText('Should Not Appear')).not.toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// Loading state
// ---------------------------------------------------------------------------

describe('FindingsWidget — loading state', () => {
  it('renders loading text when isLoading is true', () => {
    renderWidget({ isLoading: true });
    expect(screen.getByText(/Loading findings/i)).toBeInTheDocument();
  });

  it('does not render findings when isLoading is true', () => {
    renderWidget({
      data: makeData({ findings: [makeFinding({ title: 'Hidden While Loading' })] }),
      isLoading: true,
    });
    expect(screen.queryByText('Hidden While Loading')).not.toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// Detail expand/collapse
// ---------------------------------------------------------------------------

describe('FindingsWidget — detail expand/collapse', () => {
  it('does not render detail text before toggling when detail is present', () => {
    renderWidget({
      data: makeData({
        findings: [
          makeFinding({
            detail: 'Extended detail content here.',
          }),
        ],
      }),
    });
    // Detail text is hidden by default
    expect(screen.queryByText('Extended detail content here.')).not.toBeInTheDocument();
  });

  it('reveals detail text when the expand button is clicked', () => {
    renderWidget({
      data: makeData({
        findings: [
          makeFinding({
            detail: 'Extended detail content here.',
          }),
        ],
      }),
    });

    const moreButton = screen.getByRole('button', { name: /Expand detail/i });
    fireEvent.click(moreButton);

    expect(screen.getByText('Extended detail content here.')).toBeInTheDocument();
  });

  it('hides detail text again when the collapse button is clicked', () => {
    renderWidget({
      data: makeData({
        findings: [
          makeFinding({
            detail: 'Extended detail content here.',
          }),
        ],
      }),
    });

    const moreButton = screen.getByRole('button', { name: /Expand detail/i });
    fireEvent.click(moreButton);
    expect(screen.getByText('Extended detail content here.')).toBeInTheDocument();

    const lessButton = screen.getByRole('button', { name: /Collapse detail/i });
    fireEvent.click(lessButton);
    expect(screen.queryByText('Extended detail content here.')).not.toBeInTheDocument();
  });

  it('does not render an expand button when detail is absent', () => {
    renderWidget({
      data: makeData({
        findings: [makeFinding({ detail: undefined })],
      }),
    });
    expect(screen.queryByRole('button', { name: /Expand detail/i })).not.toBeInTheDocument();
  });

  it('does not render an expand button when detail is an empty string', () => {
    renderWidget({
      data: makeData({
        findings: [makeFinding({ detail: '' })],
      }),
    });
    expect(screen.queryByRole('button', { name: /Expand detail/i })).not.toBeInTheDocument();
  });
});
