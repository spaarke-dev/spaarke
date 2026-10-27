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

/** `sprk_communicationthread.sprk_privacystate` choice int for Private (Open = 100000000). */
export const THREAD_PRIVACY_PRIVATE = 100000001;

export interface IInternalOnlySeed {
  /** The target thread is KNOWN Private: the compose box shows "Internal Only" on and disabled. */
  threadIsPrivate: boolean;
  /** Respond to a source that is internal-only: the "Internal Only" switch starts on. */
  startInternalOnly: boolean;
}

/**
 * What this control already knows about the "Internal Only" switch (task 068; owner 2026-10-14). It reads
 * nothing new from Dataverse for this:
 *   - `thread` placement: the thread form's own `sprk_privacystate` value (already loaded on the form; pass
 *     `undefined` when the form does not carry it) decides whether the thread is known Private.
 *   - `respond` placement: the source `sprk_communication`'s `sprk_isinternalonly`, which the control's
 *     existing record read now also selects, decides whether the switch starts on. The thread's privacy is
 *     NOT known here; the BFF floor (task 065) covers it.
 * Anything not known leaves the switch off and unlocked.
 */
export function resolveInternalOnlySeed(
  mode: MessageActionsMode,
  sourceIsInternalOnly: boolean | null | undefined,
  threadPrivacyState: unknown
): IInternalOnlySeed {
  return {
    threadIsPrivate: mode === 'thread' && threadPrivacyState === THREAD_PRIVACY_PRIVATE,
    startInternalOnly: mode === 'respond' && sourceIsInternalOnly === true,
  };
}
