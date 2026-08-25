# Task 052 — demoting the text-search placement path: consumer check + decisions

> **Task**: `052-retire-text-search-path.poml` · **Rigor**: FULL · opus @ xhigh · **Date**: 2026-08-25
> **Scope authority**: the owner's 2026-08-24 reframing constraint in the POML — this task **DEMOTES**
> text matching, it does not eliminate the capability. Three roles survive; one is retired.
> **Blocks**: task 053 (bounded confirmable fallback).

---

## 0. Executive summary

| # | Finding | Consequence for this task |
|---|---|---|
| **F-1** | **The whole SERVER edit-validation surface is dark machinery.** `POST /api/compose/edit-batch/validate` has **zero** client callers — repo-wide grep for `edit-batch` returns no `.ts`/`.tsx` hit. Real placement happens client-side in `usePendingRedline`. | The server deletion cannot break production. It also means task 051's `ComposeEditAnchorPass` is unreachable from production — recorded, not fixed here. |
| **F-2** | **`resolveTargetSpans` has FOUR consumers, only ONE of which is AI-edit placement.** Deleting it would break three shipped features. | The client symbols on the DELETE list **survive as a module** and are removed **from the edit path only**. This is the escalation trigger firing, pre-answered by the owner's reframe. |
| **F-3** | **The real demotion is a CATALOG change, not a code deletion.** After 051, the client already resolves `anchored ?? textSearch` in that fixed order. Text search stays reachable only while the model is still *asked* for `target_text`. | Retire `target_text`/`match_mode` from the four compose Action output schemas. That is what makes the text leg reachable only by **legacy/replayed** ledger entries — exactly task 053's bounded case. |
| **F-4** | **An anchored edit replaces the ENTIRE paragraph today.** `resolveAnchoredSpans` returns `{from: block.from + 1, to: block.to - 1}`. | FR-C05's "sub-paragraph edit → local diff within the known paragraph" is a real, currently-missing behavior — the substantive new work in this task. |
| **F-5** | **A stale target is not detected at all.** The paraId resolves, the paragraph's content changed since the suggestion, and the model's `new_text` silently overwrites the user's newer edit. | FR-C05's "apply anyway?" is closing a **silent data-loss** path, not adding a nicety. |

---

## 1. Consumer check — recorded per symbol (POML constraint: "record the consumer check per deleted symbol")

Repo-wide, `src/` + `tests/`, not just Compose.

### 1.1 Server — DELETE confirmed

| Symbol | Consumers found | Verdict |
|---|---|---|
| `IComposeEditValidator` | `ComposeEndpoints.ValidateEditBatch` (param) · `ComposeModule.cs:28` (DI) · `ComposeEditAnchorPass.Validate` (param) · 4 test files | **DELETE** — every consumer is inside the retired mechanism or its tests. |
| `ComposeEditValidator` | as above + `ComposeEditBatchTests` / `ComposeEditTransactionTests` (construct it to produce verdicts) | **DELETE**; the two batch tests hand-build `BatchValidationResult` instead. |
| `FindAll` | private to `ComposeEditValidator` | **DELETE** with its owner. |
| `ProposedEdit.TargetText` | `ComposeEditValidator.ValidateOne` · the four catalog output schemas · client payloads | **DELETE from the server record**; the catalog stops emitting it (§3). |
| `MatchMode` / `match_mode` | `ProposedEdit.Mode` · `ComposeEditValidator.ValidateOne` switch · four catalog schemas · client `normalizeMatchMode` | **DELETE** — see the explicit `all` decision in §2. |

**No consumer outside the Compose AI edit path.** The escalation trigger does **not** fire for the server set.

### 1.2 Server — KEEP confirmed (POML: "their consumers still work")

| Symbol | Why it survives | Verified |
|---|---|---|
| `ComposeTextFold` | Used by the baseline stamper the entire Track A merge depends on. | Distinct file, distinct consumers; untouched by this task. |
| `AnnotationReanchorService` | The ADR-sanctioned return-from-Word fuzzy case. | Untouched; task 053 changes only the message it surfaces. |

### 1.3 Client — the DELETE list does NOT survive contact with the consumer check

`resolveTargetSpans` (and its privates `findTargetMatches` / `MATCH_FOLD` / `collapseWhitespaceIndex` /
`buildCharIndex`, all in `hooks/redlineTextSearch.ts`) has **four** consumers:

| # | Consumer | What it does | Is it AI-edit placement? |
|---|---|---|---|
| 1 | `usePendingRedline` (`:564`, `:714`) | Places an AI **edit** (strike + insert). | **YES — this is the one being demoted.** |
| 2 | `ComposeEditor.resolveAdvisoryAnchorSpan` (`:1904`, `:1914`, `:2783`) | Places an NDA/agreement advisory **comment** on `quotedText`. Already anchor-first via `resolveDeterministicAnchorSpan`. | No — an annotation, not an edit. |
| 3 | `useDocQaHighlight` | Ephemeral **view decoration** for a Doc Q&A citation (FR-35). Never mutates the doc. | No. |
| 4 | `useComposeWorkspaceReceivers` (batch note tool) | Creates a **comment thread** on a quoted span. | No. |

Deleting the symbols would break three shipped features. That is the POML's escalation trigger —
*"a symbol on the DELETE list has a consumer outside the Compose AI edit path"* — and the owner's
2026-08-24 reframe answers it in advance: the capability survives in its legitimate roles; what is
retired is text matching **as the primary targeting channel for an AI edit that had a deterministic
anchor available and discarded it**.

**Decision: `redlineTextSearch.ts` is KEPT intact and removed from the AI-edit path only.** The module
boundary task 055 created for the tripwire test is what makes "removed from the edit path" a
structurally checkable claim rather than a comment.

`useComposeFindReplace` (surviving role 3 — user-invoked find/replace) was checked and is **already
independent**: it carries its own character scan and never imported `resolveTargetSpans`. No action.

### 1.4 Newly-orphaned by this task — surfaced, NOT deleted (outside the POML's list)

`ComposeEditBatch` + `ComposeEditTransaction` are the **text-offset APPLY half** of the same R2
mechanism: they apply `ResolvedMatch(Offset, Length)` spans into a plaintext string. After this task
their only producer is gone and every anchored verdict carries an **empty** `Matches`, so they can
never apply anything. They have **no production consumer** (DI-registered in `ComposeModule.cs:29-30`,
injected nowhere).

They do **not** violate ADR-049 I-7 — they apply spans, they do not search — so they are not required
to go with the validator, and deleting ~500 lines outside the POML's DELETE list would be scope
expansion (root CLAUDE.md §6). **Recommendation: retire both, with `ComposeEndpoints`'
`/edit-batch/validate` and the models that serve only them, as a follow-up alongside task 074's
gate-confirmed deletion.** Recorded here so the decision is one line of owner input, not a
re-investigation.

---

## 2. `match_mode: 'all'` — the explicit decision the owner required

> POML constraint (owner, 2026-08-24): *"DECIDE `match_mode: 'all'` EXPLICITLY, not by omission …
> State the decision and its reasoning in the task notes either way."*

**Decision: `match_mode` is RETIRED in full, including `all`. The defined-term / global-replacement use
case is served by user-invoked find/replace, not by an AI edit.**

### The case for keeping it
A defined-term change — *"every instance of 'Company' becomes 'Supplier'"* — is genuinely
text-semantic. Under a paraId-only model the model must enumerate every affected paragraph: more
explicit and checkable, but it costs tokens and it can miss one.

### Why it goes anyway

1. **The failure modes are not symmetric.** A missed paragraph under enumeration is a **visible**
   under-application: the user sees "Company" still there and re-runs. A wrong occurrence under
   `all` is an **invisible** over-application: it rewrites "Company" inside a defined-terms
   definition, a party block, or a quoted third-party clause the user never intended to touch — and
   it presents as success. R8's charter is that a silent wrong edit is the one outcome we do not
   ship.
2. **The capability already exists, better, one layer down.** `useComposeFindReplace` (FR-17) does
   exactly this: it shows the user the **match count**, highlights every hit, and requires an
   explicit Replace-All. That is the same operation with the confirmation FR-C06 demands, and it is
   already shipped. Building a second global-replace channel through the AI edit payload violates
   CLAUDE.md §11 (cost-of-doing-nothing cannot be articulated — the user is not blocked).
3. **`all` is structurally incompatible with the anchor model.** `ResolvedMatch` fans one edit out to
   N spans; an anchored edit resolves to exactly ONE paragraph. Keeping `all` would mean keeping the
   entire span-fan-out apply path (`ComposeEditBatch`) alive purely to serve it — the back door
   task 053 is written to prevent.
4. **Token cost is the wrong axis.** Enumeration costs tokens; a mis-applied edit in a legal document
   costs a redraft and trust. The owner's stated bar for Track C is *"MUST be completely addressed"*.

### What replaces it
- **Per-paragraph enumeration** for anything the model should change — task 054/055 already deliver
  the whole-document closed paraId set that makes this possible.
- **User-invoked find/replace** for genuine defined-term sweeps, where matching text IS the
  semantics and the user sees the count before committing.

---

## 3. What actually changes (the demotion, in three layers)

| Layer | Change | Why here |
|---|---|---|
| **Catalog (the real demotion)** | Four compose Action output schemas stop asking the model for `target_text` / `match_mode`; `target_para_id` is the targeting field. | This is what makes the client's text leg reachable **only** by replayed/legacy ledger entries — i.e. exactly task 053's bounded case. A catalog **DATA** change is what ADR-039 wants (assessment C-7). |
| **Server** | Delete the validator, the interface, `FindAll`, `TargetText`, `MatchMode`; `ComposeEditAnchorPass` loses its legacy leg and its `textValidator` parameter; un-anchored ⇒ a deterministic `NoAnchor` refusal. | Removes the mechanism ADR-049 I-7 forbids. Safe: F-1 (no client caller). |
| **Client** | `redlineTextSearch.ts` KEPT (F-2). Removed from `usePendingRedline`'s primary path; FR-C05's three deterministic outcomes implemented. | Where production behavior actually changes. |

---

## 4. FR-C05 — the three deterministic outcomes, as found in code

| Outcome | State BEFORE this task | Required behavior |
|---|---|---|
| **Sub-paragraph edit** | `resolveAnchoredSpans` returns the **whole paragraph** (`block.from + 1 … block.to - 1`), so a three-word change strikes and replaces all 40 lines of a clause. | Diff **locally within the known paragraph** — we know which paragraph; the only question is where inside it. Must NOT widen to a document-scoped search (POML constraint). |
| **Stale target** | **Not detected.** The paraId resolves, the paragraph changed since the suggestion, and `new_text` silently overwrites the user's newer edit. | *"This clause changed since the suggestion — apply anyway?"* — **not** an ADR-041 Gate (task 050 §4.2), but ledger-durable per O-1…O-6 so a refresh cannot re-ask. |
| **Deleted target** | Handled as a bare `not_found` (`if (!block)`), sharing generic copy with an unresolvable citation. | Its own message: *"the text this suggestion referred to no longer exists."* |

**ADR-041 disposition (task 050, cited as the POML requires):** FR-C05's confirmation is **NOT** a Gate
— *"no `PendingPlanManager.SuspendInvocationAsync`, no `SessionGate` entry, no `gateId` on the compose
path"* (O-1). The obligation that IS live is **O-2 ledger-durable resolution**: `React.useState` /
refs / `sessionStorage` do not satisfy it, because `lastMaterializedKey` is the demonstrated
counter-example — answer "apply anyway", refresh, and the reopen pass at `ComposeWorkspace.tsx:2969`
re-materializes and asks again. Resolution reuses the shipped **FR-17 supersession leg** (O-3); it is
append-only (O-4), idempotent on re-materialize (O-5), and rendered with `ConfirmModal` (O-6).

---

## 5. ADR disposition

| ADR | Rule | Path |
|---|---|---|
| **ADR-049 I-7** | No text search as a placement mechanism. | **C — comply.** After this task no *placement* path searches document text. The surviving consumers (§1.3 rows 2–4) place annotations and decorations, not placements of an edit. |
| **ADR-041** | Confirmation policy. | **C — comply**, per task 050 §4.4 O-1…O-6. Not a Gate. |
| **ADR-039 / ADR-043** | No new dispatch protocol; catalog changes are DATA. | **C — comply.** §3's catalog layer is a `sprk_outputschema` data change (assessment C-7). |
| **ADR-010** | DI symmetry. | **C — comply.** `IComposeEditValidator`'s registration was unconditional (`ComposeModule.cs:28`, not inside an `if (flag)`), so removing it leaves no asymmetry (bff-extensions.md § F.1). |
| **ADR-050 / ADR-021** | Modal shell + semantic tokens. | **C — comply.** The stale-target confirmation uses `ConfirmModal`. |

**No §6.5 escalation is raised.** The one trigger that fired (§1.3, a DELETE-list symbol with consumers
outside the edit path) is resolved by the owner's own 2026-08-24 reframing constraint, which is the
governing scope statement for this task.

---

## 6. Verification (main session, independently re-run — not accepted on agent report)

Both halves were executed by parallel sub-agents (server / client+catalog — file- and toolchain-disjoint)
and every claim below was re-run in the main session.

| Check | Result |
|---|---|
| BFF build | clean, 0 errors |
| `Sprk.Bff.Api.Tests` | **11,285 passed / 0 failed / 97 skipped** (baseline 11,295 — Δ−10 = −12 deleted validator tests, +2 new) |
| `Spaarke.ArchTests` | **56 / 56** |
| `Sprk.Bff.Api.IntegrationTests` | **96 passed / 6 skipped** |
| Golden-utterance eval suite | **40 / 40** after the fixture fix (§6.2) |
| `Spaarke.Compose.Components` | **100 suites / 1,234 tests** (baseline 99 / 1,212 — +1 suite, +22 tests) |
| `SpaarkeAi` | **121 suites / 1,119 tests** |
| Catalog JSON | all 8 files re-parse; 0 invalid across `infra/dataverse/{actions,outputschemas}` |
| KEEP files unmodified | `ComposeTextFold.cs`, `AnnotationReanchorService.cs`, `redlineTextSearch.ts`, `docxBridge.ts` — `git diff --stat` empty on all four |
| Publish size | **43.73 MB** compressed incl. PDBs (215 files, 4 `.pdb`, raw 137.41 MB) — **−1.23 MB** vs the 44.96 MB net10 baseline; 16.27 MB under the 60 MB ceiling |
| CVE / NuGet | no vulnerable packages; no new NuGet |

### 6.1 Publish-size measurement — a recorded number was WRONG, and is corrected

The server agent reported **45.03 MB** and, on the strength of the 45.00 MB figure in
`notes/track-b-placement-justification.md`, argued the 43.7x cluster in the older notes was a stale tree
state. **Both 45.xx figures are measurement artifacts.** An independent re-measure produced **43.73 MB,
reproducibly, twice**, with the raw directory sum (**137.41 MB**) and file count (**215**) *byte-identical*
to the run that reported 45.03 MB. Identical content cannot compress to two different sizes.

Suspected cause: a publish output directory that was not emptied first. **The method that reproduces**:

```
rm -rf <out>                                     # FIRST — this is the step that matters
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o <out>
Compress-Archive -Path '<out>\*' -CompressionLevel Optimal
```

**Always report the raw directory sum (~137 MB) next to the compressed figure.** It is the invariant that
makes an inflated zip visible immediately — as it did here. `track-b-placement-justification.md`'s row has
been corrected in place with the same note.

### 6.2 What verification caught that neither agent's report did

- **`golden-utterances.json` was left stale.** The server agent fixed `GoldenUtteranceEvalSuiteTests.cs`;
  the client agent flagged the file as out of its boundary — but its flag was *already stale*, and neither
  addressed the **fixture**, which still documented `{target_text, new_text, match_mode:'strict'}` as the
  live payload shape and carried a whole case (GU-095) for *"every occurrence of a defined term
  (match_mode: all)"* — the capability §2 deliberately retired. Fixed: GU-093/094/095 rewritten to the
  anchored model, with GU-095 kept as the case that now **asserts the boundary** (the utterance stays
  valid and still dispatches; what changed is the payload, and a genuine document-wide sweep routes to
  user-invoked find/replace). This is the third stale-comment defect this project has caught in a
  fixture/comment rather than in code — the pattern is now well established enough to look for by default.
- **The publish-size correction above**, including a wrong number already committed to a project note.

### 6.3 Residual gap — filed, not deferred

FR-C05's stale-target **answer** is ledger-durable (FR-17 supersession, O-2 satisfied). Its **question** is
not: the capture-time paragraph text lives in `sessionStorage`, so a cross-tab reopen, eviction past 200
entries, or a storage-disabled environment silently restores the pre-052 overwrite. "No prompt" reads as
safe and is not — it *is* the pre-052 behavior. Filed as **task 052b**
(`tasks/052b-stale-detection-durability.poml`), detection-only, with the ADR-040 128 KB cap and the
"never revive `target_text`" constraint written into it.

### 6.4 Scope decisions taken during execution (each surfaced, none silent)

1. **`ComposeEditAnchorPass.Validate` lost its `documentText` parameter** — beyond the literal DELETE list.
   That parameter existed solely to feed the deleted leg. Removing it makes ADR-049 I-7 a property of the
   **type system** (no prose in scope to search, no collaborator to search it with) rather than of a
   comment, and let the throwing test double be replaced by a reflection assertion that the signature
   carries no `string` parameter — a stronger guarantee than a fake covering only exercised paths.
   Endpoint wire contract unchanged.
2. **`comments[]` on `compose-revise-document` KEEPS `target_text`.** A review flag is an annotation
   anchor feeding `AnnotationReanchorService` — a KEEP item. Removing it would have starved a KEEP item's
   input. An edit is a *placement* (I-7 applies); a comment is an *annotation* (it does not).
3. **The legacy branch left for task 053** is `resolveLegacyReplayedSpans`, **pinned to `strict`** and
   ignoring any stored `match_mode`. `first` picks an occurrence when several match — the UAT-21 failure;
   `strict` refuses instead of guessing, so pinning can only convert a would-be guess into an honest
   refusal. A strict reduction in reach, never an increase.
4. **C-1's unlocalizable case**: a `new_text` containing a line break spans paragraphs and cannot be
   spliced into one. Defined outcome = whole-paragraph replacement **of the anchored paragraph only**
   (pre-052 behavior, one paragraph, no widened search).
5. **Three selection-scoped `.schema.json` mirrors had never received task 051's `target_para_id`** — real
   drift found while editing, re-projected from their action seeds.
6. **`ComposeEditBatch` / `ComposeEditTransaction` kept** per §1.4, each carrying a status note recording
   that they now have no production caller and that retirement is the owner's call.
