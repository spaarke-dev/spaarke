---
name: dataverse-search-config-2026-07-21
description: How to configure Dataverse Search (global search) for a custom table + columns in 2026 — org enable is admin-center-only, but per-table + per-column IS programmatic via Web API
metadata:
  type: reference
---

# Dataverse Search configuration (2026-07)

Investigated 2026-07-21 for `sprk_communication` table (messaging-communication-app-r3). Corrects the stale 2025-12 assumption (archived task `x-ai-file-entity-metadata-extraction/011`) that Dataverse Search config is entirely UI-only.

**Org enable (env level)** — UI ONLY, no documented API/PAC command. PPAC → Manage → Environments → pick env → Settings → Product → Features → Dataverse search. Two independent toggles (2026): "Turn on search indexing to support Dataverse intelligence (Work IQ)…" and "Show global search bar in all model-driven apps…". Save. Opt-out feature. Turning off deprovisions index within 12h.

**Per-TABLE participation** — IS programmatic. Set `EntityMetadata.SyncToExternalSearchIndex = true` (requires `CanEnableSyncToExternalSearchIndex.Value == true` — already true for all custom tables — and `ChangeTrackingEnabled == true`). Web API: `PATCH [org]/api/data/v9.2/EntityDefinitions(LogicalName='sprk_communication')` with `MSCRM.MergeLabels: true`, body `{"SyncToExternalSearchIndex": true}`. Also settable via SDK UpdateEntityRequest / solution import. Table must ALSO be added to the model-driven app or results won't show. UI alt = new solution explorer → Overview → Dataverse search pane → Manage search index.

**Per-COLUMN searchable fields** — driven by the table's **Quick Find view** (a `savedquery` record, `querytype=4`). The view's **Find Columns** = searchable fields; **View Columns** = displayed; **Filter Columns** = filters. Only String / Memo (single+multi text), Lookup, Option Set Find-columns are indexed; all other types ignored. Quick Find view MUST be the table's DEFAULT view. Because savedquery is a normal Web API entity, find-columns are technically PATCH-able (fetchxml/layoutxml) headlessly, though the maker UI (Power Apps → Tables → Views → Quick Find View → edit Find columns → Save and Publish) is the documented path.

**Security trimming** — automatic, no ACL config. Doc quote: "Security and compliance: Respects Dataverse security roles and permissions. Users can only view search results for records that they have access to." Cannot be weakened or added to.

**Limits**: 50 fields indexed by default, org max 1,000 searchable → 950 configurable. Lookup=3 fields, OptionSet=2, others=1. Custom tables NOT indexed by default (must add). Changes take ~15 min to appear.

**GOTCHA for sprk_communication**: party-list fields (To/From/CC) are NOT supported by Dataverse search and are excluded from results. If `sprk_from`/`sprk_to` are plain String/Memo columns they index fine; if they are activity-party lookups they will NOT. Also polymorphic lookups (RegardingObjectId) and Owner column search are unsupported.

## Sources
- https://learn.microsoft.com/en-us/power-platform/admin/configure-relevance-search-organization (primary, updated 2026-07-13)
- https://learn.microsoft.com/en-us/power-apps/user/relevance-faq (party-list/unsupported types, updated 2026-01-13)
- https://learn.microsoft.com/en-us/power-apps/developer/data-platform/search/overview (SyncToExternalSearchIndex API)
- https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/reference/entitymetadata (property ref)

## Open questions
- No documented programmatic path for the ORG-level Features toggle — worth re-checking each release wave; Microsoft has been evolving this (split into two toggles + Work IQ in 2026).
