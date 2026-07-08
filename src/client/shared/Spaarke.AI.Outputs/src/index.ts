/**
 * @spaarke/ai-outputs
 *
 * Shared AI output and source pane widgets, registries, and type definitions.
 *
 * NOT PCF-safe — this library requires React 19 and must NOT be imported
 * by PCF controls. Use only from Code Pages, solutions (Vite), or other
 * React 19 contexts.
 *
 * Consumption:
 *   // In a Code Page or Vite solution's package.json:
 *   "@spaarke/ai-outputs": "file:../../client/shared/Spaarke.AI.Outputs"
 *
 *   // Import types and widget components:
 *   import { CitationWidget, OutputWidgetType } from "@spaarke/ai-outputs";
 */

// Types — all widget contracts, registry entry shapes, SSE event types
export * from './types';

// NOTE (Track-B batch 3, ai-architecture-redesign-r1): the R1 widget
// registries and the CustomEvent-based pane-linking module were deleted —
// superseded by the canonical registries + PaneEventBus in @spaarke/ai-widgets.

// Widget modules — populated in Wave 2 and Wave 3
export * from './output-widgets';
export * from './source-widgets';

export * from './chat-history';
