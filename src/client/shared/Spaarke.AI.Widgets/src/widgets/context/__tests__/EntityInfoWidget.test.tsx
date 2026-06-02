/**
 * EntityInfoWidget — unit tests
 *
 * Covers:
 * - Required fields (entityType, displayName) render correctly.
 * - Optional fields (status, clientName, ownerName, keyDates, budget,
 *   customFields) render when present.
 * - Absent optional fields produce no empty rows / null labels.
 * - Skeleton loading state renders when isLoading === true.
 * - Error state renders when error prop is set.
 * - Widget updates reactively when a context_update event with a different
 *   entityId arrives via the PaneEventBus.
 * - context_update events with the same entityId do not cause a re-render
 *   of the data (idempotent update guard).
 *
 * Task: AIPU2-087
 */

import '@testing-library/jest-dom';
import React, { act } from 'react';
import { render, screen, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { PaneEventBus } from '../../../events/PaneEventBus';
import { PaneEventBusProvider } from '../../../events/PaneEventBusContext';
import EntityInfoWidget from '../EntityInfoWidget';
import type { EntityInfoData } from '../EntityInfoWidget';
import type { ContextWidgetProps } from '../../../types/widget-types';

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

/** Build a full EntityInfoData fixture, allowing field overrides. */
function makeData(overrides: Partial<EntityInfoData> = {}): EntityInfoData {
  return {
    entityType: 'Matter',
    entityId: 'matter-001',
    displayName: 'Acme Corp v. Widget Co.',
    status: 'Active',
    clientName: 'Acme Corp',
    ownerName: 'Jane Smith',
    keyDates: [
      { label: 'Filing Deadline', date: '2026-09-30' },
      { label: 'Trial Date', date: '2026-11-15' },
    ],
    budget: { total: 50000, spent: 32000, currency: 'USD' },
    customFields: {
      'Practice Area': 'Litigation',
      'Matter Number': 'MAT-2026-0042',
    },
    ...overrides,
  };
}

/** Render EntityInfoWidget inside providers. */
function renderWidget(
  props: Partial<ContextWidgetProps<EntityInfoData>> & { data?: EntityInfoData } = {},
  bus: PaneEventBus = new PaneEventBus()
) {
  const finalProps: ContextWidgetProps<EntityInfoData> = {
    data: makeData(),
    widgetType: 'entity-info',
    isLoading: false,
    ...props,
  };

  const result = render(
    <Wrapper bus={bus}>
      <EntityInfoWidget {...finalProps} />
    </Wrapper>
  );

  return { ...result, bus };
}

// ---------------------------------------------------------------------------
// Field rendering — required fields
// ---------------------------------------------------------------------------

describe('EntityInfoWidget — required fields', () => {
  it('renders the entity display name as a heading', () => {
    renderWidget({ data: makeData({ displayName: 'Acme Corp v. Widget Co.' }) });
    expect(screen.getByText('Acme Corp v. Widget Co.')).toBeInTheDocument();
  });

  it('renders the entity type badge', () => {
    renderWidget({ data: makeData({ entityType: 'Matter' }) });
    expect(screen.getByText('Matter')).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// Field rendering — optional fields present
// ---------------------------------------------------------------------------

describe('EntityInfoWidget — optional fields rendered when present', () => {
  it('renders the status badge when status is provided', () => {
    renderWidget({ data: makeData({ status: 'Active' }) });
    expect(screen.getByText('Active')).toBeInTheDocument();
  });

  it('renders the client name', () => {
    renderWidget({ data: makeData({ clientName: 'Acme Corp' }) });
    expect(screen.getByText('Acme Corp')).toBeInTheDocument();
  });

  it('renders the owner name', () => {
    renderWidget({ data: makeData({ ownerName: 'Jane Smith' }) });
    expect(screen.getByText('Jane Smith')).toBeInTheDocument();
  });

  it('renders key date labels', () => {
    renderWidget({
      data: makeData({
        keyDates: [{ label: 'Filing Deadline', date: '2026-09-30' }],
      }),
    });
    expect(screen.getByText('Filing Deadline')).toBeInTheDocument();
  });

  it('renders key date values as formatted dates', () => {
    renderWidget({
      data: makeData({
        keyDates: [{ label: 'Filing Deadline', date: '2026-09-30' }],
      }),
    });
    // Intl.DateTimeFormat formats 2026-09-30 as "Sep 30, 2026"
    expect(screen.getByText('Sep 30, 2026')).toBeInTheDocument();
  });

  it('renders multiple key dates', () => {
    renderWidget({
      data: makeData({
        keyDates: [
          { label: 'Filing Deadline', date: '2026-09-30' },
          { label: 'Trial Date', date: '2026-11-15' },
        ],
      }),
    });
    expect(screen.getByText('Filing Deadline')).toBeInTheDocument();
    expect(screen.getByText('Trial Date')).toBeInTheDocument();
  });

  it('renders budget spent and total amounts', () => {
    renderWidget({
      data: makeData({ budget: { total: 50000, spent: 32000, currency: 'USD' } }),
    });
    expect(screen.getByText('$32,000 spent')).toBeInTheDocument();
    expect(screen.getByText('of $50,000')).toBeInTheDocument();
  });

  it('renders a progressbar element for the budget', () => {
    renderWidget({
      data: makeData({ budget: { total: 50000, spent: 32000, currency: 'USD' } }),
    });
    const bar = screen.getByRole('progressbar');
    expect(bar).toBeInTheDocument();
    expect(bar).toHaveAttribute('aria-valuenow', '64'); // 32000/50000 = 64%
  });

  it('renders custom field keys and values', () => {
    renderWidget({
      data: makeData({
        customFields: { 'Practice Area': 'Litigation', 'Matter Number': 'MAT-2026-0042' },
      }),
    });
    expect(screen.getByText('Practice Area')).toBeInTheDocument();
    expect(screen.getByText('Litigation')).toBeInTheDocument();
    expect(screen.getByText('Matter Number')).toBeInTheDocument();
    expect(screen.getByText('MAT-2026-0042')).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// Missing optional fields — no empty rows
// ---------------------------------------------------------------------------

describe('EntityInfoWidget — absent optional fields produce no empty rows', () => {
  it('does not render a status badge when status is absent', () => {
    // Only one badge should appear (the entity type badge "Matter").
    // There should be no second badge for status.
    renderWidget({ data: makeData({ status: undefined }) });
    // The entity type badge is always present; any status badge would be a second badge.
    const badges = screen.getAllByText(/./);
    // "Active" must not appear
    expect(screen.queryByText('Active')).not.toBeInTheDocument();
  });

  it('does not render client row when clientName is absent', () => {
    renderWidget({ data: makeData({ clientName: undefined }) });
    expect(screen.queryByText('Client')).not.toBeInTheDocument();
  });

  it('does not render owner row when ownerName is absent', () => {
    renderWidget({ data: makeData({ ownerName: undefined }) });
    expect(screen.queryByText('Owner')).not.toBeInTheDocument();
  });

  it('does not render key dates section when keyDates is absent', () => {
    renderWidget({ data: makeData({ keyDates: undefined }) });
    expect(screen.queryByText('Key Dates')).not.toBeInTheDocument();
  });

  it('does not render key dates section when keyDates is empty', () => {
    renderWidget({ data: makeData({ keyDates: [] }) });
    expect(screen.queryByText('Key Dates')).not.toBeInTheDocument();
  });

  it('does not render budget section when budget is absent', () => {
    renderWidget({ data: makeData({ budget: undefined }) });
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();
    expect(screen.queryByText(/spent/)).not.toBeInTheDocument();
  });

  it('does not render custom fields section when customFields is absent', () => {
    renderWidget({ data: makeData({ customFields: undefined }) });
    expect(screen.queryByText('Additional Info')).not.toBeInTheDocument();
  });

  it('does not render custom fields section when customFields is an empty object', () => {
    renderWidget({ data: makeData({ customFields: {} }) });
    expect(screen.queryByText('Additional Info')).not.toBeInTheDocument();
  });

  it('does not render custom field entries where value is empty string', () => {
    renderWidget({
      data: makeData({ customFields: { 'Practice Area': '', 'Matter Number': 'MAT-001' } }),
    });
    // Empty-value field should not appear; non-empty one should
    expect(screen.queryByText('Practice Area')).not.toBeInTheDocument();
    expect(screen.getByText('Matter Number')).toBeInTheDocument();
  });

  it('renders with only required fields and no optional-field DOM artifacts', () => {
    renderWidget({
      data: {
        entityType: 'Contract',
        entityId: 'contract-001',
        displayName: 'Service Agreement 2026',
      },
    });
    expect(screen.getByText('Service Agreement 2026')).toBeInTheDocument();
    expect(screen.getByText('Contract')).toBeInTheDocument();
    // No optional-field sections
    expect(screen.queryByText('Key Dates')).not.toBeInTheDocument();
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();
    expect(screen.queryByText('Additional Info')).not.toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// Loading state
// ---------------------------------------------------------------------------

describe('EntityInfoWidget — skeleton loading state', () => {
  it('renders skeleton items when isLoading is true', () => {
    renderWidget({ isLoading: true });
    // Fluent v9 SkeletonItem renders as a div with role="none" but the
    // Skeleton wrapper has accessible markup. We verify the main entity name
    // is NOT in the document (skeleton replaced the real content).
    expect(screen.queryByText('Acme Corp v. Widget Co.')).not.toBeInTheDocument();
  });

  it('does not render entity fields while loading', () => {
    renderWidget({ data: makeData(), isLoading: true });
    expect(screen.queryByText('Active')).not.toBeInTheDocument();
    expect(screen.queryByText('Acme Corp')).not.toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// Error state
// ---------------------------------------------------------------------------

describe('EntityInfoWidget — error state', () => {
  it('renders the error message when error prop is set', () => {
    renderWidget({ error: 'Failed to load entity data.' });
    expect(screen.getByText('Failed to load entity data.')).toBeInTheDocument();
  });

  it('does not render entity fields when error is set', () => {
    renderWidget({ data: makeData(), error: 'Something went wrong.' });
    expect(screen.queryByText('Acme Corp v. Widget Co.')).not.toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// Reactive update on context_update event
// ---------------------------------------------------------------------------

describe('EntityInfoWidget — reactive entityId update', () => {
  it('updates displayed data when a context_update event with a different entityId arrives', () => {
    const bus = new PaneEventBus();

    renderWidget(
      {
        data: makeData({
          entityId: 'matter-001',
          displayName: 'Acme Corp v. Widget Co.',
          clientName: 'Acme Corp',
        }),
      },
      bus
    );

    // Initial render shows the first entity
    expect(screen.getByText('Acme Corp v. Widget Co.')).toBeInTheDocument();
    expect(screen.getByText('Acme Corp')).toBeInTheDocument();

    // Dispatch a context_update event with a new entityId
    act(() => {
      bus.dispatch('context', {
        type: 'context_update',
        contextType: 'entity-info',
        contextData: makeData({
          entityId: 'matter-002',
          displayName: 'Beta LLC v. Alpha Inc.',
          clientName: 'Beta LLC',
          status: 'Pending',
        }),
      });
    });

    // Widget should now show the new entity
    expect(screen.getByText('Beta LLC v. Alpha Inc.')).toBeInTheDocument();
    expect(screen.getByText('Beta LLC')).toBeInTheDocument();
    expect(screen.queryByText('Acme Corp v. Widget Co.')).not.toBeInTheDocument();
  });

  it('does not update when a context_update event has the same entityId', () => {
    const bus = new PaneEventBus();

    renderWidget(
      {
        data: makeData({
          entityId: 'matter-001',
          displayName: 'Acme Corp v. Widget Co.',
        }),
      },
      bus
    );

    expect(screen.getByText('Acme Corp v. Widget Co.')).toBeInTheDocument();

    // Dispatch context_update with the same entityId but different display name
    // (shouldn't happen in practice, but guards against needless re-renders)
    act(() => {
      bus.dispatch('context', {
        type: 'context_update',
        contextType: 'entity-info',
        contextData: makeData({
          entityId: 'matter-001',
          displayName: 'Should Not Replace',
        }),
      });
    });

    // Original display name remains — same entityId guards the update
    expect(screen.getByText('Acme Corp v. Widget Co.')).toBeInTheDocument();
    expect(screen.queryByText('Should Not Replace')).not.toBeInTheDocument();
  });

  it('ignores context_highlight events (not context_update)', () => {
    const bus = new PaneEventBus();

    renderWidget(
      {
        data: makeData({ entityId: 'matter-001', displayName: 'Original Matter' }),
      },
      bus
    );

    act(() => {
      bus.dispatch('context', {
        type: 'context_highlight',
        citationId: 'ref-1',
      });
    });

    // Entity data unchanged
    expect(screen.getByText('Original Matter')).toBeInTheDocument();
  });

  it('ignores context_update events with no contextData', () => {
    const bus = new PaneEventBus();

    renderWidget(
      {
        data: makeData({ entityId: 'matter-001', displayName: 'Original Matter' }),
      },
      bus
    );

    act(() => {
      bus.dispatch('context', {
        type: 'context_update',
        contextType: 'entity-info',
        // no contextData
      });
    });

    expect(screen.getByText('Original Matter')).toBeInTheDocument();
  });

  it('updates the status badge when entity changes', () => {
    const bus = new PaneEventBus();

    renderWidget({ data: makeData({ entityId: 'matter-001', status: 'Active' }) }, bus);
    expect(screen.getByText('Active')).toBeInTheDocument();

    act(() => {
      bus.dispatch('context', {
        type: 'context_update',
        contextType: 'entity-info',
        contextData: makeData({ entityId: 'matter-002', status: 'Closed' }),
      });
    });

    expect(screen.getByText('Closed')).toBeInTheDocument();
    expect(screen.queryByText('Active')).not.toBeInTheDocument();
  });

  it('unsubscribes from bus events on unmount (no memory leak)', () => {
    const bus = new PaneEventBus();

    const { unmount } = renderWidget({ data: makeData({ entityId: 'matter-001' }) }, bus);

    const subscribersBefore = bus.subscriberCount('context');
    expect(subscribersBefore).toBeGreaterThan(0);

    unmount();

    expect(bus.subscriberCount('context')).toBe(0);
  });
});

// ---------------------------------------------------------------------------
// Budget — edge cases
// ---------------------------------------------------------------------------

describe('EntityInfoWidget — budget edge cases', () => {
  it('shows over-budget subtext when spent exceeds total', () => {
    renderWidget({
      data: makeData({ budget: { total: 50000, spent: 60000, currency: 'USD' } }),
    });
    expect(screen.getByText(/Over budget by/)).toBeInTheDocument();
    expect(screen.getByText(/\$10,000/)).toBeInTheDocument();
  });

  it('progressbar aria-valuenow is capped at 100 when over budget', () => {
    renderWidget({
      data: makeData({ budget: { total: 50000, spent: 75000, currency: 'USD' } }),
    });
    const bar = screen.getByRole('progressbar');
    expect(bar).toHaveAttribute('aria-valuenow', '100');
  });

  it('handles zero total budget without division-by-zero', () => {
    renderWidget({
      data: makeData({ budget: { total: 0, spent: 0, currency: 'USD' } }),
    });
    const bar = screen.getByRole('progressbar');
    // 0/0 — pct defaults to 0
    expect(bar).toHaveAttribute('aria-valuenow', '0');
  });

  it('uses USD as default currency when currency is absent', () => {
    renderWidget({
      data: makeData({ budget: { total: 100000, spent: 45000 } }),
    });
    expect(screen.getByText('$45,000 spent')).toBeInTheDocument();
    expect(screen.getByText('of $100,000')).toBeInTheDocument();
  });
});
