/**
 * Document Upload Services
 *
 * Shared services for document upload operations extracted from UniversalQuickCreate PCF.
 * Supports both PCF (context.webAPI) and Code Page (OData fetch with MSAL) contexts
 * via dependency injection (ITokenProvider, IDataverseClient).
 *
 * @version 1.0.0
 */

// Types and interfaces
export type {
  ITokenProvider,
  IDataverseClient,
  DataverseRecordRef,
  ILogger,
  SpeFileMetadata,
  ServiceResult,
  UploadTarget,
  FileUploadRequest,
  UploadFilesRequest,
  UploadProgress,
  UploadFilesResult,
  ParentContext,
  DocumentFormData,
  CreateResult,
  EntityDocumentConfig,
  LookupNavigationResponse,
} from './types';

export { consoleLogger } from './types';

// SDAP API Client — NOT re-exported from here any more (2026-09-03).
//
// `./SdapApiClient.ts` was this package's own parallel upload client, one of three in the repo. It
// is deleted; `FileUploadService` now takes `@spaarke/sdap-client`'s client. Import it from there:
//
//     import { SdapApiClient } from '@spaarke/sdap-client';
//
// Its `SdapApiClientOptions` / `OnUnauthorizedCallback` types went with it. The replacement config
// is `{ baseUrl, authenticatedFetch }` (ADR-028) — there is no `getAccessToken` / `onUnauthorized`
// pair, because `authenticatedFetch` already owns the 401-retry-and-clear-cache behaviour those
// two existed to provide.

// File Upload Services
export { FileUploadService } from './FileUploadService';
export { MultiFileUploadService } from './MultiFileUploadService';

// NavMap Client (navigation property metadata)
export { NavMapClient } from './NavMapClient';
export type { NavMapClientOptions, EntitySetNameResponse, CollectionNavigationResponse } from './NavMapClient';

// Document Record Service (Dataverse CRUD)
export { DocumentRecordService } from './DocumentRecordService';
export type { DocumentRecordServiceOptions, EntityConfigResolver, DocumentFileAttacher } from './DocumentRecordService';

// IDataverseClient implementation (Code Pages). The PCF-side PcfDataverseClient
// was DELETED 2026-10-03 (reuse audit C-26): zero instantiation sites.
export { ODataDataverseClient } from './ODataDataverseClient';

// UAC-r2 task 147 r1 (owner round 28 item 1): the upload pipeline's document creates go through the BFF (G5).
export { withBffChildCreates } from './BffChildRecordDataverseClient';
export type { ODataDataverseClientOptions } from './ODataDataverseClient';
