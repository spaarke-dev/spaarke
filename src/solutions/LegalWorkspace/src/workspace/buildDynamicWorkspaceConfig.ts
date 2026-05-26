/**
 * buildDynamicWorkspaceConfig.ts — LegalWorkspace re-export.
 *
 * The canonical implementation now lives in `@spaarke/ui-components` (hoisted in task 067).
 * This file preserves the original import path used throughout LegalWorkspace so existing
 * imports (`from "../workspace/buildDynamicWorkspaceConfig"`) continue to resolve without
 * changes elsewhere in the solution.
 *
 * See notes/drafts/067-factory-inventory.md for the architectural decision.
 */

export {
  buildDynamicWorkspaceConfig,
  SYSTEM_DEFAULT_LAYOUT_JSON,
} from "@spaarke/ui-components";
export type {
  LayoutJson,
  LayoutJsonRow,
  WorkspaceScope,
} from "@spaarke/ui-components";
