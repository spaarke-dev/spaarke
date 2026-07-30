/**
 * AnalysisHubWidget — unit tests
 *
 * As of the tabbed-Quick-Start UX change (ai-advanced-capabilities-analysis-hub-r1,
 * 2026-07-29/30) this widget is a PLAIN dataset-grid widget: grid + toolbar only.
 * The prior create-analysis cards were relocated to `AnalysisCardsWidget` (the
 * Quick Start "Analysis" tab) and the task-031 in-place reopen was dropped
 * (row-click = the DataGrid default OOB form). So these tests exercise:
 *
 *   1. The Analysis grid is composed via `<DataverseEntityViewWidget>` (the
 *      canonical `<DataGrid configId=… />` wrapper) — NOT a bespoke table —
 *      with the seeded configId threaded through, and NO `onRecordOpen` override
 *      (row-click uses the DataGrid default).
 *   2. A caller-supplied `configId` override is honored.
 *   3. `+ New` (the DataGrid `onCreateNew` override) dispatches the
 *      `conversation.open_quick_start` intent with `quickStartTab: 'analysis'`.
 *   4. ADR-021: renders under both light and dark Fluent themes.
 *
 * `DataverseEntityViewWidget` is mocked at the module boundary (an already-tested
 * shared-lib component) so the tests assert the wiring contract without a full
 * Xrm.WebApi + saved-query harness — and so the test can invoke the passed
 * `onCreateNew` to simulate the `+ New` toolbar click.
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';

// ---------------------------------------------------------------------------
// Mocks
// ---------------------------------------------------------------------------

const mockDispatch = jest.fn();
jest.mock('../../../events/useDispatchPaneEvent', () => ({
  // The widget reads the bus OPTIONALLY (Pattern D dual-use). The test provides a
  // dispatch so the `+ New` override can be asserted.
  useOptionalDispatchPaneEvent: () => mockDispatch,
}));

// Captures the props the hub hands to the (mocked) grid wrapper — notably
// `onCreateNew` (the `+ New` override) and the absence of `onRecordOpen`.
let lastGridProps: {
  data?: { configId?: string; onCreateNew?: () => void; onRecordOpen?: unknown };
  widgetType: string;
} | null = null;

jest.mock('../DataverseEntityViewWidget', () => ({
  DataverseEntityViewWidget: (props: {
    data?: { configId?: string; onCreateNew?: () => void; onRecordOpen?: unknown };
    widgetType: string;
  }) => {
    lastGridProps = props;
    return (
      <div
        data-testid="mock-dataverse-entity-view-widget"
        data-config-id={props.data?.configId}
        data-widget-type={props.widgetType}
        data-has-create-new={props.data?.onCreateNew ? 'yes' : 'no'}
        data-has-record-open={props.data?.onRecordOpen ? 'yes' : 'no'}
      />
    );
  },
}));

import { AnalysisHubWidget } from '../AnalysisHubWidget';

const SEEDED_CONFIG_ID = 'e7c8126a-968b-f111-8077-7ced8ddc4a05';

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
  lastGridProps = null;
});

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('AnalysisHubWidget', () => {
  it('composes the Analysis grid via <DataverseEntityViewWidget> with the seeded configId and NO row-open override', () => {
    renderHub();

    const mockGrid = screen.getByTestId('mock-dataverse-entity-view-widget');
    expect(mockGrid).toBeInTheDocument();
    expect(mockGrid).toHaveAttribute('data-widget-type', 'analysis-hub-grid');
    expect(mockGrid).toHaveAttribute('data-config-id', SEEDED_CONFIG_ID);
    // Row-click uses the DataGrid DEFAULT (OOB form) — no custom onRecordOpen.
    expect(mockGrid).toHaveAttribute('data-has-record-open', 'no');
    // No create-analysis cards on this surface any more.
    expect(screen.queryByTestId('analysis-hub-card-agreement-review')).not.toBeInTheDocument();
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

  it('`+ New` dispatches conversation.open_quick_start on the Analysis tab', () => {
    renderHub();

    const mockGrid = screen.getByTestId('mock-dataverse-entity-view-widget');
    expect(mockGrid).toHaveAttribute('data-has-create-new', 'yes');

    // Simulate the DataGrid `+ New` toolbar click by invoking the override.
    expect(lastGridProps?.data?.onCreateNew).toBeDefined();
    lastGridProps!.data!.onCreateNew!();

    expect(mockDispatch).toHaveBeenCalledTimes(1);
    expect(mockDispatch).toHaveBeenCalledWith('conversation', {
      type: 'open_quick_start',
      quickStartTab: 'analysis',
    });
  });

  it('ADR-021: renders correctly under both light and dark Fluent themes', () => {
    const { unmount } = renderHub(webLightTheme);
    expect(screen.getByTestId('analysis-hub-widget')).toBeInTheDocument();
    unmount();

    renderHub(webDarkTheme);
    expect(screen.getByTestId('analysis-hub-widget')).toBeInTheDocument();
    expect(screen.getByTestId('mock-dataverse-entity-view-widget')).toBeInTheDocument();
  });
});
