/**
 * savedEmailAttachments.ts — spaarkeai-word-add-in-r1 task 141.
 *
 * Which of an email's attachments are already saved under its saved `.eml` in Spaarke. The server lists the names of the
 * `.eml`'s child documents the caller may read (`resolve-email-identity` → `savedAttachmentNames`, the same rule as task
 * 138's planner: `sprk_documentname` or `sprk_filename`, case-insensitive). An attachment is saved when its file name is
 * one of those names, compared case-insensitively.
 *
 * `undefined` names mean "not determined" (an older server, or the server's read of the children failed): every
 * attachment is then `unknown`, and the pane makes no claim either way — never "unsaved", which would invite the user to
 * add something that may already be there. An EMPTY list is an answer: nothing is saved yet.
 */

import type { AttachmentInfo } from '@shared/adapters/types';

/** One attachment's saved state. `unknown` = the pane was not told (no saved/unsaved mark). */
export type AttachmentSavedState = 'saved' | 'unsaved' | 'unknown';

/** The comparison key for a file name: case-insensitive, as the server's own rule. */
function nameKey(name: string): string {
  return name.toLowerCase();
}

/**
 * Each attachment's saved state, keyed by attachment id. With `savedNames` undefined every attachment is `unknown`.
 */
export function classifySavedAttachments(
  attachments: readonly AttachmentInfo[],
  savedNames: readonly string[] | undefined
): Map<string, AttachmentSavedState> {
  const states = new Map<string, AttachmentSavedState>();
  if (savedNames === undefined) {
    for (const attachment of attachments) states.set(attachment.id, 'unknown');
    return states;
  }
  const saved = new Set(savedNames.map(nameKey));
  for (const attachment of attachments) {
    states.set(attachment.id, saved.has(nameKey(attachment.name)) ? 'saved' : 'unsaved');
  }
  return states;
}

/** True when any attachment is known to be unsaved. `unknown` never counts. */
export function hasUnsavedAttachments(states: ReadonlyMap<string, AttachmentSavedState>): boolean {
  for (const state of states.values()) {
    if (state === 'unsaved') return true;
  }
  return false;
}

/**
 * The saved names after an add: `prior` plus `added`, without a case-insensitive duplicate. `prior` undefined (not
 * determined) stays undetermined: knowing some names were added says nothing about the others.
 */
export function mergeSavedAttachmentNames(
  prior: readonly string[] | undefined,
  added: readonly string[]
): string[] | undefined {
  if (prior === undefined) return undefined;
  const merged: string[] = [];
  const seen = new Set<string>();
  for (const name of [...prior, ...added]) {
    const key = nameKey(name);
    if (seen.has(key)) continue;
    seen.add(key);
    merged.push(name);
  }
  return merged;
}

/** `names` without any that appear in `excluded` (case-insensitive). */
export function withoutNames(names: readonly string[], excluded: readonly string[] | undefined): string[] {
  if (!excluded || excluded.length === 0) return [...names];
  const drop = new Set(excluded.map(nameKey));
  return names.filter(name => !drop.has(nameKey(name)));
}
