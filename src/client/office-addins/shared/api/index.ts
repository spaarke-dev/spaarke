/**
 * Office API Client Module
 *
 * Provides typed API client for Office add-in communication with the Spaarke BFF API.
 *
 * @example
 * ```typescript
 * import { officeApiClient, SaveRequest, OfficeApiError } from '../api';
 *
 * // Save an email
 * const request: SaveRequest = {
 *   sourceType: 'OutlookEmail',
 *   associationType: 'Matter',
 *   associationId: 'guid-here',
 *   content: {
 *     emailId: 'AAMk...',
 *     includeBody: true,
 *     attachmentIds: ['att1', 'att2'],
 *   },
 * };
 *
 * try {
 *   const response = await officeApiClient.save(request);
 *   console.log('Job started:', response.jobId);
 * } catch (error) {
 *   if (error instanceof OfficeApiError) {
 *     console.error('API Error:', error.getUserMessage());
 *   }
 * }
 * ```
 */

// Main client
export {
  officeApiClient,
  createOfficeApiClient,
  OfficeApiClientImpl,
} from './OfficeApiClient';

export type {
  IOfficeApiClient,
  OfficeApiClientConfig,
  RequestOptions,
} from './OfficeApiClient';

// Error handling
export {
  OfficeApiError,
  getUserFriendlyMessage,
  createNetworkErrorDetails,
  createAuthErrorDetails,
  createTimeoutErrorDetails,
  createUnknownErrorDetails,
  parseRetryAfter,
  isRetryableError,
  formatValidationErrors,
} from './errors';

// Types
export type {
  // Source types
  SourceType,
  AssociationType,
  // Save types
  SaveRequest,
  SaveResponse,
  SaveContent,
  AttachmentContent,
  ProcessingOptions,
  SaveMetadata,
  // Job types
  JobStatus,
  StageStatus,
  JobStage,
  JobStatusResponse,
  // Search types
  EntitySearchParams,
  EntitySearchResult,
  EntitySearchResponse,
  DocumentSearchParams,
  DocumentSearchResult,
  DocumentSearchResponse,
  // Quick create types
  QuickCreateEntityType,
  QuickCreateRequest,
  QuickCreateRequestBase,
  QuickCreateMatterRequest,
  QuickCreateAccountRequest,
  QuickCreateContactRequest,
  QuickCreateResponse,
  // Share types
  ShareRole,
  ShareLinksRequest,
  ShareLinksResponse,
  ShareLink,
  ExternalInvitation,
  ShareAttachRequest,
  ShareAttachResponse,
  AttachmentInfo,
  // Recent types
  RecentItemsResponse,
  RecentAssociation,
  RecentDocument,
  FavoriteItem,
  // Error types
  ProblemDetails,
  // SSE types
  SseStageUpdateEvent,
  SseJobCompleteEvent,
  SseEventType,
  SseEventData,
} from './types';

export { OfficeApiErrorCode } from './types';
