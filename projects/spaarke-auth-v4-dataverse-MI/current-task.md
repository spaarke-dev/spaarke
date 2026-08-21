# Current Task State — spaarke-auth-v4-dataverse-MI

> **Last Updated**: 2026-08-20 (by context-handoff, after task 010)
> **Recovery**: Read "Quick Recovery" first. Everything needed to continue is in this file.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Project** | `spaarke-auth-v4-dataverse-MI` — eliminate `BFF-API-ClientSecret`; OBO → MI-FIC |
| **Branch** | `work/spaarke-auth-v4-dataverse-MI` · worktree `c:/code_files/spaarke-wt-spaarke-auth-v4-dataverse-MI` |
| **Task** | **011 — Fix DI lifetimes + record the ADR-009 cache decision** (not started) |
| **Step** | Begin Step 1 of task 011 |
| **Status** | not-started — **unblocked** (deps 003 ✅, 010 ✅) |
| **Next Action** | **1)** `git merge origin/master` (11 behind) · **2)** run `task-execute` on **`tasks/011-fix-di-lifetimes.poml`** — **alone**, not parallel with 010 |
| **Progress** | **4 of 26 active complete** (001, 002, 003, 010) · 3 deferred · 22 remaining |
| **Portfolio** | [#800](https://github.com/spaarke-dev/spaarke/issues/800) · Epic [#426](https://github.com/spaarke-dev/spaarke/issues/426) |

### Repo + live state (verified at handoff)

| Check | Value |
|---|---|
| Working tree | **clean**, 0 uncommitted |
| Pushed through | `d9ab94462` |
| Behind `origin/master` | **11** ← merge before the next code task |
| Slot `/healthz` | **200** (`spaarke-bff-dev-staging`) |
| Production `/healthz` | **200** — never swapped, untouched all session |
| Build / publish | 0 errors · 43.67 MB compressed incl. PDBs (ceiling 60) · CVE clean |
| POML lint | PASS — 29 scanned, 0 errors, 6 pre-existing explained warnings |

### Files modified this session (all committed + pushed)

- `src/server/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs` — decoupled app-only credential from OBO
- `src/server/shared/Spaarke.Dataverse/DataverseWebApiClient.cs` — MI-flag gating
- `tests/integration/seam/Auth/CredentialSelectionSeamTests.cs` — 8 behavioural tests (new)
- `src/server/api/Sprk.Bff.Api/CLAUDE.md` — removed the false "OBO requires a secret" claim
- `.claude/adr/ADR-028-spaarke-auth-architecture.md` — A4 adoption status + E4′ correction
- `.claude/CHANGELOG.md`, `infrastructure/bicep/stacks/dev.bicepparam`
- `projects/spaarke-auth-v4-dataverse-MI/` — spec, design, plan, 4 decision records, defer-issues, POMLs, TASK-INDEX

### Critical Context

**The project's premise is PROVEN.** Task 002 demonstrated on the wire that OBO works under a
Managed-Identity-issued client assertion — Graph/SPE, Dataverse `user_impersonation` (with `upn`
preserved), and long-running OBO. **No pivot to a certificate.** MI-FIC is the recorded decision
(task 003); ADR-028 A4 now carries an adoption-status block.

**OBO fails CLOSED** — a bad change locks out every user instantly. The slot exists so the credential
mechanism is the *only* variable under test. Never swap outside task 032.

---

## ⚠️ Two open items needing owner attention

### 1. CI is red on `origin/master` — NOT caused by this project

`GodClassGuardTests` FR-14 (god-class ratchet) fails on two files this branch never edited:

| File | Lines | Frozen baseline |
|---|---|---|
| `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs` | 2975 | 2864 |
| `src/server/api/Sprk.Bff.Api/Api/ComposeEndpoints.cs` | 2755 | 2651 |

`ComposeEndpoints.cs` is **byte-identical to `origin/master`**, which proves the gate is red
independently of this branch. Both arrived via the master merge (`88784e7d4`) from `#3b` and
`compose-r7`. Per [`god-class-ratchet.md`](../../.claude/patterns/testing/god-class-ratchet.md) the
remedy is **decompose, or re-baseline the waiver with a documented reason — never silently**, and that
belongs to the projects that caused the growth. **Branch CI stays red until resolved**, which will make
task 031's verification noisier.

### 2. PR #801 will conflict with our `design.md`

It corrects the unification-sequencing bullet — **same substance, different wording** from the
correction already on our branch. Flagged on the PR with a proposed resolution (close it and let our
branch carry it, or take their text on rebase — theirs is more detailed). Unresolved.

---

## Full State

### Owner directives applied this session

1. **Task 030 pulled forward** — depends only on 020, and provisioning's Wave G-3 is soft-blocked on it.
   Run it **immediately after 020**, ahead of 021/022. Do not let phase order carry it.
2. **Power BI deferred** — tasks 040/041/042 ⏭️; `PowerBi:ClientSecret` stays.
   [DEF-001 / #804](https://github.com/spaarke-dev/spaarke/issues/804). Made *visible not silent*: task 060
   allowlists Power BI with the reason, 061 keeps both sites in the census as secret-backed, criterion 10
   waived-with-reason at 090.
3. **Autonomous execution authorised** — "as long as safe and accurate". Escalation triggers and the
   fail-closed gates (022, 032, 033) still stop for judgment.
4. **Option A (decouple)** approved for task 010's escalation.

### Completed this session

| Task | Outcome |
|---|---|
| **001** | `staging` slot on `spaarke-bff-dev` — UAMI-assigned, healthy, **not swapped**. `dev.bicepparam` → P1v3 |
| **002** | **OBO PROVEN under MI-FIC.** T0–T4 pass, T5 negative control fails as required |
| **003** | Credential decision recorded; ADR-028 A4 adoption status + E4′ correction |
| **010** | MI-flag gating fixed; **app-only decoupled from OBO** in `DataverseAccessDataSource` |

Decision records: [`notes/decisions/`](notes/decisions/) — `001-slot-creation.md`,
`002-spike-results.md`, `003-credential-decision.md`, `010-credential-gating.md`.

### Key decisions

| Date | Decision | Rationale |
|---|---|---|
| 08-19 | Rollout **dev only** | `spaarke-bff-prod` is Stopped |
| 08-19 | Provider seam = **injected named interface** | `Spaarke.Core` placement is circular; fails `LayerDependencyTests` FR-14 |
| 08-20 | **Power BI deferred** | Not in use; separate credential; no OBO path reads it |
| 08-20 | Slot keeps the **faithful app-setting mirror** incl. secrets | Stripping breaks boot *and* would strip production's secrets at swap. Cost booked onto 033 (purge **both** slots) |
| 08-20 | **MI-FIC adopted** | Proven on the wire; Option B (certificate) not taken |
| 08-20 | **Decouple** app-only from OBO (010) | Copying the prescribed template would set `_cca = null` under MI and disable OBO entirely |
| 08-20 | Structural decoupling guard **deferred to task 060** | Reflection is ADR-038 ban **B8**; the class swallows errors into `AccessRights.None` so selection isn't observable behaviourally. Source analysis is the sanctioned + stronger shape |

### Carried-forward obligations (booked, not assumed)

| Onto task | Obligation |
|---|---|
| **020** | Raise `ADR010_DITests.cs:164` ceiling **153 → 154** in the same PR (`IClientAssertionProvider`) |
| **030** | Pull forward — run right after 020 |
| **031** | Verify slot `keyVaultReferenceIdentity` **before** the OBO checklist; do **not** use `Deploy-BffApi.ps1 -UseSlotDeploy` (it always swaps) |
| **032** | Site properties **do not swap** — verify identity + `keyVaultReferenceIdentity` on **both** slots first |
| **033** | Purge the secret from **BOTH** slots; on dev these are **plaintext app settings**, not KV references |
| **024** | Workstation user-secret `API_CLIENT_SECRET` is **STALE** → `AADSTS7000215` (local-dev OBO story) |
| **060** | Power BI allowlist entry **with the deferral reason**; **plus** the deferred `_cca`-decoupling source guard |
| **061** | Keep both Power BI sites in the census as **secret-backed** |
| **090** | Criterion 10 **waived with reason**, not dropped |

### Reusable recipe (tasks 031 / 041)

The BFF app registration pre-authorizes the Azure CLI, so this yields a **real delegated user token**:

```bash
az account get-access-token --resource "api://1e40baad-e065-4aea-a8d4-4b7ab273458c"
```

### Distinguishable auth failure modes (hard-won this session)

| Condition | Error |
|---|---|
| No MI present (workstation) | `managed_identity_unreachable_network` |
| Wrong identity requested | `managed_identity_request_failed` — "No User Assigned … found" |
| Wrong/stale **secret** | `AADSTS7000215` — opaque; no hint the value is merely wrong |
| Fresh FIC not yet propagated | `AADSTS70021` — **retry before concluding anything** |
| Slot missing `keyVaultReferenceIdentity` | container exit **134 / SIGABRT** — looks like a crash, is a KV-reference failure |

### Cross-project

| Direction | Item |
|---|---|
| **We owe** `customer-provisioning-orchestration-r1` | **Task 030** before their Wave G-3 |
| **They owe us** | Model 2 FIC issuer tenancy — `PROVISIONING-CHANGE-REQUEST.md` §9.2. Still open |
| **Interlock** `dataverse-access-unification-r1` | 4 shared files; **task 011 edits `DataverseAccessDataSource.cs`, which they are active in** — merge master first |
| **Conflict** | **PR #801** vs our `design.md` — see above |

### Open questions

1. **`Analysis:PromptFlowKey`** — still in use? Task 055.
2. **Model 2 FIC issuer tenancy** — with provisioning; may be structurally impossible (same-tenant rule).
3. ~~Power BI SP profiles under MI~~ — deferred with DEF-001; **still unanswered**, travels with task 040.

### Recovery commands

```bash
cd c:/code_files/spaarke-wt-spaarke-auth-v4-dataverse-MI
git merge origin/master                       # 11 behind
cat projects/spaarke-auth-v4-dataverse-MI/tasks/TASK-INDEX.md
cat projects/spaarke-auth-v4-dataverse-MI/notes/decisions/010-credential-gating.md
curl -s -o /dev/null -w "%{http_code}\n" https://spaarke-bff-dev-staging.azurewebsites.net/healthz  # 200
curl -s -o /dev/null -w "%{http_code}\n" https://spaarke-bff-dev.azurewebsites.net/healthz          # 200
# then: task-execute on tasks/011-fix-di-lifetimes.poml   (run ALONE)
```

### Blockers

**None.** Task 011 is startable. Note only that it edits a file another active project is working in —
merge `origin/master` first.
