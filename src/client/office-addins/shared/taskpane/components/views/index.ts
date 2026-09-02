export { SaveView } from './SaveView';
export type { SaveViewProps, SaveOptions } from './SaveView';

export { ShareView } from './ShareView';
export type { ShareViewProps, DocumentSearchResult, SharePermissions } from './ShareView';

export { StatusView } from './StatusView';
export type { StatusViewProps, ProcessingJob } from './StatusView';

export { SignInView } from './SignInView';
export type { SignInViewProps } from './SignInView';

// Inline "Create To Do" tool — creates a first-class sprk_todo via the BFF `POST /api/office/todo`,
// right in the pane, regarding the filed record (email-communication-intelligence-r2, owner 2026-09-02).
export { CreateTodoView } from './CreateTodoView';
export type {
  CreateTodoViewProps,
  SavedTodoContext,
  CreateTodoInput,
  CreateTodoResult,
  ContactOption,
} from './CreateTodoView';
