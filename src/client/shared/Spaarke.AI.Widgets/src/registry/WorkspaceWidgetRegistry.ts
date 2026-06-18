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
import type { WidgetMetadata } from '../types/shared';
// Pillar 9 widget-visibility serialization contract (task 071, FR-55). The
// registry's optional `getVisibleState` field returns a variant of this
// discriminated union; see RegistryGetAgentVisibleState below for the full
// rationale on why the registry's signature is data-in rather than the
// zero-arg `GetAgentVisibleState` shape from `SerializedWidgetState.ts`.
import type { SerializedWidgetState } from '../types/SerializedWidgetState';

// ---------------------------------------------------------------------------
// Pillar 9 visibility extension (task 072, D-C-27)
// ---------------------------------------------------------------------------

/**
 * Registry-level signature for the Pillar 9 widget-visibility opt-in.
 *
 * **Why this differs from `GetAgentVisibleState`** (in `SerializedWidgetState.ts`):
 * the canonical `GetAgentVisibleState` is a zero-arg closure each widget
 * INSTANCE owns — it captures its own state via closure scope and returns
 * its agent-visible projection. That signature is the contract the prompt
 * builder calls per-tab at chat-turn time.
 *
 * The REGISTRY entry, however, is global + stateless — a single registration
 * record serves every tab of that widget type. So the registry's signature
 * takes the tab's `widgetData` payload as input and returns the serialized
 * variant. The prompt builder calls this with the live tab's `widgetData`
 * to produce the per-tab visible state. This is the natural pure-derivation
 * shape for a stateless registration record.
 *
 * Both signatures honor the FR-55 / ADR-015 privacy default — returning
 * `null` (or omitting the registration field entirely) means the widget
 * contributes NOTHING to the agent prompt for that tab. Opting in is an
 * explicit author decision.
 *
 * Per FR-55 + the `GetAgentVisibleState` JSDoc, implementations MUST:
 *   - Return a variant whose `widgetType` matches the parent tab's
 *     `widgetType` from `WorkspaceTab.ts`.
 *   - Be PURE and SYNCHRONOUS — the prompt builder calls this on every chat
 *     turn; async work would block user-perceived latency.
 *   - Self-limit to the per-tab token budget (~200 tokens per tab) by
 *     truncating long fields like `summary` / `tldr` / `selectionText`.
 *
 * @see SerializedWidgetState.ts — discriminated union + per-variant rationale
 * @see FR-55 — nullable opt-out + compact representation
 * @see FR-56 — existing registrations continue to work; visibility opt-in
 *      NOT retrofitted automatically (this field is OPTIONAL)
 * @see ADR-015 — privacy default (omit method or return null to opt out)
 *
 * @param widgetData The tab's per-variant `widgetData` payload (typed as
 *                   `unknown` at the registry boundary because the registry
 *                   stores heterogeneous widget types in one Map; consumers
 *                   narrow via the returned `widgetType` discriminator).
 * @returns A `SerializedWidgetState` variant when the widget opts in for the
 *          given data, or `null` to opt out for this invocation.
 */
export type RegistryGetAgentVisibleState = (widgetData: unknown) => SerializedWidgetState | null;

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
   * **Pillar 9 widget-visibility opt-in (task 072, D-C-27).** OPTIONAL field
   * — registrations that omit it contribute NOTHING to the per-turn agent
   * prompt (privacy default per ADR-015 + FR-56). Existing registrations
   * MUST continue to compile unchanged — that's the opt-in invariant.
   *
   * When supplied, the Pillar 9 prompt builder (task 074) calls this with
   * the live tab's `widgetData` to produce the agent-visible state slice
   * that goes into the system-prompt snapshot. Pillar 9 enforces the
   * `visibleToAssistant === true` gate at the tab level BEFORE calling this
   * — so omitting the method AND `visibleToAssistant === false` are
   * equivalent for the agent (both contribute nothing).
   *
   * @see RegistryGetAgentVisibleState above for the binding contract.
   * @see FR-55 — `getAgentVisibleState()` returns compact + schema-typed +
   *      nullable representation
   * @see FR-56 — existing widget registrations continue to work; visibility
   *      opt-in NOT retrofitted automatically
   */
  getVisibleState?: RegistryGetAgentVisibleState;
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
 * Pillar 9 visibility opt-in (task 072, D-C-27): pass the optional
 * `getVisibleState` derivation to expose the widget's agent-visible state to
 * the Pillar 9 prompt builder. Omitting the argument keeps the widget
 * invisible to the agent (privacy default per ADR-015 + FR-56).
 *
 * @param type            - Unique string key matching the server-sent widget type.
 * @param metadata        - Display metadata (displayName, category, defaultOrder, …).
 * @param factory         - Dynamic import factory: `() => import('./MyWidget')`.
 * @param getVisibleState - Optional Pillar 9 agent-visibility derivation; see
 *                          `RegistryGetAgentVisibleState`. Omit to opt out.
 */
export function registerWorkspaceWidget(
  type: string,
  metadata: WidgetMetadata,
  factory: () => Promise<{ default: WorkspaceWidgetComponent }>,
  getVisibleState?: RegistryGetAgentVisibleState
): void {
  if (_registry.has(type)) {
    if (process.env.NODE_ENV !== 'production') {
      console.warn(
        `[ai-widgets] WorkspaceWidgetRegistry: type "${type}" is already registered. ` +
          'The existing registration is kept. Use replaceWorkspaceWidget() to override.'
      );
    }
    return;
  }
  _registry.set(type, { metadata, factory, getVisibleState });
}

/**
 * Replace an existing workspace widget registration.
 *
 * Use this in tests or for feature-flag-driven widget swaps. Unlike
 * registerWorkspaceWidget(), this always overwrites the existing entry and
 * clears the resolved component cache so the new factory is used on next call.
 *
 * Pillar 9 visibility opt-in (task 072): see `registerWorkspaceWidget` JSDoc.
 *
 * @param type            - Widget type string to replace.
 * @param metadata        - New metadata.
 * @param factory         - New lazy factory.
 * @param getVisibleState - Optional Pillar 9 agent-visibility derivation; see
 *                          `RegistryGetAgentVisibleState`. Omit to opt out.
 */
export function replaceWorkspaceWidget(
  type: string,
  metadata: WidgetMetadata,
  factory: () => Promise<{ default: WorkspaceWidgetComponent }>,
  getVisibleState?: RegistryGetAgentVisibleState
): void {
  _registry.set(type, { metadata, factory, getVisibleState });
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
 * Retrieve the Pillar 9 agent-visibility derivation for a registered
 * workspace widget type (task 072 / D-C-27).
 *
 * The Pillar 9 prompt builder (task 074) iterates Assistant-visible tabs and
 * calls this accessor with the tab's `widgetType` registry string. If the
 * registration opted in (i.e. passed `getVisibleState` to
 * `registerWorkspaceWidget`), the returned function is invoked with the tab's
 * live `widgetData` to produce the agent-visible state slice. If the
 * registration opted out (omitted the field) — or the widget type is unknown —
 * this accessor returns `undefined` and the prompt builder contributes
 * nothing for that tab (privacy default per ADR-015 + FR-56).
 *
 * @param type - Widget type string.
 * @returns The registration's `getVisibleState` if registered AND opted in,
 *          otherwise `undefined`.
 */
export function getWorkspaceWidgetVisibleStateFn(
  type: string
): RegistryGetAgentVisibleState | undefined {
  return _registry.get(type)?.getVisibleState;
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

// Re-export WidgetMetadata from its canonical source (shared.ts) for callers
// that import metadata types via the registry module.
export type { WidgetMetadata };
