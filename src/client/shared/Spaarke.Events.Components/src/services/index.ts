/**
 * @spaarke/events-components — services barrel
 *
 * UI-agnostic data access via `Xrm.WebApi` (FetchXML + saved-view retrieval).
 * No BFF dependency; auth is implicit through the Dataverse-hosted runtime
 * (ADR-028).
 */

export {
  executeFetchXml,
  getViewById,
  mergeDateFilterIntoFetchXml,
  ensureRequiredAttributes,
  parseLayoutXml,
  type FetchXmlResult,
  type ViewDefinition,
  type LayoutColumn,
} from './FetchXmlService';
