# Task 012 - matter-summary Insights topic: dev state, finding, owner decision

## 1. Read-only reads on dev (spaarkedev1, 2026-11-25, GET only)

| Item | Result |
|---|---|
| Binding `sprk_playbookconsumer` 05618e5d-ab79-f111-ab0e-7ced8ddc4cc6 | `sprk_consumertype` chat-summarize, `sprk_consumercode` **matter-summary**, `sprk_disposition` 100000001 (= **Work Product**), enabled, `sprk_ucid` UC-A-1, `sprk_playbook` **null** (a chat capability, SUM-CHAT@v1 action, not a playbook) |
| Registry row `sprk_aitopicregistry` cfca6a65-ab79-f111-ab0e-7ced8ddc4cc6 (`matter-summary/single`) | **already exists**: `sprk_topicname` matter-summary, `sprk_hostentity` sprk_matter, `sprk_targetfield` **sprk_recordsummary**, `sprk_cachettlminutes` 60, enabled, active, `sprk_playbookname` **chat-summarize** (created 2026-07-07, modified 2026-08-25) |
| matter-health row (precedent) c46b940e-... | target sprk_performancesummary, TTL 60, `sprk_playbookname` matter-health-single |
| `sprk_matter.sprk_recordsummary` | **already exists**: Memo, MaxLength 10000, IsSecured false, RequiredLevel None (same shape as `sprk_performancesummary`: Memo 10000, not secured) |
| `sprk_matter.sprk_mattersummary` | does not exist (the name the docs and a contract test used) |
| Matters holding a `sprk_recordsummary` value | none (no persisted summary yet) |

## 2. Finding: the task premise is stale

plan.md and the POML said the Binding had no registry row and no target column. On dev both exist and the Binding is `work_product`, so the data-driven persister (`WorkProductRecordPersister.cs`) already maps matter-summary to `sprk_matter.sprk_recordsummary`. **No new column and no new row is needed; no schema script was written** (a `sprk_mattersummary` column would duplicate `sprk_recordsummary`, which the MatterHeader/RecordHeader controls already read). No file under `src/server/**` changed; the persister needs no change.

## 3. What was changed (docs and test fidelity only)

`docs/data-model/sprk-playbookconsumer.md` (2 places) and `tests/integration/contract/Api/Ai/WorkProductEnvelopePersistenceContractTests.cs` said `sprk_mattersummary`; the real column is `sprk_recordsummary`. Corrected; the 7 contract tests pass (`dotnet test tests/unit/Sprk.Bff.Api.Tests --filter WorkProductEnvelopePersistenceContractTests`).

## 4. Open item that needs the owner (TTL key mismatch)

`TopicRegistryTtlLookup` builds its map on `sprk_playbookname` (here "chat-summarize"), and `TryGetTtlForPlaybookIdAsync` reverse-maps a playbook Guid to the Binding's `ConsumerCode` ("matter-summary"). So:

- `TryGetTtlForPlaybookNameAsync("matter-summary")` misses (only "chat-summarize" matches) and the cache would use DefaultTtl, not the seeded 60 min.
- The Binding has `sprk_playbook` null, so there is no playbook Guid to key an Insights cache entry on. Task 032 therefore cannot rely on `InsightsPlaybookExecutionCache` keyed by playbook id for this topic. It should read the persisted `sprk_recordsummary` envelope (`generatedAt` for the date, registry TTL for expiry) or start a run.

Script (written, NOT run anywhere): `scripts/dataverse/Set-MatterSummaryTopicRegistryKey.ps1` sets `sprk_playbookname` of the matter-summary row to `matter-summary` so a by-name TTL lookup finds the 60 min. Idempotent, `-WhatIf`.

Owner approves (or declines): `pwsh scripts/dataverse/Set-MatterSummaryTopicRegistryKey.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com` - one PATCH of one field on row cfca6a65-... (`sprk_playbookname` chat-summarize to matter-summary). Check first that nothing else keys on "chat-summarize" in that field (the persister keys on `sprk_topicname`; the only code reader of `sprk_playbookname` is `TopicRegistryTtlLookup`).

## 5. Owner go and run evidence

Owner go: **not given**. No script ran against any environment (negative criterion holds). Acceptance criterion 3 (a live run fills the column; the lookup returns the TTL) is not exercised: it needs a live matter-hosted chat run as a test user and the section 4 decision.

## 6. Follow-ups

- Solution export: confirm `sprk_recordsummary` and the registry row are in the exported Dataverse solution so other environments get them (dataverse-deploy; not done here).

rework: none task 012
