# Current Task State — `spaarkeai-compose-r8`

> **Last Updated**: 2026-08-26 (by `context-handoff`) · **Committed through**: `6d6ff750d` · **10 commits ahead of origin, NOT pushed**
> **Branch**: `work/spaarkeai-compose-r8` · **Recovery**: read "Quick Recovery" first.
> Everything below is recoverable from files alone.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **058 merged and verified.** **059 code-complete, awaiting human sign-off** (security-sensitive, CLAUDE.md §6). |
| **Status** | Builds clean · BFF **11,391 passed / 0 failed / 97 skipped** · ArchTests **62/62** · integration **96 P / 6 S**. No client code changed by either task, so the jest suites were not re-run. |
| **Progress** | **45 of 56 complete**, 4 open, 1 blocked |
| **Next Action** | **Sign off 059** (§8 of `notes/059-tenant-header-decisions.md` — two questions). Then Track D (**070–073**), then **090** wrap-up. |

### 058 — nested/conditional merge fields now carry (merged 2026-08-26)

Task 049 flattened these for a real structural reason, and that reasoning **survives intact**: a nested
field's recoverable instruction is a *concatenation* of both code phases, so re-emitting it authors a
different field. What 049 established is that a nested field cannot be **reconstructed** — not that it
cannot be **carried**. The third mechanism was never on the table: **carry the span's OOXML and never
parse it.** The tree survives because nothing reads it. Headline test asserts the saved span
**character-for-character** against the source — the one assertion a reconstruction cannot pass.

**It surfaced a second defect, which is the more valuable half**: `ComposeBlockMerge.InheritRunProperties`
donates the base paragraph's *dominant* run properties to every rendered run. In a conditional the
dominant run is the outer `IF` result — **bold** — so all 17 carried runs came back bold, silently bolding
both inner `MERGEFIELD` values. A fidelity loss introduced by the fix for a fidelity loss, and one that
would have shipped looking correct. Rule now stated where it lives: *inheritance repairs a re-authored
run; a carried run has nothing to repair.* Scoped to nested spans only.

Residual list: the nested half leaves §2; only the **unterminated** field (`TOC`/`INDEX`, which spans
paragraph marks) remains. [`notes/058-nested-field-carry.md`](notes/058-nested-field-carry.md).

✅ **Owner-signed 2026-08-26**: *"follow the established pattern."* A user who deletes a conditional chip
is indistinguishable from a client that never sent it, so the construct is **restored** — the same trade
already taken for bookmarks, SDT shells and objects. This is now the **fourth** construct behaving that
way and the pattern is explicitly sanctioned, so a future carry should adopt it without re-asking.

Still true and NOT covered by that sign-off: no browser/UAT run, and the document was never opened in
Word. Fidelity is asserted through the SDK, the schema validator and the relationship gate.

### 🔒 059 — what it actually turned out to be (read before signing off)

Filed as *"remove the spoofable `X-Tenant-Id` fallback from four handlers plus the auth path."*
The mandated enumeration found **21 sites across three mechanisms**, and **the filed one was the least
severe**:

| Mechanism | Sites | Status before 059 |
|---|---|---|
| `X-Tenant-Id` header, last tier of a `??` chain | 16 | **LATENT** — only reachable by a principal with **no `tid` claim at all**, since tier 1 short-circuits. One such principal exists (`RagApiKey`) but never touched this tier. |
| `X-Spaarke-Tenant-Id`, no claim consulted | 1 | Live, admin-gated, **zero senders** anywhere in the repo |
| **`?tenantId=` query string** | **4** | **LIVE for any authenticated user.** Three consult **no claim at all**; the fourth let the query string OUTRANK the claim. |

**Two of those four are Compose's own**: `GET /api/compose/documents/{documentSpeId}` (the document
**open/resume** path) and `GET /api/compose/sessions/{sessionId}/annotations`. Both took the tenant
from the URL, so a caller could open another tenant's Compose session and resume its anchored
annotations, defined terms and action history. Two of them rejected a missing value with *"tenantId
query parameter is required for multi-tenant isolation"* — isolation the caller chose.

All 21 are closed. The guarantee is **structural, not a rule**: `TenantResolution.ResolveTenantId`
takes a `ClaimsPrincipal`, **not** an `HttpContext`, so it cannot reach a header, query string or
body — the same idiom as `ComposeEditAnchorPass` (no document text) and post-064 offsets. A
two-armed tripwire (`Headers[…Tenant…]` | `[FromQuery … tenantId`) matches by **shape, not name**;
its regex is verified in both directions, and its query arm is what found the two Compose sites
*after* the header sweep was believed complete.

**Four test fixtures minted principals with no `tid`** — a shape Entra never issues — and the tests
compensated with the header. That fixture gap was holding the hole open: it made the spoofable
fallback the only tenant path those tests ever exercised. Repaired the fixture, not the symptom
(`bff-extensions.md` §F.2). Two further tests were passing **vacuously** and now assert something
real. Full record: [`notes/059-tenant-header-decisions.md`](notes/059-tenant-header-decisions.md).

### This session — 9 tasks landed, all committed, nothing at risk
`052` demote text-search · `053` bounded confirmable fallback · `053b` null-identifier edits reach the
document · `061` lazy re-index · `062` retention + availability · `063` durable erasure · `064` retire the
orphaned edit-batch surface · `047b` never-silent hole · `052b` stale-detection durability.

Ten commits, tree clean, **nothing pushed** — push is the operator's call (PR #806 is open).

### Critical context in one paragraph
**Track C (AI edit placement) and Track B (durable session files) are both COMPLETE.** Text search is no
longer a placement mechanism — and that is now enforced by the TYPE SYSTEM in two places rather than by
rule: `ComposeEditAnchorPass.Validate` takes no document text, and after 064 no type in `Services/Compose/`
can express a character offset at all. The client fallback survives only as a bounded, confirmable proposal
for replayed entries, in a module that **has no `applied` outcome**. Three defects found along the way were
worse than filed: an anchored edit replaced the ENTIRE paragraph; a stale target was not detected at all
(silent overwrite of the user's newer text); and 047b was not merely under-reporting — it was cloning an
UNTOUCHED block from the wrong base, an outright breach of ADR-049 invariant 2, in a real signed NDA.

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

### 🚨 ARMING WARNING — the code gate is closed, but do NOT set `BlobEndpoint` yet

Tasks 060–063 are done: durable store, lazy re-index, retention, erasure. ADR-015's precondition
(retention AND erasure before a persisted store is armed) is **satisfied in code**.

**Two pre-existing AUTHORIZATION defects sat on the same DELETE route.** One is now CLOSED; one remains.

1. ~~**The spoofable `X-Tenant-Id` fallback**~~ — **CLOSED by task 059** (2026-08-26), along with 20
   sibling sites it turned out to have. ⚠️ **Correction to what this warning previously said**: the
   header was described here as live on that route. It was **not**, for any caller holding a normal
   token — it sat at the END of a `??` chain, so it was only ever reached by a principal carrying **no
   `tid` claim at all**. The defect was **latent** (one route-registration away from live), not live.
   I wrote the earlier claim; it was wrong, and a test I wrote to prove it passed **vacuously** before
   the fix, which is how it was caught. See `notes/059-tenant-header-decisions.md` §3.
2. **No owner check — STILL OPEN.** `ChatSessionManager.DeleteSessionAsync(tenantId, sessionId, …)` is
   keyed on tenant + session only, and `ChatSession` has **no owner field at all** — so a check is not
   implementable without a persisted-schema change (Redis + Cosmos + Dataverse) and a policy for
   pre-existing sessions. 059 narrows it from **cross-tenant** to **within-tenant**; session ids are
   `Guid.NewGuid().ToString("N")`, so exploitation needs a leaked id, not a guess. **Owner decision
   pending** — `notes/059-tenant-header-decisions.md` §6a and §8.

What arming changes is **blast radius**: today these delete a 24-hour AI-Search index entry; armed, they
delete **90-day durable bytes**, and 063 confirms Azure soft-delete and versioning are OFF, so a
completed delete is final. A store that is armed and later disarmed also cannot be erased from.

**Arming is now gated on: (a) human sign-off of 059, and (b) the cross-user decision.** Not on further
code.

### The four operator steps (all still required, and still not done)
Provision/pick a storage account → create the container → grant **`mi-bff-api-dev`**
(the UAMI — **not** the system-assigned identity `model2-full.bicep` currently targets, which does not
exist on `spaarke-bff-dev`) *Storage Blob Data Contributor* → set `SessionFileStore:BlobEndpoint`.
063 also notes the role assignment is missing from `customer.bicep` and `model1-shared.bicep`.
Dev has **no storage account**, and the UAMI holds **no storage role of any kind**.

---

## Remaining queue (6 open, 1 blocked)

| # | Task | Gate |
|---|---|---|
| **058** | Nested / conditional merge fields | 049 ✅ 057 ✅ |
| **059** | SECURITY — `X-Tenant-Id` spoofable fallback + the cross-user DELETE gap; human sign-off required | 060 ✅ — **dispatch next; gates arming** |
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

### ✅ DEPLOYED TO DEV — 2026-08-26 (BFF + `sprk_spaarkeai` together, NFR-05 satisfied)

First deploy of Tracks A / B / C. Commit `cfc118fe4` (merged with `origin/master`, 0 behind).

| | |
|---|---|
| **BFF** | `spaarke-bff-dev` · package **45.14 MB** · SHA-256 hash-verified on 4 critical files · `/healthz` 200 · 2/2 CORS origins present |
| **SpaarkeAi** | web resource `sprk_spaarkeai` (`5206a442-…`) updated + customizations published · bundle **5,725 KB**, rebuilt today (the previous `dist` was **Aug 21** — five days stale) |
| **Route-surface proof** | **All 17 authenticated Compose routes return 401, zero 404s** — task 073's decomposition verified against the DEPLOYED app, which is stronger than the two local oracles. |

⚠️ **Still NOT observable: Track C.** `Deploy-AnalysisAction.ps1` has **not** been run. Task 052 changed
the four compose Action output schemas, so until those `sprk_analysisaction` rows are upserted, dev still
asks the model for `target_text` and the anchored-placement work cannot be exercised. **This is the next
deploy step, and it was not part of the requested deploy.**

⚠️ **Track B remains DISARMED** — `SessionFileStore:BlobEndpoint` empty; dev has no storage account and the
UAMI holds no storage role. Unchanged by this deploy.

- **Deploy prerequisite (CORRECTED 2026-08-26 — the old instruction was NOT EXECUTABLE)**: Track C needs
  the Action mirrors in `infra/dataverse/actions/` deployed to `sprk_analysisaction`, via the NEW
  `scripts/Deploy-ActionMirrors.ps1`. The previously recorded instruction — *run `Deploy-AnalysisAction.ps1`* —
  **could never have worked**: that script reads a `{actions:[...]}` wrapper (mirrors are bare objects),
  hard-requires `actionTypeName` (all 17 mirrors omit it), and writes `sprk_ActionTypeId@odata.bind` — a
  lookup that **does not exist** on the entity. The ActionType axis was retired ON PURPOSE by R7 task 028 /
  FR-07; `seed-data/manifest.yaml` already recorded `deployer: null` for this source. **DONE 2026-08-26** —
  the four Track C actions now carry `target_para_id` in both schema and prompt.
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
  **`grep -c $'\r$'` reports those files as CRLF and is WRONG.** The reliable check needs the `tr`:
  `od -An -tx1 <file> | tr ' ' '\n' | grep -c '^0d$'` — non-zero means CRLF.
  ⚠️ **Without `| tr ' ' '\n'` it returns 0 for CORRECT files too** (od prints 16 bytes per line, so no line
  ever equals `0d`) — i.e. it silently reports every file as broken. Task 047b caught exactly that error in a
  brief written from this note; the note is now correct.
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
- **Don't `dotnet build` while a `dotnet test` run is live** — the test host holds the output assembly and
  the build reports a phantom error. Same family as the mid-edit hazard. Re-run after it finishes.
- **The mid-run hazard includes FIXTURES, not just code.** Re-running a corpus generator
  (`tests/fixtures/compose-corpus/generators/*.py`) rewrites its `.docx` **in place**. Doing that during a
  live suite produced **2 corpus-theory failures at `< 1 ms`** that looked like real 058 regressions and were
  purely self-inflicted; a clean re-run gave **11,391 / 0**, exactly the predicted count. The `< 1 ms`
  duration is the tell — that is a file-read failure, not a logic failure.
- **A regenerated corpus `.docx` is NOT a no-op diff.** `zipfile.ZipFile(path, 'w')` stamps the current
  mtime into every entry, so the bytes differ on every run while the content is identical. `git status`
  cannot tell that apart from a real content change — unzip and `diff -r` before committing one.
- **Run the two client suites SEQUENTIALLY, not concurrently.** 052b saw 2 and 12 spurious failures
  running `Spaarke.Compose.Components` and `SpaarkeAi` at the same time; both green run one after the other.
- **Verify every agent report.** This session that caught: a wrong publish number already committed to a
  note, a stale test fixture, a misleading `parallel-safe` flag, and two of an agent's own tests that its
  mutation pass proved were passing vacuously.

---

## 🚨 047b found more than a reporting bug — read this before touching the merge

Task 047b was filed as "an edited block with no base counterpart reports no loss". It was **not only** that.
On `interior-text-boxes.docx`, blocks 1 and 2 project to **byte-identical** models (the text box's prose is
accept-flattened; the shape is not carried), so `ComposeBlockMerge.Plan`'s LCS was **ambiguous** — and the
traceback's tie-break skipped the *posted* block, producing:

```
posted 1 -> Render base=-1   <- the EDITED block, no counterpart -> nothing reported
posted 2 -> Clone  base=1    <- the UNTOUCHED twin, cloned from the WRONG base
              base 2 stranded, never written
```

The saved package held block 1's `v:shape` at position 2 and block 2's not at all. **ADR-049 invariant 2
("untouched blocks are preserved") was being breached by a clone.** The remark on `Plan` asserted this could
not happen — equality there is over the *projected model*, not the OOXML — and that comment is why nobody
looked. Fourth stale-comment defect this project has hit.

Corpus sweep, 24 docs × every block position = **294 single-block edits: unpaired blocks 5 → 0.** Four of the
five were in a **real signed NDA** (`AppligentNDA_Signed.docx`), on consecutive empty paragraphs.

**Why the fidelity gate never caught it**: the gate edits block 0 of that document. Every other parity row
sits in a document whose blocks all read differently. 047b added a `pictTextBoxTwin` parity row so the
published list is now measured **at a duplicate-key block position** — that is the gap that let this survive
four runs of a check built to catch it.

`COMPOSE-WRITE-RESIDUAL-LOSS.md` changed but **no row changed** — the signed five losses are identical. What
changed is that §2's promise ("reported by name … none is silent") is now *true* where it wasn't.

### Recorded by 047b, not fixed (deliberate)
- `BaselineUnavailable` / `BaselineUnaligned` fall back to R6's whole-document rebuild with no base side — a
  different failure CLASS (document-level, not per-edited-block), whose honest signal needs a new degradation
  code + client copy + banner state, which this project's CLAUDE.md forbids adding here. Reachability
  measured: **0 of 24** corpus documents. Both already on `ComposeMergeStats`; only a consumer is missing.
- LCS cannot see a MOVED block (matches never cross) — 0 of 294 after the fix.

## Doc drift to fix (not urgent, main-session only — hot path)
Root `CLAUDE.md`'s ADR-049 pointer says the save "pairs blocks by **document order**". It has paired by
**LCS** since task 040 — loosely true (matches are monotone) but imprecise, and 047b showed the imprecision
is where a real defect hid. Touching root CLAUDE.md needs `/conflict-check` + a `.claude/CHANGELOG.md` entry.
