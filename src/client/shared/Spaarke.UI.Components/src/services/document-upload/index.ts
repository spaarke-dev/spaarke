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
    FileUploadApiRequest,
    FileDownloadRequest,
    FileDeleteRequest,
    FileReplaceRequest,
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

// SDAP API Client (SPE file operations)
export { SdapApiClient } from './SdapApiClient';
export type { SdapApiClientOptions, OnUnauthorizedCallback } from './SdapApiClient';

// File Upload Services
export { FileUploadService } from './FileUploadService';
export { MultiFileUploadService } from './MultiFileUploadService';

// NavMap Client (navigation property metadata)
export { NavMapClient } from './NavMapClient';
export type {
    NavMapClientOptions,
    EntitySetNameResponse,
    CollectionNavigationResponse,
} from './NavMapClient';

// Document Record Service (Dataverse CRUD)
export { DocumentRecordService } from './DocumentRecordService';
export type { DocumentRecordServiceOptions, EntityConfigResolver } from './DocumentRecordService';

// IDataverseClient implementations
export { PcfDataverseClient } from './PcfDataverseClient';
export { ODataDataverseClient } from './ODataDataverseClient';
export type { ODataDataverseClientOptions } from './ODataDataverseClient';
