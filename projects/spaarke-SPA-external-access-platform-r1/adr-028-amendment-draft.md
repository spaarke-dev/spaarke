# ADR-028 Amendment A1 (DRAFT) — Entra External ID for the External Portal Surface

> **Status**: Proposed (resolution path **B — ADR amendment**, per root CLAUDE.md §6.5)
> **Date**: 2026-07-18
> **Amends**: [ADR-028 (concise)](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) + [ADR-028 (full)](../../docs/adr/ADR-028-spaarke-auth-architecture.md)
> **Driver project**: `spaarke-SPA-external-access-platform-r1`
> **Requires**: owner sign-off before merge into the canonical ADR (this is a reviewable draft, not yet applied to ADR-028).

---

## Why this amendment

ADR-028 v2 (2026-05-19) placed **"B2C portal"** explicitly out of scope (Deferred/Out of Scope, line 125) and modeled external portal identity as **Entra B2B guests in the workforce tenant** (external-SPA exemption, line 34). Two things have changed:

1. **Azure AD B2C is end-of-sale** (no new customers since 2025-05-01; support to ~2030). **Microsoft Entra External ID (CIAM)** is the successor, with native self-service sign-up. Any new external-identity build must target External ID.
2. The external portal is migrating **off Power Pages + B2B guests** to a **custom SPA (Azure Static Web Apps) + Entra External ID**. This deepens the ADR-028 external-SPA exemption by introducing a **second identity provider / tenant** distinct from the workforce tenant used everywhere else — which ADR-028 does not currently sanction.

### Enabling finding (Phase 0 spike, 2026-07-18)

The external portal is a **pure BFF-broker**. The external user's identity **never touches SPE or Graph**; all external-surface SPE + Dataverse access is **app-only / managed identity** (evidence: `GrantExternalAccessEndpoint.cs:237` `ForApp()`; container grant uses a synthetic `i:0#.f|membership|contact_{guid}` login, non-fatal, `:251`; `ExternalDataService.cs:580` app-only Dataverse token; no OBO anywhere under `Api/ExternalAccess`). Microsoft confirms app-only `FileStorageContainer.Selected` + container-type **`ReadContent`** can download/stream document content with **no user identity in the workforce tenant**.

**Consequence:** a CIAM identity used only to authenticate to the BFF is sufficient. A per-external-user **workforce B2B guest is NOT required** for document read/download. This removes the dual-identity concern that gated the migration and resolves the recurring corporate-account-vs-guest login conflict at its source.

---

## Proposed changes to ADR-028

### New MUST rules (external portal surface)

- **MUST** authenticate external portal users against a dedicated **Microsoft Entra External ID (CIAM) tenant** authority, distinct from the workforce tenant used for internal surfaces. This supersedes the B2B-guest identity model for the external-SPA surface.
- **MUST** use Entra External ID **self-service sign-up user flows** for external self-registration.
- **MUST** resolve the External-ID-authenticated caller to a Dataverse `Contact` and enforce authorization server-side via `sprk_externalrecordaccess` (three-plane model). Downstream authorization is **unchanged**.
- **MUST** keep all external-surface SPE + Dataverse access **app-only / managed identity** (BFF-brokered). The external user's token is used **only** to authenticate to the BFF and **MUST NOT** be exchanged for a downstream Graph/SPE/Dataverse token (no OBO on the external path). When document content is exposed, the BFF **MUST** stream it app-only via `FileStorageContainer.Selected` + `ReadContent`.

### New MUST NOT rules

- **MUST NOT** require or provision a per-external-user Entra **B2B guest** object in the workforce tenant for document read/download (eliminated by the broker-only design).
- **MUST NOT** federate the External ID tenant back to internal/workforce identities in a way that reintroduces cross-tenant guest coupling, without a further amendment.

### New documented boundary (limitation E-3)

- **Direct-Office features for external users** — Word/Excel/PowerPoint **for Web co-authoring**, **desktop open via `webUrl`**, **user-identity Copilot grounding**, and **Microsoft Search** — REQUIRE the user's own workforce identity reaching SPE (OBO/delegated) and are therefore **not available to CIAM-only external users**. These remain **out of scope**. A future project needing them for external users must reintroduce workforce B2B guests for those users and file a superseding amendment. (This is the narrow, deliberate "RED" edge of the otherwise GREEN spike.)

### Edits to existing ADR-028 text

- **Line 34 exemption** (external-SPA `PublicClientApplication` / sessionStorage) — reworded to note the external-SPA authenticates against the **Entra External ID authority** (not workforce B2B). All existing exemptions (direct `PublicClientApplication`, sessionStorage per-tab isolation, D-AUTH-7 Bearer-literal allowlist) are **preserved and extended** to the External ID authority.
- **Line 125 Deferred/Out of Scope** — "B2C portal" clarified: **Azure AD B2C remains out of scope (end-of-sale)**; **Entra External ID (CIAM) is now IN scope for the external portal surface** per this amendment.

---

## Alternatives considered (and rejected)

- **Stay on Entra B2B guests** — rejected: B2C-adjacent identity is end-of-sale, and the B2B-guest model is the direct cause of the corporate-vs-guest login conflicts; capacity/UX drawbacks vs External ID MAU.
- **Dual identity (CIAM login + workforce B2B guest per user)** — rejected as the default: the Phase 0 spike shows a workforce identity is unnecessary for a broker-only read portal, and it re-adds per-user provisioning burden. Retained **only** as the escape hatch for the direct-Office features in limitation E-3.
- **Path C (pivot to comply — stay within existing ADR-028)** — not viable: ADR-028 as written neither sanctions a second IdP/tenant nor External ID; compliance would require *not* migrating, which fails the project's requirements and ignores B2C end-of-sale.

---

## Impact if accepted (path B)

- Scope of change: two IdP-sanctioning rules + one broker-only invariant + one documented boundary, applied to the **external portal surface only**. Internal surfaces (workforce tenant, `@spaarke/auth`, PCFs, Code Pages) are **unaffected**.
- Merge ordering: this amendment merges **before or alongside** the dependent code (Phase 2 identity migration). Phases 0–1 (spike + hosting/routing on existing B2B) do not depend on it.
- Apply to both the concise `.claude/adr/ADR-028` and full `docs/adr/ADR-028` on approval.
