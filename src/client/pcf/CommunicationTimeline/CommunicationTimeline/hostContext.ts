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
