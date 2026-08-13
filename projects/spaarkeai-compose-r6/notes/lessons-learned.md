# Lessons learned — spaarkeai-compose-r6 (Render-on-Save)

> Written at wrap-up (090), 2026-08-13. Project ran 2026-08-05 → 2026-08-13: 30 tasks, 8 phases,
> 3 merge PRs (#745, #747, #748), one mid-project production deploy (2026-08-06) + full-surface
> deploy (2026-08-07), UAT on a REAL signed NDA.

## What worked (keep doing)

1. **Eliminate-by-construction beats patch-by-divergence.** Four projects (R3→R5) patched the
   anchor-reconciliation 422 one divergence at a time; R6 removed the bug class in one re-architecture
   (save renders from the canonical model — nothing to anchor against). The operator's "do not
   re-litigate" lock on this decision kept the project from sliding back into tactical patching.
2. **The mid-project re-sequence (2026-08-05) was the single best schedule call.** A Step-2 code trace
   on task 010 found a dependency inversion (render-from-model needs the docx→model projection 020 +
   hard-tier degradation 026 FIRST); re-ordering to model-first prevented re-shipping the UAT #1A
   SEV-1 fidelity regression. Lesson: trust the task-execute code-trace step over the original WBS order.
3. **Step 9.5 background review agents on committed SHAs earned their cost every single round.**
   9 reviews, all PASS-WITH-FINDINGS, each with ≥1 genuine catch (wire gap 040; version-skew replace
   vector A-HIGH-1 041; apply-template exposure; vacuous chrome assertion MED-1 042; missing flow
   tests). The pattern implement → commit → review-on-SHA → triage-fix-commit is now proven house style.
4. **Real-document UAT (operator's signed Corteva NDA) beat every synthetic fixture.** It surfaced the
   fidelity-widener priority order (indentation ×84, paragraph-style ×85) that no synthetic doc showed,
   and validated the loud-degradation principle end-to-end (nothing failed silently).
5. **"Best fidelity on common cases; rare shapes degrade LOUDLY, never silently"** as an operator-stated
   principle resolved at least four would-be design debates (stacked revisions, U+FFFD glyphs,
   table-slot overlaps, PDF reflow) without escalation churn.
6. **Seam-first testing at PublicContracts boundaries** (`ComposeFidelitySeamFixture` + boundary doubles
   for template source and PDF intake at the SAME facade seam) made through-the-wire proof cheap: the
   042 round-trip suite drove the REAL projector → renderer → canonical hub → endpoints with only the
   Azure-DI call doubled.
7. **The atomic BFF + `sprk_spaarkeai` deploy window** (~1 minute apart) plus live-bundle marker probes
   made deploy verification objective — grep the live bundle for distinctive strings instead of trusting
   deploy logs.

## What to change (do differently)

1. **Run the FULL unfiltered test suite before every merge to master.** The I-7 lexical audit
   (`ComposeWritePathTextSearchAuditTests`) sat outside the `FullyQualifiedName~Services.Compose`
   filters used mid-task, so a guard using `.EndsWith(".pdf", …)` merged to master latently red
   (#747, merged with `--admin` before CI finished). Filtered runs are fine mid-task; the merge gate
   needs the whole project. (The repair was trivial — `Path.GetExtension` + `string.Equals` — but the
   red should never have reached master.)
2. **Anti-clobber verify must be the FIRST deploy step, not a late one.** The 2026-08-06 deploy hit a
   failed anti-clobber check (the r2 session had deployed 5× that day) and needed a live operator
   decision (option A: freeze r2, deploy R6, r2 rebases over). It resolved cleanly, but discovering it
   at deploy time compressed the decision. Sibling-worktree deploy intent should surface at task START
   (the /conflict-check hot-path output could carry "last deployed by/at" per surface).
3. **Design UAT affordances alongside machinery.** Comment round-trip shipped seam-proven (024/026) but
   UAT couldn't exercise it — no "Add Comment" toolbar button existed (D7). A feature isn't UAT-able
   until its UI entry point exists; the DoD for a round-trip feature should include the affordance.
4. **Fork semantics need design-time naming rules.** D1 (duplicate Documents rows) traced to the
   "Save New Document" fork reusing the UNCHANGED filename → Graph PUT-by-path coalesced onto the same
   driveItem. Any fork/copy affordance must uniquify its target name by construction (now an R7 UC).
5. **Cross-project method-level merge unions deserve a dedicated review lens.** Both FR-C3 (dedup,
   master) and B-MED-3 (association inheritance, this branch) inserted into `PromoteIfEphemeralAsync`;
   the union resolved cleanly, but nothing structural guaranteed the combined ordering/exception
   behavior — the 090 cross-slice review exists to catch exactly this class.

## Coordination outcomes (sibling worktrees)

- **assistant-enhancements-r2**: the 2026-08-06 freeze (operator option A) worked as designed — r2
  merged/rebased over R6 (#749) and redeployed 2026-08-07→13; wrap-up verified the live artifacts are a
  strict superset carrying BOTH R6 markers and r2's newer auth work. The R6 090 deploy step therefore
  resolved as a verified no-op (redeploying would have clobbered r2's active debugging — the exact
  anti-clobber scenario the constraint exists for).
- **compose-r5 and older compose worktrees**: no collisions; `/conflict-check` before every BFF PR ran
  clean each time (zero file overlap even with #690's Compose-LFS CI fix).
- **ci-cd-unit-test-remediation-r1 lineage**: ADR-038 discipline held — R6's 281 tests dieted to ZERO
  scaffolding (see `notes/test-diet-report.md`); the seam suites are the project's product, not
  scaffolding around it.
- **spaarke-ai-architecture-redesign-r2** (sole owner of `Services/Ai/`): honored via PublicContracts
  facades only (`IComposePdfIntakeSource`, `IComposeTemplateSource`, `DocumentLayout`) — zero forks of
  AI internals into Compose.

## Handed forward

- **Defer register (consolidated)**: `projects/spaarkeai-compose-r7/notes/r6-defer-register-consolidated.md`
  (D1–D9 dispositions, engineering ledger, fidelity wideners, fast-follow scope, telemetry-triggered
  cleanups).
- **D9 diagnosis handoff** to assistant-enhancements-r3:
  `projects/spaarkeai-assistant-enhancements-r3/notes/assistant-viewport-clipping-open-in-compose-handoff.md`.
- **R7 design** (Editor UX: template picker, save semantics, autosave, name prompt, hotkeys) exists at
  `projects/spaarkeai-compose-r7/design.md` — several D-register items map directly onto its use cases.
- **Open operator decision**: Corteva NDA confidentiality sign-off for corpus row 4 (file remains
  untracked until then).
