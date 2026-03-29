/**
 * WorkspaceShell types — declarative configuration interfaces for workspace layout.
 *
 * Standards: ADR-012 (shared component library), ADR-021 (Fluent v9 tokens, dark mode)
 */

import type { FluentIcon } from "@fluentui/react-icons";

// ---------------------------------------------------------------------------
// Card configuration types
// ---------------------------------------------------------------------------

/** Configuration for a single action card in the "Get Started" row. */
export interface ActionCardConfig {
  /** Stable identifier used as React key. */
  id: string;
  /** Display label shown below the icon. */
  label: string;
  /** Fluent v9 icon component. */
  icon: FluentIcon;
  /** Accessible label for the card button — more descriptive than the visible label. */
  ariaLabel: string;
  /** Optional: when true the card renders in a non-interactive disabled state. */
  disabled?: boolean;
  /**
   * Optional click handler baked into the config.
   * Can be overridden at the row level via ActionCardSectionConfig.onCardClick.
   */
  onClick?: () => void;
}

/** Trend direction for metric cards. */
export type MetricTrend = "up" | "down" | "neutral";

/** Badge variant for metric cards. */
export type MetricBadgeVariant = "new" | "overdue";

/** Configuration for a single metric card in the "Quick Summary" row. */
export interface MetricCardConfig {
  /** Stable identifier used as React key. */
  id: string;
  /** Display label shown below the count. */
  label: string;
  /** Fluent v9 icon component. */
  icon: FluentIcon;
  /** Accessible label for the card button. */
  ariaLabel: string;
  /** The numeric value to display. undefined renders an em-dash while loading. */
  value?: number;
  /** When true, a Fluent Spinner is shown instead of the value. */
  isLoading?: boolean;
  /** Optional trend indicator direction. */
  trend?: MetricTrend;
  /** Optional badge variant ("new" → green, "overdue" → red). */
  badgeVariant?: MetricBadgeVariant;
  /** Badge count. Shown only when > 0 and not loading. */
  badgeCount?: number;
  /** Called when the card is clicked or activated via keyboard. */
  onClick?: () => void;
}

// ---------------------------------------------------------------------------
// Section panel configuration
// ---------------------------------------------------------------------------

/** Discriminated union for section type. */
export type SectionType = "action-cards" | "metric-cards" | "content";

/** Common fields shared by all section configurations. */
interface SectionConfigBase {
  /** Stable identifier used as React key. */
  id: string;
  /** Section title displayed in the header bar. */
  title: string;
  /** Optional badge count shown beside the title. */
  badgeCount?: number;
  /** Custom toolbar node rendered in the section header (right side). */
  toolbar?: React.ReactNode;
  /** Additional className applied to the outer section card container. */
  className?: string;
  /** Optional inline styles applied to the outer section card container. */
  style?: React.CSSProperties;
  /** Optional height override for the section panel (e.g. "560px"). */
  height?: string;
}

/** Section wrapping an ActionCardRow. */
export interface ActionCardSectionConfig extends SectionConfigBase {
  type: "action-cards";
  /** Action card definitions to render in the row. */
  cards: ActionCardConfig[];
  /** Map of card id → click handler (overrides ActionCardConfig.onClick). */
  onCardClick?: Partial<Record<string, () => void>>;
  /** Set of card ids to render in a disabled state. */
  disabledCards?: ReadonlySet<string>;
  /** Maximum number of cards to display. Default: show all. */
  maxVisible?: number;
}

/** Section wrapping a MetricCardRow. */
export interface MetricCardSectionConfig extends SectionConfigBase {
  type: "metric-cards";
  /** Metric card definitions to render in the row. */
  cards: MetricCardConfig[];
}

/** Generic content section that accepts arbitrary children via render prop. */
export interface ContentSectionConfig extends SectionConfigBase {
  type: "content";
  /**
   * Render prop that produces the section body content.
   * Using a render prop avoids storing React elements in plain config objects.
   */
  renderContent: () => React.ReactNode;
}

/** Discriminated union for all section configurations. */
export type SectionConfig =
  | ActionCardSectionConfig
  | MetricCardSectionConfig
  | ContentSectionConfig;

// ---------------------------------------------------------------------------
// WorkspaceShell configuration
// ---------------------------------------------------------------------------

/** Layout arrangement for sections within the shell. */
export type WorkspaceLayoutVariant =
  /** Stack all sections vertically (single column). */
  | "single-column"
  /** Arrange sections in rows defined by WorkspaceRowConfig. */
  | "rows";

/** A single row in the workspace layout. */
export interface WorkspaceRowConfig {
  /** Stable identifier for the row. */
  id: string;
  /** Section ids (from WorkspaceConfig.sections) to include in this row. */
  sectionIds: string[];
  /**
   * CSS grid-template-columns value for this row.
   * Defaults to equal columns: `repeat(${sectionIds.length}, 1fr)`.
   */
  gridTemplateColumns?: string;
  /**
   * Responsive override: grid-template-columns at max-width 767px.
   * Defaults to "1fr" (single column).
   */
  gridTemplateColumnsSmall?: string;
}

/**
 * Top-level declarative configuration for WorkspaceShell.
 *
 * Usage:
 * ```tsx
 * const config: WorkspaceConfig = {
 *   layout: "rows",
 *   rows: [
 *     { id: "row1", sectionIds: ["get-started", "quick-summary"] },
 *     { id: "row2", sectionIds: ["latest-updates"] },
 *     { id: "row3", sectionIds: ["todo", "documents"] },
 *   ],
 *   sections: [
 *     { id: "get-started", type: "action-cards", title: "Get Started", cards: [...] },
 *     // ...
 *   ],
 * };
 *
 * <WorkspaceShell config={config} />
 * ```
 */
export interface WorkspaceConfig {
  /** Layout variant. Use "rows" for multi-column arrangements. */
  layout: WorkspaceLayoutVariant;
  /** Row definitions (required when layout === "rows"). */
  rows?: WorkspaceRowConfig[];
  /** All section definitions referenced by rows or rendered in order (single-column). */
  sections: SectionConfig[];
}

// ---------------------------------------------------------------------------
// Section Registry types — used by dynamic workspace configuration
// ---------------------------------------------------------------------------

/** Category for grouping sections in the layout wizard Step 2 checklist. */
export type SectionCategory = "overview" | "data" | "ai" | "productivity";

/** Navigation target for sections that need to open Dataverse views or records. */
export type NavigateTarget =
  | { type: "view"; entity: string; viewId?: string }
  | { type: "record"; entity: string; id: string }
  | { type: "url"; url: string };

/** Options for opening a Code Page wizard dialog via Xrm.Navigation.navigateTo. */
export interface DialogOptions {
  /** Dialog width — value + unit (e.g., { value: 85, unit: "%" }). */
  width?: { value: number; unit: "%" | "px" };
  /** Dialog height — value + unit. */
  height?: { value: number; unit: "%" | "px" };
}

/**
 * Standard context passed to every section factory.
 * Sections must work with ONLY these dependencies — no bespoke parent wiring.
 */
export interface SectionFactoryContext {
  /** Xrm.WebApi for Dataverse queries. */
  webApi: unknown;
  /** Current user's systemuserid GUID. */
  userId: string;
  /** DataverseService for document/entity operations. */
  service: unknown;
  /** BFF API base URL (environment variable, BYOK-safe). */
  bffBaseUrl: string;
  /** Navigate to a Dataverse URL, view, or record. */
  onNavigate: (target: NavigateTarget) => void;
  /** Open a Code Page wizard dialog. */
  onOpenWizard: (
    webResourceName: string,
    data?: string,
    options?: DialogOptions,
  ) => void;
  /**
   * Register a badge count updater. The workspace header shows this
   * count on the section's tab. Call with updated count whenever data changes.
   */
  onBadgeCountChange: (count: number) => void;
  /**
   * Register a refetch function. The workspace calls this when the user
   * clicks a global refresh or when another section triggers a cross-refresh.
   */
  onRefetchReady: (refetch: () => void) => void;
}

/**
 * What the Section Registry knows about each section (metadata + factory).
 * This is the ONLY interface a section author needs to implement.
 */
export interface SectionRegistration {
  /** Unique section identifier (stored in Dataverse layout JSON). */
  id: string;
  /** Display name shown in wizard Step 2 checklist. */
  label: string;
  /** One-line description shown in wizard Step 2. */
  description: string;
  /** Fluent icon shown in wizard and section header. */
  icon: FluentIcon;
  /** Category for grouping in wizard Step 2. */
  category: SectionCategory;
  /** Suggested default height (e.g., "560px"). Undefined = auto. */
  defaultHeight?: string;
  /** Thumbnail preview for wizard Step 3 (optional static image or icon). */
  previewIcon?: FluentIcon;
  /**
   * Factory function that produces a SectionConfig for WorkspaceShell.
   * Receives a standardized context — no bespoke props.
   */
  factory: (context: SectionFactoryContext) => SectionConfig;
}
