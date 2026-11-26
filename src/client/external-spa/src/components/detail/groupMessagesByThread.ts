/**
 * groupMessagesByThread — the record Messages list as threads (R4 task 035, FR-22, #2453).
 *
 * Pure: no fetch, no React. The ONLY grouping key is the server-supplied `threadId` on each message (the model-driven app's
 * own thread, projected by `ExternalMessageEndpoints.ToExternalDto`; UAC-r2 approved, 2026-11-24). Messages that share a
 * `threadId` form one thread; a message with no `threadId` stands alone. Nothing else is ever compared: not the subject, the
 * sender, the direction, `inReplyTo` or the time (a guess is not the model-driven app's thread; spec FR-22).
 *
 * ORDER. The read answers oldest to newest, and the list shows it that way (newest last, the new-message indicator sits under
 * it). Grouping keeps that orientation: a thread takes the place of its LATEST message (so the most recently active thread
 * is last), and the replies inside a thread keep the read's order (oldest first). A list with no `threadId` anywhere comes
 * back exactly as it went in, one entry per message.
 *
 * Why not the shared `CommunicationTimeline.buildTimeline`: its input is the internal plane's message shape (it also threads by
 * `inReplyTo`, which FR-22 forbids here), and it lives beside the internal fetch layer; this is the one small function the
 * external plane needs.
 */
import type { ExternalMessage } from '../../api/messages-client';

/** One entry of the grouped list: a thread (`threadId` set, one or more messages) or a message that stands alone. */
export interface MessageGroup {
  /** The server's thread key, or `null` for a message with no thread. */
  threadId: string | null;
  /** The thread's messages, oldest first (the read's order). Exactly one for a standalone message. */
  messages: ExternalMessage[];
}

/** A usable key: a non-blank string. Anything else (absent, null, blank, not a string) is "no thread". */
function threadKeyOf(message: ExternalMessage): string | null {
  const key: unknown = message.threadId;
  return typeof key === 'string' && key.trim() !== '' ? key : null;
}

/** Groups `messages` (oldest to newest, as the read returns them) by `threadId`. See the file comment for the order. */
export function groupMessagesByThread(messages: readonly ExternalMessage[]): MessageGroup[] {
  const members = new Map<string, ExternalMessage[]>();
  const lastIndex = new Map<string, number>();
  messages.forEach((message, index) => {
    const key = threadKeyOf(message);
    if (key === null) return;
    const list = members.get(key);
    if (list) list.push(message);
    else members.set(key, [message]);
    lastIndex.set(key, index);
  });

  const groups: MessageGroup[] = [];
  messages.forEach((message, index) => {
    const key = threadKeyOf(message);
    if (key === null) {
      groups.push({ threadId: null, messages: [message] });
    } else if (lastIndex.get(key) === index) {
      groups.push({ threadId: key, messages: members.get(key) ?? [message] });
    }
  });
  return groups;
}
