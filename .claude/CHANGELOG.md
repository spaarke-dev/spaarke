# Procedure-Surface Changelog

> **Forward-only from 2026-05-14.** No back-fill from history.

This file tracks changes to the agent-procedure surface — `.claude/skills/`, `.claude/agents/`, `.claude/settings.json`, `.claude/patterns/`, `.claude/constraints/`, `.claude/FAILURE-MODES.md`, and the root `CLAUDE.md`. Git history covers everything; this file is the **curated** view that a human (or future agent) can scan to answer "when did skill X change?" or "when did hooks last get fixed?" without bisecting commits.

Format follows [Keep a Changelog](https://keepachangelog.com/) conventions.

---

###### 2026-09-01 — `email-communication-intelligence-r2`: document-profiling failure mode + the 3 AI execution models documented

- **New architecture doc** [`docs/architecture/DOCUMENT-PROFILE-AND-AI-EXECUTION-MODELS.md`](../docs/architecture/DOCUMENT-PROFILE-AND-AI-EXECUTION-MODELS.md) —
  authoritative map of the three ways the BFF runs AI (node playbook · direct Action/linear ADR-043 · legacy sequential),
  the three divergent document-profile entry points (wizard + Compose = direct Action, Outlook/app-only = node playbook),
  the confirmed failure mechanism (Part 4), fix options, and a change-safety checklist. GitHub #919.
- **New `FAILURE-MODES.md` AP-10** — a single-level JSON-aware renderer over a double-nested, re-parsed config. The
  Layer-1 `RenderConfigJsonStructurally` escapes only the outer wrapper; `UpdateRecordNodeExecutor.ParseConfig` re-parses
  the nested `configJson`-as-a-string and throws `0x0A invalid at $.fieldMappings[0].value`. **Corrects the prior
  checkpoint hypothesis** ("falls back to flat at `:2284`" — the fallback never fires; the outer wrapper is valid JSON).
  Root cause settled by pulling the **live** node config from Dataverse, not by forward-reasoning from the renderer.
- **Root `CLAUDE.md` §17** gained a pointer row to the new doc (read-before-changing-the-file/Document-create-pipeline).
- **Not a fix** — this entry is investigation + documentation only; the production renderer is unchanged pending the
  owner's choice among the three fix options in Part 4.

###### 2026-08-31 — `email-communication-intelligence-r2`: infinite lazy-scroll is the standard for scrollable lists

- **New ADR-051** ([`.claude/adr/ADR-051-infinite-scroll-lists.md`](adr/ADR-051-infinite-scroll-lists.md)) — every scrollable
  list uses **infinite lazy-scroll + the canonical thin scrollbar**, **never a pager** (no numbered pages, prev/next,
  "Load more", or down-arrow/chevron next-page control). `<DataGrid>` is the standard impl. Strengthens ADR-021,
  composes under ADR-012. Added to [`adr/INDEX.md`](adr/INDEX.md).
- **New pattern** [`patterns/ui/infinite-scroll-list.md`](patterns/ui/infinite-scroll-list.md) — the how-to: reuse
  `<DataGrid>` (built-in `useLazyLoad` + sentinel `IntersectionObserver`); the **page-fullness `hasMore` fallback**
  (why MDA `Xrm.WebApi` grids silently capped at 25 — the platform strips `@…morerecords`/paging-cookie on FetchXML);
  the custom-scroller recipe; explicit **DO NOT** bans. Registered in [`patterns/ui/INDEX.md`](patterns/ui/INDEX.md).
- **`patterns/ui/thin-scrollbar.md` updated** — the DataGrid `gridScroll` inline drift it had flagged
  (`colorNeutralStroke2` / 4px) was converged onto the canonical `thinScrollbarStyle`; cross-linked to the new list
  pattern.
- **Shared-lib doc** `src/client/shared/CLAUDE.md` gained a "Scrollable Lists — Infinite Lazy-Scroll (ADR-051)" section.
- **Code (context)**: `useLazyLoad` `hasMore` now `moreRecords === true || page-was-full`; DataGrid `gridScroll` uses
  `thinScrollbarStyle`; reconciliation grid pages at 50. Test: `DataGrid/__tests__/useLazyLoad.hasMore.test.ts`.

###### 2026-08-25 — `spaarkeai-compose-r8` task 056: embedded objects carried through an edited paragraph

- **ADR-049 residual list**: the `complex-object-dropped` row moves **§2 (lost) → §3 (carried)**. Images,
  charts, shapes and OLE embeds now survive an edit to their own paragraph. A **text box** keeps the row
  (its words are already preserved as prose; carrying the box too would duplicate the sentence) — the new
  `pictTextBox` parity family keeps the warning code honest, exactly as `fldNested` does for fields.
- **Empirically settled**: the save's body swap does NOT prune main-part relationships. Verified by OPENING
  the saved package and resolving every `r:*` attribute, not by reading the renderer's "orphaned … inert
  weight" remark — now corrected in place. **Second stale-comment correction in this project**, after task
  049's bookmark claim. Evidence: `projects/spaarkeai-compose-r8/notes/056-object-carry-decisions.md` §1.
- **One opaque-carry mechanism, two consumers**: `TryParsePreviousProperties<T>` renamed
  `TryParseOpaqueCarry<T>`. No second contract (CLAUDE.md §11).
- **New gate — parsing is not sufficient for this construct.** Every attribute in the OOXML relationships
  namespace must RESOLVE against the carrier before a subtree is authored: a valid drawing naming a missing
  relationship would produce a file Word reports as damaged, which is worse than the drop it replaces.
- **ADR-049 I-2 unchanged** — no OOXML crosses the wire. A browser keystroke edit keeps its image because
  `ComposeBlockMerge.CarryUnmodeledConstructs` (the task-041 base carry already used for bookmarks and SDT
  shells) restores it from the block's pre-edit base.
- **Corrects task 057's `data-atom-display` fix**, which did not reach the `object` family: the attribute
  was re-emitted only when display text was TRUTHY, and the server emits an `object` atom EMPTY — so the
  placeholder label still leaked (`Object` → `Object: Object` → …) across `getHTML()` round trips. Opaque
  atoms now always emit the attribute, empty when absent; renderable atoms (tab/symbol) untouched.
- **Owner sign-off unblocked**: both rows the owner declined on 2026-08-25 are closed (fields 049/057,
  objects 056). Residual §2 is now nested/unterminated fields, text boxes, footnote refs, endnote refs,
  content controls.

## 2026-08-25 — Compose write fidelity: the CLIENT half of the field carry (task 057, `spaarkeai-compose-r8`)

- Task 049's Word-field carry was **unreachable from a keystroke edit**: `docxBridge.ts` never mapped a
  `field` atom into the posted model, and `composeInlineAtom` did not DECLARE the `data-field-*` payload,
  so ProseMirror dropped it at parse. Both closed. A producer with no consumer — this project's recurring
  failure with the polarity reversed.
- **A field is the first segment present in the run stream and ABSENT from the text coordinate space.** A
  tab or symbol contributes one character, which is what kept task 048's walk byte-identical to
  `rejectStateText`; a field contributes zero. Byte-identity is re-proven by two independent oracles (the
  verbatim-tier gate and the rebuild-tier redline diff), both verified to FAIL under a deliberate
  one-character injection — so they are not tests that only ever pass.
- **Fixed a `getHTML()` round-trip defect**: the atom's placeholder label (`"Field: 4"`) was re-parsed as
  its display text, compounding to `"Field: Field: 4"` on a second pass. Harmless while that string was a
  UI label; a document-content bug once task 057 made it the field's `cachedResult`, and reachable via the
  ~15s dirty-autosave tick. Fixed backward-compatibly (`data-atom-display`, falling back to `textContent`,
  so server HTML is unaffected).
- **Accepted scope extension**: `opaqueAtomNode.ts` + `compose-contracts.ts` sit outside task 057's
  declared outputs. Its escalation trigger fired on the literal predicate ("attributes do not survive the
  round trip") but not on the reasoning behind it — the payload was present in the server's HTML and only
  needed declaring, the same four-line mechanism task 048 used for `symFont`/`symChar` in that file. The
  agent flagged it and offered revert-and-redispatch rather than proceeding silently, which is the
  behaviour the trigger exists to produce.
- **Correction to the published list**: on a keystroke edit the field result's bold/italic/underline are
  NOT carried (an opaque atom holds no marks), so a bold cross-reference in a plain paragraph returns
  plain. `notes/049-field-carry-decisions.md` §4 had claimed those three survive — true of the server path
  only. Both documents corrected; the field itself still survives.

## 2026-08-25 — Compose write fidelity: Word fields carried (task 049, `spaarkeai-compose-r8`)

- `docs/architecture/COMPOSE-WRITE-RESIDUAL-LOSS.md`: the field row moves **§2 (lost) → §3 (carried)**.
  Ordinary Word fields now round-trip an edit to their own paragraph as their **instruction** plus the
  result Word last computed, in the authoring form the document used. §2 keeps a narrower row for
  **nested and unterminated** fields, which have no single reproducible instruction. Owner sign-off on the
  list is now blocked on task 056 (embedded objects) alone.
- **The gate is STRUCTURAL, not a keyword allow-list.** A per-instruction freeze would make one document
  behave two ways, and a frozen `REF` goes *silently wrong* rather than visibly broken — it keeps printing
  "Section 4" after renumbering. `w:fldLock` is carried so fields an author deliberately froze stay frozen.
  Decision record: `projects/spaarkeai-compose-r8/notes/049-field-carry-decisions.md`.
- **Corrects a stale claim in `ComposeDocumentRenderer`** (review 011-P4/P9) that "the model does not carry
  bookmarks". Untrue since task 041 (`ComposeBlockMerge.CarryBookmarks`), and verifying it rather than
  inheriting it is what allowed `REF`/`PAGEREF` to be carried LIVE instead of frozen — a carried
  cross-reference is only an improvement if its target is still there.
- **Known gap, tracked as task 057:** the carry is server-side (projection → model → renderer). A
  *keystroke* edit does not yet preserve a field, because `docxBridge.ts` does not map a `field` atom back
  into the posted model. Task 049 shipped the payload (`data-field-instr` et al) so the client half is a
  small, well-specified change; 057 owns it.

## 2026-08-25 — `spaarkeai-compose-r8` task 055 (whole-document anchored placement)

No procedure-surface change. Recorded for the ADR-049 evidence trail:

- **ADR-049 I-7 strengthened on the client.** The whole-document review-flag channel (`comments[]` — the
  `flag-risks` intent's ENTIRE output) now resolves deterministically and populates
  `AnchoredAnnotationAnchor.paraId`, closing a **dark producer**: that field shipped in R3 FR-11 documented
  as the PRIMARY anchor, with a live consumer (`AnnotationReanchorService` resolves by it first), and
  nothing ever wrote it. Every whole-document review flag had been re-anchoring by fuzzy scorer even when
  the model named its paragraph exactly.
- **One anchor precedence, three consumers.** `widgets/composeAnchorResolution.ts` is now the single home
  of paraId-vs-citation precedence, shared by the AI-edit path (`usePendingRedline`), the advisory-comment
  path (`ComposeEditor.placeAdvisoryComments`) and the review-flag path
  (`ComposeWorkspace.registerAiReviewComments`). Each keeps its own span policy. The two SINKS stay
  separate deliberately — collapsing them would cost either Word `w:comment` export or ledger-key
  idempotency; §11 reasoning in `projects/spaarkeai-compose-r8/notes/055-review-flag-placement-decision.md`.
- **A silent-drop defect fixed.** `registerAiReviewComments` gated on `target_text` alone, so after task
  054 a flag carrying a deterministic anchor with weak prose was dropped — precisely the BEST-anchored
  ones. The gate is now "somewhere to hang it AND something to say".
- **Client tripwire pattern established.** The prose-matching leg moved to `hooks/redlineTextSearch.ts` so
  a test can REPLACE it — the client twin of `ThrowIfTextSearched` (`ComposeEditAnchorPassSeamTests.cs`).
  ts-jest compiles to CommonJS, where a same-module call is un-interceptable, so a module boundary is the
  only available client seam. Future "prove X was never called" client tests should follow this.

## How to maintain this

**Every PR that touches** `.claude/skills/`, `.claude/agents/`, `.claude/settings.json`, `.claude/patterns/`, `.claude/constraints/`, `.claude/FAILURE-MODES.md`, or the root `CLAUDE.md` **MUST add an entry to the `[Unreleased]` section below** before merge.

- One entry per logical change. Cite the commit SHA or PR number.
- Use the categories: **Added**, **Changed**, **Deprecated**, **Removed**, **Fixed**.
- "Bumped version" and trivial typo fixes can be omitted.
- When a project releases (a `work/<project>` branch merges to master), promote `[Unreleased]` to `[<project-name>] - <date>` and start a fresh `[Unreleased]`.

If you're not sure whether to add an entry, add one. Too granular is better than missing.

---

## [Unreleased]

### Added — FAILURE-MODES **G-12**: a stale test assembly behind a *truthful* "up-to-date" build (2026-08-27, `unified-access-control-r2` Wave A)

**Fifth stale-assembly incident in this project across two waves — and the first that the existing defence does not catch.**

The standing rule from the 075 batch is *"always read the build result before the test result."* That defends against a **failed or skipped** build masked by a stale-but-green test summary. G-12 is different: the build **succeeds** and honestly reports "up-to-date", because the falsehood is in **filesystem metadata**, not in the build.

- **Mechanism 1 — backwards-moving mtime.** `Copy-Item` (and `cp -p`, archive extraction, some editor "revert file" paths) **preserves `LastWriteTime`**. Restoring a file from a backup therefore moves its mtime *backwards*, MSBuild's incremental timestamp comparison concludes the existing DLL is newer than its input, compilation is skipped, and `dotnet test --no-build` executes the **previous** assembly. Task 011 hit this as a test failure that *contradicted the source on disk*.
- **Mechanism 2 —** `dotnet build Spaarke.sln` **did not refresh the BFF test project's output**; the test csproj had to be built explicitly.

**Why it earns its own entry rather than a line under AP-8**: perturbation testing is the primary anti-vacuity tool, and a stale assembly silently converts *"I proved this guard is load-bearing"* into *"this guard is untested"* — while looking identical. All five incidents produced **confident, wrong** verification results. Detection is by artifact, not by log: compare the DLL's mtime against the source you just edited.

**Changed**: `.claude/FAILURE-MODES.md` — TOC entry + `### G-12`; anchor verified against the heading.

### Changed — publish-size measurement convention is now BINDING, not descriptive (2026-08-27, `unified-access-control-r2` Wave A)

**Root CLAUDE.md §10's publish-size gate was measuring in two incompatible conventions, and every individual report was correct.**

Three sub-agents on the **identical base commit**, each stating *"compressed incl. PDBs"*, reported **45.07 / 45.07 / 43.78 MB** — a **1.29 MB spread on the same tree**. Cause: the POML corpus carries **two baseline clusters** (~43.65–43.71 MB across 24 POMLs, 44.96 MB across 31), so each agent compared against whichever its own POML cited, computed a small delta, and correctly concluded "within ceiling". The set was incoherent; the defect was visible **only by comparing reports across agents**, which no per-task gate can do.

The convention *was* already written down — but as a parenthetical describing how the **baseline** had been measured, not as a requirement on **your** measurement. That is the gap all three fell into.

**Changed**: `.claude/constraints/azure-deployment.md` § "BFF Publish-Size Per-Task Verification Rule (NFR-01)" — added a binding five-field reporting contract (command · RID/deployment mode · configuration · compression level · PDBs in/out), a MUST NOT on cross-convention comparison, the incident record, and an instruction to re-baseline POMLs citing the stale ~43.7 cluster.

**Impact on the gate**: the ≤60 MB HARD STOP was never at risk. What was degraded is the **≥+5 MB single-task drift detector** — with a 1.3 MB convention gap circulating, a real regression can be absorbed as a convention artifact and vice versa. The gate kept its floor and lost the sensitivity it was added for.

### Fixed — ADR-038 Amendment A1: `tests/Spaarke.ArchTests/**` is now the EIGHTH KEEP path (2026-08-24, `spaarke-auth-v4-dataverse-MI` task 090)

**Closed a contradiction that lived inside ADR-038 itself**, and that had been mitigated at the skill layer
rather than fixed since 2026-06-26:

- ADR-038 §7 bans **B1–B5** (DI-registration tests, ctor null-check tests, `Mock<HttpMessageHandler>` wiring
  tests), and its own "Some discovery loss" consequence names *"NetArchTest-style architecture tests at
  Tier 1"* as the **sanctioned replacement** for what those bans give up.
- But §2's KEEP-path list enumerated **7** categories and **did not include `tests/Spaarke.ArchTests/**`**.
- `/test-diet` is a **mandatory gate at every project close** (root CLAUDE.md §7) and classifies anything
  outside a KEEP path as a path violation → delete candidate. **The gate therefore recommended deleting the
  exact mechanism the ADR prescribes.**

**Why it persisted for two months**: task 063 fixed the *symptom* (heuristic 0 in `/test-diet`, plus naming
the category in `tests/CLAUDE.md`). That made the pain stop, which also made the cause invisible — while
leaving the protection in a skill file and a module directive, neither of which is the ADR. Those drift:
the same task found `/test-diet`'s path list had *also* been missing `tests/integration/seam/**` since
2026-07-09, silently making every vertical-slice-seam test in the repo a delete candidate.

**Changed** — all four surfaces moved together so they cannot disagree:
- `docs/adr/ADR-038-testing-strategy.md` — 7 → **8** KEEP paths; new `structural-fitness-function` row;
  the "discovery loss" consequence now points at its protected home; full **Amendment A1** record appended
- `.claude/constraints/testing.md` — "Seven" → "Eight" KEEP path categories + the new row
- `.claude/skills/test-diet/SKILL.md` — heuristic 1's path list now includes `tests/Spaarke.ArchTests/**`;
  heuristic 0's ratification note updated from OPEN to RATIFIED. **Heuristic 0 is deliberately retained** —
  the path fix alone would still let heuristics 2–12 mis-flag fitness functions on naming (B13) and
  setup-ratio (B15) grounds
- `tests/CLAUDE.md` — "same terms as the seven paths" → the eighth KEEP path, citing A1

**Evidence the category earns it**: graduation criterion 12 was exercised the same day — a deliberate ninth
secret-bearing confidential client made `CredentialGuardTests` (FR-F1) and `CredentialCensusTests` (FR-F2)
fail, naming the offending `file:line`. Note `dotnet build` **succeeds**; the ArchTests fail — the CI gate
is what fails, not the compiler.

### Added (2026-08-23 — the Compose **write-side residual loss list**, with a parity test behind it · `spaarkeai-compose-r8` task 045 / FR-A10)

- **Added — [`docs/architecture/COMPOSE-WRITE-RESIDUAL-LOSS.md`](../docs/architecture/COMPOSE-WRITE-RESIDUAL-LOSS.md)** — publishes exactly what Compose does NOT preserve on save, as the write-side companion to `COMPOSE-READ-REFERENCE-FIDELITY.md` (no duplication: that one is the read path). The **scope rule leads**, because it is what makes the list short and true: loss is **per-edited-block, never per-document** — an untouched block is cloned byte-for-byte, so a construct survives *precisely because* the save never parses it. Eight degradation codes documented; bookmarks and property inheritance documented as carried.
- **Added — `tests/integration/seam/Compose/ComposeResidualLossParityTests.cs`** — the forcing function. FR-A10 required the parity to be **demonstrated, not asserted**, so the document is not maintained by hand-review: the test measures every construct family through the real renderer (twice — untouched block and edited block) and fails if the document and the code disagree **in either direction**. Under-claim (an undocumented loss) and **over-claim** (a code the renderer no longer emits, or a family it actually preserves) are both failures — the second is the direction that lets a residual list rot into fiction while still looking maintained.
- **Fixed — `Services/Compose/ComposeBlockMerge.cs`: an INLINE `w:sdt` content control was dropped in SILENCE.** Found by the parity check on its first run (`edited: 0/1 kept · codes: (none)`), not written into it. Only the *block-level* `SdtBlock` had a shell carry and a warning; an inline control — a party name, an effective date, a defined-term placeholder, the ordinary shape in a legal template — was on no taxonomy list at all. `sdt` joined `ReportableConstructs` reusing the **existing** `hard-tier-sdt-flattened` code (root §11 — its client copy already read *"A content control … was saved as plain text"*), and the now-duplicate explicit warn on the block-level path was removed. A hand-written residual list would have inherited the same blind spot: you cannot document a loss you do not know you have.

### Changed (2026-08-21 — ADR-049 **R8 third amendment**: base re-projection + block copy-through · `spaarkeai-compose-r8`)

Owner-accepted §6.5 **Path B** amendment (*"ADR-049 is fine."*, 2026-08-21). Drafted by task 031 on the evidence of the Phase-3 architecture gate; applied at the start of task 040 rather than at the planned 045 wrap-up task, because while the write was outstanding ADR-049 still told a reader that *"render-on-save supersedes surgical byte-patch"* — the exact guidance that produced the defect 040 exists to fix.

- **Changed — [`.claude/adr/ADR-049-compose-shadow-document.md`](adr/ADR-049-compose-shadow-document.md)** — added the **R8 Path-B Amendment**. **The save renders from the content model AND preserves untouched content; these are not alternatives.** At save time the renderer re-projects the retained baseline server-side, pairs its blocks against the posted model **by document order** (`paraId` corroborates, never keys — duplicates are spec-legal across `mc:AlternateContent` and Word regenerates ids on save), then dispatches per block: unchanged → **clone the baseline's `w:p` subtree verbatim** with zero property logic; changed → render with property inheritance; unmergeable → thin render + warning, **never a content refusal**. Codifies **seven standing invariants** and — load-bearing — the **paired MUST**: *invariants (1) every-save-terminates-in-a-defined-outcome and (2) untouched-blocks-are-preserved are a PAIR; no future amendment may trade one away to obtain the other.* Both prior amendments did exactly that (**R4** took preservation and lost termination → the HTTP 422 treadmill; **R6** took termination and lost preservation → silent whole-body rebuild), which is why this clause exists. Adds normative **mechanism MUSTs** (direct `w:body` children only — never `body.Descendants<Paragraph>()`, which interleaves `w:txbxContent` paragraphs and mis-pairs every block after the first text box; "unchanged" decided against a fresh server-side re-projection, never text equality; comparison **fails closed**, baseline unavailability **fails open**). **Status line + footer updated**; the `docs/adr/` twin the footer said did not exist now does. **Scope guard**: save path only — R4.5's read/reference invariants **F-1…F-5** and **I-7** are untouched, and **I-5 (one body author) is reinforced, not relaxed** — the merge lives inside `ComposeDocumentRenderer`.
- **Added — [`docs/adr/ADR-049-compose-shadow-document.md`](../docs/adr/ADR-049-compose-shadow-document.md)** — the extended record (context, mechanism, consequences, rejected alternatives, evidence). Deliberately scoped to the R8 amendment's full reasoning rather than duplicating the whole ADR: two long documents saying the same thing drift.
- **Changed — [`.claude/adr/INDEX.md`](adr/INDEX.md)** — the 049 row still described R4's surgical `ComposeShadowPatchEngine` byte-patch and I-4 byte-identity as the save contract (never updated for R6 either). An agent scanning only the index would have taken the twice-superseded rule as current. Row rewritten to the R8 contract + status corrected to "Accepted, amended 3×".
- **Changed — [`docs/adr/INDEX.md`](../docs/adr/INDEX.md)** — added the missing ADR-049 rows (main table + Backend/API domain table); `Last Updated` refreshed.
- **Changed — root `CLAUDE.md` §17 Compose row** — same staleness, higher blast radius: root CLAUDE.md loads **every session**, and its Write/save half still read *"edits = step-level ops anchored `(paraId,runIndex,offset)` applied by ONE `ComposeShadowPatchEngine` byte-author"* (R4 — it had never been updated for R6). Replaced with the R8 contract + the paired MUST + a pointer to the extended record; the Read/reference half (R4.5) is unchanged and still accurate.

**Evidence** (measured, not argued — threshold ratified by task 023 *before* any prototype number existed): overall block preservation **18.08% → 100.00%**, near-tier **6.67% → 100%** on every one of 18 corpus documents, zero hard-fails, zero honesty violations, zero cumulative drift over 5 round trips, +2–19 ms per save, no new NuGet, publish 43.68 MB (−1.28 vs the 44.96 MB net10 baseline). `projects/spaarkeai-compose-r8/notes/{gate-contract,control-measurement,merge-prototype-results,gate-decision}.md`.

**Read this caveat with the numbers**: the gate measures **untouched** blocks and excludes the edited one by construction. The paragraph the user types in is still rebuilt from a model carrying `w:jc`/`w:b`/`w:i`. **Task 041 (FR-A04 property inheritance) owns that and is neither optional nor deferrable.** `ComposeShadowPatchEngine` is **NOT** confirmed subsumed (it serves the op-log path) and must not be deleted on this evidence — task 074 stays blocked.

Authored main-session per §3 write boundary.

### Fixed / Added (2026-08-20 — ADR-010 example corrected + new anti-pattern **AP-7** · `spaarke-auth-v4-dataverse-MI` task 011)

- **Fixed — [`.claude/adr/ADR-010-di-minimalism.md`](adr/ADR-010-di-minimalism.md), "Allowed Seams"**: the example read `services.AddSingleton<IAccessDataSource, DataverseAccessDataSource>()`. That is **not what the code does and must not be copied**. `DataverseAccessDataSource` is a **transient typed HttpClient** (`SpaarkeCore`) decorated by a scoped `CachedAccessDataSource`, and it holds **mutable per-instance auth state** (`_currentToken`, the `HttpClient`'s `Authorization` header) — so a singleton registration is a **data race that can bleed a token between users**, not merely an efficiency question. Corrected to the real registration, with the reason stated inline and a pointer to the pattern that *does* solve expensive shared state on a transient type (a static `(tenant|client|secret-fingerprint)` confidential-client cache). The example's actual point — that `IAccessDataSource` is one of only two sanctioned multi-implementation seams — is unchanged. Surfaced by `code-review` finding S-14 at task 011's Step 9.5 gate.

- **Added — [`.claude/FAILURE-MODES.md`](FAILURE-MODES.md) **AP-7: Converting a silent fallback into fail-fast, verified with targeted tests only**. Task 010 correctly replaced a silent `DefaultAzureCredential` fallback with fail-fast validation, verified with targeted seam tests + build + publish + CVE — all green — and shipped **13 failing contract tests**, found only when task 011 ran the full suite. Root cause generalises: **callers that depend on a silent fallback are by definition invisible at the change site** (they supplied nothing — there is no reference, call, or type dependency to grep for), and a targeted test run selects tests *near* the change, which is exactly the set that excludes them. Prevention: run the FULL suite for any change converting a fallback/default/permissive branch into a throw; and when failures surface in a later task, **stash and re-run before calling them pre-existing** — "fails on master too" and "fails without my current edits" are different claims.

### Changed (2026-08-20 — ADR-028 **A4 adoption CONFIRMED** + E4′ wiring correction · `spaarke-auth-v4-dataverse-MI` task 003)

- **Changed — [`.claude/adr/ADR-028-spaarke-auth-architecture.md`](adr/ADR-028-spaarke-auth-architecture.md), Amendment A4**: added an **ADOPTION STATUS** block recording that A4 is no longer accepted-on-reasoning but **verified on the wire**. Task 002 proved, against a real delegated user token on `spaarke-bff-dev/staging`, that the OBO grant succeeds under a Managed-Identity-issued client assertion — Graph/SPE, Dataverse `user_impersonation` (with `upn` preserved, so row-level authorization still evaluates as the *user*), and long-running OBO — with a negative control that fails loudly when the assertion is minted for the wrong identity. **MI-FIC is the adopted credential; the KV-certificate alternative was NOT taken** (it remains sanctioned where the same-tenant rule cannot hold, e.g. an unresolved cross-tenant Model 2 shape). This closes the question that three prior audits closed *wrongly* on an unrecorded premise.

- **Fixed — same file, A4 "Preferred wiring" section**: annotated that `Microsoft.Identity.Web`'s declarative ordered `ClientCredentials` JSON — presented by A4 as the preferred wiring — **is not usable in this codebase** (finding **E4′**). The repo has zero `EnableTokenAcquisition` / `ITokenAcquisition` / `IDownstreamApi` / `ClientCredentials` in any `.cs`; `AddMicrosoftIdentityWebApi` is inbound validation only; `Spaarke.Dataverse` has no Identity.Web reference. The JSON is retained as accurate *general* Microsoft guidance, but the direct-MSAL `.WithClientAssertion` + `ManagedIdentityClientAssertion` path is the mechanism here — **and the ordered fallback the rollback story depends on must therefore be built, not inherited**. Without this note a reader would configure the JSON, observe no effect, and reasonably conclude MI-FIC does not work.

- **Fixed — [`src/server/api/Sprk.Bff.Api/CLAUDE.md`](../src/server/api/Sprk.Bff.Api/CLAUDE.md)** (task 002, listed here for the auth-surface trail; not a `.claude/` file): removed the assertion that OBO *"still requires `BFF-API-ClientSecret` (confidential client per OAuth spec)"* — the exact false sentence that caused three audits to conclude the secret was permanent — and replaced it with the A4 shape plus the empirical evidence.

### Removed / Changed (2026-08-20 — God-class LOC ratchet RETIRED; replaced by complexity guidance)

- **Removed — `tests/Spaarke.ArchTests/GodClassGuardTests.cs`** (the hard CI gate on `src/server` file LOC). It gated on line count — the wrong instrument for a gradual, judgment-laden signal — froze existing large files at arbitrary values, and blocked normal feature work on active files (Compose, Chat) with a build failure that had to be hand-waivered. Per ADR-038's own "coverage = observation, never a gate" precedent, **size is now observed and complexity is evaluated by humans where the work is authored.**
- **Added — [`docs/standards/COMPONENT-COMPLEXITY.md`](../docs/standards/COMPONENT-COMPLEXITY.md)** — the standard: evaluate complexity/cohesion (responsibilities, coupling, ctor deps, branching), not LOC; when a large *cohesive* file is legitimate; decompose when responsibilities diverge. Wired into **root `CLAUDE.md` §11.5** (new) + **§17 pointer** (replaces the god-class-ratchet row), **`task-create` §3.5.6** (component-complexity check), **`code-review`** (maintainability dimension — complexity *direction*, not size), and a **non-blocking observation report** `scripts/report-large-server-files.ps1`. `.claude/patterns/testing/god-class-ratchet.md` converted to a RETIRED redirect stub; pattern INDEXes + project memory updated.

### Added (2026-08-18 — Navigator side-pane architecture pointer · spaarke-side-pane-navigation-history-r1 close-out)

- **Added — root `CLAUDE.md` §17 pointer** to [`docs/architecture/SPAARKE-SIDE-PANE-NAVIGATION.md`](../docs/architecture/SPAARKE-SIDE-PANE-NAVIGATION.md) (the docked "Navigator" pane). Makes the reusable feature discoverable — most importantly the `ensureNavigatorSidePane()` code-page registrar, which the doc designates a **standard code-page build step**. Doc itself refreshed post-UAT (not a `.claude/` file, no changelog obligation, noted here for context): access-based Monitored (Dataverse security trim, no owner filter — no BFF), `sprk_communication`→Email-code-page routing, name-resolution for bookmarks, the filled-evenodd-ring technique for outline pane icons, and entity-scoped ribbon ids.

### Changed (2026-08-17 — ADR-028 **Amendment A4**: secret-free confidential credential for OBO · `spaarke-auth-v4-dataverse-MI`)

Owner-directed §6.5 **path B** amendment. Fixes a rule that was **unsatisfiable for OBO** and had been generating recurring false-positive findings on every auth-touching task.

- **Fixed — [`.claude/skills/adr-check/references/adr-validation-rules.md`](skills/adr-check/references/adr-validation-rules.md)**: the `new ClientSecretCredential` rule excluded matches via `$_.Path -notmatch 'OBO|onBehalfOf'` — a **path** filter that never matched, because "OBO" appears in file *content*, not in file names. **Every OBO site therefore tripped the check on every run**, with no sanctioned alternative to migrate to. Replaced with an **E-3 / E-1 allowlist**, and added a second rule that flags **new** `.WithClientSecret(` sites and per-request `ConfidentialClientApplicationBuilder` construction (client assertions require singleton-cached CCAs). This is the concrete fix for the "cascading CI issues" this amendment was raised to stop.
- **Changed — [`.claude/adr/ADR-028-spaarke-auth-architecture.md`](adr/ADR-028-spaarke-auth-architecture.md)**: split the line-24 MUST into **app-only** (`DefaultAzureCredential`, UAMI) vs **confidential client acting as the BFF identity** (**MI-FIC** default, **Key Vault certificate** alternative, **never a secret**) — `DefaultAzureCredential` cannot perform an OBO exchange, which is why the old rule could not be satisfied. Added **Amendment A4** (required shape, platform constraints incl. same-tenant rule / UAMI-only / `api://AzureADTokenExchange`, normative deployment-shape table — **every Spaarke shape is intra-tenant so MI-FIC covers all of them**; the Spaarke-owned-app-reg-with-customer-tenant-compute shape is explicitly ruled out per owner decision 2026-08-18, so no certificate provisioning is required; the 20-FIC cap is documented as a non-factor, alternatives rejected) and transitional exception **E-3** (retained `BFF-API-ClientSecret`, time-boxed to `spaarke-auth-v4-dataverse-MI`, does **not** license new sites). Replaced the Key Patterns C# sample, which had been teaching `ClientSecretCredential` as the fallback. A4 does **not** weaken the A1/A2/A3 "no OBO on external / collaboration / module-host planes" invariants.
- **Fixed — [`.claude/constraints/auth.md`](constraints/auth.md)**: corrected *"OBO flow (OAuth spec requires confidential client + secret)"* → OAuth requires a confidential **credential** (secret / certificate / federated client assertion). **This single clause foreclosed the question in every prior auth audit.** Added the A4 MUST/MUST NOTs.
- **Changed — [`.claude/skills/adr-check/SKILL.md`](skills/adr-check/SKILL.md)** + **[`.claude/skills/adr-aware/SKILL.md`](skills/adr-aware/SKILL.md)**: added the A4 anti-pattern row; corrected "`ClientSecretCredential` for Graph" → "for app-only".
- **Changed — [`.claude/patterns/auth/service-principal.md`](patterns/auth/service-principal.md)**: updated to A4 (two credential classes, shared provider, singleton CCA caching); corrected the stale claim that the Dataverse SDK is constructed with `ClientSecretCredential` — migrated to MI by `code-quality-and-assurance-r3` #3b.

Evidence base: `projects/spaarke-auth-v4-dataverse-MI/notes/{RESEARCH-FINDINGS,CREDENTIAL-INVENTORY,TENANCY-AND-CREDENTIALS}.md`. MI-as-FIC is **GA since 2025-05-08**; Microsoft ranks client secrets *"Development and testing only."*
### Changed (2026-08-17 — push-to-github Step 1.7 real-DV smoke gate · smart-todo-r5 task 060)

- **Changed — [`.claude/skills/push-to-github/SKILL.md`](skills/push-to-github/SKILL.md)**: added **Step 1.7 — Real-Dataverse Smoke Check (Widget/Dataverse Changes)** (spec FR-20 / PROC-1). For any push that changes Dataverse-querying widget/component/service code, the pre-flight flow now WARNs + asks whether ≥1 real create+read against **real** Dataverse was exercised — a mock/prototype harness passing is not sufficient. Advisory (ask-user-first), same non-blocking shape as Steps 1.5/1.6 — **not** a CI script or hard block (§11 — extended the existing skill rather than authoring a new `/real-dv-smoke` command). Rationale cited in-step: R4 UAT-5/6 burned deploy cycles because the `spaarke-prototype` harness mocked a `sprk_contact` entity that doesn't exist in real Dataverse (real is OOB `contact`); the mock hid the entity-name bug. Also added a "Tips for AI" pointer.



- **Changed — [`.claude/patterns/testing/god-class-ratchet.md`](patterns/testing/god-class-ratchet.md)** + root `CLAUDE.md` §17 pointer: frozen-file count **14 → 13**. `DataverseWebApiService.cs` graduated off the ratchet — RED-4 "B" hardening deleted ~1,414 LOC of runtime-dead document/analysis/KPI/generic/processing-job/communication/health code (unreachable — those interfaces route to the SDK impl per `GraphModule.cs`), shrinking it **2,822 → 1,409** (below the 2,000 ceiling) and narrowing the class declaration to `: IEventDataverseService, IFieldMappingDataverseService`. Waiver removed from `GodClassGuardTests`. Verified: BFF 10,402 tests pass, ArchTests 38/38. Also surfaced **DEF-2** (WebApi field-mapping throws via the unimplemented `GetEntitySetNameAsync` stub — split-brain trap #2 is a *throwing* stub, not a duplicate) → routed in `projects/code-quality-and-assurance-r3/notes/defer-issues.md`.

### Added (2026-08-15 — God-class ratchet documentation · code-quality-and-assurance-r3 followups)

- **Added — [`.claude/patterns/testing/god-class-ratchet.md`](patterns/testing/god-class-ratchet.md)** + root `CLAUDE.md` §17 pointer + patterns INDEX entries. Documents the `GodClassGuardTests` server file-size gate (**no new `src/server/**/*.cs` > 2,000 lines; 14 existing large files frozen at LOC +100 grace**) so an editor/agent knows BEFORE growing a large file. On failure: decompose (preferred) or re-baseline the file's waiver with a PR reason — never silence. Redesigned the guard from an arbitrary single ceiling (4,950→2,700, which left actively-edited files ~24 lines of headroom) to a per-file freeze + grace.

### Added (2026-08-14 — worktree-net10-migrate skill · dotnet-10-upgrade-r1 cutover)

- **Added — [`.claude/skills/worktree-net10-migrate/SKILL.md`](skills/worktree-net10-migrate/SKILL.md)** + exemplar [`scripts/Update-WorktreeToNet10.ps1`](../scripts/Update-WorktreeToNet10.ps1) — one-command, **non-destructive** migrator to bring any worktree onto the net10 baseline (SDK check → dirty-tree guard → `git merge origin/master` → **net8-clobber guard** [every `src/server` csproj must be `net10.0`] → build; interprets NETSDK1045 + Graph 6.5/Kiota 2.0 errors). Registered in `.claude/skills/INDEX.md`. Addresses the live IDE-clobber failure mode (open VS/Rider autosaving stale net8 csproj over a merge → 503 on deploy).

### Changed (2026-08-14 — .NET 10 doc sweep · dotnet-10-upgrade-r1 cutover)

- **Changed — root [`CLAUDE.md`](../CLAUDE.md) §1** + `.claude/patterns/dataverse/web-api-client.md` + `src/server/api/Sprk.Bff.Api/CLAUDE.md` + README + 11 architecture/guide/procedure docs — swept the **normative build-target references from ".NET 8" → ".NET 10"** (and 2 stale "Graph SDK v5" → "v6") now that the backend is retargeted and **`origin/master` runs net10** (BFF + shared libs `net10.0`, `global.json` 10.0.100, dev App Service `DOTNETCORE|10.0`, Functions `dotnet-isolated 10.0`). Left intentionally unchanged: ADR history, `.claude/archive/`, other projects' `projects/*/CLAUDE.md` context files, and behavioral notes accurate from net8+ (`BackgroundServiceExceptionBehavior.StopHost`, `TimeProvider` ".NET 8+", the DATAVERSE-AUTH historical troubleshooting narrative). 19 files / 29 lines. Cutover driver: `dotnet-10-upgrade-r1` (merged to master `d71bd3547`).

### Changed (2026-08-06 — ADR-028 Amendment A3 · spaarke-SPA-external-access-platform-r2 task 010)

- **Changed — [`.claude/adr/ADR-028-spaarke-auth-architecture.md`](adr/ADR-028-spaarke-auth-architecture.md)** — applied **Amendment A3** (resolution path B, root CLAUDE.md §6.5; driver `spaarke-SPA-external-access-platform-r2`). **Generalizes A2's collaboration-host product line into a module-host SPA platform** serving all non-core (SPA) users, and **ratifies the shipped principal-agnostic endpoint pattern as canonical** (teams-app-r1 FR-22: `CallerPrincipalResolver` + `ExternalCollaboration` dual-scheme; 9761 tests pass, CIAM byte-for-byte preserved). Adds **MUST** rules (dual-plane external-app model canonical; authorize via `ICallerPrincipalResolver` + `AuthPolicies.ExternalCollaboration` dual-scheme with plane-agnostic handlers; plane selected only from validated `iss`/`tid` via `DeterminePlane`; new plane/module plugs in via one `ICallerPrincipalStrategy` + one `DeterminePlane` branch; Tier-1 module entitlement ⟂ Tier-2 record scope, both server-enforced) and **MUST NOT** rules (no second maintained workforce entry point; no OBO on either plane — broker-only/app-only preserved; no inferring Tier-1 entitlement from auth/plane/Tier-2; no routing the platform through Xrm-bound `@spaarke/auth`). **All A1+A2 invariants preserved + unweakened; internal Xrm surfaces UNAFFECTED; E-3 direct-Office boundary unchanged.** Applied **concise-only** (no `docs/adr/` full copy exists — mirrors A1/A2). A2 left intact. Task POML: `projects/spaarke-SPA-external-access-platform-r2/tasks/010-adr-028-amendment-a3.poml`.

### Changed (2026-08-05 — Compose render-on-save save-path amendment · spaarkeai-compose-r6, task 001)

- **Changed — [`.claude/adr/ADR-049-compose-shadow-document.md`](adr/ADR-049-compose-shadow-document.md)** — added an **R6 Path-B Amendment** (per CLAUDE.md §6.5) codifying **render-on-save** for the **save path only**: save re-derives a fresh `.docx` from a canonical document model into a new immutable SPE version, **superseding I-4** (untouched-subtree byte-identity) **and the line-40 MUST NOT** ("re-derive the `.docx` from the editor model on save"). Codifies the four spec points — (1) no surgical anchoring on the save path (retires the `ComposeBaselineParaIdStamper` count-gate, the 422 root); (2) version history = the fidelity safety net; (3) representative-corpus round-trip = a CI release gate; (4) `ComposeShadowPatchEngine` retained ONLY for a transitional clean-apply path. **Scope guard**: I-7 (no write-path text-search) satisfied trivially by rendering; the **R4.5 read/reference invariants F-1…F-5 remain in force** (save-path only supersession); no auth/security ADR touched; no unrelated section altered. **Path-B obligation**: MUST merge with or before the dependent R6 Phase-1 code (`spaarkeai-compose-r6` tasks 010/011/012). Authored main-session per §3 write boundary. Source: `projects/spaarkeai-compose-r6/spec.md` ADR-Tensions; summary at `projects/spaarkeai-compose-r6/notes/adr049-amendment-summary.md`.

### Changed (2026-08-03 — ADR-028 Amendment A2 · teams-app-r1 task 002)

- **Changed — [`.claude/adr/ADR-028-spaarke-auth-architecture.md`](adr/ADR-028-spaarke-auth-architecture.md)** — applied **Amendment A2** (resolution path B, root CLAUDE.md §6.5; driver `teams-app-r1`). **Generalizes the A1 exemption** from "external SPA" to "the collaboration hosts (external SPA + Teams tab)" over **one shared standalone-MSAL module with a pluggable authority** (CIAM for the SPA, workforce-multitenant Teams SSO/NAA for the Teams tab). Adds workforce-plane **MUST** rules (workforce Entra multitenant auth; shared pluggable-authority module; broker-only/no-OBO carried into the workforce plane; resolve caller to a `systemuser` **or** `contact` principal + accessible-record-set enforcement) and **MUST NOT** rules (no CIAM-in-Teams; no routing collaboration hosts through Xrm-bound `@spaarke/auth`). **All A1 invariants preserved + unweakened; internal Xrm surfaces (`@spaarke/auth`, PCFs, Code Pages) UNAFFECTED.** Cross-references ADR-034 contact-anchored entry (Path C, additive). Applied **concise-only** (the `docs/adr/` full copy does not exist — mirrors how A1 was applied; a new full ADR was declined as scope creep). Draft: [`projects/teams-app-r1/adr-028-amendment-draft.md`](../projects/teams-app-r1/adr-028-amendment-draft.md).

### Added (2026-08-01 — Canonical modal system · spaarke-modal-system, P0 docs closeout)

- **Added — [`.claude/adr/ADR-050-canonical-modal-shell.md`](adr/ADR-050-canonical-modal-shell.md)** — concise ADR codifying the one-canonical-`SprkModal`-shell + thin-presets decision: MUST compose `ModalWindowControls`/`RecordNavigationModalShell` + keep the Fluent `Dialog` envelope (transform-robust portal) + realize `--sprk-ui-scale` via a scaled Fluent theme (NOT CSS `zoom`) + semantic tokens only (**strengthens ADR-021** — bans `'1px'`/inline color in modal components); MUST NOT hand-roll `position:fixed` overlays or per-surface bespoke chrome. Preserves the Choice Dialog pattern via `ChoiceModal`. Registered in [`.claude/adr/INDEX.md`](adr/INDEX.md) (ADR-050 = next-free; ADR-049 was highest).
- **Added — [`.claude/patterns/ui/modal-shell.md`](patterns/ui/modal-shell.md)** — 25-line component-layer pointer (When / Read These Files / Constraints / Key Rules) → `SprkModal` + presets + `docs/standards/MODAL-DESIGN-SYSTEM.md`. Registered in [`.claude/patterns/ui/INDEX.md`](patterns/ui/INDEX.md).
- **Changed — [`.claude/patterns/ui/record-modal-selection.md`](patterns/ui/record-modal-selection.md)** — added a component-layer cross-link: the decision layer now points at `modal-shell.md` / `SprkModal` for HOW to build the chosen family (its decision content is unchanged).
- **Root `CLAUDE.md` §17** — added a **Modal design system (component layer)** pointer row → `docs/standards/MODAL-DESIGN-SYSTEM.md` (+ ADR-050 + the pattern pointer), sibling to the existing Modal decision-criteria row.
- Non-`.claude/` companions (same project): `docs/standards/MODAL-DESIGN-SYSTEM.md` authored (task 010) and cross-linked back from `MODAL-DECISION-CRITERIA.md`. All `.claude/` writes made main-session per §3 write boundary. spaarke-modal-system tasks 010–013; functional P0 (001–009) shipped the shell + 6 presets in `@spaarke/ui-components` (86 tests).

### Changed (2026-07-28 — Compose read/reference fidelity documented · spaarkeai-compose-fidelity-r4.5)

- **Added — `docs/architecture/COMPOSE-READ-REFERENCE-FIDELITY.md`** — narrative read/reference architecture doc (one reader · text exactness · deterministic numbering engine · `paraId→legal-number` reference layer + `CitationResolver` · honest page/line) with BFF surface, code inventory, and extension recipes. The narrative home the ADR-049 companion only gestures at. Registered in root CLAUDE.md §17 and cross-linked from ADR-049.
- **Root CLAUDE.md §17** — added a **Compose** pointer row (write/save = R4; read/reference = R4.5) → the new arch doc + [`.claude/adr/ADR-049-compose-shadow-document.md`](adr/ADR-049-compose-shadow-document.md). Compose was absent from the §17 pointer table despite five project generations.
- **ADR-049 (Compose Shadow Document)** — added an **R4.5 Read/Reference Fidelity companion** section documenting invariants **F-1…F-5** (one reader / text exactness / deterministic numbering / stable `paraId→legal-number` reference + `CitationResolver` / honest page-line). ADR body + [`.claude/adr/INDEX.md`](adr/INDEX.md) 049 row updated. **No write-side rule changed** — R4.5 built on R4's projection machinery; the two-author split stands. Doc-drift review of R4.5 also confirmed the `mammoth` references in `docs/` (CHAT-ATTACHMENT-POLICY, RECORD-HEADER-PCF, bundle-size assessment) are accurate — they concern SprkChat/attachments where `mammoth` correctly remains; only the *Compose* usage was removed. Authored main-session per §3 write boundary. Merged to master 2026-07-28.
- **Known pre-existing drift (NOT fixed here)**: `docs/adr/INDEX.md` (the *full* ADR index) remains stale (missing ADR-039/040–044/046/047/049) — same item the 2026-07-25 entry flagged; a separate backfill, not R4.5's scope.

### Changed (2026-07-25 — ADR-039 Output Determinism Modes amendment · ai-advanced-capabilities-nda-r1 task 001)

- **Amended — ADR-039 Grounded Execution & Closed Catalogs** (concise [`.claude/adr/ADR-039-grounded-execution-closed-catalogs.md`](adr/ADR-039-grounded-execution-closed-catalogs.md) + full [`docs/adr/ADR-039-grounded-execution-closed-catalogs.md`](../docs/adr/ADR-039-grounded-execution-closed-catalogs.md); `.claude/adr/INDEX.md` 039 row updated). **CLAUDE.md §6.5 Path B.** Adds **Output Determinism Modes** refining grounded-execution invariant (a): a cataloged capability declares `output_determinism` as catalog **data** — `fact` (default, deterministic — extractive/verbatim-cited, prior behavior unchanged) vs `advisory` (probabilistic — permits reasoning/synthesis depth + a Reasoning-tier deployment per ADR-016 while STILL prompt-controlled + schema-validated + source-cited for every factual claim, carrying a not-authoritative disclaimer + human-review surfacing). Advisory is a mode *of* invariant (a), **not** an escape from it — no new entry path, no fourth output category, no new mechanism. The amendment ADDS obligations (cite facts, decline-if-unverifiable, disclaimer, all other ADR-039 invariants hold) and weakens NO prior MUST/MUST NOT. Demand-pull: the first analysis/advisory vertical (NDA review) needs Claude/ChatGPT-level advisory output; the naive "extractive-only" reading of (a) was stricter than the invariant requires (ADR-039's liability posture is about *ungrounded* output, not *reasoned* output over grounded facts). Merge gate (FR-00) for the project's advisory-tier tasks. Authored main-session per §3 write boundary. NOTE: `docs/adr/INDEX.md` is stale (missing ADR-039/040–044/046/047/049) — not amended here to avoid partial-fix inconsistency; flagged for doc-drift-audit.

### Added (2026-07-23 — Assistant UI element criteria pointer · spaarkeai-assistant-enhancements-r1 task 051/090 doc-review)

- **Added — root `CLAUDE.md` §17 pointer** to [`docs/standards/ASSISTANT-UI-ELEMENT-CRITERIA.md`](../docs/standards/ASSISTANT-UI-ELEMENT-CRITERIA.md) (the bubble/chip/card/tab four-question decision + do/don't rules), placed beside its siblings `ASSISTANT-SURFACE-LAUNCH-MECHANISM.md` and `MODAL-DECISION-CRITERIA.md`. Closes the pointer gap found in the R1 pre-090 documentation review (the standards doc shipped 2026-07-22 without a root pointer). No behavioral rule change — a discoverability pointer only. Authored main-session per §3 write boundary.

### Added (2026-07-22 — Assistant surface-launch mechanism doc · spaarkeai-assistant-enhancements-r1)

- **Added — root `CLAUDE.md` §17 pointer** to the new [`docs/architecture/ASSISTANT-SURFACE-LAUNCH-MECHANISM.md`](../docs/architecture/ASSISTANT-SURFACE-LAUNCH-MECHANISM.md). Documents how the Assistant **deterministically** opens follow-on surfaces: `consumerType` (the Binding's routing decision) → `surfaceLaunchRegistry` static lookup → `handleSurfaceLaunch` branches on `kind` (wizard/oob-form via sessionStorage hand-off; workspace-tab/layout via PaneEventBus `widget_load`). Covers the two entry paths (SSE text-path + click/chip), the hand-off envelope, the intentionally-thin BFF (no surface identity server-side — surface identity stays in CODE per ADR-039 / BFF §10), 7 invariants, and the **extension recipe** (new surface = Action+Binding data + ONE registry entry in code). REQUIRED reading before adding any surface-opening capability. Grounds the registry-robustness change (retired the hardcoded `list-tasks` branch; activated the `workspace-tab` kind). Authored main-session per §3 write boundary.

### Added (2026-07-21 — use-case-to-design skill · ai-advanced-capabilities program)

- **Added — `use-case-to-design` skill** ([`.claude/skills/use-case-to-design/SKILL.md`](skills/use-case-to-design/SKILL.md) + `references/design-template.md` + `references/capability-lenses.md`). Codifies a repeatable **6-lens method** (use case → surface/UX → required AI capabilities → have-vs-gap → configuration → acceptance) that emits a complete `design.md` for the use-case-vertical projects under the `ai-advanced-capabilities-*` program. Upstream feeder to `design-to-spec` (writes design.md only; no spec/plan/tasks). Encodes the **REUSE > ACTIVATE > COMPLETE > BUILD** precedence and the demand-pull discipline that avoids the "dark-capability trap" (built-but-unwired horizontal capability, e.g. `MemoryCompositionService`). `capability-lenses.md` carries a 2026-07-21 have-vs-gap snapshot of the current AI stack with file evidence (verify-against-code required). Registered in `skills/INDEX.md` (Project Lifecycle, Tier 0). Context: `projects/ai-advanced-capabilities-development/PROGRAM-ROADMAP.md`. Authored main-session per §3 write boundary.

### Added (2026-07-21 — spaarke-notification-spine-r1 · ADR-047 Notification & Action Spine)

- **Added — `ADR-047` Notification & Action Spine** (concise [`.claude/adr/ADR-047-notification-action-spine.md`](adr/ADR-047-notification-action-spine.md) + full [`docs/adr/ADR-047-notification-action-spine.md`](../docs/adr/ADR-047-notification-action-spine.md); both ADR INDEXes updated). Claims the number reserved by ADR-046/ADR-048. Locks the ONE server-initiated **typed-signal → grounded-action → delivery** spine (Layers A–D) that collapses the `email-communication-solution-r4` / `messaging-communication-app-r3` / `spaarkeai-assistant-enhancements-r1` push forks into one. Six MUST/MUST-NOT commitments (typed signals · shared domain actions · per-source policy · SSE-as-presentation · outbox-before-ping · dumb-transport). Delivery-mode section cites the **task-001 FR-01 spike** decision (GO / Azure SignalR **Serverless** `Microsoft.Azure.SignalR.Management`, in-BFF; +0.30 MB compressed, 0 new HIGH CVE). Records the resolved ADR-043 tension (Notification `Routable=false` flip = Path C, comply-sequenced) + the FR-19 R3 contract-lock. Also updated the ADR-046/ADR-048 rows' "ADR-047 reserved" language to "authored (Proposed)". Authored main-session per §3 write boundary (spaarke-notification-spine-r1 task 010). Status **Proposed** → Accepted at the project gate.

- **Added — `ADR-048` Communication Participant Index** (concise `.claude/adr/ADR-048-communication-participant-index.md` + full `docs/adr/ADR-048-communication-participant-index.md`; both ADR INDEXes → Accepted). Codifies the message-grain `sprk_communicationparticipant` junction (task 003 schema) + the **ADR-034 path-C comply-with-intent** resolution (two typed lookups `sprk_systemuser` XOR `sprk_contact` for a 2-target identity, vs ADR-034's 6-target Guid+type tuple — not an amendment). Authored main-session per §3 write boundary (messaging-communication-app-r2 task 004). Sibling ADR-047 remains reserved for `spaarke-notification-spine-r1` (not claimed). Note: `docs/adr/INDEX.md` was also missing the ADR-046 row (R1 omission) — not back-filled here.

### Fixed + Changed + Added (2026-07-16 — project-setup pipeline modernization; POML template drift)
Trigger: `spaarkeai-compose-r3` project-pipeline run hit a stale POML template. Full audit + recommendations in [`AUDIT-FINDINGS-PIPELINE-MODERNIZATION-2026-07-16.md`](AUDIT-FINDINGS-PIPELINE-MODERNIZATION-2026-07-16.md); source finding in `projects/spaarkeai-compose-r3/notes/FINDING-poml-template-drift.md`.
- **Fixed — `templates/task-execution.template.md` demoted to a lean pointer (v3.0)**. The v2.0/Dec-2025 fossil was missing every modern task-metadata field (`model-tier`, `effort`, `rigor`, `gate`, `parallel-*`, `deps`, `justification`, `steps mode`, `escalation`, `ui-tests`) and carried dead paths (`docs/projects/`, `Spe.Bff.Api`, `docs/reference/adr/`, `docs/ai-knowledge/`). Now a current copy-paste skeleton + field-semantics table pointing at `task-create` as the single source of truth. Kills the drift class (finding rec B).
- **Changed — canonical POML field set reconciled** in `task-create` (Step 4, Step 3.5.5, POML Tag Requirements): `<rigor>` (was `<rigor-hint>`), `<deps>` (was metadata `<dependencies>`), `<gate>` added, `<blocks>` dropped. Deprecated aliases accepted for back-compat.
- **Added — Completeness lint** (finding rec C): `task-create` Validation Checklist step + `code-review` **Step 6.7** (POML completeness) + [`scripts/Validate-TaskPoml.ps1`](../scripts/Validate-TaskPoml.ps1) (regex-based, tolerant of imperfect XML in POML prose; validated clean on the compose-r3 27-POML set + exemplar 050).
- **Fixed — `project-pipeline` producer/consumer gap**: Step 3's POML-generation field list now emits the full canonical set that Step 5 dispatch + `/goal` consume; added §10 Placement-Justification + §11 component-justification prompts; removed the `MAX_THINKING_TOKENS` self-contradiction; `npm run build`→`build:prod` for PCF; planning tier → Opus 4.8 / Fable 5.
- **Added — structured-output schemas** (Agent SDK best practice) at `project-pipeline` Step 2 (discovery) + Step 5 (task outcome) for machine-readable subagent returns.
- **Fixed — `design-to-spec`**: broken §13→§15 root-CLAUDE cross-ref; mojibake artifact; added §10 `<hot-path-declaration>` + §11 component-justification seeding to the spec template.
- **Fixed — `task-execute`**: dead `src/server/api/CLAUDE.md` pointers → `…/Sprk.Bff.Api/CLAUDE.md`; `npm run build`→`build:prod` for PCF (AP-1); retired `Task`/`TaskOutput` tool names → `Agent`; rigor tree now reads the authored `<rigor>` hint; BFF checklist gained §10 publish-size (≤60 MB) + CVE + Placement-Justification; `projects/INDEX.md` maintenance noted at Step 0.5.
- **Changed — `CROSS-REFERENCE-MAP.md`** POML-format rows now name `task-create` as authoritative and the template as a synced pointer.
- Not changed (verified current): `/goal` wave-loop, §6.5 ADR-Tensions enforcement, `project-setup`.

### Changed (2026-07-12 — ADR-012 amendment: `@spaarke/visuals` sibling package, `visual-host-version-update`)
- **Amended ADR-012 (both forms)** — concise [`adr/ADR-012-shared-components.md`](adr/ADR-012-shared-components.md) + full [`docs/adr/ADR-012-shared-component-library.md`](../docs/adr/ADR-012-shared-component-library.md). **Path B amendment** per [CLAUDE.md §6.5](../CLAUDE.md): sanctions `@spaarke/visuals` (`src/client/shared/Spaarke.Visuals/`) as a **governed presentational sibling** to `@spaarke/ui-components` — the canonical home for data-viz primitives (metric cards, charts, gauges, distribution bars, calendar, due-date cards, mini-table). Records the 3-reason justification for a separate package (heavyweight `@fluentui/react-charting` quarantine; strict presentational purity — host binds data, no `Xrm`/`WebAPI`/FetchXML; `@types/react@18` pin for cross-surface JSX safety, subset-safe for both R16 PCF + R19 Code Pages). Restates the **anti-fragmentation boundary** (no ad-hoc per-project viz libs; data binding + card chrome + drill-through stay host-side) and defines a **3-test bar** (distinct contract + quarantine-worthy dep + cross-surface reuse) gating any future governed sibling package. Defers to root [CLAUDE.md §11](../CLAUDE.md) reuse rule. Introduced by `visual-host-version-update` (VHVU-070); merges alongside the dependent extraction code (VHVU-040–060).

### Added (2026-07-10 — ADR-044 Dataverse GUID Canonicalization + pattern)
- **New concise ADR [`ADR-044-dataverse-guid-canonicalization.md`](adr/ADR-044-dataverse-guid-canonicalization.md)** + full [`docs/adr/ADR-044-*.md`](../docs/adr/ADR-044-dataverse-guid-canonicalization.md) + [`adr/INDEX.md`](adr/INDEX.md) row. Codifies (as a binding MUST) the prevention that FAILURE-MODES **AP-3** (GUID case → AI Search `eq` misses) and **AP-6** (GUID braces → `@odata.bind` 400) both point to: canonicalize every Dataverse GUID to bare-lowercase at every boundary via the shared **`cleanGuid`** (client) / single-convergence-point normalize (BFF). Two prod failures, one root cause — elevated to ADR so it's `adr-check`/review-enforceable instead of tribal knowledge. **Status: Accepted (2026-07-10)** — codifies already-shipped, owner-approved behavior (PR #603/#609).
- **Extended pattern [`.claude/patterns/dataverse/relationship-navigation.md`](patterns/dataverse/relationship-navigation.md)** (the pattern that already owns `@odata.bind`/lookups) with a MANDATORY client GUID-normalization rule + the client `cleanGuid` file reference + ADR-044 constraint. This is the per-task-loaded surface, so the rule surfaces at the moment bind code is written.

### Added (2026-07-10 — ADR-042 Memory Architecture & Governance, `spaarke-ai-architecture-redesign-r2` task 065)
- **New concise ADR [`ADR-042-memory-architecture-governance.md`](adr/ADR-042-memory-architecture-governance.md)** + full [`docs/adr/ADR-042-*.md`](../docs/adr/ADR-042-memory-architecture-governance.md) + [`adr/INDEX.md`](adr/INDEX.md) row. Codifies the memory wave (tasks 050/051/052/057, shipped PRs #620/#622): TWO active scopes (Record `(entityType,entityId)` + User `systemuserid`; Conversation stays the ADR-040 ledger), subject-partitioned `memory-items` container (**never `/tenantId`**), per-fact docs with upsert-by-key supersession, the governance envelope (retentionClass→per-item TTL; sensitivity/deletionPolicy/trustLevel inert), the **AI-initiated + silent + provenance-tagged write posture — NO confirmation floor** (operator removed it 2026-07-08; review/delete + provenance + scope isolation are the controls), ADR-015 Tier-3 erasure + ids-only Tier-2 audit, and the explicitly-DEFERRED hard-governance boundary (→ security project #616). **Status: Proposed** — Accepted at the G-R2-B gate.

### Added (2026-07-09 — braced-GUID `@odata.bind` directive, PR #603 / #609)
- **[FAILURE-MODES.md](FAILURE-MODES.md) AP-6** — new anti-pattern entry: interpolating a raw (brace-wrapped, Xrm-sourced) GUID into an `@odata.bind` key predicate causes Dataverse HTTP 400 `Error in query syntax`. Carries the binding **directive on when + how to use the canonical `cleanGuid()`** helper (`import { cleanGuid } from '@spaarke/ui-components'`; wrap every GUID that enters an OData bind/URL; no-op on bare ids; don't hand-roll local `.replace(/[{}]/g,'')`). Sibling of AP-3 (same root cause — Xrm registry-format GUIDs — different symptom). Code: PR #603 (fix across all shared-lib wizard bind sites + Xrm adapter boundaries, merged `d2696b616`), PR #609 (`cleanGuid` barrel export).

### Changed (2026-07-09 — set-regarding-and-field-mapping-resolver-r2 doc consolidation)
- **Root [CLAUDE.md](../CLAUDE.md) §17 Field Mapping row** expanded to advertise the doc's new **code + PCF component inventory**, the **set-regarding / RegardingResolver** relationship (and that **AssociationResolver is retired**), the config enum reference, the Web-API seeding recipe, and the deprecated-guide stub. Doc-side changes (not procedure-surface): expanded [`docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md`](../docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md) with a full code+PCF inventory, resolver section, enum reference and UAT-hardening notes; added a table-nav path + option-set integers + Web-API seeding recipe + resolver note to [`docs/guides/FIELD-MAPPING-ADMIN-GUIDE.md`](../docs/guides/FIELD-MAPPING-ADMIN-GUIDE.md); **deprecated** the contradicting Feb-2026 `docs/product-documentation/field-mapping-admin-guide.md` (sync-modes / Refresh-from-Parent model) to a redirect stub; disambiguated `docs/data-model/field-mapping-reference.md`; fixed stale RegardingResolver/AssociationResolver rows in `src/client/pcf/CLAUDE.md`.

### Added (2026-07-09 — ADR-043 AI Capability Execution Spine, `spaarke-ai-architecture-redesign-r2` Phase E / task E-00)
- **New concise ADR [`ADR-043-ai-capability-execution-spine.md`](adr/ADR-043-ai-capability-execution-spine.md)** + full [`docs/adr/ADR-043-*.md`](../docs/adr/ADR-043-ai-capability-execution-spine.md) + [`adr/INDEX.md`](adr/INDEX.md) row. Codifies the AI execution engine that realizes the ADR-039 catalog contract, closing a verified gap (canonical engine implemented only a narrow slice: input=files-only, disposition=2-of-6, kind=Prompted-only; TWO redundant completion engines, canonical the weaker; disposition triplicated + drift-prone → the compose-r2 routing-promotion 422). Decision: three execution surfaces converging at one disposition→ledger→OutcomeCard layer; **converge the two completion engines onto one ContextBinder/ContextEnvelope input model** (no runtimeInput-straddle); **single-source disposition** (DispositionRoutability — admit follows "router can route it"); deterministic/interactive capabilities via a **deterministic ActionKind + supersession-write** (not a third spine); keep the agent-loop tool spine separate (unify = R8+). **Governance (adopted platform-wide): a `tests/integration/seam/**` vertical-slice KEEP test is the definition-of-done** for execution/dispatch changes (a green contract-shape test is not sufficient — the exact gap that let 016/042 ship "done" while 422-broken) + a named engine owner + a deferral re-parenting rule. Reserves (does not build) the future multi-step "Action Engine" seam per operator-confirmed inputs (hybrid authorization via the ADR-041 gate, closed-catalog-bound steps, ledger-resident plan, framework-agnostic). **Status: Proposed** — Accepted at the Phase-E gate. NOTE: this ADR also adds a KEEP-path category to ADR-038's scope (`tests/integration/seam/**`).

### Added (2026-07-09 — set-regarding-and-field-mapping-resolver-r2)
- **Root [CLAUDE.md](../CLAUDE.md) §17** — new pointer row for the **Field Mapping Framework** architecture doc ([`docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md`](../docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md)) + maker guide ([`docs/guides/FIELD-MAPPING-ADMIN-GUIDE.md`](../docs/guides/FIELD-MAPPING-ADMIN-GUIDE.md)). The framework restores creation-time assigned-resource inheritance for wizard-created children: two Dataverse tables, additive BFF DTO (no new endpoint/service), context-agnostic client engine (`FieldMappingService.ts`) with four mapping types (Copy incl. lookup `@odata.bind` / Default / Concat / Template), new `sprk_expression` Memo column as the Concat/Template extensibility seam, wired into all 7 `Create*Wizard` services. No Dataverse plugin, no form script, no new PCF (client-only).

### Added (2026-07-09 — ADR-041 concise mirror, `spaarke-ai-architecture-redesign-r2` task 043)
- **New concise ADR [`ADR-041-judgment-confirmation-completion-policy.md`](adr/ADR-041-judgment-confirmation-completion-policy.md)** + full [`docs/adr/ADR-041-*.md`](../docs/adr/ADR-041-judgment-confirmation-completion-policy.md) + [`adr/INDEX.md`](adr/INDEX.md) row. Principle-level judgment doctrine above ADR-039 dispatch: **D-F0** resourcefulness (reads free / writes gated; degradation ladder stays below the side-effect line, never weakens a gate), **D-F1** deterministic confirmation (risk-tier × origin × completeness; overlay precedence; E-1..E-6 ruled rows; risk = catalog DATA not runtime LLM judgment per ADR-039; confirmation state = gate-ledger property per ADR-040), **D-F2** truthful completion (OutcomeCard after ledger write; job-aware status; UI-action ack). **Status: Proposed** — Accepted flip gated on G-R2-A (task 049), mirroring the ADR-039/040 promotion-gate convention. Codifies what tasks 030/032/033/034/035/036/037/038 implement so they cite an authority rather than carrying behavior in directives. Records the open item that the Policy v2 engine (032) currently has 0 core call-sites.

### Removed (2026-07-08 — retired the vestigial AIP protocol layer)
- **Deleted `.claude/protocols/` entirely** (`AIP-001-task-execution.md`, `AIP-002-poml-format.md`, `AIP-003-human-escalation.md`, `INDEX.md`). Rationale: the AIP layer was dropped from root CLAUDE.md in the 2026-05-17 rewrite, frozen as "stable — do not modify" by `ai-procedure-refactoring-r1`, and had drifted (AIP-001 still carried stale 50/70/85% context thresholds vs CLAUDE.md's 60/70/85%). It duplicated the executable skills with only footer/bibliography inbound refs — nothing in any execution path. No unique content: POML format is canonical in [`task-create`](skills/task-create/SKILL.md) + [`task-execution.template.md`](templates/task-execution.template.md); execution/handoff in [`task-execute`](skills/task-execute/SKILL.md) + CLAUDE.md §5; escalation in CLAUDE.md §6 + §6.5.
- **Fixed inbound refs**: [`task-execute`](skills/task-execute/SKILL.md) footer, [`ai-procedure-maintenance`](skills/ai-procedure-maintenance/SKILL.md) (Checklist D + path-consistency checks + file-locations table now point to CLAUDE.md + skills; no third "protocol" home), [`CROSS-REFERENCE-MAP.md`](../CROSS-REFERENCE-MAP.md), [`docs/procedures/context-recovery.md`](../docs/procedures/context-recovery.md). Historical `projects/*` + `.claude/archive/` refs left as-is (archival).

### Added / Changed (2026-07-08 — Sonnet-5 optimization tranche 1 + `/goal` wave loop)
- **Per-task `<effort>` + effort rubric** (refines the earlier blanket-`xhigh` default, which the Sonnet-5 guide warns approaches Opus cost). [`task-create`](skills/task-create/SKILL.md) Step 3.5.5b: execution now defaults to **Sonnet 5 @ `high`**; `xhigh` reserved for brownfield/root-cause or complex-but-fully-specified work. `<effort>` added to POML metadata template + validation checklist. [`task-execute`](skills/task-execute/SKILL.md) Step 0.5 declaration adds `@ effort`; [`project-pipeline`](skills/project-pipeline/SKILL.md) Step 5 dispatches `effort = <effort>`.
- **Authoring for literal execution** ([`task-create`](skills/task-create/SKILL.md)): scoped constraints, closed-set acceptance criteria (incl. negative/auth cases), explicit "above and beyond", knowledge-curation/token-discipline note (~30% tokenizer inflation), concrete frontend visual direction (Step 3.65 — no "clean and modern").
- **Step modes + escalation element** (new Step 3.5.5c): `<steps mode="directional|prescriptive">` + optional `<escalation><trigger>`. [`task-execute`](skills/task-execute/SKILL.md) Step 0.5 honors both (`🧭 STEP MODE`), and prunes anti-laziness / forced-progress scaffolding while keeping artifact/gate-producing verification.
- **Coverage-first review** ([`code-review`](skills/code-review/SKILL.md) new Step 0 + [`adr-check`](skills/adr-check/SKILL.md) new Step 0): finding stage maximizes recall (report all + severity + confidence); orchestrator (task-execute Step 9.5) is the documented downstream filter. Removes the Sonnet-5 recall-drop from severity-filtering language.
- **`/goal` wave-completion loop** (optional, wave-level, transcript-only Haiku evaluator; NOT a per-task mechanism, NOT a quality gate). Eligibility rubric + compiled by-reference condition assigned by [`task-create`](skills/task-create/SKILL.md) new **Step 3.85** (machine-verifiable end-state, ≥3 well-specified low-ambiguity tasks, not security/deploy/irreversible). [`project-pipeline`](skills/project-pipeline/SKILL.md) Step 5 applies it to eligible waves; [`task-execute`](skills/task-execute/SKILL.md) new "`/goal` Wave-Completion Loop" section documents prerequisites + three exit states (condition met / BLOCKED.md / turn-cap) with Step 9.5 authority explicitly preserved.
- **Lessons loop** folded into the existing `notes/lessons-learned.md` convention (51+ projects) + `.claude/FAILURE-MODES.md` — no new central lessons store ([`task-create`](skills/task-create/SKILL.md) Step 3.7 + Step 3.4 reference).
- **Root [CLAUDE.md](../CLAUDE.md)** — new §8.5 "Execution Model, Effort & Wave Loops (Sonnet-5)" pointer + AIP-retirement note. **[`project-setup` claudemd-template](skills/project-setup/references/claudemd-template.md)** "Execution Model & Tiering" expanded (effort, step modes, coverage-first, `/goal`).
- **Deferred (separate evaluation, NOT implemented)**: settings-level Stop/PreToolUse hooks, `/loop`, `/batch`, scheduling, plan-mode mandates, and the proposal's calibration-pass (1.10). Per user direction 2026-07-08.

### Fixed (2026-07-08 — permission prompts)
- Added `"PowerShell(*)"` to user-level [`~/.claude/settings.json`](file) allow list (alongside the existing `"Bash(*)"`) to stop recurring PowerShell approval prompts across all worktrees. (User-scoped file; not in-repo.)

### Added (2026-07-08 visual-host-create-button-r1 — Sonnet-5 execution model tiering)
- **Model-tier strategy across the task pipeline.** Planning phases (design-to-spec, project-pipeline Steps 0–3) run on Opus 4.8 / Fable 5; task **execution defaults to Sonnet 5 @ effort `xhigh`**, with per-task escalation to Opus/Fable for the minority of high-power tasks. Mechanism is additive (absent tier ⇒ current behavior):
  - [`task-create` SKILL.md](skills/task-create/SKILL.md) — new **Step 3.5.5b** assigns a `<model-tier>` (`sonnet` default; `opus`/`fable` for high-blast-radius / architectural / ADR-migration / security tasks) + a Sonnet-5 "be-explicit" authoring note; `<model-tier>`/`<model-tier-reason>` added to the POML metadata template.
  - [`task-execute` SKILL.md](skills/task-execute/SKILL.md) — Step 0.5 declaration now includes `🔧 MODEL TIER`; serial task flagged above the session model triggers stop-and-escalate; FULL-rigor gates reaffirmed unconditional under Sonnet 5.
  - [`project-pipeline` SKILL.md](skills/project-pipeline/SKILL.md) — Step 5 dispatches each subagent with `model = <model-tier>`.
  - [`project-setup` claudemd-template](skills/project-setup/references/claudemd-template.md) — new "Execution Model & Tiering" section so every project CLAUDE.md carries the strategy.

### Changed (2026-07-08 spaarke-ai-architecture-redesign-r1 — tasks 050/055 close-out sync)
- Root [`CLAUDE.md`](../CLAUDE.md) §10 item 4 — publish-size baseline 45.65 MB (2026-05-26) → **49.63 MB incl. PDBs** (task 055 re-measurement 2026-07-08; PDB-convention reporting note added). Same update mirrored in [`.claude/adr/ADR-029`](adr/ADR-029-bff-publish-hygiene.md) (baseline + NFR-01 thresholds replace the stale 50 MB ceiling + nonexistent script hard-fail guard) and [`.claude/constraints/azure-deployment.md`](constraints/azure-deployment.md) (two baseline lines).
- [`.claude/adr/ADR-040`](adr/ADR-040-session-ledger.md) — inline payload cap MUST upgraded from "cap (pointer beyond cap)" to **enforce-at-cap 128 KB with deterministic truncation marker** (`SessionLedger.CapInlinePayload`, task 055; disposition legs fail loud on truncated payloads).
- [`.claude/constraints/bff-extensions.md`](constraints/bff-extensions.md) + `jps-validate` SKILL.md — three references to the deleted `GET /api/ai/playbook-builder/executor-config-schemas` endpoint re-pointed at `INodeExecutor.GetConfigSchema()` source implementations (`Services/Ai/Nodes/`); `jps-action-create` output-format "Add to Seed-JpsActions.ps1" step replaced with BA-editor/MCP row creation (task 050 builder-surface deletion).

### Changed (2026-07-07 spaarke-ai-architecture-redesign-r1 — task 051/052 procedure-surface sync)
- Root [`CLAUDE.md`](../CLAUDE.md) §17 — "Wiring a new consumer" row retitled to "Wiring a new capability (Action + Binding)" matching the task-052 rewrite of `ai-guide-consumer-wiring.md`.
- [`.claude/catalogs/scope-model-index.json`](catalogs/scope-model-index.json) — regenerated against spaarkedev1 by task 051 (60 Actions / 31 Skills / 31 Knowledge / 40 Tools; entries now carry deployed GUIDs + kind/tier/side-effect metadata).
- `jps-action-create` / `jps-validate` / `jps-playbook-design` SKILL.md — `Seed-JpsActions.ps1` pointers replaced with the MCP-create + `infra/dataverse/` mirror-first flow (script RETIRED by task 051); `sprk_externalid` → `sprk_knowledgecode` column drift fixed in jps-playbook-design.

### Changed (2026-07-07 spaarke-ai-architecture-redesign-r1 — G-P3 UAT hardening: input-schema authoring rules)
- [`.claude/skills/jps-action-create/SKILL.md`](skills/jps-action-create/SKILL.md) — Step 4 checklist gains a **binding `sprk_inputschema` block** for loop-projectable capabilities: OpenAI function-parameters subset required; property-level `"required": true|false` **BANNED** (object-level array only); `type:array` needs `items`; legacy `{"args":[...]}` format retired (rows normalized 2026-07-07); author-mirror-first in `infra/dataverse/inputschemas/` (CI-validated by `CatalogInputSchemaContractTests`; server twin `OpenAiFunctionSchemaValidator`). Root cause: G-P3 UAT 2026-07-07 — one invalid authored schema (task 042's create-task row) 400'd EVERY agent-loop turn platform-wide. `jps-validate` should adopt the same rules (follow-up).
- **Added** `.claude/skills/jps-action-create/examples/{create-task,draft-correspondence,refusal-handler}.json` (2026-07-06, main session) — JPS examples mirrored from ai-redesign-r1 tasks 041/042/033.

### Changed (2026-07-05 spaarke-ai-code-audit-r1 — greenfield convergence: 2 ADR amendments + 2 new ADRs)
- [`.claude/adr/ADR-037-multinode-output-composition.md`](adr/ADR-037-multinode-output-composition.md) (+ full version + INDEX row) — **Path B amendment (operator-approved, ADR review A-2)**: "DeliverComposite by default for future workspace playbooks" RESCINDED (engine frozen per OQ-2, canonical doc §4.2.1); ADR re-scoped to the section-name-keyed streaming + widget contract binding for ANY composite executor; 118R migration superseded; FieldDelta dual-render deletable at cutover (operator: customer continuity not a constraint).
- [`.claude/adr/ADR-013-ai-architecture.md`](adr/ADR-013-ai-architecture.md) (+ full version + INDEX row) — **Path B amendment (A-1)**: canonical invocation verb becomes capability invocation (`invoke(bindingId, args)`, Action+Binding model); `IInvokePlaybookAi` grandfathered as legacy shim (no new consumers); rotted architecture-map appendix replaced with canonical-doc pointer; ALL boundary rules unchanged.
- **Added** [`.claude/adr/ADR-039-grounded-execution-closed-catalogs.md`](adr/ADR-039-grounded-execution-closed-catalogs.md) (+ full version + INDEX row) — Proposed: one dispatch protocol (Event/Click/Text), two closed catalogs, grounded-output invariant, control-flow-is-code, "second intent-detection mechanism = violation". Encodes ratified D5/D6/D7 + OQ-1/OQ-2. Accepted at migration P1.
- **Added** [`.claude/adr/ADR-040-session-ledger.md`](adr/ADR-040-session-ledger.md) (+ full version + INDEX row) — Proposed: append-only addressable session ledger; storage-precedes-rendering; disposition as sole rendering contract; ADR-015 tier mapping. Encodes ratified D2/D8. Accepted at migration P0.
- Context for all four: canonical doc v0.4 (converged target) + `projects/spaarke-ai-code-audit-r1/{ADR-REVIEW-VS-GREENFIELD,OVERLAY-MATRIX,GREENFIELD-CONCEPTUAL-DESIGN}.md`.

### Changed (2026-07-01 spaarkeai-compose-r1 task 102 — ADR-013 Path B amendment)
- [`docs/adr/ADR-013-ai-architecture.md`](../docs/adr/ADR-013-ai-architecture.md) — new §"Amendment 2026-07-01 — Document-context invocation on `IInvokePlaybookAi` facade". Documents the widened facade contract (adds optional `userContext: string?` + `document: DocumentContext?` parameters, both defaulted, positioned after `cancellationToken` so existing 4-arg callers are unaffected). Motivating consumer: `spaarkeai-compose-r1` (Compose R1 drafting workspace). Boundary preserved — CRUD-side code STILL only injects `IInvokePlaybookAi` + `IConsumerRoutingService` (never AI-internal types). Reflection guard test updated with named allow-list for `Sprk.Bff.Api.Services.Ai.DocumentContext` (task 095). Signature change first shipped in tasks 095/096; SSE-mode consumer landed in task 097. Amendment filed via CLAUDE.md §6.5 Path B (amendment) — Path A (per-project exception) rejected because Compose is the first of many document-context consumers (Rewrite, Find Similar, Lookup References, downstream Matter/Communication/Insights consumers all inherit the widened facade cleanly).
- [`.claude/adr/ADR-013-ai-architecture.md`](adr/ADR-013-ai-architecture.md) — status updated to "Accepted (amended 2026-07-01)". Added two MUST rules: (1) use the new optional parameters for document-context invocation (no bypass to `IPlaybookOrchestrationService` allowed); (2) update the `PhaseAVerticalSliceTests.ADR013_InvokePlaybookAiFacade_DoesNotExposeAiInternalTypesInSurface` reflection guard's allow-list with a NAMED entry + citation when adding new types to the facade surface (silent bypass forbidden per CLAUDE.md §6.5).
- [`.claude/adr/INDEX.md`](adr/INDEX.md) — ADR-013 row updated: key constraint cites the amendment; status "Accepted (amended 2026-07-01)".
- **Enforcement of the CLAUDE.md §6.5 protocol in the field**: this is the first Path B amendment landed via the protocol added on 2026-06-29. Silent bypass was avoided; the reflection test's allow-list is a compile-time proof that the amendment was formally landed rather than tolerated.

### Changed (2026-07-01 — ai-spaarke-ai-workspace-UI-r2 Phase 3 doc sharpening, FR-15..FR-19, task 023)
- `docs/standards/MODAL-DECISION-CRITERIA.md` — Added **Two-Layout Standard** section at the top (Layout 1 canonical / Layout 2 justified exception / Layout 3 retired). Strengthened anti-pattern #4 with **verbatim MS Learn 2025-05-07 quote** ("Displaying a form within an IFrame embedded in another form is not supported") + 2026-02-10 CSP admin doc citation + 2026 CSP tightening context. Links to the R2 project's researcher evidence trail.
- `.claude/patterns/ui/record-modal-selection.md` — Rewritten around the two-layout framing. Now cites Layout 1 (85% × 85% via `Xrm.Navigation.navigateTo`) and Layout 2 (`RecordNavigationModalShell` for browse / content-shaped surfaces) as the ONLY two shapes. FR-20 binding (85% × 85% for every entity) called out inline.
- `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md` — Added § 6.6 "Row-click behavior for entity-list widgets" citing Communications as the reference example. Documents the free Layout 1 inheritance via `DataGrid.defaultRecordOpen`; documents the rare-case escape hatch (custom `onRecordOpen`) with ADR-conflict-resolution obligations.
- `docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md` — Added § 6.5 "Row-open contract" documenting `defaultRecordOpen` post-R2 (single Layout 1 code path, no dispatch on `rowOpen.type`), the new `configjson.rowOpen.formId` field (FR-01/FR-02), deprecation of `formDialogWidthPercent/HeightPercent` per FR-20, and the `onRecordOpen` host escape hatch (with audit note: no shipped consumer uses it).
- `docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` — Added § 6.5 "Modal UX standard for record row-clicks" cross-referencing MODAL-DECISION-CRITERIA. Widget authors do NOT decide the modal per widget — the DataGrid framework enforces Layout 1 automatically; RecordNavigationModalShell is the only Layout 2 path.
- **Retired**: iframe-hosted OOB `main.aspx` (Layout 3 anti-pattern). `SmartTodoModal.tsx` was the last Spaarke consumer; deleted 2026-07-01 by R2 FR-14 (task 022). Migration path (`Xrm.Navigation.navigateTo` at Layout 1 via `openSprkTodoAsLayout1` module-scope helper) shipped by R2 FR-13 (task 021).

### Added (2026-07-01 — Modal Decision Criteria standard + pattern pointer)
- `docs/standards/MODAL-DECISION-CRITERIA.md` — NEW. Binding standard covering the three Spaarke modal families: OOB `Xrm.Navigation.navigateTo` (full main-form fidelity, no browse), proprietary Fluent v9 Dialog (single record / preview / picker), and proprietary + [`RecordNavigationModalShell`](../src/client/shared/Spaarke.UI.Components/src/components/RecordNavigationModalShell/README.md) (browse-in-context "1 of N" pattern). TL;DR decision tree + 5 dimensions + 3 worked examples + 6 anti-patterns + hybrid pattern (proprietary browse + OOB escalation). Modeled on [`docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`](../docs/standards/DATA-ACCESS-DECISION-CRITERIA.md) shape.
- `.claude/patterns/ui/record-modal-selection.md` — NEW 25-line pattern pointer per CLAUDE.md §14. Points agents to the standard, the shell README, `RichFilePreviewDialog` (canonical consumer), and `ChoiceDialog` (Family 2 canonical).
- `.claude/patterns/ui/INDEX.md` — added row for `record-modal-selection.md`.
- `CLAUDE.md` §17 — added pointer row for `MODAL-DECISION-CRITERIA.md` (parallel to `DATA-ACCESS-DECISION-CRITERIA.md`).
- **Driver**: chat-review session identified that `RecordNavigationModalShell` (production-ready smart-todo-r4 infrastructure) had no adoption guidance — developers were unaware the browse "1 of N" chrome existed as reusable infrastructure and were defaulting to close/reopen UX or considering per-surface rebuilds. This standard closes the documentation gap so the shell becomes the discoverable default for cross-record browsing.
- **Next step (out of scope for this changelog entry)**: surface inventory to identify which existing modals should migrate to Family 3 (browse-in-context) vs stay Family 1 (OOB `navigateTo`) vs adopt the hybrid pattern.

### Added (2026-06-29 spaarkeai-compose-r1 — ADR Conflict Resolution Protocol governance)
- `CLAUDE.md` — new §6.5 "ADR Conflict Resolution Protocol (BINDING)". Introduces the three resolution paths for ADR conflicts: (A) project-scoped exception with documented rationale, (B) ADR amendment when context has changed, (C) pivot to comply when an ADR-compliant alternative exists. Establishes "silent compliance with a sub-optimal ADR is itself a failure mode" as the principle. Binding for ≥6 months from 2026-06-29.
- `.claude/skills/adr-check/SKILL.md` — new Step 5.5 "Surface Challenge Paths" + updated Output Format Violations block to display the three resolution paths alongside each violation. Reviewer now chooses intentionally instead of defaulting to silent compliance.
- `.claude/skills/code-review/SKILL.md` — Step 6 ADR Compliance Check rewritten to accept reasoned exceptions documented in PR description / `spec.md` "ADR Tensions" section. Silent violations still Critical; documented Path A exceptions = Warning with reviewer judgment. Cross-links CLAUDE.md §6.5.
- `.claude/skills/task-execute/SKILL.md` — Step 9.5 quality gates updated: ADR violations no longer default to "STOP, must fix" silent-comply loop. Agent applies CLAUDE.md §6.5 protocol (path A/B/C choice with user escalation for A and B).
- `.claude/skills/design-to-spec/SKILL.md` — both spec.md templates (inline Step 4 + standalone bottom template) extended with mandatory "ADR Tensions" section (table format: ADR / rule / conflict / path / rationale). Default content if no tensions: explicit "No ADR tensions surfaced" statement.
- `.claude/skills/project-pipeline/SKILL.md` — Step 1 spec validation now requires "ADR Tensions" section; new Step 1.7 processes declared tensions before Step 2 resource discovery (validates rationale concreteness, flags Path B amendment prerequisite, summarizes path counts).
- **Driver**: design conversation during `spaarkeai-compose-r1` surfaced governance gap — agents and humans default to silent ADR compliance even when path A (exception) or path B (amendment) would produce a better technical outcome. User explicit ask: "if we have surfaced a legitimate exception or required modification to an ADR then we MUST surface this conflict and resolve it. We cannot have our ADRs drive us to sub-optimal solutions." This protocol formalizes the resolution.
- Reinforcement points: design-time (`design-to-spec` + `project-pipeline`), code-review-time (`code-review`), task-execute-time (`task-execute` Step 9.5), and ad-hoc (`adr-check`). Five enforcement layers ensure the principle isn't a doc-only addition.

### Added (2026-06-29 spaarke-ai-platform-unification-r7 Wave 6 task 068 — root CLAUDE.md §17 pointer to consumer-wiring guide; Wave 6 task 064 — bff-extensions §G rewrite; Wave 7 — jps-* skill rewrites)
- Root [`CLAUDE.md`](../CLAUDE.md) §17 Pointers — added row for [`docs/guides/ai-guide-consumer-wiring.md`](../docs/guides/ai-guide-consumer-wiring.md) (created Wave 6 task 067 per FR-31). §17 row "BFF additions governance" annotated with §G rewrite date.
- [`.claude/constraints/bff-extensions.md`](constraints/bff-extensions.md) §G "Action / Node / Playbook Config Boundary" — REWRITTEN for R7 single-hop dispatch (FR-29). New 4-Home table reflects dispatch on the NODE (Home C) + Action as reusable prompt template (Home A, dispatch removed) + decorative `sprk_analysisactiontype` lookup table (Home D). New "Binding MUST rules" + "Binding MUST NOT rules" sections explicitly enumerate dropped columns + structural-fallback delete + categorization-only stance. Hot-Path Declaration section RENUMBERED §G → §H to fix duplicate-§G ambiguity introduced by sibling project ci-cd-unit-test-remediation-r1 landing.
- [`.claude/skills/jps-action-create/SKILL.md`](skills/jps-action-create/SKILL.md) — Wave 7 task 070 (FR-32). Step 1.5 Config-Home Guard table updated; Step 5.5 MCP verify drops `_sprk_actiontypeid_value`; new "R7 dispatch model" section with §3.1 WHY citation.
- [`.claude/skills/jps-playbook-design/SKILL.md`](skills/jps-playbook-design/SKILL.md) — Wave 7 task 071. Step 1.5 item 3 replaces 3-tier lookup ladder with single-hop. Step 10 verify-deploy query uses `sprk_executortype`. New 33-executor catalog by tier + Executor-Type-FIRST workflow.
- [`.claude/skills/jps-playbook-audit/SKILL.md`](skills/jps-playbook-audit/SKILL.md) — Wave 7 task 072. Step 2 query updated; Check 3.5 citation corrected; new Check 3.6 enumerates 7 R7 drift patterns A-G mirroring Wave 5 task 050 CSV shape.
- [`.claude/skills/jps-validate/SKILL.md`](skills/jps-validate/SKILL.md) — Wave 7 task 073. Step 7.5 CHECK 25 marked LEGACY; CHECK 26 (structural-fallback) DELETED; new Step 7.6 R7-V-01-V-04 + 6 LEGACY-* drift flags; new Step 7.7 typed-config schema check against Wave 3 BFF endpoint.
- [`.claude/skills/jps-scope-refresh/SKILL.md`](skills/jps-scope-refresh/SKILL.md) — Wave 7 task 074 (FR-33). Two-authoring-surfaces table updated (Node Type OptionSet → Executor Type Choice Set, 33 values). C# enum rename `ActionType` → `ExecutorType` applied throughout. Operational behavior unchanged (terminology touch-up only).

Commits:
- `d79432f9e` — Wave 4 schema drops (043+044, FR-03+FR-04)
- `7f28da008` — Wave 4 AnalysisActionService cleanup (046)
- `dd95dff69` — Wave 4 publish-hygiene gate PASS (047)
- `79ced1c6a` — Wave 8 form default (081, FR-21)
- `6e5e070e3` — Wave 8 placeholder schemas (085, FR-23)
- `2a5ff9e5a` — Wave 8 promptSchemaOverride wiring (087, FR-25)
- `e020c25e4` — Wave 7 jps-* skill rewrites + smoke test (070-075, FR-32/33)
- (this commit) — Wave 6 tasks 064 + 068

### Added (2026-06-25 smart-todo-r4 R4-112 — PCF `noAposStringType` XSD failure mode)
- `.claude/skills/pcf-deploy/SKILL.md` — new row in Failure Modes & Recovery table for `noAposStringType` XSD validation failure (Dataverse PCF import rejects apostrophes in `description-key` attribute values). Discovered during RegardingResolver v1.2.0 deploy (commit 5b7a62812) — `entity's` and `'sprk_todo'` in description-key blocked the import. Comments are fine (XSD skips them); only attribute values matter. Burned ~10 min on first import attempt; this entry saves the next operator.

### Added (2026-06-25 spaarke-ai-platform-chat-routing-redesign-r1 Phase 5R Wave 5-C — ADR-037 Multi-Node Output Composition)
- `.claude/adr/ADR-037-multinode-output-composition.md` — concise ADR (~115 lines). Decision: introduce `NodeType.DeliverComposite` + per-section SSE streaming (`section_started` / `section_data` / `section_completed` keyed by section NAME, not schema position) + FE widget rework consuming `sections: Record<string, SectionState>`. Replaces the legacy 5-coordination-point schema-aware widget model (schema-on-action + schema-aware widget + ordinal indexing + implicit linkage + author-side-only contract) with a 2-point pattern (section name + section state). Legacy `FieldDelta` path preserved via runtime event-type detection for unmigrated playbooks. Chat-destination playbooks STAY single-action (composition adds no value for one streamed paragraph). MUST / MUST NOT rules + backward-compat invariants + reference implementation table (tasks 114R/114a/114b/118R).
- `docs/adr/ADR-037-multinode-output-composition.md` — full ADR with the 5-coordination-point fragility analysis (with examples of how rename / reorder broke rendering silently), 4 alternatives considered + rejection reasons, consequences (positive / negative / neutral), per-playbook migration runbook, open questions (section-name versioning policy, per-section regeneration UX). Driver: 2026-06-24 user design conversation surfaced architectural frailty in legacy widget; Phase 5R Wave 5-C is the rework.
- `.claude/adr/INDEX.md` — new ADR-037 entry placed after ADR-036.

### Added (2026-06-23 spaarke-devops-project-tracking-r1 — GitHub-native portfolio tracking)
- **9 new `/devops-*` skills** — `.claude/skills/devops-{portfolio-setup,epic-create,idea-create,idea-promote,project-start,project-register,project-sync,portfolio-status,project-archive}/SKILL.md`. Lifecycle: capture → promote → start → sync → archive. All idempotent (NFR-04). `/devops-project-start` is THE BLESSED HANDOFF (D-13) — the one canonical bridge from a Project Issue to a local worktree.
- **9 hook injections into existing skills** — `design-to-spec`, `project-pipeline`, `task-create`, `task-execute`, `context-handoff` (HIGHEST VALUE per spec §6.2), `worktree-setup`, `worktree-sync`, `repo-cleanup`, `merge-to-master` each gained a "Portfolio Hook" appendix section (additive only per NFR-03 — existing contracts unchanged). Hooks call `/devops-project-sync` (or `register`/`archive` where appropriate) at end of host skill execution.
- **GitHub Project #2 schema** — `Type=Project` option added (preserving 6 existing); 6 new custom fields (Project Type, Worktree Path, Project Folder, Task Count, Tasks Completed, Project Status); 7 labels (epic, project, backlog, worktree:active/archived, on-hold, cancelled); 3 issue templates at `.github/ISSUE_TEMPLATE/{epic,project,idea}.yml`; 12 initial Epic Issues #421–#432.
- **`.claude/skills/INDEX.md`** — 9 new rows for `/devops-*` family.
- **`CLAUDE.md` §17 Pointers** — new row for portfolio tracking + DevOps procedures.
- **`docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md`** — extended with "Portfolio Integration" section (FR-29): Step 0 idea capture, Epic ↔ Project mechanics, Idea promotion paths, BLESSED HANDOFF walkthrough, auto-hook behaviors table, 9-skill command reference, portfolio-specific troubleshooting.
- **`docs/procedures/AI-CODING-PROCEDURES-GUIDE.md`** — extended with "Portfolio Scenarios" section (FR-30): 7 new scenarios in existing tri-section pattern (capture idea / promote / update status / close project / see what's running / package ideas / stakeholder view).

### Critical lesson surfaced
- **`updateProjectV2Field` reassigns option IDs** — empirically verified during Phase 1 task 001 (2026-06-23). The GitHub GraphQL mutation REPLACES the full option list AND generates new internal option IDs for every option, even unchanged ones. Items currently bound to old option IDs lose their references. The `/devops-portfolio-setup` skill MUST implement snapshot → mutate → reconcile pattern. Logged in `projects/spaarke-devops-project-tracking-r1/notes/spikes/phase1-task001-execution-log-2026-06-23.md`.

### Added (2026-06-22 spaarke-ai-platform-chat-routing-redesign-r1 — Component Justification governance)
- `CLAUDE.md` — new §11 "Component Justification — Default to Reuse (BINDING)" + renumber §11→§12 through §17→§18. Introduces the three-question template (Existing / Extension / Cost-of-doing-nothing) for every NEW service / abstraction / interface / endpoint / DI registration / package / Dataverse column / file surface. Enforcement points: spec authoring (project-pipeline), plan WBS (task-create Step 3.5.6), code review (code-review Step 6.6). Anti-patterns documented from real R1 examples (LegalWorkspace dead-code misreading, sprk_playbookcode field-choice error, 8-tool surface overreach). Driver: chat-routing-redesign-r1 Q&A surfaced three scope-creep failures that the rule would have caught at authoring time.
- `.claude/skills/task-create/SKILL.md` — new Step 3.5.6 "Component Justification Gate (REQUIRED per CLAUDE.md §11)". Requires `<justification>` POML element on every new-component task; decision logic for REWRITE-as-extension vs DEMOTE/DROP vs PROCEED. Scope explicitly excludes pure modifications to existing files.
- `.claude/skills/code-review/SKILL.md` — new Step 6.6 "Component Justification Check (Universal — CLAUDE.md §11)". Extends Step 6.5 (BFF Hygiene) from BFF-only to all new components. Verifies the three answers are concrete (cite file:line, name a concrete failure mode); flags hollow / boilerplate justifications as WARNINGs.

### Changed (2026-06-21 spaarke-ai-platform-chat-routing-redesign-r1 — ADR-030 v2 `memory` channel amendment)
- `.claude/adr/ADR-030-pane-event-bus.md` — v2 amendment adds 5th channel `memory` to the PaneEventBus closed union. New `MemoryPaneEvent` interface with 5 initial discriminants: `promotion_pending`, `promotion_resolved`, `fact_promoted`, `pin_added`, `pin_removed`. Channel union expanded from 4 → 5; sixth channel still requires successor ADR. Amendment Record section appended documenting context (chat-routing-redesign-r1 6-tier memory subsystem), constraints preserved (ADR-015 tier-1 safety on payloads — deterministic IDs + 80-char summaries only; tenant scope via subscriber context), required implementation updates (PaneEventTypes.ts extension; ContextPane subscriber wiring; MatterMemoryPromotionService dispatch site). Driver: chat-routing-redesign-r1 architecture §6.4 promotion approval workflow needed dedicated semantic channel instead of namespaced `workspace.*` workaround.
- `docs/adr/ADR-030-pane-event-bus.md` — full ADR amended in lockstep with concise version. Decision section §1 expanded from 4 → 5 channels; "fifth channel" references throughout updated to "sixth channel"; verification grep commands updated; AI-Directed Coding Guidance updated with new guidance for memory-domain events; Amendment History section appended (v2 record). Both ADR versions stay in sync.

### Added (2026-05-26 R4 Phase 1 F-3 — publish-size per-task verification rule)
- `.claude/constraints/azure-deployment.md` — new "BFF Publish-Size Per-Task Verification Rule (NFR-01)" section. Binding rule: every BFF-touching task MUST run `dotnet publish` + report compressed size + diff vs prior baseline. Ceiling: ≤60 MB (spec NFR-01). Current baseline ~45.65 MB. Escalation thresholds: ≥+5 MB single-task → justification; ≥55 MB cumulative → architecture review; ≥60 MB → HARD STOP. Driver: R4 NFR-01 / F-3 (operationalizes ADR-029).
- `CLAUDE.md` (root) §10 item 4 — strengthened from "verify if adding NuGet packages" → "verify on EVERY BFF-touching task" with explicit `dotnet publish` command, absolute-size + diff reporting requirement, and escalation thresholds. Cross-links to azure-deployment.md.

### Changed (2026-05-26 sdap-bff-api-remediation-fix Phase 5 wrap-up)
- `docs/guides/auth-deployment-setup.md` §3 expanded with new §3.5 covering 25+ App Settings discovered during Phase 5 demo prep beyond the original "8 settings" inventory (MI identity disambiguation 5 keys + Cosmos persistence + AgentService placeholders + feature-flag=false patterns + email subsystem).
- `docs/guides/auth-deployment-setup.md` §7c — drop `-UserPrincipalName` from `Connect-ExchangeOnline` example to avoid the mismatch failure mode discovered in Phase 5 (operator's browser-selected account vs param).
- `.claude/constraints/azure-deployment.md` Publish & Packaging — added linux-x64 RID + sourcemap exclusion MUST rules; baseline compressed size updated 60 → 45 MB (Phase 5 measured 45.65 MB post-Outcome-A).
- `.claude/FAILURE-MODES.md` extended with 4 new entries (AP-4 dev/demo bundle drift `/api` bug; G-5 Dataverse Application User registration; G-6 `Connect-ExchangeOnline -UPN` mismatch; G-7 Git Bash MSYS path mangling).
- `.claude/adr/ADR-007-spefilestore.md` — cross-reference added to refined ADR-013 + the new `Services/Ai/PublicContracts/` facade as parallel example of facade-over-SDK pattern.
- `.claude/adr/ADR-010-di-minimalism.md` — Phase 5 baseline note (265 registrations; +4 from facade is within expected delta).
- `docs/architecture/AI-ARCHITECTURE.md` — new "AI Public Contracts Facade Boundary" section documenting the 4 facade interfaces + 5 documented AI-API-surface exceptions + handler relocation to `Services/Ai/Jobs/`.
- `docs/architecture/AUTH-AND-BFF-URL-PATTERN.md` — cross-env consistency callout + checklist item for `sprk_BffApiBaseUrl` format.
- `docs/architecture/INFRASTRUCTURE-PACKAGING-STRATEGY.md` — new §5 BFF Binary Packaging covering linux-x64 RID + sourcemap exclusion + transitive override pattern + measured baselines.
- `docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md` — added MI + Cosmos DB rows to per-environment resources table.
- `docs/guides/CUSTOMER-DEPLOYMENT-GUIDE.md` — resolved `sprk_BffApiBaseUrl` `/api` suffix contradiction with auth-deployment-setup.md.
- `docs/guides/COMMUNICATION-DEPLOYMENT-GUIDE.md` — added full 17-setting email inventory discovered in Phase 5 (9 Communication + 8 EmailProcessing).
- `docs/guides/DATAVERSE-AUTHENTICATION-GUIDE.md` — added MANDATORY Application User registration section with full Web API walkthrough.
- `docs/guides/PCF-DEPLOYMENT-GUIDE.md` — added URL construction convention section documenting `getBffBaseUrl()` host-only pattern.
- `docs/guides/AI-DEPLOYMENT-GUIDE.md` — added mandatory Cosmos DB infrastructure section (account + DB + 5 containers + RBAC + App Settings).

### Added (2026-05-26)
- `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/` facade per refined ADR-013 — 4 interfaces (`IBriefingAi`, `IInvoiceAi`, `IRecordMatchingAi`, `IWorkspacePrefillAi`) + 4 implementations. 10 CRUD consumers migrated (Finance, Workspace, Jobs, Dataverse, Filters, Endpoints); 5 documented AI-API-surface exceptions (Chat/Playbook/Builder/Agent endpoints + auth filter). 92% reduction in direct AI injection in CRUD code.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Jobs/` (5 files relocated from `Services/Jobs/{Handlers,}` per FR-E3): AppOnlyDocumentAnalysisJobHandler, BulkRagIndexingJobHandler, EmailAnalysisJobHandler, ProfileSummaryJobHandler, EmbeddingMigrationService.
- LegalWorkspace `/api` prefix fix (3 sites: `FilePreviewDialog.tsx:320`, `closureService.ts:63`, `provisioningService.ts:81`) — commit `2561ce37`. Deployed to both dev + demo `sprk_corporateworkspace` web resource.

### Changed
- **`code-review` + `adr-check` now enforce CLAUDE.md §10 BFF Hygiene + `bff-extensions.md`** — closes the gap where the binding §10 rule was loaded as context but never explicitly checked. `adr-check` Step 2's quick-reference table adds ADR-013 (refined 2026-05-20); new Step 2.5 conditionally loads `bff-extensions.md` and applies its 5-rule pre-merge checklist when changed files touch `Sprk.Bff.Api/`, `Spaarke.Core/`, or `Spaarke.Dataverse/`. `code-review` Step 6 adds ADR-013 to its CRITICAL ADRs list; new Step 6.5 runs the same §10 checklist with explicit severity assignment (missing Placement Justification → Critical; new direct CRUD→AI dep → Critical; new HIGH-severity CVE → Critical). Both edits cite `bff-extensions.md` as the single source of truth — zero duplication of rule content.

### Added
- `.claude/AUDIT-FINDINGS-CLAUDEMD.md` — Phase 3a audit of root `CLAUDE.md` against community best practices + Phase 0 inventory (75-section sign-off table + proposed skeleton + open questions). Commit `0c11cd43`.
- `.claude/archive/2026-05-17/CLAUDE.md` — preserved copy of the 1190-line OLD root `CLAUDE.md` before Phase 3b rewrite (reversibility per NF-1).
- **Auth v2 pre-flight** — STOP banners on 5 partially-superseded docs (`.claude/patterns/auth/spaarke-sso-binding.md`, `.claude/patterns/auth/token-caching.md`, `.claude/constraints/auth.md`, `docs/architecture/AUTH-AND-BFF-URL-PATTERN.md`, `docs/architecture/sdap-auth-patterns.md`) + full-deprecation banners on 2 DEPRECATED-* files. Each banner names what stays canonical (INV-1..INV-7, server-side OBO, `buildBffApiUrl()`, etc.). PF-4..PF-10. Commit `281f7210`.
- **Auth v2 pre-flight** — Pointer row in root `CLAUDE.md` §15 directing all agents (any worktree) to `.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md` as the active auth v2 design until ADR-027 ships. PF-12. Commit `5b04b6ff`.

### Changed
- **Root `CLAUDE.md` rewritten** from 1190 → 264 lines (78% reduction) per Phase 3b. Applies community best practices: project-specific operational rules only; tutorials/marketing/long reference tables moved out; pointer-heavy structure. User-locked decisions: §1 identity updated to "enterprise AI-directed legal operations intelligence platform"; §11 System Entry Points + §12 Context Layer Hierarchy kept inline (user judgment); §13 Knowledge Repository section added pointing at `spaarke/knowledge/` + `researcher` subagent for rapidly-evolving Microsoft platform topics; Rigor Level template kept inline; Hooks: Current Guidance compressed to one paragraph.
- 5 internal contradictions resolved in the rewrite (Hooks System vs Current Guidance; trigger phrases in 2 places; Before-Starting-Work vs Working-Checklist; etc.).
- **Auth v2 pre-flight** — 11 in-scope references updated to point at the new `DEPRECATED-*` filenames with "⛔ DEPRECATED — superseded by Spaarke Auth v2" markers: `.claude/patterns/auth/INDEX.md`, `.claude/patterns/INDEX.md`, `.claude/constraints/auth.md`, `.claude/patterns/auth/spaarke-sso-binding.md`, `.claude/patterns/webresource/{code-page-wizard-wrapper.md, full-page-custom-page.md}`, `.claude/skills/code-page-deploy/SKILL.md`, `docs/architecture/sdap-auth-patterns.md`, `CROSS-REFERENCE-MAP.md`, `src/solutions/SpaarkeAi/src/App.tsx`, `src/solutions/Reporting/{main.tsx, services/authInit.ts, config/runtimeConfig.ts, config/reportingConfig.ts}`. Historical `projects/*` references, `.claude/archive/`, and the audit doc's rename-action narrative left intentionally unchanged. PF-3. Commit `c2198007`.

### Deprecated
- **Auth v2 pre-flight** — Two fully-superseded auth pattern docs renamed with `DEPRECATED-` prefix so the filename itself is a stop signal in Grep/Glob output:
  - `.claude/patterns/auth/msal-client.md` → `.claude/patterns/auth/DEPRECATED-msal-client.md`
  - `.claude/patterns/auth/spaarke-auth-initialization.md` → `.claude/patterns/auth/DEPRECATED-spaarke-auth-initialization.md`
  Both files will be removed when v2 ships (Workstream F4, task 094). PF-1, PF-2. Commit `c2198007`.

### Removed
- The 22 extract-candidate sections totaling ~720 lines from old `CLAUDE.md`. Content remains preserved in `.claude/archive/2026-05-17/CLAUDE.md`. Topics removed: detailed Adaptive Thinking tutorial, Permission Modes tutorial, Hooks System tutorial, Headless Mode, Agent Teams (experimental), Component Skills note (now in `.claude/skills/INDEX.md`), Trigger Phrases table, Slash Commands table, Coding Standards code samples (in `docs/standards/`), Repository Structure tree (in `README.md`), ADR summary table (in `.claude/adr/INDEX.md`), Quality Gates with Hooks (feature not configured), and dated/duplicate sections.

### Fixed
- N/A — Phase 3a/3b are restructuring; no behavioral fixes in this scope.

### Verified
- **Auth v2 pre-flight** — `projects/spaarke-auth-v2-and-hardening/CLAUDE.md` "🚨 ACTIVE AUTH V2 REFACTOR — DO NOT REGRESS" section cross-checked against audit §8.2 Layer 3 (PF-11) requirements. All MUST/MUST NOT bullets present plus extras (/debug endpoint ban, plain-text secret ban, INV-1..INV-8 preservation). No edits required. PF-11. Commit `f58317b0`.

### Retirement note
- All "Auth v2 pre-flight" entries above (PF-1..PF-13) are transitional. They will be retired during Workstream F (Engineering canonical docs): F1 ships ADR-027, F2 partial-rewrites `spaarke-sso-binding.md`, F3 ships `docs/guides/auth-deployment-setup.md`, F4 deletes the `DEPRECATED-*` files and removes the STOP banners + project CLAUDE.md prohibition + root CLAUDE.md pointer row. See `.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md` §8.4–§8.5.

---

## [ai-procedure-quality-r1] - planned for 2026-05-XX

---

## [ai-procedure-quality-r1] - planned for 2026-05-XX

> Entry will be promoted from `[Unreleased]` when the project's PR #294 merges. The deliverables below are the planned set.

### Added
- `.claude/agents/researcher.md` — Opus, effort: high researcher subagent for deep-dive Microsoft platform investigation; accumulates findings via project memory (`MEMORY.md`). Per design.md Directive 1. (Task 010)
- `.claude/skills/_template/SKILL.md` — canonical skill scaffold enforcing the 7 best practices; new skills clone this; existing skills are measured against it during Phase 2a audit. (Task 011)
- `.claude/CHANGELOG.md` — this file. Forward-only convention. (Task 012)
- `.claude/FAILURE-MODES.md` — repo-level catalog of cross-cutting failure patterns. 4 inaugural entries derived from 2026-05-14 incidents. (Task 013)
- `.claude/archive/` directory with date-organized subdirectory convention; reversibility-first removal pattern. (Task 014)
- `scripts/quality/Validate-SkillReferences.ps1` — Light reference check across all 49 skills (file paths, URLs, skill names). Runs in CI; <10s. (Task 065)
- `scripts/quality/Find-SkillReferenceDrift.ps1` — 7-surface drift detector; catches broken refs after rename/split/merge. (Task 066)

### Changed
- Root `CLAUDE.md` rewritten to the tiered target (<200 lines). Reference content moved to subdirectories. The pre-rewrite version is preserved in `.claude/archive/2026-05-14/CLAUDE.md`. (Phase 3b deliverable)
- Multiple skills refined per `.claude/AUDIT-FINDINGS-SKILLS.md`. Specific refactors listed under each skill in the per-skill section of the audit findings. (Phase 2b deliverable)

### Removed
- Skills audit-recommended-and-approved for removal (specific list determined at Human Gate 1). Folders archived to `.claude/archive/2026-05-14/skills/<name>/`, not deleted from disk. (Phase 2b deliverable)

### Fixed
- N/A — Phase 0 inventory surfaced existing issues (5 failing workflows, 3 PCFs with wrong `build:prod`, etc.) but their fixes are in separate scope from this project.

---

*Established 2026-05-14 by project `ai-procedure-quality-r1` (task 012). See [.claude/archive/README.md](archive/README.md) for the reversibility convention referenced above.*
