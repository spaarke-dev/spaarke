# Data Model — `sprk_playbookconsumer`

> **Last reviewed**: 2026-06-28
> **Status**: Canonical schema reference. Architecture/dispatch semantics live in [`docs/architecture/ai-architecture-playbook-consumer-routing.md`](../architecture/ai-architecture-playbook-consumer-routing.md).
> **Solution**: `Spaarke` (managed component).
> **Owner**: AI Platform team.
> **Created by**: `spaarke-ai-platform-chat-routing-redesign-r1` Phase 1R (FR-1R-02 / FR-1R-03 / FR-1R-04).

---

## 1. Purpose

Stores the runtime mapping from a **consumer surface** (a BFF service, endpoint, widget, or Agent that needs to dispatch an AI playbook) to the **playbook GUID** it should invoke. Resolved by [`IConsumerRoutingService.ResolveAsync`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/IConsumerRoutingService.cs) at runtime.

Replaces the per-consumer `Workspace__*PlaybookId` environment-variable lookup pattern that shipped pre-Phase-1R. Maker-managed; no BFF deploy required to redirect a consumer.

---

## 2. Schema

### 2.1 Entity metadata

| Property | Value |
|---|---|
| **Logical name** | `sprk_playbookconsumer` |
| **Display name** | "Playbook Consumer" |
| **Schema name** | `sprk_PlaybookConsumer` |
| **Primary key** | `sprk_playbookconsumerid` (Guid, system) |
| **Primary name column** | `sprk_consumertype` (string) — used in Power Apps lookup UIs |
| **Ownership** | Organization (not user-owned — these are infrastructure routing rows) |
| **Audit** | Enabled (changes to routing rows are security-relevant) |
| **Statecode** | Standard active/inactive (use `sprk_enabled` for soft-disable; statecode is for record lifecycle) |

### 2.2 Columns

| Logical name | Type | Required | Default | Meaning |
|---|---|---|---|---|
| `sprk_playbookconsumerid` | UniqueIdentifier (system PK) | system | system | Stable row identifier |
| `sprk_consumertype` | String (NVARCHAR(250)) | **Yes** | — | Stable consumer-type code. Must match a `public const string` in [`ConsumerTypes.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerTypes.cs). Lower-kebab-case, no spaces (e.g., `matter-pre-fill`, `daily-briefing-narrate`). |
| `sprk_consumercode` | String (NVARCHAR(100)) | No | `"default"` | Sub-discriminator within a consumer type. Resolution prefers exact match, then falls back to `"default"`. Use for area-specific variants (e.g., `vendor-contract`, `employment`). |
| `sprk_enabled` | Boolean | **Yes** | `true` | If `false`, the row is ignored at resolve time. Soft-disable without deletion. |
| `sprk_playbookid` | Lookup → `sprk_analysisplaybook` | **Yes** | — | The target playbook to dispatch. `sprk_analysisplaybook` is the existing playbook entity. |
| `sprk_environment` | String (NVARCHAR(50)) | No | `"*"` | Environment scope: `dev`, `test`, `prod`, or `*` for wildcard. Specific environments win over wildcard. |
| `sprk_priority` | Whole Number (Int32) | **Yes** | `500` | Tiebreaker when multiple rows match. **Lowest wins.** Use `100` for high-priority overrides, `500` for normal, `900` for fallback. |
| `sprk_matchconditionsjson` | Memo (NVARCHAR(MAX)) | No | `null` | Optional JSON predicates evaluated against `IRoutingContext`. Used for content-aware routing (MIME type, document classification). Schema: see [`projects/spaarke-ai-platform-chat-routing-redesign-r1/architecture/playbookconsumer-matchconditions.schema.json`](../../projects/spaarke-ai-platform-chat-routing-redesign-r1/architecture/playbookconsumer-matchconditions.schema.json). Unknown keys are IGNORED (defensive forward-compat). |
| `createdon` | DateTime | system | system | Standard audit |
| `modifiedon` | DateTime | system | system | Standard audit |
| `createdby` | Lookup → `systemuser` | system | system | Standard audit |
| `modifiedby` | Lookup → `systemuser` | system | system | Standard audit |
| `ownerid` | Lookup → `systemuser` / `team` | system | system | Organization-owned; ownership rarely meaningful |

### 2.3 Indexes + alternate keys

| Index | Columns | Purpose |
|---|---|---|
| Standard FK index | `sprk_playbookid` | Lookup performance for "find all consumers using this playbook" reverse queries |
| (Recommended) | `sprk_consumertype`, `sprk_environment`, `sprk_enabled` | Speeds up the `ResolveAsync` query path |

No alternate keys defined — uniqueness is enforced operationally (the resolution algorithm picks the lowest-priority match), not by schema constraint. Multiple rows with the same `(consumertype, code, environment)` are valid and serve as override-with-fallback patterns.

### 2.4 Relationships

| Direction | Related entity | Cardinality | Behavior |
|---|---|---|---|
| Many-to-One | `sprk_analysisplaybook` (via `sprk_playbookid`) | N:1 | Many consumers can route to the same playbook. **Restrict** delete on the playbook side — deleting a playbook with active consumer rows would break dispatch silently. |
| Many-to-One | `systemuser` / `team` (via `ownerid`, `createdby`, `modifiedby`) | N:1 | Standard ownership/audit |

No N:N relationships. **Note**: §10.3 of the architecture doc discusses why true N:N (one consumer → multiple playbooks) is NOT modeled at this layer — composition is handled at the playbook layer via multi-node playbooks instead.

---

## 3. Sample rows (production, 2026-06-28)

The seven rows currently deployed to `spaarkedev1`. Each is created by chat-routing-redesign-r1 Phase 1R seed scripts except the last, which was added by spaarke-daily-update-service-r4.

| `sprk_consumertype` | `sprk_consumercode` | `sprk_environment` | `sprk_priority` | `sprk_enabled` | `sprk_playbook` (display) | Origin |
|---|---|---|---|---|---|---|
| `matter-pre-fill` | `default` | `*` | `500` | `true` | MATTER-PREFILL@v2 | chat-routing-redesign-r1 |
| `project-pre-fill` | `default` | `*` | `500` | `true` | PROJECT-PREFILL@v1 | chat-routing-redesign-r1 |
| `ai-summary` | `default` | `*` | `500` | `true` | DOCUMENT-PROFILE@v1 | chat-routing-redesign-r1 |
| `summarize-file` | `default` | `*` | `500` | `true` | SUMMARIZE-FILE@v1 | chat-routing-redesign-r1 |
| `chat-summarize` | `default` | `*` | `500` | `true` | SUM-CHAT@v1 | chat-routing-redesign-r1 |
| `email-analysis` | `default` | `*` | `500` | `true` | EMAIL-ANALYSIS@v1 | chat-routing-redesign-r1 |
| `daily-briefing-narrate` | `default` | `*` | `500` | `true` | DAILY-BRIEFING-NARRATE | spaarke-daily-update-service-r4 (task 031, row id `b4503359-1771-f111-ab0e-7ced8ddc4a05`) |

### 3.1 Future P1-summarize-document variant routing

Per [`projects/P1-summarize-document-v1/design.md`](../../projects/P1-summarize-document-v1/design.md), P1 will add area-specific variants of `chat-summarize`:

| `sprk_consumertype` | `sprk_consumercode` | `sprk_priority` | `sprk_playbook` |
|---|---|---|---|
| `chat-summarize` | `vendor-contract` | `100` (high — wins over default) | summarize-vendor-contract@v1 |
| `chat-summarize` | `employment` | `100` | summarize-employment-agreement@v1 |
| `chat-summarize` | `real-estate-lease` | `100` | summarize-real-estate-lease@v1 |
| `chat-summarize` | `ip-agreement` | `100` | summarize-ip-agreement@v1 |
| `chat-summarize` | `default` | `500` (fallback) | summarize-generic@v1 (refreshed from SUM-CHAT@v1) |

`SessionSummarizeOrchestrator` will pass the detected area as `consumerCode`. If no area matches, resolution falls back to `code = "default"`.

---

## 4. Seed + deployment

Rows are deployed via PowerShell scripts, not Power Apps maker UI (deterministic + auditable + reproducible across environments):

| Script | Purpose |
|---|---|
| [`scripts/dataverse/Seed-PlaybookConsumers.ps1`](../../scripts/dataverse/Seed-PlaybookConsumers.ps1) | Bulk-seeds the standard 7 consumer rows. Idempotent — uses `Upsert` semantics by `(consumertype, consumercode, environment)`. |
| [`scripts/dataverse/Add-PlaybookConsumer.ps1`](../../scripts/dataverse/Add-PlaybookConsumer.ps1) | Adds or updates a single row. Use this for one-offs (new consumer type, environment-specific override). |

Power Apps maker UI MAY be used for inspection / disabling rows during incident response. New rows added in maker UI are valid but lack the same audit trail as script-deployed rows. Prefer scripts.

---

## 5. Operational notes

### 5.1 Cache propagation lag

`ConsumerRoutingService` caches resolved Guids for **5 minutes** per `(consumertype, code, environment)` tuple. A new row, a disable, or a playbook redirect takes up to 5 min to propagate per BFF instance. Plan deployments accordingly (restart BFF for instant propagation if needed).

### 5.2 Diagnosis: "why is this consumer falling through?"

Check in this order:

1. Does a row exist with `sprk_consumertype = X` AND `sprk_enabled = true`? Query: `dataverse.read_query("sprk_playbookconsumers", filter: "sprk_consumertype eq 'X' and sprk_enabled eq true")`
2. Does the row's `sprk_environment` match the BFF's current environment (or `*`)?
3. If `consumerCode` was passed: does any row's `sprk_consumercode` exactly match? If not, is there a `code = "default"` row?
4. Is the `sprk_playbookid` lookup populated and pointing at an active `sprk_analysisplaybook`?
5. Has 5+ minutes passed since the row was created/changed? (Cache lag)

### 5.3 Audit trail

Standard Dataverse audit is enabled on this entity. Changes to `sprk_playbookid`, `sprk_enabled`, or `sprk_priority` are security-relevant — they redirect playbook dispatch and can silently change AI behaviour for end users. Reviewers should treat consumer-row edits like config changes.

### 5.4 Backup considerations

Rows are deterministic + low-volume + maker-editable. Inclusion in the standard Spaarke solution export covers most cases. For ALM, the seed scripts (§4) are the source of truth for the 7 standard rows.

---

## 6. Related schemas

| Entity | Relationship |
|---|---|
| `sprk_analysisplaybook` | FK target via `sprk_playbookid`. The playbook this consumer routes to. |
| `sprk_playbooknode` | Indirect — accessed via the resolved playbook. The node graph that defines what the playbook does. |
| `sprk_analysistool` | Indirect — accessed via the resolved playbook's node configurations. Tool handlers consumed by playbook nodes. |
| `sprk_analysisaction` | Indirect — actions that orchestrate playbook execution. |

---

## 7. Related docs

| Doc | Topic |
|---|---|
| [`docs/architecture/ai-architecture-playbook-consumer-routing.md`](../architecture/ai-architecture-playbook-consumer-routing.md) | Architecture, semantics, Path A.5, decision matrix, Action Engine relationship |
| [`docs/guides/HOW-TO-ADD-A-CONSUMER-ROUTING-TYPE.md`](../guides/HOW-TO-ADD-A-CONSUMER-ROUTING-TYPE.md) | 3-step procedure for adding a new consumer type |
| `docs/architecture/ai-architecture-playbook-runtime.md` | How the orchestrator executes the resolved playbook |
| [`src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerTypes.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerTypes.cs) | Compile-time constants (must stay in sync with `sprk_consumertype` values) |
