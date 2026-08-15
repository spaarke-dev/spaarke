# Tasks 042 + 063 — CI-Gate Wiring: Coordinated-PR Deferral

> **Date**: 2026-08-14
> **Applies to**: task 042 (CVE + publish/bundle-size + doc-drift + ArchTest + Graph-parity gates) and
> task 063 (naming-conformance gate) — specifically their `.github/workflows/**` wiring steps.
> **Resolution path**: CLAUDE.md §6.5 **Path A** (documented project-scoped deferral) — this is the
> POMLs' own escalation trigger, not an omission.

## Why the workflow-file edits are deferred (binding)

Both tasks' final step wires a gate into the **existing** CI layers (`sdap-ci.yml`,
`ci-tier1-blocking.yml`, `ci-tier2-advisory.yml`, `nightly-health.yml`). Those files are **owned by the
active worktree `ci-cd-unit-test-remediation-r1`** for the 28-day window (`projects/INDEX.md`:
"`ci-cd-unit-test-remediation-r1` owns existing CI workflow modifications"). They were **modified today
(2026-08-14 15:03)** — in-flight. Task 042 additionally forbids the new-file workaround ("wire into the
EXISTING R1 layers — no parallel workflows"), so the sanctioned add-a-new-file pattern does not apply.

Task 042 Step 1 + its escalation trigger, and task 063's `parallel-reason`, both require coordinating
every `.github/workflows` edit with `ci-cd-unit-test-remediation-r1` via `/conflict-check` **before**
the PR, and **STOP rather than edit over their in-flight changes**. That condition is met → the
workflow-file edits are deferred to a coordinated PR. (This worktree is also not being pushed, so a CI
edit could not take effect regardless.)

## What IS done now (not deferred)

- **042 — config-validation gate is LIVE via the existing suite.** Per task 042's own constraint, "the
  config-validation gate IS the ArchTests run itself." Task 040's fitness functions (incl. rule (a)
  customer-critical-IOptions-validated-on-start, consuming task 061's exemption list, and rule (c)
  no-secret-Dataverse) run inside `dotnet test tests/Spaarke.ArchTests/**`, which the existing CI
  `build-test`/tier-1 job already executes on every PR. No workflow edit is required for this gate to
  gate. ✅
- **063 — standard + gate authored + self-tested.** `docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md`
  § "KV-Secret & Resource Naming Standard (Conformance-Gated)" + `scripts/naming-conformance-check.ps1`
  (`-SelfTest` passes; real-file scan correctly reports the live FR-29 drift). Runnable locally and in
  the coordinated CI PR. r1 handoff recorded (`task-063-naming-standard-r1-handoff.md`). ✅

## The coordinated-PR checklist (for whoever wires these with ci-cd-r1)

Each gate below is authored/available now; wiring adds a step into an existing layer. All are
**advisory/per-surface** until their precondition is met (never red before their owning task lands):

| Gate | Layer | Precondition | Owning task |
|---|---|---|---|
| ArchTests fitness functions | PR (build-test) — **already runs** | 040 landed ✅ | 040/042 |
| Naming-conformance (`naming-conformance-check.ps1`) | PR — **advisory** until r1 remediates live drift | 063 ✅; r1 remediation | 063 |
| CVE scan (`dotnet list --vulnerable`) | PR/nightly | none (net10 graph currently 0 vulns) | 042 |
| Publish-size budget (≤60 MB compressed BFF) | PR | none | 042 |
| Doc-drift | nightly | none | 042 |
| Graph app-role parity (`GraphAppRoleVerifier` vs env SP) | **nightly only** (live-Graph; NOT per-PR) | 062 landed ✅ + Graph creds present; skip/no-op cleanly otherwise | 042/062 |

**Coordination action**: open ONE CI PR against the ci-cd-r1-owned workflows, `/conflict-check` first,
and have ci-cd-r1 review file ownership. Do not enable any gate repo-wide while a surface is still dirty
(design §4A per-surface activation); each gate flips blocking per-surface as it reaches zero findings.
