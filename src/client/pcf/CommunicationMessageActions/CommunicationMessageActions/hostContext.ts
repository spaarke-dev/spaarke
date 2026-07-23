/**
 * Pure derivation of which action mode this control offers, given the host form's entity and
 * (when placed on `sprk_communication`) the record's channel + thread lookup. Extracted so the
 * mode-selection logic is testable without Xrm/auth/webAPI — mirrors `composerPrefill.ts`'s
 * extraction rationale in `CommunicationActions`. Task 062 / FR-12.
 */

/** `sprk_communicationtype` choice int for the Message (ACS Chat) channel — task 002/051. */
export const COMMUNICATION_TYPE_MESSAGE = 100000004;

export type MessageActionsMode = 'thread' | 'respond' | 'unsupported';

export interface IResolvedMessageTarget {
  /**
   * `'thread'` — placed on `sprk_communicationthread`: compose a fresh message directly into this
   * thread (no specific parent message). `'respond'` — placed on a Message-typed `sprk_communication`:
   * reply into that message's thread. `'unsupported'` — any other placement/channel (e.g. an Email
   * `sprk_communication`, or the record fields haven't loaded yet) — the caller renders a notice.
   */
  mode: MessageActionsMode;
  /** Target `sprk_communicationthread` id to stamp on send (task 062 `SendCommunicationOptions.threadId`). */
  threadId?: string;
  /** Parent `sprk_communication` id for reply continuity (`SendCommunicationOptions.inReplyToMessageId`). */
  inReplyToMessageId?: string;
}

export function resolveMessageTarget(
  hostEntityName: string | undefined,
  hostRecordId: string | undefined,
  communicationType: number | null | undefined,
  threadLookupId: string | null | undefined
): IResolvedMessageTarget {
  if (hostEntityName === 'sprk_communicationthread') {
    return { mode: 'thread', threadId: hostRecordId };
  }

  if (hostEntityName === 'sprk_communication' && communicationType === COMMUNICATION_TYPE_MESSAGE) {
    return {
      mode: 'respond',
      threadId: threadLookupId ?? undefined,
      inReplyToMessageId: hostRecordId,
    };
  }

  return { mode: 'unsupported' };
}
