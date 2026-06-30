/**
 * sectionMetadataCatalog.ts — Single source of truth for workspace section
 * METADATA (no factories).
 *
 * Why this file exists (R4 W-3 / task 040, 2026-05-26):
 *   - `LegalWorkspace/src/sectionRegistry.ts` aggregates 7 `SectionRegistration`s
 *     (id, label, description, icon, category, defaultHeight, FACTORY).
 *   - `WorkspaceLayoutWizard/src/App.tsx` previously hardcoded a 5-entry
 *     `SECTION_CATALOG` constant — drifted from `SECTION_REGISTRY` (Calendar +
 *     Daily Briefing were not pickable in the wizard).
 *   - The wizard cannot directly import `SECTION_REGISTRY` because each
 *     registration's `factory` pulls heavy dependencies into the wizard bundle
 *     (`@spaarke/events-components`, `ActivityFeed`, `SmartToDo`, etc.) and
 *     `@spaarke/events-components` isn't even in the wizard's `package.json`.
 *
 * Resolution:
 *   - This file holds ONLY the static, lightweight metadata fields needed by
 *     both the wizard (for picker UI) AND the dashboard (for rendering).
 *   - LegalWorkspace's `sectionRegistry.ts` validates against this catalog at
 *     module load (dev mode) to detect drift early.
 *   - Adding a new section requires (1) a new entry HERE and (2) a new
 *     registration with matching `id`. Forgetting either trips the dev guard.
 *
 * Spec reference: R4 FR-01 (W-3). Establishes a single source of truth and
 * eliminates the "hardcoded SECTION_CATALOG" anti-pattern (R4 spec MUST NOT).
 *
 * Standards: ADR-012 (shared components), ADR-021 (Fluent v9 icons).
 */

import type { FluentIcon } from '@fluentui/react-icons';
import {
  RocketRegular,
  DataBarVerticalRegular,
  ClockRegular,
  CheckmarkCircleRegular,
  DocumentRegular,
  SparkleRegular,
  CalendarLtr24Regular,
  FolderRegular,
  ReceiptRegular,
  BriefcaseRegular,
  BriefcaseSearchRegular,
  EditRegular,
} from '@fluentui/react-icons';
import type { SectionCategory } from './types';

/**
 * Lightweight metadata for a workspace section. Strict subset of
 * `SectionRegistration` — id/label/description/icon/category/defaultHeight only.
 * NO factory. NO React node. Safe to import from any context (wizard, dashboard,
 * tests, docs tooling).
 */
export interface SectionMetadata {
  /** Unique section identifier — MUST match the corresponding `SectionRegistration.id`. */
  readonly id: string;
  /** Display name shown in wizard Step 2 + dashboard section header. */
  readonly label: string;
  /** One-line description shown in wizard Step 2. */
  readonly description: string;
  /** Fluent v9 icon component (ADR-021). */
  readonly icon: FluentIcon;
  /** Category for grouping in wizard Step 2. */
  readonly category: SectionCategory;
  /** Suggested default height (e.g. "560px"). Undefined = auto. */
  readonly defaultHeight?: string;
}

/**
 * Canonical, ordered metadata catalog for ALL workspace sections.
 *
 * Update protocol:
 *   1. Add a new entry to this array (in desired display order).
 *   2. Create a corresponding `SectionRegistration` in LegalWorkspace's
 *      `sections/{id}.registration.ts` with a matching `id`.
 *   3. Add the registration to `LegalWorkspace/src/sectionRegistry.ts`'s
 *      `SECTION_REGISTRY` array.
 *
 * The dev-mode guard in `sectionRegistry.ts` will warn if the two lists drift.
 *
 * NOTE on icons: each entry references the icon used by its registration. If
 * registrations change icons, update them here too. (A future refactor could
 * have registrations spread `...SECTION_METADATA[id]` to eliminate even this
 * duplication; deferred to keep this task surgical.)
 */
export const SECTION_METADATA_CATALOG: readonly SectionMetadata[] = [
  {
    id: 'get-started',
    label: 'Get Started',
    description: 'Quick-action cards for common workflows',
    category: 'overview',
    icon: RocketRegular,
    defaultHeight: '200px',
  },
  {
    id: 'quick-summary',
    label: 'Quick Summary',
    description: 'Key metrics at a glance',
    category: 'overview',
    icon: DataBarVerticalRegular,
  },
  {
    id: 'latest-updates',
    label: 'Latest Updates',
    description: 'Recent activity feed with flagging',
    category: 'data',
    icon: ClockRegular,
    defaultHeight: '325px',
  },
  {
    id: 'todo',
    label: 'My To Do List',
    description: 'Embedded smart to-do list with flag sync',
    category: 'productivity',
    icon: CheckmarkCircleRegular,
    defaultHeight: '560px',
  },
  {
    id: 'documents',
    label: 'My Documents',
    description: 'Your documents',
    category: 'data',
    icon: DocumentRegular,
    defaultHeight: '480px',
  },
  // ai-spaarke-ai-workspace-UI-r1 #4 (2026-06-08): three new entity-view
  // sections sharing <DataverseEntityViewWidget>. Each needs an operator-
  // created sprk_gridconfiguration row (see each registration file's
  // DEPLOYMENT REQUIREMENT note).
  {
    id: 'matters',
    label: 'Matters',
    description: 'Your matters',
    category: 'data',
    icon: BriefcaseSearchRegular,
    defaultHeight: '480px',
  },
  {
    id: 'projects',
    label: 'Projects',
    description: 'Your projects',
    category: 'data',
    icon: FolderRegular,
    defaultHeight: '480px',
  },
  {
    id: 'invoices',
    label: 'Invoices',
    description: 'Your invoices',
    category: 'data',
    icon: ReceiptRegular,
    defaultHeight: '480px',
  },
  {
    id: 'work-assignments',
    label: 'Work Assignments',
    description: 'Work assignments routed to you',
    category: 'data',
    icon: BriefcaseRegular,
    defaultHeight: '480px',
  },
  {
    id: 'daily-briefing',
    label: 'Daily Briefing',
    description: 'AI-curated highlights from your day',
    category: 'ai',
    icon: SparkleRegular,
    defaultHeight: '325px',
  },
  {
    id: 'calendar',
    label: 'Calendar',
    description: 'All events + tasks you have access to',
    category: 'data',
    icon: CalendarLtr24Regular,
    defaultHeight: '720px',
  },
  // spaarkeai-compose-r1 task 040 (2026-06-29): Compose editor section type.
  // Mounted by the "Compose" system workspace layout (sprk_workspacelayout row
  // c09d26be-e173-f111-ab0e-7ced8ddc4a05, task 010). Registration lives in
  // LegalWorkspace's sections/composeEditor.registration.ts. R1 placeholder
  // skeleton; task 042 (Phase 4) wires the TipTap editor widget.
  {
    id: 'compose-editor',
    label: 'Compose',
    description: 'TipTap editor for drafting and revising documents',
    category: 'productivity',
    icon: EditRegular,
    defaultHeight: '720px',
  },
] as const;

/** Look up section metadata by ID. Returns `undefined` if not found. */
export function getSectionMetadata(id: string): SectionMetadata | undefined {
  return SECTION_METADATA_CATALOG.find(m => m.id === id);
}

/** Stable set of all registered section IDs (derived). */
export const SECTION_METADATA_IDS: ReadonlySet<string> = new Set(SECTION_METADATA_CATALOG.map(m => m.id));
