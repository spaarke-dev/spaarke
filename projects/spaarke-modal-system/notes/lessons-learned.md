# Lessons Learned — spaarke-modal-system (2026-08-02)

Project ran P0→P8 in ~2 sessions: P0 build (13 tasks), then all conversions (P0.5–P7, 15 tasks) + wrap-up in one autonomous session using parallel task-execute sub-agents with main-session orchestration, per-wave commits, and per-wave consolidation passes.

## What worked (repeat these)

1. **Preset-gap consolidation as an orchestrator pass.** Agents were told "compose the preset; a gap = REPORT, don't fork." P2's three agents converged on the same gaps (no `busy`, no exported danger class, no `cancelLabel`); the main session closed them once and re-pointed consumers. By P3 this became *pre-emptive*: FormModal was extended (`submitDisabled`/`busy`/`dismiss:'alert'`) BEFORE dispatch, and all three forms landed on the literal preset with zero SprkModal-direct workarounds. **Rule of thumb: after the first conversion wave against a new preset family, budget a main-session preset-extension pass; before later waves, read the preset against the target dialogs and extend first.**
2. **Per-wave build/dist ownership rules.** Parallel agents sharing `Spaarke.UI.Components` were forbidden from rebuilding its dist (tsc-only + scoped jest); the main session did one consolidated rebuild + full suite per wave. Zero dist races across 6 parallel waves.
3. **Escalation-as-legitimate-outcome.** Two POML escalation triggers fired properly (030's v1.1.59 no-X; 051's self-chromed EmailComposer + live legacy consumers) and produced zero source damage — the 051 agent returned with evidence and ZERO edits. Honest deferral (#713) with an interim mitigation (the 720px `md` cap) beat forcing the AC.
4. **Two-write defer discipline paid compound interest.** 5 issues filed (#712 LW build defect, #713 051 deferral, #714 FindSimilar 3-copy collision, #715 WizardShell fork, #716 web-resource drift) — each a REAL pre-existing defect surfaced by conversion work, none swept under the rug, none blocking the project.
5. **A/B (stash) proof for "pre-existing failure" claims.** Every "that failure is baseline" assertion was proven by reverting the wave's diff and re-running — caught one real self-inflicted bug (091's optional-chaining) and prevented several wild-goose chases.
6. **Structural tests for visual invariants.** FR-08 (transform-robust centering) was "un-testable without a browser" until framed structurally: assert the Dialog surface mounts OUTSIDE a `transform:scale(0.9)` ancestor — run under real React 16 in the actual PCF. The load-bearing invariant now has CI-runnable proof.

## What cost time (avoid these)

1. **End-to-end async integration tests in jsdom + React 19 are intrinsically racy.** The one real time-sink (~2h) was `actionConfirmationIntegration.test.tsx` flaking ONLY under full-suite worker load. Two stacked root causes (jest's 5s whole-test default; React 19 scheduler starvation on jsdom's `setTimeout` fallback — no MessageChannel) required five layers: 30s budgets, in-act deferred network release, repeated poll-with-flush, a ref-pinned MessageChannel polyfill (Node auto-`ref()`s ports on listener attach — unref alone silently reverts and hangs the jest worker), and finally `jest.retryTimes(2)` as a documented absorber. **Lesson: when a full-render async pipeline meets jsdom, either polyfill MessageChannel + drive ALL mock async inside act() from the start, or don't test through the real scheduler.** The layered fix + disproven-alternatives record lives in `task-042-completion.md`.
3. **Inventory greps must match every call SHAPE.** 090's `.navigateTo(`-only grep counted 4 sites for 091; the real number was 26 (the `openForm`-shaped helper was invisible). When retiring a module, grep its EXPORTS' call sites, not one API's textual pattern.
4. **"Byte-identical copies" is a claim to verify, not inherit.** The two `sprk_DocumentOperations.js` copies had drifted in 3 regions (#716) despite documentation calling them identical. Diff before you rely.

## Architecture takeaways

- **pcf-safe by exclusion worked exactly as designed**: the family stayed Code-Page-scoped by construction until the P5 PCF consumer materialized; adding `SprkModal` to the allow-list was the anticipated "low cost later" move — with the empirical footnote that PCF webpack needs the `dist/pcf-safe` specifier, not `src/pcf-safe`.
- **The interim-adapter → re-base ladder (P1 → P2+) was worth it.** The owner's visible-controls mandate landed in one wave; later re-bases then *deleted* the interim wiring wave-by-wave. Ship the visible thing first, formalize under it after.
- **The one deliberately-retained hand-roll boundary held**: WizardShell kept its envelope (owner §11-G light-first) and the P4/P6 interplay (061 routing xl through WizardShell props; 080 swapping only defaults) proved prop-contract-preserving alignment beats forced wrapping.
- **`.claude` guidance can silently teach retired anti-patterns**: the webresource pattern file still cited the deleted DOM overlay as its reference impl. Retiring a pattern in CODE requires sweeping `.claude/patterns/**` for docs that teach it (main-session write).

## For the successor / follow-ons

Consolidated follow-on surface: #713 (EmailComposer chrome-suppression + DailyBriefing migration + legacy SendEmailDialog delete), #714 (FindSimilar rename/dedup + dead `embedded` prop), #715 (WorkAssignmentWizardDialog fork), #716 (web-resource drift), #712 (LW fresh-worktree build). The one-time visual review checklist is consolidated in `notes/success-criteria-verification.md` (bottom).
