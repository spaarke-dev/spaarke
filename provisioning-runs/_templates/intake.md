# Intake — {customerId}-{runId}

> **Created by**: `customer-provisioning-orchestration-r1` task 203a per punch list row A06 (template).
> Analog of `spec.md`. Populated by `/provision-environment` skill Step 1.

## Required inputs

| Field | Value | Source | Notes |
|---|---|---|---|
| `customerId` | `{customerId}` | operator | slug; matches `sprk_dataverseenvironment.sprk_customerid` |
| `tenantId` | `{tenantId}` | operator | **explicit per NFR-11 (I1)** — never inferred |
| `environmentId` | `{environmentId}` | operator | `sprk_dataverseenvironment` GUID placeholder created in skill Step 1 pre-POST |
| `tenancyModel` | `Model1 \| Model2` | operator | drives H4-per-tenant vs H4-shared handler branching |
| `profile` | `spaarke-hosted-model1-trial \| spaarke-hosted-model2 \| customer-owned-model2` | operator | L2 enum-validated (drift → 400) |

## Optional inputs

| Field | Value | Default | Notes |
|---|---|---|---|
| `displayName` | `{displayName}` | (customerId) | for handoff-report + CLAUDE.md |
| `region` | `{region}` | westus2 | for platform resources; H2a OpenAI may override to westus3 per F4 |
| `upgradeMode` | `{upgradeMode}` | Auto | matches `sprk_dataverseenvironment.sprk_upgrademode` |
| `subscriptionId` | `{subscriptionId}` | (platform default) | override for Model 2 dedicated stamp |
| `operatorUpn` | `{operatorUpn}` | (session identity) | logged in handler-log.md per NFR-11 |

## Operator decisions

Timestamped log of any operator judgment call made during Step 1 intake (or later — append as run progresses).

| timestamp | decision | rationale |
|---|---|---|
| — | — | — |

Example: `2026-09-01T10:03Z | Chose westus3 for OpenAI | westus2 lacks gpt-5 family (per F4)`

## Cross-refs

- Placeholder `sprk_dataverseenvironment` record: `{environmentRecordId}` (created by skill Step 1 pre-POST /api/runs)
- L2 run enqueue: `POST /api/runs` payload snapshot at [`preflight-report.md`](preflight-report.md) §H0
