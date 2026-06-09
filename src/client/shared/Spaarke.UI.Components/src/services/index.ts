// IDataverseClient - Spaarke DataGrid Framework R1 contract (task 001)
// (Type names chosen to avoid collision with the existing `DataverseAttributeType` enum
//  in types/ColumnRendererTypes.ts and `RetrieveMultipleResult` in utils/xrmContext.ts —
//  those are PCF-dataset / raw-Xrm shapes, distinct from these Web-API-metadata projections.)
export type {
  IDataverseClient,
  SavedQueryResult,
  SavedQuerySummary,
  EntityMetadata,
  EntityAttributeMetadata,
  MetadataAttributeType,
  OptionSetOption,
  FetchMultipleResult,
} from './IDataverseClient';

export { XrmDataverseClient } from './XrmDataverseClient';

// BffDataverseClient — BFF-passthrough sibling impl (task 015, FR-BFF-06).
// Use in non-MDA hosts: Code Pages, workspace widgets, Office Add-ins, Storybook.
// Constructor takes `authenticatedFetch` from `@spaarke/auth` (DI for decoupling)
// and an optional `bffBaseUrl` (falls back to window.SPAARKE_BFF_URL / env).
export { BffDataverseClient } from './BffDataverseClient';
export type { BffDataverseClientOptions, AuthenticatedFetchFn as BffAuthenticatedFetchFn } from './BffDataverseClient';
export {
  BffDataverseClientError,
  BffDataverseClientConfigurationError,
  BffNotFoundError,
  BffForbiddenError,
  BffBadRequestError,
  BffServerError,
} from './BffDataverseClient';

export { AppInsightsService } from './AppInsightsService';
export { reportClientError, setClientErrorTelemetryHook } from './reportClientError';
export type { ClientErrorContext } from './reportClientError';
export { EntityCreationService } from './EntityCreationService';
export type {
  IFileUploadResult,
  ISpeFileMetadata,
  IDocumentLinkResult,
  ISendEmailInput,
  ISendEmailResult,
  IUploadProgress,
  AuthenticatedFetchFn,
} from './EntityCreationService';
export * from './CommandRegistry';
export * from './CommandExecutor';
export { FieldMappingService } from './FieldMappingService';
export {
  EventTypeService,
  eventTypeService,
  DEFAULT_EVENT_FIELD_STATES,
  ALL_EVENT_FIELDS,
  DEFAULT_SECTION_STATES,
  ALL_SECTION_NAMES,
  getEventTypeFieldConfig,
} from './EventTypeService';
export type { IGetEventTypeFieldConfigResult, SectionName } from './EventTypeService';
export { FetchXmlService } from './FetchXmlService';
export type {
  IFetchXmlResult,
  IFetchXmlOptions,
  IColumnDefinition,
  IFilterGroup,
  IFilterCondition,
  ColumnDataType,
} from './FetchXmlService';
export { ViewService } from './ViewService';
export type { IGetViewsOptions } from './ViewService';
export { ConfigurationService } from './ConfigurationService';
export type {
  IGridConfiguration,
  IGridConfigJson,
  IColumnOverride,
  IDefaultFilter,
  IRowFormattingRule,
  IGridFeatures,
  GridConfigViewType,
} from './ConfigurationService';
export { resolveRecordType, buildRecordUrl, findNavProp, applyResolverFields } from './PolymorphicResolverService';
export type {
  IPolymorphicWebApi,
  IRecordTypeRef,
  INavPropEntry,
  IResolverFieldValues,
} from './PolymorphicResolverService';
export { renderMarkdown, SPRK_MARKDOWN_CSS } from './renderMarkdown';
export type { RenderMarkdownOptions } from './renderMarkdown';
export { SprkChatBridge } from './SprkChatBridge';

// Shared people/contact lookup helpers (canonical home for new code).
//
// NOTE: `searchUsersAsLookup` and `searchContactsAsLookup` are also re-exported
// from `components/CreateMatterWizard` for historical reasons; to avoid a
// duplicate-export collision at the top-level barrel we only re-export the
// NEW combined helper here. Import the individual functions directly from
// `./userLookup` when needed (e.g. `import { searchUsersAsLookup } from
// '@spaarke/ui-components/services/userLookup'`).
export { searchUsersAndContacts, extractEmailKey } from './userLookup';

// Typed wrapper around POST /api/communications/send.
export { sendCommunication } from './communicationApi';
export type {
  SendCommunicationOptions,
  SendCommunicationResult,
  ICommunicationApiClientOptions,
  ICommunicationAssociation,
  CommunicationBodyFormat,
  CommunicationSendMode,
} from './communicationApi';
export type {
  SprkChatBridgeEventMap,
  SprkChatBridgeEventName,
  SprkChatBridgeHandler,
  SprkChatBridgeOptions,
  SprkChatBridgeUnsubscribe,
  DocumentStreamStartPayload,
  DocumentStreamTokenPayload,
  DocumentStreamEndPayload,
  DocumentReplacedPayload,
  ReAnalysisProgressPayload,
  SelectionChangedPayload,
  ContextChangedPayload,
} from './SprkChatBridge';
