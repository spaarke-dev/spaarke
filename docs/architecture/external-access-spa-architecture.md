# External Access SPA — Architecture Reference

> **Domain**: Secure Project & External Access Platform (SDAP)
> **Status**: Active — production implementation
> **Last Updated**: 2026-03-19

---

## Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| Entra B2B guests (not B2C) | External users (attorneys, corporate counsel) already have Microsoft 365 accounts — SSO with no new credentials, no separate B2C tenant to operate |
| BFF-only data access (not Power Pages `/_api/`) | Single auditable path, managed identity auth, BFF enforces project access before any query, avoids maintaining two auth paths and field-whitelist site settings |
| MSAL authorization code + PKCE (not implicit grant) | Implicit grant is deprecated; MSAL handles silent refresh, MFA, and conditional access automatically |
| HashRouter (not BrowserRouter) | Power Pages serves the SPA as a single static file — server-side routing returns 404; all navigation must be hash-based |
| SessionStorage for tokens | Tokens survive page refresh but are isolated per tab — avoids token leakage across shared workstation scenarios common in legal environments |
| Three-plane access model | Dataverse records (Power Pages table permissions), SPE files (BFF-managed Graph permissions), AI Search (BFF constructs filter at query time) — each plane requires independent access management |
| Per-endpoint auth filter (ADR-008) | `ExternalCallerAuthorizationFilter` resolves Dataverse Contact identity after JWT validation; follows endpoint-filter-over-global-middleware rule |
| Redis 60s TTL for participation data | Avoids Dataverse query on every BFF call; invalidated immediately on grant/revoke/close operations |

---

## Identity Model: Entra B2B

External users are **Azure AD B2B guest accounts** in the main Spaarke workforce tenant. They authenticate with their existing Microsoft 365 credentials. The BFF validates their JWT and resolves their Dataverse Contact identity via `preferred_username` claim.

**Auth flow**: Authorization code + PKCE → tokens stored in sessionStorage → every BFF call attaches Bearer token → `ExternalCallerAuthorizationFilter` resolves Contact by email → loads project participations from Redis (60s TTL, falls back to Dataverse `sprk_externalrecordaccess`).

**Limitation**: External users without Microsoft accounts cannot authenticate. Non-Microsoft users would require a B2C configuration.

---

## Three-Plane Access Model

| Plane | What It Controls | Who Manages It |
|-------|-----------------|----------------|
| **Plane 1 — Power Pages** | Dataverse record access via parent-chain table permissions | Automatic (cascades from participation record) |
| **Plane 2 — SPE Files** | SharePoint Embedded container membership | BFF-managed via Graph API on grant/revoke |
| **Plane 3 — AI Search** | Azure AI Search query scope | BFF constructs `search.in` filter at query time from active participations |

**Parent-chain model** (Plane 1): Creating one `sprk_externalrecordaccess` record + assigning the web role grants the contact access to the parent project and all child records (documents, events) automatically. Revoking = deactivating that record. No per-record grants needed.

**Access level enforcement**: Access level (`ViewOnly`, `Collaborate`, `FullAccess`) is embedded in the `/me` response. Client-side capability flags (`canUpload`, `canDownload`, etc.) are UX-only. Actual enforcement is server-side in the BFF via `ExternalCallerAuthorizationFilter` and per-endpoint access checks.

---

## Power Pages Hosting Constraints

| Constraint | Impact |
|-----------|--------|
| Single-file SPA hosting | `HashRouter` required — `BrowserRouter` causes 404 on direct URL navigation |
| No server-side routing | All routes must be hash-based (`#/project/{id}`) |
| Max parent-chain depth ~4 | Spaarke uses 2–3 levels — well within limit |
| No polymorphic parent lookups | Use explicit single-type relationships only |
| Web role assignment has no expiry | Use `sprk_externalrecordaccess.sprk_expirydate` + scheduled deactivation |
| B2B guests require Microsoft account | Non-Microsoft external users would need B2C |

**Build output**: `vite-plugin-singlefile` inlines all JS/CSS into a single `dist/index.html` (~800 KB uncompressed, ~1.1 MB base64-encoded in Dataverse). Within the 5 MB web resource limit.

**Deployment**: `npm run build` → `scripts/Deploy-ExternalWorkspaceSpa.ps1` (base64 encode → Dataverse Web API → PublishXml). Not deployed via `pac pages upload-code-site` due to assembly conflict in PAC CLI 1.46.x.

---

## Related Documents

| Document | Purpose |
|----------|---------|
| [uac-access-control.md](uac-access-control.md) | Unified Access Control model (three-plane detail) |
| [sdap-auth-patterns.md](sdap-auth-patterns.md) | Auth patterns including Pattern 7 (MSAL ssoSilent for code pages) |
| [`docs/guides/EXTERNAL-ACCESS-ADMIN-SETUP.md`](../guides/EXTERNAL-ACCESS-ADMIN-SETUP.md) | Power Pages config, table permissions, site settings |

---

*Last Updated: 2026-03-19*
