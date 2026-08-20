# TASK-INDEX — spaarke-auth-v4-dataverse-MI

> **Generated**: 2026-08-19 by `/project-pipeline` · **29 tasks / 8 phases**
> **Status legend**: 🔲 not-started · 🔄 in-progress/retry · ✅ complete · ⛔ blocked · ⏭️ deferred
> **Execution is owner-gated — NOT started.** Every task runs via `task-execute`.

## Registry

| # | Task | Phase | Rigor | Tier/Effort | Deps | Group | ∥-safe | FR | Status |
|---|---|---|---|---|---|---|---|---|---|
| 001 | Create the dev deployment slot + assign UAMI | 0 Spike | STANDARD | sonnet/med | — | — | ❌ | — | 🔲 |
| 002 | Spike: prove OBO under a MI client assertion | 0 Spike | FULL | **opus/xhigh** | 001 | — | ❌ | — | 🔲 |
| 003 | Record the credential decision with evidence | 0 Spike | STANDARD | sonnet/med | 002 | — | ❌ `.claude/` | — | 🔲 |
| 010 | Fix the MI-flag gating defect | 1 Prereq | FULL | sonnet/high | 003 | **A** | ✅ | FR-A1 | 🔲 |
| 011 | Fix DI lifetimes + record the ADR-009 decision | 1 Prereq | FULL | sonnet/high | 003 | **A** | ✅ | FR-A2 | 🔲 |
| 020 | `IClientAssertionProvider` seam + raise ADR-010 ceiling | 2 Provider | FULL | **opus/xhigh** | 011 | **B** | ❌ | FR-B1 | 🔲 |
| 021 | Ordered credential selection (the rollback mechanism) | 2 Provider | FULL | sonnet/xhigh | 020 | **C** | ✅ | FR-B2 | 🔲 |
| 022 | Migrate the 6 BFF-identity confidential clients | 2 Provider | FULL | **opus/xhigh** | 021 | **D** | ❌ | FR-B3 | 🔲 |
| 023 | UAMI ↔ app-reg conflation guard + test | 2 Provider | FULL | sonnet/high | 020 | **C** | ✅ | FR-B4 | 🔲 |
| 024 | Relax the three config validators | 2 Provider | FULL | sonnet/high | 020 | **C** | ✅ | FR-B5 | 🔲 |
| 030 | `Register-EntraAppRegistrations.ps1` FIC extension | 3 Rollout | FULL | sonnet/high | 020 | **E** | ✅ | FR-C4 | 🔲 |
| 031 | Deploy to slot + full §6.1 OBO checklist | 3 Rollout | FULL | sonnet/high | 022 | — | ❌ | FR-C1 | 🔲 |
| 032 | Slot swap + soak | 3 Rollout | FULL | sonnet/high | 031 | — | ❌ | FR-C2 | 🔲 |
| 033 | Remove the secret + reconcile 11 scripts / ~25 docs | 3 Rollout | FULL | sonnet/high | 032 | — | ❌ `.claude/` | FR-C3 | 🔲 |
| 040 | ⚠️ Verify Power BI SP **profiles** under a managed identity | 4 PowerBI | FULL | **opus/xhigh** | 020 | **E** | ✅ | FR-D2 gate | 🔲 |
| 041 | Power BI tenant setting + rework both services | 4 PowerBI | FULL | sonnet/high | 040 | — | ✅ | FR-D1/D2 | 🔲 |
| 042 | Remove `PowerBi:ClientSecret` | 4 PowerBI | FULL | sonnet/high | 041 | — | ✅ | FR-D3 | 🔲 |
| 050 | Content Safety → MI (path already exists) | 5 Group2 | FULL | sonnet/high | — | **F** | ✅ | FR-E1 | 🔲 |
| 051 | Service Bus → namespace + MI; rotate the leaked SAS | 5 Group2 | FULL | sonnet/high | — | **F** | ✅ | FR-E2 | 🔲 |
| 052 | Azure OpenAI E-2 — custom-subdomain check first | 5 Group2 | FULL | sonnet/high | — | **F** | ✅ | FR-E3 | 🔲 |
| 053 | AI Search ×2 → Entra/MI | 5 Group2 | FULL | sonnet/high | — | **F** | ✅ | FR-E4 | 🔲 |
| 054 | Document Intelligence ×3 → Entra/MI | 5 Group2 | FULL | sonnet/high | — | **F** | ✅ | FR-E5 | 🔲 |
| 055 | `Analysis:PromptFlowKey` disposition | 5 Group2 | STANDARD | sonnet/med | — | **F** | ✅ | FR-E6 | 🔲 |
| 056 | Bing key → KV-by-name (Group 1 hygiene) | 5 Group2 | FULL | sonnet/high | — | **F** | ✅ | FR-E7 | 🔲 |
| 060 | ArchTest credential ban + E-1/E-3 allowlist | 6 Forcing | FULL | sonnet/high | 022 | **G** | ✅ | FR-F1 | 🔲 |
| 061 | Credential census (**source analysis, not DI**) | 6 Forcing | FULL | sonnet/high | 022 | **G** | ✅ | FR-F2 | 🔲 |
| 062 | Startup assertion (non-Development) | 6 Forcing | FULL | sonnet/high | 022 | **G** | ✅ | FR-F3 | 🔲 |
| 063 | Pre-declare ArchTests MAINTAIN-class for `/test-diet` | 6 Forcing | STANDARD | sonnet/med | 060,061 | **G** | ❌ `.claude/` | FR-F0 | 🔲 |
| 090 | Project wrap-up | 9 Wrap | STANDARD | sonnet/high | all | — | ❌ | — | 🔲 |

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Goal-eligible | Notes |
|---|---|---|---|---|
| — | 001 → 002 → 003 | none | ❌ | Serial. 002/003 are the credential decision gate |
| **A** | 010, 011 | 003 | ✅ | Both prerequisite defects; different files |
| **B** | 020 | 011 | ❌ | Serial — everything downstream depends on the seam |
| **C** | 021, 023, 024 | 020 | ✅ | Independent surfaces of the provider |
| **D** | 022 | 021 | ❌ | The migration itself; highest blast radius |
| **E** | 030, 040 | 020 | ✅ | FIC automation + Power BI verification; fully independent |
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

12 tasks. Phases 4, 5, 6 and task 030 branch off and rejoin at 090.

## High-Risk Items

| Task | Risk | Guard |
|---|---|---|
| **002** | The whole project's premise. Failure = pivot to certificate | Escalation trigger; owner decides the pivot |
| **020** | Trips `ADR010_DITests.cs:164` (ceiling 153 → 154) — **reddens CI on the first PR** | Raise the ceiling in the same PR with the FR-14 justification (acceptance criterion) |
| **022** | Migrates OBO. **Fails closed** — breakage locks out every user, totally | Secret retained as ordered fallback; slot-only; no swap in this task |
| **032** | The flip. `#3b` attempt 1 took dev down | Slot swap only; no in-session flips; rollback = swap back |
| **033** | Irreversible. 6 secret paths + 11 scripts + ~25 docs | Only after soak; lowercase KV alias breaks the Office add-in if missed |
| **040** | Gates Phase 4 entirely — SP profiles under a managed identity are unverified | Verify BEFORE 041/042 commit; fallbacks documented in spec |
| **030** | Cross-project — provisioning task 130 (Wave G-3) is soft-blocked | Sequence early; verify by performing a real token exchange |

## Cross-Project Dependencies

| Direction | Project | Item |
|---|---|---|
| **We owe them** | `customer-provisioning-orchestration-r1` | **Task 030** before their Wave G-3, else their task 130 builds a duplicate |
| **They owe us** | `customer-provisioning-orchestration-r1` | Confirm Model 2's FIC issuer is not cross-tenant (`PROVISIONING-CHANGE-REQUEST.md` §9.2) |
| **Interlock** | `dataverse-access-unification-r1` | 4 files; `DataverseServiceClientImpl.cs` needs real sequencing (tasks 010, 011, 022) |
| **Watch** | Open PR #293 | `Azure.Identity` 1.17.1→1.21.0 affects `ClientAssertionCredential` |

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
