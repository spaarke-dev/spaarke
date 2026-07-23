# Task 074 — Audit-container partition re-key decision record

> **Date**: 2026-07-10 · **Task**: AIR2-074 (FR-D-05, design row R21) · **Status**: code + infra cutover implemented; live copy-forward is an operator deploy step.
> Coordinates with task 050 (memory subject-key discipline) and ADR-042. The task's escalation trigger (large-container migration risk) is addressed by the non-destructive posture below.

## What the re-key is

The permanent Tier-2 compliance **audit** container was keyed on bare `/tenantId`. Spaarke deployments are **customer-dedicated** (one tenant per Cosmos database), so `/tenantId` collapses *every* audit write into a **single logical partition** — a slow-motion failure against Cosmos's **20 GB logical-partition cap**. Re-keying while the container is small is cheap; later it is a painful data-movement migration.

Cosmos partition keys **cannot change in place**, so the re-key ships as a **NEW container** with the audit-write path cut over — never an in-place re-key, never destructive to existing audit entries (ADR-015 Tier 2 = append-only compliance log).

## Decision summary

| Question | Ruling | Rationale |
|---|---|---|
| **Partition key** | NEW container **`audit-partitioned`**, partition path **`/partitionKey`** = synthetic **`{tenantId}\|{yyyy-MM}`** (tenant + monthly time bucket) — NEVER bare `/tenantId` | Time-bucketing rolls each month into a fresh logical partition so no single partition grows unboundedly; tenant remains the leading segment so tenant-scoped compliance queries stay bounded. Design R21 named exactly this shape ("`/tenantId` + month, or synthetic"). |
| **Reconciliation with task 050** | Same **single-synthetic-scalable-key** convention: one synthetic partition-key path, value **computed + normalized at the single store write chokepoint**, never bare `/tenantId`. Memory keys by **subject identity** (`/subjectId`); audit — which has no natural single subject and is inherently time-series + tenant-scoped — keys by **tenant + time** (`/partitionKey`). Tenant-id normalization **mirrors `MemoryItemStore.NormalizeSubjectId`** (trim, strip braces, lowercase-`D` GUIDs). | Spec Unresolved Question: "coordinate with FR-D-05 so both adopt the subject/scalable-key pattern from day one." ADR-042 MUST NOT partition memory by `/tenantId`; the audit container now follows the same rule. |
| **Migration posture** | **NON-DESTRUCTIVE.** New container + code cutover now; existing (small) records **copied forward** (read legacy `audit` → `CreateItem` into `audit-partitioned` recomputing `partitionKey`), leaving the legacy `audit` container **fully intact**. Copy-forward is an **operator deploy step** (commands in `infrastructure/cosmos/audit-container-policy.json` → `deploymentCommands`); this project declares infra in bicep and reports the `az` commands rather than mutating live Cosmos. | Mirrors the 050 fresh-container/leave-legacy ruling. Audit is append-only compliance data — records are **never deleted or rewritten**. Legacy container stays queryable (and retains its own immutability policy) for the tail of its 7-year retention. |
| **Legacy `audit` container** | **NOT retired, NOT re-keyed, NOT touched.** Retained read-only. | Append-only Tier-2 compliance history. Bicep comment + policy JSON `legacyContainer` block warn future maintainers. |
| **Escalation (large container)** | If live `audit` has grown large enough that copy-forward is not cheap/safe, **STOP → choose leave-legacy**: new writes already target `audit-partitioned`; historical reads continue against legacy `audit`. Surface measured size to operator. | POML escalation trigger. Cannot measure live size from the worktree; the premise ("while small") holds and the fallback is non-destructive. |

## Coverage preservation (NFR-06)

The Cosmos audit container's **only** writer is `AuditLogService.LogInteractionAsync` (single chokepoint). All callers route through it — `MemoryItemStore` (memory write/supersede), `SafetyPipelineMiddleware`, `MemoryGovernanceEndpoints` (delete/erase). Re-pointing the container name + partition computation in `AuditLogService` cuts over **every** producer at once, so **no audit coverage is dropped** — memory write/delete events continue to be written, now to `audit-partitioned`. The BFF audit *read* endpoint (`AuditLogEndpoints`) reads Dataverse `sprk_speauditlog`, not this Cosmos container, so there is no BFF read path to re-point; Cosmos-side reads are compliance/Synapse and target `/partitionKey` on the new container.

## Tier separation preserved (ADR-015 / ADR-042)

GDPR erasure on the Tier-3 `memory-items` container **never** touches this Tier-2 audit container (unchanged). The re-key does not alter the erasure/audit boundary.

## Files changed

- `src/server/api/Sprk.Bff.Api/Services/Ai/Audit/AuditPartitionKey.cs` **(new)** — synthetic `{tenantId}|{yyyy-MM}` builder + tenant-id normalization (mirrors 050).
- `src/server/api/Sprk.Bff.Api/Services/Ai/Audit/AuditEntry.cs` — added derived `partitionKey` (`/partitionKey`) property; `tenantId` retained as queryable field + PK segment.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Audit/AuditLogService.cs` — `ContainerName` `audit` → `audit-partitioned`; write partition uses `entry.PartitionKey`.
- `infrastructure/bicep/modules/cosmos-db.bicep` — declared `audit-partitioned` container (`/partitionKey`, TTL -1); legacy `audit` container left as-is.
- `infrastructure/cosmos/audit-container-policy.json` — active container = `audit-partitioned`; `legacyContainer` block + non-destructive copy-forward operator commands.
- Tests: `AuditLogServiceTests.cs` (re-keyed assertions); `AuditPartitionKeyTests.cs` **(new, 9 pure-unit tests)**.

## Placement Justification (BFF Hygiene, root CLAUDE.md §10 / §11)

- **New surface?** No new endpoint, service, DI registration, or NuGet package. One new *internal* helper class (`AuditPartitionKey`) inside the existing `Services/Ai/Audit/` namespace — extends the existing audit-write path rather than introducing a parallel component. One new Cosmos **container** (a re-keyed successor to an existing one; not a new Azure account/dependency).
- **Existing / Extension / Cost-of-doing-nothing**: (1) overlaps only with the existing `AuditLogService` write path; (2) extended it in place (const + PK computation) — no new service; (3) without the re-key, a busy customer-dedicated tenant's audit log hits the Cosmos 20 GB single-partition cap and audit writes begin failing (a concrete compliance-data-loss failure mode).
- **Publish-size**: no package/csproj change; ~1 new small source file, comment/logic edits — delta ≈ 0 MB vs the ~49.63 MB (incl. PDBs) baseline. Well under the 60 MB ceiling; no ≥+5 MB single-task delta.
- **CVE**: no dependency change → no new HIGH/critical CVE surface.

## Verification

- Build: `dotnet build src/server/api/Sprk.Bff.Api/` → **0 errors**.
- Tests: audit suite **25/25 green** (`AuditLogServiceTests` 16/16 in isolation, no flake observed; `AuditPartitionKeyTests` 9/9).
- NEGATIVE (post-cutover): grep confirms **no** `src` code path references the old `audit` container name, `PartitionKey(entry.TenantId)`, or `ContainerName = "audit"`. Remaining `"audit"` literals are the intentionally-retained legacy bicep/policy blocks.
