# Current Task State — `spaarkeai-compose-r8`

> **Last Updated**: 2026-08-25 (by `context-handoff`) · **Pushed through**: `f06d47e7d`
> **Branch**: `work/spaarkeai-compose-r8` · **Recovery**: read "Quick Recovery" first.
> Everything below is recoverable from files alone.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **none in progress** — a 5-task wave completed and every owner question is answered |
| **Status** | Tree CLEAN · in sync with origin · full solution builds · BFF **11,295 passed / 0 failed** · client **99 suites / 1,212** |
| **Progress** | **33 of 53 complete**, 13 open, 1 blocked |
| **Next Action** | Dispatch **052** (`tasks/052-retire-text-search-path.poml`, FULL · opus @ xhigh). Read its POML first — its scope was NARROWED on 2026-08-24: it **demotes** text matching, it does not delete the capability. Then **061–063** in parallel (the only `parallel-safe: ✅` family). |

### Files modified this session
All committed and pushed. Nothing uncommitted, nothing at risk.

### Critical context in one paragraph
A five-task wave landed via parallel sub-agents (055 · 049 · 057 · 056 · 060), each independently
re-verified in the main session rather than accepted on report. The residual-loss list is **SIGNED OFF**
(task 045 closed) — but note the owner *declined* the field and object rows and they were **fixed**, not
accepted. Three new tasks came out of the owner Q&A: **058** (conditional merge fields — matters because
templates are coming), **059** (a spoofable tenant header, security), **047b** (a silent-loss hole that
undermines the guarantee the signed list rests on).

---

## Owner decisions — 2026-08-25 (all five answered; do not re-ask)

| Q | Decision |
|---|---|
| **Q1** Which bicep stack? | **The question was wrong.** Dev is not stack-deployed at all. See "Track B is blocked" below. |
| **Q2** Sign off the residual list? | **YES — signed 2026-08-25.** Task 045 CLOSED. |
| **Q3** Conditional merge fields? | **Fix it** → task **058** created. |
| **Q4** `X-Tenant-Id` fallback? | **Separate task, fix in R8** → task **059** created. |
| **Q5** Silent-loss hole? | **Fix in R8** → task **047b** created. |
| (earlier) Container + ADR-015 records | **Resolved.** ADR-015 governed-stores row applied (§6.5 Path B); dedicated `session-files` container is the target, default stays `ai-chunks` until bicep declares it. |

### ⚠️ Track B is blocked in dev — and NOT for the reason the task assumed

Measured against the live dev subscription 2026-08-25:

- **Dev is not deployed from any bicep stack.** `az deployment group list -g rg-spaarke-dev` shows no stack
  deployment; the BFF's dependencies span four resource groups. Hand-assembled.
- **No storage account exists in `rg-spaarke-dev`.**
- **`spaarke-bff-dev` has NO system-assigned identity** (`type: UserAssigned`, `systemAssigned: null`), so
  `model2-full.bicep`'s role assignment — which grants the system-assigned principal — would target an
  identity that does not exist.
- Its UAMI **`mi-bff-api-dev`** (`5967251e-171c-46fe-a6c2-ef843c90309d`) holds OpenAI, Key Vault, Service
  Bus, Search and ACS roles — **no storage role of any kind**.

**Four operator steps** (all required, recorded in `notes/track-b-placement-justification.md`): pick or
provision a storage account → create the container → grant **`mi-bff-api-dev`** *Storage Blob Data
Contributor* → set `SessionFileStore:BlobEndpoint`. **Do NOT do the last one until 062 + 063 merge** —
ADR-015 requires retention and erasure for a persisted store, and the empty endpoint is the only thing
holding that line.

---

## What the wave delivered

| Task | Substance |
|---|---|
| **055** | Whole-document anchored placement. `comments[]` (the `flag-risks` intent's ENTIRE output) was 100% prose-anchored; now populates `AnchoredAnnotationAnchor.paraId` — a **6th dark-machinery instance** (shipped R3 FR-11 as PRIMARY, live consumer, no writer). Converged the RESOLUTION, kept the two SINKS separate. |
| **049** | Word fields carried, SERVER half. Gate is STRUCTURAL, not a keyword allow-list. Corrected a **stale renderer comment** ("the model does not carry bookmarks" — untrue since 041), which is what allowed `REF`/`PAGEREF` to be carried LIVE. |
| **057** | Fields, CLIENT half — created because 049 was a **producer with no consumer**. A field is the first segment present in the run stream and ABSENT from the coordinate space. |
| **056** | Objects carried. Settled relationship survival **empirically** (opened the saved package, resolved every `r:*`) rather than trusting the "orphaned … inert weight" remark — **2nd stale-comment correction**. Added a gate that every relationship-namespace attribute must RESOLVE against the carrier. |
| **060** | Durable tenant-partitioned session-file byte store. Ships **DISABLED**. |

---

## Remaining queue (13 open, 1 blocked)

| # | Task | Gate |
|---|---|---|
| **052** | Demote the text-search path — **scope NARROWED 2026-08-24**: demote, do not delete. `match_mode: 'all'` must be decided explicitly. | READY — **dispatch next** |
| **053** | Bounded confirmable fallback | 052 |
| **061 · 062 · 063** | Track B lazy re-index · retention/TTL · erasure | 060 ✅ — **the only `parallel-safe: ✅` family** |
| **047b** | Never-silent hole (unpaired block reports no loss) | 056 ✅ |
| **058** | Nested / conditional merge fields | 049 ✅ 057 ✅ |
| **059** | SECURITY — `X-Tenant-Id` spoofable fallback; human sign-off required | 060 ✅ |
| **070–073** | Track D decomposition | ready; same files as Track A/C — sequence carefully |
| **074** ⛔ | Retire `ComposeShadowPatchEngine` | gate-confirm before deleting 3,000 lines |
| **090** | Wrap-up (incl. `/test-diet`) | all |

---

## How to run the next wave (this worked — reuse it)

**Parallelism.** The project's blanket `parallel-safe: false` on the Compose spine is too coarse. Judge
**file AND toolchain disjointness per pair**:
- 055 (client/jest) ∥ 049 (server/dotnet) — clean.
- 060 — independent files but also dotnet ⇒ **isolated worktree**, cherry-picked back cleanly.
- **052 ∥ 047b/058 would collide** (all `Services/Compose`). **061–063 are the safe parallel family.**

**Main session reserves** `TASK-INDEX.md`, `current-task.md`, `.claude/**` and ALL git operations; agents
own code only. Prevented every collision. Tell agents explicitly — they cannot write `.claude/` (root §3)
and should report proposed CHANGELOG text instead.

**Never build/test while an agent is mid-edit in the same tree** — you will read half-written work as a
regression (happened once: `TryCarryEmbeddedObjects`).

**Verify every agent report.** What that caught this wave:
- a suite failure the agent did not have (SSE timing flake under concurrent-agent CPU load — passes in
  isolation in 178 ms);
- a publish-size number 1.3 MB off — **the raw directory sum is ~137 MB; measure the ZIPPED size**;
- a doc claim true of only the server path (`notes/049 §4` on bold/italic/underline);
- 057's display fix silently not reaching `object` atoms — caught only because 056 was told to *confirm*
  rather than assume it generalised.

**Escalation precedent (057).** A trigger fired on its literal predicate but not on its reasoning. The
scope extension was accepted because it was minimal, used an in-file precedent, and was **surfaced with an
offer to revert**. Judge the reasoning behind a trigger, not just its wording.

---

## Standing constraints (unchanged)

- **Deploy prerequisite**: `Deploy-AnalysisAction.ps1` MUST run before ANY of Track C is observable —
  including task 051's work. Dev stores the WHOLE mirror file in `sprk_inputschema` for
  `compose-draft-alternative` / `compose-compare-to-playbook`, so `GetDeclaredProperties` returns null.
  Deploy BFF + `sprk_spaarkeai` together (NFR-05). **Nothing from Phase 3 onward is deployed.**
- Publish ceiling 60 MB **compressed**; current **43.74 MB** incl. PDBs. No new NuGet on Track A.
- **NEVER delete `docxBridge.ts`.**
- Pre-existing CI red, NOT ours: **Compose Client Gate** (timeout flake since `7069717bd`) and **Trivy**
  (HIGH CVE on master) — verified identical on `dfa713cbf`, before this session. PR **#806** open.
- **C-4 still unmeasured against a real model response.** Anchors add 3.50% at realistic payload size
  (40.4 KB, under the 128 KB cap); the over-cap case at the schema's declared maxima is pre-existing.
- **Nothing in Track B has run against real Azure** — no storage account, no MI, no RBAC.
- **No bicep file has been changed by this project at any point.**
