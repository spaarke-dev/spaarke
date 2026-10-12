/**
 * Unit tests for SearchCommandBar component
 *
 * Layout under test: [Refresh] [Columns] [Delete] [... overflow]. The overflow menu hosts
 * Email a Link / Open in Web / Open in Desktop / Download / Send to Index / Save Search.
 *
 * Covers:
 *   - Always-visible toolbar buttons (Refresh, Columns, Delete)
 *   - Selection-dependent enabled/disabled states (0, 1, multiple selections)
 *   - Document-only overflow commands are shown only in the documents domain
 *   - Callback invocations with the correct arguments
 *   - Column picker: checked state mirrors hiddenColumns; toggling reports the new hidden set
 *   - Save Search is always available
 */

import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SearchCommandBar, type SearchCommandBarProps } from '../../components/SearchCommandBar';
import type { SearchDomain } from '../../types';
import type { IDatasetColumn } from '../../hooks/useSearchViewDefinitions';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const COLUMNS: IDatasetColumn[] = [
  { name: 'name', displayName: 'Name', dataType: 'SingleLine.Text', isPrimary: true },
  { name: 'similarity', displayName: 'Similarity', dataType: 'Decimal' },
  { name: 'modified', displayName: 'Modified', dataType: 'DateAndTime.DateOnly' },
];

const DEFAULT_PROPS: SearchCommandBarProps = {
  selectedIds: [],
  activeDomain: 'documents',
  onDelete: jest.fn(),
  onRefresh: jest.fn(),
  onEmailLink: jest.fn(),
  onOpenInWeb: jest.fn(),
  onOpenInDesktop: jest.fn(),
  onDownload: jest.fn(),
  onSendToIndex: jest.fn(),
  onSaveSearch: jest.fn(),
  columns: [],
  hiddenColumns: new Set<string>(),
  onHiddenColumnsChange: jest.fn(),
};

function renderCommandBar(props: Partial<SearchCommandBarProps> = {}) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <SearchCommandBar {...DEFAULT_PROPS} {...props} />
    </FluentProvider>
  );
}

/** Open the "..." overflow menu (secondary actions render only while it is open). */
function openOverflow() {
  fireEvent.click(screen.getByRole('button', { name: 'More actions' }));
}

/**
 * The Delete toolbar button. Its accessible name is the Tooltip (relationship="label") text, which
 * tells the user why it is disabled: "Select items to delete" with no selection, "Delete selected" otherwise.
 */
function deleteButton(hasSelection: boolean) {
  return screen.getByRole('button', { name: hasSelection ? 'Delete selected' : 'Select items to delete' });
}

/** The overflow menu item with this accessible name. */
function overflowItem(name: string) {
  return screen.getByRole('menuitem', { name });
}

/** Fluent MenuItem exposes its disabled state through aria-disabled (it stays focusable). */
function expectDisabled(el: HTMLElement) {
  expect(el).toHaveAttribute('aria-disabled', 'true');
}

function expectEnabled(el: HTMLElement) {
  expect(el).not.toHaveAttribute('aria-disabled', 'true');
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('SearchCommandBar', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  // ---- Always-visible toolbar buttons ----

  it('renders Refresh, Columns, Delete (with its why-disabled tooltip label) and the overflow trigger on the toolbar', () => {
    renderCommandBar();

    expect(screen.getByRole('button', { name: 'Refresh' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Choose columns' })).toBeInTheDocument();
    expect(deleteButton(false)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'More actions' })).toBeInTheDocument();
  });

  it('keeps secondary actions out of the toolbar until the overflow menu is opened', () => {
    renderCommandBar();

    expect(screen.queryByRole('menuitem', { name: 'Email a Link' })).not.toBeInTheDocument();

    openOverflow();

    expect(overflowItem('Email a Link')).toBeInTheDocument();
  });

  it('calls onRefresh when Refresh is clicked, regardless of selection', () => {
    const onRefresh = jest.fn();
    renderCommandBar({ onRefresh, selectedIds: [] });

    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }));

    expect(onRefresh).toHaveBeenCalledTimes(1);
  });

  // ---- No selection (disabled states) ----

  describe('with no selection', () => {
    it('disables Delete', () => {
      renderCommandBar({ selectedIds: [] });

      expect(deleteButton(false)).toBeDisabled();
    });

    it('does not call onDelete when the disabled Delete is clicked', () => {
      const onDelete = jest.fn();
      renderCommandBar({ selectedIds: [], onDelete });

      fireEvent.click(deleteButton(false));

      expect(onDelete).not.toHaveBeenCalled();
    });

    it('disables Email a Link and every document command in the overflow menu', () => {
      renderCommandBar({ selectedIds: [], activeDomain: 'documents' });
      openOverflow();

      expectDisabled(overflowItem('Email a Link'));
      expectDisabled(overflowItem('Open in Web'));
      expectDisabled(overflowItem('Open in Desktop'));
      expectDisabled(overflowItem('Download'));
      expectDisabled(overflowItem('Send to Index'));
    });

    it('does not call a handler when a disabled overflow command is clicked', () => {
      const onEmailLink = jest.fn();
      const onSendToIndex = jest.fn();
      renderCommandBar({ selectedIds: [], activeDomain: 'documents', onEmailLink, onSendToIndex });
      openOverflow();

      fireEvent.click(overflowItem('Email a Link'));
      fireEvent.click(overflowItem('Send to Index'));

      expect(onEmailLink).not.toHaveBeenCalled();
      expect(onSendToIndex).not.toHaveBeenCalled();
    });
  });

  // ---- Single selection ----

  describe('with single selection', () => {
    const singleSelection = ['record-abc-123'];

    it('enables Delete and every overflow command', () => {
      renderCommandBar({ selectedIds: singleSelection, activeDomain: 'documents' });

      expect(deleteButton(true)).not.toBeDisabled();

      openOverflow();
      expectEnabled(overflowItem('Email a Link'));
      expectEnabled(overflowItem('Open in Web'));
      expectEnabled(overflowItem('Open in Desktop'));
      expectEnabled(overflowItem('Download'));
      expectEnabled(overflowItem('Send to Index'));
    });

    it('calls onDelete with the selected IDs', () => {
      const onDelete = jest.fn();
      renderCommandBar({ selectedIds: singleSelection, onDelete });

      fireEvent.click(deleteButton(true));

      expect(onDelete).toHaveBeenCalledTimes(1);
      expect(onDelete).toHaveBeenCalledWith(['record-abc-123']);
    });

    it.each([
      ['Email a Link', 'onEmailLink'],
      ['Open in Web', 'onOpenInWeb'],
      ['Open in Desktop', 'onOpenInDesktop'],
      ['Download', 'onDownload'],
    ] as const)('%s calls %s with the single selected ID', (label, handlerName) => {
      const handler = jest.fn();
      renderCommandBar({ selectedIds: singleSelection, activeDomain: 'documents', [handlerName]: handler });
      openOverflow();

      fireEvent.click(overflowItem(label));

      expect(handler).toHaveBeenCalledTimes(1);
      expect(handler).toHaveBeenCalledWith('record-abc-123');
    });

    it('calls onSendToIndex with the selected IDs (array)', () => {
      const onSendToIndex = jest.fn();
      renderCommandBar({ selectedIds: singleSelection, activeDomain: 'documents', onSendToIndex });
      openOverflow();

      fireEvent.click(overflowItem('Send to Index'));

      expect(onSendToIndex).toHaveBeenCalledWith(['record-abc-123']);
    });
  });

  // ---- Multiple selection ----

  describe('with multiple selection', () => {
    const multiSelection = ['id-1', 'id-2', 'id-3'];

    it('enables Delete', () => {
      renderCommandBar({ selectedIds: multiSelection });

      expect(deleteButton(true)).not.toBeDisabled();
    });

    it('disables the single-record commands (Email a Link, Open in Web/Desktop, Download)', () => {
      renderCommandBar({ selectedIds: multiSelection, activeDomain: 'documents' });
      openOverflow();

      expectDisabled(overflowItem('Email a Link'));
      expectDisabled(overflowItem('Open in Web'));
      expectDisabled(overflowItem('Open in Desktop'));
      expectDisabled(overflowItem('Download'));
    });

    it('keeps Send to Index enabled (it accepts many records)', () => {
      renderCommandBar({ selectedIds: multiSelection, activeDomain: 'documents' });
      openOverflow();

      expectEnabled(overflowItem('Send to Index'));
    });

    it('calls onDelete with all selected IDs', () => {
      const onDelete = jest.fn();
      renderCommandBar({ selectedIds: multiSelection, onDelete });

      fireEvent.click(deleteButton(true));

      expect(onDelete).toHaveBeenCalledWith(['id-1', 'id-2', 'id-3']);
    });

    it('calls onSendToIndex with all selected IDs', () => {
      const onSendToIndex = jest.fn();
      renderCommandBar({ selectedIds: multiSelection, activeDomain: 'documents', onSendToIndex });
      openOverflow();

      fireEvent.click(overflowItem('Send to Index'));

      expect(onSendToIndex).toHaveBeenCalledWith(['id-1', 'id-2', 'id-3']);
    });

    it('does not call onEmailLink for a multi-selection', () => {
      const onEmailLink = jest.fn();
      renderCommandBar({ selectedIds: multiSelection, onEmailLink });
      openOverflow();

      fireEvent.click(overflowItem('Email a Link'));

      expect(onEmailLink).not.toHaveBeenCalled();
    });
  });

  // ---- Domain-specific command visibility ----

  describe('document-only commands', () => {
    it('shows the document commands in the overflow menu for the documents domain', () => {
      renderCommandBar({ activeDomain: 'documents' });
      openOverflow();

      expect(overflowItem('Open in Web')).toBeInTheDocument();
      expect(overflowItem('Open in Desktop')).toBeInTheDocument();
      expect(overflowItem('Download')).toBeInTheDocument();
      expect(overflowItem('Send to Index')).toBeInTheDocument();
    });

    it.each<SearchDomain>(['matters', 'projects', 'invoices'])('hides the document commands for the %s domain', domain => {
      renderCommandBar({ activeDomain: domain });
      openOverflow();

      expect(screen.queryByRole('menuitem', { name: 'Open in Web' })).not.toBeInTheDocument();
      expect(screen.queryByRole('menuitem', { name: 'Open in Desktop' })).not.toBeInTheDocument();
      expect(screen.queryByRole('menuitem', { name: 'Download' })).not.toBeInTheDocument();
      expect(screen.queryByRole('menuitem', { name: 'Send to Index' })).not.toBeInTheDocument();
    });

    it.each<SearchDomain>(['documents', 'matters', 'projects', 'invoices'])(
      'always offers Refresh, Delete, Email a Link and Save Search (%s domain)',
      domain => {
        renderCommandBar({ activeDomain: domain });

        expect(screen.getByRole('button', { name: 'Refresh' })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /^(Delete selected|Select items to delete)$/ })).toBeInTheDocument();

        openOverflow();
        expect(overflowItem('Email a Link')).toBeInTheDocument();
        expect(overflowItem('Save Search')).toBeInTheDocument();
      }
    );
  });

  // ---- Save Search ----

  describe('Save Search', () => {
    it('is enabled with no selection and calls onSaveSearch', () => {
      const onSaveSearch = jest.fn();
      renderCommandBar({ selectedIds: [], onSaveSearch });
      openOverflow();

      expectEnabled(overflowItem('Save Search'));
      fireEvent.click(overflowItem('Save Search'));

      expect(onSaveSearch).toHaveBeenCalledTimes(1);
    });
  });

  // ---- Column picker ----

  describe('column picker', () => {
    function openColumns() {
      fireEvent.click(screen.getByRole('button', { name: 'Choose columns' }));
    }

    it('lists every column as a checkbox item', () => {
      renderCommandBar({ columns: COLUMNS });
      openColumns();

      COLUMNS.forEach(col => {
        expect(screen.getByRole('menuitemcheckbox', { name: col.displayName })).toBeInTheDocument();
      });
    });

    it('checks visible columns and leaves hidden columns unchecked', () => {
      renderCommandBar({ columns: COLUMNS, hiddenColumns: new Set(['similarity']) });
      openColumns();

      expect(screen.getByRole('menuitemcheckbox', { name: 'Name' })).toHaveAttribute('aria-checked', 'true');
      expect(screen.getByRole('menuitemcheckbox', { name: 'Modified' })).toHaveAttribute('aria-checked', 'true');
      expect(screen.getByRole('menuitemcheckbox', { name: 'Similarity' })).toHaveAttribute('aria-checked', 'false');
    });

    it('reports the new hidden set when a visible column is unchecked', () => {
      const onHiddenColumnsChange = jest.fn();
      renderCommandBar({ columns: COLUMNS, hiddenColumns: new Set<string>(), onHiddenColumnsChange });
      openColumns();

      fireEvent.click(screen.getByRole('menuitemcheckbox', { name: 'Similarity' }));

      expect(onHiddenColumnsChange).toHaveBeenCalledTimes(1);
      expect(Array.from(onHiddenColumnsChange.mock.calls[0][0] as Set<string>)).toEqual(['similarity']);
    });

    it('reports an emptied hidden set when a hidden column is checked again', () => {
      const onHiddenColumnsChange = jest.fn();
      renderCommandBar({ columns: COLUMNS, hiddenColumns: new Set(['similarity']), onHiddenColumnsChange });
      openColumns();

      fireEvent.click(screen.getByRole('menuitemcheckbox', { name: 'Similarity' }));

      expect(onHiddenColumnsChange).toHaveBeenCalledTimes(1);
      expect((onHiddenColumnsChange.mock.calls[0][0] as Set<string>).size).toBe(0);
    });

    it('preserves other hidden columns when one more is hidden', () => {
      const onHiddenColumnsChange = jest.fn();
      renderCommandBar({ columns: COLUMNS, hiddenColumns: new Set(['modified']), onHiddenColumnsChange });
      openColumns();

      fireEvent.click(screen.getByRole('menuitemcheckbox', { name: 'Name' }));

      expect(Array.from(onHiddenColumnsChange.mock.calls[0][0] as Set<string>).sort()).toEqual(['modified', 'name']);
    });
  });
});
