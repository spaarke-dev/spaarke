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
// Exported so consumers branch on `status` by type instead of matching message text — the mistake
// that made a 409 name-collision indistinguishable from a real failure in the wizard upload path.
export { SdapHttpError, describeHttpFailure } from './operations/httpFailure';
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
