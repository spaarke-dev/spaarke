# Phase 3 Task 022 — Configure Power Pages Web API Site Settings

> **Task**: 022
> **Phase**: 3 — Power Pages Configuration
> **Status**: Documented (manual operator steps required)
> **Estimated effort**: 3 hours
> **Prerequisite**: Task 021 complete (web roles and table permissions configured)
> **Parallel with**: Task 023 (CSP/CORS settings — independent, can run concurrently)

---

## Overview

This guide covers enabling the Power Pages Web API (`/_api/`) for each Dataverse table that the external SPA needs to read or write. It also covers configuring OAuth implicit grant flow so the SPA can obtain tokens for BFF API calls.

The Power Pages Web API is an OData 4.0 endpoint that the SPA uses to query Dataverse data directly from the browser. Each table and each field must be explicitly enabled through Site Settings — this is the field-level whitelist for the portal's data access layer.

**How it works**:

```
SPA (browser)
    │
    │  GET /_api/sprk_projects?$select=...
    │  Cookie: .ASPXAUTH (portal session)
    ▼
Power Pages Web API endpoint
    │
    │  Evaluates: table enabled? field in whitelist? table permission allows access?
    ▼
Dataverse (OData)
    │
    ▼
Returns filtered rows (only rows the user's table permission chain allows)
```

---

## How to Create Site Settings

All configuration in this guide is created as records in the **Site Settings** table in the Portal Management model-driven app.

**Navigation**:
1. Open Portal Management: `https://spaarkedev1.crm.dynamics.com/main.aspx?appname=MicrosoftPortalApp`
2. Go to **Website** → **Site Settings**.
3. For each setting below: click **New**, enter the Name and Value, select your portal in the **Website** field, click **Save**.

> Site setting names are **case-sensitive** and must match exactly as shown below. Value fields are plain text strings.

---

## Step 1 — Enable Web API for sprk_project

Create these two site settings:

### Setting 1: Enable the table

| Field | Value |
|-------|-------|
| **Name** | `Webapi/sprk_project/enabled` |
| **Value** | `true` |
| **Website** | [your portal] |

### Setting 2: Specify allowed fields

| Field | Value |
|-------|-------|
| **Name** | `Webapi/sprk_project/fields` |
| **Value** | `sprk_projectid,sprk_name,sprk_issecure,sprk_status,sprk_externalaccountid` |
| **Website** | [your portal] |

> **Field list notes**:
> - `sprk_projectid` — primary key, required for relationship navigation and record identification
> - `sprk_name` — display name
> - `sprk_issecure` — flag indicating whether this is a secure project (controls SPA feature visibility)
> - `sprk_status` — project lifecycle status (Active, Closed, etc.)
> - `sprk_externalaccountid` — external account identifier used by BFF API
> - Do not include sensitive internal fields (billing amounts, internal notes, etc.)

---

## Step 2 — Enable Web API for sprk_document

### Setting 1: Enable the table

| Field | Value |
|-------|-------|
| **Name** | `Webapi/sprk_document/enabled` |
| **Value** | `true` |
| **Website** | [your portal] |

### Setting 2: Specify allowed fields

| Field | Value |
|-------|-------|
| **Name** | `Webapi/sprk_document/fields` |
| **Value** | `sprk_documentid,sprk_name,sprk_filename,sprk_projectid,sprk_matterid,createdon` |
| **Website** | [your portal] |

> **Field list notes**:
> - `sprk_documentid` — primary key
> - `sprk_name` — document display name
> - `sprk_filename` — filename (used to derive file type icon, extension)
> - `sprk_projectid` — parent project lookup (for filtering and grouping)
> - `sprk_matterid` — matter association if applicable
> - `createdon` — system timestamp (for display and sorting)
> - Document content (file bytes) is never served through the Web API — downloads go through the BFF API (`/api/documents/{id}/download`) which performs authorization via Task 010 filter.

---

## Step 3 — Enable Web API for sprk_event

### Setting 1: Enable the table

| Field | Value |
|-------|-------|
| **Name** | `Webapi/sprk_event/enabled` |
| **Value** | `true` |
| **Website** | [your portal] |

### Setting 2: Specify allowed fields

| Field | Value |
|-------|-------|
| **Name** | `Webapi/sprk_event/fields` |
| **Value** | `sprk_eventid,sprk_name,sprk_startdate,sprk_enddate,sprk_projectid` |
| **Website** | [your portal] |

> **Field list notes**:
> - `sprk_eventid` — primary key
> - `sprk_name` — event title
> - `sprk_startdate`, `sprk_enddate` — event timing for calendar display
> - `sprk_projectid` — parent project lookup
> - The Web API field list controls readable AND writable fields. For sprk_event, the table permission (Task 021) grants Create/Write, and the SPA will POST/PATCH these same fields for event creation/editing.

---

## Step 4 — Enable Web API for contact

### Setting 1: Enable the table

| Field | Value |
|-------|-------|
| **Name** | `Webapi/contact/enabled` |
| **Value** | `true` |
| **Website** | [your portal] |

### Setting 2: Specify allowed fields

| Field | Value |
|-------|-------|
| **Name** | `Webapi/contact/fields` |
| **Value** | `contactid,fullname,emailaddress1` |
| **Website** | [your portal] |

> **Field list notes**:
> - `contactid` — primary key
> - `fullname` — computed full name for display
> - `emailaddress1` — primary email
> - Do NOT include sensitive contact fields (phone numbers, addresses, internal notes). The minimal field set reduces PII exposure.
> - The `Self` table permission (Task 021 Step 6) also allows the user to write their own Contact — the field list here limits which fields of their own Contact they can update via Web API.

---

## Step 5 — Enable Web API for sprk_organization

### Setting 1: Enable the table

| Field | Value |
|-------|-------|
| **Name** | `Webapi/sprk_organization/enabled` |
| **Value** | `true` |
| **Website** | [your portal] |

### Setting 2: Specify allowed fields

| Field | Value |
|-------|-------|
| **Name** | `Webapi/sprk_organization/fields` |
| **Value** | `sprk_organizationid,sprk_name` |
| **Website** | [your portal] |

---

## Step 6 — Configure Registration Settings

### Setting: Allow registration

| Field | Value |
|-------|-------|
| **Name** | `Authentication/Registration/Enabled` |
| **Value** | `true` |
| **Website** | [your portal] |

### Setting: Disable open registration (invite-only model)

| Field | Value |
|-------|-------|
| **Name** | `Authentication/Registration/OpenRegistrationEnabled` |
| **Value** | `false` |
| **Website** | [your portal] |

> **Invite-only rationale**: Setting `OpenRegistrationEnabled = false` means users cannot self-register directly on the portal. They must receive an invitation (via Task 013 invitation endpoint) that triggers the Entra External ID invitation flow. Only users who complete the invitation flow get a Contact + `adx_externalidentity` created. This prevents anonymous sign-ups from creating empty Contact records.
>
> **If open registration is needed for testing**: Temporarily set to `true` during testing, then revert to `false` for production.

---

## Step 7 — Configure OAuth Implicit Grant (for SPA → BFF API token acquisition)

The SPA needs OAuth tokens to call the BFF API. Power Pages can issue tokens via an OAuth implicit grant flow scoped to the portal session.

### Setting: Enable OAuth implicit grant

| Field | Value |
|-------|-------|
| **Name** | `OAuth/ImplicitGrantEnabled` |
| **Value** | `true` |
| **Website** | [your portal] |

### Setting: Token expiry (seconds)

| Field | Value |
|-------|-------|
| **Name** | `OAuth/ExpiresIn` |
| **Value** | `3600` |
| **Website** | [your portal] |

> **Token usage**: The SPA calls `GET /_oauth/implicitgrant?response_type=token&client_id={portal-client-id}` to get a bearer token. This token is passed as `Authorization: Bearer {token}` to the BFF API. The BFF API validates the token against the Power Pages tenant (Task 010 external caller authorization filter).

### Setting: Allowed client IDs

| Field | Value |
|-------|-------|
| **Name** | `OAuth/AllowedClientIds` |
| **Value** | `*` (or a specific client ID if restricting to a known SPA app registration) |
| **Website** | [your portal] |

> Using `*` allows any client. For production, replace with the specific application ID of the SPA app registration in Entra.

---

## Step 8 — Complete Site Settings Reference

For ease of entry, the full list of site settings to create:

| Name | Value | Notes |
|------|-------|-------|
| `Webapi/sprk_project/enabled` | `true` | |
| `Webapi/sprk_project/fields` | `sprk_projectid,sprk_name,sprk_issecure,sprk_status,sprk_externalaccountid` | |
| `Webapi/sprk_document/enabled` | `true` | |
| `Webapi/sprk_document/fields` | `sprk_documentid,sprk_name,sprk_filename,sprk_projectid,sprk_matterid,createdon` | |
| `Webapi/sprk_event/enabled` | `true` | |
| `Webapi/sprk_event/fields` | `sprk_eventid,sprk_name,sprk_startdate,sprk_enddate,sprk_projectid` | |
| `Webapi/contact/enabled` | `true` | |
| `Webapi/contact/fields` | `contactid,fullname,emailaddress1` | |
| `Webapi/sprk_organization/enabled` | `true` | |
| `Webapi/sprk_organization/fields` | `sprk_organizationid,sprk_name` | |
| `Authentication/Registration/Enabled` | `true` | |
| `Authentication/Registration/OpenRegistrationEnabled` | `false` | Invite-only; set to `true` for initial testing |
| `OAuth/ImplicitGrantEnabled` | `true` | Required for SPA token acquisition |
| `OAuth/ExpiresIn` | `3600` | Token TTL in seconds |
| `OAuth/AllowedClientIds` | `*` | Restrict to SPA client ID in production |

---

## Step 9 — Clear Portal Cache

After creating all site settings, clear the portal cache:

1. Power Pages admin center → **Portal Actions** → **Clear cache**.
2. Or navigate to `https://{portal-domain}/_services/about` → **Clear Cache**.

---

## Step 10 — Test Web API Access

### Test 1: Web API endpoint accessible

From a browser where you are signed into the portal:

```
GET https://{portal-domain}/_api/sprk_projects?$select=sprk_name
```

Expected: OData JSON response with the user's accessible projects (scoped by table permissions from Task 021).

### Test 2: Field restriction enforced

Request a field NOT in the allowed field list:

```
GET https://{portal-domain}/_api/sprk_projects?$select=sprk_name,sprk_internalfield
```

Expected: The response either omits `sprk_internalfield` or returns an error indicating the field is not accessible.

### Test 3: Anonymous access blocked

Open an InPrivate window (not signed in) and call:

```
GET https://{portal-domain}/_api/sprk_projects
```

Expected: 401 Unauthorized (not 200 with data).

### Test 4: Create event via Web API

As an authenticated user with a project in scope:

```http
POST https://{portal-domain}/_api/sprk_events
Content-Type: application/json
RequestVerificationToken: {antiforgery-token-from-page}

{
  "sprk_name": "Test Event",
  "sprk_startdate": "2026-04-01T09:00:00Z",
  "sprk_enddate": "2026-04-01T17:00:00Z",
  "sprk_projectid@odata.bind": "/sprk_projects({project-id})"
}
```

Expected: 201 Created with the new event record.

> The antiforgery token is obtained from the portal page source (`__RequestVerificationToken` hidden input or from `/_api/antiforgery/token`).

### Test 5: OAuth token acquisition

```
GET https://{portal-domain}/_oauth/implicitgrant?response_type=token&client_id=*
```

Expected: JSON response containing `access_token`, `token_type: Bearer`, `expires_in`.

---

## Acceptance Criteria Checklist

- [ ] All 10 Webapi site settings created (5 tables × 2 settings each)
- [ ] Registration settings created (`Enabled = true`, `OpenRegistrationEnabled = false`)
- [ ] OAuth implicit grant settings created
- [ ] Portal cache cleared
- [ ] Authenticated user can query `/_api/sprk_projects` and sees only their projects
- [ ] Non-whitelisted fields are not returned
- [ ] Anonymous access returns 401
- [ ] Event creation via POST succeeds for Collaborate-level users
- [ ] OAuth token endpoint returns a bearer token

---

## Troubleshooting

| Symptom | Likely Cause | Resolution |
|---------|-------------|------------|
| 404 on `/_api/sprk_projects` | Web API not enabled for the table | Check `Webapi/sprk_project/enabled` site setting exists and value is `true` |
| Field not returned | Field not in the field list | Add field to `Webapi/sprk_project/fields` (comma-separated, no spaces) |
| 401 despite being signed in | Session cookie not sent (cross-origin), or portal cache | Clear portal cache; ensure request is same-origin or has `withCredentials: true` |
| 403 on event creation | Table permission does not grant Create for sprk_event | Revisit Task 021 Step 5 — confirm Create = Yes on the event table permission |
| Empty array on all queries | Table permissions not yet clearing cache, or no ERA records | Ensure Task 021 is complete and cache is cleared; verify ERA records exist in Dataverse |
| OAuth token request fails | `OAuth/ImplicitGrantEnabled` not set | Create the site setting with value `true` and clear cache |
