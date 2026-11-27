/**
 * The build-time subset of `@spaarke/ui-components/services` this widget bundles (spaarkeai-mcp-bff-api-r2 task 034, FR-16, NFR-03).
 *
 * The package's inline To Do hook imports the services barrel, which also exports the chat bridge, the Markdown renderer, the telemetry
 * client and the rest of the library's services (marked, DOMPurify and Application Insights among them). Each name below is re-exported from
 * its own file instead. The inline To Do is hidden in a widget (task 010: it needs the BFF), so these are present for the import graph only.
 * The build maps the exact subpath `@spaarke/ui-components/services` to this file (vite alias, tsconfig `paths`, jest `moduleNameMapper`).
 */
export {
  applyResolverFields,
  discoverNavProps,
  type IPolymorphicWebApi,
} from '@spaarke/ui-components/services/PolymorphicResolverService';
export { TODO_REGARDING_CATALOG } from '@spaarke/ui-components/services/TodoRegardingUpdateBuilder';
export { createChildRecordViaBff } from '@spaarke/ui-components/utils/adapters/bffChildWriteAdapter';
