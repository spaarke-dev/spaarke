# CLAUDE.md — {customerId}-{runId} provisioning run

> **Created by**: `customer-provisioning-orchestration-r1` task 203a per punch list row A06 (template).
> Loaded by Claude Code when operating in this folder.
> Root CLAUDE.md + project CLAUDE.md rules apply — this file EXTENDS.

## What this run does

1-sentence: "Provision customer '{displayName}' (customerId=`{customerId}`) to tenancy model `{tenancyModel}`, profile `{profile}`, target sub `{subscriptionId}`, target region `{region}`, started `{startTime}` by operator `{operatorUpn}`."

## Cross-refs (in this run folder)

- Intake: [`intake.md`](intake.md)
- Prereqs snapshot: [`prerequisites-check.md`](prerequisites-check.md)
- Preflight report (H0 + Step 2): [`preflight-report.md`](preflight-report.md)
- Handler live log: [`handler-log.md`](handler-log.md)
- Manual gates: [`manual-gates.md`](manual-gates.md)
- Final report: [`handoff-report.md`](handoff-report.md) (written at Step 6)
- Lessons: [`lessons-learned.md`](lessons-learned.md) (written at Step 7 — MANDATORY)

## Applicable prereqs

Filter [`docs/guides/PROVISIONING-PREREQUISITES.md`](../../docs/guides/PROVISIONING-PREREQUISITES.md) by `scope` + `tenancy-model` for this run. Failures at Step 0.5 HARD STOP the run before Step 2 preflight: record the failure in [`manual-gates.md`](manual-gates.md), apply the prerequisite's `remediation`, then resume from Step 0.5.

## Run-scoped invariants (per project design.md §4D tenant-isolation)

- **I1** — `customerId`, `tenantId`, and `runId` are IMMUTABLE for this folder's lifetime. No handler reassigns them.
- **I2** — All AI Search queries for this run MUST include unconditional `tenantId eq '{tenantId}'` filter (FR-29).
- **I3** — All Cosmos reads/writes for this run MUST include partition-key `/customerId` predicate (FR-30).
- **I4** — SPE container IDs come from the record being served or the stamp's own settings, and every app-only SPE call passes `SpeContainerOwnershipGuard` — the one definition of the stamp's containers (FR-31; T227d/T227f).
- **I5** — All Graph token acquisitions for this run use tenant `{tenantId}` (FR-32).
- **BINDING** — Before creating, seeding, deleting or purging any Key Vault secret for this run, read and apply the current text of [`.claude/constraints/provisioning.md`](../../.claude/constraints/provisioning.md) "KV credential lifecycle" (time-boxed and environment-specific; root CLAUDE.md §9).

## Escalations

Any operator escalation → append to [`manual-gates.md`](manual-gates.md) with timestamp + decision + rationale. Per root CLAUDE.md §6.5 protocol, ADR-conflict escalations use the root §6.5 format (ADR, quoted rule, conflict, proposed path, rationale, impact, alternatives rejected).

## Postmortem obligation

Skill Step 7 (mandatory per task 203) writes [`lessons-learned.md`](lessons-learned.md) BEFORE the run is marked complete. Cross-run audit rolls up recurring themes into `PROVISIONING-PREREQUISITES.md` and `.claude/patterns/provisioning/` updates.
