/**
 * Container-type owner facts and presentation helpers — pure data, no JSX.
 *
 * Sibling of `containerTypeLifecycle.ts`, and kept separate from it for the same reason that file
 * exists: statements about the platform belong somewhere they can be sourced, asserted, and checked
 * against the corpus when it is refreshed — not inlined as JSX prose that quietly drifts.
 *
 * Spec FR-C09 / task 027.
 */

import type { ContainerTypeOwner } from "../../types/spe";

/**
 * The owner count the SharePoint admin center allows.
 *
 * ⚠️ **This is a UX limit, not a Graph-enforced one, and the distinction is deliberate.**
 *
 * Task 027's POML states "The SharePoint admin center allows up to three owners for settings and
 * billing". That claim is **not corroborated anywhere available to this repo**: the word "owner" does
 * not appear in `knowledge/sharepoint-embedded/docs/learn-containertypes.md` at all, and neither the
 * v1.0 nor the beta CSDL places any bound on the `permissions` collection.
 *
 * So the UI cites the admin center as the source rather than asserting Graph enforces it — and the
 * add path still surfaces the server's error, because a client-side guard is a convenience, never
 * evidence about what the API will accept. Stating an unverified limit as a fact would be the same
 * failure this project exists to remove, just pointed at a number.
 */
export const ADMIN_CENTER_OWNER_LIMIT = 3;

/** Explains what an owner is, and where the limit comes from. */
export const ADMIN_CENTER_OWNER_GUIDANCE =
  `Owners can change this container type's settings and billing. The SharePoint admin center allows ` +
  `up to ${ADMIN_CENTER_OWNER_LIMIT} owners. This is separate from the Permissions tab, which controls ` +
  `which applications may access containers of this type.`;

/** Consequence of removing the only remaining owner. */
export const LAST_OWNER_WARNING =
  "Removing the only owner can leave this container type with nobody able to change its settings or " +
  "billing. That is not something this app can undo — restoring an owner may require the SharePoint " +
  "admin center or a tenant administrator.";

/** How an owner should be labelled in the UI, given Graph may report very little about them. */
export interface DescribedOwner {
  /** Best available human-readable identity. Never blank. */
  readonly primary: string;
  /** Supporting detail, or null when there is nothing more to say. */
  readonly secondary: string | null;
}

/**
 * Choose display text for an owner.
 *
 * Graph may return a display name, an email, an id, or only some of those. Falling through in that
 * order keeps the row meaningful without ever rendering an empty label — and when nothing usable
 * came back, it says so explicitly rather than showing a blank row that reads as a corrupt record.
 */
export function describeOwner(owner: ContainerTypeOwner): DescribedOwner {
  const name = owner.displayName?.trim();
  const email = owner.email?.trim();
  const id = owner.userId?.trim();

  if (name) return { primary: name, secondary: email ?? id ?? null };
  if (email) return { primary: email, secondary: id ?? null };
  if (id) return { primary: id, secondary: null };

  return {
    primary: "Unknown user",
    secondary: "Microsoft Graph did not report a name, email, or ID for this owner.",
  };
}
