/**
 * Services barrel export — re-exports all service classes and helpers
 * for clean import paths in consuming components and hooks.
 *
 * Usage:
 *   import { DataverseService } from '../services';
 *   import { buildMattersQuery } from '../services';
 */

export { DataverseService } from './DataverseService';
export * from './queryHelpers';
export { EntityCreationService } from './EntityCreationService';
export type { IFileUploadResult, ISpeFileMetadata, IDocumentLinkResult, IAiPreFillResponse, IUploadProgress } from './EntityCreationService';
export { bffAuthProvider, authenticatedFetch } from './bffAuthProvider';
export type { IBffAuthProvider } from './bffAuthProvider';
