/**
 * SearchCommandBar — Power-Apps-OOB-style unified command bar for the
 * Semantic Search Code Page.
 *
 * Layout (single row, left → right):
 *   [Refresh] [Delete] [...overflow] | [Columns] | divider | [ViewToggle tabs]
 *
 * The overflow menu hosts the secondary actions that don't fit the
 * "primary + tabs" Power Apps dataset-grid pattern: Email a Link,
 * Open in Web, Open in Desktop, Download, Send to Index, Save Search.
 * Document-only commands stay hidden for Matters/Projects/Invoices
 * domains (unchanged from prior version).
 *
 * Task 035 UI alignment (2026-06-04): operator directive to bring the
 * SemanticSearch toolbar in line with the Spaarke dataset grid command
 * bar style + move the column picker into the unified toolbar.
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
  Button,
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
  root: {
    display: 'flex',
    alignItems: 'center',
    width: '100%',
    columnGap: tokens.spacingHorizontalS,
  },
  primaryToolbar: {
    columnGap: tokens.spacingHorizontalXS,
  },
  spacer: {
    flex: 1,
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
    <div className={styles.root}>
      {/* Primary: Refresh + Delete inline */}
      <Toolbar className={styles.primaryToolbar} size="small" aria-label="Search actions">
        <ToolbarButton icon={<ArrowClockwiseRegular />} onClick={onRefresh}>
          Refresh
        </ToolbarButton>

        <Tooltip
          content={hasSelection ? 'Delete selected' : 'Select items to delete'}
          relationship="label"
        >
          <ToolbarButton icon={<DeleteRegular />} disabled={!hasSelection} onClick={handleDelete}>
            Delete
          </ToolbarButton>
        </Tooltip>

        {/* Overflow menu — secondary actions */}
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

      <div className={styles.spacer} />

      {/* Column picker — Menu of checkbox items. Right-aligned within the
          SearchCommandBar; view tabs + visualization settings render as
          App.tsx siblings after this component. */}
      <Menu
        checkedValues={columnCheckedValues}
        onCheckedValueChange={handleColumnsCheckedChange}
      >
        <MenuTrigger disableButtonEnhancement>
          <Button
            appearance="subtle"
            size="small"
            icon={<ColumnTriple20Regular />}
            aria-label="Choose columns"
          >
            Columns
          </Button>
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
    </div>
  );
};

export default SearchCommandBar;
