/**
 * Side Pane Platform — Type Definitions
 *
 * Shared interfaces for the SidePaneManager core module.
 * These are compile-time only (no runtime output).
 *
 * Architecture: docs/architecture/SIDE-PANE-PLATFORM-ARCHITECTURE.md
 */

// ============================================================================
// Xrm API Type Declarations (subset for side pane registration)
// ============================================================================

/* eslint-disable @typescript-eslint/no-explicit-any */
declare const Xrm: any;
/* eslint-enable @typescript-eslint/no-explicit-any */

/** Options for Xrm.App.sidePanes.createPane() */
interface SidePaneCreateOptions {
  paneId: string;
  title: string;
  imageSrc?: string;
  canClose: boolean;
  width: number;
  isSelected?: boolean;
  hideHeader?: boolean;
  badge?: boolean;
  alwaysRender?: boolean;
}

/** Side pane instance returned by createPane() */
interface SidePane {
  paneId: string;
  title?: string;
  close(): void;
  select(): void;
  navigate(pageInput: SidePanePageInput): Promise<void>;
}

/** Page input for pane.navigate() — web resource variant */
interface SidePanePageInput {
  pageType: 'webresource';
  webresourceName: string;
  data?: string;
}

/** Xrm.App.sidePanes API */
interface AppSidePanes {
  state: 0 | 1;
  createPane(options: SidePaneCreateOptions): Promise<SidePane>;
  getPane(paneId: string): SidePane | undefined;
  getSelectedPane(): SidePane | undefined;
  getAllPanes(): SidePane[];
}

// ============================================================================
// Pane Configuration
// ============================================================================

/**
 * Configuration for a side pane registered in the SidePaneManager.
 *
 * Each entry in PANE_REGISTRY uses this interface. Adding a new pane
 * to the platform requires only adding a PaneConfig entry and recompiling.
 */
interface PaneConfig {
  /** Unique ID for singleton behavior (e.g., "sprk-chat") */
  paneId: string;

  /** Display title in pane header and launcher tooltip */
  title: string;

  /** Web resource path for side pane launcher icon (e.g., "WebResources/sprk_Icon.svg") */
  icon: string;

  /** Code Page web resource name (e.g., "sprk_SprkChatPane") */
  webResource: string;

  /** Panel width in pixels (min 300, recommended 350-450) */
  width: number;

  /** false = always present in launcher (like Copilot); true = user can close */
  canClose: boolean;

  /** true = keeps React state alive when user switches to another pane tab */
  alwaysRender: boolean;

  /** true = passes current entity type/ID via URL params on navigate */
  contextAware: boolean;
}
