/**
 * SearchCommandBar — Power-Apps-OOB-style toolbar for the Semantic Search
 * Code Page.
 *
 * Layout (all items right-aligned; parent App.tsx row supplies the leading
 * spacer to push the toolbar to the right):
 *   [Refresh] [Columns] [Delete] [...overflow]
 *
 * Column picker is the SECOND toolbar item (not in the overflow) per
 * operator directive 2026-06-04. The overflow `...` Menu hosts secondary
 * actions: Email a Link / Open in Web / Open in Desktop / Download / Send to
 * Index / Save Search. Document-only commands remain domain-gated.
 *
 * The view tabs + visualization settings render as App.tsx siblings AFTER
 * this component (i.e. even further right), per the dataset grid command bar
 * pattern.
 *
 * @see spec.md Section 6.6 / FR-09 — command bar specification
 * @see docs/guides/DATAGRID-CODE-PAGE-HOST-CONTRACT.md — the dataset grid contract
 */

import React, { useCallback } from 'react';
import {
  makeStyles,
  tokens,
  Toolbar,
  ToolbarButton,
  Tooltip,
  Menu,
  MenuTrigger,
  MenuList,
  MenuItem,
  MenuPopover,
  MenuItemCheckbox,
} from '@fluentui/react-components';
import {
  DeleteRegular,
  ArrowClockwiseRegular,
  MailRegular,
  OpenRegular,
  DesktopRegular,
  ArrowDownloadRegular,
  DatabaseSearchRegular,
  SaveRegular,
  MoreHorizontalRegular,
  ColumnTriple20Regular,
} from '@fluentui/react-icons';
import type { SearchDomain } from '../types';
import type { IDatasetColumn } from '../hooks/useSearchViewDefinitions';

// =============================================
// Props
// =============================================

export interface SearchCommandBarProps {
  /** IDs of currently selected rows. */
  selectedIds: string[];
  /** Active search domain. */
  activeDomain: SearchDomain;
  /** Delete selected records. */
  onDelete: (ids: string[]) => void;
  /** Refresh search results. */
  onRefresh: () => void;
  /** Email a link to a single record. */
  onEmailLink: (id: string) => void;
  /** Open document in web (Documents only). */
  onOpenInWeb: (id: string) => void;
  /** Open document in desktop app (Documents only). */
  onOpenInDesktop: (id: string) => void;
  /** Download document (Documents only). */
  onDownload: (id: string) => void;
  /** Send documents to AI index (Documents only). */
  onSendToIndex: (ids: string[]) => void;
  /** Save current search to favorites. */
  onSaveSearch: () => void;

  /** Column definitions for the picker menu. */
  columns: IDatasetColumn[];
  /** Set of column names currently HIDDEN by the user. */
  hiddenColumns: Set<string>;
  /** Callback to update the hidden-column set. */
  onHiddenColumnsChange: (hidden: Set<string>) => void;
}

// =============================================
// Styles
// =============================================

const useStyles = makeStyles({
  // Toolbar (right-aligned by parent commandBar row). Holds all primary
  // actions inline: Refresh + Columns + Delete + overflow.
  toolbar: {
    columnGap: tokens.spacingHorizontalXS,
  },
});

// =============================================
// Component
// =============================================

export const SearchCommandBar: React.FC<SearchCommandBarProps> = ({
  selectedIds,
  activeDomain,
  onDelete,
  onRefresh,
  onEmailLink,
  onOpenInWeb,
  onOpenInDesktop,
  onDownload,
  onSendToIndex,
  onSaveSearch,
  columns,
  hiddenColumns,
  onHiddenColumnsChange,
}) => {
  const styles = useStyles();

  const hasSelection = selectedIds.length > 0;
  const isSingle = selectedIds.length === 1;
  const isDocumentDomain = activeDomain === 'documents';

  const handleDelete = useCallback(() => {
    if (hasSelection) onDelete(selectedIds);
  }, [hasSelection, onDelete, selectedIds]);

  const handleEmailLink = useCallback(() => {
    if (isSingle) onEmailLink(selectedIds[0]);
  }, [isSingle, onEmailLink, selectedIds]);

  const handleOpenInWeb = useCallback(() => {
    if (isSingle) onOpenInWeb(selectedIds[0]);
  }, [isSingle, onOpenInWeb, selectedIds]);

  const handleOpenInDesktop = useCallback(() => {
    if (isSingle) onOpenInDesktop(selectedIds[0]);
  }, [isSingle, onOpenInDesktop, selectedIds]);

  const handleDownload = useCallback(() => {
    if (isSingle) onDownload(selectedIds[0]);
  }, [isSingle, onDownload, selectedIds]);

  const handleSendToIndex = useCallback(() => {
    if (hasSelection) onSendToIndex(selectedIds);
  }, [hasSelection, onSendToIndex, selectedIds]);

  // Column picker — MenuItemCheckbox state shape
  const columnCheckedValues = React.useMemo(() => {
    const visible = columns.filter(col => !hiddenColumns.has(col.name)).map(col => col.name);
    return { columns: visible };
  }, [columns, hiddenColumns]);

  const handleColumnsCheckedChange = useCallback(
    (_ev: unknown, data: { name: string; checkedItems: string[] }) => {
      if (data.name !== 'columns') return;
      const visibleSet = new Set(data.checkedItems);
      const next = new Set<string>();
      for (const col of columns) {
        if (!visibleSet.has(col.name)) next.add(col.name);
      }
      onHiddenColumnsChange(next);
    },
    [columns, onHiddenColumnsChange]
  );

  return (
    <Toolbar className={styles.toolbar} size="small" aria-label="Search actions">
      <ToolbarButton icon={<ArrowClockwiseRegular />} onClick={onRefresh}>
        Refresh
      </ToolbarButton>

      {/* Column picker — SECOND toolbar item (per operator directive
          2026-06-04). Implemented as a Menu wrapping a ToolbarButton so the
          checkbox behavior works without leaving the Fluent v9 Toolbar's
          visual style. */}
      <Menu
        checkedValues={columnCheckedValues}
        onCheckedValueChange={handleColumnsCheckedChange}
      >
        <MenuTrigger disableButtonEnhancement>
          <ToolbarButton icon={<ColumnTriple20Regular />} aria-label="Choose columns">
            Columns
          </ToolbarButton>
        </MenuTrigger>
        <MenuPopover>
          <MenuList>
            {columns.map(col => (
              <MenuItemCheckbox key={col.name} name="columns" value={col.name}>
                {col.displayName}
              </MenuItemCheckbox>
            ))}
          </MenuList>
        </MenuPopover>
      </Menu>

      <Tooltip
        content={hasSelection ? 'Delete selected' : 'Select items to delete'}
        relationship="label"
      >
        <ToolbarButton icon={<DeleteRegular />} disabled={!hasSelection} onClick={handleDelete}>
          Delete
        </ToolbarButton>
      </Tooltip>

      {/* Overflow Menu — secondary actions */}
      <Menu>
        <MenuTrigger disableButtonEnhancement>
          <ToolbarButton icon={<MoreHorizontalRegular />} aria-label="More actions" />
        </MenuTrigger>
        <MenuPopover>
          <MenuList>
            <MenuItem
              icon={<MailRegular />}
              disabled={!isSingle}
              onClick={handleEmailLink}
            >
              Email a Link
            </MenuItem>

            {isDocumentDomain && (
              <>
                <MenuItem
                  icon={<OpenRegular />}
                  disabled={!isSingle}
                  onClick={handleOpenInWeb}
                >
                  Open in Web
                </MenuItem>
                <MenuItem
                  icon={<DesktopRegular />}
                  disabled={!isSingle}
                  onClick={handleOpenInDesktop}
                >
                  Open in Desktop
                </MenuItem>
                <MenuItem
                  icon={<ArrowDownloadRegular />}
                  disabled={!isSingle}
                  onClick={handleDownload}
                >
                  Download
                </MenuItem>
                <MenuItem
                  icon={<DatabaseSearchRegular />}
                  disabled={!hasSelection}
                  onClick={handleSendToIndex}
                >
                  Send to Index
                </MenuItem>
              </>
            )}

            <MenuItem icon={<SaveRegular />} onClick={onSaveSearch}>
              Save Search
            </MenuItem>
          </MenuList>
        </MenuPopover>
      </Menu>
    </Toolbar>
  );
};

export default SearchCommandBar;
