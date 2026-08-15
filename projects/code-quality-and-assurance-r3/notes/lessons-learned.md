# Lessons Learned — code-quality-and-assurance-r3

> **Program**: standing quality program (r1 system → r2 structural → r3 program). Single worktree,
> surfaces-as-workstreams, assessment-first with mandatory Fable adversarial verification.
> **Closed**: 2026-08-14.

## What worked

1. **Assessment-first with a hard Fable adversarial-verify gate.** The `quality-assessment` Workflow's
   mandatory Fable verification caught **2 real BFF production bugs** AND corrected **2 false-positive
   "dead code" claims that were load-bearing**. An un-verified assessment would have both missed real
   defects and deleted live code. The adversarial pass is the single highest-leverage practice — never
   skip it.
2. **A gating rubric produces honest signal.** Capping the aggregate by D2/D3 (`min(mean, D2, D3)`) turned
   one live unauthenticated endpoint into an F headline — which correctly refused to let a C+ mean hide a
   security hole. When that one endpoint was fixed, the aggregate moved F→D and the honest C+ maintainability
   mean showed through. The "A 95/100" it superseded was stale and unverified.
3. **Prove the harness on one surface before fanning out.** The `quality-assessment` Workflow had 4 runtime
   bugs (JSON-string args, null-finder crash, banned `new Date()`, null verify/synth derefs). Running it on
   ONE surface first caught all 4 at **0 wasted agent cost** before the parallel fan-out.
4. **Forcing-functions as fitness functions, not prose.** The durable deliverable is executable: 4 ArchTests
   (downcast ban, God-class ceiling, ADR-013 boundary, layer direction), C# analyzers-as-errors, config
   fail-fast (ValidateOnStart), and a naming-conformance script — each fails a build/PR on re-drift. Docs
   drift; tests don't.
5. **Re-grounding at HEAD beats trusting the POML.** Task 061's POML (grounded pre-net10-merge) claimed ~37
   `Configure<>` validation gaps; at net10 HEAD the merge had already closed most of them. Re-running the
   census at head avoided ~20 redundant/duplicate edits and a big-bang.

## What was hard / what to change

1. **Cross-worktree CI ownership is a real serializer.** Tasks 042/063's `.github/workflows` wiring is owned
   by an active worktree (`ci-cd-unit-test-remediation-r1`) that edits those files continuously. The correct
   move was to author the gates + defer the wiring to a coordinated PR (CLAUDE.md §6.5 Path A) — NOT edit
   over in-flight changes. Lesson: for CI-touching forcing-functions, **make the gate runnable standalone
   first** (a script/ArchTest that runs in the existing suite) so its value lands even when the workflow-file
   wiring must wait for a coordination window.
2. **A stuck background subagent can burn a lot of wall-clock silently.** Task 061's dispatched agent ran
   across an entire compaction producing zero disk edits (over-exploring). Lesson: check `git status` +
   `TaskOutput` early; if a mechanical edit-only agent has zero disk output after a reasonable window, take
   it over in the main session rather than waiting.
3. **The BingGrounding trap is subtle and recurring.** Bare `[Required]` on a kill-switch-gated option class
   evaluates eagerly on `.Value` and crashes the disabled boot path (the 2026-06-09 incident). Config
   fail-fast for gated options MUST use `IValidateOptions<T>` that short-circuits when disabled — never bare
   `[Required]`. Codified in task 061's exemption list (the task-040 allowlist).
4. **"Done" for a standing program ≠ "A+".** The chartered A+ target was not reached in one cycle; it is
   gated on deferred live-env items (plugins decommission, web-resource live validation) + per-surface TS
   activation. Honest close: publish the real grade (D, un-gated from F, C+ mean), ship the forcing-functions
   that prevent re-drift, and record the residuals — do not paper over the gap with an aspirational grade.

## Deferrals handed off (tracked, not dropped)

- **Live-env**: plugins `BaseProxyPlugin` decommission (D3=D cap); Finance web-resource MSAL token flow live
  validation (`notes/task-023-notes.md`); naming current→canonical rename application
  (`notes/task-063-naming-standard-r1-handoff.md`) — all owned by the deployment/`customer-provisioning-
  orchestration-r1` track.
- **CI wiring**: 042/063 `.github/workflows` gate wiring → coordinated PR with `ci-cd-unit-test-remediation-r1`
  (`notes/task-042-063-ci-gate-wiring-deferral.md`).
- **Per-surface TS mechanical baseline** (`--max-warnings 0` + `no-console`): ongoing per-surface activation
  (`notes/task-041-mechanical-baseline-activation.md`).
- **NG1** (Dataverse access-stack unification + #3b shared-lib ClientSecret→MI): assess-then-decide on
  task 011 (Idea #742).
- **#772** deferred package majors; **033** residual console/email sites; **032** PDF-parse smoke test.
