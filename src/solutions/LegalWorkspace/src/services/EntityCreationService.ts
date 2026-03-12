/**
 * EntityCreationService.ts
 * Re-export from shared library for backward compatibility.
 *
 * The service now requires dependency injection — pass authenticatedFetch and
 * bffBaseUrl to the constructor instead of importing them:
 *
 * ```typescript
 * import { authenticatedFetch } from '../services/authInit';
 * import { getBffBaseUrl } from '../config/bffConfig';
 *
 * const service = new EntityCreationService(webApi, authenticatedFetch, getBffBaseUrl());
 * ```
 */
export {
  EntityCreationService,
  type IFileUploadResult,
  type ISpeFileMetadata,
  type IDocumentLinkResult,
  type IUploadProgress,
  type AuthenticatedFetchFn,
} from '../../../../client/shared/Spaarke.UI.Components/src/services/EntityCreationService';
