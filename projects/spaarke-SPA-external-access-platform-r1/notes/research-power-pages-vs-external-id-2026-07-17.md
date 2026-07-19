# Research — Power Pages vs Custom SPA + Entra External ID (mid-2026)

> **Date**: 2026-07-17
> **Question**: As of July 2026, is Power Pages still the best route for Spaarke's external access site, given the three original drivers (Contact-based access control, self-registration, capacity-pack licensing)?
> **Method**: researcher subagent — Microsoft Learn + official sources + 2025–2026 community/MVP writing.

---

## Bottom line

Power Pages is **not deprecated** (active 2026 investment, esp. Copilot Studio agent embedding), so staying is safe-from-obsolescence. But **two of the three original deciding factors have eroded**: self-registration is now native in Entra External ID, and capacity licensing is beaten on price by External ID's MAU model (first 50k MAU free). Only **contact-based access control** remains a distinct Power Pages advantage — and Spaarke already duplicates that in `sprk_externalrecordaccess`. A July 2026 SharePoint change forces **all** external users onto Entra B2B guest identities regardless of portal choice, neutralizing SPE identity as a differentiator. **Net: Power Pages = faster MVP; custom SPA + Entra External ID = stronger strategic fit** given Spaarke already owns the BFF/SPA/SPE stack.

---

## Findings

### 1. Power Pages strategic status — no deprecation
- 2026 Release Wave 1 continues Power Pages investment ("intelligent business portals"); direction is AI/agent-centric (embed Copilot Studio agents, web-role-gated). Zero deprecation risk.

### 2. Power Pages licensing (2026) — model unchanged
- Authenticated: capacity packs of 100 users/site/month; ~$200 per 100 at base, down to ~$50 per 100 at 100k+ tier.
- Anonymous: packs of 500/site/month; ~$75 per 500 base, down to ~$25 at scale.
- Per-**site** capacity; pay-as-you-go via Azure meter at higher unit rate. No structural change.

### 3. Alternatives
| Option | Contact authz | Self-reg | Cost | Build effort |
|---|---|---|---|---|
| Power Pages | Native | Built-in | Capacity packs/site | Lowest (low-code) |
| Custom SPA (SWA/App Service) + Entra External ID + BFF | Build it (Spaarke already has `sprk_externalrecordaccess`) | Native External ID sign-up flows | MAU (first 50k free) + hosting | Highest, but Spaarke owns most of the stack |
| Newer MS angle | Copilot agents embedded in Power Pages; SPE external → Entra B2B | — | — | Additive, not a portal replacement |

### 4. Entra External ID status — B2C is end-of-sale
- Azure AD B2C: no new customers since **2025-05-01**; **P2 discontinued 2026-03-15** (auto-downgrade to P1); support to ~**2030**; migration tooling rolling out.
- Any new external build **must** use Entra External ID (CIAM successor). Native self-service sign-up flows (social / MSA / email OTP) → covers self-registration requirement.

### 5. SharePoint Embedded + external users (2026) — the pivotal constraint
- All SPE container users must be Entra members or **Entra B2B guests**. No pure app-only path to give an external person file access without an Entra identity.
- **Additive drive-item invite permissions do NOT support app-only** — require a user (OBO) context. App-only *can* manage container-role membership, but item-level additive grants cannot be app-only.
- Org-wide forcing function: SharePoint/OneDrive external sharing moving to **mandatory Entra B2B**; **from July 2026, external collaborators without an Entra B2B guest account get access-denied**. No opt-out.
- Implication: external users need Entra B2B guest objects **regardless of Power Pages vs custom SPA** → SPE access is a wash; whole external stack converging on Entra External ID as identity fabric.

### 6. Recommendation
- **MVP**: keep/repair Power Pages now (fastest), **but** keep BFF + SPE authz portal-neutral so the move is a front-end swap.
- **Strategic target**: custom React SPA (Azure Static Web Apps) + Entra External ID + existing BFF. Strongest arguments: (a) Spaarke already owns the hard parts; (b) cost (50k MAU free vs per-site packs); (c) two of three original reasons eroded; (d) UI/UX parity with internal Fluent v9 surfaces; (e) unblocks non-Microsoft users (B2C dead end).

---

## Sources
- Power Platform 2026 release wave 1 — https://learn.microsoft.com/en-us/power-platform/release-plan/2026wave1/
- Embed Copilot Studio agents into Power Pages — https://www.microsoft.com/en-us/power-platform/blog/power-pages/seamlessly-embed-copilot-studio-agents-into-power-pages/
- Power Pages pricing — https://www.microsoft.com/en-us/power-platform/products/power-pages/pricing/
- Power Platform licensing FAQ — https://learn.microsoft.com/en-us/power-platform/admin/powerapps-flow-licensing-faq
- Azure AD B2C FAQ (end-of-sale 2025-05-01, P2 discontinuation 2026-03-15, support to 2030) — https://learn.microsoft.com/en-us/azure/active-directory-b2c/faq
- Entra External ID pricing (MAU, first 50k free) — https://learn.microsoft.com/en-us/entra/external-id/external-identities-pricing
- Entra External ID self-service sign-up — https://learn.microsoft.com/en-us/entra/external-id/self-service-sign-up-overview
- SPE — Share files and manage permissions (app-only NOT supported for additive invite) — https://learn.microsoft.com/en-us/sharepoint/dev/embedded/development/sharing-and-perm
- FAQ: Entra B2B integration for external sharing (mandatory B2B, July 2026 access-denied) — https://learn.microsoft.com/en-us/sharepoint/faqs-odspintegrationwithentrab2b
- SPE security Q&A (Tech Community) — https://techcommunity.microsoft.com/blog/marketplace-blog/sharepoint-embedded-security-features-a-comprehensive-qa-guide/4485400
- Project internal — `knowledge/sharepoint-embedded/NOTES.md`

## Caveats
- Per-MAU rate above 50k is behind the live meter (`aka.ms/ExternalIDPricing`) — not captured; verify before cost modeling.
- Power Pages list prices vary by agreement/tier/region — treat $200/$75 as list.
- SPE item-level additive invite "app-only unsupported" → confirm Spaarke's item-level grant path uses OBO/user context, not app-only.
- Plan explicit B2C→External ID migration if any Spaarke component still uses B2C.
