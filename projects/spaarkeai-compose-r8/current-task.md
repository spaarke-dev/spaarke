# Current Task State — `spaarkeai-compose-r8`

> **Last Updated**: 2026-08-25 (by `context-handoff`) · **Committed through**: `738778643`
> **Branch**: `work/spaarkeai-compose-r8` · **Recovery**: read "Quick Recovery" first.
> Everything below is recoverable from files alone.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **none in progress** — task **052** landed (the project's highest-risk deletion) |
| **Status** | Full solution builds · BFF **11,285 passed / 0 failed** · ArchTests **56/56** · integration **96** · eval **40/40** · compose-components **100 suites / 1,234** · SpaarkeAi **121 suites / 1,119** |
| **Progress** | **34 of 54 complete**, 13 open, 1 blocked |
| **Next Action** | **053** (`tasks/053-bounded-confirmable-fallback.poml`, FULL · opus @ xhigh) — 052 left it the exact structural boundary it needs: `resolveLegacyReplayedSpans` in `usePendingRedline.ts`, pinned to `strict`, the sole remaining edit-path caller of `resolveTargetSpans`. **Read 053's step 1 first**: if NO genuinely anchorless source remains, the correct outcome is to NOT build the fallback and report that. Then **061–063** in parallel (the only `parallel-safe: ✅` family). |

### Files modified this session
All committed. Nothing uncommitted, nothing at risk.

### Critical context in one paragraph
Task 052 demoted the text-search placement path. Three findings reshaped it before any code moved: the
**server** validation surface it deletes is dark machinery (`/api/compose/edit-batch/validate` has zero
client callers), the **client** DELETE list did not survive its consumer check (`resolveTargetSpans` has
four consumers; three are annotations/decorations, not placement), and the **real demotion is a catalog
change** — the four compose Actions stop asking the model for `target_text`, which is what leaves the text
leg reachable only by replayed ledger entries. Two of FR-C05's three "deterministic outcomes" turned out to
be live defects rather than polish: an anchored edit replaced the ENTIRE paragraph, and a stale target was
not detected at all (silent overwrite of the user's newer text). Full reasoning:
[`notes/052-text-search-demotion-decisions.md`](notes/052-text-search-demotion-decisions.md).

---

## ⚠️ Publish-size measurement — a recorded number was WRONG (corrected 2026-08-25)

**Current: 43.73 MB compressed incl. PDBs** (215 files, 4 `.pdb`, **raw dir sum 137.41 MB**) —
**−1.23 MB** vs the 44.96 MB net10 baseline; ceiling 60 MB.

A sub-agent reported 45.03 MB and cited the 45.00 MB figure in `notes/track-b-placement-justification.md`
to argue the 43.7x cluster was stale. **Both 45.xx figures were artifacts.** An independent re-measure
produced 43.73 MB twice, with raw sum and file count *byte-identical* to the 45.03 MB run — identical
content cannot compress to two sizes. The note has been corrected in place.

**The method that reproduces — use exactly this:**

```
rm -rf <out>                                     # FIRST. A dirty output dir is the suspected cause.
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o <out>
Compress-Archive -Path '<out>\*' -CompressionLevel Optimal
```

**Always report the raw directory sum (~137 MB) next to the compressed figure.** It is the invariant that
makes an inflated zip visible immediately — it is what caught this.

---

## Owner decisions still in force (do not re-ask)

| Q | Decision |
|---|---|
| **Q1** Which bicep stack? | The question was wrong — dev is not stack-deployed. See "Track B is blocked". |
| **Q2** Sign off the residual list? | **YES — signed 2026-08-25.** Task 045 CLOSED. (Note: the field and object rows were *declined and fixed*, not accepted.) |
| **Q3** Conditional merge fields? | **Fix it** → task **058**. |
| **Q4** `X-Tenant-Id` fallback? | **Separate task, fix in R8** → task **059**. |
| **Q5** Silent-loss hole? | **Fix in R8** → task **047b**. |
| **052** `match_mode: 'all'` | **Retired in full.** Asymmetric failure modes; document-wide sweeps route to user-invoked find/replace. Reasoning: `notes/052-…-decisions.md` §2. |

### ⚠️ Track B is blocked in dev — and NOT for the reason the task assumed
Measured against the live dev subscription 2026-08-25: dev is **not deployed from any bicep stack**; there
is **no storage account in `rg-spaarke-dev`**; `spaarke-bff-dev` has **NO system-assigned identity** (so
`model2-full.bicep`'s role assignment targets an identity that does not exist); and its UAMI
**`mi-bff-api-dev`** (`5967251e-171c-46fe-a6c2-ef843c90309d`) holds **no storage role of any kind**.

**Four operator steps** (in `notes/track-b-placement-justification.md`): provision/pick a storage account →
create the container → grant `mi-bff-api-dev` *Storage Blob Data Contributor* → set
`SessionFileStore:BlobEndpoint`. **Do NOT do the last one until 062 + 063 merge** — ADR-015 requires
retention and erasure for a persisted store, and the empty endpoint is the only thing holding that line.

---

## Remaining queue (13 open, 1 blocked)

| # | Task | Gate |
|---|---|---|
| **053** | Bounded confirmable fallback + prove "wording differs slightly" unreachable | 052 ✅ — **dispatch next** |
| **061 · 062 · 063** | Track B lazy re-index · retention/TTL · erasure | 060 ✅ — **the only `parallel-safe: ✅` family** |
| **052b** | Stale-target DETECTION durability (052's answer is ledger-durable; the question is `sessionStorage`) | 052 ✅ |
| **047b** | Never-silent hole (unpaired block reports no loss) | 056 ✅ |
| **058** | Nested / conditional merge fields | 049 ✅ 057 ✅ |
| **059** | SECURITY — `X-Tenant-Id` spoofable fallback; human sign-off required | 060 ✅ |
| **070–073** | Track D decomposition | ready; same files as Track A/C — sequence carefully |
| **074** ⛔ | Retire `ComposeShadowPatchEngine` | gate-confirm before deleting 3,000 lines |
| **090** | Wrap-up (incl. `/test-diet`) | all |

### One-line owner decision waiting
`ComposeEditBatch` + `ComposeEditTransaction` are now orphaned — the text-offset APPLY half of the
mechanism 052 retired, with no producer and no production consumer, so they can never apply anything. They
do **not** violate I-7 (they apply spans, they do not search), so 052 left them rather than delete ~500
lines outside its list. **Retire them (with `/edit-batch/validate` and the models serving only them)
alongside task 074?** Evidence: `notes/052-…-decisions.md` §1.4.

---

## How to run the next wave (this keeps working — reuse it)

**Parallelism.** The blanket `parallel-safe: false` on the Compose spine is too coarse. Judge **file AND
toolchain disjointness per pair**. Task 052 split cleanly into `src/server/**`+`tests/**/*.cs` (dotnet) ∥
`src/client/**`+`infra/dataverse/**` (jest) — but give each agent an explicit "you MUST NOT touch X"
boundary naming the *other* agent's paths, or they collide. **052 ∥ 047b/058 would collide** (all
`Services/Compose`). **061–063 are the safe parallel family.**

**Main session reserves** `TASK-INDEX.md`, `current-task.md`, `.claude/**` and ALL git operations. Tell
agents explicitly they cannot write `.claude/` (root §3) and should report proposed CHANGELOG text instead.

**Never build/test while an agent is mid-edit in the same tree** — you will read half-written work as a
regression. Note the cross-toolchain case: a C# test that reads `infra/dataverse/**` JSON at runtime is
affected by the *client* agent's edits.

**Run `dotnet format` before committing.** Task 052's files had whitespace/EOL violations; CI auto-formats
and pushes, which rejects your next push. Use `dotnet format whitespace --include <your paths>` — a
project-wide `dotnet format` also "fixes" ~22 pre-existing IDE1006 naming violations in unrelated files and
produces a huge diff.

**Beware `grep -i compose` in this worktree** — the path is `spaarke-wt-spaarkeai-compose-r8`, so it
matches EVERY line. Scope to `Services\\Compose\\` or a filename.

**Verify every agent report.** What that caught this time:
- a **wrong publish number already committed to a project note** (see the box above);
- a **stale test fixture neither agent owned** — `golden-utterances.json` still documented `match_mode` as
  a live payload field and carried a whole case for the retired `all` sweep. One agent fixed only the `.cs`
  half; the other flagged the file as out-of-boundary, and its flag was itself stale. **When two agents
  share a contract, check the seam neither one owns.**

---

## Standing constraints (unchanged)

- **Deploy prerequisite**: `Deploy-AnalysisAction.ps1` MUST run before ANY of Track C is observable.
  **052 raises the stakes** — it changed the four compose Action output schemas, so until that script runs,
  dev still asks the model for `target_text`. Deploy BFF + `sprk_spaarkeai` together (NFR-05).
  **Nothing from Phase 3 onward is deployed.**
- Publish ceiling 60 MB **compressed**; current **43.73 MB**. No new NuGet on Track A.
- **NEVER delete `docxBridge.ts`.** Confirmed unmodified through 052.
- Pre-existing CI red, NOT ours: **Compose Client Gate** (timeout flake since `7069717bd`) and **Trivy**
  (HIGH CVE on master). PR **#806** open.
- **C-4 still unmeasured against a real model response.** Anchors add 3.50% at realistic payload size.
- **Nothing in Track B has run against real Azure** — no storage account, no MI, no RBAC.
- **No bicep file has been changed by this project at any point.**
