/**
 * @spaarke/sdap-client
 *
 * SDAP API client for PCF controls and TypeScript applications.
 *
 * @packageDocumentation
 */

export { SdapApiClient } from './SdapApiClient';

// Name-collision handling. Consumers catch UploadNameConflictError by `instanceof` to offer the
// rename / save-as-new-version choice, then retry with a ConflictBehaviorOption.
export { UploadNameConflictError } from './operations/UploadOperation';
export type { ConflictBehaviorOption } from './operations/UploadOperation';

export type {
  SdapClientConfig,
  DriveItem,
  UploadSession,
  FileMetadata,
  UploadProgressCallback,
  SdapApiError,
  Container,
  AuthenticatedFetchFn,
  ParentEntityContext,
  IndexFileRequest,
  IndexFileResult,
} from './types';
