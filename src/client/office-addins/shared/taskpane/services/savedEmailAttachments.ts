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
 *
 * Task 141 review: the names only ever come from the SERVER (the identity answer, and an add's `attachmentsAdded`), never
 * from what the pane sent. An attachment no email save ever creates a document for is `notSaveable`: it is never marked
 * saved, never tickable, and never counts as "unsaved" (see {@link isNeverSavedByTheServer}).
 */

import type { AttachmentInfo } from '@shared/adapters/types';

/**
 * One attachment's saved state. `unknown` = the pane was not told (no saved/unsaved mark). `notSaveable` = no email save
 * creates a document for it (task 141 review): shown as such, never ticked, never counted as unsaved.
 */
export type AttachmentSavedState = 'saved' | 'unsaved' | 'unknown' | 'notSaveable';

/** The comparison key for a file name: case-insensitive, as the server's own rule. */
function nameKey(name: string): string {
  return name.toLowerCase();
}

// ── The server's attachment noise filter, mirrored (task 141 review) ─────────────────────────────────────────────
// MIRRORS src/server/api/Sprk.Bff.Api/Services/Email/AttachmentFilterService.cs (ShouldFilterAttachment) with the
// defaults of src/server/api/Sprk.Bff.Api/Configuration/EmailProcessingOptions.cs, plus the planner's "can be created"
// rule (EmailAttachmentAddPlanner.CanBeCreated: no content). The pane has no other copy of these rules: change them here
// when the server's change. An environment that overrides those options may save a little more or less than this says;
// the server's own answer (`attachmentsNotSaveable`) is the backstop, and it always wins for a name it reports.

/** `EmailProcessingOptions.BlockedAttachmentExtensions`. */
const SERVER_BLOCKED_EXTENSIONS = ['.exe', '.dll', '.bat', '.ps1', '.vbs', '.js', '.cmd', '.com', '.scr'];
/** `EmailProcessingOptions.CalendarFileExtensions` (`FilterCalendarFiles` = true). */
const SERVER_CALENDAR_EXTENSIONS = ['.ics', '.vcs'];
/** `EmailProcessingOptions.MaxAttachmentSizeMB` = 25. */
const SERVER_MAX_ATTACHMENT_BYTES = 25 * 1024 * 1024;
/** `EmailProcessingOptions.MinImageSizeKB` = 5. */
const SERVER_MIN_IMAGE_BYTES = 5 * 1024;
/** `EmailProcessingOptions.TrackingPixelPatterns` (case-insensitive). */
const SERVER_TRACKING_PIXEL_PATTERNS = [
  /\.gif\?.*tracking/i,
  /pixel\.(gif|png)/i,
  /^spacer\d*\.(gif|png)$/i,
  /beacon\.(gif|png)/i,
  /^1x1\.(gif|png)$/i,
  /tracker\.(gif|png)/i,
  /open\.(gif|png)/i,
];
/** `EmailProcessingOptions.SignatureImagePatterns` (case-insensitive; images only). */
const SERVER_SIGNATURE_IMAGE_PATTERNS = [
  /^image\d{3}\.(png|gif|jpg|jpeg)$/i,
  /^spacer\.(gif|png)$/i,
  /^logo.*\.(png|gif|jpg|jpeg)$/i,
  /^signature.*\.(png|gif|jpg|jpeg)$/i,
];

function extensionOf(name: string): string {
  const dot = name.lastIndexOf('.');
  return dot < 0 ? '' : name.slice(dot).toLowerCase();
}

/**
 * Whether the server would never create a document for this attachment, by the same rules in the same order as
 * `AttachmentFilterService.ShouldFilterAttachment` (see the block comment above): an empty name, a blocked extension, a
 * calendar file, an inline attachment, a file over the size limit, a tracking pixel, or an image that is a signature image
 * or smaller than the minimum; plus an attachment with no content. Such an attachment can never be added, so the pane
 * never offers it and never calls it "unsaved".
 */
export function isNeverSavedByTheServer(attachment: AttachmentInfo): boolean {
  const name = attachment.name ?? '';
  if (name.trim().length === 0) return true;
  const extension = extensionOf(name);
  if (SERVER_BLOCKED_EXTENSIONS.includes(extension)) return true;
  if (SERVER_CALENDAR_EXTENSIONS.includes(extension)) return true;
  if (attachment.isInline) return true;
  if (attachment.size > SERVER_MAX_ATTACHMENT_BYTES) return true;
  if (SERVER_TRACKING_PIXEL_PATTERNS.some(pattern => pattern.test(name))) return true;
  if ((attachment.contentType ?? '').toLowerCase().startsWith('image/')) {
    if (SERVER_SIGNATURE_IMAGE_PATTERNS.some(pattern => pattern.test(name))) return true;
    if (attachment.size < SERVER_MIN_IMAGE_BYTES) return true;
  }
  return attachment.size <= 0;
}

/**
 * Each attachment's saved state, keyed by attachment id. With `savedNames` undefined every attachment is `unknown`.
 * Otherwise a name the server lists is `saved`; one the server reported it can never save (`notSaveableNames`, an add's
 * `attachmentsNotSaveable`) or that {@link isNeverSavedByTheServer} is `notSaveable`; the rest are `unsaved`.
 */
export function classifySavedAttachments(
  attachments: readonly AttachmentInfo[],
  savedNames: readonly string[] | undefined,
  notSaveableNames?: readonly string[]
): Map<string, AttachmentSavedState> {
  const states = new Map<string, AttachmentSavedState>();
  if (savedNames === undefined) {
    for (const attachment of attachments) states.set(attachment.id, 'unknown');
    return states;
  }
  const saved = new Set(savedNames.map(nameKey));
  const notSaveable = new Set((notSaveableNames ?? []).map(nameKey));
  for (const attachment of attachments) {
    const key = nameKey(attachment.name);
    states.set(
      attachment.id,
      saved.has(key) ? 'saved' : notSaveable.has(key) || isNeverSavedByTheServer(attachment) ? 'notSaveable' : 'unsaved'
    );
  }
  return states;
}

/** True when any attachment is known to be unsaved. `unknown` and `notSaveable` never count. */
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
