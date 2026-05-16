/**
 * @spaarke/ai-outputs — Widget Type Contracts
 *
 * Enums, generic prop types, and registry entry shapes shared across all
 * output and source pane widgets. Tasks 020, 021, 022, and 031 all reference
 * these types.
 *
 * NOT PCF-safe — requires React 19.
 */

import type React from "react";

// ---------------------------------------------------------------------------
// Widget identity enums
// ---------------------------------------------------------------------------

/**
 * Identifies an output pane widget by its rendered type.
 * Values are string-keyed so they survive JSON serialisation in SSE payloads.
 */
export enum OutputWidgetType {
  // Wave 2 (task 020) — widgets 1-4
  BudgetDashboard = "BudgetDashboard",
  SearchResults = "SearchResults",
  AnalysisEditor = "AnalysisEditor",
  ContractComparison = "ContractComparison",

  // Wave 2 (task 021) — widgets 5-8
  Timeline = "Timeline",
  DocumentCompare = "DocumentCompare",
  DataTable = "DataTable",
  Chart = "Chart",

  // Wave 3 (task 031) — widgets 9-11
  StatusSummary = "StatusSummary",
  Recommendation = "Recommendation",
  ActionPlan = "ActionPlan",
}

/**
 * Identifies a source pane widget by its rendered type.
 * Populated by Wave 2 task 022.
 */
export enum SourceWidgetType {
  DocumentViewer = "DocumentViewer",
  WebSource = "WebSource",
  LegalLibrary = "LegalLibrary",
  Citation = "Citation",
  ImageViewer = "ImageViewer",
  CodeViewer = "CodeViewer",
}

// ---------------------------------------------------------------------------
// Generic widget prop contracts
// ---------------------------------------------------------------------------

/**
 * Base props for all output pane widget components.
 *
 * @template T - The shape of the widget's data payload, parsed from the AI
 *               streaming response before being handed to the widget.
 *
 * @example
 * interface BudgetData { title: string; items: BudgetItem[] }
 * type BudgetWidgetProps = OutputWidgetProps<BudgetData>;
 */
export interface OutputWidgetProps<T> {
  /** Parsed widget payload delivered via the AI streaming response. */
  data: T;
  /** When true the widget should render a loading skeleton instead of data. */
  isLoading?: boolean;
  /** Human-readable error message — widget renders an error state when set. */
  error?: string;
  /** Optional class name for root element overrides (mergeClasses compatible). */
  className?: string;
}

/**
 * Base props for all source pane widget components.
 *
 * @template T - The shape of the source widget's data payload.
 */
export interface SourceWidgetProps<T> {
  /** Parsed widget payload delivered via the AI streaming response. */
  data: T;
  /** When true the widget should render a loading skeleton instead of data. */
  isLoading?: boolean;
  /** Human-readable error message — widget renders an error state when set. */
  error?: string;
  /** Optional class name for root element overrides (mergeClasses compatible). */
  className?: string;
}

// ---------------------------------------------------------------------------
// Registry entry types
// ---------------------------------------------------------------------------

/**
 * Registry entry for a lazily-loaded widget component.
 *
 * @template T - The widget's prop type (e.g. OutputWidgetProps<BudgetData>).
 *
 * @example
 * const entry: WidgetRegistryEntry<OutputWidgetProps<BudgetData>> = {
 *   type: OutputWidgetType.BudgetDashboard,
 *   label: "Budget Dashboard",
 *   factory: () => import('./output-widgets/BudgetDashboardWidget'),
 * };
 */
export interface WidgetRegistryEntry<T> {
  /** String key matching the enum value used as the registry key. */
  type: string;
  /** Human-readable display label shown in widget pickers and debug UI. */
  label: string;
  /**
   * Lazy factory function that returns the module containing a `default` export
   * of the widget component. Uses dynamic import() to enable code splitting.
   *
   * The module must have a default-exported React component that accepts props
   * of type T.
   */
  factory: () => Promise<{ default: React.ComponentType<T> }>;
}
