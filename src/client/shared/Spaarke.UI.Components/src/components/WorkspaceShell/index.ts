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

// Dynamic workspace config builder (hoisted in task 067 from LegalWorkspace)
export {
  buildDynamicWorkspaceConfig,
  SYSTEM_DEFAULT_LAYOUT_JSON,
} from "./buildDynamicWorkspaceConfig";
export type {
  LayoutJson,
  LayoutJsonRow,
  WorkspaceScope,
} from "./buildDynamicWorkspaceConfig";

// Style hooks (optional, for advanced consumers)
export {
  useWorkspaceShellStyles,
  useSectionContentPaddingStyles,
  useToolbarDividerStyles,
} from "./WorkspaceShell.styles";

// Daily Briefing section (hoisted in task 069 from LegalWorkspace).
// The hook + section component are context-agnostic — consumers supply
// `authenticatedFetch` and optionally `tenantId` / `onRateLimitError`.
// The registration is a FACTORY (not a static const) because consumer-supplied
// auth deps must close over the factory call; see comments in
// `dailyBriefing.registration.ts` for rationale.
export { useDailyBriefing } from "./sections/dailyBriefing/useDailyBriefing";
export type {
  DailyBriefingState,
  DailyBriefingError,
  UseDailyBriefingOptions,
} from "./sections/dailyBriefing/useDailyBriefing";

export {
  DailyBriefingSection,
  TELEMETRY_EVENT_DAILY_BRIEFING_429,
} from "./sections/dailyBriefing/DailyBriefingSection";
export type { DailyBriefingSectionProps } from "./sections/dailyBriefing/DailyBriefingSection";

export { createDailyBriefingRegistration } from "./sections/dailyBriefing/dailyBriefing.registration";
export type { CreateDailyBriefingRegistrationOptions } from "./sections/dailyBriefing/dailyBriefing.registration";
