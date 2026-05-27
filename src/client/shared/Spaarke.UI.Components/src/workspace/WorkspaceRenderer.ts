/**
 * @spaarke/ui-components — WorkspaceRenderer interface (R4 task 052 / C-4)
 *
 * # Purpose
 *
 * Defines the minimum-viable seam between the workspace pane widget
 * (`WorkspaceLayoutWidget` in `@spaarke/ai-widgets`) and the concrete renderer
 * component that paints the workspace experience inside a tab.
 *
 * Today the only renderer is `LegalWorkspaceApp` (in `src/solutions/LegalWorkspace/`).
 * Tomorrow other hosts (SharePoint webparts, Power Pages portals) may register
 * alternate renderers without modifying `WorkspaceLayoutWidget`.
 *
 * # Design tenets
 *
 *   1. **Minimal surface** — `WorkspaceRendererProps` mirrors
 *      `ILegalWorkspaceAppProps` exactly so the existing renderer satisfies the
 *      interface with zero internal changes (R4 Risk R-4: zero behavioural diff).
 *
 *   2. **Context-agnostic** (ADR-012) — the interface uses only platform-
 *      neutral types (a loose Dataverse-WebApi-like shape, primitive scalars).
 *      No PCF / Xrm / solution-specific imports.
 *
 *   3. **Function-based auth** (ADR-028) — the interface NEVER carries a
 *      `token` or `accessToken` field. Renderers resolve auth via their own
 *      bootstrap (e.g. LegalWorkspace uses `setLegalWorkspaceRuntimeConfig`
 *      + injected `authenticatedFetch` inside `useWorkspaceLayouts`).
 *
 *   4. **React 19 compatible** (ADR-022) — uses standard `React.ComponentType`
 *      so the interface is satisfied by function components, class components,
 *      and `React.memo`-wrapped components alike.
 *
 * @see projects/spaarke-ai-platform-unification-r4/notes/c4-interface-design.md
 * @see projects/spaarke-ai-platform-unification-r4/notes/c4-pre-change-snapshot.md
 */

import type * as React from "react";

// ---------------------------------------------------------------------------
// WorkspaceRendererWebApi — loose Dataverse WebApi shape
// ---------------------------------------------------------------------------

/**
 * Loose Dataverse-WebApi shape consumed by workspace renderers. Mirrors what
 * `LegalWorkspaceApp` already accepts as its `webApi` prop today.
 *
 * Concrete consumers may pass a narrower platform-specific type
 * (e.g. LegalWorkspace's `IWebApi` interface, which is a structural superset
 * of this shape). This loose definition keeps the interface portable across
 * hosts that may obtain `webApi` via different routes (PCF context, frame-walk,
 * mock service factory).
 *
 * Only `retrieveMultipleRecords` is required — every workspace renderer is
 * expected to read data. The other methods are optional and become typed when
 * the host passes a richer concrete object.
 */
export interface WorkspaceRendererWebApi {
  /**
   * Retrieve multiple records matching the OData filter/select options.
   * Mirrors `Xrm.WebApi.retrieveMultipleRecords`.
   */
  retrieveMultipleRecords: (
    entityLogicalName: string,
    options?: string,
    maxPageSize?: number
  ) => Promise<{ entities: Record<string, unknown>[] }>;

  /**
   * Optional: retrieve a single record by id.
   * Return shape uses `Record<string, unknown>` so both Xrm.WebApi's
   * `WebApiEntity` (a structural alias for the same shape) and ComponentFramework
   * variants are assignable.
   */
  retrieveRecord?: (
    entityLogicalName: string,
    id: string,
    options?: string
  ) => Promise<Record<string, unknown>>;

  /**
   * Optional: create a new record.
   * The required-id-only return shape matches `Xrm.WebApi.createRecord`. Hosts
   * that pass a richer object (e.g. `{ id, entityType }`) remain assignable
   * because TypeScript's structural typing tolerates extra fields.
   */
  createRecord?: (
    entityLogicalName: string,
    data: Record<string, unknown>
  ) => Promise<{ id: string }>;

  /**
   * Optional: update an existing record by id.
   * Same id-only return contract as `createRecord` for the same reason.
   */
  updateRecord?: (
    entityLogicalName: string,
    id: string,
    data: Record<string, unknown>
  ) => Promise<{ id: string }>;

  /**
   * Optional: delete a record by id.
   * Same id-only return contract for parity with create/update.
   */
  deleteRecord?: (
    entityLogicalName: string,
    id: string
  ) => Promise<{ id: string }>;
}

// ---------------------------------------------------------------------------
// WorkspaceRendererProps — props passed by host widget to the renderer
// ---------------------------------------------------------------------------

/**
 * Props passed by `WorkspaceLayoutWidget` (or any future host widget) to a
 * `WorkspaceRenderer`. Designed to mirror `ILegalWorkspaceAppProps` exactly
 * so the existing renderer implements this interface with zero shape changes.
 *
 * @see src/solutions/LegalWorkspace/src/LegalWorkspaceApp.tsx
 */
export interface WorkspaceRendererProps {
  /**
   * Renderer-specific version identifier. Hosts that render the widget inside
   * a tab typically pass `"embedded"`; standalone renderers (if any) pass
   * their build version. The renderer is free to display this in debug UI.
   */
  version: string;

  /**
   * Reserved layout-dimension hint in pixels. Hosts that do not constrain the
   * renderer's size pass `0` (the renderer should fill its container).
   */
  allocatedWidth: number;

  /**
   * Reserved layout-dimension hint in pixels. Hosts that do not constrain the
   * renderer's size pass `0` (the renderer should fill its container).
   */
  allocatedHeight: number;

  /**
   * Dataverse WebApi reference, resolved by the host (frame-walk-aware in
   * the typical SpaarkeAi case). The renderer MUST NOT walk frames itself —
   * that is host responsibility. Renderers running outside a Dataverse host
   * receive a mock implementation of this shape.
   */
  webApi: WorkspaceRendererWebApi;

  /**
   * Current Dataverse user GUID (curly braces stripped). Used by the renderer
   * to filter user-scoped data (pinned layouts, personal preferences, etc.).
   * Hosts MUST pass an empty string `""` when no user context is available
   * (the renderer handles the empty case gracefully).
   */
  userId: string;

  /**
   * Optional workspace layout GUID for deep-link / initial-state activation.
   * When provided, the renderer should activate this layout on mount instead
   * of the user's pinned default.
   */
  initialWorkspaceId?: string;

  /**
   * `true` when the renderer is hosted inside another shell (e.g. SpaarkeAi
   * workspace tab). Renderers MUST suppress their own chrome when embedded:
   *   - skip the outer `<FluentProvider>` (assumes a parent provider)
   *   - skip the internal page header (host owns the workspace switcher)
   *   - skip the footer
   *   - skip cross-device theme-sync side effects (host owns the theme lifecycle)
   *
   * Defaults to `false` (standalone mode).
   */
  embedded?: boolean;
}

// ---------------------------------------------------------------------------
// WorkspaceRenderer — the public contract
// ---------------------------------------------------------------------------

/**
 * A React component that satisfies `WorkspaceRendererProps`. The host widget
 * (`WorkspaceLayoutWidget`) accepts an injected `WorkspaceRenderer` prop or
 * resolves the default-registered renderer via `getDefaultWorkspaceRenderer()`.
 *
 * `LegalWorkspaceApp` is the default registered renderer today (R4 task 052 /
 * C-4). Default registration happens at host bootstrap in SpaarkeAi's
 * `main.tsx` via `setDefaultWorkspaceRenderer(LegalWorkspaceApp)`.
 *
 * @example
 * ```tsx
 * // Future host registers an alternate renderer at bootstrap:
 * import { MyAlternateRenderer } from "./MyAlternateRenderer";
 * import { setDefaultWorkspaceRenderer } from "@spaarke/ui-components";
 *
 * setDefaultWorkspaceRenderer(MyAlternateRenderer);
 * ```
 */
export type WorkspaceRenderer = React.ComponentType<WorkspaceRendererProps>;
