/**
 * AnalysisHubWidget — unit tests (task 030)
 *
 * Exercises the widget's own contribution — the 3-card row + the DataGrid
 * composition wiring — per the task's ui-tests + acceptance criteria:
 *
 *   1. Exactly three cards render; Agreement Review is actionable, Legal
 *      Research + Patent Application are disabled with a visible "Coming
 *      soon" badge affordance.
 *   2. Clicking Agreement Review dispatches a `widget_load` PaneEventBus
 *      event opening `create-analysis-wizard` with the Agreement Review
 *      work-type value.
 *   3. Negative: clicking a disabled coming-soon card performs no dispatch
 *      and surfaces no error.
 *   4. The Analysis grid is composed via `<DataverseEntityViewWidget>` (the
 *      canonical `<DataGrid configId=… />` wrapper) — NOT a bespoke table —
 *      with the hub's configId threaded through.
 *   5. ADR-021: renders under both light and dark Fluent themes.
 *
 * `DataverseEntityViewWidget` is mocked at the module boundary: it is an
 * ALREADY-TESTED shared-lib component (owns its own Xrm frame-walk +
 * `<DataGrid configId=… />` render — see its own file header) and this
 * widget's job is composition/wiring, not re-deriving DataGrid's internal
 * behavior. The mock lets these tests assert the wiring contract (configId +
 * widgetType passed through) without needing a full Xrm.WebApi + saved-query
 * mock harness — the same "mock the reused, already-tested component"
 * convention `CreateAnalysisWizardWidget.test.tsx` uses for `applyFieldMappings`.
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';

// ---------------------------------------------------------------------------
// Mocks
// ---------------------------------------------------------------------------

const mockDispatch = jest.fn();
jest.mock('../../../events/useDispatchPaneEvent', () => ({
  useDispatchPaneEvent: () => mockDispatch,
}));

jest.mock('../DataverseEntityViewWidget', () => ({
  DataverseEntityViewWidget: (props: { data?: { configId?: string }; widgetType: string }) => (
    <div
      data-testid="mock-dataverse-entity-view-widget"
      data-config-id={props.data?.configId}
      data-widget-type={props.widgetType}
    />
  ),
}));

import { AnalysisHubWidget } from '../AnalysisHubWidget';

// ---------------------------------------------------------------------------
// Test harness
// ---------------------------------------------------------------------------

function renderHub(theme: typeof webLightTheme = webLightTheme) {
  return render(
    <FluentProvider theme={theme}>
      <AnalysisHubWidget data={{}} widgetType="analysis-hub" />
    </FluentProvider>
  );
}

beforeEach(() => {
  mockDispatch.mockClear();
});

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('AnalysisHubWidget', () => {
  it('renders exactly three work-type cards — Agreement Review actionable, two disabled with a coming-soon affordance', () => {
    renderHub();

    expect(screen.getByTestId('analysis-hub-card-agreement-review')).toBeInTheDocument();
    expect(screen.getByTestId('analysis-hub-card-legal-research')).toBeInTheDocument();
    expect(screen.getByTestId('analysis-hub-card-patent-application')).toBeInTheDocument();

    const agreementButton = screen.getByRole('button', { name: /Agreement Review/i });
    expect(agreementButton).toHaveAttribute('aria-disabled', 'false');

    const legalResearchButton = screen.getByRole('button', { name: /Legal Research.*coming soon/i });
    expect(legalResearchButton).toHaveAttribute('aria-disabled', 'true');

    const patentButton = screen.getByRole('button', { name: /Patent Application.*coming soon/i });
    expect(patentButton).toHaveAttribute('aria-disabled', 'true');

    // Visible "Coming soon" affordance — exactly the two disabled cards.
    const badges = screen.getAllByText('Coming soon');
    expect(badges).toHaveLength(2);
  });

  it('dispatches a widget_load event opening create-analysis-wizard when Agreement Review is clicked', () => {
    renderHub();

    const agreementButton = screen.getByRole('button', { name: /Agreement Review/i });
    fireEvent.click(agreementButton);

    expect(mockDispatch).toHaveBeenCalledTimes(1);
    expect(mockDispatch).toHaveBeenCalledWith('workspace', {
      type: 'widget_load',
      widgetType: 'create-analysis-wizard',
      widgetData: {
        workTypeValue: 100000000,
        workTypeLabel: 'Agreement Review',
      },
      displayName: 'Create Agreement Review Analysis',
    });
  });

  it('negative: clicking a disabled coming-soon card performs no dispatch and surfaces no error', () => {
    renderHub();

    const legalResearchButton = screen.getByRole('button', { name: /Legal Research.*coming soon/i });
    const patentButton = screen.getByRole('button', { name: /Patent Application.*coming soon/i });

    expect(() => {
      fireEvent.click(legalResearchButton);
      fireEvent.click(patentButton);
    }).not.toThrow();

    expect(mockDispatch).not.toHaveBeenCalled();
    // No error state rendered.
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('composes the Analysis grid via <DataverseEntityViewWidget> (the DataGrid framework wrapper), threading the hub configId — not a bespoke table', () => {
    renderHub();

    const mockGrid = screen.getByTestId('mock-dataverse-entity-view-widget');
    expect(mockGrid).toBeInTheDocument();
    expect(mockGrid).toHaveAttribute('data-widget-type', 'analysis-hub-grid');
    // Placeholder constant until task 071 seeds the real sprk_gridconfiguration GUID.
    expect(mockGrid.getAttribute('data-config-id')).toBe('00000000-0000-0000-0000-000000000000');
  });

  it('honors a caller-supplied configId override for the Analysis grid', () => {
    render(
      <FluentProvider theme={webLightTheme}>
        <AnalysisHubWidget data={{ configId: 'custom-config-id' }} widgetType="analysis-hub" />
      </FluentProvider>
    );

    expect(screen.getByTestId('mock-dataverse-entity-view-widget')).toHaveAttribute(
      'data-config-id',
      'custom-config-id'
    );
  });

  it('ADR-021: renders correctly under both light and dark Fluent themes', () => {
    const { unmount } = renderHub(webLightTheme);
    expect(screen.getByTestId('analysis-hub-widget')).toBeInTheDocument();
    expect(screen.getAllByText('Coming soon')).toHaveLength(2);
    unmount();

    renderHub(webDarkTheme);
    expect(screen.getByTestId('analysis-hub-widget')).toBeInTheDocument();
    expect(screen.getAllByText('Coming soon')).toHaveLength(2);
    expect(screen.getByRole('button', { name: /Agreement Review/i })).toHaveAttribute('aria-disabled', 'false');
  });
});
