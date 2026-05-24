/**
 * SprkChat Hooks - Barrel exports
 *
 * Re-exports all hooks from the SprkChat hooks folder for convenient
 * single-import access.
 *
 * @see ADR-012 - Shared Component Library
 */

export { useSseStream, parseSseEvent, parsePaneEvent } from './useSseStream';

export { useChatSession } from './useChatSession';

export { useChatPlaybooks } from './useChatPlaybooks';
export type { IUseChatPlaybooksResult } from './useChatPlaybooks';

export { useActionMenuData } from './useActionMenuData';
export type { UseActionMenuDataOptions, IUseActionMenuDataResult } from './useActionMenuData';

export { useActionHandlers, openCodePageDialog, navigateToTarget, dispatchConfirmedAction } from './useActionHandlers';
export type {
  UseActionHandlersOptions,
  IUseActionHandlersResult,
  ActionHandlerContext,
  ActionHandler,
  ActionHandlerMap,
  WriteMode,
} from './useActionHandlers';

export { useSelectionListener } from './useSelectionListener';
export type { UseSelectionListenerOptions, IUseSelectionListenerResult } from './useSelectionListener';

export { useDynamicSlashCommands } from './useDynamicSlashCommands';
export type { UseDynamicSlashCommandsOptions, IUseDynamicSlashCommandsResult } from './useDynamicSlashCommands';

// FR-07: multi-file chat attachment hook (task 024) — consumed by SprkChat
// toolbar `+` button (task 025) and outbound payload wiring (task 026).
export {
  useChatFileAttachment,
  MAX_ATTACHMENTS,
  MAX_FILE_BYTES,
  MAX_PDF_PAGES,
  ALLOWED_MIME_TYPES,
} from './useChatFileAttachment';
export type {
  ChatAttachment,
  AttachmentChip,
  AttachmentChipStatus,
  AttachmentError,
  AttachmentErrorReason,
  AttachmentExtractionErrorCallback,
  UseChatFileAttachmentOptions,
  IUseChatFileAttachmentResult,
} from './useChatFileAttachment';
