# Membership Resolution Service — Operator Guide

> **Status**: Shipped in R3 (`spaarke-platform-foundations-r3`, 2026-06-22)
> **Audience**: Spaarke operators, system administrators, and makers configuring per-entity overrides — **not** developers extending the service
> **Applies To**: BFF API (`Sprk.Bff.Api`), Dataverse, optional Azure Service Bus topic + Redis cache
> **Related**:
> - Architecture deep-dive: [`docs/architecture/membership-resolution-pattern.md`](../architecture/membership-resolution-pattern.md)
> - Binding ADR: [`.claude/adr/ADR-034-user-record-membership.md`](../../.claude/adr/ADR-034-user-record-membership.md)
> - Background-job infrastructure (recon job): [`.claude/adr/ADR-036-background-job-infrastructure.md`](../../.claude/adr/ADR-036-background-job-infrastructure.md)
> - Null-Object kill-switch (the three feature flags): [`.claude/adr/ADR-032-bff-nullobject-kill-switch.md`](../../.claude/adr/ADR-032-bff-nullobject-kill-switch.md)
> - Auth model (caller identity resolution): [`docs/guides/auth-deployment-setup.md`](auth-deployment-setup.md)

---

## Table of Contents

- [What This Service Does](#what-this-service-does)
- [Key Concepts](#key-concepts)
- [Endpoints — Quick Reference](#endpoints--quick-reference)
- [User Endpoint — Examples](#user-endpoint--examples)
- [Admin Endpoints — Examples](#admin-endpoints--examples)
- [Configuration](#configuration)
- [Deployment Status Checklist](#deployment-status-checklist)
- [Smoke Checks — Verify It's Working](#smoke-checks--verify-its-working)
- [Common Issues + Troubleshooting](#common-issues--troubleshooting)
- [Operational Cadence](#operational-cadence)
- [Related Documentation](#related-documentation)

---

## What This Service Does

The Membership Resolution Service answers one question in one place:

> **"Which records of entity type T is this user associated with, and in what role?"**

Before R3, every AI playbook and UI surface that needed to answer this question wrote its own FetchXML query. Some of those queries silently broke (the R2 `notification-new-documents.json` playbook joined through a Dataverse entity that didn't exist and returned zero rows in production). The Membership Resolution Service replaces those ad-hoc queries with one canonical mechanism that every consumer goes through.

### Concrete example

An attorney logs into Spaarke and asks "show me my matters." Behind the scenes:

1. The UI calls `GET /api/users/me/memberships/sprk_matter`.
2. The service identifies the attorney (Entra ID `oid` claim → Dataverse `systemuserid` via `azureactivedirectoryobjectid`).
3. It looks up every Lookup column on `sprk_matter` that points to a person/team/org table (e.g., `sprk_assignedattorney1`, `sprk_assignedattorney2`, `sprk_assignedparalegal1`, `ownerid`, `sprk_assignedlawfirm1`, `sprk_assignedlawfirm2`, …).
4. It runs ONE FetchXML query that asks Dataverse "give me every matter where ANY of those columns points at this user (or this user's team, business unit, contact, account, or organization)."
5. It returns a JSON shape grouping the matter IDs by role: `{ "owner": [...], "assignedAttorney": [...], "assignedLawFirm": [...] }`.

The same endpoint works for `sprk_document`, `sprk_event`, `sprk_task`, `sprk_opportunity`, and any other entity — discovery is metadata-driven, so adding a new entity does **not** require code or configuration changes in most cases.

---

## Key Concepts

| Concept | Plain-language description |
|---|---|
| **Identity normalization** | Spaarke knows a person isn't just a `systemuser` row. A "person" is a 6-tuple: their systemuser id, their contact id, the teams they belong to, their business unit, their parent account, and any organizations (`sprk_organization`) they're affiliated with. The service resolves all six in parallel before querying for memberships. |
| **Discovery model** | The service looks at the Dataverse metadata of any entity and **automatically picks** the Lookup columns that count as "membership" columns — they're the ones whose targets are one of the six identity tables. A new Lookup added in Dataverse appears automatically in membership results within an hour (the metadata cache TTL). |
| **Role name** | The friendly label returned to callers (e.g., `"assignedAttorney"`). Derived automatically from the column logical name by stripping `sprk_`, stripping trailing digits, and camelCasing: `sprk_AssignedAttorney1` → `assignedAttorney`. Operators can override via `FieldRoleOverrides` for edge cases. |
| **Per-entity overrides** | Most entities need NO configuration — defaults Just Work. Overrides exist for: (a) collapsing numbered fields under one role (e.g., `sprk_assignedlawfirm1` + `sprk_assignedlawfirm2` both → `"assignedLawFirm"`); (b) excluding a column that LOOKS like membership but isn't; (c) force-including a column that would otherwise be globally excluded. |
| **Phase 1A vs Phase 2** | Phase 1A (live per-request FetchXML) ships unconditionally — the endpoint works on day one. Phase 2 (materialized junction table `sprk_userentityassociation` + Service Bus event sync + Redis cache invalidation) is **deployment-gated**: ships in the code but stays dormant until an operator flips three feature flags. The endpoint behaves identically in both phases — consumers see no difference. |
| **The Q4 organization mapping** | `sprk_assignedlawfirm1` and `sprk_assignedlawfirm2` on `sprk_matter` point to `sprk_organization`, **not** `contact`. The service resolves them via a configurable Lookup field on `sprk_organization` that points back to `systemuser` (see [Configuration → OrganizationLookup](#5-organization-mapping-required-when-using-sprk_organization-lookups)). |
| **Recon backstop** | A nightly background job (`MembershipReconciliationJob`) re-scans source-of-truth Lookups and synthesizes membership events. This is **load-bearing**: most identity-Lookup mutations happen via maker portals / Power Automate / plugins — NOT BFF endpoints — so real-time event publishing alone is not enough. Max staleness: 24 hours. |
| **The three Phase 2 feature flags** | `Membership:EventPublisher:Enabled`, `Membership:JunctionUpdater:Enabled`, `Membership:CacheInvalidator:Enabled` — each independently gates one Phase 2 component. All default to `false`. See [Deployment Status Checklist](#deployment-status-checklist). |

---

## Endpoints — Quick Reference

| Method | Path | Auth | What it does |
|---|---|---|---|
| `GET` | `/api/users/me/memberships/{entityType}` | Any authenticated user | Returns the caller's memberships on the target entity, grouped by role |
| `GET` | `/api/admin/membership/discovered/{entityType}` | `SystemAdmin` policy | Audits the discovery output — which Lookup columns the service picked up, which were excluded, which were ignored (with reasons) |
| `POST` | `/api/admin/membership/refresh-metadata` | `SystemAdmin` policy | Invalidates the discovery cache so the next call re-reads Dataverse metadata (use after schema changes) |
| `GET` | `/api/admin/jobs/membership-reconciliation/status` | `SystemAdmin` policy | Status of the nightly recon job (last run, next scheduled tick, last result) |
| `POST` | `/api/admin/jobs/membership-reconciliation/trigger` | `SystemAdmin` policy | Trigger the recon job immediately (don't wait for the nightly schedule) |
| `GET` | `/api/admin/jobs/membership-reconciliation/history` | `SystemAdmin` policy | Recent run history with per-entity counts (verified / removed / errors) |
| `POST` | `/api/admin/jobs/membership-reconciliation/disable` | `SystemAdmin` policy | Disable the nightly recon (use sparingly — junction freshness depends on it) |
| `POST` | `/api/admin/jobs/membership-reconciliation/enable` | `SystemAdmin` policy | Re-enable the nightly recon |

The job-management endpoints (`/api/admin/jobs/membership-reconciliation/*`) are the standard background-job admin surface — same shape as every other scheduled job in the BFF.

---

## User Endpoint — Examples

### Basic call — "my matters"

```http
GET /api/users/me/memberships/sprk_matter HTTP/1.1
Host: spe-api-dev-67e2xz.azurewebsites.net
Authorization: Bearer eyJ0eXAiOiJKV1QiLCJh…
```

Response:

```json
{
  "entityType": "sprk_matter",
  "personIdentity": {
    "systemUserId": "6c1f8a93-9f01-44ca-a3bd-1adb6b2c8b71",
    "contactId": "0e2c4f15-4d76-4ea4-8b8e-3aa118b6c2cf",
    "primaryEmail": "jane.doe@spaarke.com",
    "teamIds": ["7d0bf91a-6a3f-4f15-8a1c-cb5a3b3f4422"],
    "businessUnitId": "e2c7d0aa-3b14-4d4e-9c9c-2c0a85e3b6f1",
    "organizationIds": ["a5cf1b88-aaaa-bbbb-cccc-d3e4f5a6b7c8"]
  },
  "ids": [
    "11111111-1111-1111-1111-111111111111",
    "22222222-2222-2222-2222-222222222222",
    "33333333-3333-3333-3333-333333333333"
  ],
  "byRole": {
    "owner": ["11111111-1111-1111-1111-111111111111"],
    "assignedAttorney": [
      "11111111-1111-1111-1111-111111111111",
      "22222222-2222-2222-2222-222222222222"
    ],
    "assignedParalegal": ["33333333-3333-3333-3333-333333333333"],
    "assignedLawFirm": ["22222222-2222-2222-2222-222222222222"]
  },
  "count": 3,
  "cacheExpiresAt": "2026-06-22T15:34:00+00:00",
  "continuationToken": null
}
```

### Filter by role — "matters where I'm the owner"

```http
GET /api/users/me/memberships/sprk_matter?roles=owner
```

### Filter by identity type — "matters my team is on, not my user account"

```http
GET /api/users/me/memberships/sprk_matter?identityTypes=Team
```

Valid identity types: `SystemUser`, `Contact`, `Team`, `BusinessUnit`, `Account`, `Organization` (case-insensitive). Multiple are CSV: `?identityTypes=SystemUser,Team`.

### Pagination

```http
GET /api/users/me/memberships/sprk_matter?limit=100
```

Default `limit` is 500 (clamped server-side to a hard ceiling). If the response includes a non-null `continuationToken`, pass it back on the next call:

```http
GET /api/users/me/memberships/sprk_matter?limit=100&continuationToken=eyJza2lwIjoxMDB9
```

### Transitive expansion (1-hop max)

```http
GET /api/users/me/memberships/sprk_matter?includeRelated=sprk_document,sprk_event
```

This returns matters the caller is a member of PLUS the documents and events 1-hop reachable from those matters. Chains deeper than 1 hop are rejected with `400 BadRequest` and a `transitive-chain-too-deep` reason tag. (Phase 1A accepts but ignores the parameter; full transitive expansion lights up with R3 task 054.)

### Errors

| Status | Meaning | Common cause |
|---|---|---|
| `400` | Bad request | Missing `entityType`, malformed query params, `includeRelated` deeper than 1 hop |
| `401` | Unauthenticated, OR authenticated principal has no matching `systemuser` row in Dataverse | Either no token, or the user has not been provisioned in Dataverse for this org |
| `500` | Server error | Dataverse outage, malformed configuration — check server logs |

---

## Admin Endpoints — Examples

### Audit discovery output for an entity

> **When to use**: Before deploying a new entity type, OR when an end-user reports a missing role, OR after changing per-entity overrides.

```http
GET /api/admin/membership/discovered/sprk_matter HTTP/1.1
Authorization: Bearer eyJ0eXAi…   (SystemAdmin policy)
```

Response (abbreviated):

```json
{
  "entityType": "sprk_matter",
  "discoveredAt": "2026-06-22T14:12:00+00:00",
  "discoveredFields": [
    { "field": "ownerid",                 "role": "owner",            "identityType": "SystemUser",   "target": "systemuser",       "source": "auto" },
    { "field": "owningteam",              "role": "owningTeam",       "identityType": "Team",         "target": "team",             "source": "auto" },
    { "field": "owningbusinessunit",      "role": "owningBusinessUnit","identityType": "BusinessUnit","target": "businessunit",     "source": "auto" },
    { "field": "sprk_assignedattorney1",  "role": "assignedAttorney", "identityType": "SystemUser",   "target": "systemuser",       "source": "auto" },
    { "field": "sprk_assignedattorney2",  "role": "assignedAttorney", "identityType": "SystemUser",   "target": "systemuser",       "source": "auto" },
    { "field": "sprk_assignedparalegal1", "role": "assignedParalegal","identityType": "SystemUser",   "target": "systemuser",       "source": "auto" },
    { "field": "sprk_assignedlawfirm1",   "role": "assignedLawFirm",  "identityType": "Organization", "target": "sprk_organization","source": "override" },
    { "field": "sprk_assignedlawfirm2",   "role": "assignedLawFirm",  "identityType": "Organization", "target": "sprk_organization","source": "override" }
  ],
  "excludedFields": [
    { "field": "createdby",  "reason": "global-exclusion" },
    { "field": "modifiedby", "reason": "global-exclusion" }
  ],
  "ignoredFields": [
    { "field": "sprk_parentmatterid", "reason": "target-table-not-in-identity-list", "target": "sprk_matter" }
  ]
}
```

The `source` field tells you whether the role was auto-derived (`"auto"`) or came from your `FieldRoleOverrides` (`"override"`).

### Refresh the metadata cache

> **When to use**: After adding/removing a Lookup column in Dataverse, OR after changing `Membership:EntityOverrides` in `appsettings.json` (then restart the BFF to pick up the config change, then call this to flush the discovery cache).

Refresh a single entity:

```http
POST /api/admin/membership/refresh-metadata HTTP/1.1
Content-Type: application/json
Authorization: Bearer eyJ0eXAi…

{ "entityType": "sprk_matter" }
```

Refresh ALL entities (empty body, or omit the body entirely):

```http
POST /api/admin/membership/refresh-metadata HTTP/1.1
Authorization: Bearer eyJ0eXAi…
```

Response:

```json
{
  "refreshed": ["sprk_matter"],
  "at": "2026-06-22T14:15:00+00:00"
}
```

### Trigger the nightly recon job on-demand

```http
POST /api/admin/jobs/membership-reconciliation/trigger HTTP/1.1
Authorization: Bearer eyJ0eXAi…
```

The job runs immediately (out-of-band from the cron schedule) and writes a row to its run-history table. Check status:

```http
GET /api/admin/jobs/membership-reconciliation/history
Authorization: Bearer eyJ0eXAi…
```

Response includes per-entity breakdown: `discoveredFields`, `parentRowsScanned`, `verified`, `removed`, `errors`.

---

## Configuration

All configuration lives under the `Membership` section in `appsettings.json` (and per-environment overrides in `appsettings.{Environment}.json`). Operators do **not** need to redeploy code to change any of these — restart the BFF to pick up changes, then `POST /api/admin/membership/refresh-metadata` to flush the discovery cache.

### 1. Identity tables (rarely needs changing)

```json
{
  "Membership": {
    "IncludedIdentityTables": [
      { "Table": "systemuser",         "IdentityType": "SystemUser" },
      { "Table": "contact",            "IdentityType": "Contact" },
      { "Table": "team",               "IdentityType": "Team" },
      { "Table": "businessunit",       "IdentityType": "BusinessUnit" },
      { "Table": "account",            "IdentityType": "Account" },
      { "Table": "sprk_organization",  "IdentityType": "Organization" }
    ]
  }
}
```

The default list covers every identity surface Spaarke uses. Only change this if your tenant has a custom identity table (rare — discuss with engineering first).

### 2. Global field exclusions (rarely needs changing)

```json
{
  "Membership": {
    "GlobalFieldExclusions": [
      "createdby",
      "modifiedby",
      "createdonbehalfby",
      "modifiedonbehalfby"
    ]
  }
}
```

These four columns appear on every Dataverse table but represent touch-history, not association. Default value covers what every tenant needs.

### 3. Per-entity overrides (the common configuration knob)

Used for: collapsing numbered fields under one role, excluding columns that look like membership but aren't, or force-including a globally-excluded column on one specific entity.

Worked example — `sprk_matter` collapses the two law-firm columns under one role:

```json
{
  "Membership": {
    "EntityOverrides": {
      "sprk_matter": {
        "FieldRoleOverrides": {
          "sprk_assignedlawfirm1": "assignedLawFirm",
          "sprk_assignedlawfirm2": "assignedLawFirm"
        },
        "ExcludedFields": [],
        "IncludedFields": []
      },
      "sprk_event": {
        "FieldRoleOverrides": {
          "sprk_assignedattorney1": "assignedAttorney",
          "sprk_assignedattorney2": "assignedAttorney",
          "sprk_assignedparalegal1": "assignedParalegal",
          "sprk_assignedparalegal2": "assignedParalegal"
        },
        "ExcludedFields": [],
        "IncludedFields": []
      }
    }
  }
}
```

**Rule of thumb**: only add an override when the auto-discovered role name is wrong or you need to disambiguate. Most entities work out of the box.

### 4. Metadata cache TTL

```json
{
  "Membership": {
    "MetadataCacheTtlMinutes": 60
  }
}
```

Default 60 minutes. Lower = faster pick-up of schema changes; higher = fewer Dataverse `EntityDefinitions` calls. Operators can ALWAYS force-flush via `POST /api/admin/membership/refresh-metadata` without restarting the BFF.

### 5. Organization mapping (REQUIRED when using `sprk_organization` Lookups)

> **Critical**: If your tenant uses `sprk_assignedlawfirm1` / `sprk_assignedlawfirm2` on `sprk_matter` (or any other `Lookup → sprk_organization` field), you MUST set `Membership:OrganizationLookup:UserLookupField`. Without it, organization memberships silently return empty (logged as Info, not an error — see [Troubleshooting](#common-issues--troubleshooting)).

```json
{
  "Membership": {
    "OrganizationLookup": {
      "UserLookupField": "sprk_owneruser",
      "MaxOrganizationsPerUser": 1000
    }
  }
}
```

`UserLookupField` is the logical name of a Lookup column on the `sprk_organization` table that points to `systemuser`. The resolver queries `sprk_organization` rows where this column equals the caller's systemuser GUID. Common operator choices:

- `sprk_owneruser` — if your tenant adds a "primary user" column to organizations
- `sprk_relationshipowner` — if your CRM tracks relationship ownership at the organization level
- A custom column added specifically for this purpose

Leaving the value as `""` (empty string) is **safe** — the resolver returns an empty organization list and logs once at startup. No errors, no broken responses; just no organization memberships.

See the design decision notes at `projects/spaarke-platform-foundations-r3/notes/sprk-organization-mapping-decision.md` for the rationale behind this configurable mechanism.

### 6. Phase 2 — the three feature flags

All three default to `false`. Each independently gates one Phase 2 component. Until ALL THREE are flipped (and the corresponding Azure resources are deployed), the system runs on Phase 1A live FetchXML — fully functional, just without the materialized junction.

#### 6a. EventPublisher — publishes events when BFF endpoints mutate a Lookup

```json
{
  "Membership": {
    "EventPublisher": {
      "Enabled": false,
      "TopicName": "sprk-membership-changes"
    }
  }
}
```

| When to flip to `true` | After the operator deploys the Service Bus topic `sprk-membership-changes` (see operator follow-up runbook). The topic Bicep is authored in R3 task 071; operator deploy happens out-of-band. |
| When to leave `false` | Topic not deployed yet. The Null-Object peer logs a quiet no-op and the BFF starts cleanly with NO Azure Service Bus dependency. |

#### 6b. JunctionUpdater — consumes the topic and writes to `sprk_userentityassociation`

```json
{
  "Membership": {
    "JunctionUpdater": {
      "Enabled": false,
      "TopicName": "sprk-membership-changes",
      "SubscriptionName": "recon-junction-updater",
      "ServiceBusNamespace": "",
      "MaxConcurrentCalls": 5
    }
  }
}
```

| When to flip to `true` | After the topic + subscription are deployed AND `ServiceBusNamespace` is populated with the FQDN (e.g., `spaarkesb-dev.servicebus.windows.net`). Auth uses Managed Identity via `DefaultAzureCredential` per ADR-028 — no connection string needed. |
| When to leave `false` | Topic/subscription not deployed, or namespace not configured. Null-Object peer registers in place and no subscription is opened. |

`MaxConcurrentCalls=5` matches the existing background-job processor and is safe for the expected mutation-event volume. Tune higher only if recon-job dispatches start backing up (visible in the `errors` count on `/api/admin/jobs/membership-reconciliation/history`).

#### 6c. CacheInvalidator — Redis pub/sub for cross-instance cache invalidation

```json
{
  "Membership": {
    "CacheInvalidator": {
      "Enabled": false,
      "Channel": "membership-cache-invalidate"
    }
  }
}
```

| When to flip to `true` | When `Redis:Enabled=true` in this environment AND a Redis connection string is configured. Without Redis, leave this `false`. |
| When to leave `false` | No Redis (local dev, CI, environments without Redis). The per-user membership cache still works via its 5-minute TTL — pub/sub invalidation is a latency optimization, NOT a correctness requirement. |

> **Correctness backstop**: even if all three Phase 2 flags are `false` AND no recon job ever runs, the user endpoint still returns correct results because the per-request FetchXML always reads source-of-truth Lookups directly.

### 7. Reconciliation job

```json
{
  "Membership": {
    "Reconciliation": {
      "Enabled": true,
      "CronSchedule": "0 2 * * *",
      "EntityTypes": [
        "sprk_matter",
        "sprk_document",
        "sprk_event",
        "sprk_task",
        "sprk_opportunity"
      ],
      "FetchPageSize": 500,
      "OrphanFetchPageSize": 500
    }
  }
}
```

Defaults are production-safe. The job is **enabled by default** because it does NOT depend on the Service Bus topic — it dispatches directly to the junction updater handler. Add new entity types to `EntityTypes` as your tenant rolls out membership tracking on additional tables. The default cron `"0 2 * * *"` runs at 02:00 UTC every day; change as needed for your tenant's quiet window.

---

## Deployment Status Checklist

Use this checklist when bringing up a new environment OR auditing an existing one.

### Required (Phase 1A — always-on)

- [ ] **Dataverse entities present**: `sprk_backgroundjob`, `sprk_backgroundjobrun` (background-job scheduling infrastructure per ADR-036). Verified by checking the model-driven app — both should appear under "Background Jobs."
- [ ] **`Membership` configuration section present** in `appsettings.{Environment}.json` (copy from `appsettings.Development.json.template` as starting point).
- [ ] **`Membership:OrganizationLookup:UserLookupField`** set IF your tenant uses any `Lookup → sprk_organization` membership column. Leave empty otherwise.
- [ ] **BFF restarted** after `appsettings` changes.
- [ ] **Smoke check 1 passes** (see below).

### Required (recon job — runs nightly by default)

- [ ] **Recon job seeded** in Dataverse — verify a `sprk_backgroundjob` row exists with `sprk_jobid = "membership-reconciliation"`. This is seeded automatically at BFF startup the first time it runs against a fresh environment.
- [ ] **`Membership:Reconciliation:EntityTypes`** lists every entity your tenant tracks memberships on.
- [ ] **Smoke check 3 passes** (recon trigger + history).

### Optional (Phase 2 — event-driven sync)

Phase 2 lights up incrementally. You can run any subset of the three components:

- [ ] **Service Bus topic deployed**: `sprk-membership-changes` with subscription `recon-junction-updater` (see operator-follow-up runbook for the deploy procedure — R3 task 071's Bicep module `membership-topic.bicep`).
- [ ] **Junction entity deployed**: `sprk_userentityassociation` (7 columns + composite alternate key `sprk_uea_natural_key` on `{personId, personIdType, entityLogicalName, entityRecordId, sourceField}`).
- [ ] **`Membership:EventPublisher:Enabled = true`** (after topic deploy).
- [ ] **`Membership:JunctionUpdater:Enabled = true`** AND **`ServiceBusNamespace`** populated (after topic + subscription deploy).
- [ ] **Redis configured** for this environment (`Redis:Enabled=true` + connection string).
- [ ] **`Membership:CacheInvalidator:Enabled = true`** (after Redis configured).
- [ ] **Smoke check 4 passes** (Phase 2 event flow end-to-end).

### Feature-flag matrix — current-state guide

| Stage | EventPublisher.Enabled | JunctionUpdater.Enabled | CacheInvalidator.Enabled | Behavior |
|---|---|---|---|---|
| **Default ship state** | `false` | `false` | `false` | Phase 1A only. Per-request FetchXML. Recon job populates junction directly (independent of topic). Correct results, no Azure dependencies beyond Dataverse. |
| **Cache-only** | `false` | `false` | `true` (when Redis enabled) | Phase 1A + Redis pub/sub for any cache invalidations that DO happen (e.g., recon-triggered ones via the shared handler). |
| **Topic deployed, no Redis** | `true` | `true` | `false` | Phase 2 sync via Service Bus. Cache invalidation falls back to 5-min TTL. |
| **Full Phase 2** | `true` | `true` | `true` | Real-time event sync + sub-second cache invalidation across instances. |

---

## Smoke Checks — Verify It's Working

Each check is independent. Run them in order on a new environment; on an existing environment run only the ones relevant to what you changed.

### Step 1 — Discovery returns expected fields for a known entity

```http
GET /api/admin/membership/discovered/sprk_matter
Authorization: Bearer <SystemAdmin token>
```

**Pass criteria**: HTTP 200; `discoveredFields[]` includes `ownerid`, all your `sprk_assigned*` columns, etc. If `sprk_assignedlawfirm1` and `sprk_assignedlawfirm2` appear with `"identityType": "Organization"` and `"source": "override"`, your `FieldRoleOverrides` are wired correctly.

**Fail criteria**: `404 Not Found` → entity doesn't exist in Dataverse. Empty `discoveredFields[]` → all Lookups got excluded; check your `GlobalFieldExclusions` and `EntityOverrides`.

### Step 2 — User endpoint returns expected IDs for a known user

Authenticate as a user with known matter assignments (one you can verify by hand in the model-driven app):

```http
GET /api/users/me/memberships/sprk_matter
Authorization: Bearer <user token via OBO>
```

**Pass criteria**: HTTP 200; `ids[]` contains the matter GUIDs you can see assigned to that user in Dataverse. `personIdentity` shows their systemUserId, businessUnitId, and (if applicable) organizationIds.

**Fail criteria**:
- `401` with detail "Authenticated principal is not provisioned as a systemuser" → user has an Entra ID account but no `systemuser` row with matching `azureactivedirectoryobjectid`. Provision via Dataverse Application User flow.
- `200` with empty `ids[]` and a non-empty `personIdentity` → the user IS resolved but no matters match. Cross-check by hand in Dataverse; if memberships exist, see [Troubleshooting → User sees empty results](#user-sees-empty-results).

### Step 3 — Recon job trigger + history

```http
POST /api/admin/jobs/membership-reconciliation/trigger
Authorization: Bearer <SystemAdmin token>
```

Then within a minute:

```http
GET /api/admin/jobs/membership-reconciliation/history
Authorization: Bearer <SystemAdmin token>
```

**Pass criteria**: most recent run shows `"success": true`, `errors: 0`, and per-entity counts for every entity in `Reconciliation:EntityTypes`. `processedItems` > 0 if your tenant has any assigned-attorney / assigned-paralegal / etc. data.

**Fail criteria**: `errors > 0` → check BFF logs for the per-row exception. `processedItems = 0` AND your tenant has membership data → check `Reconciliation:EntityTypes` covers the right entities AND discovery for those entities returns non-empty fields (Step 1).

### Step 4 — Phase 2 event flow (only after all 3 flags flipped + Service Bus + Redis deployed)

1. Identify a `sprk_matter` row with a known `sprk_assignedattorney1` value.
2. Change `sprk_assignedattorney1` to a different user via Dataverse UI or Power Automate.
3. Within ~5 seconds, query `sprk_userentityassociation` rows — a new row should exist for the new assignee + the old row should be removed (or its `lastSyncedOn` updated if Updated was synthesized).
4. Within the same ~5 seconds, BFF logs should show a `MembershipCacheInvalidator` publish AND a `MembershipCacheInvalidationSubscriber` evict on every instance.

**Pass criteria**: Junction row + cache invalidation observed within seconds. **Fail criteria**: see [Troubleshooting → Stale data](#stale-data-after-an-assignment-changes).

---

## Common Issues + Troubleshooting

### User sees empty results

**Symptom**: `GET /api/users/me/memberships/sprk_matter` returns `200 OK` with empty `ids[]`, but you can verify in Dataverse that the user IS assigned to matters.

**Diagnostic order**:

1. Run `GET /api/admin/membership/discovered/sprk_matter`. Confirm the assignment columns (`sprk_assignedattorney1`, etc.) appear in `discoveredFields[]`. If they don't, check:
   - Is the column actually a Lookup type in Dataverse (not Text)?
   - Does the column's target match one of `IncludedIdentityTables`?
   - Is it listed in `GlobalFieldExclusions` or per-entity `ExcludedFields`?
2. Check the response's `personIdentity` field. If `systemUserId` is the right user but `teamIds` is empty even though you expect team memberships, the user might not be in the team in Dataverse (or the team membership hasn't propagated — log out and back in).
3. Check BFF logs for an `IdentityNormalizationService` warning indicating one of the 6 identity paths failed.

### Wrong role names in response

**Symptom**: User endpoint returns memberships under a role name like `"assignedlawfirm1"` instead of `"assignedLawFirm"`.

**Cause**: `FieldRoleOverrides` not configured for this entity. The default CamelCase strategy strips trailing digits, so `sprk_assignedattorney1` → `assignedAttorney` works automatically — but the strategy preserves the digit when needed for disambiguation. If you WANT both numbered fields collapsed under one role, you must opt in via override.

**Fix**: Add to `Membership:EntityOverrides.{entityType}.FieldRoleOverrides` — see [Configuration → Per-entity overrides](#3-per-entity-overrides-the-common-configuration-knob). Restart BFF + `POST /api/admin/membership/refresh-metadata`.

### `sprk_assignedlawfirm1` / `sprk_assignedlawfirm2` not resolving

**Symptom**: Discovery shows these columns with `"identityType": "Organization"`, but the user endpoint never returns matters under the `assignedLawFirm` role even for users you KNOW are at law firms with matters.

**Cause**: `Membership:OrganizationLookup:UserLookupField` is empty (the default). The resolver returns an empty organization list and logs once at startup — no error, just no organization memberships.

**Fix**: Set `Membership:OrganizationLookup:UserLookupField` to the logical name of the Lookup column on `sprk_organization` that points to `systemuser`. See [Configuration → Organization mapping](#5-organization-mapping-required-when-using-sprk_organization-lookups). Restart BFF.

### Stale data after an assignment changes

**Symptom**: User changes `sprk_assignedattorney1` on a matter. The new assignee still sees the OLD matter list for several minutes.

**Cause (Phase 1A)**: The per-user membership cache has a 5-minute TTL. Without Phase 2 pub/sub invalidation, the cache simply expires.

**Cause (Phase 2 partial)**: If `EventPublisher` is enabled but `CacheInvalidator` is not, the junction table updates in seconds but the user-facing cache still waits for its 5-min TTL.

**Fixes (pick one)**:
- Wait up to 5 minutes (correctness backstop).
- Enable `Membership:CacheInvalidator:Enabled = true` (requires Redis) for sub-second invalidation.
- Restart the BFF to flush all caches immediately (drastic — only for testing).

### Recon job not running

**Symptom**: `GET /api/admin/jobs/membership-reconciliation/history` returns empty OR the most recent run is more than 24 hours old.

**Diagnostic order**:

1. Check job status: `GET /api/admin/jobs/membership-reconciliation/status`. If `enabled` is `false`, re-enable: `POST /api/admin/jobs/membership-reconciliation/enable`.
2. Check `Reconciliation:Enabled` in `appsettings` — defaults to `true`; only `false` if an operator explicitly disabled.
3. Check `Reconciliation:CronSchedule` — default `"0 2 * * *"` (02:00 UTC daily). Confirm the cron is what you expect; tools like crontab.guru help interpret.
4. Check BFF logs for `MembershipReconciliationJob` entries. The job logs `tick started` and `tick completed` lines around each run.
5. Trigger manually: `POST /api/admin/jobs/membership-reconciliation/trigger`. If this works, the job is healthy and the schedule is what's wrong.

### Slow user endpoint responses

**Symptom**: `GET /api/users/me/memberships/{entityType}` p95 > 500ms.

**Diagnostic order**:

1. Check BFF App Insights for the request duration distribution. Look for outliers.
2. Check if discovery cache is warm. First request after BFF restart hits Dataverse `EntityDefinitions` (60-min TTL on the cache). Repeat calls should be fast.
3. Check if per-user identity cache is warm (10-min TTL). First call per user hits Dataverse 4-6 times in parallel for identity normalization.
4. If consistently slow after caches warm, consider enabling Phase 2 — the junction-table-backed read path is faster than the OR-joined FetchXML.

### `transitive-chain-too-deep` 400 error

**Symptom**: `GET /api/users/me/memberships/sprk_matter?includeRelated=sprk_document,sprk_document_child` returns `400 BadRequest` with body containing `"reasonTag": "transitive-chain-too-deep"`.

**Cause**: Spaarke enforces a 1-hop max on `includeRelated` (binding per ADR-034). Multi-hop chains were forbidden during design to prevent N+1 query explosions.

**Fix**: Reduce `includeRelated` to one comma-separated list of direct-related entity types only. If you need deeper traversal, make multiple calls and assemble the chain client-side.

---

## Operational Cadence

| Cadence | What happens |
|---|---|
| **On every authenticated request** | User endpoint resolves identity (cached 10 min) → discovery (cached 60 min) → FetchXML against the target entity. Phase 1A: query runs every time (5-min cache on the resolved IDs). Phase 2: query against the junction table. |
| **Event-driven (Phase 2)** | When a BFF endpoint mutates an identity Lookup, an event is published to `sprk-membership-changes` topic. Subscription consumer updates the junction within seconds. Cache invalidator publishes to Redis channel; subscribers on every BFF instance evict matching entries. End-to-end: ~1-5 seconds. |
| **Nightly recon (02:00 UTC default)** | Background job re-scans source-of-truth Lookups for every entity in `Reconciliation:EntityTypes`. Self-heals any drift (max 24h staleness). LOAD-BEARING for entities mutated outside the BFF (maker portal, Power Automate, plugins). |
| **Metadata cache TTL** | 60 minutes. Schema changes propagate automatically within an hour, or immediately via `POST /api/admin/membership/refresh-metadata`. |
| **Per-user membership cache TTL** | 5 minutes Phase 1A. Auto-invalidated via Redis pub/sub on Phase 2 junction write (typically sub-second across all instances when enabled). |
| **AAD-oid → systemuserid cache TTL** | 10 minutes. A freshly disabled user continues to look authenticated for at most 10 minutes, at which point the next request re-resolves and surfaces the row's absence as 401. |

---

## Related Documentation

- **Architecture deep-dive** — full pattern, naming-collision register, contracts, code entry points: [`docs/architecture/membership-resolution-pattern.md`](../architecture/membership-resolution-pattern.md)
- **Binding ADR (concise — MUST / MUST NOT rules)**: [`.claude/adr/ADR-034-user-record-membership.md`](../../.claude/adr/ADR-034-user-record-membership.md)
- **Background-job infrastructure** (recon job runs on this): [`.claude/adr/ADR-036-background-job-infrastructure.md`](../../.claude/adr/ADR-036-background-job-infrastructure.md)
- **Null-Object kill-switch pattern** (the three Phase 2 feature flags use this): [`.claude/adr/ADR-032-bff-nullobject-kill-switch.md`](../../.claude/adr/ADR-032-bff-nullobject-kill-switch.md)
- **Spaarke Auth v2** (how caller identity is established): [`docs/guides/auth-deployment-setup.md`](auth-deployment-setup.md), [`.claude/adr/ADR-028-spaarke-auth-architecture.md`](../../.claude/adr/ADR-028-spaarke-auth-architecture.md)
- **Configuration matrix** (all BFF settings in one place): [`docs/guides/CONFIGURATION-MATRIX.md`](CONFIGURATION-MATRIX.md)
- **`LookupUserMembership` playbook node** (AI playbook authors): see playbook-architecture docs and the executor at `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/LookupUserMembershipNodeExecutor.cs` (`ActionType=52`)
- **Junction entity schema** (when published): `docs/data-model/sprk_userentityassociation.md`
- **Naming-collision warning** — do NOT confuse this with the `AssociationResolver` PCF: see the Naming-Collision Register in the architecture page

---
