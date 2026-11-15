/**
 * recordSectionState — which record-template sections a user has collapsed, remembered per user
 * (spaarke-SPA-external-access-platform-r4 task 013, owner retest 2026-11-14: "the sections should expand/collapse; and
 * save settings after close for the user").
 *
 * WHERE. Per BROWSER, in `localStorage` (the same place the grid settings of FR-19 and the theme live; there is no
 * preferences service). One entry per user + record type + section: collapsing "Documents" on one matter collapses it on
 * every matter of that user, and it is still collapsed after the record is closed and opened again.
 *
 * WHO. The key is a short non-cryptographic hash of the caller's stable `/me` id (the contact id; for a workforce user with
 * no linked contact, the systemuser id), never the email or the UPN, so two people sharing one browser do not see each
 * other's choices and nothing readable about the person is in the key. The stored VALUE is the constant `'1'` (collapsed);
 * an expanded section has no entry. No identity, no record id and no name is stored.
 *
 * FAIL-OPEN. Every read and write is wrapped in try/catch. A store that is empty, blocked, full or throwing means "nothing
 * is collapsed" (every section expanded) with no error: the toggle then works in memory for the life of the open record.
 * With no user key yet (the `/me` read has not answered, or failed) nothing is read or written either.
 */
import * as React from 'react';
import type { CallerIdentity } from '../../api/delete-own';
import type { RecordSectionId } from './recordTemplate.types';

/** Bump when the stored shape changes, so an old shape is simply ignored. */
const KEY_PREFIX = 'sprk.ext.recordSection.v1';
const COLLAPSED = '1';

/**
 * A 53-bit non-cryptographic string hash (cyrb53), as 14 hex digits. It only keeps one user's entries apart from another's
 * in the same browser; it is not a security measure.
 */
function hashOf(text: string): string {
  let h1 = 0xdeadbeef;
  let h2 = 0x41c6ce57;
  for (let i = 0; i < text.length; i++) {
    const code = text.charCodeAt(i);
    h1 = Math.imul(h1 ^ code, 2654435761);
    h2 = Math.imul(h2 ^ code, 1597334677);
  }
  h1 = Math.imul(h1 ^ (h1 >>> 16), 2246822507) ^ Math.imul(h2 ^ (h2 >>> 13), 3266489909);
  h2 = Math.imul(h2 ^ (h2 >>> 16), 2246822507) ^ Math.imul(h1 ^ (h1 >>> 13), 3266489909);
  const value = 4294967296 * (2097151 & h2) + (h1 >>> 0);
  return value.toString(16).padStart(14, '0');
}

/**
 * The per-user storage key of the signed-in caller: the hash of their `/me` contact id (else systemuser id), or `null` while
 * the identity is unknown (nothing is then read or stored). Never derived from an email or UPN.
 */
export function sectionStateUserKeyOf(caller: CallerIdentity | null | undefined): string | null {
  if (!caller) return null;
  if (caller.contactId) return hashOf(`contact:${caller.contactId}`);
  if (caller.systemUserId) return hashOf(`systemuser:${caller.systemUserId}`);
  return null;
}

const storageKeyOf = (userKey: string, entity: string, section: RecordSectionId): string =>
  `${KEY_PREFIX}.${userKey}.${entity}.${section}`;

/** True when the user collapsed this section of this record type. Any storage failure answers false (expanded). */
export function readSectionCollapsed(userKey: string | null, entity: string, section: RecordSectionId): boolean {
  if (!userKey) return false;
  try {
    return window.localStorage.getItem(storageKeyOf(userKey, entity, section)) === COLLAPSED;
  } catch {
    return false;
  }
}

/** Remembers the section as collapsed (or, when expanded, forgets it). A failing store is ignored. */
export function writeSectionCollapsed(
  userKey: string | null,
  entity: string,
  section: RecordSectionId,
  collapsed: boolean
): void {
  if (!userKey) return;
  try {
    const key = storageKeyOf(userKey, entity, section);
    if (collapsed) window.localStorage.setItem(key, COLLAPSED);
    else window.localStorage.removeItem(key);
  } catch {
    // Not remembered; the section still collapses for this opening.
  }
}

/** What the record modal tells its section cards: the record type and the caller's storage key (null while unknown). */
export interface RecordSectionStateScope {
  entity: string;
  userKey: string | null;
}

/** Provided by `RecordTemplateModal`; absent (null) for a card drawn outside it, which then remembers nothing. */
export const RecordSectionStateContext = React.createContext<RecordSectionStateScope | null>(null);

/**
 * One section card's collapsed state: read from the store when the card mounts (so a record opened later shows the
 * remembered state), kept in memory, and written on every toggle. If the user key arrives after the card mounted, the
 * stored state is applied (unless the user already toggled), and a toggle made before then is stored once it is known.
 */
export function useSectionCollapsed(section: RecordSectionId): { collapsed: boolean; toggle: () => void } {
  const scope = React.useContext(RecordSectionStateContext);
  const entity = scope?.entity ?? '';
  const userKey = scope?.userKey ?? null;
  const [collapsed, setCollapsed] = React.useState<boolean>(() => readSectionCollapsed(userKey, entity, section));
  const touched = React.useRef(false);
  const latest = React.useRef(collapsed);
  latest.current = collapsed;

  React.useEffect(() => {
    if (!userKey) return;
    if (touched.current) writeSectionCollapsed(userKey, entity, section, latest.current);
    else setCollapsed(readSectionCollapsed(userKey, entity, section));
  }, [userKey, entity, section]);

  const toggle = React.useCallback((): void => {
    touched.current = true;
    const next = !latest.current;
    latest.current = next;
    setCollapsed(next);
    writeSectionCollapsed(userKey, entity, section, next);
  }, [userKey, entity, section]);

  return { collapsed, toggle };
}
