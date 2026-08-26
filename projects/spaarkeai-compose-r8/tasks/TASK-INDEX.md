# Task Index — `spaarkeai-compose-r8`

> **Created**: 2026-08-19 · **Re-cut**: 2026-08-20 (decomposed by **file-pass**, not by concern)
> **Status**: INITIALIZED — execution owner-gated
> **36 tasks / 9 phases** · Legend: 🔲 pending · 🔄 needs retry · ✅ complete · ⛔ blocked

**Phase 4 does not start until Phase 3's gate passes.** A miss is an owner escalation (root §6/§6.5).

---

## Decomposition principle (why 36 and not 58)

The **entire Compose spine is `parallel-safe: false`** — `Services/Compose/**`, `Api/ComposeEndpoints.cs`,
`ComposeWorkspace.tsx`, `ComposeEditor.tsx`, `ComposeAiToolbar.tsx`, `usePendingRedline.ts`. Splitting one
file's changes across N tasks therefore means **N sequential passes**: N context loads, N review cycles, N
merge windows on the most contended files in the repo.

Tasks are decomposed by **file-pass**, not by concern. A consolidated task carries the **union** of the
constraints and a **union acceptance-criteria closed set** (incl. negative cases) — density, not dilution.
**No scope was removed in the re-cut.**

Separate "write the tests" tasks are an ADR-038 anti-pattern and were folded into each task's acceptance
criteria — tests are part of the work, not a follow-on.

## Model-tier principle

Capability-matched: the tier that **fully meets** the required capability. Budget is not a constraint —
code quality is the priority. The discriminator is **"does a subtle miss ship silently?"**

- `opus` — any task where correctness is subtle and a miss ships (i.e. most of this project, by its nature).
- `sonnet` — genuinely mechanical work with a clear oracle: verification notes, corpus fixture authoring,
  baseline measurement, deploy mechanics.
- `effort: max` — the gate and the merge mechanism. `xhigh` — brownfield root-cause work. `high` — mechanical.

---

## Phase 0 — Coordination & prerequisites

| # | Task | Rigor | Tier/Effort | ∥ | Deps | Status |
|---|---|---|---|---|---|---|
| 001 | Land/verify **PR #690** (Git-LFS corpus fixtures in CI); confirm fixtures resolve to real bytes | MINIMAL | sonnet/high | ✅ | — | ✅ |
| 002 | Publish-size baseline (vs 44.96 MB) + `/conflict-check` + **PR #266** (OpenXml 3.5.1) sequencing decision | MINIMAL | sonnet/high | ✅ | — | ✅ |

## Phase 1 — Track S: Save reliability · **P0, SHIPS ALONE** (no architecture dependency)

| # | Task | FR | Rigor | Tier/Effort | ∥ | Deps | Status |
|---|---|---|---|---|---|---|---|
| 010 | **Client save-error contract** — route on `ApiError.status`, delete the unreachable `!response.ok` block, rebuild tests on the real thrown path | FR-S01 | FULL | opus/xhigh | ❌ | — | ✅ |
| 011 | **Concurrency** — last-writer-wins + warning (retire the 412 loop) **and** `If-Match` at the storage boundary | FR-S02, A12 | FULL | opus/xhigh | ❌ | 010 | ✅ |
| 012 | **Save lifecycle hardening** — dirty flag survives a failed POST · timeout + `AbortSignal` + in-flight guard · working 423 recovery | FR-S03/04/05 | FULL | opus/xhigh | ❌ | 010 | ✅ |
| 013 | **Save-outcome contract + telemetry** — closed enum on the wire; no 200-with-nothing-written; emit the outcome | FR-S06, S10 | FULL | opus/xhigh | ❌ | — | ✅ |
| 014 | **Engine-side integrity** — re-anchor download failure must never persist the stale baseline *(the ONE Half-A defect in Track S)* | FR-S07 | FULL | opus/xhigh | ❌ | — | ✅ |
| 015 | **Document size ceilings** — route to the existing chunked upload; remove the ~22 MB body ceiling; honest oversize pre-flight | FR-S08 | FULL | opus/xhigh | ❌ | 013 | ✅ |
| 016 | **Honest-failure set** — silent guard drops · name-modal gate · tenant precondition · checkout force-close · promote-after-write · 429 mapping · filesize/filepath refresh · per-document draft slot | FR-S09 | FULL | opus/xhigh | ❌ | 010, 013 | ✅ |
| 018 | **Track S enforcement** — run the Compose client suite in CI as a self-contained gate (the Half-B counterpart to `compose-fidelity-gate`); fix sibling-resolution + non-determinism | — | FULL | opus/xhigh | ❌ | 010 | ✅ |
| 017 | **Track S deploy** (BFF + `sprk_spaarkeai` together) + owner UAT | — | STANDARD | sonnet/high | ❌ | 010–016, **018** | ✅ |

> **✅ Phase 1 CLOSED — owner UAT GO, 2026-08-21.** Save works; zero Track S failure modes observed. The UAT
> surfaced one genuine Track S defect (the save-degradation banner told the user *"the original file is
> unchanged until you save"* **after** the bytes were written) — fixed + regression-tested the same day; ships
> with the next `sprk_spaarkeai` deploy. The two banners the owner still sees are **not** Track S: the
> formatting-simplified banner is **Track A** (Phases 2–4) and *"wording differs slightly"* is **Track C**
> (051–053, startable now — not gated on 031). Evidence: [`notes/track-s-uat.md`](../notes/track-s-uat.md).

> **Ordering note**: 018 runs **before** 017 — the deps column is authoritative, not the number. 018 was added
> 2026-08-20 after task 010 found that `Spaarke.Compose.Components` (88 suites / 786 tests) is not in CI at
> all, which is the mechanism by which a test validating unreachable code passed for three releases. Track S
> ships with a gate on the client save contract, not a promise of one. Owner decision, 2026-08-20.

## Phase 2 — Oracle & corpus (build the measurement BEFORE the fix)

| # | Task | FR | Rigor | Tier/Effort | ∥ | Deps | Status |
|---|---|---|---|---|---|---|---|
| 020 | **The gate contract** — preservation oracle + outcome-honesty assertion + two comparison levels with normalization *(all one harness file)* | FR-G01/02/03 | FULL | opus/max | ❌ | 001, 013 | ✅ |
| 021 | Corpus: the 3 synthetic R4-breakers (`mc:AlternateContent` dup paraIds · interior text boxes · multi-part collisions) | FR-G04 | STANDARD | sonnet/high | ✅ | 001 | ✅ |
| 022 | Corpus: near-tier owner documents (char formatting · court spacing · footnotes · `REF` · content controls) | FR-G04 | STANDARD | sonnet/high | ✅ | 001 | ✅ |
| 023 | **Control measurement** — run the oracle on current master; publish today's real loss numbers | — | STANDARD | opus/high | ❌ | 020–022 | ✅ |

> **✅ 020 CLOSED — the control is published, 2026-08-21.** The gate now measures what SURVIVES, not just
> whether the save crashed. On current master: **6.53% overall block preservation, 2.55% near-tier**, across
> 245 comparable blocks in 10 documents. Every save reports `persisted` and **none of them lies** — Track S's
> outcome contract holds corpus-wide. Three findings for Phase 3: (a) **block counts are stable** (109→109,
> 50→50) so the loss is INSIDE blocks, not structural — exactly the shape "clone the untouched blocks" is
> built for; (b) the loss lands on `pPr/spacing`, `pPr/pStyle`, `pPr/ind`, `r/rPr`, `pPr/tabs` — the owner's
> dev banner, itemised; (c) `AppligentNDA_Signed.docx` **already** carries duplicate `w14:paraId`s, so task
> 021 should check coverage before synthesizing that R4-breaker.
> Evidence: [`notes/gate-contract.md`](../notes/gate-contract.md).

> **✅ PHASE 2 COMPLETE — the control is published, 2026-08-21.** Current master preserves **18.08%** of
> untouched blocks (lenient), **12.18%** strict, and **6.67%** of the near tier, over 18 documents / 271
> blocks. All 18 saves terminate `persisted` with **zero** outcome-honesty violations — Track S holds.
> Task 023's classification pass found and fixed **two oracle artifacts** (one over-reporting by 9 points,
> one under-reporting a dropped footnote reference as 100%), each with a paired test asserting the real loss
> it was masking is still caught. **Thresholds ratified: 100% near-tier / ≥95% overall at LENIENT; strict is
> a no-regression ratchet at 12.18%, not a gate.** The MISS condition is defined in advance — see
> [`notes/control-measurement.md`](../notes/control-measurement.md). **030 may proceed.**

## Phase 3 — Model proof · **THE GATE**

| # | Task | Rigor | Tier/Effort | ∥ | Deps | Status |
|---|---|---|---|---|---|---|
| 030 | **Merge prototype + measurement** — stamp → re-project → per-block compare → clone unchanged; answers spec §5.3; includes heavy-restructure (FR-G06) + N-cycle Word round-trip (FR-G07) | FULL | opus/max | ❌ | 023 | ✅ |
| 031 | **GATE DECISION** + ADR-049 third-amendment draft. *Escalation trigger: a miss goes to the owner — do not improvise* | FULL | opus/max | ❌ | 030 | ✅ |

> **✅ 030 COMPLETE — the prototype clears every gate condition, 2026-08-21.** **18 of 18 documents at 100%
> overall and 100% near-tier preservation**, against a control of 18.08% / 6.67%. The 109-block patent claims
> document goes from preserving ONE block to preserving all 108 untouched blocks. All three R4-breakers pass.
> **No miss condition fired; neither escalation trigger fired.** N=5 round trip shows **zero** cumulative
> drift through paraId regeneration. Cost +2 to +19 ms per save (within NFR-07). Pure `DocumentFormat.OpenXml`
> — no new package. Prototype is **opt-in, default OFF**; no production behaviour changed.
>
> **Read the caveat before celebrating**: the oracle measures UNTOUCHED blocks. The EDITED block is still
> rebuilt from the lossy model, so it still loses its font, size, indentation and numbering — **FR-A04
> property inheritance (041) is what closes that, and 030 does not exercise it.** Reorder also yields no
> merge benefit (degrades to R6, never fails). Full analysis + what 040 must do differently:
> [`notes/merge-prototype-results.md`](../notes/merge-prototype-results.md). **031 has its evidence.**

> **✅ 031 — GATE DECISION: PASS, 2026-08-21.** All five threshold criteria met; neither escalation trigger
> fired. **Phase 4 is AUTHORIZED**, with two conditions: (a) **041 is not optional** — it owns the only
> remaining user-visible loss (the EDITED block still loses its own formatting; the gate measures UNTOUCHED
> blocks by construction); (b) **074 is BLOCKED ⛔** — `ComposeShadowPatchEngine` subsumption is
> **NOT-CONFIRMED**: all three live call sites are on the op-log path the prototype never exercised, so
> FR-D01 keeps one waiver rather than deleting 3,000 lines on "probably". Four Phase-4 POMLs need amending
> (040/041/044/074 — see the decision §6). ADR-049 third amendment is **DRAFTED, awaiting owner sign-off**
> (§6.5 Path B) → **✅ OWNER-ACCEPTED 2026-08-21** (*"ADR-049 is fine."*) → **✅ APPLIED 2026-08-21** at the
> start of task 040. Landed in `.claude/adr/ADR-049-*.md` (concise) + a NEW `docs/adr/ADR-049-*.md` twin
> (full), plus three stale pointer surfaces found in the same sweep: both ADR INDEXes and **root CLAUDE.md
> §17**, all three of which still described R4's surgical byte-patch as the save contract. [`notes/gate-decision.md`](../notes/gate-decision.md) ·
> [`notes/adr-049-third-amendment-draft.md`](../notes/adr-049-third-amendment-draft.md)

## Phase 4 — Track A: Faithful save *(blocked until 031 passes; POMLs provisional — amendable by 031)*

> **✅ 041 / ✅ 042 / ✅ 044 — 2026-08-22..23.**
> **041**: edited block **10 → 12 of 18 intact**. Carries bookmarks (dropping one breaks cross-references
> ELSEWHERE — silent and non-local) and the content-control shell, from the BASE block rather than a client
> payload (four reasons in `notes/edited-block-loss.md`). FR-A06 proved unnecessary as specified.
> **042**: four of five criteria already satisfied STRUCTURALLY by 040; **FR-G05 now RUNS** (headless
> LibreOffice opens four merged documents). Found and fixed the op-log path re-serializing `comments.xml`.
> **044**: the false save banner was on the CLIENT (folding load-time flatten warnings) — no longer folded;
> the silent loss now warns (`edited-paragraph-line-break-dropped ×2`); FR-A08 Authored-vs-Imported scoped by
> provenance. **FR-A09**: measured first, and the diagnosis moved — a PDF's second save after a refresh does
> not merely rebuild, it re-projects the PDF, leaving the user's saved work INVISIBLE in a document they have
> no pointer to while their next save mints a DUPLICATE. Fixed at LOAD (resume on the document that exists),
> which makes save two an ordinary imported save. That also ruled out the cheap save-side dedup, which would
> have traded a visible duplicate for silent data loss. **FR-A08 was NOT fully done when reported**: its
> enumeration criterion was skipped and PDF-sourced rows were being stamped `Imported`, so the suppression
> could not fire for the class the requirement names first — now split into routing vs persisted origin, with
> the enumeration recorded. One criterion (Authored still gets save-outcome warnings) remains untested.
> Beyond scope: 9 schema-invalid corpus fixtures repaired, a missing comment-range fixture added, two
> near-vacuous tests corrected, TWO experiments implemented → measured → reverted, and two silent defects in
> FR-A09's own first cut caught at the Step-9.5 gate.
> [`notes/edited-block-loss.md`](../notes/edited-block-loss.md) ·
> [`notes/merge-integrity-results.md`](../notes/merge-integrity-results.md) ·
> [`notes/pdf-refresh-baseline.md`](../notes/pdf-refresh-baseline.md) ·
> [`notes/document-creation-paths.md`](../notes/document-creation-paths.md)

> **✅ 040 — THE MERGE IS IN PRODUCTION, 2026-08-21.** Gate re-run against the production implementation:
> **100.00% overall / 100% near-tier (lenient)** on 18/18 documents, and **100% STRICT on 16 of 18** — better
> than the prototype, which only had to clear a no-regression ratchet. 253 blocks cloned, 18 rendered. Control
> arm reproduces **18.08% / 6.67%** exactly. Zero hard-fails, zero honesty violations, flat 100% over 5 round
> trips. +3.9 ms/document; publish **43.69 MB** (−1.27 vs baseline); no new NuGet; no new CVE; NetArchTest
> 36/36; full suite **10,792 / 0**. **Five POML reconciliations** — FR-A01 dropped; comparison strips `ParaId`
> (without which the merge scores 100% at the renderer and near 0% through the wire); **LCS alignment** instead
> of document order (positional pairing gives ZERO preservation on insert/delete, which the prototype never
> measured); one shared list-run cursor; basic FR-A04 inheritance done here. **041 still owns** the edited
> block's residual formatting loss and is still not optional.
> [`notes/merge-mechanism-results.md`](../notes/merge-mechanism-results.md)

> **⊘ 043 — SUPERSEDED, 2026-08-23** (owner-directed: close the corpus gap, then decide). The first pass
> found FR-A07's premise unsupported — the gate measured **zero hard-fails**, and the two families the POML's
> own example names ("3 embedded charts, 1 legacy form field") were already in the corpus and already carried.
> But six families had **ZERO coverage**, so "zero hard-fails" said nothing about them. Owner directed closing
> that gap first. Four fixtures now cover five of the six (macros excluded with reasons — a `vbaProject.bin` in
> a `.docx` is invalid by construction). Result: **100% strict preservation** on all four when the construct
> sits in an untouched block (it is cloned byte-verbatim — the merge never parses it), and when the
> construct's OWN block is edited, a **named** warning every time (`complex-object-dropped`,
> `unrepresented-endnote-reference`) with the saved document schema-valid and the package part surviving.
> No hard fail, no silent loss → no gate. **"Edit a copy" also has no trigger to attach to**: every existing
> read-only trigger is "we cannot read this at all", and a copy of that is not editable; the one genuine
> read-but-never-write case (the PDF) already ships. Suite 10,920 → **11,044 / 0**. Residual carried to 045:
> warn at EDIT rather than at save, and `.docm`.
> [`notes/capability-gate-triggers.md`](../notes/capability-gate-triggers.md)

| # | Task | FR | Rigor | Tier/Effort | ∥ | Deps | Status |
|---|---|---|---|---|---|---|---|
| 040 | **The merge mechanism** — server-side re-projection oracle · LCS alignment · block copy-through · property inheritance *(FR-A01 stamper promotion DROPPED — proved unnecessary)* | FR-A02/03/04 | FULL | opus/max | ❌ | 031 | ✅ |
| 041 | **Opaque-atom payload carry** + table/atom identity (write model + `opaqueAtomNode.ts`) | FR-A05/06 | FULL | opus/xhigh | ❌ | 040 | ✅ |
| 042 | **Comment anchors + revision-id seeding under cloning** (dup-paraId consume-in-order, cross-boundary ranges) | FR-A11 | FULL | opus/xhigh | ❌ | 040 | ✅ |
| 043 | **Capability gate** → read-only + "Edit a copy" — **SUPERSEDED** (owner 2026-08-23): no construct family needs a gate; corpus gap closed, 4 fixtures, all 100% strict + named warnings | FR-A07 | FULL | opus/xhigh | ❌ | 040 | ⊘ |
| 044 | **Two document classes** (Authored/Imported; warnings suppressed for Authored) + PDF version-coordinate tracking | FR-A08/09 | FULL | opus/xhigh | ❌ | 040 | ✅ |
| 045 | **Residual loss list published + owner sign-off** + **ADR-049 third amendment merged** (7 invariants) — main-session only¹ | FR-A10 | FULL | opus/xhigh | ❌ | 040–044, **049, 056** | ✅ |
| 046 | **Soft line breaks carried** — `IsLineBreak` marker run (first row retired from the residual list) | FR-A10 residual | FULL | opus/xhigh | ❌ | 045 | ✅ |
| 047 | **Editor node-inventory survey** — found a SECOND loss direction (editor-native content the model cannot name) + the opaque-atom pipeline already exists | — | STANDARD | opus/xhigh | ❌ | 046 | ✅ |
| 048 | **Tabs + symbols carried** — `IsTab` / `Symbol{Font,CharCode}` marker runs on the existing atom node; two more rows retired | FR-A10 residual | FULL | opus/xhigh | ❌ | 047 | ✅ |
| 049 | **Fields carried** — `Field{Instruction,CachedResult}` marker run; per-class carry decision (PAGE/DATE live vs REF/TOC bookmark-dependent); corrects the field row's misleading page-numbers wording | FR-A10 residual | FULL | opus/xhigh | ❌ | 040, 048 | ✅ |
| 057 | **Field carry — CLIENT half** · map a `field` atom back into the posted model in `docxBridge.ts`; without it 049's carry is unreachable from a keystroke edit. A field contributes ZERO characters, so `collectSegments`/`rejectStateText` byte-identity must be re-proven | FR-A10 residual | FULL | opus/xhigh | ❌ | 049 | ✅ |
| 047b | **Never-silent hole** · an edited block with NO base counterpart reports no construct loss (`ComposeBlockMerge.Plan` cannot pair two paragraphs with identical projected text). Found by 056; undermines the guarantee the SIGNED list rests on | FR-A10 | FULL | opus/xhigh | ❌ | 056 | 🔲 |
| 058 | **Nested / conditional merge fields carried** · the shape real templates use (`{ IF { MERGEFIELD x } … }`). Simple MERGEFIELD already carries; nested flattens because the recovered instruction is a CONCATENATION and re-emitting authors a different field | FR-A10 | FULL | opus/xhigh | ❌ | 049, 057 | 🔲 |
| 059 | **SECURITY — `X-Tenant-Id` spoofable fallback** · pre-existing, but 060 promotes it from a 4h cache key to a durable 90-day blob partition key. ENUMERATE callers before modifying; human sign-off required | — | FULL | opus/xhigh | ❌ | 060 | 🔲 |
| 056 | **Objects carried** — opaque `OuterXml` carry on `ComposeFormatChange`'s SDK-parse-gated contract; **relationship survival must be proven empirically** (a carried drawing with a dead `r:embed` = a package Word calls corrupt, worse than today's drop); place-indicator is the evidence-gated fallback | FR-A10 residual | FULL | opus/xhigh | ❌ | 040, 048, 049 | ✅ |

¹ `.claude/` write — sub-agents cannot write these paths (root §3). Main session executes.

> **046–048 are owner-directed follow-ons to 045**, not planned WBS tasks — they exist because the owner
> ruled the residual list's exceptions unacceptable for release: *"we cannot have the Compose editor to just
> 'lose' content; and pushing to r9 is just semantic."* Each retires rows from the published list and is
> enforced by `ComposeResidualLossParityTests` in both directions. Remaining scope + sizing:
> [`notes/zero-loss-scope.md`](../notes/zero-loss-scope.md) (§2c records what building 048 taught).

## Phase 5 — Track C: AI edit placement

| # | Task | FR | Rigor | Tier/Effort | ∥ | Deps | Status |
|---|---|---|---|---|---|---|---|
| 050 | **ADR-043 + ADR-041 assessment** (ADR-043 explicitly names "compose edit"; is FR-C05's "apply anyway?" a Gate?). *Escalation trigger* | — | FULL | opus/xhigh | ✅ | — | ✅ |
| 051 | **Anchor supply** — thread the captured `(paraId, span)` request→response→apply · wire `CitationResolver` · closed-set paraId VALIDATION · the three selection-scoped Actions can now return an anchor *(five dispatch sites, one code path)* | FR-C01/02/03 | FULL | opus/max | ❌ | 050 | ✅ |
| 054 | **Whole-document closed set — SUPPLY** · enumerate the paragraph set for `compose-revise-document` and deliver it via the ADR-043 Amendment 1 declared-input channel; catalog change lands WITH it, never before | FR-C03 | FULL | opus/xhigh | ❌ | 051 | ✅ |
| 055 | **Whole-document — PLACEMENT** · anchored `edits[]` + `comments[]` (the `flag-risks` intent's entire output, today 100% prose-anchored); per-item failure isolation preserved | FR-C03 | FULL | opus/xhigh | ❌ | 054 | ✅ |
| 052 | **DEMOTE the text-search path** — retired as the PRIMARY targeting channel. Server validator + `FindAll` + `target_text`/`match_mode` DELETED (I-7 now enforced by `ComposeEditAnchorPass`'s **signature**, not a comment); catalog stops asking the model for prose locators; `match_mode` retired **in full incl. `all`**. Client matchers KEPT — 3 of their 4 consumers are annotations/decorations, not placement. FR-C05's three outcomes shipped: sub-paragraph **local diff**, stale-target **confirmable**, deleted-target message | FR-C04/05 | FULL | opus/xhigh | ❌ | 051, **054, 055** | ✅ |
| 053b | **Null-identifier edits reach the document** · an explicit `target_para_id: null` used to insert at the CARET and report `applied` (a revised clause could land in the recitals). Now routes into 053's propose-then-confirm path so the change still lands — owner bar: *"whatever ensures the document updates and saves"*. Discriminator is **key PRESENCE** (`hasOwnProperty`), never truthiness; genuine insertion consumers unchanged. Found + fixed a 2nd defect: a null edit rendered as a raw JSON fence in SpaarkeAi | FR-C05/C06 residual | FULL | opus/xhigh | ❌ | 052, 053 | ✅ |
| 064 | **Retired the orphaned edit-batch surface** · `ComposeEditBatch` + `ComposeEditTransaction` + `POST /api/compose/edit-batch/validate` + 7 dead model types. Caught the trap: `CamelCaseStringEnumConverter` lives in the same file and is used by **3 unrelated surfaces**. **No type in `Services/Compose/` can express a character offset any more** — ADR-049 I-7 is now a type-system property. DI 14→12. ⚠️ **`ComposeEditAnchorPass` + `ComposeAnchorResolver` now have ZERO production callers** — surfaced, NOT deleted (owner decision; see below) | — | FULL | opus/xhigh | ❌ | 052 | ✅ |
| 052b | **Stale-target detection made durable** · the hole was the **INFERENCE, not the store**: 052 read "no baseline recorded" as "first materialize", which is also what a different tab / an evicted entry / a disabled store look like — so it re-baselined against a possibly-drifted paragraph and applied silently. **No store choice could fix that.** Fixed by carrying `ComposeDraftProvenance.origin: 'live'\|'replay'` (invariant 7 — carried, never re-derived), giving a 4th case: replay+unrecorded ⇒ ASK (`reason: 'unverifiable'`). Default is **fail-closed** (`?? 'replay'`). Carrier is a fingerprint, not text (ADR-015 Tier-3 stays out of durable storage) | FR-C05 residual | FULL | opus/xhigh | ❌ | 052 | ✅ |
| 053 | **Bounded confirmable fallback** — the anchorless population is REAL (pre-052 `compose` ledger entries, 90-day TTL / indefinite when filed), so the fallback was built. TWO STRUCTURAL bounds: an anchored edit cannot be minted into the argument type (module-private brand), and the module **has no `applied` outcome** — prose can PROPOSE, never PLACE. *"Wording differs slightly"* eliminated: `PendingRedlineError` now carries a required `source`, so the banner names the channel that actually failed. Return-from-Word copy made specific | FR-C06/07 | FULL | opus/xhigh | ❌ | 052 | ✅ |

## Phase 6 — Track B: Durable session files *(the only genuinely parallel track)*

| # | Task | FR | Rigor | Tier/Effort | ∥ | Deps | Status |
|---|---|---|---|---|---|---|---|
| 060 | **Durable byte store** (blob, tenant-partitioned, existing MI RBAC + provisioned container) | FR-B01 | FULL | opus/xhigh | ✅ | — | ✅ |
| 061 | **Lazy re-index** on recall + `SessionFilesCleanupJob` evicts the **hot index only** — scope enforced STRUCTURALLY (`SessionFilesHotIndexAccess` closes the set; the job no longer holds `IServiceProvider`), which matters because **063 adds the delete surface**. Rehydration triggers on EVICTION (a `TopK=1` probe), not on any empty result. ⚠️ 2nd DI registration (spec budgeted 1) — justified, unconditional. Store stays DISABLED | FR-B02/03 | FULL | opus/xhigh | ⚠️ not parallel with 062/063 | 060 | ✅ |
| 062 | **Retention follows session TTL** (incl. `-1` filed = indefinite) + server-authoritative availability | FR-B04/05 | FULL | opus/xhigh | ✅ | 060 | 🔲 |
| 063 | **Erasure deletes the bytes** + tenant-isolation verification (ADR-014/015) | FR-B06 | FULL | opus/xhigh | ✅ | 060 | 🔲 |

## Phase 7 — Track D: God-class removal *(interleave with Phases 4–6; each task deletes its own waiver)*

| # | Task | File (LOC) | Rigor | Tier/Effort | ∥ | Deps | Status |
|---|---|---|---|---|---|---|---|
| 070 | Decompose `ComposeService.cs` + delete its waiver | 3,573 | FULL | opus/xhigh | ❌ | 040 | 🔲 |
| 071 | Decompose `ComposeDocxProjectionBuilder.cs` + delete its waiver | 3,085 | FULL | opus/xhigh | ❌ | 040 | 🔲 |
| 072 | Decompose `ComposeDocumentRenderer.cs` + delete its waiver | 2,304 | FULL | opus/xhigh | ❌ | 040 | 🔲 |
| 073 | Decompose `Api/ComposeEndpoints.cs` + delete its waiver | 2,651 | FULL | opus/xhigh | ❌ | 013 | 🔲 |
| 074 | **Retire `ComposeShadowPatchEngine.cs`** + delete its waiver — confirm at the gate **before** deleting | 2,999 | FULL | opus/max | ❌ | 031, 040 | ⛔ |

## Phase 8 — Wrap-up

| # | Task | Rigor | Tier/Effort | ∥ | Deps | Status |
|---|---|---|---|---|---|---|
| 090 | Anti-clobber deploy · `/test-diet` · write-side fidelity doc · lessons-learned · `projects/INDEX.md` + root §17 update | STANDARD | sonnet/high | ❌ | all | 🔲 |

---

## Critical path

```
001 → 020 → 021/022 → 023 → 030 → 031 (GATE) → 040 → 044 → 045 → 090
```

**Track S (010–017) is off the critical path** — it ships first and independently, and needs no gate.

## Parallel groups

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| P0 | 001, 002 | — | Independent coordination |
| C1 | 021, 022 | 001 | Corpus fixture authoring — different files |
| B1 | 060 → 063 | — | **Track B is the only genuinely parallel-safe track** (touches `Services/Ai/Sessions`, not the Compose spine) |
| C2 | 050 | — | ADR assessment is read-only; runs any time |

Everything else is serial **by necessity**, not oversight — 13+ active worktrees contend on the BFF.

## Goal-eligible waves

**None.** Every wave has a judgment boundary, touches irreversible surface, or is single-task-serial. Root §8.5
requires ≥3 well-specified low-ambiguity parallel tasks; no wave here qualifies.

## High-risk items

| Task | Risk |
|---|---|
| **031** | Gate failure re-opens the architecture. **Escalate; do not improvise.** |
| 040 | The merge mechanism — the project's central bet, in one pass |
| 043 | Capability-gate false positives block documents we could have handled |
| 052 | Retiring the text-search path — verify no consumer outside Compose |
| 074 | Deleting a 3,000-line engine — gate-confirm first |
| 011 | Concurrency semantics reversal on the live save path |

## Re-cut record (2026-08-20)

58 → 36 tasks, **zero scope removed**. Merged: client lifecycle (3→1) · concurrency + If-Match (2→1) ·
outcome + telemetry (2→1) · gate contract (3→1) · merge mechanism (4→1) · anchor supply (3→1) ·
Phase-3 proof (3→2) · coordination (3→2). Removed the standalone test task (ADR-038 anti-pattern — folded
into acceptance criteria). Model tiers re-assigned on capability-match, not budget.
