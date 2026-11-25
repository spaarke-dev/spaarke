/**
 * SDAP client — the BFF client for SharePoint Embedded uploads, attaching an uploaded file to its
 * `sprk_document` row, and indexing it.
 *
 * Until 2026-11-24 this was the separate `@spaarke/sdap-client` package. It failed ADR-012's
 * three-test bar on its own (no distinct contract, no dependency to quarantine, two importers), so
 * it was folded in here (DEF-001, #2801). Framework-free: no React, and `authenticatedFetch` is
 * injected (ADR-028).
 *
 * Consumers outside this package import from the main barrel (`@spaarke/ui-components`), which
 * re-exports the public part through `services/index.ts`. It is deliberately NOT exported from
 * `pcf-safe.ts`: no PCF uses it, and the six Communication PCFs used to stub it out.
 */

export { SdapApiClient } from './SdapApiClient';

// Name-collision handling. Consumers catch UploadNameConflictError by `instanceof` to offer the
// rename / save-as-new-version choice, then retry with a ConflictBehaviorOption.
export { UploadNameConflictError, UploadReplaceNotSupportedError } from './operations/UploadOperation';
// Exported so consumers branch on `status` by type instead of matching message text — the mistake
// that made a 409 name-collision indistinguishable from a real failure in the wizard upload path.
export { SdapHttpError, describeHttpFailure } from './operations/httpFailure';
export type { ConflictBehaviorOption } from './operations/UploadOperation';

export type {
  SdapClientConfig,
  DriveItem,
  UploadSession,
  UploadProgressCallback,
  SdapApiError,
  Container,
  AuthenticatedFetchFn,
  ParentEntityContext,
  IndexFileRequest,
  IndexFileResult,
  AttachDocumentFileResult,
} from './types';
