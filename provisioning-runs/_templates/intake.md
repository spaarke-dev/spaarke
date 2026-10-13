# Intake — {customerId}-{runId}

> **Created by**: `customer-provisioning-orchestration-r1` task 203a per punch list row A06 (template).
> Analog of `spec.md`. Populated by `/provision-environment` skill Step 1.

## Required inputs

| Field | Value | Source | Notes |
|---|---|---|---|
| `customerId` | `{customerId}` | operator | **The customerId standard** `^[a-z][a-z0-9]{2,7}$` — 3-8 lowercase letters and digits, starting with a letter, no hyphens ([`AZURE-RESOURCE-NAMING-CONVENTION.md`](../../docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md) § "The customerId standard"). Abbreviate a longer name once, here (`northwind` → `nwind`). Stored in `sprk_dataverseenvironment.sprk_customerid`; enforced by skill Step 1a, `intake.schema.json` and `POST /api/runs`. |
| `tenantId` | `{tenantId}` | operator | **explicit per NFR-11 (I1)** — never inferred |
| `environmentId` | `{environmentId}` | operator | `sprk_dataverseenvironment` GUID placeholder created in skill Step 1 pre-POST |
| `tenancyModel` | `Model1 \| Model2` | operator | drives the **Lighthouse-delegation + admin-consent** requirement (Model 2 only). *Amended 2026-09-28 (D-12): was "H4-per-tenant vs H4-shared handler branching" — there is no shared handler surface.* |
| `identityPreset` | `B2BGuest \| NativeAccount` | operator | H11 (design.md D6). Exact case. T245c. |
| `users` | **`{userCount}` entries — count only** | operator | H11. 🔒 Names and emails are personal data: they live in the L2 run document (owner decision 2026-10-01) and are **never** written here — this folder is committed to git. |
| `exchangePolicyScopeGroupId` | `{exchangePolicyScopeGroupId}` | operator (group created by the stamp tenant's Exchange admin — PRQ-C-08) | H14a ApplicationAccessPolicy scope. T245c. |
| `communicationDefaultMailbox` | `{communicationDefaultMailbox}` | operator | H4 → KV `Communication-DefaultMailbox`. T245c. Use a shared/service mailbox — this file is committed, so a personal mailbox (here or in the Graph resources above) would put an individual's address in git. |
| `profile` | `spaarke-hosted-model2 \| customer-owned-model2` | operator | Follows from `tenancyModel`: `Model1` ↔ `spaarke-hosted-model2`, `Model2` ↔ `customer-owned-model2`. `POST /api/runs` refuses any other value or pair (`tenancy-profile-invalid`). The names predate the D-12 renumbering; `spaarke-hosted-model1-trial` is retired. |

## Optional inputs

| Field | Value | Default | Notes |
|---|---|---|---|
| `displayName` | `{displayName}` | (customerId) | The customer's full name (e.g. `Northwind Traders`). Written to `sprk_dataverseenvironment.sprk_name` next to the id, so the abbreviation is recorded once on the registry row. Also used in the handoff report. |
| `region` | `{region}` | westus2 | for platform resources; H2a OpenAI may override to westus3 per F4 |
| `upgradeMode` | `{upgradeMode}` | Auto | matches `sprk_dataverseenvironment.sprk_upgrademode` |
| `subscriptionId` | `{subscriptionId}` | **required — no default** | 🔴 **One Azure subscription per customer in BOTH models** (ADR-027 amended 2026-09-28). There is no platform-default subscription to fall back to. |
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
