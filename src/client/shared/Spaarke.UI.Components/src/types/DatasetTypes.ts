/**
 * Core types for Universal Dataset component
 */

export type ViewMode = "Grid" | "Card" | "List";
export type ThemeMode = "Auto" | "Spaarke" | "Host";
export type SelectionMode = "None" | "Single" | "Multiple";
export type ScrollBehavior = "Auto" | "Infinite" | "Paged";

export interface IDatasetRecord {
  id: string;
  entityName: string;
  [key: string]: any;
}

export interface IDatasetColumn {
  name: string;
  displayName: string;
  dataType: string;
  isKey?: boolean;
  isPrimary?: boolean;
  visualSizeFactor?: number;
  // Field-level security (column-level security)
  isSecured?: boolean;
  canRead?: boolean;
  canUpdate?: boolean;
  canCreate?: boolean;
}

export interface IDatasetConfig {
  viewMode: ViewMode;
  enableVirtualization: boolean;
  rowHeight: number;
  selectionMode: SelectionMode;
  showToolbar: boolean;
  enabledCommands: string[];
  theme: ThemeMode;
  scrollBehavior: ScrollBehavior;

  // Toolbar configuration
  compactToolbar?: boolean;           // Icon-only mode
  toolbarShowOverflow?: boolean;      // Enable overflow menu (default: true)
}
