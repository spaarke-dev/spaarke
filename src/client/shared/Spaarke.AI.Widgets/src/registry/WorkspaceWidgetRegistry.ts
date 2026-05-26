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
 * @param type     - Unique string key matching the server-sent widget type.
 * @param metadata - Display metadata (displayName, category, defaultOrder, …).
 * @param factory  - Dynamic import factory: `() => import('./MyWidget')`.
 */
export function registerWorkspaceWidget(
  type: string,
  metadata: WidgetMetadata,
  factory: () => Promise<{ default: WorkspaceWidgetComponent }>
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
  _registry.set(type, { metadata, factory });
}

/**
 * Replace an existing workspace widget registration.
 *
 * Use this in tests or for feature-flag-driven widget swaps. Unlike
 * registerWorkspaceWidget(), this always overwrites the existing entry and
 * clears the resolved component cache so the new factory is used on next call.
 *
 * @param type     - Widget type string to replace.
 * @param metadata - New metadata.
 * @param factory  - New lazy factory.
 */
export function replaceWorkspaceWidget(
  type: string,
  metadata: WidgetMetadata,
  factory: () => Promise<{ default: WorkspaceWidgetComponent }>
): void {
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
export async function resolveWorkspaceWidget(
  type: string
): Promise<WorkspaceWidgetComponent> {
  const entry = _registry.get(type);

  // Unknown type — fall back to GenericTextWidget.
  if (!entry) {
    console.warn(
      `[ai-widgets] WorkspaceWidgetRegistry: unknown widget type "${type}". ` +
        'Falling back to GenericTextWidget.'
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
      `[ai-widgets] WorkspaceWidgetRegistry: failed to load widget "${type}". ` +
        'Falling back to GenericTextWidget.',
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
