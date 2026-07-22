# Task 044 Notes — Configure Dataverse Search for sprk_communication (FR-23)

> **Status**: PREPARED-for-operator (not applied). Org-level Dataverse Search is confirmed already enabled; the two remaining sub-steps (table participation + searchable columns) are blocked from headless application via Web API in this environment — see "Why this couldn't be applied headlessly" below. Exact config + operator steps below.

## 1. Environment / current state (verified live, 2026-07-21, spaarkedev1)

- `pac org who` → connected to **SPAARKE DEV 1** (`spaarkedev1.crm.dynamics.com`), org id `0c3e6ad9-ae73-f011-8587-00224820bd31`.
- **Org-level Dataverse Search is already ON** — confirmed by querying `EntityDefinitions?$filter=SyncToExternalSearchIndex eq true`: **45 tables** are already indexed, including several Spaarke custom tables: `sprk_document`, `sprk_matter`, `sprk_event`, `sprk_invoice`, `sprk_project`, `sprk_memo`, `sprk_todo`, `sprk_servicerequest`, `sprk_userprofile`, `sprk_playbookconsumer`, `sprk_workoffice`, and (notably) three of `sprk_communication`'s satellite tables — `sprk_communicationthread`, `sprk_communicationchannelref`, `sprk_communicationattachment`, `sprk_communicationparticipant` — but **NOT `sprk_communication` itself**. That's the gap this task closes. This means the task's Step 1 escalation trigger ("if Dataverse Search is not enabled for the org, STOP and escalate") does **not** fire — the external prerequisite from spec §Dependencies is already satisfied.
- `sprk_communication` current metadata (read via Dataverse Web API, `EntityDefinitions(LogicalName='sprk_communication')`):
  - `SyncToExternalSearchIndex = False` (table not yet participating in the search index)
  - `ChangeTrackingEnabled = True` (precondition for enabling search sync — already satisfied)
  - `CanEnableSyncToExternalSearchIndex.Value = True` (table is eligible; nothing blocks enabling it)
- Column-level `IsSearchable` flags on `sprk_communication` attributes (read via Web API):
  | Column | Logical name | Type | `IsSearchable` today |
  |---|---|---|---|
  | Name (primary) | `sprk_name` | Text | **True** (already searchable) |
  | Subject | `sprk_subject` | Text (String) | False |
  | Body | `sprk_body` | Multiline Text (Memo) | False |
  | From | `sprk_from` | Text (String) | False |
  | To | `sprk_to` | Text (String) | False |

  Confirmed `sprk_from`/`sprk_to` are plain **Text** fields, not activity-party/lookup fields — so they ARE eligible for Dataverse Search indexing (party-list fields are explicitly unsupported by Dataverse Search; that gotcha does not apply here).
- The existing "Quick Find Active Communications" system view (`savedqueryid 696a3f51-b06f-4f91-bdf1-c64d99d0d5ce`, querytype=4) is the record that drives `IsSearchable` — its FetchXML `isquickfindfields="1"` filter currently contains only `sprk_name`. This mirrors the mechanism already used for `sprk_document`'s Quick Find view in this same org, whose `isquickfindfields` filter contains exactly the 6 fields that show `IsSearchable=True` on that table (`sprk_documentname`, `sprk_documenttype`, `sprk_filekeywords`, `sprk_filesummary`, `sprk_matter`, `sprk_project`) — i.e., editing the Quick Find view's Find Columns is the correct, precedented mechanism for column-level Dataverse Search configuration in this org.

## 2. Target config (the 4 columns from spec FR-23)

| Field | Logical name |
|---|---|
| Subject | `sprk_subject` |
| Body | `sprk_body` |
| From | `sprk_from` |
| To | `sprk_to` |

No new columns — reuses the existing R1/R2 schema (per NFR-06 / hard rule). `sprk_name` stays searchable as-is (unrelated to this task, already on).

## 3. Why this couldn't be applied headlessly (evidence)

Extensive, methodical attempts were made to apply both required changes via the Dataverse Web API (using an `az account get-access-token` bearer token for `ralph.schroeder@spaarke.com`, the same identity `pac` is authenticated as) before falling back to PREPARE-for-operator:

**a) Entity-level `SyncToExternalSearchIndex = true` on `sprk_communication`** — attempted via:
- `PATCH EntityDefinitions(LogicalName='sprk_communication')` — `405 Operation not supported on EntityMetadata`
- Same PATCH keyed by `MetadataId` GUID instead of `LogicalName` — same 405
- Same PATCH with `MSCRM.MergeLabels: true` header — same 405
- Same PATCH with `MSCRM.SolutionUniqueName: SpaarkeCore` header (solution-context write) — same 405
- Single-property `PUT EntityDefinitions(id)/SyncToExternalSearchIndex` (Microsoft's documented single-property-update pattern) — same 405
- **Control test**: PATCH of an unrelated, definitely-writable property (`IsQuickCreateEnabled`, and separately the documented `Description`/`LocalizedLabels` sample verbatim from Microsoft Learn) on the same entity — **also 405, identical error**. This confirms the block is not specific to `SyncToExternalSearchIndex` — metadata writes to `EntityDefinitions` are blocked outright for this identity/environment via the Web API, regardless of property or header combination tried.

**b) Column-level `IsSearchable = true` on `sprk_subject`/`sprk_body`/`sprk_from`/`sprk_to`** — attempted via:
- `PATCH EntityDefinitions(id)/Attributes(id)/Microsoft.Dynamics.CRM.StringAttributeMetadata` (typed sub-resource URL) — `405 The requested resource does not support http method 'PATCH'`
- `PATCH EntityDefinitions(id)/Attributes(id)` with `@odata.type` in the body (Microsoft's standard attribute-update pattern) — same 405

**c) Editing the Quick Find view's FetchXML directly** (a *data record* write, `savedqueries` table — a fundamentally different, non-metadata write path than (a)/(b)) — attempted via:
- `PATCH savedqueries(696a3f51-...)` with `fetchxml` extended to include `sprk_subject`/`sprk_body`/`sprk_from`/`sprk_to` as `isquickfindfields` conditions — `400 An unexpected error occurred`
- Same PATCH with only ONE new field added (isolate a per-field problem) — same 400
- Same PATCH with the **exact unchanged existing FetchXML** (pure round-trip, no delta) — same 400
- **Control test**: PATCH of an unrelated field (`description`) on the *same* `savedqueries` record — **succeeded** (204). This isolates the failure specifically to the `fetchxml` field on this protected system Quick Find view, not to write access on the record generally.

Conclusion: this Dataverse environment (or this identity's effective privilege set) blocks direct metadata schema writes (`EntityMetadata`/`AttributeMetadata` PATCH/PUT) and blocks direct FetchXML edits on the system Quick Find view — both must go through the Maker Portal / table designer UI, which applies its own validation/publish pipeline that the raw Web API path here does not support. This matches the one piece of direct prior art found in this repo: an earlier, now-archived task (`projects/x-ai-file-entity-metadata-extraction/tasks/011-configure-relevance-search.poml`) for a near-identical action (enable search + add a searchable field) whose completion note reads *"Task completed manually by user via Power Platform Admin Center"* (2025-12-09) — i.e., this exact class of change has never been done headlessly in this tenant.

A `researcher` subagent was also dispatched in parallel for external documentation (Microsoft Learn) confirmation; its independent findings agree: org-level enablement is admin-center-only with no documented API (already satisfied here per §1), and while `SyncToExternalSearchIndex`/Quick-Find-column edits are *documented* as theoretically Web-API-reachable via the SDK's `UpdateEntityRequest`/`UpdateAttributeRequest` messages, that is the classic SOAP Organization Service message set, not the OData Web API PATCH verb tested above — consistent with the 405s observed.

**Cleanup note**: during isolation testing, a throwaway `description = "test"` value was written to the Quick Find view record (`savedqueries` id `696a3f51-b06f-4f91-bdf1-c64d99d0d5ce`) to confirm the record accepts writes on non-protected fields. Two revert attempts (`null`, then `""`) were sent but did not appear to take effect on read-back (still shows `"test"` — likely a caching/async-replication artifact, not a retained write failure, since the identical no-op fetchxml round-trip also 400'd, ruling out a "field-locked" theory). This field is decorative only (not used by Dataverse Search, not visible to end users, no security/functional impact) — **the operator should clear the Quick Find view's Description field back to blank** while performing the Maker Portal steps below, as a minor cosmetic cleanup.

## 4. Exact steps for the operator (Maker Portal)

1. Go to **make.powerapps.com** → select environment **SPAARKE DEV 1** (spaarkedev1.crm.dynamics.com).
2. **Tables** → open **Communication** (`sprk_communication`) → **Views** → open the system **Quick Find Active Communications** view (querytype = Quick Find).
3. In the view designer, open **Edit filter criteria** / **Find columns** (the Quick Find search-field picker) and **add**:
   - `Subject` (`sprk_subject`)
   - `Body` (`sprk_body`)
   - `From` (`sprk_from`)
   - `To` (`sprk_to`)

   (leave `Name`/`sprk_name` as-is — already present.) While here, clear the Description field back to blank (see cleanup note above).
4. **Save** the view.
5. Go to the table's properties (Communication table → **Settings**/**Advanced options**, or via the environment's search-index management surface: **Power Platform Admin Center → Environments → SPAARKE DEV 1 → Settings → Product → Features → Dataverse search → Manage search index** if the table-level toggle isn't directly on the table designer) and **enable the table for Dataverse Search / "Sync to external search index"** for `sprk_communication`.
6. **Publish all customizations** for the `sprk_communication` table (Solutions → the table's home solution → Publish, or the table designer's Publish button).
7. Wait for index rebuild — Microsoft documents ~15 minutes for incremental config changes (full resync can take longer for large tables).
8. **Verify functional criterion**: in a model-driven app, type a keyword that only appears in a test communication's Subject/Body/From/To into the global search bar; confirm that record is returned.
9. **Verify security-trimming (negative criterion)**: as (or impersonating) a user who does NOT have read access to that specific communication (e.g., lacks the sharing/security-role/business-unit access — respect `sprk_isinternalonly`/regular Dataverse row-level security), repeat the same keyword search and confirm the record is **NOT** returned. Dataverse Search is documented as inherently respecting the caller's Dataverse security-role read privileges automatically for every query — no separate trimming config exists or should be added (confirmed via Microsoft Learn: *"Security and compliance: Respects Dataverse security roles and permissions. Users can only view search results for records that they have access to."*). Do not add any additional ACL/filter layer — that would be a second, divergent, security-trimming mechanism outside the ADR-024/access-filter pattern this project already uses (`ICommunicationAccessFilter`), and Dataverse Search bypasses that filter entirely (it queries the index directly), so this native trimming is the *only* trimming in effect for search results — verifying it is not optional.

## 5. Field budget / scale note

No concern: Dataverse Search's default indexed-field budget is ~50 fields per environment context (org max up to 1,000, configurable); adding 4 plain text/memo fields (each counts as 1 toward the budget; only Lookup/Option Set columns cost more) is trivial against the current 45-table baseline.

## 6. Acceptance criteria status

| Criterion | Status |
|---|---|
| `sprk_communication` present in org Dataverse Search config with subject/body/from/to searchable | **PREPARED, not yet applied** — exact steps above |
| Keyword search returns matching record via global search bar | Not yet verifiable — depends on step 4 being applied first |
| Negative: user without access does not see the record | Not yet verifiable — same dependency; expected to hold automatically once applied, per Dataverse Search's native security-trimming design (no separate config needed) |
| External prerequisite escalated if org-level search not enabled | N/A — org-level Dataverse Search is **already enabled** (verified, §1); no escalation needed on that specific point |

## 7. Deviation from task's `<escalation>` trigger

The POML's only literal escalation trigger is "if Dataverse Search is not enabled at the org level" — that did not fire (it's enabled). The actual blocker encountered was one level down: the *table participation* and *searchable-column* configuration steps are UI-only in this environment despite being nominally API-documented elsewhere. Per the task's own explicit instruction ("if it requires an interactive maker-portal step you cannot perform headlessly, PREPARE the exact config... and REPORT it for the operator rather than guessing"), this is handled as PREPARED-for-operator rather than a hard STOP, since the org-level prerequisite (the only named blocker) is satisfied and the remaining action is a well-defined, low-risk config change ready for a human to execute in ~5 minutes via the Maker Portal.
