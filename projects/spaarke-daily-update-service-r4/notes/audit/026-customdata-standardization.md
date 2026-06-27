# Task 026 — customData FR-6 Standardization Across 7 Notification Playbooks

**Date**: 2026-06-25
**Task**: 026 (Phase 3 PR 3 W1 — customData consistency)
**Rigor**: STANDARD
**Status**: ✅ Completed

## Summary

All 7 notification playbooks (PB-016 through PB-022) updated to emit the FR-6 enriched
customData schema. Each playbook's `CreateNotification.itemNotification` block now passes
the 8 new config params introduced by task 020's `BuildNotificationEntity` enrichment:
`regardingName`, `sourceEntityType`, `sourceId`, `sourceModifiedOn`, `sourceOwningUser`,
`viaMatterId`, `viaMatterName`, `viaMatterMembershipsVariable`.

Existing fields (`category`, `priority`, `actionUrl`, `dueDate`, `regardingId`, `regardingType`)
were already present and remain unchanged. The combination produces the full FR-6 customData
shape at runtime via the executor (R4 spec FR-6 line 134; AC-6a; AC-10).

## FR-6 Field Coverage Matrix

Columns = the 10 customData fields the FR-6 enriched schema can carry. Values:
- ✅ = config param now wired in playbook itemNotification (post task-026)
- ⚪ = already present pre-task (R3/R4 baseline)
- ⚠️ = data-flow limitation (documented below)

| Playbook | category | priority | actionUrl | dueDate | regardingName | regardingEntityType | regardingId | viaMatter (id/name/memberships) | source.entityType | source.id | source.modifiedOn | source.owningUser |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| PB-016 (new-emails) | ⚪ | ⚪ | ⚪ | ⚪ (n/a) | ✅ | ⚪ | ⚪ | ✅✅✅ | ✅ | ✅ | ✅ (createdon) | ✅ |
| PB-017 (matter-activity) | ⚪ | ⚪ | ⚪ (item) | ⚪ (n/a) | ✅ | ⚪ | ⚪ | ✅✅✅ | ✅ | ✅ | ✅ | ✅ |
| PB-018 (new-documents) | ⚪ | ⚪ | ⚪ (item) | ⚪ (n/a) | ✅ | ⚪ | ⚪ | ✅✅✅ | ✅ | ✅ | ✅ (createdon) | ✅ |
| PB-019 (new-events) | ⚪ | ⚪ | ⚪ | ⚪ (n/a) | ✅ | ⚪ | ⚪ | ✅✅✅ | ✅ | ✅ | ✅ (createdon) | ✅ |
| PB-020 (tasks-due-soon) | ⚪ | ⚪ | ⚪ | ⚪ | ✅ | ⚪ | ⚪ | ✅✅✅ | ✅ | ✅ | ✅ (modifiedon added) | ✅ |
| PB-021 (tasks-overdue) | ⚪ | ⚪ | ⚪ | ⚪ | ✅ | ⚪ | ⚪ | ✅✅✅ | ✅ | ✅ | ✅ (modifiedon added) | ✅ |
| PB-022 (work-assignments) | ⚪ | ⚪ | ⚪ | ⚪ | ✅ | ⚪ | ⚪ | ✅✅✅ | ✅ | ✅ | ✅ (modifiedon) | ✅ |

**Coverage**: 7 / 7 playbooks emit the full FR-6 schema. 0 playbooks have partial or pending coverage.

### viaMatter.memberships[] runtime resolution

All 7 playbooks now set `viaMatterMembershipsVariable: "myMatters"` — the executor's
`ResolveViaMatterMemberships` projects `byRole` from the upstream `Lookup My Matters`
node (ActionType 52) for the resolved matter ID. When the source record's matter is in
a role bucket (owner / assignedAttorney / assignedParalegal), `viaMatter.memberships[]`
emits one entry per matching role. When no matter linkage, the executor omits the
entire `viaMatter` field (per FR-6 omission rule, AC-6b).

### Data-flow notes / minor adjustments

- **PB-020 & PB-021** — FetchXml previously did NOT fetch `modifiedon`. Added
  `<attribute name="modifiedon"/>` to both queries so `sourceModifiedOn` can be populated
  via `{{item.modifiedon}}`.
- **PB-022** — Deployed FetchXml (post task 025) included an additional `modifiedon` attribute
  + a `createdon OR modifiedon` time filter that the repo file lacked. Repo JSON updated
  to match deployed state, plus the FR-6 enrichment fields.
- **PB-016 regardingName** — uses `{{item.sprk_subject}}` (email subject) as a human-readable
  regarding label. Other entities use the entity's primary name field.

## Files Edited (Repo JSON — Source of Truth)

All 7 repo JSON files at `projects/spaarke-daily-update-service/notes/playbooks/`:

1. `notification-new-emails.json` (PB-016) — added 8 FR-6 config params to `itemNotification`
2. `notification-matter-activity.json` (PB-017) — added 8 FR-6 config params
3. `notification-new-documents.json` (PB-018) — added 8 FR-6 config params
4. `notification-new-events.json` (PB-019) — added 8 FR-6 config params
5. `notification-tasks-due-soon.json` (PB-020) — added `modifiedon` to FetchXml + 8 FR-6 config params
6. `notification-tasks-overdue.json` (PB-021) — added `modifiedon` to FetchXml + 8 FR-6 config params
7. `notification-work-assignments.json` (PB-022) — synced FetchXml with deployed (modifiedon attribute + filter) + 8 FR-6 config params

## Playbooks Redeployed (via MCP `update_record` on `sprk_analysisplaybook`)

All 7 playbooks deployed successfully:

| Code | Record ID | Result |
|---|---|---|
| PB-016 | `2f46208e-5f2d-f111-88b5-7c1e520aa4df` | ✅ updated |
| PB-017 | `24051c80-5f2d-f111-88b5-7ced8d1dc988` | ✅ updated |
| PB-018 | `29051c80-5f2d-f111-88b5-7ced8d1dc988` | ✅ updated |
| PB-019 | `a4bc529c-5f2d-f111-88b5-7ced8d1dc988` | ✅ updated |
| PB-020 | `77f77aa5-5f2d-f111-88b5-7ced8d1dc988` | ✅ updated |
| PB-021 | `4369cab2-5f2d-f111-88b5-7ced8d1dc988` | ✅ updated |
| PB-022 | `be7874be-5f2d-f111-88b5-7ced8d1dc988` | ✅ updated |

Post-deploy verification: `sprk_configjson` for PB-021 re-queried and confirmed to contain
`regardingName`, `sourceEntityType`, `sourceId`, `sourceModifiedOn`, `sourceOwningUser`,
`viaMatterId`, `viaMatterName`, `viaMatterMembershipsVariable` in `Create Notification.configJson.itemNotification`.

## AC-10 Status

**🟢 GREEN** — All 7 playbooks emit the full FR-6 enriched customData schema. The downstream
schema-validation fixture (task 028) can rely on uniform output shape across all categories.

## References

- Spec FR-6 (line 134) — enriched customData schema definition
- Spec FR-10 (line 151), AC-10 (line 152) — customData consistency requirement
- `CreateNotificationNodeExecutor.cs` — `BuildNotificationEntity` post-task-020 with 8 new config params
- Task 020 — executor enrichment (already merged)
- Tasks 022, 023, 024, 025 — playbook entity corrections + LookupUserMembership wiring (predecessors)
- Task 028 — downstream schema-conformance fixture test (will consume this uniform shape)
