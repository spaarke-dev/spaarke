/**
 * Document Upload Services
 *
 * Shared services for document upload operations extracted from UniversalQuickCreate PCF.
 * Supports both PCF (context.webAPI) and Code Page (OData fetch with MSAL) contexts
 * via dependency injection (ITokenProvider, IDataverseClient).
 *
 * @version 1.0.0
 */
export { consoleLogger } from './types';
// SDAP API Client (SPE file operations)
export { SdapApiClient } from './SdapApiClient';
// File Upload Services
export { FileUploadService } from './FileUploadService';
export { MultiFileUploadService } from './MultiFileUploadService';
// NavMap Client (navigation property metadata)
export { NavMapClient } from './NavMapClient';
// Document Record Service (Dataverse CRUD)
export { DocumentRecordService } from './DocumentRecordService';
// IDataverseClient implementations
export { PcfDataverseClient } from './PcfDataverseClient';
export { ODataDataverseClient } from './ODataDataverseClient';
//# sourceMappingURL=index.js.map