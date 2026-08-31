# Handoff: `code-quality-and-assurance-r3` → `customer-provisioning-orchestration-r1`

> **From**: code-quality-and-assurance-r3 (COMPLETE 2026-08-14; + follow-ups merged 2026-08-15)
> **To**: customer-provisioning-orchestration-r1 (was **paused pending** r3's deployment-complexity ask)
> **Master at handoff**: `02490abc4` · **Action**: you are **unblocked** — read §1–§6, then resume.

---

## TL;DR

r3 finished the four deployment-complexity items you were waiting on (#1/#2/#3a/#4). **You can resume.**
Two things changed for you: (a) **drop** your Phase-E absorption of #2/#4 — r3 owns those now (§3); (b)
your remaining charter is **apply canonical resource/secret names at provisioning + remediate live-env
drift** (§4) and the **#1 KV-federation remediation** (§4b). The **#3b credential migration is NOT yours**
(§5). When you update from master, your build inherits new gates (§6) — read the checklist first.

## 1. What r3 landed for you (all ✅ on master)

| Ask | r3 task | What it means for you |
|---|---|---|
| **#3a** vestigial Dataverse S2S app-reg | **060** ✅ | The separate Dataverse S2S app registration (scripts/docs/KV refs, **zero code consumers**) is DROPPED. Do not re-provision it. The BFF's Dataverse access uses the single Dataverse Application User (see `docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md` Automation-Auth table, updated by task 060). |
| **#2** uniform config fail-fast | **061** ✅ | Customer-critical BFF options now validate on startup (`ValidateOnStart`) — a fresh environment missing a required setting **fails at boot with the offending keys named**, not at first request. Kill-switch-gated options stay deferred (exemption list: `code-quality-and-assurance-r3/notes/task-061-config-validation-classification.md`). **Provisioning implication**: a mis-provisioned env now fails loudly at startup — factor this into your smoke/health gates. |
| **#4** Graph app-role constants | **062** ✅ | The 14 Graph app-role IDs are now a single source of truth (`Infrastructure/Auth/GraphAppRoles.cs`). Same ownership seam as #063: r3 owns the constants; **you own granting them on the MI/app-reg at provisioning** + verifying parity (a nightly parity check is queued behind the CI-wiring coordination in §7). |
| **#1** KV-federation assessment | **017** ✅ | Assessment ONLY (not remediation). The verified design + phased remediation plan + the FR-29 naming-drift census live in `code-quality-and-assurance-r3/workstreams/config-deployment/design.md`. **You own the remediation** (§4b), sequenced from that design. |

## 2. Naming standard + gate (task 063 ✅)

r3 authored the **standard** (`docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md` §"KV-Secret &
Resource Naming Standard (Conformance-Gated)") + the **gate** (`scripts/naming-conformance-check.ps1`,
self-tested; scans template/config/bicep). The gate is **advisory-until-remediated** — it currently reports
real live drift (that's your backlog). **You own applying the canonical names + remediating live drift.**

## 3. STOP doing this

**Drop your Phase-E absorption of #2 and #4.** r3 owns config-validation (061) and Graph app-role constants
(062) with ArchTest/CI enforcement. Your Phase-E should now reference these as prerequisites, not re-implement
them. (This was the owner decision recorded 2026-08-13.)

## 4. What you now OWN

### 4a. Apply + remediate resource/secret names (the rename map)
Full current→canonical rename map + the live-state pre-check obligations are in
**`code-quality-and-assurance-r3/notes/task-063-naming-standard-r1-handoff.md`**. Highlights:
- Apply canonical, **env-agnostic** names at provisioning (no `SPRK-DEV-*` env token baked into a replicated
  secret name; one canonical casing; `sprk-{env}-kv` vault with the codified `spaarke-spekvcert` dev exception).
- Remediate existing drift during a maintenance window (rename secret + update App Service KV references +
  rotation). **prod/demo are decommissioned → dev is the only live target for now.**
- **BINDING pre-check**: before removing any alias/fallback spelling, pre-check LIVE App Service settings +
  KV + Dataverse-persisted config — a live env may be feeding the alternate name. **Never remove**
  `Dataverse-ClientSecret` / `BFF-API-ClientSecret` (OBO + shared-lib Dataverse depend on them).
- Durable fix: drive seeder + Configure script + tokens doc from ONE canonical secret-catalog manifest
  (017 design §Phase 3 / D5-03).

### 4b. #1 KV-federation remediation
Sequence from `workstreams/config-deployment/design.md` (task 017). It will add an **external-spa +
code-pages** touch — coordinate then. The naming canonicalization (4a) is a prerequisite (canonical names
first, then federation/gate).

## 5. What is NOT yours (boundary)

**#3b — the shared-lib `ClientSecret`→Managed-Identity credential migration** (the BFF's own Dataverse path
is still secret-based) is **on the NG1 / task-011 track (Idea #742)**, NOT r1. It's an identity-attribution
change entangled with the two-Dataverse-stack unification. Analysis seed:
`code-quality-and-assurance-r3/notes/red-item-analyses/RED-4-ng1-dataverse-stack-unification.md`. Do not
attempt the credential migration in r1 provisioning.

## 6. Gates you inherit when you `git merge origin/master` (READ before your next BFF PR)

r3 + its follow-ups installed repo-wide forcing-functions. Your build/CI can break **with zero textual
conflicts**:
- **Analyzers-as-errors** (`TreatWarningsAsErrors=true`) — now stricter (CS8601/CS8604 nullable are errors;
  CS0109/CS1998 removed). Any warning in your BFF code fails the build.
- **God-class ratchet** (`GodClassGuardTests`): no NEW `src/server` file > 2,000 lines; 14 existing large
  files frozen at their LOC (+100 grace). See `.claude/patterns/testing/god-class-ratchet.md`.
- **4 ArchTests**: a new Dataverse downcast, an ADR-013 `IActionResolver`/`IActionRunner` injection, a layer
  violation → red.
- **Config fail-fast** (061): a provisioning/test that boots with an out-of-range config value now fails at
  startup.

**Turnkey update checklist:**
```
1. git merge origin/master                 # resolve INDEX.md by unioning rows; code usually auto-merges
2. dotnet build Spaarke.sln -c Release      # MUST be 0 errors — fix any new warning-as-error
3. dotnet test tests/Spaarke.ArchTests      # MUST be 38/0
4. dotnet test <your suite>; then PR
```

## 7. Coordination

- **CI-workflow wiring** for the naming-conformance gate + Graph-app-role parity check is a **coordinated PR
  with `ci-cd-unit-test-remediation-r1`** (owns existing workflow files) — see
  `code-quality-and-assurance-r3/notes/task-042-063-ci-gate-wiring-deferral.md`. Don't edit the workflow
  files directly; compose with them.
- `/conflict-check` before every BFF PR (13+ active BFF worktrees).

## 8. Reference index

| Topic | Doc |
|---|---|
| Rename map + live-state pre-checks | `code-quality-and-assurance-r3/notes/task-063-naming-standard-r1-handoff.md` |
| KV-federation assessment + remediation phases | `code-quality-and-assurance-r3/workstreams/config-deployment/design.md` |
| Naming standard (canonical) | `docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md` |
| Config-validation exemption list | `code-quality-and-assurance-r3/notes/task-061-config-validation-classification.md` |
| #3b credential migration (NOT yours) | `code-quality-and-assurance-r3/notes/red-item-analyses/RED-4-ng1-dataverse-stack-unification.md` |
| CI-gate wiring coordination | `code-quality-and-assurance-r3/notes/task-042-063-ci-gate-wiring-deferral.md` |
| Gate pattern (god-class ratchet) | `.claude/patterns/testing/god-class-ratchet.md` |
