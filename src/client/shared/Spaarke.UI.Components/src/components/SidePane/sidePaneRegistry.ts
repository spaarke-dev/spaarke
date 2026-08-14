/**
 * sidePaneRegistry — the code-owned static registration table for
 * `SprkSidePaneHost` contributors (spaarke-side-pane-navigation-history-r1
 * task 011, spec FR-01).
 *
 * One entry = one right-rail icon = one contributor. A contributor registers
 * itself here (typically at module-load time, in its own entry-point file)
 * and `SprkSidePaneHost` renders the sorted rail + the active contributor's
 * component — the host never hardcodes a contributor's identity.
 *
 * Modeled on the two existing shared-lib registry precedents (root CLAUDE.md
 * §11 — extend a proven shape rather than invent a third one):
 *  - `WorkspaceWidgetRegistry` (@spaarke/ai-widgets) contributes the
 *    lazy-factory + resolve/cache ergonomics (a contributor's component code
 *    is only loaded when first selected).
 *  - `surfaceLaunchRegistry` (this package) contributes the plain
 *    code-owned static table + a `resolve*` accessor that degrades to
 *    `undefined` for an unknown id (never throws) — surface identity stays
 *    in code, not maker-authored data.
 *
 * Genuinely new here (root CLAUDE.md §11 justification): neither precedent
 * drives a right-rail ICON STRIP — `WorkspaceWidgetRegistry` resolves a
 * server-sent widget type inside an already-open workspace tab;
 * `surfaceLaunchRegistry` maps a one-shot launch to a surface. This registry
 * is the first "always-visible icon per registration" contributor table.
 */

import type * as React from 'react';

/**
 * Props every `SprkSidePaneHost` contributor component receives.
 *
 * Deliberately minimal (ADR-012 context-agnostic components) — a contributor
 * that needs more (e.g. Navigator's capture feed) reads it via its own
 * services/hooks, not through this contract. `paneId` is passed through so a
 * contributor can address its own pane-scoped storage/messaging without
 * hardcoding the host's pane id.
 */
export interface SidePaneContributorProps {
  /** The `Xrm.App.sidePanes` pane id the host registered. */
  paneId: string;
}

/** A contributor's component, resolved from its lazy factory. */
export type SidePaneContributorComponent = React.ComponentType<SidePaneContributorProps>;

/**
 * One registry entry: how to render a single right-rail icon + its content.
 *
 * `component` is a lazy factory (`WorkspaceWidgetRegistry` precedent) so a
 * contributor's code is not pulled into the initial pane bundle until the
 * user actually selects its rail icon.
 */
export interface SidePaneRegistryEntry {
  /** Unique contributor id (also the rail icon's React key + a11y label key). */
  readonly id: string;
  /**
   * Rail icon element (from `@fluentui/react-icons`). Typed as
   * `React.ReactElement` (not `React.ReactNode`) so it composes into the
   * Fluent v9 `Button` icon slot without a cast — the same reason
   * `HeaderToolbar`'s `IHeaderToolbarSlot.icon` uses this type (v9 slots
   * reject `ReactNode` unions like `bigint`, relevant per ADR-022's
   * shared-library React-version-drift note).
   */
  readonly icon: React.ReactElement;
  /** Rail icon tooltip + contributor pane title. */
  readonly title: string;
  /** Sort order for the rail strip (ascending). Ties break on registration order. */
  readonly order: number;
  /** Lazy factory returning the contributor's default-exported component. */
  readonly component: () => Promise<{ default: SidePaneContributorComponent }>;
}

/** Internal registration record — adds the resolved-component cache. */
interface SidePaneRegistration extends SidePaneRegistryEntry {
  resolved?: SidePaneContributorComponent;
}

const _registry = new Map<string, SidePaneRegistration>();

/**
 * Register a side-pane contributor.
 *
 * Duplicate registrations are silently ignored (first registration wins,
 * matching `registerWorkspaceWidget`'s "first wins" — a dev-mode console
 * warning helps catch accidental double-registration without throwing in
 * production). Use {@link replaceSidePaneContributor} to override explicitly
 * (tests, feature-flag swaps).
 */
export function registerSidePaneContributor(entry: SidePaneRegistryEntry): void {
  if (_registry.has(entry.id)) {
    if (process.env.NODE_ENV !== 'production') {
      console.warn(
        `[ui-components] sidePaneRegistry: contributor "${entry.id}" is already registered. ` +
          'The existing registration is kept. Use replaceSidePaneContributor() to override.'
      );
    }
    return;
  }
  _registry.set(entry.id, { ...entry });
}

/**
 * Replace an existing side-pane contributor registration (always overwrites;
 * clears the resolved-component cache so the new factory is used next time).
 * Intended for tests and feature-flag-driven contributor swaps.
 */
export function replaceSidePaneContributor(entry: SidePaneRegistryEntry): void {
  _registry.set(entry.id, { ...entry });
}

/** Look up a single registry entry (metadata only) by id, or `undefined`. */
export function getSidePaneRegistryEntry(id: string): SidePaneRegistryEntry | undefined {
  const reg = _registry.get(id);
  if (!reg) return undefined;
  // Strip the internal `resolved` cache field from the public shape.
  const { resolved: _resolved, ...entry } = reg;
  return entry;
}

/** Whether a contributor id is registered. */
export function hasSidePaneContributor(id: string): boolean {
  return _registry.has(id);
}

/**
 * All registered entries, sorted by `order` ascending. Ties preserve
 * registration (Map insertion) order — `Array.prototype.sort` is stable.
 */
export function listSidePaneRegistryEntries(): SidePaneRegistryEntry[] {
  return Array.from(_registry.values())
    .map(({ resolved: _resolved, ...entry }) => entry)
    .sort((a, b) => a.order - b.order);
}

/**
 * Resolve a contributor's component by id.
 *
 * - Calls the registered lazy factory on first resolution, then caches.
 * - Returns `undefined` for an unknown id — this registry does NOT fall back
 *   to a generic placeholder component (unlike `WorkspaceWidgetRegistry`,
 *   which must always render something inside an already-open tab). Here the
 *   HOST decides what "unknown id" looks like (a benign empty state) per the
 *   task's negative acceptance criterion — never throws either way.
 * - Returns `undefined` if the factory itself throws/rejects (logs the error).
 */
export async function resolveSidePaneContributor(id: string): Promise<SidePaneContributorComponent | undefined> {
  const reg = _registry.get(id);
  if (!reg) return undefined;

  if (reg.resolved !== undefined) {
    return reg.resolved;
  }

  try {
    const mod = await reg.component();
    reg.resolved = mod.default;
    return reg.resolved;
  } catch (err) {
    console.error(`[ui-components] sidePaneRegistry: failed to load contributor "${id}".`, err);
    return undefined;
  }
}

/**
 * Clear all registrations and resolved-component caches. Test-only — do not
 * call in production code (mirrors `clearWorkspaceRegistry`).
 */
export function clearSidePaneRegistry(): void {
  _registry.clear();
}
