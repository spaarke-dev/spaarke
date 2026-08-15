# Task 063 — Naming Standard: r1 Handoff (current→canonical rename map)

> **From**: `code-quality-and-assurance-r3` (owns the STANDARD + the GATE)
> **To**: `customer-provisioning-orchestration-r1` (owns APPLYING canonical names at provisioning +
> REMEDIATING live-environment drift during maintenance windows)
> **Date**: 2026-08-14
> **Source census**: `workstreams/config-deployment/design.md` (task 017, FR-29) + `notes/bff-auth-surface-map.md` §Portal verification

## What r3 delivered (this task)

1. **Standard** — `docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md` § "KV-Secret & Resource
   Naming Standard (Conformance-Gated)": the env-agnostic rule (R1), one-canonical-casing (R2),
   canonical vault `sprk-{env}-kv` with the `spaarke-spekvcert` DO-NOT-RENAME dev exception (R3), and
   the no-orphan/no-duplicate policy, plus the single reference syntax.
2. **Gate** — `scripts/naming-conformance-check.ps1` (self-tested; read-only). Runs **advisory** until
   r1 remediates; flips **blocking** per-surface as each reaches zero violations. CI-workflow wiring is
   the coordinated follow-on (see `task-042-063-ci-gate-wiring-deferral.md`).

## r1's remediation backlog — current → canonical (from the 017 census)

r3 renames NOTHING live. r1 applies these during a maintenance window (update KV secret name +
App Service KV references + rotation), env-coordinated (prod currently decommissioned → cheap window):

| Current (drift) | Canonical | Census ref | Notes |
|---|---|---|---|
| `SPRK-DEV-DATAVERSE-URL` (env-token-in-name) | `Dataverse-ServiceUrl` | D3-04 / FR-29 | env token in a replicated name |
| AI-Search key: 3 aliases / 3 casings (`AzureAISearchApiKey`, …) | one canonical `aisearch-admin-key` (or `AiSearch--AdminKey`) | D5-02 / D6-03 / D2-03 | ⚠ Dataverse + live App-Service pre-check FIRST (dual-bound; rotation hazard) |
| `BFF-API-ClientSecret` vs `bff-api-client-secret` (Office-addin path) | single canonical casing `BFF-API-ClientSecret` | D6-04 | casing split — rotate-one-break-other; grandfather ONE casing |
| vault `sprk-platform-prod-kv`, `kv-sdap-{env}`, `spaarke-kv-dev`, `sprkshareddev-kv` | `sprk-{env}-kv` + codified `spaarke-spekvcert` dev exception | D6-01 / D10-04 | make bicep vault name a PARAM; do not recreate the live vault |
| platform.bicep flat keys `openai-api-key`/`aisearch-admin-key`/`docintel-key` (naming-orphaned, 0 code binds) | canonical names + `__` app-setting keys, or delete redundant settings | D6-02 | IaC vs script divergence |
| 6 template-referenced secrets never seeded (orphan refs) | add to canonical seeder manifest (or document out-of-band) | D3-07 / D2-05 | fail-closed today (safe) but irreproducible |
| webhook secrets `communication-webhook-signing-key` vs `compose-webhook-signingkey` (inconsistent separation) | consistent kebab separation under canonical convention | D6-08 | run-together forms are documented live PROD names — env-coordinate |

## Durable fix (r1, Tranche B — 017 design §Phase 3)

Drive the seeder + Configure script + tokens doc from ONE canonical secret-catalog manifest (name +
purpose + env), parameterize the vault name by environment everywhere (D5-03). This closes the
D2-03/D2-05/D3-07 orphan/duplicate class at the root. r3's gate then guards it from re-drifting.

## Live-state pre-check obligation (BINDING before any alias removal)

Per 017 design §4: before r1 removes any alias/fallback spelling, pre-check the LIVE App Service
settings + KV + any Dataverse-persisted config — a live env may be feeding the alternate name. Never
delete `Dataverse-ClientSecret` / `BFF-API-ClientSecret` (never-remove — OBO + shared-lib Dataverse).
