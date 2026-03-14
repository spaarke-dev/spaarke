/**
 * Host adapter module for Office add-ins.
 *
 * This module exports the interfaces, types, and factory needed to implement
 * the host adapter pattern for Outlook and Word add-ins.
 *
 * @example
 * ```typescript
 * import {
 *   IHostAdapter,
 *   HostAdapterFactory,
 *   AttachmentInfo,
 *   Recipient,
 * } from '@shared/adapters';
 *
 * // Get adapter instance
 * const adapter = await HostAdapterFactory.getOrCreate();
 *
 * // Use adapter methods
 * const hostType = adapter.getHostType();
 * const subject = await adapter.getSubject();
 * const body = await adapter.getBody();
 *
 * // Check capabilities before using host-specific features
 * if (adapter.getCapabilities().canGetAttachments) {
 *   const attachments = await adapter.getAttachments();
 * }
 * ```
 */

// Main interface
export type {
  IHostAdapter,
  IHostContext,
  IContentData,
  IContentMetadata,
  ContentFormat,
  HostFeature,
} from './IHostAdapter';

// Types
export type {
  HostType,
  ItemType,
  BodyType,
  BodyContent,
  AttachmentInfo,
  Recipient,
  HostCapabilities,
  HostAdapterError,
  HostAdapterErrorCode,
  InsertLinkResult,
  AttachFileResult,
  GetDocumentContentOptions,
  ItemMetadata,
  EmailMetadata,
  DocumentMetadata,
} from './types';

// Factory
export { HostAdapterFactory, isHostAdapterError } from './HostAdapterFactory';
export { default as hostAdapterFactory } from './HostAdapterFactory';
