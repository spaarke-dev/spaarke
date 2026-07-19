---
name: power-pages-vs-entra-external-id-portal-2026-07-17
description: External-facing portal choice (Power Pages vs custom SPA + Entra External ID) for Dataverse+SPE legal-ops SaaS, mid-2026 direction/licensing/SPE-external story
metadata:
  type: project
---

# External portal: Power Pages vs custom SPA + Entra External ID (2026-07-17)

Investigated for a legal-ops SaaS external secure workspace over Dataverse + SharePoint Embedded, with an existing .NET 8 BFF + React SPAs + SPE `sprk_externalrecordaccess` dual-write projection layer. See [[spe-dedup-content-identity-2026-07]] and knowledge/sharepoint-embedded/NOTES.md (documents Spaarke's current Power Pages invitation-linking flow).

**Why (context):** Team originally picked Power Pages for 3 reasons — contact-based access control, self-registration, capacity-pack licensing. Question is whether that still holds mid-2026.
**How to apply:** Use when advising on the external-access surface architecture / licensing.

## Key findings
- **Power Pages is NOT deprecated — actively invested.** 2026 release wave 1 adds Copilot Studio agent embedding (all auth types, web-role-gated agent visibility), design studio enhancements, Dataverse-for-Agents. Still Microsoft's recommended low-code route for external Dataverse portals.
- **Power Pages licensing (2026) unchanged model:** authenticated capacity packs of 100 users/site/month (base list ~$200/100, volume down to $50/100 at 100k+); anonymous packs of 500/site/month (base ~$75/500 → $25/500). Per-SITE (multi-site = multiply). Pay-as-you-go via linking env to an Azure subscription (higher unit cost, no commit).
- **Azure AD B2C: end of sale to NEW customers 2025-05-01.** B2C P2 discontinued 2026-03-15 (auto-downgrade to P1). Support to at least May 2030. New builds MUST use **Microsoft Entra External ID** (CIAM successor unifying B2B + CIAM). External ID has native self-service sign-up user flows (social IdPs, email OTP).
- **Entra External ID pricing:** MAU-based, **first 50,000 MAU free**, then per-MAU (far cheaper than Power Pages packs for auth alone). Premium add-ons (M2M, SMS, Go-Local, ID Governance) billed separately. Requires linking tenant to an Azure subscription.
- **SPE + external users (2026) — pivotal:** OTP retirement; SharePoint/OneDrive external sharing moving to **Entra B2B mandatory**. From **July 2026, external collaborators WITHOUT an Entra B2B guest account get access denied**; manual enable until end-April 2026, auto-rollout after. SPE requires all container users to be Entra tenant members or B2B guests. **Additive drive-item invite permissions do NOT support app-only** (needs OBO/user context); container-role membership can be app-only. => external users need Entra guest objects REGARDLESS of Power Pages vs custom SPA — the SPE identity story is a wash.

## Assessment of the 3 original deciding factors
- Contact-based access: still a genuine Power Pages strength, BUT Spaarke already owns authz truth in `sprk_externalrecordaccess` + projects to SPE app-only, so Power Pages web-roles/table-permissions become a SECOND authz layer.
- Self-registration: NO LONGER Power-Pages-exclusive — External ID self-service sign-up covers it natively. (Note: Spaarke's supported pattern is invitation-linking, not open self-signup.)
- Capacity licensing: External ID MAU (50k free) undercuts Power Pages packs for auth; but Power Pages packs bundle the WHOLE portal runtime (data binding, rendering, forms), not just identity.

## Recommendation given
Staged/portal-agnostic: Power Pages is the faster MVP if the surface is mostly Dataverse-record CRUD + light doc access. But given Spaarke already has BFF + React SPA + SPE projection, Power Pages duplicates authz and adds per-site pack cost. Strategic direction = custom React SPA + Entra External ID + BFF. Recommend keeping the BFF/SPE layer portal-agnostic so a later move is a front-end swap, not re-architecture.

## Open questions
- Exact per-MAU rate above 50k free (aka.ms/ExternalIDPricing is the live meter — not captured).
- Whether Power Pages honors Entra External ID external tenants as IdP cleanly for the guest-object provisioning SPE needs (likely yes via OIDC, confirm).
- Confirm Spaarke's `grantedToV2.user.id` claim for invited external contacts once BFF projection path is identified (still a TODO in SPE NOTES.md).

## Sources
- https://learn.microsoft.com/en-us/power-platform/release-plan/2026wave1/ (Power Pages continued investment)
- https://www.microsoft.com/en-us/power-platform/blog/power-pages/seamlessly-embed-copilot-studio-agents-into-power-pages/
- https://www.microsoft.com/en-us/power-platform/products/power-pages/pricing/ ; https://learn.microsoft.com/en-us/power-platform/admin/powerapps-flow-licensing-faq
- https://learn.microsoft.com/en-us/azure/active-directory-b2c/faq (B2C end-of-sale/discontinuation dates)
- https://learn.microsoft.com/en-us/entra/external-id/external-identities-pricing (MAU model, 50k free)
- https://learn.microsoft.com/en-us/entra/external-id/self-service-sign-up-overview
- https://learn.microsoft.com/en-us/sharepoint/dev/embedded/development/sharing-and-perm (app-only NOT supported for additive invite; container roles)
- https://learn.microsoft.com/en-us/sharepoint/faqs-odspintegrationwithentrab2b (Entra B2B mandatory; July 2026 access-denied)
- https://techcommunity.microsoft.com/blog/marketplace-blog/sharepoint-embedded-security-features-a-comprehensive-qa-guide/4485400
