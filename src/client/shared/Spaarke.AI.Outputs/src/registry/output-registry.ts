/**
 * @spaarke/ai-outputs — Output Widget Registry
 *
 * Two complementary registration APIs:
 *
 * 1. LAZY RECORD (outputWidgetRegistry) — a static Record<OutputWidgetType, factory>
 *    populated at module load time via dynamic import(). Each factory is called at
 *    render time to obtain the widget component. Zero eager imports.
 *
 * 2. IMPERATIVE MAP (registerOutputWidget / getOutputWidget / …) — a runtime Map
 *    used by the pane orchestration layer to register widgets with metadata such as
 *    displayName, defaultOrder, and allowMultiple.
 *
 * NOT PCF-safe — requires React 19.
 *
 * Usage (lazy record):
 *   import { outputWidgetRegistry, resolveOutputWidget } from "@spaarke/ai-outputs";
 *   const Comp = await resolveOutputWidget(OutputWidgetType.BudgetDashboard);
 *   return <Comp data={...} />;
 *
 * Usage (imperative):
 *   registerOutputWidget({
 *     type: OutputWidgetType.BudgetDashboard,
 *     displayName: "Budget Dashboard",
 *     componentFactory: outputWidgetRegistry[OutputWidgetType.BudgetDashboard],
 *     defaultOrder: 10,
 *   });
 */

import type React from 'react';
import type { OutputWidgetRegistryEntry, OutputWidgetRegistryMap, OutputWidgetProps } from '../types';
// OutputWidgetType is used as a value (Record key) so we import it without 'type'
import { OutputWidgetType } from '../types';

// ---------------------------------------------------------------------------
// Internal registry store
// ---------------------------------------------------------------------------

const _registry: OutputWidgetRegistryMap = new Map();

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * Register an output pane widget.
 *
 * @param entry - Registry entry describing the widget and its component factory.
 * @throws {Error} If a widget with the same type is already registered.
 */
export function registerOutputWidget(entry: OutputWidgetRegistryEntry): void {
  if (_registry.has(entry.type)) {
    throw new Error(
      `[ai-outputs] Output widget type "${entry.type}" is already registered. ` +
        'Use replaceOutputWidget() to override an existing registration.'
    );
  }
  _registry.set(entry.type, entry);
}

/**
 * Replace an existing output widget registration.
 * Use this in tests or for feature-flag-driven widget swaps.
 *
 * @param entry - New registry entry (must have same type as the one being replaced).
 */
export function replaceOutputWidget(entry: OutputWidgetRegistryEntry): void {
  _registry.set(entry.type, entry);
}

/**
 * Retrieve a registered output widget by type.
 *
 * @param type - The OutputWidgetType to look up.
 * @returns The registry entry, or undefined if not registered.
 */
export function getOutputWidget(type: OutputWidgetType): OutputWidgetRegistryEntry | undefined {
  return _registry.get(type);
}

/**
 * Check whether an output widget type is registered.
 *
 * @param type - The OutputWidgetType to check.
 */
export function hasOutputWidget(type: OutputWidgetType): boolean {
  return _registry.has(type);
}

/**
 * Return all registered output widgets, sorted by defaultOrder (ascending).
 * Widgets without a defaultOrder are placed at the end.
 */
export function getAllOutputWidgets(): OutputWidgetRegistryEntry[] {
  return Array.from(_registry.values()).sort((a, b) => {
    const orderA = a.defaultOrder ?? Number.MAX_SAFE_INTEGER;
    const orderB = b.defaultOrder ?? Number.MAX_SAFE_INTEGER;
    return orderA - orderB;
  });
}

/**
 * Unregister all output widgets.
 * Intended for use in tests — do not call in production code.
 */
export function clearOutputRegistry(): void {
  _registry.clear();
}

// ---------------------------------------------------------------------------
// Lazy output widget registry (Record-based, no eager imports)
// ---------------------------------------------------------------------------

/**
 * Lazy factory type: returns a module with a default-exported widget component.
 * The component accepts OutputWidgetProps<unknown> — callers cast to their
 * specific data type when invoking the resolved component.
 */
type OutputWidgetFactory = () => Promise<{
  default: React.ComponentType<OutputWidgetProps<unknown>>;
}>;

/**
 * Creates a "not yet implemented" stub factory for widget types owned by
 * future tasks. Throws a descriptive error at runtime if called before
 * the owning task adds the real implementation.
 */
function _notYetImplemented(widgetType: string, ownerTask: string): OutputWidgetFactory {
  return () =>
    Promise.reject(
      new Error(
        `[ai-outputs] Widget "${widgetType}" is not yet implemented. ` + `It will be added by task ${ownerTask}.`
      )
    );
}

/**
 * Static mapping of every OutputWidgetType to a lazy import() factory.
 *
 * Rules:
 * - NO widget module is imported at the top of this file (zero eager imports).
 * - Widgets 1-4 (this task) use dynamic import() factories.
 * - Widgets 5-8 (task 021) and 9-11 (task 031) use stub factories that throw
 *   at runtime until those tasks replace them with real import() factories.
 *
 * @example
 * const factory = outputWidgetRegistry[OutputWidgetType.BudgetDashboard];
 * const { default: BudgetWidget } = await factory();
 * return <BudgetWidget data={budgetData} />;
 */
export const outputWidgetRegistry: Record<OutputWidgetType, OutputWidgetFactory> = {
  // Wave 2, task 020 — widgets 1-4 (implemented in this task)
  [OutputWidgetType.BudgetDashboard]: () =>
    import('../output-widgets/BudgetDashboardWidget') as Promise<{
      default: React.ComponentType<OutputWidgetProps<unknown>>;
    }>,
  [OutputWidgetType.SearchResults]: () =>
    import('../output-widgets/SearchResultsWidget') as Promise<{
      default: React.ComponentType<OutputWidgetProps<unknown>>;
    }>,
  [OutputWidgetType.AnalysisEditor]: () =>
    import('../output-widgets/AnalysisEditorWidget') as Promise<{
      default: React.ComponentType<OutputWidgetProps<unknown>>;
    }>,
  [OutputWidgetType.ContractComparison]: () =>
    import('../output-widgets/ContractComparisonWidget') as Promise<{
      default: React.ComponentType<OutputWidgetProps<unknown>>;
    }>,

  // Wave 2, task 021 — widgets 5-8 (stubs replaced by AIPU-021)
  [OutputWidgetType.Timeline]: _notYetImplemented('Timeline', 'AIPU-021'),
  [OutputWidgetType.DocumentCompare]: _notYetImplemented('DocumentCompare', 'AIPU-021'),
  [OutputWidgetType.DataTable]: _notYetImplemented('DataTable', 'AIPU-021'),
  [OutputWidgetType.Chart]: _notYetImplemented('Chart', 'AIPU-021'),

  // Wave 3, task 031 — widgets 9-11 (implemented by AIPU-031)
  [OutputWidgetType.StatusSummary]: () =>
    import('../output-widgets/StatusSummaryWidget') as Promise<{
      default: React.ComponentType<OutputWidgetProps<unknown>>;
    }>,
  [OutputWidgetType.Recommendation]: () =>
    import('../output-widgets/RecommendationWidget') as Promise<{
      default: React.ComponentType<OutputWidgetProps<unknown>>;
    }>,
  [OutputWidgetType.ActionPlan]: () =>
    import('../output-widgets/ActionPlanWidget') as Promise<{
      default: React.ComponentType<OutputWidgetProps<unknown>>;
    }>,
};

/**
 * Resolve an output widget component by type.
 *
 * Calls the lazy factory from outputWidgetRegistry and returns the default
 * export (the widget React component). Throws if the type has no factory.
 *
 * @param type - The OutputWidgetType to resolve.
 * @returns The widget component (default export of its module).
 *
 * @example
 * const BudgetWidget = await resolveOutputWidget(OutputWidgetType.BudgetDashboard);
 * return <BudgetWidget data={budgetData} />;
 */
export async function resolveOutputWidget(
  type: OutputWidgetType
): Promise<React.ComponentType<OutputWidgetProps<unknown>>> {
  const factory = outputWidgetRegistry[type];
  if (!factory) {
    throw new Error(`[ai-outputs] No output widget factory registered for type "${type}".`);
  }
  const module = await factory();
  return module.default;
}
