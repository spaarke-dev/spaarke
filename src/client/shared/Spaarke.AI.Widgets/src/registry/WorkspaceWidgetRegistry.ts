/**
 * @spaarke/ai-widgets — WorkspaceWidgetRegistry
 *
 * Lazy-factory registry for workspace pane widgets. Each widget type is
 * registered with a dynamic import() factory so its code is only loaded
 * when first requested. Resolved components are cached — the factory is
 * called at most once per type.
 *
 * Design:
 * - Register at module load time via registerWorkspaceWidget().
 * - Resolve at render time via resolveWorkspaceWidget() — always returns
 *   a component, never undefined and never throws.
 * - Unknown types return GenericTextWidget (safe fallback for the shell).
 *   This differs from ContextWidgetRegistry which returns null for unknowns,
 *   because the workspace pane must always render something for the user.
 *
 * React 19, NOT PCF-safe.
 *
 * @example
 * // At widget module load time:
 * registerWorkspaceWidget('document-summary', {
 *   displayName: 'Document Summary',
 *   category: 'document',
 *   defaultOrder: 10,
 * }, () => import('./widgets/DocumentSummaryWidget'));
 *
 * // At render time (WorkspacePane):
 * const Widget = await resolveWorkspaceWidget('document-summary');
 * return <Widget data={payload} widgetType="document-summary" />;
 */

import type React from 'react';
import type { WorkspaceWidgetComponent } from '../types/widget-types';
// Use the canonical WidgetMetadata from shared.ts (task AIPU2-071) — it is the
// richer definition with icon, required allowMultiple, and required defaultOrder.
import type {
  WidgetMetadata,
  WidgetContextType,
  WidgetAssistantContract,
  WidgetAssistantContractOptOut,
} from '../types/shared';
// FR-15 (task 050) — structural registration enforcement helpers.
import { isAssistantContractOptOut } from '../types/shared';
// Pillar 9 note (C-21, 2026-10-03): the client-side `getVisibleState` registry slot,
// `RegistryGetAgentVisibleState`, `getWorkspaceWidgetVisibleStateFn`, the per-widget
// `pillar9-visibility.ts` derivations and the `SerializedWidgetState` union were DELETED.
// They were never called in production: the BFF derives the agent-visible tab state itself
// from the raw `WorkspaceTab.widgetData` (`SprkChatAgentFactory.TryDeriveVisibleState` /
// `BuildWorkspaceStateBlock`) and never trusts a client serialization. That server path is
// the ONLY Pillar 9 enforcement point — change it there (owner decision 2026-10-03, #1112).

// ---------------------------------------------------------------------------
// Internal registration record
// ---------------------------------------------------------------------------

/**
 * Full registration record for a workspace widget.
 * Stored in the registry map; not exported — callers use the public API only.
 */
interface WorkspaceWidgetRegistration {
  /** Metadata describing the widget for UI display. */
  metadata: WidgetMetadata;
  /**
   * Lazy factory that returns the module containing the default-exported
   * widget component. Called at most once — subsequent calls return the cache.
   */
  factory: () => Promise<{ default: WorkspaceWidgetComponent }>;
  /**
   * Cached resolved component. Set after the first successful factory call.
   * Prevents redundant dynamic imports on every render.
   */
  resolved?: WorkspaceWidgetComponent;
}

// ---------------------------------------------------------------------------
// Internal registry store
// ---------------------------------------------------------------------------

/**
 * Maps widget type strings to their lazy registration records.
 * Populated by registerWorkspaceWidget() at module load time.
 */
const _registry = new Map<string, WorkspaceWidgetRegistration>();

/**
 * Cached reference to the GenericTextWidget component.
 * Loaded lazily on the first call to resolveWorkspaceWidget() for an unknown type.
 */
let _genericTextWidgetCache: WorkspaceWidgetComponent | null = null;

// ---------------------------------------------------------------------------
// GenericTextWidget loader (internal)
// ---------------------------------------------------------------------------

/**
 * Load and cache the GenericTextWidget component.
 * Called whenever resolveWorkspaceWidget() needs a fallback.
 */
async function _loadGenericTextWidget(): Promise<WorkspaceWidgetComponent> {
  if (_genericTextWidgetCache !== null) {
    return _genericTextWidgetCache;
  }
  const mod = await import('../widgets/GenericTextWidget');
  _genericTextWidgetCache = mod.default as WorkspaceWidgetComponent;
  return _genericTextWidgetCache;
}

// ---------------------------------------------------------------------------
// FR-15 (task 050) — Assistant-contract structural enforcement guard
// ---------------------------------------------------------------------------

/**
 * Runtime backstop for the FR-15 (task 050) registration contract.
 *
 * `WidgetMetadata.assistantContract` is a REQUIRED member (compile-time type
 * error if omitted from a typed literal), so a TypeScript caller cannot ship a
 * contract-less registration. This guard is the runtime half of the
 * belt-and-suspenders NFR-09 enforcement: it catches a dynamically-shaped
 * registration a compile-time type cannot cover — a plain object cast through
 * `as WidgetMetadata`, a JS caller, or a malformed field — and FAILS FAST so
 * an Assistant-contract-less widget cannot silently register.
 *
 * Every one of the four shared-lib registration sites plus SpaarkeAi's
 * `registerComposeWidget` funnels through `registerWorkspaceWidget` /
 * `replaceWorkspaceWidget`, so placing the guard here enforces all five sites
 * with one check.
 *
 * Throws when `assistantContract` is absent, is a blank-reason opt-out, or is a
 * structurally-malformed contract.
 */
function assertAssistantContractDeclared(type: string, metadata: WidgetMetadata): void {
  const contract: WidgetAssistantContract | WidgetAssistantContractOptOut | null | undefined =
    metadata?.assistantContract;

  if (contract === undefined || contract === null) {
    throw new Error(
      `[ai-widgets] WorkspaceWidgetRegistry: widget "${type}" is missing the REQUIRED ` +
        `assistantContract (FR-15). Declare a WidgetAssistantContract (overviewTools + ` +
        `perItemCards + interactionPattern) OR an explicit opt-out via ` +
        `assistantContractOptOut('<reason>'). Silent absence is not allowed.`
    );
  }

  if (isAssistantContractOptOut(contract)) {
    if (typeof contract.reason !== 'string' || contract.reason.trim().length === 0) {
      throw new Error(
        `[ai-widgets] WorkspaceWidgetRegistry: widget "${type}" declared an assistantContract ` +
          `opt-out with no reason (FR-15). An opt-out MUST document WHY the widget has no ` +
          `Assistant contract.`
      );
    }
    return;
  }

  // A positive contract — minimal structural validation (the compile-time type
  // already enforces the full shape for typed callers; this catches dynamic /
  // JS / cast callers passing a partial object).
  const c = contract as Partial<WidgetAssistantContract>;
  if (!Array.isArray(c.overviewTools) || !Array.isArray(c.perItemCards) || typeof c.interactionPattern !== 'string') {
    throw new Error(
      `[ai-widgets] WorkspaceWidgetRegistry: widget "${type}" declared a MALFORMED ` +
        `assistantContract (FR-15). A contract needs overviewTools[], perItemCards[], and a ` +
        `string interactionPattern — or use assistantContractOptOut('<reason>') to opt out.`
    );
  }
}

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * Register a workspace widget type with its metadata and lazy import factory.
 *
 * Call this at the top of your widget module (or in an entry-point barrel)
 * so the type is available before the WorkspacePane first renders.
 *
 * Duplicate registrations are silently ignored in production — the first
 * registration wins. A console warning is emitted in development.
 *
 * @param type            - Unique string key matching the server-sent widget type.
 * @param metadata        - Display metadata (displayName, category, defaultOrder, …).
 * @param factory         - Dynamic import factory: `() => import('./MyWidget')`.
 */
export function registerWorkspaceWidget(
  type: string,
  metadata: WidgetMetadata,
  factory: () => Promise<{ default: WorkspaceWidgetComponent }>
): void {
  // FR-15 (task 050): fail fast if the widget did not declare an Assistant
  // contract OR an explicit opt-out — checked BEFORE the first-wins early
  // return so a malformed registration always fails, never silently no-ops.
  assertAssistantContractDeclared(type, metadata);
  if (_registry.has(type)) {
    if (process.env.NODE_ENV !== 'production') {
      console.warn(
        `[ai-widgets] WorkspaceWidgetRegistry: type "${type}" is already registered. ` +
          'The existing registration is kept. Use replaceWorkspaceWidget() to override.'
      );
    }
    return;
  }
  _registry.set(type, { metadata, factory });
}

/**
 * Replace an existing workspace widget registration.
 *
 * Use this in tests or for feature-flag-driven widget swaps. Unlike
 * registerWorkspaceWidget(), this always overwrites the existing entry and
 * clears the resolved component cache so the new factory is used on next call.
 *
 * @param type            - Widget type string to replace.
 * @param metadata        - New metadata.
 * @param factory         - New lazy factory.
 */
export function replaceWorkspaceWidget(
  type: string,
  metadata: WidgetMetadata,
  factory: () => Promise<{ default: WorkspaceWidgetComponent }>
): void {
  // FR-15 (task 050): the replacement metadata is held to the same required
  // Assistant-contract enforcement as an initial registration.
  assertAssistantContractDeclared(type, metadata);
  _registry.set(type, { metadata, factory });
}

/**
 * Resolve a workspace widget component by type.
 *
 * - Calls the registered lazy factory on first resolution, then caches.
 * - Returns GenericTextWidget for unknown types (never returns null or throws).
 * - Returns GenericTextWidget if the factory throws (logs the error).
 *
 * @param type - Widget type string as sent by the server.
 * @returns Promise resolving to the widget component (never rejects).
 */
export async function resolveWorkspaceWidget(type: string): Promise<WorkspaceWidgetComponent> {
  const entry = _registry.get(type);

  // Unknown type — fall back to GenericTextWidget.
  if (!entry) {
    console.warn(
      `[ai-widgets] WorkspaceWidgetRegistry: unknown widget type "${type}". ` + 'Falling back to GenericTextWidget.'
    );
    return _loadGenericTextWidget();
  }

  // Cache hit — return the previously resolved component.
  if (entry.resolved !== undefined) {
    return entry.resolved;
  }

  // First resolution — call the factory.
  try {
    const mod = await entry.factory();
    entry.resolved = mod.default;
    return entry.resolved;
  } catch (err) {
    console.error(
      `[ai-widgets] WorkspaceWidgetRegistry: failed to load widget "${type}". ` + 'Falling back to GenericTextWidget.',
      err
    );
    return _loadGenericTextWidget();
  }
}

/**
 * Retrieve the metadata for a registered workspace widget type.
 *
 * @param type - Widget type string.
 * @returns The WidgetMetadata, or undefined if the type is not registered.
 */
export function getWorkspaceWidgetMetadata(type: string): WidgetMetadata | undefined {
  return _registry.get(type)?.metadata;
}

/**
 * Derive the complete widget-type → context-type map from the LIVE registry
 * (FR-08). This is NOT a second/parallel registry — every entry is read
 * directly off that widget's own `metadata.contextType`, so the map can
 * never drift from the per-widget declarations in the register-*.ts files
 * (§11 reuse-first: extend the existing registration shape, don't invent a
 * parallel one).
 *
 * A widget with no declared `contextType` maps to `undefined` — an honest
 * "none of the six values fit" (see `WidgetMetadata.contextType`), not a
 * gap in the map.
 *
 * @returns Record keyed by every currently-registered widget type string.
 */
export function getWidgetContextTypeMap(): Record<string, WidgetContextType | undefined> {
  const map: Record<string, WidgetContextType | undefined> = {};
  for (const [type, entry] of _registry.entries()) {
    map[type] = entry.metadata.contextType;
  }
  return map;
}

/**
 * Retrieve a registered widget's Assistant-contract metadata (FR-08 + FR-15
 * SHAPE), if it declared one.
 *
 * Returns `undefined` for widgets that have not declared a contract — task
 * 022 populates it for the in-scope widgets (grids, Daily Briefing/Calendar
 * via `'workspace'`, Email, Documents via `'document-viewer'`); task 050
 * makes the field required and adds the structural enforcement guard.
 *
 * @param type - Widget type string.
 */
export function getWidgetAssistantContract(type: string): WidgetAssistantContract | undefined {
  // FR-15 (task 050): `assistantContract` is now REQUIRED and may hold an
  // explicit opt-out marker. An opt-out means "this widget has no Assistant
  // contract" — so this accessor keeps returning `undefined` for it, exactly
  // as it did pre-050 for an omitted field. Every downstream consumer (the
  // FR-14/task-041 follow-on derivation, `getWidgetInteractionPattern` below,
  // the interaction-pattern invariant suite) already treats `undefined` as
  // "no contract", so the opt-out is transparent to them.
  const contract = _registry.get(type)?.metadata.assistantContract;
  if (contract === undefined || isAssistantContractOptOut(contract)) return undefined;
  return contract;
}

/**
 * Retrieve a registered widget's respond/direct/hybrid interaction pattern
 * (FR-13, task 040 — the SINGLE-SOURCED runtime read point for this field).
 *
 * This is a thin projection of `getWidgetAssistantContract(type)?.interactionPattern`
 * — it exists so callers that only need the pattern (the FR-14/task-041
 * follow-on derivation is the first one) have ONE canonical accessor to
 * import, rather than each reaching into the contract shape inline and
 * risking a scattered, re-derived, or hardcoded per-widget-type guess
 * (the exact anti-pattern FR-13/FR-14 close — see `AssistantInteractionPattern`'s
 * JSDoc in `types/shared.ts`). Task 041 MUST read the pattern through this
 * accessor (or `getWidgetAssistantContract`) — never re-encode
 * respond/direct/hybrid logic per widget type at the call site.
 *
 * Returns `undefined` for widgets that have not declared a contract, mirroring
 * `getWidgetAssistantContract`'s "no gap" semantics.
 *
 * @param type - Widget type string.
 */
export function getWidgetInteractionPattern(type: string): WidgetAssistantContract['interactionPattern'] | undefined {
  // Read through getWidgetAssistantContract so opt-out widgets (FR-15/task 050)
  // resolve to `undefined` here too — an opt-out has no interactionPattern.
  return getWidgetAssistantContract(type)?.interactionPattern;
}

/**
 * Return all registered workspace widget type strings.
 *
 * The order reflects insertion order (Map iteration order).
 *
 * @returns Array of registered type strings.
 */
export function getAllWorkspaceWidgetTypes(): string[] {
  return Array.from(_registry.keys());
}

/**
 * Check whether a workspace widget type is registered.
 *
 * @param type - Widget type string.
 */
export function hasWorkspaceWidget(type: string): boolean {
  return _registry.has(type);
}

/**
 * Clear all registrations and the GenericTextWidget cache.
 *
 * Intended for use in tests — do not call in production code.
 */
export function clearWorkspaceRegistry(): void {
  _registry.clear();
  _genericTextWidgetCache = null;
}

// ---------------------------------------------------------------------------
// Re-export registration type for external widget authors
// ---------------------------------------------------------------------------

export type { WorkspaceWidgetRegistration };

// Re-export WidgetMetadata (+ the closed WidgetContextType union, task 020,
// + the Assistant-contract SHAPE types, task 022) from their canonical
// source (shared.ts) for callers that import metadata types via the
// registry module.
export type { WidgetMetadata, WidgetContextType };
export type {
  WidgetAssistantContract,
  AssistantContractCard,
  AssistantInteractionPattern,
  AssistantCardLanding,
  // FR-15 (task 050) — the explicit opt-out marker type.
  WidgetAssistantContractOptOut,
} from '../types/shared';
export { OVERVIEW_QUERY_TOOL_NAME } from '../types/shared';
// FR-15 (task 050) — the opt-out factory + guard, re-exported so registration
// sites (incl. SpaarkeAi's registerComposeWidget) can declare a documented
// "no Assistant contract" without reaching past the registry barrel.
export { assistantContractOptOut, isAssistantContractOptOut } from '../types/shared';
