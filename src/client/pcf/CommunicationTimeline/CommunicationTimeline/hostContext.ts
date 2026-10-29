/**
 * Pure derivation of the `sprk_communicationthread` id this control should
 * render, given the host form's entity and (when placed on
 * `sprk_communication`) the record's thread lookup. Extracted so the
 * placement logic is testable without Xrm/auth/webAPI — mirrors
 * `CommunicationMessageActions/hostContext.ts`'s extraction rationale.
 * Task 061 / FR-11.
 */

export function resolveThreadId(
  hostEntityName: string | undefined,
  hostRecordId: string | undefined,
  threadLookupId: string | null | undefined
): string | undefined {
  // `sprk_communicationthread` — the record IS the thread; render it directly.
  if (hostEntityName === 'sprk_communicationthread') {
    return hostRecordId;
  }

  // `sprk_communication` — render the thread the `_sprk_communicationthread_value`
  // lookup points at (undefined until that lookup value has loaded, or if the
  // record has no thread).
  if (hostEntityName === 'sprk_communication') {
    return threadLookupId ?? undefined;
  }

  // Any other placement — the caller renders an "unavailable" notice.
  return undefined;
}

/** `sprk_communicationthread.sprk_privacystate` choice int for Private (Open = 100000000). */
export const THREAD_PRIVACY_PRIVATE = 100000001;

/**
 * Is the rendered thread KNOWN to be Private (task 068; owner 2026-10-14)? Only the `sprk_communicationthread`
 * placement can know it without a Dataverse read: the thread form's own `sprk_privacystate` value (already
 * loaded on the form; `undefined` when the form does not carry it). On the `sprk_communication` placement the
 * thread's privacy is not known here, so the compose box stays unlocked and the BFF floor (task 065) covers it.
 */
export function resolveThreadIsPrivate(hostEntityName: string | undefined, threadPrivacyState: unknown): boolean {
  return hostEntityName === 'sprk_communicationthread' && threadPrivacyState === THREAD_PRIVACY_PRIVATE;
}
