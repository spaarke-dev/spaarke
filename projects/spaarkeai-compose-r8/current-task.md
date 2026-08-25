# Current Task State — `spaarkeai-compose-r8`

> **Last Updated**: 2026-08-24 (by `context-handoff`) · **Pushed through**: `64548f227`
> **Recovery**: read "Quick Recovery" first. Everything below is recoverable from files alone.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Last task** | **051** — anchor supply (`tasks/051-anchor-supply.poml`) — ✅ **COMPLETE** |
| **Next task** | **054** — `tasks/054-whole-document-closed-set-supply.poml` (not started) |
| **Status** | Branch clean, synced with master (**0 behind, 78 ahead**), all pushed. No task in progress. |
| **Rigor / tier** | 054 is FULL · **opus @ xhigh** — do not run it on a lower tier |
| **Next Action** | Start task 054 via the `task-execute` skill. Its step 2 owns the one real decision: enumerate the whole-document paragraph set **server-side** from `ChatSession.ReferenceMap`, or **client-side** in SpaarkeAi's `ConversationPane.tsx`. Read `notes/adr-043-041-assessment.md` C-1-as-amended and C-7 FIRST. |

### Project status: 26 of 42 tasks complete, 15 open (1 dropped)

| Track | Open |
|---|---|
| **C — AI edit placement** | **054** supply · **055** placement · **052** demote text-search *(gated on 051+054+055)* · **053** bounded fallback |
| **B — durable session files** | 060 blob store · 061 lazy re-index · 062 retention/TTL · 063 erasure — *not started, genuinely parallel* |
| **D — decomposition** | 070–073 decompose 4 files · **074 ⛔ BLOCKED** (retire `ComposeShadowPatchEngine` — gate-confirm first; it still serves the op-log path) |
| **Wrap-up** | **045** — residual-loss list ⏳ **awaiting OWNER sign-off** (not an agent task) · 090 wrap-up |

---

## What task 051 delivered (all committed + pushed)

Prose matching is no longer the targeting channel for any **selection, caret, note, or citation** edit. The
chain is verified end to end, link by link, by tests:

**capture → send → reach the model → model echoes the id → deterministic placement**

| Link | Where |
|---|---|
| Capture the paraId | 5 dispatch sites (2 toolbar mounts, note single + batch, caret) |
| Carry it to the model | **ADR-043 Amendment 1** — declared companion inputs render into `## Input` |
| Model can answer with it | `target_para_id` on 3 selection-scoped Action output schemas |
| Resolve it deterministically | `ComposeAnchorResolver` + `ComposeEditAnchorPass` (server), `resolveAnchoredSpans` (client) |

`CitationResolver` now has its first real BFF consumer (`ComposeAnchorResolver.cs:60,74`).

### Commits (this session, newest first)

```
64548f227 Merge origin/master (91 commits; CHANGELOG conflict resolved by keeping BOTH sides)
29f11f7ec docs(incident): dev BFF Service Bus config/code mismatch + stale net8 template
be9cae9e3 docs: define tasks 054/055; 051 complete; 052 reframed and re-gated
67689b3ad fix(ai-spine): declared inputs reach the model (ADR-043 Amendment 1)
aa7445a20 feat: task 051 FR-C03 — let the model ANSWER with an anchor (3 Actions)
f3ab59a95 feat: close the fifth dispatch site (caret path)
52c7f57f8 feat: task 051 FR-C02 — CitationResolver gets its first consumer
```

---

## Critical context for whoever picks this up

**⚠️ THE RECURRING TRAP — check for a DARK implementation before building anything.** Five instances now:
046 (hardBreak), 048 (atoms), 051 FR-C01 (bookmark controllers), 051 FR-C02 (`CitationResolver`), and the
whole ADR-043 Amendment 1 chain. In every case the machinery existed, was tested, and had **no production
consumer**. Grep for the thing before you write it.

**⚠️ AND THE HARDER VERSION — verify the PRODUCER before building the CONSUMER.** This session built the
anchor consumer (server + client, green tests) before checking whether anything could emit a
`target_para_id`. Nothing could: the Action schemas didn't declare it, no Action declared an `inputSchema`
at all, and `Deploy-AnalysisAction.ps1` never wrote `sprk_inputschema`. Three dead links behind a green
test suite. **Write the failing test first and watch it fail** — that is how all three were found.

**ADR-043 Amendment 1 is a spine change; know why it exists.** The operand channel is **single-valued** —
`TryFindDeclaredOperandField` returns on the first vocabulary match and builds a one-key object. So it can
say *what content* a completion runs over but never *where it came from*. Adding a 4th vocabulary entry
would make the anchor **compete** with `selectionText` (first match wins → one silently vanishes, breaking
every selection dispatch); nesting it in the operand value is a type pun (Tier-3 content vs Tier-1 id).
The fix: **declaration is the contract** — an Action's declared inputs render alongside the operand.
Bounded: never the operand field, never another vocabulary name, never `ledger_resolution`, count/size
capped, oversize **skipped and logged, never truncated**. Both ADR versions amended; C-1 superseded in part.

**052's scope changed — re-read the POML before executing it.** It **demotes** text matching; it does not
eliminate the capability. Three roles legitimately survive: return-from-Word re-anchor
(`AnnotationReanchorService`, KEEP), 053's bounded confirmable fallback for anchorless input, and
user-invoked find/replace where matching text IS the semantics. Also: **`match_mode: 'all'` must be decided
explicitly**, not dropped as a side effect.

**Sequencing rule for 054 — do not break it.** The `compose-revise-document` catalog change lands **with**
the supply, never before. Requiring an id the model was never given refuses every whole-document edit —
strictly worse than today's prose matching.

**`usePendingRedline.ts` contains a literal NUL byte** (its text-index sentinel), so `grep` treats it as
binary — use `grep -a`.

**A `BlockInfo`'s `from`/`to` are NODE boundaries.** A redline needs `from+1 .. to-1`, or the replacement
lands *after* the paragraph. Commented at `spanOf`.

---

## Environment + deployment state

- **`spaarke-bff-dev` is HEALTHY (HTTP 200).** It was down 18:38–~19:5x UTC 2026-08-24 after a deploy from
  this branch; **fixed by `spaarke-auth-v4-dataverse-MI`**, and both fixes are now IN this tree via the
  merge: `JobProcessingModule` no longer throws on a missing connection string (MI path via
  `ServiceBusClientFactory.Create`), and `runtimeconfig.template.json` no longer injects a `net8` block.
  Incident write-up: [`notes/incident-2026-08-24-servicebus-config-mismatch.md`](notes/incident-2026-08-24-servicebus-config-mismatch.md)
  (both defects it names are now RESOLVED upstream — keep it for the process gaps).
- **⚠️ Azure CLI default subscription was `Spaarke Model 1 Production`; this session set it to
  `Spaarke Devlopment Environment`.** `spaarke-bff-dev` lives in the dev subscription — a `az webapp show`
  against the prod default returns `ResourceGroupNotFound` and looks like a missing resource.
- **NOTHING from Phase 3 onward is deployed.** Track S shipped; Tracks A and C are merged to the branch and
  unshipped. Deploy BFF + `sprk_spaarkeai` **together** (NFR-05).
- **The 3 Action seed changes need `Deploy-AnalysisAction.ps1` run** — and that now depends on this
  session's `sprk_inputschema` mapping fix in that script. Until it runs, the anchor work is **not
  observable in the product**. This is a prerequisite, not a follow-up.
- **CI note:** `Compose Client Gate (Save-Contract Suite)` is RED and was already failing at `7069717bd`,
  before any Track C work. Cause is per-test 5s **timeouts** (the suite takes ~90s locally for 16 tests);
  all 16 **pass** locally. Pre-existing hygiene debt, not a product defect. PR **#806** is open.

---

## Still open, not on the task index

- **C-4 is UNMEASURED.** ADR-040 caps inline payloads at 128 KB and `ChatEndpoints.ProjectComposeOutputs`
  **skips** truncated entries — a whole-document revise could vanish from the read projection rather than
  degrade. Now a constraint on both 054 and 055; still nobody has measured it.
- **045 needs OWNER sign-off** on the published residual-loss list. Not an agent task.
- **Process gaps from the outage** (in the incident note): no config pre-flight in `bff-deploy`;
  `/conflict-check` + `projects/INDEX.md` track file overlap but have **no notion of shared environment
  config**; fail-fast guards that encode a single auth model.

---

## Task 051 — decisions made (carry these forward)

1. **Wire, don't rebuild.** The two R4 controllers are the FR-C01 implementation; only the mount was missing.
   Invariant (6) holds by construction — the bookmark rebases on the editor's own ProseMirror `Mapping` (the
   op-log's primitive) and applied ops go through a normal `chain()`, so the step interceptor captures them into
   the same log as user keystrokes. **No parallel rebaser was written**, which acceptance criterion 5 requires.
2. **`beginGenerate({ range })` instead of a second mechanism.** The review-note tools anchor on a note's clause
   span, not the caret. One additive parameter reuses all downstream behaviour. The batch path calls
   `dispatchNoteToolRequest`, so both note sources were covered by the single change.
3. **Reanchor options deliberately omitted** from `useAiApplyValidation`. Without them `canReanchor` is false and
   an unvalidatable op surfaces with NO fuzzy hint — the more conservative branch. The hint is presentation only
   and never a placement (the hook's own SCOPE DECISION), so nothing is lost and no service dependency is added.

## Task 051 — findings that bind the REST of Track C

**F-1 / F-2 — the task-050 assessment's C-1 is half right.** It says the client already sends
`selectionAnchorStart` / `selectionAnchorEnd` / `targetParaId`. The first two are unconditional; **`targetParaId`
was behind `useBookmark` and never shipped**. Now fixed. Cite the corrected version, not C-1 as written.

**F-3 — ESCALATION TRIGGER 1 FIRED AND IS NOW CLOSED.** Four dispatch sites build edit slots, not one:

| # | Site | Durable identity | Status |
|---|---|---|---|
| 1 | `ComposeAiToolbar.handleActionClick` (selection) | bookmark paraId | ✅ wired (part 1) |
| 2 | `ComposeEditor.dispatchNoteToolRequest` (review-note, single) | comment `threadId` → `findCommentAnchorRange` | ✅ wired (part 1) |
| 3 | `ComposeEditor.runBatchNoteToolAsync` (batch) | same — reuses site 2 | ✅ covered |
| 4 | Context-pane bridge → `enqueueComposeAction` | inherits from 1–3 | ✅ inherits |
| 5 | `ComposeEditor.runDescribeChangeAtCaret` (Ctrl+Space, UC-5) | caret's enclosing textblock | ✅ wired (`5ea76633d`) — **found AFTER part 1 closed the trigger; there were five sites, not four** |

**Task 052 may now retire the search path only after FR-C02/C03 also land** — a citation-driven or
review-pass edit with no anchor would still depend on it.

## Task 051 — HISTORICAL: what remained mid-task (all resolved; kept for the constraint list)

> ⚠️ Task 051 is COMPLETE. This section is the mid-task snapshot — read it ONLY for the C-1…C-7
> constraint text at the end, which still binds tasks 052/053/054/055.

- ~~**FR-C02**~~ — ✅ DONE (`6e663be1c`). `ComposeAnchorResolver` is the consumer; server + client both resolve a
  citation deterministically, and both refuse rather than degrade to a search.
- **FR-C03** — *validate* half ✅ DONE (membership check + loud refusal, server and client). *Supply* half is the
  open decision — see the 🔔 ESCALATION at the top of this file. It MUST land in the same PR as the
  `compose-revise-document` catalog-DATA change.
- ~~**Tests**~~ — ✅ DONE. `ComposeEditAnchorPassSeamTests.cs` is the C-2 vertical slice (real corpus →
  projection → anchor → verdict, 13 tests); `usePendingRedline.anchor.test.tsx` is its client mirror (14). The
  "zero text matching invoked" criterion is enforced by a throwing validator, not by output inspection.
- **C-4 unchecked** — ADR-040 caps inline payloads at 128 KB and `ChatEndpoints.ProjectComposeOutputs` SKIPS
  truncated entries entirely. Anchors now ride every edit entry; **measure the whole-document-revise worst case
  and confirm headroom, or state the degradation.** Not yet done.
- **C-1** — anchors MUST ride as `args.slots.*`; adding a fourth `OperandVocabulary` entry converts Path C into
  Path B (a spine change). **C-3** — never add `body_html` to an edit payload. **C-6** — do not touch the
  supersession leg. **C-7** — closed-set validation belongs in the Compose-owned path, not `SessionDispatchOrchestrator`.

### The one thing to understand

**Untouched blocks are preserved; the edited block is mostly preserved; a PDF now reopens as the document
it became; nothing is deployed.**

| | Control (master) | Now |
|---|---:|---:|
| Untouched-block preservation, lenient | 18.08% | **100.00%** (18/18 docs) |
| Near tier | 6.67% | **100%** (14/14 measurable) |
| Strict | 12.18% | **100% on 16 of 18** |
| **Edited block intact** | — | **12 of 18 documents** |
| **PDF → 2 documents after a refresh** | yes | **no** (1 item, 1 row, both edits) |

---

## What shipped in 044 (2026-08-23)

**FR-A09 — measured first, and the diagnosis moved.** The POML said a PDF's second save after a refresh
"falls back to a full rebuild". What actually happens is worse: re-opening projects the PDF *again*, so the
user's saved work is **invisible** in a Word document they have no pointer to, and their next save mints a
**duplicate** (the transient key is per-mount and never persisted). Fixed at **load** — resume on the
document that already exists — which makes save two an ordinary imported save that resolves and clones.

The cheap save-side fix (stable transient key → dedup onto the existing doc) was **rejected**: after a
refresh the client's model is the fresh PDF projection, so rendering it into the existing document
overwrites the first save's edit. It trades a visible duplicate for silent data loss.

**Mechanism**: two `IDistributedCache` keys (ADR-009) — `pdf-session:{sessionId}` carries the server's own
bytes-first PDF determination from load to save; `pdf-derived:{driveId}:{speId}` records what that PDF
became. Both best-effort; a miss degrades to the old behavior, never to a failure. **No version id is
stored** — deliberately; the resumed load re-reads the current version, and a creation-time version id
would be read-never and stale. That deviation from the requirement's literal wording is recorded, not
buried.

**FR-A08 was not fully done when it was reported done.** Its first acceptance criterion — enumerate every
creation path and stamp it correctly — was skipped, and PDF-sourced rows were being stamped `Imported`
(measured: `100000001`). The suppression FR-A08 built reads that marker, so **it could not fire for the
class the requirement names first**. Now split: `origin` stays Imported-biased for routing (never forces an
imported doc onto the clean branch — the SEV-1 shape), `originToPersist` is what the document *is*.

---

## What happened to 043 (2026-08-23) — SUPERSEDED

FR-A07 assumes constructs exist the merge cannot safely carry. **There are none.** Owner directed closing
the corpus gap first: four new fixtures now cover OLE objects, chart parts, endnotes and embedded fonts
(five of the six families that had ZERO coverage; macros excluded with reasons). Results:

| | Untouched block | The construct's OWN block edited |
|---|---|---|
| OLE object · chart · endnote · font | **100% strict**, cloned byte-verbatim | dropped, but **NAMED** every time; saved doc schema-valid; package part survives |

Loss is **per-edited-block, never per-document** — so a gate keyed on construct presence would refuse
editing on documents we handle at 100%, a false positive by construction. And **"Edit a copy" has no
trigger to attach to**: every read-only trigger is "we cannot read this at all", and the one genuine
read-but-never-write case (the PDF) already ships.
[`notes/capability-gate-triggers.md`](notes/capability-gate-triggers.md)

---

## Open items carried to task 045

1. **The untested FR-A08 criterion** — *"an Authored document STILL receives save-outcome warnings"* has no
   end-to-end test. Two levers were tried; neither fired through the wire. The property holds structurally
   (every save-outcome warning is constructed after the provenance capture) but that is an argument from the
   code, not evidence from a run.
2. **Browse/local-file PDF door** stamps `Imported` — `/api/compose/project` is contracted stateless and a
   local file has no server identity to key on. First save shows one false warning; the second is correct.
   [`notes/document-creation-paths.md`](notes/document-creation-paths.md) path 4.
3. **Foreign session id on a save** would read another document's PDF marker. A client-contract violation,
   but it is the SEV-1 direction, so it is written down rather than assumed away. Needs a server-side
   binding check at save time if it is to be closed.
4. **A redirected open wastes one PDF download** — detection is bytes-first so the mapping can only be
   consulted after the fetch. The filename pre-check was rejected: it needs a second call site every test
   would leave unexercised.
5. **`ComposeService.cs` is now 4,373 lines** (was 4,031). Track D's file. The PDF-provenance code is one
   self-contained region depending only on `_cache`/`_logger`/`_spe` — extraction is mechanical, same shape
   as `ComposeBlockMerge.cs`.
6. **043 residual — the warning arrives at SAVE, after the edit.** Version history means nothing is
   unrecoverable, but the consent is after the fact. The evidence-supported version of FR-A07 is a warning
   **at the edit**, on the specific block, reusing 044's taxonomy — far smaller than the document gate the
   POML described. Also open: whether Compose should accept `.docm` at all (a product question, not a merge
   one; a `vbaProject.bin` cannot be fixtured as a `.docx`).
7. **`w:br` soft breaks** (1 doc) and **run-level `rPr` variation** (2 docs) on the edited block — read-side
   projection gaps base-carry cannot reach. **`mc:AlternateContent` paraId re-mint** — 2 docs below 100%
   strict (the reverted experiment).

---

## Experiments implemented, measured, and REVERTED (do not repeat)

| Change | Looked right because | Measured |
|---|---|---|
| Exclude opaque regions from `AssignParaIds` | Stops mutating cloned `mc:AlternateContent` subtrees | 2 docs to 100% strict — but breaks task 011's paraId-uniqueness guarantee. **Strict is a ratchet, not a gate**; trading a safety invariant for a non-gating number is what the ADR-049 paired MUST forbids. |
| Emit `xml:space="preserve"` only when needed | Matches what Word "should" do | **Markedly worse**: intact fell 12 → 2. Word emits it far more liberally. |

---

## Traps (all live)

- **`w:pPr` / `w:rPr` are `xsd:sequence`** — child ORDER is schema. Use the order tables in `ComposeBlockMerge`.
- **The I-7 source audit scans source TEXT including comments.** A comment containing the banned membership
  call trips it. Move the code, not the guard.
- **`mergeUnchangedBlocks` is a TEST SEAM, not a feature flag.** Three seam tests are pinned to `false`.
- **A test double that never remembers measures the double, not the behavior** — the promotion's idempotency
  looks rows up by `sprk_graphitemid`; a double answering "no such row" forever reports a false duplicate.
- **Corpus fixtures are held schema-valid.** Never "fix" a real-world fixture — their quirks are the test case.
- **Compose.Components uses Jest, not vitest.**
- **Run the FULL test project before closing a task**, never `--filter`.
- Bash heredocs mangle escapes inside quoted Python — write patch scripts to the scratchpad and run them.
- `git checkout <commit> -- <path>` writes the **INDEX**; the safe A/B form is `git show <commit>:<path> > <path>`.
- `w14:paraId` must be **8 hex digits, non-zero, ≤ `0x7FFFFFFF`**.
- **The pre-commit hook fails on `prettier` not being on PATH** (root install gap) — which is why CI keeps
  landing auto-format commits. `dotnet format <csproj> --include <files>` and
  `npx --no-install prettier --check` from the package dir both work; run them and commit the result.
- **`dotnet format` needs a csproj/sln path** — a bare `--include` throws `MSBuildWorkspaceFinder`.
- SpaarkeAi is Vite and aliases shared-lib SOURCE — clear `dist/ node_modules/.vite/ .vite/` before building.
- Use `pwsh`, not `powershell`, for the deploy hash-verify step.

---

## Owner-visible banners

| Banner | Track | Status |
|---|---|---|
| "Some formatting was simplified when saving" | A | **Should now be gone** for documents that lose nothing, **and for PDF-sourced documents** — but **undeployed** |
| "wording differs slightly from this document" | **C** | Untouched. **051–053**, independent of everything above |

## Not deployed

**Nothing from Phases 3–4 is live.** Task 017's banner fix, the merge, the carry, the taxonomy and 044's
PDF work all ship with the next paired **BFF + `sprk_spaarkeai`** deploy (NFR-05). Never build from a net8 tree.

## Tasks complete

**Phase 0** 001 · 002 — **Phase 1 (Track S)** 010–018 — **Phase 2** 020 · 021 · 022 · 023 —
**Phase 3** 030 · 031 — **Phase 4** 040 · 041 · 042 · **044** (043 superseded) — **Phase 5** 050

**Blocked**: 074 ⛔ (`ComposeShadowPatchEngine` subsumption NOT-CONFIRMED — gate-decision §5).

## Evidence trail

`projects/spaarkeai-compose-r8/notes/` — `gate-contract.md` · `control-measurement.md` ·
`merge-prototype-results.md` · `gate-decision.md` · `merge-mechanism-results.md` · `edited-block-loss.md` ·
`merge-integrity-results.md` · **`pdf-refresh-baseline.md`** · **`document-creation-paths.md`** ·
`adr-049-third-amendment-draft.md` · `track-s-uat.md` · `honest-failure-set.md` · `document-size-ceilings.md`
