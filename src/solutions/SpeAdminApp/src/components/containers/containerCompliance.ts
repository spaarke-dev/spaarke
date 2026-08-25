/**
 * Container compliance routing + container-URL presentation — pure data, no JSX.
 *
 * WHY THIS FILE EXISTS (spec FR-C10 + FR-C11 / §4.2c, task 028).
 *
 * SPE compliance — legal hold, retention, eDiscovery — is delivered through Microsoft Purview, not
 * through container-level app APIs. R2 deliberately builds none of it: a hold surface here would
 * duplicate an audited compliance system with a narrower, unauditable one. But "we don't do that"
 * is only a defensible answer if the admin is ROUTED rather than stonewalled, and the concrete thing
 * they need in Purview is the container's URL, which this app surfaced nowhere.
 *
 * Kept as data rather than JSX prose for the same reason as the sibling `containerTypeLifecycle.ts`:
 * these strings are asserted in tests, and — as task 029 showed when `billingLabel` was duplicated
 * across three components and drifted — a string rendered from two places eventually says two things.
 *
 * ADR-021: no presentation here. Rendering lives in ContainersPage / ContainerDetail.
 */

/**
 * The Microsoft Purview portal.
 *
 * 🔑 Deliberately the PORTAL ROOT, not a `/ediscovery` deep path, and that is a considered choice
 * rather than laziness. The task constraint is "it MUST resolve — a dead link is worse than no link",
 * so the link had to be *verified*, and a deep path cannot be:
 *
 *   Purview is an auth-gated SPA. Every path returns `302 → login.microsoftonline.com` before any
 *   routing happens. Control test, 2026-08-24: `https://purview.microsoft.com/zzz-not-real-xyz123`
 *   returned the identical 302 as `/ediscovery`. So a 302 proves the HOST exists and proves nothing
 *   whatsoever about the PATH — accepting it as evidence would be this project's signature defect
 *   (a weak signal read as confirmation) committed inside the fix for it.
 *
 * What IS verified: Microsoft's own OIDC handshake for this host returns
 * `redirect_uri=https%3A%2F%2Fpurview.microsoft.com%2F` — the root — including when the request was
 * for `/ediscovery`. The root is a real registered reply URL. So the root is linked, and the
 * navigation step is spelled out in `PURVIEW_GUIDANCE_STEPS` instead, where a portal reorganisation
 * degrades to slightly stale wording rather than a 404.
 */
export const PURVIEW_PORTAL_URL = "https://purview.microsoft.com/";

/** Heading for the compliance-routing notice. */
export const PURVIEW_GUIDANCE_TITLE = "Legal hold, retention, and eDiscovery are managed in Microsoft Purview";

/**
 * Why this app does not manage compliance, in the admin's terms.
 *
 * States the boundary as a deliberate design decision, not a missing feature — an admin who reads
 * "not supported" goes looking for a workaround, whereas one who reads "governed elsewhere, here is
 * the door" completes the task.
 */
export const PURVIEW_GUIDANCE_BODY =
  "SharePoint Embedded containers are governed by the same Microsoft Purview policies as the rest of " +
  "your tenant. Holds, retention labels, and eDiscovery searches are applied in Purview so that they " +
  "stay auditable in one place — this admin app intentionally does not duplicate them.";

/** How to get from the Purview portal to a container-scoped search. */
export const PURVIEW_GUIDANCE_STEPS = [
  "Open the Microsoft Purview portal and go to Solutions → eDiscovery.",
  "Create or open a case, then add a search.",
  "Under locations, choose SharePoint sites and paste the container URL below as the site to search.",
] as const;

/** Names the container URL's role, so it reads as a compliance key rather than a stray link. */
export const CONTAINER_URL_PURPOSE =
  "A container's URL is the value Purview needs to scope an eDiscovery search to that container.";

/** Label for the container URL field / column. */
export const CONTAINER_URL_LABEL = "Container URL";

/**
 * Absent state for a container URL on a DETAIL response (NFR-06).
 *
 * "Not reported" — never a blank cell, and never a synthesised URL. The container id encodes the
 * SharePoint site GUID but NOT the tenant hostname, so any URL derived from it would be a fabricated
 * value dressed as a fact, which is the precise failure this project exists to remove.
 */
export const CONTAINER_URL_ABSENT_LABEL = "Not reported";

/** Explains the absent state on hover, distinguishing it from an empty value. */
export const CONTAINER_URL_ABSENT_TOOLTIP =
  "Microsoft Graph did not report a URL for this container. This is not the same as the container " +
  "having no URL — a container that is still provisioning has no drive yet.";

/**
 * Why the grid resolves the URL per row instead of showing it for every row at once.
 *
 * Not an apology for a limitation — it is the honest description of a measured platform behaviour,
 * surfaced so the next reader does not "optimise" it into a silent null. See
 * notes/task-028-findings.md §1 and the `SpeContainerSummary.WebUrl` doc comment.
 */
export const CONTAINER_URL_ON_DEMAND_TOOLTIP =
  "Get this container's URL. Microsoft Graph does not return container URLs when listing containers, " +
  "so it is fetched for the one container you ask for.";

/** Transient states for the per-row URL affordance. */
export type ContainerUrlState =
  | { kind: "idle" }
  | { kind: "loading" }
  | { kind: "resolved"; url: string }
  | { kind: "absent" }
  | { kind: "error"; message: string };
