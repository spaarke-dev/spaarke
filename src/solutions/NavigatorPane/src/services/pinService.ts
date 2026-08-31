/**
 * pinService — the FULL inline star pin/unpin gesture
 * (spaarke-side-pane-navigation-history-r1 task 050, spec FR-07).
 *
 * Starring any pane row creates a per-user `sprk_type=Pin` `sprk_navitem`;
 * unstarring deletes it. Personal pins live ONLY in `sprk_navitem` — this
 * gesture MUST NOT write `sprk_monitor` (the shared record-level flag the
 * Monitored lens, task 052, reads separately). See module docblock on
 * `navItemRepository.ts` and project `CLAUDE.md` non-negotiable constraints.
 *
 * This module is a thin wrapper over `navItemRepository.ts`'s
 * `findPinItem`/`createPinItem`/`deleteNavItem` (task 041/050) — it does NOT
 * re-implement `Xrm.WebApi` CRUD (CLAUDE.md §11: extend the existing
 * read/write path rather than introducing a second one). Its own
 * responsibility is the pin-gesture SEMANTICS: dedupe-before-create so a
 * double-star never creates a duplicate row, and toggle (pin <-> unpin) as a
 * single call for the star affordance.
 *
 * Idempotency (project constraint / spec MUST): {@link pin} always looks up
 * the current user's existing pin for the target via `findPinItem` BEFORE
 * calling `createPinItem`. If one is already found, the existing row is
 * returned unchanged (`created: false`) rather than creating a second row.
 * This closes the "star an already-pinned target" acceptance criterion for
 * ANY sequential call (re-render, remount, a second click that lands after
 * the first one settles).
 *
 * True same-instant concurrent double-clicks (both requests firing before
 * either resolves) are not closed by a dedupe-before-create read+write pair
 * alone — two parallel calls can both observe "not found" before either
 * writes. This is the SAME limitation `RecentTab.tsx`'s task-041 promote-to-
 * pin star already has, and it is closed there (and here) at the UI layer:
 * the star `Button` is `disabled` while a pin/unpin call for that row's
 * `navItemId` is in flight (single-flight-per-row), so a rapid double-click
 * only ever issues one in-flight request — the second click is a no-op while
 * `disabled`. This is the accepted, already-shipped pattern for this exact
 * gesture (task 041); task 050 replicates it rather than introducing new
 * concurrency machinery (e.g. a server-side unique constraint, which is out
 * of scope for a host-context-only, no-plugin project). Per the task's
 * `<escalation>` trigger: this is judged a RELIABLE prevention for the
 * gesture's real usage pattern (a single user's own clicks on one pane), not
 * a case requiring escalation — see
 * `projects/spaarke-side-pane-navigation-history-r1/notes/task-050-dedupe-approach.md`.
 *
 * @see navItemRepository.ts (task 030/041/050) — Xrm.WebApi CRUD this module wraps
 * @see PinnedTab.tsx (task 050) — renders pins, calls `unpin`
 * @see RecentTab.tsx (task 041) — Recent tab's promote-to-pin star (own minimal
 *      `createPinItem` call; not migrated to this module in task 050 to keep
 *      041's already-shipped, already-tested surface untouched — see
 *      `notes/task-050-dedupe-approach.md`)
 */

import {
  createPinItem,
  deleteNavItem,
  findPinItem,
  type CreatePinItemInput,
  type NavItemRecord,
} from '@spaarke/ui-components/services/navigator/navItemRepository';

/** The target being pinned/unpinned. Reuses `CreatePinItemInput`'s shape — see that type's own docblock. */
export type PinTarget = CreatePinItemInput;

/** Result of {@link pin}. */
export interface PinResult {
  /** The pin row's `sprk_navitemid` (either newly created, or the pre-existing one). */
  navItemId: string;
  /** `false` when an existing pin for this target was found and reused (dedupe) — no write occurred. */
  created: boolean;
}

/**
 * Pin a target for the current user: create a per-user `sprk_type=Pin`
 * `sprk_navitem` (task 050). Dedupes by target BEFORE create — if the current
 * user already has a pin for this exact target, no row is created and the
 * existing row is returned. Never writes `sprk_monitor` (project HARD
 * MUST-NOT — this function's only Dataverse write is `createRecord` on
 * `sprk_navitem`, via `navItemRepository.createPinItem`).
 *
 * @param ownerId - Current user id (GUID, no braces) — `_ownerid_value`.
 * @param target - The record/view/page/link being pinned.
 */
export async function pin(ownerId: string, target: PinTarget): Promise<PinResult> {
  const existing = await findPinItem(ownerId, target.targetLogicalName, target.targetId);
  if (existing) {
    return { navItemId: existing.sprk_navitemid, created: false };
  }
  const created = await createPinItem(target);
  return { navItemId: created.id, created: true };
}

/**
 * Unpin a target for the current user: delete the per-user `sprk_type=Pin`
 * `sprk_navitem` for this target, if one exists (task 050). No-op (returns
 * `false`) if the current user has no pin for this target — unstarring an
 * already-unpinned target is safe to call. Never writes/touches
 * `sprk_monitor` (project HARD MUST-NOT — this function's only Dataverse
 * write is `deleteRecord` on `sprk_navitem`, via
 * `navItemRepository.deleteNavItem`).
 *
 * @param ownerId - Current user id (GUID, no braces) — `_ownerid_value`.
 * @param targetLogicalName - The pinned target's logical name.
 * @param targetId - The pinned target's record id.
 * @returns `true` if a pin was found and deleted, `false` if there was none.
 */
export async function unpin(
  ownerId: string,
  targetLogicalName: string,
  targetId: string
): Promise<boolean> {
  const existing = await findPinItem(ownerId, targetLogicalName, targetId);
  if (!existing) return false;
  await deleteNavItem(existing.sprk_navitemid);
  return true;
}

/**
 * Unpin a target for the current user by its ALREADY-KNOWN pin row id
 * (task 050 — the Pinned tab's own rows already carry `sprk_navitemid`, so
 * unstarring there does not need the extra `findPinItem` lookup {@link unpin}
 * performs for the "unstar from a non-Pinned surface, id unknown" case).
 * Still never touches `sprk_monitor` — delegates straight to
 * `navItemRepository.deleteNavItem`.
 *
 * @param navItemId - The pin row's `sprk_navitemid`.
 */
export async function unpinById(navItemId: string): Promise<void> {
  await deleteNavItem(navItemId);
}

/**
 * Look up the current user's pin `sprk_navitem` for a target, or `null` if
 * none exists. Thin re-export of {@link findPinItem} so consumers of
 * `pinService` (the pin-gesture module) don't need a second import from
 * `navItemRepository` just to check pinned-state.
 */
export async function findPin(
  ownerId: string,
  targetLogicalName: string,
  targetId: string
): Promise<NavItemRecord | null> {
  return findPinItem(ownerId, targetLogicalName, targetId);
}
