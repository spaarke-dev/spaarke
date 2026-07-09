/**
 * wizardRegistry.ts
 * Central dispatch table for Visual Host's "+" (Create) button.
 *
 * Visual Host stays agnostic to specific wizard implementations (design.md §5.3,
 * §6.2): it never imports `CreateEventWizard`/`CreateInvoiceWizard`/etc. directly.
 * Instead it calls `resolveWizard(key, fallbackEntity)` to get a lazily-loaded
 * component conforming to `WizardHostProps`, and mounts whatever comes back
 * (or shows a toast when the result is `null`).
 *
 * Adding a new wizard = one new entry in `wizardRegistry` below. No other
 * Visual Host code changes.
 *
 * @see WorkAssignmentWizardDialog.tsx (`IWorkAssignmentWizardDialogProps`) — the
 *      prop-shape model for `WizardHostProps`.
 * @see .claude/adr/ADR-012-shared-component-library.md — context-agnostic shared UI.
 * @see projects/visual-host-create-button-r1/design.md §5.3, §5.5.
 */
import * as React from 'react';

import type { AssociationResult } from '../AssociateToStep/types';
import type { IDataService, INavigationService } from '../../types/serviceInterfaces';
import type { AuthenticatedFetchFn } from '../../services/EntityCreationService';

// ---------------------------------------------------------------------------
// WizardHostProps — common injection contract for every registered wizard
// ---------------------------------------------------------------------------

/**
 * The common prop contract every wizard registered in `wizardRegistry` accepts.
 * Modeled on `IWorkAssignmentWizardDialogProps`
 * (`CreateWorkAssignmentWizard/WorkAssignmentWizardDialog.tsx`) — the canonical,
 * dependency-injected wizard shape used across the shared library (no
 * solution-specific imports; Dataverse/BFF access only via injected services).
 */
export interface WizardHostProps {
  /** Whether the wizard dialog is open. */
  open: boolean;
  /** Called when the wizard is closed/cancelled. */
  onClose: () => void;
  /** IDataService for Dataverse operations. */
  dataService: IDataService;
  /** Authenticated fetch function for BFF API calls. */
  authenticatedFetch: AuthenticatedFetchFn;
  /** BFF API base URL (e.g. "https://spe-api-dev-67e2xz.azurewebsites.net/api"). */
  bffBaseUrl: string;
  /** Optional navigation service for opening entity records post-create. */
  navigationService?: INavigationService;
  /** Resolves the SPE container ID for file uploads. If omitted, uploads are skipped. */
  resolveSpeContainerId?: () => Promise<string>;
  /** AAD tenant ID, forwarded for post-upload RAG indexing. Omitted → indexing skipped. */
  tenantId?: string;
  /**
   * When `true`, the wizard relies on the host's modal chrome for the title
   * bar and close button (no internal title rendered).
   */
  embedded?: boolean;
  /**
   * Launch-context seed (design.md §5.5) — the host record the wizard was
   * opened from, e.g. `{ entityType: hostEntityLogicalName, recordId, recordName }`.
   * Read from `context.mode.contextInfo` by the Visual Host dispatcher.
   */
  initialAssociation?: AssociationResult;
  /**
   * `true` when launched from Visual Host: the wizard hides/locks step 1
   * (Associate To) and treats `initialAssociation` as fixed, since the parent
   * is unambiguous. `false`/omitted leaves the step visible for other launch
   * contexts (design.md §5.5).
   */
  lockAssociation?: boolean;
}

// ---------------------------------------------------------------------------
// WizardComponent — the lazy-loaded component type stored in the registry
// ---------------------------------------------------------------------------

export type WizardComponent = React.LazyExoticComponent<React.ComponentType<WizardHostProps>>;

/**
 * Wraps `React.lazy()` and asserts the resolved default export as a
 * `WizardComponent`.
 *
 * Individual wizard prop interfaces (e.g. `ICreateEventWizardProps`) evolve on
 * their own migration timelines and are not required to structurally match
 * `WizardHostProps` field-for-field at every point in time (optionality of
 * `authenticatedFetch`/`resolveSpeContainerId` differs today, for example).
 * The registry is the single dispatch boundary that guarantees every caller
 * supplies a `WizardHostProps`-shaped object; each wizard reads the subset of
 * fields it needs. The assertion is centralized here (rather than repeated
 * per entry) so the rationale lives in exactly one place.
 */
function toWizardComponent<P>(factory: () => Promise<{ default: React.ComponentType<P> }>): WizardComponent {
  return React.lazy(factory) as unknown as WizardComponent;
}

// ---------------------------------------------------------------------------
// wizardRegistry — key -> lazy-loaded wizard component
// ---------------------------------------------------------------------------

/**
 * Registry keys are **dev-defined**: makers enter one of these strings into
 * `sprk_createwizardkey` on the chart-definition record (or leave it blank to
 * fall back to `sprk_entitylogicalname`, see `resolveWizard`). Valid keys today:
 *
 *   - `event`
 *   - `invoice`
 *   - `report-card`
 *
 * PLACEHOLDER NOTE (task 011, visual-host-create-button-r1): `CreateInvoiceWizard`
 * and `CreateReportCardWizard` don't exist yet — they ship in tasks 030 and 040
 * respectively. Until then, `invoice` and `report-card` point at the local
 * `PlaceholderWizard` (a minimal "not yet available" dialog conforming to
 * `WizardHostProps`) so the registry has a real module to import — pointing
 * `lazy()` at a not-yet-created file path would break every build that touches
 * this module. Tasks 030/040 each replace their one line here with the real
 * `lazy(() => import('../CreateInvoiceWizard/CreateInvoiceWizard'))` /
 * `lazy(() => import('../CreateReportCardWizard/CreateReportCardWizard'))`
 * import — `PlaceholderWizard` itself is left in place for any future entry
 * that needs the same bridge.
 *
 * RENAME NOTE (2026-07-08, owner decision post-Phase-0): the third wizard
 * targets `sprk_reportcard` (a Matter/Project-regarding review artifact),
 * NOT `sprk_kpiassessment` directly. `sprk_kpiassessment` records are
 * line-items that belong to a `sprk_reportcard` (via `sprk_kpiassessment.
 * sprk_reportcard`); adding KPI line-items to a report card is a separate,
 * later capability. The registry key was renamed from the original
 * `kpi-assessment` to `report-card` accordingly.
 */
export const wizardRegistry: Record<string, WizardComponent> = {
  event: toWizardComponent(() => import('../CreateEventWizard/CreateEventWizard')),
  invoice: toWizardComponent(() => import('../CreateInvoiceWizard/CreateInvoiceWizard')),
  'report-card': toWizardComponent(() => import('../CreateReportCardWizard/CreateReportCardWizard')),
};

// ---------------------------------------------------------------------------
// Entity fallback normalization
// ---------------------------------------------------------------------------

/**
 * Entity logical names whose registry key isn't a plain `sprk_` prefix-strip
 * of the logical name (e.g. `sprk_reportcard` -> `report-card`).
 *
 * Both `sprk_reportcard` and `sprk_kpiassessment` fall back to `report-card`:
 * a Visual Host visual bound to either entity should default to launching the
 * Report Card creation flow (see the RENAME NOTE above) unless the maker sets
 * an explicit `sprk_createwizardkey`.
 */
const ENTITY_TO_WIZARD_KEY: Readonly<Record<string, string>> = {
  sprk_event: 'event',
  sprk_invoice: 'invoice',
  sprk_reportcard: 'report-card',
  sprk_kpiassessment: 'report-card',
};

const SPRK_PREFIX = 'sprk_';

/**
 * Normalizes a Dataverse entity logical name (e.g. `sprk_event`) to a
 * `wizardRegistry` key (e.g. `event`). Falls back to stripping the `sprk_`
 * prefix for any entity not in the explicit alias map.
 */
function normalizeEntityToWizardKey(entityLogicalName: string): string {
  const lower = entityLogicalName.trim().toLowerCase();
  if (!lower) return '';
  const aliased = ENTITY_TO_WIZARD_KEY[lower];
  if (aliased) return aliased;
  return lower.startsWith(SPRK_PREFIX) ? lower.slice(SPRK_PREFIX.length) : lower;
}

// ---------------------------------------------------------------------------
// resolveWizard
// ---------------------------------------------------------------------------

/**
 * Resolves a `wizardRegistry` entry for the Visual Host "+" button.
 *
 * Resolution order (design.md §5.1, spec.md FR-03):
 *   1. `key` (`sprk_createwizardkey`), if non-empty — used verbatim.
 *   2. Otherwise `fallbackEntity` (`sprk_entitylogicalname`), normalized
 *      (`sprk_event` -> `event`, `sprk_kpiassessment` -> `kpi-assessment`, ...).
 *   3. If neither resolves to a registered key, returns `null` — the caller
 *      shows a toast ("No create wizard registered for {key}") rather than
 *      crashing.
 */
export function resolveWizard(
  key: string | null | undefined,
  fallbackEntity: string | null | undefined
): WizardComponent | null {
  const trimmedKey = key?.trim();
  const resolvedKey = trimmedKey ? trimmedKey : fallbackEntity ? normalizeEntityToWizardKey(fallbackEntity) : '';

  if (!resolvedKey) return null;
  return wizardRegistry[resolvedKey] ?? null;
}
