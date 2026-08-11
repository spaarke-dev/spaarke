/**
 * `@spaarke/communication-components/logic` — React-agnostic (Layer 1) logic
 * shared across the PCF ↔ code-page boundary (ADR-022 slim-first; NFR-05).
 *
 * Scaffold established by email-communication-solution-r5 task 020; sibling
 * Phase-2 tasks (021/022) add further `./logic/<domain>` sub-barrels here.
 */

export * from './connections';
export * from './attachments';
export * from './citations';
// `./actions` is re-exported by explicit name (not `export *`) because its
// `filterFileAttachments` (source-communication attachment carry-forward,
// task 022) collides with `./attachments`' OWN `filterFileAttachments`
// (attachment-list inline-image filter, task 021) — two independently
// extracted helpers over different row shapes that happen to share a name.
// `export *` ambiguity fails TS declaration emit (TS2308). The task-022
// module itself (`./actions/attachmentsSource.ts`) still exports the name
// verbatim; deep-importing `@spaarke/communication-components/logic/actions`
// (what the CommunicationActions PCF + its tests do) sees the full, unrenamed
// export list. Only this AGGREGATE barrel disambiguates, via a rename.
export {
  deriveComposerFields,
  splitRecipients,
  buildQuotedThread,
  launchCreate,
  CREATE_LAUNCH_TARGETS,
  fetchSourceAttachments,
  buildDocumentLinkUrl,
  projectSourceAttachment,
  filterFileAttachments as filterActionsFileAttachments,
} from './actions';
export type {
  ComposerMode,
  RecordPrefill,
  ComposerFields,
  CreateKind,
  CreateLaunchTarget,
  LaunchCreateOptions,
  IActionsWebApi,
  ISourceAttachmentRecord,
} from './actions';
