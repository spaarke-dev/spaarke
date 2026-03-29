/**
 * WorkspaceShell — barrel exports for the workspace layout component family.
 *
 * Components:
 *   - WorkspaceShell: top-level declarative layout container
 *   - SectionPanel: titled bordered section card
 *   - ActionCard / ActionCardRow: square action cards (Get Started pattern)
 *   - MetricCard / MetricCardRow: square metric cards (Quick Summary pattern)
 *
 * Types:
 *   - WorkspaceConfig, WorkspaceRowConfig, WorkspaceLayoutVariant
 *   - SectionConfig (union), ActionCardSectionConfig, MetricCardSectionConfig, ContentSectionConfig
 *   - ActionCardConfig, MetricCardConfig
 *   - MetricTrend, MetricBadgeVariant
 *
 * Style hooks (for consumers who need to extend or reuse):
 *   - useWorkspaceShellStyles
 *   - useSectionContentPaddingStyles
 *   - useToolbarDividerStyles
 */

// Components
export { WorkspaceShell } from "./WorkspaceShell";
export type { WorkspaceShellProps } from "./WorkspaceShell";

export { SectionPanel } from "./SectionPanel";
export type { SectionPanelProps } from "./SectionPanel";

export { ActionCard } from "./ActionCard";
export type { ActionCardProps } from "./ActionCard";

export { ActionCardRow } from "./ActionCardRow";
export type { ActionCardRowProps } from "./ActionCardRow";

export { MetricCard } from "./MetricCard";
export type { MetricCardProps } from "./MetricCard";

export { MetricCardRow } from "./MetricCardRow";
export type { MetricCardRowProps } from "./MetricCardRow";

// Configuration types
export type {
  WorkspaceConfig,
  WorkspaceRowConfig,
  WorkspaceLayoutVariant,
  SectionConfig,
  ActionCardSectionConfig,
  MetricCardSectionConfig,
  ContentSectionConfig,
  ActionCardConfig,
  MetricCardConfig,
  MetricTrend,
  MetricBadgeVariant,
  SectionType,
  // Section Registry types (workspace personalization)
  SectionCategory,
  SectionFactoryContext,
  SectionRegistration,
  NavigateTarget,
} from "./types";

// Layout templates (workspace personalization)
export { LAYOUT_TEMPLATES, getLayoutTemplate } from "./layoutTemplates";
export type {
  LayoutTemplate,
  LayoutTemplateId,
  LayoutTemplateRow,
} from "./layoutTemplates";

// Style hooks (optional, for advanced consumers)
export {
  useWorkspaceShellStyles,
  useSectionContentPaddingStyles,
  useToolbarDividerStyles,
} from "./WorkspaceShell.styles";
