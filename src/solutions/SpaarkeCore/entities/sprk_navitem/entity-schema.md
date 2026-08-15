# sprk_navitem Entity Schema

> **Entity Purpose**: Per-user (owner-scoped, private) store for Navigator history rows,
> pins, and bookmarks. Backs the `SprkSidePaneHost` "Navigator" pane's Recent/Pinned/Views
> shapes (FR-06, FR-07). Not visible cross-user — standard Dataverse `UserOwned` security
> (NFR-03). Capture is zero-form-handler / zero-plugin: a persistent app-level pane polls
> `getPageContext()` and upserts rows via host-context `Xrm.WebApi` (no BFF code).
>
> **Source**: `projects/spaarke-side-pane-navigation-history-r1/spec.md` §"Data Model —
> sprk_navitem (per-user)"
> **Solution**: SpaarkeCore (unmanaged) — tenant-portable via solution export/import.

## Entity Definition

| Property | Value |
|----------|-------|
| **Logical Name** | sprk_navitem |
| **Display Name** | Nav Item |
| **Plural Display Name** | Nav Items |
| **Primary Name Field** | sprk_displayname |
| **Ownership Type** | User (UserOwned) — **override of the sprk_todo "User or Team" pattern**; per-user isolation is a hard requirement (NFR-03), so Team ownership is intentionally excluded. |
| **Has Activities** | No |
| **Has Notes** | No |
| **Is Activity** | No (custom entity, NOT a Dataverse Activity) |
| **Description** | A per-user Navigator entry: a captured history row (page/record view), a manual pin, or a bookmark (including raw-URL weblinks). Owner-scoped for per-user isolation; no cross-user visibility beyond standard Dataverse security. |

## Fields

### Primary Fields

| Logical Name | Display Name | Type | Required | Max Length | Description |
|--------------|--------------|------|----------|------------|--------------|
| sprk_navitemid | Nav Item | Uniqueidentifier | Auto | - | Primary key |
| sprk_displayname | Display Name | String | Yes | 200 | Resolved (record/view title) or user-supplied (manual bookmark) label. **Is the primary name attribute** — the spec data model has no separate `sprk_name` field, so `sprk_displayname` fills that role directly (its purpose — "resolved or user-supplied label" — is exactly what a primary name attribute holds). |

### Navigator Shape Fields

| Logical Name | Display Name | Type | Required | Max Length | Description |
|--------------|--------------|------|----------|------------|-------------|
| sprk_type | Type | Choice (global: `sprk_type`) | No | - | `history` / `pin` — distinguishes a captured history row from a user-created pin. |
| sprk_source | Source | Choice (global: `sprk_source`) | No | - | `captured` / `manual` — how the row was created: automatic capture (poll) vs a manual pin/bookmark gesture. |
| sprk_pagetype | Page Type | Choice (global: `sprk_pagetype`) | No | - | `entityrecord` / `entitylist` / `custom` / `weblink` — the kind of page/target this row represents (mirrors `Xrm.Utility.getPageContext().input.pageType`, plus `weblink` for raw-URL bookmarks that don't parse to a host page). |
| sprk_targetlogicalname | Target Logical Name | String | No | 100 | e.g. `sprk_matter`, `custompage`. Nullable for raw-URL links (`sprk_pagetype = weblink`) that have no Dataverse entity target. |
| sprk_targetid | Target Id | String | No | 100 | Normalized GUID (as text) of the target record, when the target is an `entityrecord`. Nullable for pages/lists/weblinks that have no single record id. **Stored as String, not Uniqueidentifier** — see "Type Decision" note below. |
| sprk_url | URL | String (Format: Url) | No | 500 | Raw URL for manual bookmarks that don't parse to a host entity/page shape (weblink bookmarks); also usable as a fallback deep link for any row. |
| sprk_lastvisited | Last Visited | DateTime | No | - | Timestamp of most recent visit/creation; drives ordering (most-recent-first) and the 30-day prune-on-write retention policy (FR-06 / task 031). |
| sprk_visitcount | Visit Count | Whole Number (Integer) | No | - | Optional dedupe/rank counter; incremented on repeat capture of the same target instead of creating a duplicate history row. |

### System Fields

| Logical Name | Display Name | Type | Description |
|--------------|--------------|------|-------------|
| ownerid | Owner | Owner (User only — `UserOwned` entity) | Standard ownership; enforces per-user isolation (NFR-03). Because the entity is `UserOwned` (not "User or Team"), `ownerid` can only ever resolve to a `systemuser`. |
| owningbusinessunit | Owning Business Unit | Lookup → businessunit | Standard |
| statecode | Status | State | 0 = Active / 1 = Inactive |
| statuscode | Status Reason | Status | Default OOB values (Active/Inactive) — no custom status reasons required for r1. |
| createdon | Created On | DateTime | Record creation timestamp |
| modifiedon | Modified On | DateTime | Last modification timestamp |
| createdby | Created By | Lookup | User who created the record |
| modifiedby | Modified By | Lookup | User who last modified the record |

## Choice Values

All three choice fields are backed by **global** option sets (created before the entity's
picklist attributes reference them — schema order is load-bearing, see
`scripts/Deploy-SprkNavItemEntity.ps1`).

### sprk_type (global option set)

| Value | Label |
|-------|-------|
| 100000000 | History |
| 100000001 | Pin |

### sprk_source (global option set)

| Value | Label |
|-------|-------|
| 100000000 | Captured |
| 100000001 | Manual |

### sprk_pagetype (global option set)

| Value | Label |
|-------|-------|
| 100000000 | Entity Record |
| 100000001 | Entity List |
| 100000002 | Custom |
| 100000003 | Weblink |

## Notes

- **Ownership override**: `sprk_todo` (the structural exemplar) is "User or Team" owned;
  `sprk_navitem` is **UserOwned only**. This is an intentional deviation, not an oversight —
  Navigator history/pins are inherently personal and must never be assignable to a team
  (NFR-03, spec MUST NOT list: "personal pins are `sprk_navitem` only (per-user);
  `sprk_monitor` stays the shared record-level flag").
- **Primary name field**: `sprk_displayname`, not a separate `sprk_name` (see Primary Fields
  note above). This differs from `sprk_todo` (which has both `sprk_name` and separate detail
  fields) because the spec's `sprk_navitem` data model table defines exactly one label field.
- **Type Decision — `sprk_targetid` is String (Text), not Uniqueidentifier**: The spec lists
  this field as "Text/GUID". `sprk_targetid` is a **polymorphic** foreign reference — its
  target entity varies row-to-row per `sprk_targetlogicalname` (matter, project, custom page,
  etc.) and is nullable for non-record targets (lists/weblinks). Dataverse has no mechanism to
  create a non-PK `Uniqueidentifier` attribute that is a real (validated) FK into an
  arbitrary, row-varying target entity, and the `sprk_todo` exemplar already establishes the
  precedent for exactly this shape: `sprk_regardingrecordid` ("Resolver: normalized GUID of
  the regarding record") is a **String** field, 100 chars, storing the GUID as text with no
  referential-integrity constraint, precisely because the target entity is resolved
  dynamically rather than declared via a `Lookup`/relationship. `sprk_navitem` follows the
  same pattern for the same reason. This is a documented design decision (not a guess) —
  escalation trigger did not fire because the exemplar already resolves this exact type
  question for an analogous polymorphic-target-id field.
- **No regarding-lookup / resolver pattern**: Unlike `sprk_todo` (11 specific regarding
  lookups + `PolymorphicResolverService`, per ADR-024), `sprk_navitem` does NOT use typed
  Dataverse lookups to the target record. The target is denormalized as
  `sprk_targetlogicalname` + `sprk_targetid` (text pair) precisely because Navigator targets
  include non-Dataverse shapes (entity lists, custom pages, raw weblinks) that ADR-024's
  lookup-based resolver cannot represent, and because per-row security trimming happens at
  read time against the live target (spec MUST: "security-trim cached labels at render time"),
  not via Dataverse's own relationship security.
- **Deployment**: Created via `scripts/Deploy-SprkNavItemEntity.ps1` (Web API + PowerShell —
  PAC CLI has no `pac table create`). Global option sets are created FIRST, then the
  `UserOwned` entity + primary name, then the remaining fields (picklists bound to the global
  option sets), then publish. Idempotent — safe to re-run.
- **Solution**: SpaarkeCore (unmanaged). Tenant-portable via export/import.
- **Not in scope for this doc**: security roles (task 021), retention/prune-on-write logic
  (task 031), and the Navigator code page itself (task 040+) — this document covers schema
  only.
