/**
 * Unit tests for ViewToggleToolbar component
 *
 * The toolbar is a 4-way icon-only view switcher (Grid | Network | Treemap | Timeline) rendered as a
 * Fluent v9 TabList. Saved-search selection and the cluster-by dropdown no longer live here
 * (saved searches: SavedSearchSelector inside SearchFilterPane; see SavedSearchSelector.test.tsx).
 *
 * Covers:
 *   - one tab per view mode, reachable by its aria-label
 *   - the selected tab mirrors the `viewMode` prop
 *   - selecting a tab reports the matching ViewMode through `onViewModeChange`
 */

import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { ViewToggleToolbar, type ViewToggleToolbarProps } from '../../components/ViewToggleToolbar';
import type { ViewMode } from '../../types';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const DEFAULT_PROPS: ViewToggleToolbarProps = {
  viewMode: 'grid',
  onViewModeChange: jest.fn(),
};

function renderToolbar(props: Partial<ViewToggleToolbarProps> = {}) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <ViewToggleToolbar {...DEFAULT_PROPS} {...props} />
    </FluentProvider>
  );
}

/** mode → the tab's accessible name (the tabs are icon-only, so aria-label is the contract). */
const TAB_LABELS: Record<ViewMode, string> = {
  grid: 'Switch to grid view',
  map: 'Switch to network graph view',
  treemap: 'Switch to treemap view',
  timeline: 'Switch to timeline view',
};

const ALL_MODES = Object.keys(TAB_LABELS) as ViewMode[];

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('ViewToggleToolbar', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('renders exactly one tab per view mode', () => {
    renderToolbar();

    expect(screen.getAllByRole('tab')).toHaveLength(ALL_MODES.length);
    ALL_MODES.forEach(mode => {
      expect(screen.getByRole('tab', { name: TAB_LABELS[mode] })).toBeInTheDocument();
    });
  });

  it.each(ALL_MODES)('marks only the %s tab as selected when viewMode is %s', mode => {
    renderToolbar({ viewMode: mode });

    ALL_MODES.forEach(other => {
      expect(screen.getByRole('tab', { name: TAB_LABELS[other] })).toHaveAttribute(
        'aria-selected',
        other === mode ? 'true' : 'false'
      );
    });
  });

  it.each(ALL_MODES.filter(mode => mode !== 'grid'))(
    "calls onViewModeChange with '%s' when that tab is clicked from grid mode",
    mode => {
      const onViewModeChange = jest.fn();
      renderToolbar({ viewMode: 'grid', onViewModeChange });

      fireEvent.click(screen.getByRole('tab', { name: TAB_LABELS[mode] }));

      expect(onViewModeChange).toHaveBeenCalledTimes(1);
      expect(onViewModeChange).toHaveBeenCalledWith(mode);
    }
  );

  it("calls onViewModeChange with 'grid' when the Grid tab is clicked from another mode", () => {
    const onViewModeChange = jest.fn();
    renderToolbar({ viewMode: 'timeline', onViewModeChange });

    fireEvent.click(screen.getByRole('tab', { name: TAB_LABELS.grid }));

    expect(onViewModeChange).toHaveBeenCalledTimes(1);
    expect(onViewModeChange).toHaveBeenCalledWith('grid');
  });

  it('does not call onViewModeChange on render', () => {
    const onViewModeChange = jest.fn();
    renderToolbar({ onViewModeChange });

    expect(onViewModeChange).not.toHaveBeenCalled();
  });
});
