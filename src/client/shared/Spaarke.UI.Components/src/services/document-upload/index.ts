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
export type { DocumentRecordServiceOptions, EntityConfigResolver } from './DocumentRecordService';

// IDataverseClient implementations
export { PcfDataverseClient } from './PcfDataverseClient';
export { ODataDataverseClient } from './ODataDataverseClient';
export type { ODataDataverseClientOptions } from './ODataDataverseClient';
