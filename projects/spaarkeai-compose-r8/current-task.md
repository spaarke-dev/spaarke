# Current Task State — `spaarkeai-compose-r8`

> **Last Updated**: 2026-08-25 (by `context-handoff`) · **Committed through**: see `git log`
> **Branch**: `work/spaarkeai-compose-r8` · **Recovery**: read "Quick Recovery" first.
> Everything below is recoverable from files alone.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **none in progress** — 052 · 053 · 053b · 061 · 064 all landed |
| **Status** | Builds clean · BFF **11,277 passed / 0 failed** · ArchTests **62/62** · integration **96 P / 6 S** · compose-components **102 suites / 1,298** · SpaarkeAi **121 / 1,121** |
| **Progress** | **39 of 56 complete**, 10 open, 1 blocked |
| **Next Action** | **062** then **063** — SEQUENTIALLY (their `parallel-safe: ✅` flag is wrong; see the parallelism section). Before dispatching 063, read the TASK-INDEX 061 row: it adds the delete surface to `SessionFileBlobStore` (which today has only `WriteAsync`/`ReadAsync` — **no delete, no list**), at which point `SessionFilesCleanupScopeTests` becomes the load-bearing guard — **do not weaken it**. Erasure MUST enumerate by **tenant PREFIX** (`{tenantId}/session-files/{sessionId}/`), not by walking the Cosmos manifest: that container has `DefaultTimeToLive = 7776000` (90d), so the manifest expires while the blobs do not, and a manifest walk would orphan bytes permanently. |

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

## ⚠️ Publish-size: the ~1.3 MB divergence is the SHELL — settled 2026-08-25

**Current: 45.03 MB compressed incl. PDBs under `pwsh` 7** (215 files, 4 `.pdb`, **raw dir sum 137.41 MB**)
— **+0.07 MB** vs the 44.96 MB net10 baseline; ceiling 60 MB.

This project has carried two conflicting clusters (43.68–43.74 vs 45.00–45.04) for months. Zipping the
*same directory twice in the same minute* settled it:

| Shell | `Compress-Archive -CompressionLevel Optimal` |
|---|---|
| Windows PowerShell **5.1** (what `powershell` resolves to from Git Bash) | **43.73 MB** |
| **pwsh 7.6.3** (what the `PowerShell` tool and CI use) | **45.03 MB** |

Neither is an artifact — different `System.IO.Compression` implementations. **Canonical: `pwsh` 7**, because
CI uses it and it reconciles with the 44.96 MB baseline at +0.07 MB; PS 5.1 would imply a −1.23 MB drop no
code-only change could produce, which is itself the evidence the baseline was taken under pwsh 7.

**Method — pin the shell:**
```
rm -rf <out>
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o <out>
pwsh -Command "Compress-Archive -Path '<out>\*' -DestinationPath '<out>.zip' -CompressionLevel Optimal -Force"
```
**Always report the raw dir sum (~137 MB) + file count (215 / 4 `.pdb`) next to the zip.** Those are
shell-independent, so a mismatch there is a real content change while a zip-only mismatch is tooling. That
invariant is exactly what made this diagnosable.

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

## Remaining queue (10 open, 1 blocked)

| # | Task | Gate |
|---|---|---|
| **062 · 063** | Track B retention/TTL · erasure | 061 ✅ — **062 then 063, sequentially** |
| **052b** | Stale-target DETECTION durability (052's answer is ledger-durable; the question is `sessionStorage`) | 052 ✅ |
| **047b** | Never-silent hole (unpaired block reports no loss) | 056 ✅ |
| **058** | Nested / conditional merge fields | 049 ✅ 057 ✅ |
| **059** | SECURITY — `X-Tenant-Id` spoofable fallback; human sign-off required | 060 ✅ |
| **070–073** | Track D decomposition | ready; same files as Track A/C — sequence carefully |
| **074** ⛔ | Retire `ComposeShadowPatchEngine` | gate-confirm before deleting 3,000 lines |
| **090** | Wrap-up (incl. `/test-diet`) | all |

---

## 🔔 The ONE decision waiting

**`ComposeEditAnchorPass` + `ComposeAnchorResolver` now have ZERO production callers.** Verified
independently after 064: only comment references remain in `src/`; all 15 `Validate` call sites are in
tests. `POST /api/compose/edit-batch/validate` was their only caller and 064 deleted it.

They are the same orphan category 064 just retired — but task **052 kept the anchor pass deliberately**, and
the ADR-043/041 assessment (§7, C-7) names it the designated home for closed-set validation. So retiring it
is an owner decision, not a cleanup. Three options, in `notes/064-orphan-retirement-decisions.md` §4:

- **(a) Keep** as the designated home — accept it is currently dark.
- **(b) Wire it** — the obvious candidate is server-side validation of whole-document `target_para_id`s
  (today the closed-set check is client-side only).
- **(c) Retire it too** and amend the assessment.

> Owner decisions A and B (2026-08-25) are DONE — A → task 053b, B → task 064. Do not re-ask them.
> One sub-decision inside 064 has a revert point: three always-default fossils (`MatchCount`,
> `EditErrorKind.Overlap`, `BatchValidationResult.BatchErrors`) were removed beyond the task's list.
> Rationale + blast radius: `notes/064-orphan-retirement-decisions.md` §3.4.

### Superseded — decision #1 is CLOSED
### 🔔 Decision waiting #1 — a false `applied` that contradicts what we tell the model (surfaced by 053 §5)

A **post-052** payload can carry `target_para_id: null` — Structured Outputs requires the key to be present,
so "no identifier" arrives as an explicit null, not an absent field. Such an edit has no anchor **and no
prose**, so 053's fallback cannot serve it; it falls through to the insertion-at-cursor branch and reports
**`applied`**. Meanwhile the catalog prompt tells the model, verbatim:

> *"Set target_para_id to null ONLY when you genuinely cannot identify the paragraph. An EDIT with a null
> identifier is **REFUSED rather than placed** — there is no prose fallback — so a missing identifier costs
> you the edit."*

So the system currently lies to the model and gives the user a stray insertion reported as success. It is
**not** a UAT-21 mis-placement (nothing is struck; it is a pending insertion at the user's own caret), which
is why 053 surfaced it instead of changing it — the same branch also serves `compose-draft-document` and
`compose_context_insert`, which are *legitimately* anchorless.

**The discriminator that separates them cleanly**: `hasOwnProperty(payload, 'target_para_id')` — key present
and null ⇒ an edit that failed to identify its target ⇒ **refuse**; key absent ⇒ a genuine insertion ⇒ insert
as today. **Fix it, or change the catalog promise to match the code?** Recommend fixing the code: the promise
is the correct behavior and R8's charter is no false `applied`.

### Superseded — decision #2 is CLOSED (task 064 executed it)
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
`Services/Compose`).

⚠️ **Do NOT trust the POML `parallel-safe` flag — read the file sets.** 061/062/063 are all marked
`parallel-safe: ✅`, but **all three declare `Services/Ai/Sessions/` as `primary-edit`**, and 062
additionally touches the Compose client. They are safe *relative to other tracks*, not *to each other*.
Running them concurrently would collide on `SessionFileBlobStore` / `SessionFilesCleanupJob` /
`SessionRestoreService`. **Sequence them: 061 → 062 → 063.** The genuinely disjoint pair is
**053 (Compose client / jest) ∥ 061 (Ai Sessions server / dotnet)**, which is what was dispatched.

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

---

## Hard-won gotchas (this session) — do not rediscover these

- **Publish size: PIN THE SHELL.** `Compress-Archive` gives **43.73 MB under Windows PowerShell 5.1** and
  **45.03 MB under pwsh 7** for the SAME directory. Canonical is **pwsh 7** (CI uses it; reconciles with the
  44.96 MB baseline at +0.07 MB). Always report the **raw dir sum (~137.41 MB) + file count (215 / 4 `.pdb`)**
  alongside the zip — those are shell-independent and are the only reason this was diagnosable.
- **Line endings**: `.gitattributes` sets `*.cs text eol=crlf`, and edits can silently produce pure LF.
  **`grep -c $'\r$'` reports those files as CRLF and is WRONG.** Use `od -An -tx1 | grep -c '^0d$'`.
- **`dotnet format` before committing**, scoped: `dotnet format whitespace <csproj> --no-restore --include
  <your paths>`. CI auto-formats and pushes, which rejects the next push. A project-wide run also "fixes"
  ~22 pre-existing IDE1006 violations in unrelated files.
- **`grep -i compose` matches EVERY line** — the worktree path is `spaarke-wt-spaarkeai-compose-r8`. Scope to
  `Services\Compose\` or a filename.
- **Don't trust the POML `parallel-safe` flag — read the file sets.** 061/062/063 are all marked ✅ but all
  three declare `Services/Ai/Sessions/` as `primary-edit`.
- **Give each agent an explicit "you MUST NOT touch X" naming the OTHER agent's paths.** Both parallel waves
  this session stayed clean because of that; the one cross-agent seam that broke was a file *neither* owned.
- **When two agents share a contract, check the seam neither one owns.** Task 052: one agent fixed the `.cs`
  eval test, the other flagged the file as out-of-boundary (and its flag was itself stale) — the JSON fixture
  went stale and only main-session verification caught it.
- **Verify every agent report.** This session that caught: a wrong publish number already committed to a
  note, a stale test fixture, a misleading `parallel-safe` flag, and two of an agent's own tests that its
  mutation pass proved were passing vacuously.
