---
description: Execution guide for Claude Code to improve the Spaarke test suite — extends /test-diet, adds test-debt controls to /project-pipeline (/task-create, /task-execute), strengthens CI/CD flake handling, and defines an incremental (non-audit) strategy for paying down debt across ~11,000 existing tests
tags: [testing, test-debt, ci-cd, project-pipeline, task-create, task-execute, test-diet, adr-038]
status: draft — requires ground-truth pass before execution
last-authored: 2026-08-28
---

# How to Improve the Spaarke Test Suite

## 0. How to use this document

This is a planning and execution guide, not a spec ready to implement verbatim. Before touching any file, Claude Code must read the repo to confirm every path, skill name, and existing mechanism referenced below. Where a detail is genuinely unknown from this document alone, it is marked `<CONFIRM>` (verify by reading the repo) or `<PLACEHOLDER>` (decide and fill in during execution, then update this doc and the governing files together). Do not fabricate paths, ADR numbers, or skill names not already confirmed to exist.

Ground-truth checklist to run first:
- Read `.claude/skills/test-diet/SKILL.md` in full (the version this document was written against is summarized in §1).
- Read `.claude/skills/task-execute/SKILL.md`, `.claude/skills/task-create/SKILL.md` <CONFIRM path>, and `.claude/skills/project-pipeline/SKILL.md` <CONFIRM path>.
- Read `docs/adr/ADR-038-testing-strategy.md` in full, not just §7.
- Read `.claude/constraints/testing.md` and `tests/CLAUDE.md`.
- Confirm the current CI/CD platform <CONFIRM: GitHub Actions / Azure DevOps / other> and where its config lives.
- Confirm whether a mutation-testing tool is already installed or evaluated for this stack (.NET → likely candidate is Stryker.NET; <CONFIRM> whether it's present, and whether a spike/ADR already exists on this).
- Get an actual current test count and flake rate if telemetry exists, rather than assuming the ~11,000 figure is exact or evenly distributed across modules.

Everything below is organized by where in the pipeline the change lands, in the order a rollout should probably happen (§7 gives a suggested sequence). Each governance change described here must be accompanied by the corresponding update to `CLAUDE.md` hierarchy, constraint files, and/or ADR-038 — per standing principle, a change that isn't reflected in governed files is not reliably honored by Claude Code regardless of what this document says.

---

## 1. Baseline: what `/test-diet` already does

`/test-diet` is a **project-close, structural scaffolding classifier**. At `090-wrapup-*`, it enumerates test files touched since the project's start commit, classifies each test method against the ADR-038 §7 seventeen-ban list (B1–B17) as MAINTAIN, SCAFFOLDING, AMBIGUOUS, or PATH-VIOLATION, and emits (but does not execute) `git rm`/`git mv` recommendations plus a report at `projects/{project-name}/notes/test-diet-report.md`. It is read-only by default, cites the specific ban that triggered each classification, and is wired into `task-execute` Step 11 as a hard warning if skipped.

What it structurally cannot see, because none of the 17 bans test for it:

- **Coupling / isolation** — shared mutable fixtures, shared mocks, or execution-order dependency across test files. This is the dominant root cause of flakiness that appears "degrees removed" from the code that actually changed.
- **Fault-detection redundancy** — three cosmetically distinct tests that all kill the same mutants. A test can pass every B1–B17 check and still contribute zero unique verification value.
- **Spec alignment** — whether a MAINTAIN-classified test actually corresponds to something the task's acceptance criteria asked for, versus breadth padding generated as uncertainty-hedging.
- **Anything outside the current project's touched-file scope** — by design. Legacy tests untouched by the current project are invisible to it, which is the correct default for a per-project skill but means it cannot be the sole mechanism for paying down debt across the other ~10,000+ tests it never looks at.

Sections 2–5 close these gaps at different pipeline stages. Section 6 is specifically the "clean up as we go" strategy for the existing 11,000-test backlog, since a comprehensive audit is not the right tool here.

---

## 2. Enhance `/test-diet`

### 2.1 Add an isolation/coupling check (new ban class, distinct from B1–B17)

The 17 bans classify test *shape*. Isolation is a different axis and should not be forced into the existing ban numbering — add a parallel check, e.g. `I1`–`I4` <PLACEHOLDER: confirm naming convention with ADR-038 owner>, run as Step 3.5:

- **I1 — Shared mutable fixture**: test references a static field, singleton, shared DB seed, or fixture class also referenced by a test outside the current file, without documented per-test isolation (fresh instance, transaction rollback, or explicit cleanup in teardown).
- **I2 — Order-dependent assertion**: test's assertion is only valid given a specific prior test having run (detectable heuristically by checking for asserted state not set up within the test's own arrange section).
- **I3 — Unregistered shared mock**: test constructs or references a mock/fixture that appears (via text search) in 3+ other test files but has no entry in the shared-fixture registry (see §4.2).
- **I4 — CI-observed flake**: test's name/file matches an entry in the CI flake registry (see §5.2) from the last `<PLACEHOLDER: N>` build cycles.

Classification output for I1–I4 should be **ISOLATION-SUSPECT**, separate from SCAFFOLDING, since the fix is usually refactor-to-isolate rather than delete. The report format in §"Emit reconciliation report" should get a fifth table for this class, with a proposed fix action (extract fixture, add teardown, move to per-test setup) rather than a delete/move command, since auto-suggesting deletion of a coupled-but-otherwise-valid test would be the wrong default.

### 2.2 Add mutation-informed redundancy detection

For the touched-test scope only (not a full-repo run — too expensive per project-close pass), run a scoped mutation analysis limited to the production code paths the touched tests exercise. <CONFIRM tool choice; Stryker.NET is the standard candidate for a dotnet/xunit stack per the tech stack declared in this skill's frontmatter.> A test that kills zero mutants not already killed by another test in the same touched set is redundant in the formal sense (its removal doesn't change the mutants caught by the suite) and should be flagged **REDUNDANT** rather than folded into SCAFFOLDING, since it may be structurally clean (good name, isolated, low setup ratio) and simply add no unique verification value. This is the check that catches the "three near-identical AI-generated happy-path variants" pattern that B1–B17 structurally cannot, because all three can individually look fine.

Keep this scoped and optional-by-flag initially (e.g. `/test-diet --with-mutation`) until runtime cost against a real touched-set size is measured, rather than making it default behavior on an unverified cost assumption.

### 2.3 Cross-reference against spec acceptance criteria

Step 3 already loads `projects/{project-name}/spec.md`. Extend the MAINTAIN path: for each MAINTAIN-classified test, check whether its scenario corresponds to something named in the spec's acceptance criteria (string/keyword match is enough as a first pass — this doesn't need to be exact). Tests with no traceable link to stated acceptance criteria get a new **UNSCOPED** flag — not an automatic delete or even AMBIGUOUS, since legitimate defensive tests exist outside the letter of the spec, but a visible signal in the report so a reviewer can see how much of the touched-test set is padding versus spec-driven.

### 2.4 Full-repo mode, separately triggered

Add a distinct invocation mode — `/test-diet --full-repo` <PLACEHOLDER: confirm flag name> — that is NOT part of the default wrap-up flow, triggered instead on a size/time cadence (e.g., quarterly, or after every `<PLACEHOLDER: N>` projects) and run against the whole suite rather than the touched-file scope. This is the mechanism that eventually reaches legacy tests the per-project mode never sees. It should reuse the same B1–B17 plus I1–I4 plus mutation-redundancy logic, just against a much larger scope, and should expect to run as a scheduled CI job rather than inline in a wrap-up task given the runtime cost of full-suite mutation analysis. See §6 for how this interacts with the incremental cleanup strategy — full-repo mode is a periodic backstop, not the primary debt-reduction mechanism.

---

## 3. `/project-pipeline`, `/task-create` — prevent debt at authoring time

The cheapest place to prevent test debt is before it's written. Two additions to POML task specs, enforced at `/task-create`:

**Test-scope clause.** Every task's acceptance criteria should state the behaviors and edge cases in scope for test coverage, not just the production-code behavior. This gives both the model generating tests and `/test-diet`'s new UNSCOPED check (§2.3) something concrete to check against. Frame it as intent, not a hard count — "cover the stated contract and the edge cases named below; anything beyond that needs a one-line justification in task notes" — since a numeric cap invites gaming (satisfying the number with padding) rather than preventing it.

**Fixture ownership declaration.** If a task introduces a new shared fixture, mock, or test-data builder likely to be reused (heuristic: touches more than one test class, or is placed somewhere other than the test file's own directory), the task must declare it in the shared-fixture registry (§4.2) with an owner and a one-line description of its intended reuse scope. This is the upstream half of what `/test-diet`'s I3 check enforces downstream — cheap to require at creation time, expensive to reconstruct later once fifteen files depend on an undocumented fixture.

`/task-create` should read `.claude/constraints/testing.md` as part of its own context-gathering (if it doesn't already <CONFIRM>), so these two requirements are enforced at generation time rather than caught only in review.

---

## 4. `/task-execute` — catch coupling and duplication at write time

### 4.1 New Step 9.5 gate: isolation check

Add a specialist reviewer (or extend an existing one — check first per the inventory-before-generation principle whether an existing Step 9.5 reviewer is a natural home for this rather than adding a new one <CONFIRM current Step 9.5 reviewer roster before deciding>) that runs the I1–I4 checks from §2.1 against tests written in the current task, before they're committed. This is strictly cheaper than catching the same coupling at wrap-up or, worse, three tasks later when an unrelated module starts flaking in CI — the cost of a coupling defect scales with how long it sits undetected.

### 4.2 Shared-fixture registry

A single file, e.g. `tests/shared-fixtures-registry.md` <PLACEHOLDER: confirm location/format — could also be a lightweight JSON/YAML manifest if that's easier for tooling to parse>, listing every fixture/mock/test-data builder intended for reuse across test classes, its owner, and its intended scope. This is the concrete artifact that makes both `/task-create`'s declaration requirement (§3) and `/test-diet`'s I3 check (§2.1) enforceable rather than aspirational — without a registry, "is this fixture shared or task-scoped" is a judgment call every time; with it, it's a lookup.

### 4.3 Lightweight duplicate-test similarity check

At the same Step 9.5 gate, a cheap structural similarity pass (not full mutation analysis — that's reserved for `/test-diet`'s optional mode) comparing new test bodies against existing tests in the same file/class, flagging near-duplicates for human confirmation before commit. This is the per-task, low-cost substitute for catching the "three near-identical variants" pattern incrementally, rather than only in a batch at wrap-up.

---

## 5. CI/CD — make flakiness observable and self-correcting

### 5.1 Rerun-on-failure with flake logging

If not already in place <CONFIRM current CI retry behavior>, configure the test runner to rerun a failed test up to `<PLACEHOLDER: N, e.g. 2-3>` times before failing the build, and log every test that passed on retry as a flake observation (test name, build ID, failure message, timestamp) rather than silently absorbing the flake. A test that "eventually passes" and is never logged teaches the team nothing; the same test logged every time it flakes builds the dataset the rest of this section depends on.

### 5.2 Flake registry

Aggregate the rerun-on-failure log into a queryable registry — a simple table (test name, first-observed date, occurrence count, current status) is enough to start; it doesn't need to be sophisticated. This is the data source for `/test-diet`'s I4 check and for CI's own auto-quarantine policy below. <CONFIRM whether existing CI/observability tooling already has a place for this, e.g. a dashboard or a table in an existing telemetry store, before standing up something new — extend, don't rebuild.>

### 5.3 Auto-quarantine policy

A test observed flaking `<PLACEHOLDER: N times, e.g. 3>` within `<PLACEHOLDER: window, e.g. 30 days>` gets automatically tagged (e.g. `[Flaky]` / `[Trait("Quarantined", "true")]`) and excluded from the blocking suite, with an issue filed automatically referencing the flake registry entry. This protects CI trust — a red build that's "probably just that flaky test again" is worse than no signal at all, because it trains the team to ignore red builds generally. Quarantine is not resolution; it buys time without hiding the debt, and the filed issue is what prevents quarantine from becoming a graveyard (see §6.4 for how quarantined tests re-enter the paydown cycle).

### 5.4 Mutation testing as a scheduled job, not a per-PR gate

Full or even module-scoped mutation analysis is too slow for per-PR CI. Run it as a scheduled job (nightly or weekly <PLACEHOLDER>) against recently-changed modules, feeding results into the same reporting surface as `/test-diet --full-repo`, rather than trying to gate merges on it.

---

## 6. Incremental test-debt cleanup — the ~11,000-test backlog

A full audit of 11,000 existing tests is the wrong tool: too slow to run, too expensive to review, and by the time it finished, more debt would have accumulated behind it. The right model is a small, forced, recurring paydown quota plus opportunistic sweeps triggered by proximity to work already happening — clean up what you touch and what you're near, on a steady cadence, rather than trying to clean up everything at once.

### 6.1 Touch-radius expansion (opportunistic, near-zero marginal cost)

`/test-diet`'s per-project scope is currently "tests added or modified during the project." Expand this slightly to "tests added or modified, **plus** pre-existing tests in the same file or class as a touched test." If a task edits one method in an existing test file, the other untouched methods in that file get swept by the same classification pass. This costs almost nothing extra (the file's already open) and steadily reaches legacy tests purely as a side effect of ordinary development touching nearby code over time.

### 6.2 Blast-radius sampling (targeted, uses data you're already building)

When a task modifies module X, use the shared-fixture registry (§4.2) and flake registry (§5.2) to identify tests **coupled to X but not directly touched by the task** — tests sharing a fixture with X's tests, or tests in modules with an observed flake history correlated with changes to X. Run the I1–I4 isolation check (not full mutation analysis) against just that coupled set as part of the same wrap-up pass. This directly targets the "degrees removed" flakiness problem, using exactly the coupling data the registry exists to capture, without expanding into a full audit.

### 6.3 Fixed-quota rotation (deliberate, bounded, guarantees forward motion)

Add a small mandatory quota to every `090-wrapup-*` task, independent of what that project touched: review `<PLACEHOLDER: N, e.g. 15-25>` tests from a rotating backlog queue (oldest-reviewed-first, or worst-flake-history-first) against the full B1–B17 + I1–I4 classification. This is the mechanism that guarantees the backlog shrinks over time even for modules nobody happens to touch organically. Track a single visible metric — a **test-debt ledger**: total tests reviewed-and-reconciled vs. total tests outstanding — updated by every wrap-up, so progress against the 11,000 is visible without needing a full audit to know where things stand. <PLACEHOLDER: decide where this ledger lives — a file under `docs/`, or a dashboard if the CI telemetry work in §5 gives you a natural home for it.>

### 6.4 Quarantine is a paydown queue, not a dead end

Every test auto-quarantined by §5.3 should feed directly into the §6.3 rotation queue, prioritized ahead of the general oldest-first ordering — a test flaky enough to trigger auto-quarantine is higher-value to fix than an average legacy test, precisely because it's already causing measurable CI cost. This closes the loop between "observed in production CI" and "gets reviewed," which is the piece that prevents quarantine from silently accumulating an ever-growing pile of ignored tests.

### 6.5 What this deliberately does not attempt

No component of this plan tries to mutation-test all 11,000 tests, no component tries to manually review the whole backlog in one pass, and full-repo mode (§2.4) is explicitly a periodic backstop rather than the primary mechanism — it exists to catch cross-project coupling the touch-radius and blast-radius mechanisms can't see by construction, not to replace them. The combination of touch-radius expansion, blast-radius sampling, fixed-quota rotation, and quarantine-priority is what makes the debt shrink continuously without ever requiring a stop-the-world audit.

---

## 7. Suggested rollout sequence

Ground-truth pass (§0) first, always. After that, roughly in order of cheapest-and-most-preventive to most-expensive-and-most-corrective, since upstream prevention reduces the size of every downstream cleanup problem this document defines:

1. §3 (`/task-create` test-scope clause + fixture declaration) and §4.2 (fixture registry, even empty/seeded) — cheapest, stops new debt fastest.
2. §5.1–5.2 (rerun-on-failure + flake registry) — needed as a data source before I4 and blast-radius sampling (§6.2) can work at all.
3. §4.1 and §4.3 (`task-execute` isolation + duplicate checks) — needs the fixture registry from step 1 to be useful.
4. §2.1 (`/test-diet` I1–I4) and §6.1 (touch-radius expansion) — needs the registry and flake data from steps 1–2.
5. §5.3 (auto-quarantine) and §6.3–6.4 (fixed-quota rotation + quarantine-priority) — needs the flake registry populated with real signal first, or the quota queue has nothing meaningful to prioritize.
6. §2.2 (mutation redundancy) and §2.4 (`--full-repo` mode) — highest cost, biggest payoff, last because everything above should be shrinking the size of what this eventually needs to cover.

Each numbered item should land as its own task with its own POML spec and its own governance-file update (CLAUDE.md, constraints, ADR-038 amendment where the change affects the seventeen-ban framework itself) — per standing practice, do not batch these into one large ungoverned change.

---

## 8. Governance updates required alongside implementation

- `docs/adr/ADR-038-testing-strategy.md` — needs an amendment (following the A1 pattern already used for the ArchTests carve-out) to formally add the I1–I4 isolation classes, the REDUNDANT/UNSCOPED flags, and the fixture-registry requirement as canonical, not just described in a skill file.
- `.claude/constraints/testing.md` — add the fixture-declaration requirement and test-scope clause as MUST-NOT/MUST bullets, since per standing principle anything not in a constraint file is effectively optional to Claude Code.
- `tests/CLAUDE.md` — update "Expect to Defend at Project Close" section to describe the expanded touch-radius scope and the fixed-quota rotation obligation, so a developer reading module-local context understands wrap-up now touches more than their own diff.
- `.claude/skills/task-execute/SKILL.md` — Step 9.5 roster update (new or extended reviewer) and Step 11 update (expanded `/test-diet` invocation contract).
- `.claude/skills/task-create/SKILL.md` <CONFIRM path> — POML acceptance-criteria template update for test-scope clause.
