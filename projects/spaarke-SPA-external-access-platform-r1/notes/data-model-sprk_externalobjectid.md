# Data-model note — `contact.sprk_externalobjectid` (Task 004)

> Added 2026-07-19 by task 004 (spaarke-SPA-external-access-platform-r1). Live change applied to `spaarkedev1`.

## Field

| Property | Value |
|---|---|
| Entity | `contact` (standard) |
| Schema name | `sprk_ExternalObjectId` |
| Logical name | `sprk_externalobjectid` |
| Type | String (Text), MaxLength **100** |
| Required | None (optional) |
| MetadataId (dev) | `b28603f2-bd83-f111-8076-7ced8ddc4cc6` |
| Solutions | **SpaarkeCore**, **SpaarkeMaster** (unmanaged) — mirrors the `sprk_externalrecordaccess` + existing contact `sprk_*` column convention |
| Environment | `spaarkedev1` (created + published 2026-07-19) |

## Purpose

Stores the stable **Microsoft Entra External ID (CIAM) object id (`oid`)** for the external user
linked to this Contact. It is the **immutable, non-spoofable resolution key** for external-caller
authorization.

- **NOT** email (mutable, social-IdP-variable) and **NOT** `sub` (pairwise/per-app).
- **Written by** the admin-initiated CIAM provisioner (task 025), persisting the `oid` returned
  from Graph `POST /users`.
- **Read by** `ExternalCallerAuthorizationFilter` / `ExternalParticipationService` (task 023) to
  resolve the Contact by `oid`.

Supersedes the Power-Pages-only `adx_externalidentity` linkage, which is retired with the Power
Pages site (Phase 3).

## Transport

Present in **SpaarkeCore** and **SpaarkeMaster** unmanaged solutions on dev. When promoting to
`spaarke-demo` / prod, include via whichever of these solutions the environment pipeline ships
(same as the sibling external-access schema). Additive-only — no existing Contact attribute or
relationship was modified.
