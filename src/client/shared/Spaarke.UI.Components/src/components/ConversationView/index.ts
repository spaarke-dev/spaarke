/**
 * ConversationView/index.ts
 *
 * Local barrel for the `ConversationView` folder (task 011). NOT wired into
 * the shared lib's top-level `src/index.ts` / `src/components/index.ts`
 * barrels here — the main session adds that export once this task lands
 * (per the parallel file-ownership boundary with task 012).
 */
export { ConversationView, isOwnMessage, buildConversationRenderItems } from './ConversationView';
export type {
  ConversationViewProps,
  ConversationViewHandle,
  IConversationViewRegarding,
  ConversationRenderItem,
  MessageBubbleStatus,
} from './ConversationView.types';
export { MessageBubble } from './subcomponents/MessageBubble';
export type { IMessageBubbleProps } from './subcomponents/MessageBubble';
export { EmailInFlowBlock } from './subcomponents/EmailInFlowBlock';
export type { IEmailInFlowBlockProps } from './subcomponents/EmailInFlowBlock';
