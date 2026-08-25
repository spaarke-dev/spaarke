# Current Task State — `spaarkeai-compose-r8`

> **Last Updated**: 2026-08-25 (by `context-handoff`) · **Branch**: `work/spaarkeai-compose-r8`
> **Recovery**: read "Quick Recovery" first. Everything below is recoverable from files alone.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Status** | Wave complete. Tree clean, all pushed, full solution builds, BFF **11,295 passed / 0 failed**, client **99 suites / 1,212**. |
| **Landed this wave** | **055** · **049** · **057** · **056** · **060** — all independently re-verified in the main session, not accepted on agent report |
| **Next task** | **052** — demote the text-search path (`tasks/052-retire-text-search-path.poml`), FULL · opus @ xhigh. Deps 051 ✅ 054 ✅ 055 ✅. |
| **Next Action** | Answer the OWNER QUESTIONS below (two of them gate real work), then dispatch 052. Track B 061–063 are unblocked and `parallel-safe: ✅` — the only genuinely parallel family left. |

### Project status: 32 of 44 tasks complete

Residual-loss list: **both rows the owner declined on 2026-08-25 are closed** (fields 049+057, objects 056).

---

## 🔔 OWNER QUESTIONS — nothing proceeds on these without an answer

**Q1 — Which bicep stack do the live environments use? (blocks Track B in production)**
Task 060's premise was wrong: `storage-account.bicep` only creates the Blob-Data-Contributor role assignment
when `appServicePrincipalId` is passed, and **`customer.bicep` and `stacks/model1-shared.bicep` do not pass
it**. Where `model2-full` does, it grants the **system-assigned** identity while the BFF pins a **UAMI** when
`Graph:ManagedIdentity:ClientId` is set. Nothing is broken today — the store ships DISABLED
(`SessionFileStore:BlobEndpoint` empty) — but 061–063 build on it.

**Q2 — Sign off the residual-loss list? (closes task 045, open since Phase 4)**
Five remaining rows, each occurring ONLY in the paragraph the user edits, each reported by name on save:
nested/unterminated fields · text boxes · footnote refs · endnote refs · content controls. The owner has
called the last three not important; the first two are new, narrower carve-outs from the rows just retired.

**Q3 — Conditional merge fields in templates: fix, or accept? (NEW — surfaced 2026-08-25)**
The owner is introducing templates + field-merge codes. Task 049's gate is STRUCTURAL, so:
- `MERGEFIELD FirstName \* MERGEFORMAT` → **carried verbatim** ("unknown/vendor instruction: carry, never
  interpret"). Simple merge codes survive.
- **`{ IF { MERGEFIELD Company } = "" "" "…" }` → FLATTENED.** Nested fields are structurally uncarryable:
  the scan folds the inner field into the outer span, so the recoverable instruction is a concatenation and
  re-emitting it would author a DIFFERENT field. Flattened with a named warning, never silent.
- `TOC` / `INDEX` also flatten (they span paragraph marks).

Conditional merge blocks are common in real templates. **Decide before template authoring starts.**

**Q4 — The `X-Tenant-Id` header fallback: separate task, or accept?**
Pre-existing and repo-wide. Task 060 promotes the same value from a 4-hour cache key to a **durable 90-day
blob partition key** — a spoofed header would place bytes permanently in another tenant's prefix. 060 did
NOT change it (four handlers + the auth path); it added a warning log at both write sites.

**Q5 — The silent-loss hole: fix in R8, or file out?**
On `interior-text-boxes.docx`, editing block 1 loses a `w:pict` with **no warning** while block 2 reports it.
Two paragraphs with byte-identical projected text make `ComposeBlockMerge.Plan` pair the edited block against
NO base, and loss-reporting is skipped for unpaired blocks. Predates R8, but *"an edited block with no base
counterpart reports no construct loss"* undercuts the never-silent guarantee the residual list rests on.
Written up in `notes/056-object-carry-decisions.md` §7.

### Already resolved 2026-08-25 (no action needed)

- **ADR-015 records** — governed-stores table now carries a Tier-3 row for `SessionFileBlobStore`, with
  retention/erasure honestly marked NOT YET IMPLEMENTED and the mechanical gate named. §6.5 Path B, applied.
- **Blob container** — target is a dedicated `session-files` container; default stays `ai-chunks` until
  bicep declares it (one line, three stacks). Deliberately deferred to pair with Q1's role-assignment fix.
  Rationale in `notes/track-b-placement-justification.md` "Owner resolutions".

---

## What this wave delivered

| Task | Substance |
|---|---|
| **055** | Whole-document anchored placement. `comments[]` (the `flag-risks` intent's ENTIRE output) was 100% prose-anchored; now populates `AnchoredAnnotationAnchor.paraId` — a **6th dark-machinery instance** (shipped R3 FR-11 as PRIMARY, live consumer, no writer). Decision: converge the RESOLUTION, keep the two SINKS separate. |
| **049** | Word fields carried, SERVER half. Gate is STRUCTURAL, not a keyword allow-list. Corrected a **stale renderer comment** ("the model does not carry bookmarks" — untrue since task 041), which is what allowed `REF`/`PAGEREF` to be carried LIVE. |
| **057** | Fields, CLIENT half — created because 049 was a **producer with no consumer**. A field is the first segment present in the run stream and ABSENT from the coordinate space. Also fixed a `getHTML()` defect turning an atom's label into document content. |
| **056** | Objects carried. Settled relationship-survival **empirically** — opened the saved package and resolved every `r:*` attribute rather than trusting the renderer's "orphaned … inert weight" remark (**2nd stale-comment correction**). Full carry in two halves; added a gate that every relationship-namespace attribute must RESOLVE against the carrier, because a valid drawing naming a missing relationship yields a file Word calls damaged. |
| **060** | Durable tenant-partitioned session-file byte store. Ships DISABLED. See Q1. |

---

## Remaining queue

| # | Task | Gate |
|---|---|---|
| **052** | Demote the text-search path | READY — dispatch next |
| **053** | Bounded confirmable fallback | 052 |
| **061/062/063** | Track B lazy re-index · retention/TTL · erasure | 060 ✅ — **the only `parallel-safe: ✅` family** |
| **070–073** | Track D decomposition | ready; same files as Track A/C, sequence carefully |
| **074** ⛔ | Retire `ComposeShadowPatchEngine` | gate-confirm before deleting 3,000 lines |
| **045** | Residual list sign-off | **Q2** |
| **090** | Wrap-up (incl. `/test-diet`) | all |

---

## How to run the next wave (this worked — reuse it)

**Parallelism**: the project's blanket `parallel-safe: false` on the Compose spine is too coarse. Judge
**file AND toolchain disjointness per pair**:
- 055 (client/jest) ∥ 049 (server/dotnet) — clean.
- 060 — independent files but also dotnet ⇒ **isolated worktree**, cherry-picked back cleanly.
- **052 ∥ 056 would collide** (both `Services/Compose`). 061–063 are the safe parallel family now.

**Main session reserves** `TASK-INDEX.md`, `current-task.md`, `.claude/**` and ALL git operations; agents own
code only. Prevented every collision. Tell agents explicitly — they cannot write `.claude/` (root §3) and
should report proposed CHANGELOG text instead.

**Never build/test while an agent is mid-edit in the same tree** — you will read half-written work as a
regression (happened once: `TryCarryEmbeddedObjects`).

**Verify every agent report.** What that caught this wave: a suite failure the agent did not have (SSE
timing flake under concurrent-agent CPU load — passes in isolation in 178ms); a publish-size number 1.3 MB
off (**the raw directory sum is ~137 MB — measure the ZIPPED size**); a doc claim true of only the server
path; and 057's display fix silently not reaching `object` atoms.

**Escalation precedent (057)**: a trigger fired on its literal predicate but not on its reasoning. Accepted
the scope extension because it was minimal, used an in-file precedent, and was **surfaced with an offer to
revert**. Judge the reasoning behind a trigger, not just its wording.

---

## Standing constraints (unchanged)

- **Deploy prerequisite**: `Deploy-AnalysisAction.ps1` MUST run before ANY of Track C is observable —
  including task 051's work. Dev stores the WHOLE mirror file in `sprk_inputschema` for
  `compose-draft-alternative` / `compose-compare-to-playbook`, so `GetDeclaredProperties` returns null.
  Deploy BFF + `sprk_spaarkeai` together (NFR-05). **Nothing from Phase 3 onward is deployed.**
- Publish ceiling 60 MB **compressed**; current **43.74 MB** incl. PDBs. No new NuGet on Track A.
- **NEVER delete `docxBridge.ts`.**
- Pre-existing CI red, NOT ours: **Compose Client Gate** (timeout flake since `7069717bd`) and **Trivy**
  (HIGH CVE on master). Verified identical on `dfa713cbf`, before this session's work. PR **#806** open.
- **C-4 still unmeasured against a real model response.** Anchors add 3.50% at realistic payload size
  (40.4 KB, under the 128 KB cap); the over-cap case at the schema's declared maxima is pre-existing.
- **Nothing in Track B has run against real Azure** — no storage account, no MI, no RBAC.
