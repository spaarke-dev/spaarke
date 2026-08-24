# TASK-INDEX — spaarke-auth-v4-dataverse-MI

> **Generated**: 2026-08-19 by `/project-pipeline` · **29 tasks / 8 phases** — **26 active, 3 deferred** (Phase 4 Power BI, owner decision 2026-08-19)
> **Status legend**: 🔲 not-started · 🔄 in-progress/retry · ✅ complete · ⛔ blocked · ⏭️ deferred
> **Execution is owner-gated — NOT started.** Every task runs via `task-execute`.

## Registry

| # | Task | Phase | Rigor | Tier/Effort | Deps | Group | ∥-safe | FR | Status |
|---|---|---|---|---|---|---|---|---|---|
| 001 | Create the dev deployment slot + assign UAMI | 0 Spike | **FULL**¹ | sonnet/med | — | — | ❌ | — | ✅ |
| 002 | Spike: prove OBO under a MI client assertion | 0 Spike | FULL | **opus/xhigh** | 001 | — | ❌ | — | ✅ |
| 003 | Record the credential decision with evidence | 0 Spike | STANDARD | sonnet/med | 002 | — | ❌ `.claude/` | — | ✅ |
| 010 | Fix the MI-flag gating defect | 1 Prereq | FULL | sonnet/high | 003 | **A** | ❌ | FR-A1 | ✅ |
| 011 | Fix DI lifetimes + record the ADR-009 decision | 1 Prereq | FULL | sonnet/high | 003 | **A** | ❌ | FR-A2 | ✅ |
| 020 | `IClientAssertionProvider` seam (**ceiling NOT raised — see ¹⁴**) | 2 Provider | FULL | **opus/xhigh** | 011 | **B** | ❌ | FR-B1 | ✅ |
| 021 | Ordered credential selection (the rollback mechanism) | 2 Provider | FULL | sonnet/xhigh | 020 | **C** | ✅ | FR-B2 | ✅ |
| 022 | Migrate the 6 BFF-identity confidential clients | 2 Provider | FULL | **opus/xhigh** | 021 | **D** | ❌ | FR-B3 | ✅ |
| 023 | UAMI ↔ app-reg conflation guard + test | 2 Provider | FULL | sonnet/high | 020 | **C** | ✅ | FR-B4 | ✅ |
| 024 | Relax the three config validators | 2 Provider | FULL | sonnet/high | 020 | **C** | ✅ | FR-B5 | ✅ |
| 030 | `Register-EntraAppRegistrations.ps1` FIC extension **⏩ PULLED FORWARD** | 3 Rollout | FULL | sonnet/high | 020 | **E** | ✅ | FR-C4 | ✅ |
| 031 | Deploy to slot + §6.1 OBO checklist (**MI-FIC OBO PROVEN live 2026-08-24; rollback verified — 032 unblocked. ALL checklist surfaces verified EXCEPT Office add-ins, which need a human in the Office host. Open owner decision: §6.1 task-030 carry-forward**) | 3 Rollout | FULL | sonnet/high | 022 | — | ❌ | FR-C1 | 🔄 |
| 032 | Promote by swap + retire slot (**DONE 2026-08-24: swapped 14:50:59Z, MI-FIC proven on the default slot at credential level; staging slot DELETED 15:37:45Z. Rollback is now credential-reorder only — proven in 031 §5.6**) | 3 Rollout | FULL | sonnet/high | 031 | — | ❌ | FR-C2 | ✅ |
| 033 | Remove the secret + reconcile 11 scripts / ~25 docs | 3 Rollout | FULL | sonnet/high | 032 | — | ❌ `.claude/` | FR-C3 | 🔲 |
| 040 | ⚠️ Verify Power BI SP **profiles** under a managed identity | 4 PowerBI | FULL | **opus/xhigh** | 020 | — | ✅ | FR-D2 gate | ⏭️ |
| 041 | Power BI tenant setting + rework both services | 4 PowerBI | FULL | sonnet/high | 040 | — | ✅ | FR-D1/D2 | ⏭️ |
| 042 | Remove `PowerBi:ClientSecret` | 4 PowerBI | FULL | sonnet/high | 041 | — | ✅ | FR-D3 | ⏭️ |
| 050 | Content Safety → MI (path already exists) | 5 Group2 | FULL | sonnet/high | — | **F** | ✅ | FR-E1 |✅ |
| 051 | Service Bus → namespace + MI (**rotation DONE**, code DONE; cutover booked to 031/033) | 5 Group2 | FULL | sonnet/high | — | **F** | ✅ | FR-E2 | 🔄 |
| 052 | Azure OpenAI E-2 — custom-subdomain check first | 5 Group2 | FULL | sonnet/high | — | **F** | ✅ | FR-E3 |✅ |
| 053 | AI Search → Entra/MI (**×7, not ×2**) | 5 Group2 | FULL | sonnet/high | — | **F** | ✅ | FR-E4 | 🔄 |
| 054 | Document Intelligence ×3 → Entra/MI (1 done by 053, 2 retained w/ reason) | 5 Group2 | FULL | sonnet/high | — | **F** | ✅ | FR-E5 |✅ |
| 055 | `Analysis:PromptFlowKey` disposition | 5 Group2 | STANDARD | sonnet/med | — | **F** | ✅ | FR-E6 |✅ |
| 056 | Bing key → KV-by-name + **stop fabricating web results** | 5 Group2 | FULL | sonnet/high | — | **F** | ✅ | FR-E7 |✅ |
| 060 | ArchTest credential ban + E-1/E-3 allowlist | 6 Forcing | FULL | sonnet/high | 022 | **G** | ✅ | FR-F1 | ✅ |
| 061 | Credential census (**source analysis, not DI**) | 6 Forcing | FULL | sonnet/high | 022 | **G** | ✅ | FR-F2 | ✅ |
| 062 | Startup assertion (non-Development) | 6 Forcing | FULL | sonnet/high | 022 | **G** | ✅ | FR-F3 | ✅ |
| 063 | Pre-declare ArchTests MAINTAIN-class for `/test-diet` | 6 Forcing | STANDARD | sonnet/med | 060,061 | **G** | ❌ `.claude/` | FR-F0 | ✅ |
| 090 | Project wrap-up | 9 Wrap | STANDARD | sonnet/high | all active | — | ❌ | — | 🔲 |

¹ **001 ran at FULL, not the authored STANDARD** — `task-execute` Step 0.5 overrides *up* when tags include
`auth`. Completed 2026-08-20. Slot `staging` is live and healthy, **not swapped**. It surfaced three findings that
changed downstream tasks — see [`notes/decisions/001-slot-creation.md`](../notes/decisions/001-slot-creation.md):

| Finding | Effect on later tasks |
|---|---|
| **A** `keyVaultReferenceIdentity` is a *site* property, not copied by `--configuration-source`; slot aborted with exit 134 (SIGABRT) until set. Also absent from Bicep IaC — but the IaC turns out not to describe the live dev env at all, so this is a **reproducibility** gap, not a production risk (severity corrected high→medium same day) | Pre-check added to **031**; "site properties do not swap" constraint added to **032**; filed as **ISS-001** ([#805](https://github.com/spaarke-dev/spaarke/issues/805)) |
| **B** Escalation trigger fired — 16 plaintext secret app settings mirrored into the slot. Retained deliberately (alternatives are worse); reversible | **033** must purge **both** slots, and treat removal as app-setting deletion, not only Key Vault deletion |
| **C** `Deploy-BffApi.ps1 -UseSlotDeploy` always swaps — no `-SkipSwap` | **031** and **032** must not use it; explicit `az webapp deploy` recipe recorded |

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Goal-eligible | Notes |
|---|---|---|---|---|
| — | 001 → 002 → 003 | none | ❌ | Serial. 002/003 are the credential decision gate |
| **A** | 010 → 011 | 003 | ❌ | **CORRECTED 2026-08-20: NOT parallel — the "different files" claim was wrong.** Both modify `Spaarke.Dataverse/DataverseAccessDataSource.cs`. Run **sequentially**: 011's CCA caching builds on the branch structure 010 corrects |
| **B** | 020 | 011 | ❌ | Serial — everything downstream depends on the seam |
| **C** | 021, 023, 024 | 020 | ✅ | Independent surfaces of the provider |
| **D** | 022 | 021 | ❌ | The migration itself; highest blast radius |
| **E** | **030** | 020 | ✅ | FIC automation. **Run immediately after 020 — do not let phase order carry it** (provisioning Wave G-3 is soft-blocked). 040 removed: Power BI deferred |
| **F** | 050–056 (7) | none | ✅ | Group 2 — independent of everything. **Split 6+1: max concurrency is 6** |
| **G** | 060, 061, 062 (063 after) | 022 | ✅ | Forcing functions need the end-state shape to assert against |
| — | 031 → 032 → 033 | 022 | ❌ | Serial by construction: deploy → verify → swap → soak → remove |
| — | 090 | all | ❌ | Wrap-up |

**Goal-eligibility**: groups A, C, E, F, G have machine-verifiable end states and low ambiguity. Groups B, D and
the 031→033 chain are **not** goal-eligible — they are irreversible or fail-closed and require operator judgment.

⚠️ **`parallel-safe: false` for `.claude/` tasks** (003, 033, 063) — main-session only per the sub-agent write
boundary. A dispatched agent will fail with "Edit denied"; that is the boundary working, not a bug.

## Critical Path

```
001 → 002 → 003 → 010 → 011 → 020 → 021 → 022 → 031 → 032 → 033 → 090
```

12 tasks. Phases 5, 6 and task 030 branch off and rejoin at 090. **Phase 4 (Power BI) is deferred out of this project** — see "Deferred scope" below.

## High-Risk Items

| Task | Risk | Guard |
|---|---|---|
| ~~**002**~~ | ~~The whole project's premise. Failure = pivot to certificate~~ | ✅ **RETIRED 2026-08-20 — spike PASSED.** OBO proven under a MI-issued client assertion (Graph/SPE, Dataverse `user_impersonation`, long-running). No pivot needed. Evidence: [`notes/decisions/002-spike-results.md`](../notes/decisions/002-spike-results.md) |
| ~~**020**~~ | ~~Trips `ADR010_DITests.cs:164` (ceiling 153 → 154) — reddens CI on the first PR~~ | ✅ **RETIRED 2026-08-21 — the premise was FALSE.** ArchTests pass at 153; real count is **151**; `IClientAssertionProvider` is **absent** from the counted list because the test scans `typeof(Program).Assembly` (BFF only) and the interface lives in `Spaarke.Dataverse`. **A cross-assembly 1:1 seam is invisible to this ratchet.** Ceiling left untouched — raising it would have widened slack 2→3, letting a future *in-assembly* interface land unreviewed. Both quality gates reproduced it independently. Blind spot → [#809](https://github.com/spaarke-dev/spaarke/issues/809); sibling `PackageReference` gap → [#810](https://github.com/spaarke-dev/spaarke/issues/810) |
| **022** | ⏳ **Task 011's ADR-028 A4 exception EXPIRES HERE.** Three per-class static CCA caches (`DataverseUserClient`, `DataverseAccessDataSource`, `AgentTokenService`) mean one process can hold three confidential clients for the same `(tenant|client)` — the per-call-site duplication A4 line 207 forbids. Accepted at 011 only because task 020 is about to build the shared provider | Booked as a **constraint + acceptance criterion on both 020 and 022** (not prose in a notes file — that was `adr-check` finding **W2** at task 011). 022 must leave **zero** per-class CCA statics; if it doesn't, escalate rather than defer |
| **022** | Migrates OBO. **Fails closed** — breakage locks out every user, totally | Secret retained as ordered fallback; slot-only; no swap in this task |
| **032** | The flip. `#3b` attempt 1 took dev down | Slot swap only; no in-session flips; rollback = swap back |
| **033** | Irreversible. 6 secret paths + 11 scripts + ~25 docs | Only after soak; lowercase KV alias breaks the Office add-in if missed |
| ~~**040**~~ | *Deferred with Phase 4* — the unverified SP-profiles question travels with the deferral and must be answered before 041/042 are ever attempted | n/a while deferred |
| **030** | Cross-project — provisioning task 130 (Wave G-3) is soft-blocked | **⏩ Pulled forward (owner, 2026-08-19): run as soon as 020 lands**, ahead of 021/022. Verify by performing a real token exchange |

## Cross-Project Dependencies

| Direction | Project | Item |
|---|---|---|
| **We owe them** | `customer-provisioning-orchestration-r1` | **Task 030** before their Wave G-3, else their task 130 builds a duplicate |
| **They owe us** | `customer-provisioning-orchestration-r1` | Confirm Model 2's FIC issuer is not cross-tenant (`PROVISIONING-CHANGE-REQUEST.md` §9.2) |
| ~~Interlock~~ | `dataverse-access-unification-r1` | ⛔ **CLEARED 2026-08-20 — project INACTIVE / not scheduled** (owner: investigation found it not valuable). No sequencing, no shared-file contention. `DataverseWebApiService` + `DataverseWebApiClient` are **not** being deleted, so task 010's gating fix on the latter is permanent, and `DataverseServiceClientImpl.cs` needs no cross-project sequencing in 010/011/022 |
| **Watch** | Open PR #293 | `Azure.Identity` 1.17.1→1.21.0 affects `ClientAssertionCredential` |

## Deferred scope — Phase 4 (Power BI), owner decision 2026-08-19

> *"we can ignore Power BI if it is not readily available and defined — we are not yet using Power BI
> (it will be in the future but we can address the MI at that time)."*

| Task | FR | State |
|---|---|---|
| 040 | FR-D2 gate | ⏭️ Deferred — the SP-**profiles**-under-a-managed-identity question stays open and travels with it |
| 041 | FR-D1 / FR-D2 | ⏭️ Deferred |
| 042 | FR-D3 | ⏭️ Deferred — **`PowerBi:ClientSecret` therefore stays in place** |

**Why this is safe to defer.** `PowerBi:ClientSecret` is a *genuinely separate* credential from
`BFF-API-ClientSecret` (`PowerBiOptions.cs:44-45` vs the five `BFF-API-ClientSecret` config keys). Nothing in the
OBO migration reads it, so deferring Workstream D does not weaken FR-C3 (task 033) and does not touch the
fail-closed path. Task 033 already carries a negative criterion asserting the Power BI secret is untouched.

**What the deferral obligates us to do anyway** (so it is not silent debt):

- **Task 060** — Power BI is an explicit ArchTest **allowlist entry carrying this reason**, not an unexplained
  exemption. Un-deferring is then a one-line allowlist removal.
- **Task 061** — the census keeps both Power BI sites as **secret-backed entries**, so the count tells the truth
  about what is still secret-bearing rather than reporting a cleaner estate than exists.
- **Task 090** — graduation criterion 10 is marked **waived with this reason**, not silently dropped.

**Re-open trigger**: Spaarke actually adopts Power BI. Start at 040 — the profiles question is still unanswered
and still gates 041/042.

## POML Generation Status — ✅ COMPLETE

All **29 POMLs written** and validated 2026-08-19:

```
Scanned 29 POML(s): 23 clean, 0 error(s), 6 warning(s)
PASS - all task POMLs carry the required canonical field set.
```

### The 6 warnings are linter over-fire, deliberately not silenced

`Validate-TaskPoml.ps1` warns when a task has any `relevant-files role="new"` but no `<justification>`. It fires
on **010, 021, 023, 060, 061, 090** — every one of which declares a new **test file** or **decision document**,
not new production surface.

CLAUDE.md §11's three-question gate targets *new services / abstractions / interfaces / endpoints / DI
registrations / packages / Dataverse columns*. A new seam test is mandated by ADR-038 (every new auth path gets
`tests/integration/auth/**` or `seam/**` coverage), and a decision record is a project artifact. Neither is
architectural surface.

**Task 020 — the only task creating genuinely new production surface — does carry a full `<justification>`**
and passes clean.

Adding boilerplate justification blocks to the other six to silence the linter would be exactly the hollow
rationale `project-pipeline` Step 1.7 warns against, so they are left flagged and explained here instead.
Consider tightening the linter's heuristic to exclude `tests/**` and `notes/**` paths — that is a
`task-create` improvement, not a change to this project.

¹⁴ **Task 020 did NOT raise the ADR-010 ceiling**, though its POML instructed it to — see the High-Risk row above. It also found that the ordered credential selection task 021 builds (**MI-FIC → certificate → secret**) cannot live behind `IClientAssertionProvider`, because only the first of the three *is* an assertion. Consequence: the shared confidential-client cache belongs to a **client-level seam task 021 must author**, not to the assertion contract and not to task 022. Tasks 021 and 022 were amended accordingly; 022's original criterion required a `grep` result that cannot exist. Full reasoning: [`notes/decisions/020-assertion-seam.md`](../notes/decisions/020-assertion-seam.md).
