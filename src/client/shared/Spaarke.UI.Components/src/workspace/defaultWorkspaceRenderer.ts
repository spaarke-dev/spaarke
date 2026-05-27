/**
 * @spaarke/ui-components — Default workspace renderer registration (R4 task 052 / C-4)
 *
 * # Purpose
 *
 * Provides a tiny module-level slot for the default `WorkspaceRenderer`
 * implementation. The host application (SpaarkeAi today; possibly others
 * tomorrow) calls `setDefaultWorkspaceRenderer()` at bootstrap to register
 * the concrete renderer it wants to use.
 *
 * `WorkspaceLayoutWidget` in `@spaarke/ai-widgets` consults this slot via
 * `getDefaultWorkspaceRenderer()` when no `renderer` prop is injected. This
 * is the seam that lets the widget remain context-agnostic (per ADR-012)
 * while still defaulting to `LegalWorkspaceApp` in today's deployment.
 *
 * # Why a module slot (not a registry)
 *
 * There is exactly ONE default workspace renderer per host today. The
 * `WorkspaceWidgetRegistry` lazy-load + multi-key pattern (in
 * `@spaarke/ai-widgets/registry`) would be over-engineered here — we do not
 * have dozens of renderer types competing for resolution by string key. A
 * simple module slot keeps the seam minimal (R4 task 052 spec constraint:
 * "no abstract base class, no plugin lifecycle hooks unless `LegalWorkspaceApp`
 * already uses them").
 *
 * @see WorkspaceRenderer.ts — the interface this module dispatches to
 * @see projects/spaarke-ai-platform-unification-r4/notes/c4-interface-design.md
 */

import type { WorkspaceRenderer } from "./WorkspaceRenderer";

// ---------------------------------------------------------------------------
// Internal module state
// ---------------------------------------------------------------------------

let _default: WorkspaceRenderer | null = null;

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * Register a default workspace renderer. Call once at host bootstrap (e.g.
 * SpaarkeAi `main.tsx`) BEFORE rendering the React tree.
 *
 * Subsequent calls overwrite the previously-registered renderer (last writer
 * wins). Tests may use `clearDefaultWorkspaceRenderer()` between cases to
 * isolate state.
 *
 * @param renderer - The React component to register as the default renderer.
 *
 * @example
 * ```ts
 * // In SpaarkeAi main.tsx:
 * import { LegalWorkspaceApp } from "@spaarke/legal-workspace";
 * import { setDefaultWorkspaceRenderer } from "@spaarke/ui-components";
 *
 * setDefaultWorkspaceRenderer(LegalWorkspaceApp);
 * ```
 */
export function setDefaultWorkspaceRenderer(
  renderer: WorkspaceRenderer
): void {
  _default = renderer;
}

/**
 * Retrieve the registered default workspace renderer.
 *
 * Returns `null` when no renderer has been registered. Callers (typically
 * `WorkspaceLayoutWidget` in `@spaarke/ai-widgets`) should handle the null
 * case by rendering a graceful empty state rather than throwing.
 *
 * @returns The registered default renderer, or `null` if none registered.
 */
export function getDefaultWorkspaceRenderer(): WorkspaceRenderer | null {
  return _default;
}

/**
 * Clear the registered default renderer. Intended for use in tests only —
 * production code should not need to clear the slot.
 */
export function clearDefaultWorkspaceRenderer(): void {
  _default = null;
}
