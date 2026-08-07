/**
 * `EmailWorkspace` — component-folder barrel (email-communication-solution-r5
 * task 040). The single shared composition root both the SpaarkeAi widget
 * (041) and the standalone Email code page (042) render.
 */
export { EmailWorkspace } from './EmailWorkspace';
export type { EmailWorkspaceProps, EmailWorkspaceWebApi } from './EmailWorkspace.types';
export { CollapsibleSection } from './CollapsibleSection';
export type { CollapsibleSectionProps, SectionStatusTone } from './CollapsibleSection';
export { EmailRelatedToPills } from './EmailRelatedToPills';
export type { EmailRelatedToPillsProps } from './EmailRelatedToPills';
export {
  COMMUNICATION_ENTITY,
  EMAIL_TRACKING_FIELDS,
  DEFAULT_ACCESS_PERMISSION_OPTIONS,
  mapRowToEmailCardItem,
  readFiledAssociations,
  toWorkspaceRecordState,
  resolveEmlArchiveDocumentId,
  deriveEmailWorkspaceVisibleState,
  EMAIL_VISIBLE_SNIPPET_CAP_CHARS,
} from './EmailWorkspace.mapping';
export type {
  RawCommunicationRecord,
  EmailWorkspaceRecordState,
  EmailWorkspaceVisibleState,
} from './EmailWorkspace.mapping';
export { useEmailWorkspaceRecord } from './useEmailWorkspaceRecord';
export type { UseEmailWorkspaceRecordResult } from './useEmailWorkspaceRecord';
