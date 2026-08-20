# Current Task State — spaarke-auth-v4-dataverse-MI

> **Last Updated**: 2026-08-20 (by task-execute, after task 001)
> **Recovery**: Read "Quick Recovery" first. Everything needed to continue is in this file.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Project** | `spaarke-auth-v4-dataverse-MI` — zero-secret BFF confidential credential (OBO → MI-FIC) |
| **Branch** | `work/spaarke-auth-v4-dataverse-MI` · worktree `c:/code_files/spaarke-wt-spaarke-auth-v4-dataverse-MI` |
| **Task** | **002 — Spike: prove OBO under a MI client assertion** (not started) |
| **Step** | Begin Step 1 of task 002 |
| **Status** | not-started — **unblocked**, dep 001 is ✅ |
| **Next Action** | Run `task-execute` on **`tasks/002-spike-obo-under-mi-fic.poml`** |
| **⚠️ Model** | Task 002 is **`opus`/`xhigh`** — do NOT run it on a lower tier. It is the project's decision gate |
| **Portfolio** | [Project #800](https://github.com/spaarke-dev/spaarke/issues/800) · Epic [#426](https://github.com/spaarke-dev/spaarke/issues/426) · **1 of 26 active** (3 deferred) |

### Repo state

| Check | Value |
|---|---|
| Working tree | clean (all work committed + pushed) |
| Behind `origin/master` | 0 |
| Build | `dotnet publish -c Release` → **0 errors**, 7 pre-existing obsolete-API warnings |
| Publish size | **43.67 MB** compressed incl. PDBs · ceiling 60 MB · baseline 44.96 MB |
| CVE scan | clean |
| POML lint | **PASS** — 29 scanned, 0 errors, 6 pre-existing explained warnings |

### Critical Context

The client secret survived three prior audits because of **one false sentence** in
`.claude/constraints/auth.md:108`. That premise is corrected (ADR-028 **A4** + **E-3**), the dev MI-FIC exists,
and **as of 2026-08-20 the `staging` slot exists, is healthy, and is NOT swapped**.

**OBO fails CLOSED.** Task 002 is the spike that decides whether this project proceeds at all or pivots to a
Key Vault certificate. Treat its escalation trigger as a legitimate outcome, not a failure to push through.

Read [`CLAUDE.md`](CLAUDE.md) and [`notes/decisions/001-slot-creation.md`](notes/decisions/001-slot-creation.md)
before task 002.

---

## Full State

### Owner directives applied 2026-08-19/20

1. **Task 030 pulled forward.** It only depends on 020 and `customer-provisioning-orchestration-r1`'s Wave G-3
   is soft-blocked on it. Run it **immediately after 020**, ahead of 021/022 — do not let phase order carry it.
2. **Power BI deferred out of the project.** Tasks 040/041/042 → ⏭️; `PowerBi:ClientSecret` stays. Recorded as
   **DEF-001** / issue [#804](https://github.com/spaarke-dev/spaarke/issues/804), and made *visible not silent*:
   task 060 allowlists Power BI with the reason, task 061 keeps both sites in the census as secret-backed, and
   success criterion 10 is waived-with-reason at 090.
3. **Autonomous execution authorised** by the owner — "as long as safe and accurate". Escalation triggers and
   the fail-closed gates (022, 032, 033) still stop for judgment.

### Task 001 — COMPLETE (2026-08-20, FULL rigor)

Slot `staging` on `spaarke-bff-dev`: `https://spaarke-bff-dev-staging.azurewebsites.net` · `/healthz` **200
Healthy** · UserAssigned only (`mi-bff-api-dev`, principalId `9fd47efb-…`) · **NOT swapped**, production 200
throughout · `dev.bicepparam` set `B1` → `P1v3` (aspirational — that stack does not describe the live env).

Full record: [`notes/decisions/001-slot-creation.md`](notes/decisions/001-slot-creation.md).

**Three findings that changed downstream tasks:**

| # | Finding | Booked onto |
|---|---|---|
| **A** 🟠 | `keyVaultReferenceIdentity` is a **site** property — `--configuration-source` does not copy it. Slot defaulted to `SystemAssigned` (which this app lacks), every Key Vault reference failed, container aborted **exit 134 / SIGABRT** — the `#3b` signature. **Severity corrected 🔴→🟠 same day**: the Bicep IaC does not describe the live dev env (`sprkspaarkedev1dev-api` vs `spaarke-bff-dev`), so this is a reproducibility gap, not a production risk | Pre-check on **031**; "site properties do not swap" on **032**; **ISS-001** / [#805](https://github.com/spaarke-dev/spaarke/issues/805) |
| **B** 🟠 | **Escalation trigger fired** — `--configuration-source` mirrored **16 plaintext secret app settings** into the slot. Retained deliberately (stripping breaks boot *and* would strip production's secrets at swap; KV-refs would add a second variable to the comparison). Reversible by deleting the slot | **033** purges **both** slots; removal is app-setting deletion, not only Key Vault |
| **C** 🟠 | `Deploy-BffApi.ps1 -UseSlotDeploy` **always swaps** — no `-SkipSwap` | **031** + **032** must use explicit `az webapp deploy` / `slot swap` |

**Incidental, valuable for task 023 (FR-B4)**: conflation confirmed live — `AZURE_CLIENT_ID` = `5967251e-…`
(the **UAMI**) while `API_APP_ID`/`AzureAd__ClientId` = `1e40baad-…` (the **app registration**).
`GraphClientFactory.cs:54` resolves `AZURE_CLIENT_ID ?? API_APP_ID`, so that fallback silently yields the wrong
identity. Task 023 now has a real reproduction to test against.

### Decisions

| Date | Decision | Rationale |
|---|---|---|
| 2026-08-19 | Rollout is **dev only** | `spaarke-bff-prod` is Stopped |
| 2026-08-19 | Provider seam = **injected named interface** | `Spaarke.Core` placement is circular, fails `LayerDependencyTests` FR-14 |
| 2026-08-19 | **FR-C4 (FIC automation) in scope**, pulled forward | Provisioning task 130 soft-blocked on it |
| 2026-08-20 | **Power BI deferred** (DEF-001) | Not yet in use; separate secret; does not gate OBO |
| 2026-08-20 | Slot keeps the **faithful app-setting mirror** incl. secrets | Both alternatives are worse — see Finding B. Cost booked onto 033 |
| 2026-08-20 | `dev.bicepparam` → **P1v3** | *Not* a drift fix — that stack has never been applied and names a different env. Still worth doing: B1 has no slots, so it would silently make a slot rollout impossible if ever used |

### Two live CI gates that will bite

1. **`ADR010_DITests.cs:164`** — 1:1-interface ceiling is **153**. `IClientAssertionProvider` makes 154.
   **Task 020 must raise it in the same PR** with the FR-14 justification.
2. **`LayerDependencyTests.cs:43`** — fails if `Spaarke.Dataverse` gains a `ProjectReference`. It must not.

### Cross-project obligations

| Direction | Item |
|---|---|
| **We owe** `customer-provisioning-orchestration-r1` | **Task 030** before their Wave G-3. **Pulled forward** — run right after 020 |
| **They owe us** | Confirm Model 2's FIC issuer is not cross-tenant — `notes/PROVISIONING-CHANGE-REQUEST.md` §9.2 |
| **Interlock** `dataverse-access-unification-r1` | 4 shared files; `DataverseServiceClientImpl.cs` needs real sequencing (tasks 010, 011, 022) |

### Open questions carried into execution

1. **`Analysis:PromptFlowKey`** — still in use? Task 055.
2. **Model 2 FIC issuer tenancy** — with provisioning.
3. ~~Power BI SP profiles under a managed identity~~ — **deferred with DEF-001**; still unanswered, travels with task 040.

### Recovery commands

```bash
cd c:/code_files/spaarke-wt-spaarke-auth-v4-dataverse-MI
cat projects/spaarke-auth-v4-dataverse-MI/tasks/TASK-INDEX.md
cat projects/spaarke-auth-v4-dataverse-MI/notes/decisions/001-slot-creation.md
# verify the slot is still healthy and still not swapped:
curl -s -o /dev/null -w "%{http_code}\n" https://spaarke-bff-dev-staging.azurewebsites.net/healthz   # expect 200
curl -s -o /dev/null -w "%{http_code}\n" https://spaarke-bff-dev.azurewebsites.net/healthz           # expect 200
# then: task-execute on tasks/002-spike-obo-under-mi-fic.poml   (opus/xhigh)
```

### Blockers

**None.** Task 002 is startable and its prerequisite (task 001) is complete and verified.
