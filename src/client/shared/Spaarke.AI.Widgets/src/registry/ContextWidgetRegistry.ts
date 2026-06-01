/**
 * @spaarke/ai-widgets — ContextWidgetRegistry
 *
 * Lazy-factory registry for context pane widgets. Mirrors the pattern of
 * WorkspaceWidgetRegistry but with a null-return contract for unknown types.
 *
 * Design:
 * - Register at module load time via registerContextWidget().
 * - Resolve at render time via resolveContextWidget() — returns null for
 *   unknown types and factory failures (never throws).
 * - Unknown types return null because context widgets are always server-driven.
 *   An unknown type indicates a client/server version mismatch; the context
 *   pane renders nothing rather than a placeholder that could mislead the user.
 *
 * React 19, NOT PCF-safe.
 *
 * @example
 * // At widget module load time:
 * registerContextWidget('document-metadata', {
 *   factory: () => import('./widgets/DocumentMetadataWidget'),
 * });
 *
 * // At render time (ContextPaneController):
 * const Widget = await resolveContextWidget('document-metadata');
 * if (Widget) {
 *   return <Widget data={payload} widgetType="document-metadata" />;
 * }
 * // Widget is null — render nothing for this unknown type.
 */

import type { ContextWidgetComponent } from '../types/widget-types';

// ---------------------------------------------------------------------------
// Registration type
// ---------------------------------------------------------------------------

/**
 * Registration record for a context widget.
 * Context widgets carry only a factory (no metadata) — the context pane
 * is server-driven and does not need a widget picker UI.
 */
export interface ContextWidgetRegistration {
  /**
   * Lazy factory that returns the module containing the default-exported
   * context widget component. Called at most once per type.
   */
  factory: () => Promise<{ default: ContextWidgetComponent }>;
  /**
   * Cached resolved component. Set after the first successful factory call.
   */
  resolved?: ContextWidgetComponent;
}

// ---------------------------------------------------------------------------
// Internal registry store
// ---------------------------------------------------------------------------

/**
 * Maps context widget type strings to their lazy registration records.
 * Populated by registerContextWidget() at module load time.
 */
const _registry = new Map<string, ContextWidgetRegistration>();

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * Register a context widget type with its lazy import factory.
 *
 * Duplicate registrations are silently ignored in production — the first
 * registration wins. A console warning is emitted in development.
 *
 * @param type         - Unique string key matching the server-sent widget type.
 * @param registration - Registration object containing the lazy factory.
 */
export function registerContextWidget(type: string, registration: Pick<ContextWidgetRegistration, 'factory'>): void {
  if (_registry.has(type)) {
    if (process.env.NODE_ENV !== 'production') {
      console.warn(
        `[ai-widgets] ContextWidgetRegistry: type "${type}" is already registered. ` +
          'The existing registration is kept. Use replaceContextWidget() to override.'
      );
    }
    return;
  }
  _registry.set(type, { factory: registration.factory });
}

/**
 * Replace an existing context widget registration.
 *
 * Clears the resolved component cache so the new factory is used on next call.
 * Use this in tests or for feature-flag-driven widget swaps.
 *
 * @param type         - Widget type string to replace.
 * @param registration - New registration record.
 */
export function replaceContextWidget(type: string, registration: Pick<ContextWidgetRegistration, 'factory'>): void {
  _registry.set(type, { factory: registration.factory });
}

/**
 * Resolve a context widget component by type.
 *
 * - Calls the registered lazy factory on first resolution, then caches.
 * - Returns null for unknown types (logs a warning with the type name).
 * - Returns null if the factory throws (logs the error).
 * - Never throws — the context pane is always safe to call this without
 *   a try/catch.
 *
 * @param type - Context widget type string as sent by the server.
 * @returns Promise resolving to the widget component, or null.
 */
export async function resolveContextWidget(type: string): Promise<ContextWidgetComponent | null> {
  const entry = _registry.get(type);

  // Unknown type — return null with a warning.
  if (!entry) {
    console.warn(
      `[ai-widgets] ContextWidgetRegistry: unknown context widget type "${type}". ` +
        'This may indicate a client/server version mismatch. Returning null.'
    );
    return null;
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
      `[ai-widgets] ContextWidgetRegistry: failed to load context widget "${type}". ` + 'Returning null.',
      err
    );
    return null;
  }
}

/**
 * Check whether a context widget type is registered.
 *
 * @param type - Widget type string.
 */
export function hasContextWidget(type: string): boolean {
  return _registry.has(type);
}

/**
 * Return all registered context widget type strings.
 *
 * @returns Array of registered type strings (insertion order).
 */
export function getAllContextWidgetTypes(): string[] {
  return Array.from(_registry.keys());
}

/**
 * Clear all registrations.
 *
 * Intended for use in tests — do not call in production code.
 */
export function clearContextRegistry(): void {
  _registry.clear();
}
