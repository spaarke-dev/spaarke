# Provisioning Constraints

> **Last Reviewed**: 2026-08-25
> **Reviewed By**: customer-provisioning-orchestration-r1 task 203a per punch list row A08
> **Load when**: task tags include `provisioning`, `provisioning-run`, `l2-controlplane`, `provisioning-handler`, `customer-provisioning`
> **Wired into**: `.claude/skills/task-execute/SKILL.md` Step 4a tag map
> **Sibling constraints**: `.claude/constraints/{api,pcf,auth,data,ai,jobs,testing,config,bff-extensions}.md`

## Purpose

Cross-cutting constraints for customer-provisioning code — the L2 control-plane (`src/server/services/Sprk.Provisioning.ControlPlane.*/**`), Bicep infra (`infrastructure/bicep/**`), operator scripts (`scripts/provisioning*/`), and the `/provision-environment` L3 skill. Applies whether the task touches a handler, a template, a preflight check, or the L3 skill wiring.

**Load this alongside**: `.claude/constraints/bff-extensions.md` (for any BFF-side work surfaced during provisioning), `.claude/constraints/auth.md` (for H3/H4/H10), `.claude/constraints/data.md` (for H5/H6/H7 Dataverse ops), and the pattern files at `.claude/patterns/provisioning/*.md`.

## Tenant-isolation invariants (I1–I5) — BINDING per design.md §4D

Any code path in the L2 control-plane, handlers, BFF-provisioning surface, or operator scripts that touches customer resources MUST enforce these:

- **I1 — Explicit tenantId** (FR-28): the operator's `tenantId` is passed explicitly on every operation. **NEVER** hardcode a "default tenant" in provisioning scripts. Handlers reject requests missing `tenantId`. Operator's own AAD identity per NFR-11 — bootstrap under a service principal is a HARD violation.
- **I2 — AI Search unconditional `tenantId` filter** (FR-29): every AI Search query MUST include `tenantId eq '{tenantId}'` in the filter clause. The `spaarke-session-files` index uses tenantId + sessionId dual-filter (ADR-014 strengthens). A query missing this filter is a silent cross-tenant leak.
- **I3 — Cosmos partition-key predicate** (FR-30): every Cosmos read/write MUST include the partition-key `/customerId` predicate. Cross-partition queries are audit-flagged and treated as bugs.
- **I4 — SPE container ID resolution** (FR-31): SPE container IDs derived from tenant context via `ITenantContainerResolver`. NEVER hardcode a container ID or resolve via lookup that isn't tenant-scoped.
- **I5 — Graph token per-tenant** (FR-32): every Graph token acquisition uses tenant `{tenantId}` — never the operator's home tenant, never a shared "app-tenant" for cross-tenant ops. `ITokenAcquisition.GetAccessTokenForAppAsync(tenant)` is the discipline.

Violations of I1-I5 surface via ArchTests (planned in task 204e) + are Critical findings in code-review Step 6.

## KV secret never-delete list — BINDING per root CLAUDE.md §10 + spec.md MUST

**NEVER delete these KV secrets** from any KV in any code path, script, or handler:

- `Dataverse-ClientSecret` — legacy but still referenced; deletion breaks BFF auth path.
- `BFF-API-ClientSecret` — active BFF client-cred.

Additionally, any secret with `never_delete: true` in `scripts/canonical-secret-catalog/manifest.yaml` MUST NOT be deleted regardless of context (test cleanup, sweep, "temporary" removal).

`§7.9 pre-check gate` — BINDING per spec.md FR-35: BEFORE any secret rename/delete, verify LIVE App Service + KV + Dataverse-persisted config for references. Skipping the pre-check is a HARD violation.

## Publish-size ≤60 MB — BINDING per CLAUDE.md §10 NFR-01

Every BFF-touching task (including provisioning tasks that modify BFF DI, register new services, or update BFF app-settings via H4b) MUST measure and report BFF publish size.

- Command: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`
- Measure: compressed size (`du -sh deploy/api-publish/`) + delta vs prior baseline (currently ~44.96 MB incl. PDBs, dotnet-10 framework-dependent linux-x64).
- Thresholds: `≥+5 MB single-task delta` → explicit justification required; `≥55 MB cumulative` → architecture review; `≥60 MB` → HARD STOP.

Report absolute + delta in task notes / PR description. See `.claude/constraints/azure-deployment.md` "BFF Publish-Size Per-Task Verification Rule" for full mechanic.

## Handler registration completeness — BINDING per ADR-032 + `.claude/patterns/provisioning/handler-registration-completeness.md`

- Every new `IProvisioningHandler` is a 3-file dance: `HandlerIds.cs` + `HandlerDispatchRegistrationModule.cs` + `Worker/Program.cs`. Missing any → runtime dispatch throws.
- `HandlerRegistrationCompletenessTests` ArchTest MUST pass on every PR (currently 21/21; adding a handler → 22/22 target).
- Handler contract: `IProvisioningHandler.ExecuteAsync(HandlerEnvelope, CancellationToken)` returns `HandlerResult` (Success | Failure | Deferred | Rollback). No other shapes accepted by the L2 dispatcher.
- Feature-gated handlers follow ADR-032 P1/P2/P3 — null-impl UNCONDITIONAL outside the gate; real-impl CONDITIONAL inside. Last-write-wins for the same key resolves correctly at runtime.

## ADR-032 F.1 asymmetric-registration — BINDING per `.claude/constraints/bff-extensions.md` § F.1

Cross-references the BFF-side constraint verbatim; applies analogously to L2 control-plane modules. Every conditionally-registered service (inside `if (options.EnableX)` block in `*Module.cs`) MUST have a null-object counterpart registered UNCONDITIONALLY. No unconditional consumer may inject a conditionally-registered service — inject the INTERFACE + let the kill-switch resolve at runtime.

Static-scan recipe + IActionSeam case study: see `.claude/patterns/provisioning/null-object-kill-switch-anti-pattern.md`.

## Fixture-Config-FIRST + Empirical-Reproduction-FIRST (F.2 / F.3)

When a provisioning-adjacent test suspects a DI issue, FIRST inspect fixture config for non-contract values (per `docs/procedures/test-fixture-contracts.md`) before assuming DI code is wrong (F.2).

Before applying a ledger entry's recommended fix, hand-trace + reproduce empirically. File a path-b decision record if root cause differs from the ledger's (F.3).

## Class-A / Class-B / Class-C routing — BINDING per owner 2026-08-24

Provisioning-surfaced bugs default-route to the project that OWNS the fix location:

- **Class-A** — fix in `src/server/services/Sprk.Provisioning.ControlPlane.*/**` → land in provisioning project.
- **Class-B** — fix in `src/server/api/Sprk.Bff.Api/**` or `src/server/shared/Spaarke.*/**` → file in BFF-owning worktree (per project CLAUDE.md coordination table). SESSION 7 amendment: if target BFF worktree is CLOSED and row is E2E-blocking, absorb into current provisioning project as a task-204 sub-phase.
- **Class-C** — touches both surfaces → document split in punch list; coordinate merge across worktrees.

Every Class-B routing MUST include an accompanying ArchTest (or equivalent forcing function) that prevents the class-of-bug at build time. Fix without prevention = fix that will recur.

Full mechanic: `.claude/patterns/provisioning/bff-vs-provisioning-boundary.md`.

## Handler idempotency + drift-detection

- Every handler MUST be idempotent. Second run of the same handler against the same customer resources produces the same end state (assuming no external drift).
- Drift-detection handlers (H4-shared for from-shared-service secrets, H10 for setup registry) MUST audit-log the drift + rotate/repair automatically OR escalate per §6.5 (if drift indicates an ADR conflict).
- Idempotency verified via `HandlerIdempotencyTests` (per handler). Missing test → PR blocker.

## Progressive fail-fast recovery — BFF startup completeness

- New BFF `AddOptions<T>().ValidateOnStart()` module → MUST add corresponding entry to `per_env_settings` list in `scripts/canonical-secret-catalog/manifest.yaml`.
- Deploy discipline: H4b bulk-set applies ALL settings in ONE batch → ONE App Service restart cycle. NO manual `az webapp config appsettings set` single-setting fixes in production.
- Nightly `IOptions-inventory-drift` ArchTest (planned task 203-followup) catches drift between BFF DI + manifest.

Full mechanic: `.claude/patterns/provisioning/progressive-fail-fast-recovery.md`.

## Reserved-suffix registry for global-namespace resources

Global-namespace resources (Service Bus, Storage, Cognitive Services, ACR, Front Door) MUST run `checkNameAvailability` preflight at Step 2.5 (per `.claude/patterns/provisioning/resource-name-availability-precheck.md`) + consult `scripts/provisioning-prereqs/reserved-suffixes.yaml` (task 203-followup authors).

- Service Bus reserves `-sb` suffix (F10 discovery, 2026-08-22).
- Adding a new global-namespace resource type → add its `checkNameAvailability` API to preflight + add any known reserved suffixes to the registry.

## Prerequisites — `docs/guides/PROVISIONING-PREREQUISITES.md`

Every provisioning task MUST honor the prereq registry. `/provision-environment` Step 0.5 verifies via `scripts/provisioning-prereqs/prereqs.yaml`. Adding a new manual prereq → add to both files with `scope`, `tenancyModel`, `check_recipe`, `consequence-if-absent`.

Task 202 established the registry with 27 prereqs across 4 scopes (once_per_tenant, once_per_subscription, once_per_env, once_per_customer). Do NOT bypass Step 0.5.

## Auth v2 (ADR-028) — 21 MUSTs

Provisioning tasks touching auth (H3, H4, H10, `/provision-environment` skill Step 0-6) MUST honor the ADR-028 21 MUSTs. Key ones:

- UAMI-outbound preference (not System-Assigned) for shared platform → source services.
- `keyVaultReferenceIdentity` on App Service MUST be set to the UAMI resource ID when `identity.type='UserAssigned'`.
- Client-cred rotation cadence: no more than once per 90 days SCHEDULED (drift-recovery is a different failure mode, escalate per §6.5).
- Operator's own AAD identity per NFR-11 (never a service principal).

Full pattern: `.claude/patterns/provisioning/keyvault-reference-identity-invariant.md` (T1) + `.claude/patterns/provisioning/operator-rbac-bootstrap.md` (F15/F18).

## Sub-Agent Write Boundary — BINDING per root CLAUDE.md §3

Tasks touching `.claude/**` (skills, patterns, constraints, catalogs, agents, settings) MUST run in the MAIN SESSION. Sub-agents cannot Write/Edit these paths and will fail with "Edit denied". `task-create` auto-marks these tasks `parallel-safe: false`.

If a parallel agent is accidentally dispatched to a `.claude/**` task, it will fail cleanly — main session picks up sequentially. Do NOT attempt workarounds.

## Test update obligation — analogous to bff-extensions.md § F

PRs modifying `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/**` MUST add/update tests in `tests/unit/Sprk.Provisioning.ControlPlane.Core.Tests/Handlers/**`. Handler-registration-completeness ArchTest is the forcing function; skipping the actual behavior tests is a Critical finding.

PRs modifying `scripts/canonical-secret-catalog/manifest.yaml` MUST prove generator determinism via `Invoke-CatalogGenerator.ps1 -Verify` → exit 0.

## E2E acceptance ceremony — task 186 gate

Task 186 (E2E live-fire against sub `cd95fcec-6b89-49ea-8339-c2b579b12587`) is BLOCKED by the pre-check trigger: `pre-live-fire-punch-list-gate` per task 202 (BINDING per owner 2026-08-24). Cannot fire until Class-A + Class-B `blocks_e2e=yes` rows are all `applied|already-applied`. Verify via `notes/task-202-punch-list.md` status column before invoking 186.

## Cross-refs

- Sibling constraints: `.claude/constraints/{api,auth,data,jobs,testing,config,bff-extensions,azure-deployment}.md`
- Related patterns: `.claude/patterns/provisioning/*.md` (9 files)
- Related ADRs: ADR-004 (job contract), ADR-013 (AI architecture), ADR-028 (Spaarke auth v2), ADR-032 (Null-Object kill-switch), ADR-036 (background job infrastructure), ADR-044 (Dataverse GUID canonicalization)
- Related project docs: `projects/customer-provisioning-orchestration-r1/{spec.md,design.md,CLAUDE.md,notes/task-202-punch-list.md}`
- Related prereq: `docs/guides/PROVISIONING-PREREQUISITES.md` + `scripts/provisioning-prereqs/prereqs.yaml`
