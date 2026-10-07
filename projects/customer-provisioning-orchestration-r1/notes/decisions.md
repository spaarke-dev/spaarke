# Decisions — customer-provisioning-orchestration-r1

> The project `CLAUDE.md` keeps one binding line per decision. This file says where the full rationale lives and
> records every rule that was **superseded or withdrawn**, with the date, so nobody restores it from an old note.
> Created 2026-10-07 when `CLAUDE.md` was restructured to the operating-manual template; the previous `CLAUDE.md` is
> archived verbatim at `handoff-history/CLAUDE-archive-2026-10-07.md`.

## 1. Where the rationale lives

| Series | What | Where |
|---|---|---|
| **design D1–D20** (2026-08) | Original design decisions (L2 control plane, handler catalog, two-tier model) | `../design.md` §3 "Locked Decisions", §16 "Resolved Design Decisions" |
| **plan D1–D29** (owner, 2026-09-30 → 2026-10-06, LOCKED) | Model 1 dedicated-stamp decisions: guests + PAYG (D2), Model 2 out of scope (D3), operator prerequisites (D4), Redis (D12), keyless (D13), MI-FIC owning app (D16), shared-prod RG (D23), Exchange sidecar (D24–D26), demo next (D27), SPE type/container (D28), SPE Admin confined (D29) | `model1-dedicated-remediation-plan.md` §2 |
| **UAC-r2 D-12 / D-13 / D-14** (hyphenated, 2026-09-28) | D-12 dedicated stamps for every customer; D-13 one BFF app registration per customer (BINDING); D-14 customerId standard | `../INCOMING-D12-D13-REMEDIATION.md`; `projects/unified-access-control-r2/notes/D-13-per-customer-bff-app-registration.md` |
| Gaps G1–G38 | What the 2026-09-30 assessment found and which task fixes each | `model1-dedicated-remediation-plan.md` §4, §7 |
| ADR tensions (§6.5) | Every A/B/C resolution with rationale | `../spec.md` "ADR Tensions"; `../design.md` §17 (T227d, T230b, T254, B04, ADR-020/T247) |
| Per-task decisions | T232 guests (D1–D12, K1–K10) · T254 spend limit · T237 customerId · task 031 topology · ADR-028 A4 integration · FIC parity | `t232-guest-access-decisions.md` · `t254-ai-spend-limit-decisions.md` · `t237-customerid-standard.md` · `task-031-topology-decision.md` · `decisions/adr-028-a4-integration-conflict-resolution.md` · `decisions/205b-a42-fic-parity-contract.md` |
| Coordination notes | Hand-offs owed to/from other projects | `coordination/` |

## 2. Superseded or withdrawn — do not restore

| Date | Was | Now | Source |
|---|---|---|---|
| 2026-09-28 | Model 1 = a shared trial/SMB tier (one BFF, one Dataverse environment, many customers) | Every customer gets a dedicated stamp; Model 1 = in Spaarke's tenant, Model 2 = in the customer's (Model 2 out of scope, plan D3) | UAC-r2 D-12; owner 2026-09-30 |
| 2026-09-28 | One multitenant BFF app + admin consent per customer tenant (spec MUST) | One BFF app registration per customer, both models — D-13, BINDING, not to be re-opened | D-13 |
| 2026-09-28 | MUST NOT provision Redis per customer for Model 1 | Redis per stamp in both models | D-12 §3 |
| 2026-10-04 | Per-stamp Redis at Standard tier with an access key | Azure Managed Redis `Balanced_B0`, HA on, Microsoft Entra only | plan D12; T242; ADR-009 as amended |
| 2026-09-28 | ADR-027 Path A exception for the shared tier ("§4D invariants enforce logical isolation") | Withdrawn: ADR-027 amended to one subscription per customer; I2–I4 key on `tenantId`, which cannot separate Model 1 customers anyway | spec "ADR Tensions" |
| 2026-09-28 | ADR-027/028 Path A for B04 multi-tenant Dataverse routing | Withdrawn: no shared tier, one Dataverse environment per stamp | spec "ADR Tensions"; design §17 |
| 2026-10-04 | ADR-028 Path A for H14a Exchange PowerShell (certificate in platform KV) | Superseded: the sidecar holds no credential; `Spaarke Exchange Admin` trusts the L2 Worker UAMI (MI-FIC) | T251; plan D24–D26 |
| 2026-10-07 | ADR-028 time-boxed exception for stamp keys (Redis key, Document Intelligence key) | Closed for stamps (T242, T243); only the shared dev/demo Document Intelligence key fallback remains, until T235 | spec "ADR Tensions" |
| 2026-08-25 | r3-handoff blanket "never delete `Dataverse-ClientSecret` / `BFF-API-ClientSecret`" | `.claude/constraints/provisioning.md` "KV credential lifecycle" (three environment prongs, hold until 2026-11-23) | ADR-028 A4 + E-3 closure |
| 2026-10-06 | "MUST use a confidential-client (app-only) token for SPE container-type creation (T6)" | Container-type creation is delegated-only, an operator one-time step | topology doc §R5 / H8-B |
| 2026-08-17 | "Complete 11 of 14 null `AppRoleId`s in `GraphAppRoles.cs`" | Done (14; task 144 added a 15th) | spec MUST list |
| 2026-10-06 | task-execute Step 9.5 "at most 2 fix rounds, never start round 3" | Withdrawn by the owner (PR #1336): review limits ceremony, never fixing; no cap on fixing real defects | `.claude/skills/task-execute/SKILL.md` Step 9.5 |
| 2026-10-06 | "Never delete Key Vault secrets" (unscoped) | Scoped to spike/test cleanup; planned deletions follow the KV lifecycle rule (e.g. T241's `sprk-prod-kv` only after 2026-11-23) | 2026-10-06 conversion review |
| 2026-10-06 | "Run pac `--help` locally to check usage" (S39) | Never run pac with flags to check usage — on 2026-08-23 that executed `pac admin create-service-principal` | lessons-learned 2026-08-22 |
| 2026-10-02 | `.github/workflows/**` ownership "temporary by default" while ci-cd-unit-test-remediation-r1 was dormant | ci-cd-unit-test-remediation-r1 CLOSED by the owner; this project changes its own provisioning workflows; check `projects/INDEX.md` first | owner 2026-10-02 (T225a escalation) |
| 2026-09-28 | T216 (separate task) and T217 (separate task) | T216 dropped; T217 folded into T218 (define the complete solution package) | owner 2026-09-28 |
| 2026-10-06 | "Do NOT enable Managed Environments / Environment Groups / PAYG for Model 1" (owner Q1–Q3, 2026-08-23) | Decided for the retired shared environment. The customer's Dataverse environment is an operator prerequisite (T228), and its PAYG billing policy is now REQUIRED for guest access (PRQ-C-11, owner 2026-10-07) | conversion review; T232 |
| 2026-10-06 | "KEEP the trial1 environment permanently" (SESSION 11) | N/A — that trial1 was never created; `rg-spaarke-trial01-prod-model1` is the 2026-08-22 stand-up stamp, deleted under T241 | conversion review |
| 2026-10-06 | `runs/trial1-intake.json` as T186's intake | Pre-D-12 (Model1Shared / shared-trial, no subscriptionId / containerTypeId / dataverseEnvUrl) — do not use; T186's target customer is not yet defined | CLAUDE.md gotcha |
| 2026-10-06 | NFR-04 cost envelope per tier, `costEnvelopePolicy`, `warnAndProceed` | One envelope per dedicated stamp, both models | T229 |
| 2026-08-20 | NFR-07 god-class LOC ratchet | Retired repo-wide; complexity judged at review | root CLAUDE.md §11 |
| 2026-10-06 | INCOMING-D12-D13 banner "READ FIRST — do not start work or resolve a merge conflict before reading" | The four required D-13 changes are done (T222, T227, T228); the file stays as reference | `../INCOMING-D12-D13-REMEDIATION.md` |
| 2026-10-07 | `scripts/check-task-status-drift.ps1` reports false "unpaired" entries for this project | No longer reproduces: 214/214 parsed, no drift (2026-10-07) | — |
| 2026-10-07 | Project `CLAUDE.md` sections that copied repo-wide rules: task-execute protocol, rigor and model-tier tables, sub-agent boundary, context thresholds, escalation triggers, per-tag constraint/pattern tables, "load these first" list, canonical-implementation exemplars | Removed: root `CLAUDE.md` §3–§8, `task-execute`, `task-create` and each POML's `<knowledge>` carry them; the code is the exemplar | claudemd-template restructure |
| 2026-10-07 | Coordination table naming code-quality-and-assurance-r3, spaarke-ai-architecture-redesign-r1/r2, devops-project-tracking-r1 (PR #453), "19 active BFF worktrees" | Dropped: no current shared work with them; `/conflict-check` + `projects/INDEX.md` cover hot-path overlap | — |

## 3. Escalation triggers carried from the old CLAUDE.md

Still valid; they now live in `CLAUDE.md` §2/§3 or are covered by repo rules:
- A `GraphAppRoles.cs` enumeration returning unexpected role ids (drift) → escalate.
- A KV secret rename/delete without the live App Service + KV + Dataverse pre-check (FR-35) → stop.
- A tenant-isolation invariant (I1–I5) failure → escalate.
- A Model 2 customer commitment → reopens Model 2 scope (plan D3).
- Live-dev KV drift found during naming work → report, do not remediate live dev (owner directive #3, 2026-08).
