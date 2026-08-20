# Component Complexity — evaluate complexity, not line count

> **Status**: Standard · **Owner**: code-quality · **Created**: 2026-08-20 (supersedes the God-class LOC ratchet)
> **Applies at**: task authoring (`task-create` §3.5.6), code authoring, and `code-review`.
> **Enforcement model**: **human judgment at authoring + review; observation (not a hard gate) at CI.**

## The principle

**File size is a *symptom*, not the thing we care about. The thing we care about is *complexity* — whether a
single component is doing too much.** A 2,500-line file that is one cohesive, single-responsibility unit can be
perfectly fine; a 400-line file that tangles five concerns is worse. **A large file is sometimes the right
answer.** So the rule is: **evaluate complexity and cohesion; use size only as a prompt to look, never as the
verdict.**

This replaces the old `GodClassGuardTests` LOC ratchet (retired 2026-08-20). That gate treated line count as the
verdict, froze existing large files at arbitrary values, and blocked normal feature work on active files
(Compose, Chat) with a hard CI failure — the wrong instrument for a gradual, judgment-laden signal in a large,
active codebase. Per ADR-038's own precedent ("coverage = observation, never a gate"), **file size is now
observed, and complexity is evaluated by humans where the work is authored.**

## What "too complex" actually means (evaluate these — NOT the LOC)

A component (class / file / module) is a decomposition candidate when it shows the real smells:

| Signal | Question |
|---|---|
| **Multiple responsibilities (SRP)** | Does it change for more than one reason? Could you name it without "And"/"Manager"/"Helper"? |
| **Low cohesion** | Do its methods/fields cluster into groups that barely touch each other? (Those clusters are the seams.) |
| **High coupling / ctor deps** | Many constructor dependencies (>~8–12) usually means many collaborators = many jobs. |
| **Cyclomatic complexity** | Deeply nested / high-branching methods — hard to reason about and test. |
| **Mixed abstraction levels** | Orchestration + low-level I/O + formatting in the same method/class. |
| **Change-friction / merge-conflict magnet** | Every feature touches it; PRs collide; reviews lose context. |

**Size is a prompt, not a smell.** A file crossing ~2,000 LOC is a good moment to *ask* "is this still one
cohesive thing?" — but the answer may legitimately be "yes, leave it."

## When a large file is legitimately fine (do NOT split these just for size)

- A **cohesive state machine / engine** whose parts genuinely interlock (e.g., an OOXML patch engine).
- An **exhaustive mapping / dispatch table** that is long but flat and single-purpose.
- **Generated or vendored** code.
- A file where the only "decomposition" available is **arbitrary partial-class slicing** that hurts readability
  without separating a real responsibility. (Splitting to satisfy a number is an anti-pattern.)

If a component is large *and* cohesive, that is a **documented decision**, not a violation — say so in the PR.

## The decision heuristic (authoring + review)

1. **Decompose when responsibilities diverge, not when a line is crossed.** Extract the cluster that has its own
   reason to change into its own component (with a name that doesn't need "And").
2. **Prefer extending a cohesive component over splitting it** (CLAUDE.md §11 — default to reuse; don't manufacture
   thin components to dodge a size number).
3. **If it is large but genuinely one job → keep it, and note why** in the PR / design.
4. **If it is large because of accreted concerns → schedule deliberate decomposition** (a task/project, e.g. the
   RED-1/RED-2/RED-4-C seeds), not a reactive mid-feature split.

## How this is enforced (no hard LOC gate)

- **Task authoring** (`task-create` §3.5.6, Component Justification): when a task creates a new component OR
  materially grows an existing one, evaluate complexity/cohesion — design for the right seams up front; don't add
  a new concern to an already-multi-responsibility class.
- **Code authoring**: apply the heuristic above as you write; if a file is accreting a second responsibility,
  extract it then, while the context is fresh.
- **`code-review`**: the maintainability dimension evaluates **complexity/cohesion direction** (is this change
  making the component do more jobs?) and flags decomposition opportunities — but **accepts a justified,
  cohesive large file**. Direction matters more than absolute size (a complex file getting simpler is good).
- **CI = observation only**: a non-blocking report lists large `src/server` files and their trend (see
  `scripts/report-large-server-files.ps1`), feeding the decomposition backlog. It never fails a build.

## Anti-patterns this replaces

- ❌ Freezing files at an arbitrary LOC and failing the build on +100 lines of normal feature work.
- ❌ Hand-bumping a waiver number to get a PR green (gaming a gate that measures the wrong thing).
- ❌ Splitting a cohesive file into partial classes purely to drop under a number.
- ✅ Instead: evaluate *complexity* where the code/task is authored, decompose deliberately when responsibilities
  diverge, and keep a large-but-cohesive file when that is honestly the better design.
