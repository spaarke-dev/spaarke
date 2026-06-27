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
export { WorkspaceShell } from './WorkspaceShell';
export type { WorkspaceShellProps } from './WorkspaceShell';

export { SectionPanel } from './SectionPanel';
export type { SectionPanelProps } from './SectionPanel';

export { ActionCard } from './ActionCard';
export type { ActionCardProps } from './ActionCard';

export { ActionCardRow } from './ActionCardRow';
export type { ActionCardRowProps } from './ActionCardRow';

export { MetricCard } from './MetricCard';
export type { MetricCardProps } from './MetricCard';

export { MetricCardRow } from './MetricCardRow';
export type { MetricCardRowProps } from './MetricCardRow';

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
} from './types';

// Layout templates (workspace personalization)
export { LAYOUT_TEMPLATES, getLayoutTemplate } from './layoutTemplates';
export type { LayoutTemplate, LayoutTemplateId, LayoutTemplateRow } from './layoutTemplates';

// Section metadata catalog (R4 W-3 / task 040, 2026-05-26).
// Single source of truth for static section metadata (id, label, icon, ...).
// Consumed by WorkspaceLayoutWizard's section picker AND validated against
// LegalWorkspace's `SECTION_REGISTRY` at dev-mode module load. Eliminates the
// pre-R4 drift where the wizard's hardcoded `SECTION_CATALOG` lagged behind
// the dashboard's registry (Calendar + Daily Briefing missing from picker).
export { SECTION_METADATA_CATALOG, SECTION_METADATA_IDS, getSectionMetadata } from './sectionMetadataCatalog';
export type { SectionMetadata } from './sectionMetadataCatalog';

// Dynamic workspace config builder (hoisted in task 067 from LegalWorkspace)
export { buildDynamicWorkspaceConfig, SYSTEM_DEFAULT_LAYOUT_JSON } from './buildDynamicWorkspaceConfig';
export type { LayoutJson, LayoutJsonRow, WorkspaceScope } from './buildDynamicWorkspaceConfig';

// Style hooks (optional, for advanced consumers)
export {
  useWorkspaceShellStyles,
  useSectionContentPaddingStyles,
  useToolbarDividerStyles,
} from './WorkspaceShell.styles';

// Daily Briefing section — REMOVED from @spaarke/ui-components in R2.1 hotfix
// (2026-06-19). The old narrative-only DailyBriefingSection + useDailyBriefing
// hook + createDailyBriefingRegistration factory have been deleted; the full
// DailyBriefingApp (TL;DR + Activity Notes + per-item actions) is now mounted
// by the new factory at @spaarke/daily-briefing-components/widgets,
// closing the Pattern D dual-use gap left by R2 task 018. Consumers (the
// LegalWorkspace shim) now import the factory + TELEMETRY_EVENT_DAILY_BRIEFING_429
// + NarrateRequest from @spaarke/daily-briefing-components/widgets.
// See projects/spaarke-daily-update-service-r2/notes/option-d-registry-as-composition.md
// and the R2.1 hotfix commit for full context.

// Wizard launchers (hoisted in Round 4 Fix 2 / task 085 — see file header).
// Shared Xrm.Navigation.navigateTo wrappers for the seven Get Started wizards.
// Reused by SpaarkeAi's ContextPaneController; LegalWorkspace's WorkspaceGrid
// continues to use its own local handlers for FR-25 byte-stability.
export {
  launchCreateMatterWizard,
  launchCreateProjectWizard,
  launchSummarizeFilesWizard,
  launchFindSimilarWizard,
  launchAssignWorkWizard,
  launchPlaybookIntent,
} from './wizardLaunchers';
export type {
  BaseLauncherOptions,
  SummarizeFilesLauncherOptions,
  FindSimilarLauncherOptions,
  PlaybookIntentLauncherOptions,
} from './wizardLaunchers';
