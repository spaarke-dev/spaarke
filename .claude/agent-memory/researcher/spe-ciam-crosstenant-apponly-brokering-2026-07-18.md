---
name: spe-ciam-crosstenant-apponly-brokering-2026-07-18
description: DEFINITIVE gate answer — can Entra External ID (CIAM) users read SPE docs in a DIFFERENT workforce tenant? Cross-tenant identity + app-only content brokering limits for external portal migration.
metadata:
  type: project
---

# SPE + CIAM cross-tenant + app-only brokering (2026-07-18)

Follow-up to [[power-pages-vs-entra-external-id-portal-2026-07-17]]. Gates the External ID migration decision. See also [[spe-dedup-content-identity-2026-07]] and knowledge/sharepoint-embedded/NOTES.md.

**Question**: Can a CIAM (Entra External ID) user — member of a SEPARATE directory, not the workforce tenant where SPE lives — access SPE containers? Or is a B2B guest in the workforce tenant still required? Can BFF app-only brokering eliminate the per-user workforce identity entirely?

## Definitive findings

1. **CIAM identity CANNOT be a delegated SPE container member cross-tenant.** SPE container permissions apply ONLY to delegated access, and the user "must be a member of the container" as an Entra member or B2B guest **of the consuming (workforce) tenant**. A CIAM user is a member of the *CIAM* tenant, a separate directory. There is no cross-tenant path for a CIAM token to satisfy container membership. For any delegated/OBO SPE call, a **B2B guest object in the workforce tenant is required** — confirming the prior suspicion. (Also: "Any Microsoft Entra user that isn't an external identity can be a container type owner" — external identities are further restricted from CT ownership, though that's orthogonal to read access.)

2. **One sign-up does NOT natively produce both identities.** CIAM identity (CIAM tenant) and workforce B2B guest (workforce tenant) are inherently two directory objects. To get both you must provision the B2B guest separately (Graph invitation API, app-only) and link — the BFF orchestrates CIAM sign-up → invite/guest-create in workforce tenant. Cross-tenant B2B can associate them but they remain two objects; redeeming a B2B guest whose home is a CIAM consumer directory is not a clean supported federation, so in practice the guest ends up OTP/email-based and distinct from the CIAM login credential. This is the awkward YELLOW path.

3. **App-only CAN read/stream file CONTENT with no user identity at all — this is the escape hatch and it works.** "An app that accesses containers without a user gets the full access defined by its container type application permissions" and "**container permissions apply only to delegated access**." App-only needs Graph `FileStorageContainer.Selected` (application) + container-type app permission **ReadContent** ("Read the content of containers of this container type"). Content download (`GET /drives/{id}/items/{id}/content`) and thumbnail retrieval are content reads → app-only covered. The BFF can download bytes and render its own preview/thumbnail, streaming to a CIAM-only user who never touches SharePoint. Per-item additive `driveItem invite` (needs user context) is MOOT if we never do per-user grants and broker everything app-only.
   - **What app-only CANNOT do (requires the user's own workforce token to reach SPE, impossible for CIAM-only):** Word/Excel/PowerPoint **for Web co-authoring**, Office **desktop open via `webUrl`**, **Copilot grounding** on the user's identity, and the interactive **`driveItem: preview` embed URL** (binds to the viewer's SharePoint session). Microsoft **Search** over SPE needs delegated `Files.Read.All`. `List containers` 403s for a delegated user without OneDrive but **app-only is unaffected** (app-only is actually better here). Admin ops (`Manage.All`) need an admin user.

4. **Bottom line / VERDICT: GREEN for a read-brokered portal; RED only for direct-Office features.**
   - If the external portal is BFF-as-single-data-path serving **read/download/preview-thumbnail** of documents → **GREEN**: no workforce B2B guest required per external user. CIAM-only login + app-only `ReadContent` brokering fully suffices. Authz stays in Dataverse (`sprk_externalrecordaccess`); BFF enforces it and the app-only token is the only thing that touches SPE. This is the recommended Microsoft-aligned pattern for "external users reading tenant-owned SPE docs through a backend."
   - If the portal must offer **in-place Word-for-Web co-authoring, desktop Office open, user-identity Copilot grounding, or Microsoft Search** for external users → those need the user's own workforce token → **B2B guest in the workforce tenant required (YELLOW/RED)**, and CIAM can't supply it. You'd be back to today's B2B-guest model, making the CIAM migration pointless for those features.
   - Net: eliminating the per-user workforce identity is viable **iff** you accept read-only brokered document access (BFF renders previews, downloads bytes) and give up direct-to-SharePoint Office/Copilot experiences for external users.

## July 2026 Entra-B2B mandatory rollout — does it change this?
- The mandatory-B2B change is about **SPO/OneDrive external SHARING** (OTP → Entra B2B guest). From **July 2026, external collaborators WITHOUT a workforce B2B guest see access denied** for shared links; manual enable until end-April 2026, auto-rollout after. OTP is **NOT** retired (it becomes B2B's default guest auth). Anyone/Anonymous links unaffected.
- **Impact on the app-only brokering path: NONE.** That rollout governs *user-delegated external sharing*, not app-only container access. App-only `ReadContent` does not depend on guest accounts or sharing links. So the July 2026 change actually *strengthens* the case for app-only brokering: the alternative (per-user B2B guest sharing) is the thing getting more locked-down, while app-only sidesteps it entirely.

## Sources
- https://learn.microsoft.com/en-us/sharepoint/dev/embedded/build/configure-authentication-authorization — MOST authoritative. App-only vs delegated, "container permissions apply only to delegated access," ReadContent CT permission, exceptional access patterns (Search/List-containers/Manage.All), item-invite sharing. Updated 2026-07-15.
- https://learn.microsoft.com/en-us/sharepoint/faqs-odspintegrationwithentrab2b — July 2026 access-denied for non-B2B guests; OTP not retired; manual-enable until end-Apr 2026. Updated 2026-05-09.
- knowledge/sharepoint-embedded/NOTES.md — Spaarke SpeFileStore app-only vs OBO split; webUrl/Word-Copilot flow REQUIRES user identity to SPE (the CIAM-blocker for Office features); `sprk_externalrecordaccess` dual-write authz truth.
- learn.microsoft.com/en-us/graph/api/driveitem-invite (additive invite needs user/OBO — moot if brokering app-only)

## Open questions
- Confirm app-only support for Graph `/thumbnails` on SPE driveItems specifically (high confidence yes as content-read; 30-min spike to verify against a real container).
- If Spaarke wants BOTH CIAM login AND occasional Office co-authoring for a subset of external users, is a hybrid (CIAM login + on-demand app-only-provisioned workforce B2B guest for the co-author subset) operationally worth it, or does that reintroduce the full B2B management burden it was trying to shed?
