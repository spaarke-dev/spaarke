/**
 * Document Upload Services
 *
 * Shared services for document upload operations extracted from UniversalQuickCreate PCF.
 * Supports both PCF (context.webAPI) and Code Page (OData fetch with MSAL) contexts
 * via dependency injection (ITokenProvider, IDataverseClient).
 *
 * @version 1.0.0
 */
export type { ITokenProvider, IDataverseClient, DataverseRecordRef, ILogger, SpeFileMetadata, ServiceResult, FileUploadApiRequest, FileDownloadRequest, FileDeleteRequest, FileReplaceRequest, FileUploadRequest, UploadFilesRequest, UploadProgress, UploadFilesResult, ParentContext, DocumentFormData, CreateResult, EntityDocumentConfig, LookupNavigationResponse, } from './types';
export { consoleLogger } from './types';
export { SdapApiClient } from './SdapApiClient';
export type { SdapApiClientOptions, OnUnauthorizedCallback } from './SdapApiClient';
export { FileUploadService } from './FileUploadService';
export { MultiFileUploadService } from './MultiFileUploadService';
export { NavMapClient } from './NavMapClient';
export type { NavMapClientOptions, EntitySetNameResponse, CollectionNavigationResponse } from './NavMapClient';
export { DocumentRecordService } from './DocumentRecordService';
export type { DocumentRecordServiceOptions, EntityConfigResolver } from './DocumentRecordService';
export { PcfDataverseClient } from './PcfDataverseClient';
export { ODataDataverseClient } from './ODataDataverseClient';
export type { ODataDataverseClientOptions } from './ODataDataverseClient';
//# sourceMappingURL=index.d.ts.map