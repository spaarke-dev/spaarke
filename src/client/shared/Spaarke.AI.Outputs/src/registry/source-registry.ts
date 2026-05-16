/**
 * @spaarke/ai-outputs — Source Widget Registry
 *
 * Lazy-loaded registry mapping SourceWidgetType enum values to dynamic import()
 * factories. The source pane resolves widget components at render time — no
 * widget module is eagerly imported.
 *
 * NOT PCF-safe — requires React 19.
 *
 * Usage:
 *   import { resolveSourceWidget, SourceWidgetType } from "@spaarke/ai-outputs";
 *
 *   const mod = await resolveSourceWidget(SourceWidgetType.DocumentViewer);
 *   const Component = mod.default;
 *   return <Component data={...} />;
 */

import type React from "react";
import { SourceWidgetType, type SourceWidgetProps } from "../types/widget-types";

// ---------------------------------------------------------------------------
// Lazy factory record
// ---------------------------------------------------------------------------

/**
 * Maps every SourceWidgetType to a lazy factory that returns the widget module.
 * Consumers call resolveSourceWidget() rather than indexing this record directly.
 *
 * All imports are dynamic (`import()`) — no widget code is loaded until needed.
 */
export const sourceWidgetRegistry: Record<
  SourceWidgetType,
  () => Promise<{ default: React.ComponentType<SourceWidgetProps<unknown>> }>
> = {
  [SourceWidgetType.DocumentViewer]: () =>
    import("../source-widgets/DocumentViewerWidget") as Promise<{
      default: React.ComponentType<SourceWidgetProps<unknown>>;
    }>,

  [SourceWidgetType.WebSource]: () =>
    import("../source-widgets/WebSourceWidget") as Promise<{
      default: React.ComponentType<SourceWidgetProps<unknown>>;
    }>,

  [SourceWidgetType.LegalLibrary]: () =>
    import("../source-widgets/LegalLibraryWidget") as Promise<{
      default: React.ComponentType<SourceWidgetProps<unknown>>;
    }>,

  [SourceWidgetType.Citation]: () =>
    import("../source-widgets/CitationWidget") as Promise<{
      default: React.ComponentType<SourceWidgetProps<unknown>>;
    }>,

  [SourceWidgetType.ImageViewer]: () =>
    import("../source-widgets/ImageViewerWidget") as Promise<{
      default: React.ComponentType<SourceWidgetProps<unknown>>;
    }>,

  [SourceWidgetType.CodeViewer]: () =>
    import("../source-widgets/CodeViewerWidget") as Promise<{
      default: React.ComponentType<SourceWidgetProps<unknown>>;
    }>,
};

// ---------------------------------------------------------------------------
// Public helper
// ---------------------------------------------------------------------------

/**
 * Resolve a source widget component by type.
 *
 * Calls the lazy factory for the given SourceWidgetType and returns the module.
 * Returns null if the type is not registered (defensive against unknown values).
 *
 * @param type - The SourceWidgetType to resolve.
 * @returns Promise resolving to the widget module (with `default` export), or null.
 *
 * @example
 * const mod = await resolveSourceWidget(SourceWidgetType.DocumentViewer);
 * if (mod) {
 *   const Widget = mod.default;
 *   return <Widget data={{ documentUrl: url, fileName: name }} />;
 * }
 */
export async function resolveSourceWidget(
  type: SourceWidgetType
): Promise<{ default: React.ComponentType<SourceWidgetProps<unknown>> } | null> {
  const factory = sourceWidgetRegistry[type];
  if (!factory) return null;
  return factory();
}
