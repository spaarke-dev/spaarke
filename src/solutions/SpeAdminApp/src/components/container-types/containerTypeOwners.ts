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
 * The maximum number of owners Graph allows on a container type.
 *
 * ✅ **Graph-enforced, and documented.** Microsoft's Create-permission reference states: *"A maximum
 * of 3 permissions per container type is allowed. Adding a fourth permission returns a
 * `400 Bad Request` error."*
 *
 * ⚠️ Corrected 2026-08-25. This was first written as an **unsourced** admin-center UX claim, because
 * the word "owner" appears nowhere in `knowledge/sharepoint-embedded/docs/learn-containertypes.md`
 * and neither CSDL bounds the `permissions` collection. Both of those observations were true — and
 * the conclusion drawn from them was still wrong, because the API reference was never checked. The
 * corpus not saying something is not the platform not saying it.
 *
 * The client-side guard remains a convenience, not the enforcement: the add path still surfaces the
 * server's error, since a UI check is never evidence about what the API will accept.
 */
export const ADMIN_CENTER_OWNER_LIMIT = 3;

/**
 * Explains what an owner is, the limit, and who may change it.
 *
 * The "who may add" sentence matters operationally: Graph permits this only for existing owners,
 * SharePoint Embedded Administrators, and Global Administrators. Every container type in the live
 * tenant currently has ZERO owners, so in practice it is the directory-role holders who can bootstrap
 * one — and an admin who lacks the role needs to know that before reading a 403 as a product fault.
 */
export const ADMIN_CENTER_OWNER_GUIDANCE =
  `Owners can change this container type's settings and billing. Microsoft Graph allows a maximum of ` +
  `${ADMIN_CENTER_OWNER_LIMIT} owners. Only an existing owner, a SharePoint Embedded Administrator, or a ` +
  `Global Administrator can add one. This is separate from the Permissions tab, which controls which ` +
  `applications may access containers of this type.`;

/**
 * Only `owner` is currently supported by Graph for container-type permissions.
 *
 * Named rather than inlined so that if Graph adds roles, the place to change is obvious — and so a
 * reader does not assume the single-element array is an accident.
 */
export const SUPPORTED_OWNER_ROLES = ["owner"] as const;

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
