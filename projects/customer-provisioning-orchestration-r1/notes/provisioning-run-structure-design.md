# Provisioning-Run Structure Design

> **Task**: 202
> **Status**: Design only — task 203 implements
> **Author**: task 202 (2026-08-24)
> **Owner directive 2026-08-24 SESSION 5 (BINDING)**: "shouldn't there be a project structure within which a new environment provisioning project runs?"

---

## The parallel to the coding-project pattern

Spaarke's coding projects follow a well-established structure:

```
projects/{name}/
├── README.md              # 1-page overview + graduation criteria
├── CLAUDE.md              # per-project AI context (loaded when in dir)
├── spec.md                # authoritative FRs + NFRs + SC + ADR Tensions
├── design.md              # architectural decisions + rationale
├── plan.md                # phase structure + dependencies
├── current-task.md        # active-task ephemeral state (recovery)
├── tasks/                 # POML task files + TASK-INDEX.md
└── notes/                 # per-task deviations, drift, lessons
```

Rationale: every coding project is a bounded transaction with (a) declared scope, (b) tracked progress, (c) deferred items surfaced as GitHub issues, and (d) postmortem lessons that flow back into shared knowledge (via `test-diet`, `doc-drift-audit`, root CLAUDE.md updates).

**Every customer provisioning run is a bounded transaction with those same properties.** Today, provisioning runs are ephemeral — the L2 REST API + Cosmos state track *machine* execution, but there's no *human-auditable* per-run artifact bundle to review, hand off, or postmortem. SESSION 2's Model 1 Prod standup demonstrated this gap: 20+ lessons captured in one 870-line `lessons-learned-model1-prod-standup-2026-08-22.md` because there was no per-run home for them.

---

## Proposed layout

```
provisioning-runs/                                # NEW folder at repo root (main repo, not per-worktree)
├── INDEX.md                                      # cross-run registry — mirrors projects/INDEX.md
├── {customerId}-{runId}/                         # one folder per run
│   ├── CLAUDE.md                                 # per-run AI context (loaded when in dir)
│   ├── intake.md                                 # analog of spec.md — operator's 4 inputs + decisions
│   ├── prerequisites-check.md                    # PROVISIONING-PREREQUISITES.md verification snapshot
│   ├── preflight-report.md                       # H0 output + Step 2 preflight report
│   ├── handler-log.md                            # analog of current-task.md — LIVE handler progress
│   ├── manual-gates.md                           # H0.5 / H8 / H11 / any WaitingOnGate transitions
│   ├── handoff-report.md                         # final report — supersedes today's `runs/{runId}.md`
│   └── lessons-learned.md                        # mandatory Step 7 postmortem (task 203 wires it)
└── _archive/                                     # NEW — completed runs older than 90 days move here
    └── {customerId}-{runId}/…                    # same 8-file structure preserved
```

**Storage discipline**:
- `provisioning-runs/` lives in the **main git repo** at root, NOT in per-worktree copies. Worktrees don't own runs; runs belong to Spaarke platform ops.
- Each run folder is authored **incrementally during the run** (skill Step 0 creates folder + intake.md; Step 5 appends manual-gates.md; Step 7 writes lessons-learned.md).
- `handler-log.md` is APPENDED-TO in real time by the skill after each handler completes (mirrors coding `current-task.md` "Completed Steps" section).
- Post-run: git commit + push. The record is auditable + shareable + queryable via GitHub search.

**Not chosen** (evaluated + rejected):
- ❌ Cosmos-persisted per-run structure (task-202 POML escalation trigger #4). File-based mirrors coding-project pattern; aids operator understanding + git-blameability + review-in-PR.
- ❌ Per-worktree `provisioning-runs/` (each worktree owns its runs). Provisioning runs are cross-worktree operations by definition — always belong to main repo.
- ❌ Per-customer-nested folders (`provisioning-runs/{customerId}/{runId}/`). Extra nesting hurts INDEX.md queryability; runId-encoded-in-folder-name is sufficient.

---

## Per-file specifications

### `INDEX.md` — cross-run registry

Mirrors `projects/INDEX.md`. Columns:

```markdown
| runId | customerId | tenancyModel | profile | started | ended | status | lessons-count | tenant-hot-path* | link |
|-------|-----------|--------------|---------|---------|-------|--------|---------------|------------------|------|
| a1b2c3 | acme | Model1 | spaarke-hosted-model1-trial | 2026-09-01T10:00Z | 2026-09-01T11:30Z | Ready | 3 | Y | [folder](acme-a1b2c3/) |
```

`*tenant-hot-path`: Y if the run touched a shared tenant resource that another concurrent run might contend on (SPE container-type, shared BFF Entra app-reg, per-tenant KV RBAC bootstrap). Feeds the "shared-tenant coordination" workflow (analog of `/conflict-check`).

Updated atomically by skill Step 6 (Completion Handoff) — no cron.

### `{runId}/CLAUDE.md` — per-run AI context

Loaded by Claude Code when operating in the run folder. Contents:

```markdown
# CLAUDE.md — {customerId}-{runId} provisioning run

> Loaded by Claude Code when operating in this folder.
> Root CLAUDE.md + project CLAUDE.md rules apply — this file EXTENDS.

## What this run does
1-sentence: "Provision customer '{displayName}' (customerId={customerId}) to tenancy model {model}, profile {profile}, target sub {sub}, target region {region}, started {startTime} by operator {upn}."

## Cross-refs
- Intake: intake.md
- Handler live log: handler-log.md
- Final report: handoff-report.md (writen at Step 6)
- Lessons: lessons-learned.md (written at Step 7)

## Applicable prereqs
Filter `PROVISIONING-PREREQUISITES.md` by scope + tenancy model.

## Run-scoped invariants
- customerId, tenantId, and runId are IMMUTABLE for this folder's lifetime.
- All Cosmos reads/writes for this run MUST include partition-key `/customerId` (I3).
- All Graph token acquisitions for this run MUST use tenant `{tenantId}` (I5).

## Escalations
Any operator escalation → append to manual-gates.md with timestamp + decision + rationale.
```

### `{runId}/intake.md` — operator inputs + decisions

Analog of `spec.md`. Populated by skill Step 1. Fields:

```markdown
# Intake — {customerId}-{runId}

## Required inputs
- customerId: {slug}
- tenantId: {GUID}                # explicit per NFR-11 (I1)
- environmentId: {GUID}           # from sprk_dataverseenvironment placeholder record
- tenancyModel: Model1 | Model2
- profile: spaarke-hosted-model1-trial | spaarke-hosted-model2 | customer-owned-model2

## Optional inputs
- displayName, region, upgradeMode, ...

## Operator decisions
Timestamped log of any operator judgment call (e.g. "Chose westus3 for OpenAI because westus2 lacks gpt-5 family" — F4).
```

### `{runId}/prerequisites-check.md` — Step 0.5 output

Snapshot of `PROVISIONING-PREREQUISITES.md` verification. Table:

```markdown
| prereq_id | name | status | evidence | remediation-if-failed |
|-----------|------|--------|----------|-----------------------|
| PRQ-S-03 | Resource-provider registration | ✅ OK | 12 of 12 namespaces `Registered` (`az provider show` all pass) | — |
| PRQ-C-01 | OpenAI TPM headroom | ❌ FAIL | `gpt-5.4 GlobalStandard: limit 0, current 0` | Support ticket via `az support tickets create` (PRQ-S-02 present ✓) |
```

Written by skill Step 0.5. Failures HARD STOP the run before Step 2 preflight.

### `{runId}/preflight-report.md` — H0 output + Step 2 preflight

H0's own report + skill Step 2 cost + duration + naming-collision + admin-consent status. Written by skill Step 2.

### `{runId}/handler-log.md` — live handler progress

Analog of coding `current-task.md`. Appended after each handler:

```markdown
## Handler H0 (Preflight)
- Started: 2026-09-01T10:05Z
- Completed: 2026-09-01T10:07Z
- Status: Success
- Cosmos-run-id: <cosmos-doc-id>
- Notes: ...

## Handler H2a (Bicep infra)
- Started: 2026-09-01T10:07Z
- Completed: 2026-09-01T10:22Z
- Status: Success
- Resources provisioned: 16
- Notes: ...
```

### `{runId}/manual-gates.md` — gate log

Every gate (H0.5 admin consent, H1 quota, H8 SPE 24h) writes here with:
- Gate name + timestamp opened
- What operator was asked to do
- What operator did (or "abandoned")
- Timestamp closed / resumed / cancelled
- Any escalation decision + rationale

### `{runId}/handoff-report.md` — supersedes `runs/{runId}.md`

Final report at Step 6 (Completion Handoff). Same shape as current skill Step 6 output, but archived per-run rather than free-floating in `runs/`.

### `{runId}/lessons-learned.md` — Step 7 mandatory postmortem

Task 203 adds skill Step 7 (mandatory). Template:

```markdown
# Lessons Learned — {customerId}-{runId}

## What went right
- 3-5 bullets

## What went wrong
- Each surprise (unexpected error, timing gap, silent failure) → normalized shape:
  {lesson-id, symptom, root-cause, fix-applied (yes/no + where), landing-spot,
   blocks-future-runs (yes/no)}

## New prereqs to codify
- Any newly-discovered manual step → propose PROVISIONING-PREREQUISITES.md entry

## New patterns to add
- Any recurring shape worth extracting to `.claude/patterns/provisioning/`

## Recommendations for next run
- Concrete, actionable items
```

Cross-run audit (planned by task 203 or a future audit skill) periodically reads all `lessons-learned.md` files + rolls up recurring themes into `PROVISIONING-PREREQUISITES.md` or `.claude/patterns/provisioning/` updates.

---

## `.claude/patterns/provisioning/` proposal

Sibling to existing `.claude/patterns/{ai,api,auth,caching,dataverse,pcf,testing,ui,webresource}/`. Follows the standard convention: `INDEX.md` + max-25-line pointer files with `When → Read These Files → Constraints → Key Rules` structure.

Initial pattern files (skeletons authored by task 202; content filled by task 203 or future audit):

| Pattern file | When to load |
|---|---|
| `manifest-driven-secret-catalog.md` | Adding a new provisioning handler that seeds KV secrets (points to `scripts/canonical-secret-catalog/`) |
| `handler-registration-completeness.md` | Adding a new `IProvisioningHandler` (points to `HandlerRegistrationCompletenessTests`) |
| `progressive-fail-fast-recovery.md` | Diagnosing BFF SIGABRT chain / IOptions ValidateOnStart cascades (points to H4b + F20 lessons) |
| `operator-rbac-bootstrap.md` | Fresh sub + fresh KV data-plane bootstrap (F15/F18 pattern; `az rest --method put` fallback) |
| `keyvault-reference-identity-invariant.md` | App Service KV ref binding correctness (T1 + F16/F16.5) |
| `resource-name-availability-precheck.md` | Global namespace collision prevention (F10 pattern) |
| `openai-quota-region-composition.md` | Compose region + deployment set from auto-granted TPM (F1/F2/F4/F5) |
| `null-object-kill-switch-anti-pattern.md` | Detecting ADR-032 F.1 asymmetric-registration bugs at design time (IActionSeam case study) |
| `bff-vs-provisioning-boundary.md` | Deciding whether a lesson belongs in a BFF worktree vs this project (§10 F + task 202 constraint) |

Root-level `INDEX.md` addition to `.claude/patterns/INDEX.md`:

```markdown
| [provisioning/](provisioning/INDEX.md) | ~9 | Customer provisioning handlers, prereqs, RBAC bootstrap, secret-catalog | 2026-08-24 | Skeleton (task 203 fills) |
```

### `.claude/constraints/provisioning.md` proposal

Sibling to existing `.claude/constraints/{api,pcf,plugins,auth,data,ai,jobs,testing,config}.md`. Cross-references `PROVISIONING-PREREQUISITES.md` + bff-extensions.md § F (asymmetric-registration) + the tenant-isolation invariants I1-I6.

Wired via `task-execute` Step 4a tag map (task 203 authors):

```
| provisioning, provisioning-run, l2-controlplane, provisioning-handler | .claude/constraints/provisioning.md |
```

---

## Cross-refs into other files (planned)

- `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` §4.3 (Post-Phase-D flow) — add: "For every run, a folder is created at `provisioning-runs/{customerId}-{runId}/`. See [Provisioning-run structure design](../../projects/customer-provisioning-orchestration-r1/notes/provisioning-run-structure-design.md)."
- `.claude/skills/provision-environment/SKILL.md` Step 0 pre-step — add: "Create provisioning-run folder + intake.md before any handler enqueue."
- `.claude/skills/provision-environment/SKILL.md` Step 7 (NEW, mandatory postmortem) — task 203 authors.
- Root `CLAUDE.md` §17 pointers table — add row for provisioning-runs pattern.

## Implementation plan (task 203)

1. Create `provisioning-runs/` root + `INDEX.md`.
2. Create `provisioning-runs/_archive/` (empty).
3. Create `.claude/patterns/provisioning/INDEX.md` + 9 skeleton pattern files (main-session-only per Sub-Agent Write Boundary).
4. Create `.claude/constraints/provisioning.md` + wire into `task-execute` Step 4a tag map.
5. Extend `/provision-environment` skill: Step 0 pre-step + Step 0.5 prereqs verification + Step 7 mandatory postmortem.
6. Cross-refs into `SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` + root `CLAUDE.md` §17.
7. Add `/audit-provisioning-lessons` slash-command (or a task-execute pattern) that scans `provisioning-runs/*/lessons-learned.md` cross-run.
