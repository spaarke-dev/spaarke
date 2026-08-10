/**
 * widgetRegistry — the external module-host WorkspaceWidgetRegistry (task 012).
 *
 * Adopts the PRODUCTION `@spaarke/ai-widgets` WorkspaceWidgetRegistry PATTERN (see
 * `src/client/shared/Spaarke.AI.Widgets/src/registry/WorkspaceWidgetRegistry.ts`) — a registry of
 * descriptors, each with a lazy-import factory so widget bodies only load when first opened. This
 * module does NOT import that package (§11 justification, task 012 POML: "Yes for the registry
 * pattern (adopt it); No for a ready-made surface" — the ai-widgets package is Xrm/SpaarkeAi-
 * coupled and out of bounds for the broker-only external SPA). Extended beyond the base pattern
 * with `planes` + `requiredEntitlement` + `defaultForRoles` — the entitlement/role-defaulting
 * metadata this external, /me-driven registry needs that the internal pattern leaves to its host.
 *
 * Entitlement model (spec.md FR-01/FR-02, NFR-06):
 *   - `planes` is a hard gate — the plane(s) that may EVER see/open this widget, independent of
 *     `entitlements`.
 *   - `requiredEntitlement` (optional) is the module-level entitlement id from `/me.entitlements`
 *     (Tier-1). Omitted = entitled to everyone within `planes` (e.g. Messages).
 *   - `defaultForRoles` is the subset of `planes` for which the widget auto-opens as a default
 *     tab (workspace-shell-foundation.md "Role-defaulted widgets").
 * This is UX-honest gating only — NOT the security boundary (NFR-06); server-side Tier-1/Tier-2
 * enforcement lives in tasks 015/022.
 *
 * Actual data widgets are tasks 016+ — every definition here resolves to `PlaceholderWidgetBody`
 * (see that file) with the correct entitlement/role metadata already wired.
 */
import * as React from 'react';
import type { FluentIcon } from '@fluentui/react-icons';
import {
  ClipboardTaskListLtrRegular,
  LightbulbRegular,
  ChatMultipleRegular,
  LibraryRegular,
  FolderRegular,
  GavelRegular,
  TaskListSquareLtrRegular,
  DocumentFolderRegular,
  MoneyRegular,
  ShieldKeyholeRegular,
  DocumentAddRegular,
} from '@fluentui/react-icons';
import type { Plane, MeEntitlementsResponse } from '../api/me-client';
import type { WidgetBodyComponent } from './PlaceholderWidgetBody';

// ---------------------------------------------------------------------------
// Descriptor shape
// ---------------------------------------------------------------------------

export interface WidgetDefinition {
  /** Unique widget id — also the tab id. */
  id: string;
  /** Tab title + widget-library card title. */
  title: string;
  /** Fluent v9 icon (tab + library card). */
  icon: FluentIcon;
  /** Accessible label for the library card. */
  ariaLabel: string;
  /** One-line library card description. */
  description: string;
  /** Hard gate: the plane(s) that may ever see/open this widget. */
  planes: Plane[];
  /** Module-level entitlement id required from `/me.entitlements`. Omit = entitled to all `planes`. */
  requiredEntitlement?: string;
  /** Subset of `planes` for which this widget auto-opens as a default tab. */
  defaultForRoles: Plane[];
  /** Lazy factory resolving the widget body (task 016+ swaps this per-widget for the real body). */
  lazyLoader: () => Promise<{ default: WidgetBodyComponent }>;
}

// ---------------------------------------------------------------------------
// Registry
// ---------------------------------------------------------------------------

const placeholderLoader = (): Promise<{ default: WidgetBodyComponent }> =>
  import('./PlaceholderWidgetBody').then(m => ({ default: m.PlaceholderWidgetBody }));

// Outside-counsel (ciam) data widgets (task 016) — each a thin `<DataGrid configId=… />` binding
// (see `widgets/GridWidgetBody.tsx`), swapped in for the task-012 placeholder.
const projectsLoader = (): Promise<{ default: WidgetBodyComponent }> =>
  import('../widgets/ProjectsWidget').then(m => ({ default: m.ProjectsWidgetBody }));
const mattersLoader = (): Promise<{ default: WidgetBodyComponent }> =>
  import('../widgets/MattersWidget').then(m => ({ default: m.MattersWidgetBody }));
const workAssignmentsLoader = (): Promise<{ default: WidgetBodyComponent }> =>
  import('../widgets/WorkAssignmentsWidget').then(m => ({ default: m.WorkAssignmentsWidgetBody }));
const documentsLoader = (): Promise<{ default: WidgetBodyComponent }> =>
  import('../widgets/DocumentsWidget').then(m => ({ default: m.DocumentsWidgetBody }));
const invoicesLoader = (): Promise<{ default: WidgetBodyComponent }> =>
  import('../widgets/InvoicesWidget').then(m => ({ default: m.InvoicesWidgetBody }));

/**
 * All registered widgets. Role-default coverage (workspace-shell-foundation.md):
 *   - workforce: My Requests · Inventions · Messages · Policy Library
 *   - ciam (outside counsel): Projects · Matters · Work Assignments · Documents · Invoices
 *   - admin (core-user): Access Admin (+ Messages)
 * `nda` is registered but NOT a default — it is opened via the Quick Start "NDA Assessment" card
 * ("NDA is accessed via the Quick Start card, not a default widget" — workspace-shell-foundation.md).
 */
export const WIDGET_DEFINITIONS: WidgetDefinition[] = [
  // ── Workforce (internal employee) — Legal Front Door ──────────────────────
  {
    id: 'my-requests',
    title: 'My Requests',
    icon: ClipboardTaskListLtrRegular,
    ariaLabel: 'Open My Requests — track your submitted requests',
    description: 'Track your own requests and legal’s responses.',
    planes: ['workforce'],
    requiredEntitlement: 'legal-front-door',
    defaultForRoles: ['workforce'],
    lazyLoader: placeholderLoader,
  },
  {
    id: 'inventions',
    title: 'Inventions',
    icon: LightbulbRegular,
    ariaLabel: 'Open Inventions — your invention submissions',
    description: 'Your invention disclosures and their review stage.',
    planes: ['workforce'],
    requiredEntitlement: 'legal-front-door',
    defaultForRoles: ['workforce'],
    lazyLoader: placeholderLoader,
  },
  {
    id: 'policy-library',
    title: 'Policy Library',
    icon: LibraryRegular,
    ariaLabel: 'Open the Policy & Procedures library',
    description: 'Browse and read the current policies.',
    planes: ['workforce'],
    requiredEntitlement: 'policy-library',
    defaultForRoles: ['workforce'],
    lazyLoader: placeholderLoader,
  },
  {
    id: 'nda',
    title: 'NDA Assessment',
    icon: DocumentAddRegular,
    ariaLabel: 'Open NDA Assessment — upload and assess an NDA',
    description: 'Upload an NDA and get an AI compliance assessment.',
    planes: ['workforce'],
    requiredEntitlement: 'legal-front-door',
    defaultForRoles: [], // opened via the Quick Start card, not a default tab
    lazyLoader: placeholderLoader,
  },
  // ── Cross-plane — available to workforce + admin (not a ciam default) ─────
  {
    id: 'messages',
    title: 'Messages',
    icon: ChatMultipleRegular,
    ariaLabel: 'Open secure cross-boundary messages',
    description: 'One governed thread across the internal/external boundary.',
    planes: ['workforce', 'admin'],
    // No requiredEntitlement — available to everyone in the allowed planes.
    defaultForRoles: ['workforce', 'admin'],
    lazyLoader: placeholderLoader,
  },
  // ── Outside counsel (CIAM) — R1 Outside-Counsel surfaces re-hosted as widgets ──
  {
    id: 'projects',
    title: 'Projects',
    icon: FolderRegular,
    ariaLabel: 'Open Projects — engagements you are staffed on',
    description: 'Engagements and workstreams you are staffed on.',
    planes: ['ciam'],
    requiredEntitlement: 'assigned-work',
    defaultForRoles: ['ciam'],
    lazyLoader: projectsLoader,
  },
  {
    id: 'matters',
    title: 'Matters',
    icon: GavelRegular,
    ariaLabel: 'Open Matters — legal matters assigned to you',
    description: 'Legal matters assigned to your firm.',
    planes: ['ciam'],
    requiredEntitlement: 'assigned-work',
    defaultForRoles: ['ciam'],
    lazyLoader: mattersLoader,
  },
  {
    id: 'work-assignments',
    title: 'Work Assignments',
    icon: TaskListSquareLtrRegular,
    ariaLabel: 'Open Work Assignments — tasks assigned to you',
    description: 'Tasks the law department has assigned to you.',
    planes: ['ciam'],
    requiredEntitlement: 'assigned-work',
    defaultForRoles: ['ciam'],
    lazyLoader: workAssignmentsLoader,
  },
  {
    id: 'documents',
    title: 'Documents',
    icon: DocumentFolderRegular,
    ariaLabel: 'Open Documents — matter documents shared with you',
    description: 'Matter documents shared with you in the workspace.',
    planes: ['ciam'],
    requiredEntitlement: 'assigned-work',
    defaultForRoles: ['ciam'],
    lazyLoader: documentsLoader,
  },
  {
    id: 'invoices',
    title: 'Invoices',
    icon: MoneyRegular,
    ariaLabel: 'Open Invoices — invoices you have submitted',
    description: 'Invoices you have submitted and their status.',
    planes: ['ciam'],
    requiredEntitlement: 'assigned-work',
    defaultForRoles: ['ciam'],
    lazyLoader: invoicesLoader,
  },
  // ── Core-user admin (MDA) ──────────────────────────────────────────────────
  {
    id: 'admin',
    title: 'Access Admin',
    icon: ShieldKeyholeRegular,
    ariaLabel: 'Open Access Administration — grant and revoke access',
    description: 'Grant/revoke module + record access.',
    planes: ['admin'],
    requiredEntitlement: 'admin',
    defaultForRoles: ['admin'],
    lazyLoader: placeholderLoader,
  },
];

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/** Look up a widget definition by id. Returns undefined for an unknown/unregistered id. */
export function getWidgetDefinition(id: string): WidgetDefinition | undefined {
  return WIDGET_DEFINITIONS.find(d => d.id === id);
}

/**
 * The minimal shape the Tier-1 entitlement check needs — shared by widget definitions
 * (`WidgetDefinition`) and the Quick Start action catalog (`QuickStartPane.tsx`'s
 * `QuickStartActionDef`), so both gate against `/me` with the SAME rule instead of two
 * near-identical implementations (CLAUDE.md §11).
 */
export interface EntitlementGate {
  /** Hard gate: the plane(s) allowed to ever see/use this item. */
  planes: Plane[];
  /** Module-level entitlement id required from `/me.entitlements`. Omit = entitled to all `planes`. */
  requiredEntitlement?: string;
}

/**
 * Tier-1 entitlement check: the caller's plane must be in `gate.planes`, AND (if
 * `gate.requiredEntitlement` is set) the caller's `/me.entitlements` must include it.
 */
export function isEntitled(me: MeEntitlementsResponse, gate: EntitlementGate): boolean {
  if (!gate.planes.includes(me.plane)) return false;
  if (gate.requiredEntitlement && !me.entitlements.includes(gate.requiredEntitlement)) return false;
  return true;
}

/** Widget-definition-specific alias of `isEntitled` — kept for call-site readability. */
export function isWidgetEntitled(me: MeEntitlementsResponse, def: WidgetDefinition): boolean {
  return isEntitled(me, def);
}

/**
 * Entitlement-honest routability check by id. An unknown id is never entitled (negative:
 * unregistered ids are non-routable exactly like unentitled ones — both no-op at the open path).
 */
export function isEntitledToWidgetId(me: MeEntitlementsResponse, widgetId: string): boolean {
  const def = getWidgetDefinition(widgetId);
  return def ? isWidgetEntitled(me, def) : false;
}

/** The role-defaulted widget ids to open as tabs on workspace load, for this caller. */
export function defaultWidgetIdsFor(me: MeEntitlementsResponse): string[] {
  return WIDGET_DEFINITIONS.filter(d => isWidgetEntitled(me, d) && d.defaultForRoles.includes(me.plane)).map(d => d.id);
}

/** Widgets shown in the "Add widget" library for this caller — entitled ONLY (FR-02). */
export function libraryWidgetsFor(me: MeEntitlementsResponse): WidgetDefinition[] {
  return WIDGET_DEFINITIONS.filter(d => isWidgetEntitled(me, d));
}

// ---------------------------------------------------------------------------
// Lazy component resolution (cached — the factory runs at most once per id)
// ---------------------------------------------------------------------------

const lazyCache = new Map<string, React.LazyExoticComponent<WidgetBodyComponent>>();

/** Resolve (and cache) the `React.lazy` component for a widget definition. */
export function getWidgetLazyComponent(def: WidgetDefinition): React.LazyExoticComponent<WidgetBodyComponent> {
  let cached = lazyCache.get(def.id);
  if (!cached) {
    cached = React.lazy(def.lazyLoader);
    lazyCache.set(def.id, cached);
  }
  return cached;
}
