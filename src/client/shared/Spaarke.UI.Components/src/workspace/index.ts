/**
 * @spaarke/ui-components — workspace barrel
 *
 * Public surface for the `WorkspaceRenderer` interface and default-renderer
 * accessor introduced in R4 task 052 (C-4).
 *
 * @see WorkspaceRenderer.ts
 * @see defaultWorkspaceRenderer.ts
 */

export type { WorkspaceRenderer, WorkspaceRendererProps, WorkspaceRendererWebApi } from './WorkspaceRenderer';

export {
  setDefaultWorkspaceRenderer,
  getDefaultWorkspaceRenderer,
  clearDefaultWorkspaceRenderer,
} from './defaultWorkspaceRenderer';
