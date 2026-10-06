/**
 * partialSaveOutcome — which of the pane's dirty fields a FAILED save actually persisted.
 *
 * unified-access-control-r2 decision round 51 item 2 (task 147, integration): `saveEvent` writes the event's FILING first,
 * through the BFF, then every other field as the caller's own update. When the filing lands and the other fields then
 * fail, the save answers `success: false` with `savedFields` = the filing's payload keys. The rollback must revert ONLY
 * the fields that failed — never the persisted filing, which the server has already re-filed (owner re-derived, F3
 * applied). Before this, the pane handed EVERY dirty field to the rollback, so "Discard" restored the old filing in the
 * form while the record stayed filed under the new one.
 *
 * Pure: the pane's own payload builder decides each dirty field's payload keys, so a lookup (`sprk_regardingmatter`)
 * is matched by the key it is sent as (`sprk_RegardingMatter@odata.bind`). A field counts as persisted only when EVERY
 * key it produces is in `savedKeys` (fail closed: anything unaccounted for stays rollback-able and retryable).
 */
import type { DirtyFields } from "./eventService";

export interface PartialSaveOutcome {
  /** Dirty fields the server persisted (the filing): treat as saved — never rolled back, never retried. */
  persisted: DirtyFields;
  /** Dirty fields that were not saved: the only ones rollback reverts and retry resends. */
  failed: DirtyFields;
}

export function splitPartialSave(
  fields: DirtyFields,
  savedKeys: readonly string[] | undefined,
  buildPayload: (fields: DirtyFields) => Record<string, unknown>
): PartialSaveOutcome {
  const saved = new Set(savedKeys ?? []);
  const persisted: DirtyFields = {};
  const failed: DirtyFields = {};
  for (const [field, value] of Object.entries(fields)) {
    const keys = Object.keys(buildPayload({ [field]: value } as DirtyFields));
    if (saved.size > 0 && keys.length > 0 && keys.every((k) => saved.has(k))) {
      persisted[field] = value;
    } else {
      failed[field] = value;
    }
  }
  return { persisted, failed };
}
