/**
 * Words the wizards use, as pure functions of task 084's DTOs (ebilling-r1 task 084b). No React, no calls.
 */

import type { FirmRef, OverrideView, VersionView } from '../../services/recordProceduresApi';
import { recordWords, shortDate, versionLabel } from '../../services/model';

/** "from 1 Jan 2026 to 1 Jul 2026" / "from 1 Jan 2026" (open-ended) / "no start date". The window is `[from, to)`. */
export function versionWindow(v: Pick<VersionView, 'from' | 'to'>): string {
  if (!v.from) return 'no start date';
  return v.to ? `from ${shortDate(v.from)} to ${shortDate(v.to)}` : `from ${shortDate(v.from)}`;
}

/** "v2 · from 1 Jan 2026" for the Pin picker. */
export const versionOption = (v: VersionView): string => `${versionLabel(v)} · ${versionWindow(v)}`;

/** The versions a pin accepts: one that has been in force (EB-D-155: Active or Superseded). */
export const isPinnable = (v: VersionView): boolean => v.status === 'Active' || v.status === 'Superseded';

/** "Pinned to v1 on matter 2026-LIT-4471" / "Excluded here" / "Assigned on the parent project PRJ-20418", for one override row. */
export function overrideWords(o: OverrideView): string {
  const where = o.distance > 0 ? ` on ${recordWords(o.chainRecord.table, o.chainRecord.name)}` : o.mode === 'FollowLatest' ? '' : ' here';
  switch (o.mode) {
    case 'Pinned':
      return `Pinned to ${versionLabel(o.pinnedVersion)}${where}`;
    case 'Excluded':
      return `Excluded${where}`;
    case 'FollowLatest':
      return o.distance > 0 ? `Assigned${where}` : 'Assigned here, follows the latest version';
    default:
      return `${o.mode}${where}`;
  }
}

/** "Dana Whitfield, 2 Sep 2026" (the id-less fallback when the creator's name was not read). */
export function stampWords(o: { stamp: { byName: string | null; on: string | null } }): string {
  return [o.stamp.byName ?? 'Someone', o.stamp.on ? shortDate(o.stamp.on) : null].filter(Boolean).join(', ');
}

export const firmWords = (firm: FirmRef | null | undefined): string => firm?.name ?? 'the firm';
