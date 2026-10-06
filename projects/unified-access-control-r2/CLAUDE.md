# CLAUDE.md — `unified-access-control-r2`

> **Project context for Claude Code.** Loads with every task in this project.
> **Read `spec.md` + the relevant `notes/investigation/` pass before implementing.** Root [`CLAUDE.md`](../../CLAUDE.md) still applies.

---

## 🚨 Task execution protocol (MANDATORY)

**Every task in this project MUST be executed via the `task-execute` skill.** Do NOT read a `.poml` and implement manually — that skips knowledge loading, ADR constraints, checkpointing, and the Step 9.5 quality gates.

All authorization-path tasks are **`<rigor>FULL</rigor>`** → `code-review` + `adr-check` run at Step 9.5, unconditionally.

## What this project is

Spaarke has **two disjoint authorization systems** sharing a data resolver and nothing else. This project unifies them into ONE evaluator returning **`(recordId → rights)`**. The parent→child access cascade that seeded the project falls out of the model.

## The five facts that govern every decision here

1. **Wherever a read goes through the BFF, the BFF filter is the ENTIRE security boundary.** Reads are app-only, so Dataverse row-level security is inert. A bug here is a disclosure — a client seeing another client's matter — not a nuisance. ⚠️ **Ask "does this read go through the BFF?", NOT "is this the MDA?"** — this was previously written as "on the MDA, Dataverse enforces natively and we write no code", which is **false for MDA-hosted PCFs that read via the BFF**. Proven 2026-08-25: a non-admin denied Read on all 442 documents by Dataverse saw a matter's full document list, and opened and downloaded the files, through `SemanticSearchControl` → `POST /api/ai/search` on an **MDA form**. Native MDA forms/grids/views *are* Dataverse-enforced; embedded PCFs are not. See design §4.1 correction + [`notes/task-046-secure-project-owner-role.md`](notes/task-046-secure-project-owner-role.md) §7b.
2. **Being referenced by a lookup grants ZERO access in Dataverse.** Access comes only from ownership, role privilege, team membership, share (POA), or the user hierarchy.
3. **Dataverse has NO per-record deny** (verified against current Microsoft docs, 2026-08-20). Isolation = scope the baseline, grant additively. Never "restrict a row".
4. **A `contact` is not a security principal.** It cannot be a POA share target and cannot be impersonated. That is *why* the contact plane must compute access rather than store it.
5. **`"No Access"` is a VETO, never a level.** Under highest-wins, `max()` would ignore it and an ethical wall would fail silently in exactly the case it exists for.

## The model

| Surface / read path | Enforced by |
|---|---|
| MDA — **native** forms, grids, views | Dataverse natively (role depth × owner/BU/team + sharing) — **no code** |
| MDA — **embedded PCFs reading via the BFF** | ⚠️ The BFF evaluator. **Same exposure as SPA** — not Dataverse |
| SPA / Teams | The BFF evaluator — **the only boundary** |

| Type | Door | Record permission from |
|---|---|---|
| 1 systemuser, licensed | workforce Entra | Dataverse's real answer (impersonated read) ∪ contact grants |
| 2 customer employee, no licence | workforce Entra | `sprk_assigned*` ∪ org — **no business unit** |
| 3 external contact | CIAM | `sprk_assigned*` ∪ org — **no business unit** |

**Evaluator term order** — additive terms union with **highest wins**, then vetoes in this order:

```
max( dataverse-answer, explicit-grant, derived-member, org-expansion, inherited )
  → deny list  (ethical wall + per-child revocation)   → None
  → Restricted (sprk_accesspermission)                  → None for ALL contacts
  → Secure     (sprk_issecure) suppresses derived + org BEFORE the max, for EVERY principal kind
```

**Records**: *core* (project, matter, work assignment, service request) need direct grants. *Child* (invoice, communication, document, event, to-do, analysis) inherit **1 hop** via a denormalized core ancestor. **Matter does NOT inherit from Project** — both are core.

🔴 **"Core" and "externally grantable" are NOT the same list** (owner-confirmed 2026-09-09, task 028). Service request is **core** — nothing else confers access to it — but it is **never grantable to an external contact**. Service requests are submitted by internal workforce users through the SPA; a law firm must never reach one. The grant table `sprk_externalrecordaccess` therefore carries lookups for **project, matter, work assignment, invoice and organization — and deliberately no service request**, and `CallerPrincipal` composes exactly three externally-grantable root sets. Service-request scoping already exists and is a **different mechanism**: the `service-requests` external module scopes by *requester* (`sprk_requestedby == caller`) and returns an empty set for any non-workforce plane, shipped by `spaarke-SPA-external-access-platform-r2` #028 on 2026-08-10. **Do not "complete the fourth root" by adding an accessible service-request set** — it would compose from grants that cannot exist and would encode service requests as externally grantable. See [`notes/task-028-service-request-root.md`](notes/task-028-service-request-root.md).

## Reuse, do not fork

| Need | Use this — it exists |
|---|---|
| Impersonated read | `Spaarke.Dataverse/DataverseImpersonation.cs` + `DataverseWebApiService.RetrieveMultipleImpersonatedAsync:953-989` — **live**; refuses `Guid.Empty` (fail-closed by construction) |
| The gate to extend | `Infrastructure/ExternalAccess/AccessibleRecordSetService.cs` |
| POA with teams + revoke | `Services/Ai/PlaybookSharingService.cs:302-350` — **consolidate** with `IDataverseAccessGrantService`, don't write a third client |
| Child scoping | `ExternalModuleRegistry` `ScopeDimension` + `Api/ExternalAccess/Tier2ScopeFilterInjector.cs` |

## Hard gates — do not merge without these

| Gate | Rule |
|---|---|
| **NFR-04** negative canary | Impersonated low-privilege read MUST return a strict subset AND **strictly fewer** rows than app-only. **Equality means impersonation is inert → fail the build.** Task 034 is a blocking merge gate for 036 |
| **NFR-05** role-depth assertion | No security role may reach the `Secure Projects` BU. A role edit that re-opens secure projects fails the build |
| **NFR-07** | Characterization suite exists BEFORE Phase 1 changes behaviour — the current baseline is near-zero |
| **FR-07 → FR-29** | Delegation ("you may grant if you have Write on the record") ships BEFORE the PCF "+ User" button. Otherwise that button is a one-click privilege escalation on a confidential matter |
| **Integration suites, run in full, before the PR** | `dotnet test` on `tests/integration/Sprk.Bff.Api.IntegrationTests` AND `tests/integration/Spe.Integration.Tests`, not just a build. `Router` does not run them; the legacy Build & Test does, about an hour after the push. Batch 3 (PR #1096) ran the unit and arch suites only, and three integration tests whose fixtures had not followed tasks 138 and 152 surfaced only in CI (fixed in `8531711d6`) |

## Parallel-safety rules

- **`parallel-safe:false`** for `Infrastructure/ExternalAccess/**`, `Api/ExternalAccess/**`, `Spaarke.Core/Auth/**`, `Spaarke.Dataverse/DataverseWebApiService.cs`. Two agents editing an authorization path concurrently produces a silent merge mess.
- The **three ADR-amendment tasks** (030 ADR-003, 031 ADR-028 A2, 040 ADR-034) edit `.claude/**` → **main-session-only**. Sub-agents CANNOT write there (root CLAUDE.md §3); "Edit denied" is the boundary working, not a bug.
- `AccessGrantModal.tsx` is shared by 065/066/067 — those serialize.

## Every BFF-touching task

State the **Placement Justification** in the PR citing [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md), and verify publish size **≤60 MB** (baseline ~44.96 MB incl. PDBs). Run `/conflict-check` before **every** BFF PR — this surface is shared with shipped `SPA-external-access-platform-r1/r2` + `teams-app-r1`, and draft `SPA-r3`.

## ADR tensions — all CLAUDE.md §6.5 **path B**

| ADR | Why | Task |
|---|---|---|
| ADR-003 | "Two seams", "rules only", "no new auth service layers", "per-request cache only" — none describe reality; the rules would force a shape that cannot carry rights or vetoes | 030 |
| ADR-028 A2 | Mandates workforce → ADR-034 membership derivation; we substitute Dataverse's real answer. Token stays workforce — only derivation changes | 031 |
| ADR-034 | The access-conferring allow-list becomes first-class and per-surface, covering org-typed lookups too | 040 |

The 1-hop cap needs **no** exception — the ancestor stamp makes every chain one hop.

## Out of scope

AI-search trimming for contacts (finding A-21 → AI/indexing owner) · field-level visibility · break-glass · organization-hierarchy cascade · GDPR erasure of grant rows · **the BU restructure itself** (UAT/environment work — spec § UAT & Environment Setup)

## Key documents

| Doc | Use |
|---|---|
| [`spec.md`](spec.md) | 32 FRs / 7 NFRs — the contract |
| [`design.md`](design.md) | The model and its reasoning |
| [`notes/design-register.md`](notes/design-register.md) | Every finding, decision, deferral, prerequisite (§A–I) |
| [`notes/investigation/10-finding-confirmations.md`](notes/investigation/10-finding-confirmations.md) | Per-finding evidence + failure scenarios — **read before any Phase 0 task** |
| [`notes/investigation/08-option-b-feasibility.md`](notes/investigation/08-option-b-feasibility.md) | The impersonation mechanism + fail-OPEN risk |
| [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) | Dependencies + parallel groups |

## Standing directives & gotchas

> **Why this section exists (repo procedure change, 2026-10-06).**
> - **What changed:** `current-task.md` now holds CURRENT state only and is rewritten at each checkpoint (`.claude/skills/context-handoff/SKILL.md` "State, not history").
> - **Why:** here it had grown 5 KB → 483 KB, because every checkpoint stacked a new block over the old ones, and task-execute reads it at Step 0 + Step 2 of every task.
> - **What this section is:** the items below were stated in that file as standing or binding and are still in force. They moved here so they survive the rewrite and are read on every recovery.
> - **Where the rest went:** the old file is archived verbatim at `notes/handoff-history/current-task-archive-2026-10-06.md`. Items the conversion could not classify are in `notes/handoff-history/2026-10-06-conversion-review.md`; resolve them when convenient.
> - **Going forward:** add a new standing directive or gotcha HERE (one dated bullet), not in `current-task.md`.
>
> **Also new, repo-wide:** task-execute Step 9.5 "Finding triage and round limits" generalizes this project's owner rounds 56/59.
> - F1–F4 fix-now / K1–K4 known-limit.
> - At most 2 fix rounds, each re-verifying the fix diff only.
> - 1 verifier pass per task (2 for auth/security/tenant-isolation).
> - Escalate any F1 still open instead of starting round 3.
>
> The skill files reach this worktree on the next master merge (`work/procedure-throughput-fixes-r1`); the rules apply now.
>
> Already elsewhere, so not repeated here: the Opus pin, the session-cwd rule, the workflow-agent SendMessage ban and the finding bar (rounds 56/59) are in project memory; full integration suites, publish size and `/conflict-check` are in the sections above. Decisions are recorded as numbered rounds in `notes/session27-owner-decisions-and-research.md`.

**Owner directives**
- Continue autonomously; stop only for a genuine owner decision (root §6/§6.5). Batch owner questions into ONE AskUserQuestion round, "(Recommended)" first (2026-10-01/03).
- The main session decides the complete fix on partial-option escalations and records it as a numbered round in the session27 decisions note (2026-10-03).
- Terminology: say "save the email as documents" (archive route), not "Save to SharePoint" (owner, 2026-10-06).
- No Dataverse test in CI. Live assertions are manual gates (036 canary) and UAT/live-gate checks (2026-09-10).
- A failed revoke must give the user a message, not a bare 500. The status code is for the client; the message is for the person (2026-09-10).
- Do not relocate users to fix BU reach; test users are in the right BU (2026-09-09). Root-BU reach is a dev artifact; production = users plus the BFF app user in the customer's child BU (round 5, 2026-10-02).
- `sprk_related{type}` is the document filing lookup we build on. Legacy direct lookups retire. Readers follow BOTH families via the shipped `DocumentLinkFields` (2026-09-04).
- Don't absorb other surfaces' work (Compose/Tiptap, CI gates) and don't hand work to a mid-execution project. The 27-unbuilt-solutions gap is fixed per surface, never with a CI workflow (2026-09-07).
- 036 runs alone (opus/xhigh), never in a wave (2026-09-21).
- Do NOT mass-rename `tenantId`. D-12/D-13 code remediation belongs to cpo-r1, not this project (2026-09-28).

**Agents and coordination**
- Answer a running workflow agent by appending to `NOTE-FROM-MAIN.md` in its worktree, and never commit that file. Agent-tool (non-workflow) agents MAY be resumed with SendMessage (2026-10-03/04).
- Workflow resume caching is prefix-ordered. For a pooled/DAG script, write a CONTINUATION script that embeds the done results instead of using `resumeFromRunId` (2026-10-03). *(verify: memory `workflow-agent-messaging` says otherwise; see conversion review)*
- Peer sessions (e.g. word-add-in-r1) hold incoming messages until their user approves them. The owner relays; durable hand-offs go through GitHub issues/comments (2026-10-02, reaffirmed 2026-10-06).
- If agents' `current-task.md` copies conflict on merge, keep the orchestrator's: `git checkout --ours` on that path (2026-08-28).
- Agents sharing ONE worktree must not edit TASK-INDEX/current-task, run git, or run solution-wide `dotnet`. The main session does the build, the suite and ONE commit (2026-09-04).
- Re-read a task's `parallel-safe` against its CURRENT scope. Source-disjoint packages can still share a build (`dist/`, barrel) (2026-09-21).

**CI, merge and deploy**
- `Router` is the only required check. `gh pr checks` never lists it: probe by name via the check-runs API and gate on the full rollup with pending = 0. Tier 2 full-unit cancelled at 30 min is advisory (2026-09-17/10-02).
- Don't push while a CI run is in flight (cancel-in-progress kills the verdict). Merge BEFORE `/context-handoff`, whose commit re-triggers the gate (2026-08/09-02).
- Deploy the BFF from a FRESH short-path worktree of master (`Deploy-BffApi.ps1`), with `pac.cmd`/pwsh from a short path (2026-10-02/06).

**Build, test and measurement**
- Never launch a background suite with `&`; use `run_in_background`. Two suites run concurrently give flaky timing failures (2026-10-02).
- Never pipe a command whose exit code you need. Write the EXPECTED test count into the command, and never `--no-build` after an unguarded build (2026-09-19).
- Prove a test filter is non-empty with `--list-tests`: `tests/integration/{auth,seam}/**` are globbed into the UNIT csproj (2026-09-17).
- `Sprk.Bff.Api.Tests` silently vanishes from a root `dotnet test` when it fails to build. For any interface/facade addition, build `Spaarke.sln`, not one project (2026-08/09-02).
- A build that breaks after a clean needs `dotnet restore` + a plain rebuild. No MSBuild property overrides (2026-09-20).
- A perturbation that does not apply or compile is INVALID, not a result. Commit before perturbing, because `git checkout <file>` discards ALL uncommitted edits in that file (2026-09-10/17).
- Probe the platform before trusting a fake. Example: a keyed PATCH with `If-None-Match: *` answers 404 on Dataverse (2026-10-03). Dataverse's privilege cache lags role edits, so re-probe ≥3 times (2026-08).
- Verify a POML's or doc comment's premises against code and live metadata before obeying them. Never trust `<dependency status>` attributes; re-derive them from the dependency's own POML (recurring; 2026-09-07).

**Environment**
- The connected system is DEV and its records are test records (2026-09-17).
- Web API PATCH by id must send `If-Match: *`, or it upserts and a bad id CREATES a row. Grant "today" is the UTC date, which rolls over in the local evening (2026-09-11).
- `mcp__dataverse__create_table` has no publisher/solution parameter. Create schema via the Web API + `MSCRM.SolutionUniqueName`, then read back and assert the prefix (prefixes are immutable) (2026-09-04, AP-13).
- Verify every schema name with a query that SUCCEEDS. `sprk_related*` casing is not uniform: `sprk_relatedmatter`/`relatedproject`/`relatedvendororg` are lowercase, the rest PascalCase, and `@odata.bind` is case-sensitive (2026-09-04).
- Azure.Identity 1.16 needs `AZURE_TOKEN_CREDENTIALS=dev`. Task 034's canary needs `AZURE_TOKEN_CREDENTIALS=AzureCliCredential` + `SPAARKE_TESTS_ALLOW_OUTBOUND=1` (2026-09-09/10-03).
- An `az` CLI Graph token 403s on SPE container permissions; use the BFF's identity. `roleprivilegescollection` cannot `$expand=roleid`; filter per role (2026-09-10).
- Fresh worktree: run root `npm install --ignore-scripts`, else lint-staged's prettier fails and kills `dotnet format`. `src/client/pcf` needs `node_modules` for the ESLint hook. Never `--no-verify` (2026-09-21/10-04).
- The pre-commit hook stashes unstaged changes and reformats files. Never commit where a perturbation/build is in flight, and re-run suites after the hook (2026-09-11/21).
- Never `git stash` and never `git add -A`; stage explicit paths (2026-09-04).
- Remove a `node_modules` junction with `cmd /c rmdir` BEFORE `git worktree remove` (2026-09-21).
- Python needs `PYTHONIOENCODING=utf-8`. Scratchpad PowerShell stays 7-bit ASCII and runs under `pwsh` (2026-09-17/21).
- Never put characters above U+FFFF in a grep pattern (it silently matches 0). Count TASK-INDEX by the ASCII `[open]`/`[done]` tokens (2026-09-07, G-16).
