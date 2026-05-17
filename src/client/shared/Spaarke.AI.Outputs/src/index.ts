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
 *   // Import types and registry functions:
 *   import { registerOutputWidget, OutputWidgetType } from "@spaarke/ai-outputs";
 */

// Types — all widget contracts, registry entry shapes, SSE event types
// Note: CrossPaneLink interface (data model) is exported from this barrel.
// The CrossPaneLink *component* is exported below from ./cross-pane, which
// takes precedence for the name "CrossPaneLink" in this package's public API.
// Consumers needing the data model type should import it as:
//   import type { CrossPaneLink as CrossPaneLinkModel } from "@spaarke/ai-outputs/types"
export * from './types';

// Registries — output and source widget registration/resolution
export * from './registry';

// Widget modules — populated in Wave 2 and Wave 3
export * from './output-widgets';
export * from './source-widgets';

// Cross-pane linking (Wave 3, task 031).
// CrossPaneLink exported here (component) shadows the CrossPaneLink interface
// from ./types in the package public API. TypeScript resolves the last
// re-export wins — we explicitly re-export the component to avoid ambiguity.
export {
  CROSS_PANE_LINK_EVENT,
  dispatchCrossPaneLink,
  subscribeToCrossPaneLinks,
  useDispatchCrossPaneLink,
  useCrossPaneSubscription,
  CrossPaneLink,
} from './cross-pane';
export type { CrossPaneLinkEvent, CrossPaneLinkProps } from './cross-pane';

export * from './chat-history';
