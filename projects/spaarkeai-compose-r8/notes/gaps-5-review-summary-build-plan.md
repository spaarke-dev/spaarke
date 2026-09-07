# §GAPS-5 — Review Summary: comprehensive approach + build plan

> **Date**: 2026-09-04 · **Status (2026-09-07)**: **Phases 1, 3, 4 BUILT and committed.**
> **Phase 5 is the one open item** — it deletes another project's feature surface, so it waits for
> explicit owner sign-off. See §7 for what shipped and what each decision was resolved as.
> Every claim below is verified against code/schema, not inferred — citations inline.
> **Supersedes** my earlier "one client POST call" sizing, which was wrong (§2.2).

---

## 7. What shipped (2026-09-07) — status of every phase + decision

| Phase | Status | Commit |
|---|---|---|
| **1 — stop dropping the discrete fields** | ✅ Built | `f60657a23` |
| **2 — decide `afterText`** | ✅ Resolved as D2: ship without | (no code, by design) |
| **3 — the write call** | ✅ Built | `4134b7fda` |
| **4 — rename to "Review Summary"** | ✅ Built (ran BEFORE 3, as required) | `117e9d83d` |
| **5 — retire `summaryPage`** | 🔲 **OPEN — needs owner sign-off** | — |

### Decisions as resolved

- **D1 → Option A** (complete `reviewMemo`). Not assumed: the owner's own §0 naming decision
  ("Review Summary") presupposes the feature ships, which excludes Option B. The half of A that
  *deletes* (`summaryPage`, Phase 5) was deliberately NOT taken on that inference — deletion of
  another project's surface is hard to reverse and is the one thing still open.
- **D2 → ship without `afterText`.** The builder never invents one; a test pins that.
- **D3 → relax, don't exclude.** The four grounding fields are optional server-side and render an
  em dash. Findings with no `quotedText` are dropped, and the count is REPORTED to the user.
- **D4 → confirmed.** Phase 4 ran before Phase 3.

### Two things the plan got wrong, corrected during the build

1. **"The rename is free because nothing POSTs" was an inference.** It is now a measurement:
   `sprk_analysisoutput` was queried before the rename and held **zero** rows under the old name
   (the whole table had one row, named "Too Long Didn't Read"). The guard test records that query
   as the recipe to re-run before any future rename.
2. **The plan assumed "Review Summary" was an available name.** It was not: the toolbar already
   had a **"Toggle Review Summary"** control in the same group. Naming the dropdown the same thing
   would have put two identically-named controls side by side — one toggling a view, one acting on
   an artifact. The dropdown is therefore **"Review Summary document"**.

### Scope deliberately NOT taken

- **`sprk_outputtypecode` ("REVMEMO") wiring** — Phase 4 said to pair it with the rename. It was
  not built. Its only benefit is protecting a *future* rename, and it would add a new
  `IAnalysisDataverseService` method; "future flexibility" is exactly what CLAUDE.md §11 rejects as
  justification for new surface. The hazard is handled where it bites instead: the constant
  documents the name/key coupling, `ReviewMemoOutputNameGuardTests` makes a silent change
  impossible, and the contract tests now reference the constant rather than repeating the literal.
- **Phase 5.** See above.

### Not yet exercised against a real environment

Everything above is verified by automated tests (BFF 12,066 · ArchTests 199 · Compose client 1458)
and a control run proving the new end-to-end test fails when the wiring is removed. **No UAT has
been run against spaarkedev1** — the generate → download → email round trip has never executed
against a live Dataverse row, because until this change no such row could exist.

## 0. ⚠️ Naming — read before the word "memo" appears below

**"Memo" is retired as the user-facing name for this feature.** It collides with **`sprk_memo`**, the
first-class entity behind the Notepad — a user-authored scratchpad — and the collision became live on
2026-08-25 when `sprk_agreement` joined `sprk_memo`'s supported parents, so one agreement record can now
carry both a Notepad memo and a "Summary Memo" meaning unrelated things. A user who knows Memos will read
"Create Summary Memo" as *make me one of those, on this record*; it does neither.

**The product name is "Review Summary."** Owner decision, 2026-09-04.

This document nonetheless says "memo" often, in exactly one sense — **naming the code artifacts by their
CURRENT identifiers** (`ReviewMemoAssembler`, `POST /review-memo`, `review-memo-v1`, and today's
`"Create Summary Memo"` toolbar label). Referring to an existing thing by a name it does not have would
make the plan unusable. Two layers, different timelines:

| Layer | Today | Plan |
|---|---|---|
| **User-facing** (toolbar label, tooltip, persisted row display name) | "Create Summary Memo" | → **"Review Summary"** — **Phase 4, before Phase 3** (§5) |
| **Code identifiers** (class, route, schema tag) | `ReviewMemo*`, `/review-memo` | Cosmetic; rename later or never. No user sees them. |

⚠️ The one that is **not** cosmetic is the **persisted row's display name**, because it doubles as the
lookup key (§5 Phase 4). That is why the rename is sequenced BEFORE the write call rather than left to a
tidy-up pass.

---

## 1. What actually exists today — verified, layer by layer

| Layer | State | Evidence |
|---|---|---|
| **Action + schema** | `agreement-review` emits `{overallRisk, flaggedSections[{sectionRef, quotedText, riskLevel, flaggedClause, assessment, standardRef}]}` | `infra/dataverse/outputschemas/agreement-review.schema.json` |
| **SSE → client** | The event **carries** `flaggedClause` / `assessment` | `PaneEventTypes.ts:110` |
| **Comment gutter** | **Consumes** the discrete fields correctly | `advisoryNoteFormatting.ts:147` |
| **Review-summary state** | 🔴 **DROPS** `flaggedClause` + `assessment` in BOTH population paths | `ComposeWorkspace.tsx:3777` (live), `:3395` (ledger restore) |
| **Memo assembler** | Pure, correct, tested | `ReviewMemoAssembler.cs` |
| **POST** `…/review-memo` | Assembles + persists an `sprk_analysisoutput` row | `ReviewMemoEndpoints.cs:122` |
| **POST caller** | 🔴 **NONE in production** — contract tests only | repo-wide `git grep` |
| **GET** `…/review-memo` + `/docx` | Wired to the toolbar; read-from-persisted | `ComposeWorkspace.tsx:3189`, `:3213` |
| **Toolbar** | "Create Summary Memo" dropdown, 4 UAT rounds | `ComposeFormatToolbar.tsx:1035` |

**Net effect**: the toolbar's two actions read a row that nothing writes, so both always hit
`NoMemoProblem()` → the "generate the review/memo first" banner. The feature cannot succeed.

---

## 2. The gaps — there are FOUR, not one

### 2.1 No write call (the known one)
Nothing POSTs. The read half was built against task 050's POST *as though it were already being called*.

### 2.2 🔴 The client's finding shape is PRE-SPLIT — and this is why "add one POST call" was wrong

`agreement-review` was an **FR-05 split** of `nda-review`: per the schema's own description,
*"flaggedClause (grounded fact) + assessment (reasoned judgment) **replace the single explanation blob**
so no consumer string-parses 'Grounded fact —' markers."*

The client's `NdaReviewFindingSummary` still models the **pre-split** shape — `explanation`, and its doc
comment cites `nda-review.schema.json`, **a file that no longer exists**.

| POST requires | Client has |
|---|---|
| `sectionRef` (required) | `sectionRef?` — optional |
| `quotedText` (required) | ✅ `quotedText` |
| `assessment` (required) | ❌ — fused into `explanation` |
| `standardRef` (required) | `standardRef?` — optional |
| `flaggedClause` (required) | ❌ **absent** |
| `riskLevel` (optional) | ✅ |
| `afterText` (optional) | ❌ **absent** (§2.3) |

**The good news: the DATA is already on the wire.** The SSE event carries both discrete fields and the
comment gutter consumes them. Only the review-summary mapping discards them — **five hand-copied fields,
two silently dropped, in two places.**

> ⚠️ **This is the THIRD instance of the drop-in-a-hand-mapping class found in this session** (after
> `revisionReport` and `Style`), and the first one that is client→client rather than crossing HTTP. The
> guard built this session (`InboundBodyDtoMappingGuardTests`) covers **inbound HTTP DTOs** and cannot see
> it. Worth noting as evidence the class is broader than the transport boundary.

### 2.3 `afterText` has no source — but it is NOT a blocker

The POST's one field *"the server cannot derive itself"*. Per-finding accept/reject is **not tracked
durably anywhere**: `AnchoredAnnotation` has no disposition field, and `ComposeCommentThreadModel.resolved`
is explicitly UI-only and never written to native `w:comment` on save. Confirmed — "disposition" elsewhere
in this codebase means the *AI action* disposition (compose/analyze), not per-finding accept/reject.

**Why it is not a blocker**: `ReviewMemoAssembler` **already** defaults `after` → `before` when `afterText`
is absent, and its remarks call that semantically correct —

> *"Its absence represents EITHER a rejected suggestion OR a section the reviewer never acted on — both
> converge on the identical observable fact that the original text is what stands in the final document."*

So a Review Summary assembled with no `afterText` at all is **correct by the existing design**, not degraded. See
§4 for what it does and does not then claim.

### 2.4 Nullability mismatch (small, but decide it)
`sectionRef` and `standardRef` are `required` server-side but **optional** client-side — the panel's own
comment says *"the rest are optional so a partially-grounded finding never crashes the panel."* A finding
missing either **cannot be sent** as the contract stands. Needs a rule (§6, D3).

---

## 3. Options

| | Scope | Ends with |
|---|---|---|
| **A. Complete `reviewMemo`, retire `summaryPage`** ⭐ | §5 phases 1–5 | One working path; the redundant one gone |
| **B. Retire both** | Delete memo endpoints + client + `summaryPage` + generator + seam tests | No NDA summary capability |
| **C. Wire `summaryPage`, retire memo** | DTO + mapping + a client sender, mirroring `revisionReport`; delete the memo stack | In-document appendix instead of a separate memo |

**Recommend A.** Everything except one call and one mapping fix already exists and has been through UAT;
B discards a nearly-complete, UAT'd feature; C throws away more working code than it saves and re-opens a
design (appendix vs separate document) the owner already had built both ways.

---

## 4. What the shipped Review Summary will and will not say (be honest up front)

**Will**: for each flagged section — location, the verbatim clause (`before`), what the clause says
(`flaggedClause`), why it was flagged (`assessment`), the firm standard it was measured against
(`standardRef`), risk level; plus overall risk. Downloadable `.docx`, and an email prefill.

**Will not (Phase 1)**: distinguish *accepted* from *rejected/untouched* — every row shows `after` =
`before` until §2.3 disposition tracking exists.

**This is a coherent product, not a stub**, because it pairs with what R8 already shipped:

| Artifact | Question it answers |
|---|---|
| **Revision report** (R8, shipped) | *What changed in this document?* |
| **Review Summary** (this plan) | *What did the review find?* |

They are complementary. Do not merge them.

---

## 5. Build plan

### Phase 1 — Stop dropping the discrete fields ⭐ *do this regardless of the §6 decision*
- Widen `NdaReviewFindingSummary` with `flaggedClause?` + `assessment?`.
- Carry them through **both** mappings (`ComposeWorkspace.tsx:3777` live, `:3395` ledger restore).
- Point the type's doc comment at `agreement-review.schema.json` (it cites a deleted file).
- Test: a finding with discrete fields survives BOTH paths — the regression guard for §2.2.

**Independently valuable**: this is live data loss today. The panel could already render a grounded fact
separately from the judgment and cannot, because the fields are discarded on the way in.
**Size: small.** No server change, no contract change.

### Phase 2 — Decide `afterText` (§6 D2). Recommended: ship without.
No code if deferred. The assembler already handles absence correctly.

### Phase 3 — The write call
- Add a **"Generate"** action to the dropdown (today labelled "Create Summary Memo"; Phase 4 renames it). It currently has only the two READ actions.
- Build `GenerateReviewMemoRequest` from `reviewSummaryFindings` + `reviewSummaryOverallRisk`.
- POST, then re-read. Gate on `hasReviewFindingsRef` — the existing "no findings" signal.
- Reuse the `useComposeChangeSummary` outcome-union shape from R8 item 8 (`needs-save` / `no-changes` /
  `dispatched` / `failed`) rather than inventing a second error surface.
- Tests: happy path persists + re-read renders; **empty findings must refuse** (mirrors the R8 rule that a
  summary of nothing is itself the defect); a finding missing `sectionRef`/`standardRef` follows D3.

**Size: moderate.** One fetch, one payload builder, one menu item, tests.

### Phase 4 — Rename to "Review Summary" (§0), paired with the categorisation fix
- Toolbar label/tooltip + the persisted row's display name.
- ⚠️ **Must be paired**: `PersistReviewMemoAsync` sets `OutputTypeId = null` deliberately (env-specific
  GUID), so the row is found by **matching its display name**. Renaming alone orphans rows silently — the
  read returns "no memo", indistinguishable from "not generated". Wire `sprk_outputtypecode` (`"REVMEMO"`),
  which the code's own comment already names as the intended follow-up.
- **Free only while no rows exist** — which is true today precisely because of this gap. Doing the rename
  *after* Phase 3 means migrating rows.
- ⇒ **Order matters: Phase 4 BEFORE Phase 3**, or accept a migration.

### Phase 5 — Retire `summaryPage`
Delete `SaveComposeDocumentRequest.SummaryPage`, `ComposeSummaryPageGenerator`, its seam test, and the
`SaveAsync` call site. Delete `ComposeSaveBodyMappingGuardTests.SummaryPage_IsStillTheOpenInstanceOfThisDefectClass`
— its purpose is to stop the gap being forgotten, not to preserve it.

**Size: small**, but it is another project's feature surface — hence D1.

---

## 6. Decisions required

| # | Decision | Recommendation |
|---|---|---|
| **D1** | Option **A** (complete + retire `summaryPage`), **B** (retire both), or **C** (wire `summaryPage` instead)? | **A** |
| **D2** | Ship Phase 1 **without** `afterText`, or block on disposition tracking? | **Ship without.** The assembler already treats absence as correct, and R8's revision report already answers "what changed". |
| **D3** | Findings missing `sectionRef`/`standardRef`: relax the server contract to optional and render "—", or exclude them from the memo? | **Relax to optional.** Silently excluding findings is the exact failure class this whole session has been about. If excluded instead, the count MUST be surfaced. |
| **D4** | Confirm the product name is **"Review Summary"** (§0 — "memo" retired; it collides with `sprk_memo`/Notepad) and that Phase 4 runs **before** Phase 3. | Yes to both. Code identifiers stay `ReviewMemo*` — cosmetic, no user sees them. |

**Phase 1 is safe to start now under any answer to D1** — it fixes live data loss and is a prerequisite
for A, harmless under B/C.
