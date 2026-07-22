/**
 * Local barrel for `ConversationWorkspace` (task 012). NOT re-exported from
 * the shared library's `src/components/index.ts` / `src/index.ts` barrels by
 * this task — per the task 012 file-ownership boundary, the main session adds
 * that export once the concurrent `ConversationView` (task 011) has landed
 * and the `renderConversation` seam can be wired to the real component.
 */
export { ConversationWorkspace, default } from './ConversationWorkspace';
export type {
  ConversationWorkspaceProps,
  IConversationWorkspaceRegarding,
  IConversationRendererProps,
} from './ConversationWorkspace';
export { ThreadList } from './subcomponents/ThreadList';
export type { IThreadListRow, IThreadListProps, ThreadListStatus } from './subcomponents/ThreadList';
