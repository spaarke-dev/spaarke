# Phase 1 Deployment Guide — Dataverse Schema

> **Created**: 2026-03-16
> **Status**: Pending Manual Steps (Task 004)
> **Environment**: https://spaarkedev1.crm.dynamics.com

## Overview

Phase 1 schema artifacts have been documented. The actual Dataverse entity creation must be done via the Power Apps maker portal. This guide provides step-by-step instructions.

## Pre-requisites

- [ ] PAC CLI authenticated to spaarkedev1 (✅ already done — index [2] is active)
- [ ] Access to https://make.powerapps.com with SpaarkeCore solution

## Step 1: Create sprk_externalrecordaccess Table

In Power Apps maker portal → Tables → New table:

**Reference**: `src/solutions/SpaarkeCore/entities/sprk_externalrecordaccess/entity-schema.md`

1. Table name: `External Record Access`, logical name: `sprk_externalrecordaccess`
2. Add all fields per entity-schema.md (Primary Fields, Core Lookup Fields, Access Control Fields)
3. Choice field `sprk_accesslevel`: View Only (100000000), Collaborate (100000001), Full Access (100000002)
4. Configure statecode behavior (Active/Inactive)
5. Add to SpaarkeCore solution

## Step 2: Add Fields to sprk_project

In Power Apps maker portal → Tables → sprk_project → Columns:

**Reference**: `src/solutions/SpaarkeCore/entities/sprk_project/secure-project-fields-schema.md`

1. Add `sprk_issecure` (Boolean, default: No)
2. Add `sprk_securitybuid` (Lookup → BusinessUnit)
3. Add `sprk_externalaccountid` (Lookup → Account)
4. Business Rule: Lock `sprk_issecure` after creation
5. Add all three fields to sprk_project main form (Secure Project Configuration section)

## Step 3: Configure Views and Subgrid

**Reference**: `src/solutions/SpaarkeCore/entities/sprk_externalrecordaccess/views-schema.md`

1. Create "Active Participants" view (default)
2. Create "By Project" view
3. Create "By Contact" view
4. Create "Expiring Access" view
5. Add "External Participants" subgrid to sprk_project main form
6. Create Quick Create form for sprk_externalrecordaccess

## Step 4: Export Solution

In Power Apps maker portal → Solutions → SpaarkeCore → Export:
- Export as **Managed**
- Save ZIP to `src/solutions/SpaarkeCore/bin/Release/SpaarkeCore_managed.zip`

## Step 5: Import to Dev Environment (PAC CLI)

```powershell
# From repo root
pac solution import `
    --path src/solutions/SpaarkeCore/bin/Release/SpaarkeCore_managed.zip `
    --publish-changes `
    --force-overwrite `
    --environment https://spaarkedev1.crm.dynamics.com
```

## Step 6: Verify

```powershell
# Verify tables exist
pac data export `
    --entity sprk_externalrecordaccess `
    --select sprk_externalrecordaccessid,sprk_name,sprk_contactid,sprk_projectid,sprk_accesslevel `
    --output-directory ./exports

pac data export `
    --entity sprk_project `
    --select sprk_projectid,sprk_name,sprk_issecure,sprk_securitybuid,sprk_externalaccountid `
    --output-directory ./exports
```

## Estimated Time

~2 hours for maker portal configuration + export/import.

## Notes

- Phase 2 BFF API code can proceed in parallel with this deployment (code can reference tables before they exist in dev)
- Phase 2 can be fully tested once Phase 1 deployment is complete
