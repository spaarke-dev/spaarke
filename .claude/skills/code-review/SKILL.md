---
name: code-review
description: Comprehensive code review covering security, performance, style, and ADR compliance — the judgment-layer quality gate that complements CI/CD's mechanical checks
tags: [code-review, quality, security, performance, adr-compliance, judgment-layer]
techStack: [all]
appliesTo: ["**/*.cs", "**/*.ts", "**/*.tsx", "post-task", "pre-merge", "PR review"]
alwaysApply: false
exemplar: none-too-volatile
last-reviewed: 2026-11-03
---

# code-review

> **Category**: Code-quality
> **Last Reviewed**: 2026-11-03
> **Exemplar rationale**: a review is a per-change output; no canonical example stays current.

## When to Use

- The user says "review code", "review my changes" or "code review", or invokes `/code-review {file-or-path}`
- Before creating a pull request
- After completing a task, to validate the implementation (`task-execute` Step 9.5 runs it)

This is a holistic, multi-dimension quality gate: security, performance, maintainability and architectural compliance. `adr-check` covers architecture only. CI covers the mechanical part of review — the CI Router (Tier 1: compile, ArchTests, tenant/eval/Compose gates; Tier 2: format, lint, unit tests, ADR compliance), `ci-standalone-checks.yml` (dependency audit, Release build) and the nightly Trivy scan. This skill is the judgment layer automation cannot replace: design quality, abstraction levels, cohesion and coupling, "is this over-engineered?".

## Inputs

| Input | Required | Source |
|-------|----------|--------|
| Files to review | Yes | Explicit paths, git diff, or current selection |
| Review depth | No | Default "standard"; can be "quick" or "thorough" (see Conventions) |
| Focus areas | No | Default all; can narrow to security, performance, etc. |

If the request names a depth or a narrow focus ("quick", "security review of the auth endpoints", "thorough pre-PR review"), read `examples/invocation-examples.md` before Step 1 to scope it.

## Constraints (MUST / MUST NOT)

- **MUST** run every Workflow step for the scope. A step that does not apply gets one line in the report ("Step 6.7: N/A — no POMLs in the diff"); skipping a step because it "didn't seem to apply" is how reviews miss whole dimensions.
- **MUST** report every finding you make, with severity, confidence and a suggested triage class (Step 0). This skill maximizes coverage; the caller filters.
- **MUST** perform the review when triggered. Checking CI status is not a review.
- **MUST NOT** turn a size metric into a finding (root CLAUDE.md §11.5: judge cohesion, not line count).
- **MUST NOT** read the review-scope limits as limits on fixing. Root CLAUDE.md §8.5: review limits ceremony, never fixing — the caller fixes every F-class finding, however many rounds that takes.

## Workflow

Track the steps; tick each one, or mark it N/A with a reason:

- [ ] Step 0 — Coverage-first contract
- [ ] Step 1 — Scope (and the Step 2.6 baseline)
- [ ] Step 2 — Context
- [ ] Step 2.5 — Quantitative metrics
- [ ] Step 2.6 — Quality direction
- [ ] Steps 3, 4, 4.5, 5 — Security, performance, lint, style
- [ ] Step 5.5 — AI code smells
- [ ] Step 6 — ADR compliance
- [ ] Step 6.5 — BFF hygiene (conditional)
- [ ] Step 6.55 — Enforcement check (when a rule is added or a pattern fixed)
- [ ] Step 6.6 — Component justification (any new surface)
- [ ] Step 6.7 — Task POML completeness (conditional)
- [ ] Step 7 — Technology-specific checks (by file type)
- [ ] Step 8 — Report

### Step 0: Coverage-First Contract (read first)

**At the finding stage, maximize recall — do not self-filter for importance.** Filtering language ("only report significant issues", "be conservative", "don't nitpick") makes a literal-following model investigate just as deeply, find the bug, and then withhold it because it judged it below the bar. That reads as a capability regression but is a harness effect.

When producing findings (Steps 3–7):
- **Report every issue you find, including low-severity ones and ones you are unsure about.** Surfacing a finding that is filtered out later is better than silently dropping a real bug.
- **Annotate each finding with a severity (Critical / Warning / Suggestion) and a confidence.** Low confidence is a label, never a reason to omit.
- **Filtering is the downstream job.** This skill maximizes coverage; the orchestrator (`task-execute` Step 9.5) applies the accept / patch / escalate / block judgment and explains its decisions. The one exception: pure style or naming nits when the task explicitly scoped them out may be omitted.
- **Suggest a triage class for each finding** — F1–F4 fix-now or K1–K4 known-limit, as defined in `task-execute` Step 9.5 "Finding triage and review scope" (root CLAUDE.md §8.5). For an F1, state the concrete failure scenario; a finding with no stateable scenario is K4. A K class never holds a confirmed defect on a real path: a confirmed defect with a stateable failure scenario on a real path is F-class, however old or rare. This is a label, not a filter: still report everything.
- **That includes defects in code this change did not write** (pre-existing, adjacent, another project's). Mark them "found in passing" so the orchestrator fixes them in scope, or files and reports them.
- **A re-review after fixes reviews the fix scope only:** the lines the fix round changed plus the code that directly calls or is called by them, not the whole task surface. Re-reviewing the whole surface every round kept producing new findings without end. This limits review ceremony; fixing continues until no F-class finding remains.

Severity stays an output annotation (the report still ranks Critical → Warning → Suggestion). The contract changes only that nothing is dropped before it reaches the report.

### Step 1: Determine Scope

```
IF this is a re-review after a fix round:
  SCOPE = lines changed by the fix + their direct callers and callees (Step 0)
ELIF files explicitly specified:
  SCOPE = specified files
ELIF git has uncommitted changes:
  SCOPE = git diff --name-only (staged + unstaged)
ELIF user mentions "recent" or "last commit":
  SCOPE = git diff HEAD~1 --name-only
ELIF reviewing a branch or PR:
  SCOPE = git diff $(git merge-base HEAD master) --name-only
ELSE:
  ASK user what to review

SET BASE (the "before" version for Step 2.6):
  uncommitted changes -> HEAD;  last commit -> HEAD~1;  branch or PR -> git merge-base HEAD master
  explicit files -> whichever of these holds the change

CATEGORIZE files — each category switches on a conditional step:
  .cs                                                   -> Step 7 ".NET"
  .ts / .tsx                                            -> Step 7 "TypeScript / PCF"
  record create/update paths (wizard services, BFF
    writers) or any plugin code                         -> Step 7 "Dataverse write path" (ADR-002)
  src/server/api/Sprk.Bff.Api/**, src/server/shared/
    Spaarke.Core/**, src/server/shared/Spaarke.Dataverse/** -> Step 6.5 BFF hygiene
  projects/*/tasks/*.poml                               -> Step 6.7 POML completeness
  any new surface (Step 6.6 list)                       -> Step 6.6 component justification
```

### Step 2: Load Context

```
LOAD the CLAUDE.md files for the code area:
  - Root CLAUDE.md (always)
  - Module-specific CLAUDE.md if one exists

FOR each file in scope:
  READ file content
  IDENTIFY: purpose, dependencies, public API
```

If the scope is too large to read every line closely (a whole-project wrap-up review, or dozens of files), read `references/review-checklist.md` before Step 3 and run its grep patterns over the scope to find the hits for Steps 3–6, then read the hits.

### Step 2.5: Quantitative Metrics Collection

Collect measurable metrics for each file in scope. They give the cohesion judgment objective input and let quality be tracked over time.

```
METRIC             HOW TO MEASURE
Total lines        wc -l {file}
Public methods     .cs: grep -c "public.*(" {file}
                   .ts: grep -c -E "(export (function|const|class)|public )" {file}
Private methods    .cs: grep -c "private.*(" {file}      .ts: count non-exported functions
Ctor parameters    .cs: count the parameters of "public {ClassName}(" — the DI injection count
Complexity est.    .cs: grep -c -E "(^\s*if\b|else if|switch\b|case\b|for\b|foreach\b|while\b|catch\b)" {file}
                   .ts: grep -c -E "(^\s*if\b|else if|switch\b|case\b|for\b|\.forEach|\.map|while\b|catch\b)" {file}
                   then add the ?? and ternary (?:) operators and the && / || in conditions, plus 1 (baseline path)
Interfaces         grep -c "interface I[A-Z]" {file}; note any with a single implementation
```

**Look-closer signals** (observations, never findings by themselves):

| Metric | Look closer when | Check |
|--------|------------------|-------|
| Total lines | > 300 | Cohesion (root CLAUDE.md §11.5) |
| Public methods | > 10 | More than one reason to change? |
| Constructor parameters | > 7 | A second responsibility? (`docs/standards/COMPONENT-COMPLEXITY.md`) |
| Cyclomatic complexity | > 15 | Branches that could be data or separate cases? |
| Interfaces per file | > 1 | Single-implementation interfaces (Smell 1) |

Report the numbers in the report's Quantitative Metrics table **without a severity**. A metric never produces a finding on its own: root CLAUDE.md §11.5 ("judge cohesion, not line count; no LOC gate") and `docs/standards/COMPONENT-COMPLEXITY.md` decide. A large, cohesive, single-responsibility file is fine — say so in the review. A finding comes from the cohesion judgment (for example, a second responsibility added), and its severity follows the finding, not the number.

**Required for every file over a look-closer value:** one line in the review — "cohesive: <reason>" or "not cohesive: <responsibilities that diverge>". The second is a finding.

### Step 2.6: Quality Direction Analysis (Before/After Comparison)

Direction matters more than absolute state: a file with complexity 20 that was 25 is improving; one that was 15 is degrading.

```
FOR each file in SCOPE:
  git show {BASE}:{file} > {temp file}          (BASE from Step 1)
  IF that fails (new file): REPORT "New file -- no baseline for comparison"; SKIP the file
  COMPUTE the Step 2.5 metrics on the previous version
  BUILD a per-file table: Metric | Before | After | Delta | Signal

SIGNAL per metric:
  Improved -- metric decreased (fewer lines, lower complexity)
  Stable   -- unchanged (delta = 0)
  Neutral  -- changed within the normal range
  Warning  -- increased past a threshold: lines grew > 20%; complexity rose by > 3;
              constructor parameters increased; new interface without multiple implementations
  Degraded -- crossed a Step 2.5 look-closer value from below (e.g. complexity 12 -> 18)

OVERALL per file:
  any Degraded                          -> Quality Degraded
  2+ Warning                            -> Quality Declining
  any Improved and no Warning           -> Quality Improved
  otherwise                             -> Neutral
```

Signals are observations, like the metrics they come from: a Declining or Degraded file gets the Step 2.5 cohesion line, and becomes a finding only if that judgment says so. Report the per-file tables, the Quality Direction Summary and a 1–2 sentence actionable insight on whether the changeset moves quality up or down.

### Step 3: Security Review

```
CHECK for common vulnerabilities:
  Secrets/credentials: hardcoded strings that look like tokens/passwords; connection strings in code; API keys
  Input validation:    user input used without validation; SQL/XSS injection vectors; path traversal
  Authorization:       missing auth checks on endpoints; inconsistent permission models; elevation of privilege
  Data exposure:       sensitive data in logs; PII in error messages; overly permissive CORS

FLAG: Critical / Warning / Suggestion
```

### Step 4: Performance Review

```
CHECK for performance issues:
  N+1 queries:          loops with individual database calls; Graph API calls in loops
  Missing async/await:  blocking calls (.Result, .Wait()); sync-over-async
  Resource management:  missing disposal (IDisposable); large allocations in loops; unbounded collections
  Caching (ADR-009):    missing caching for repeated lookups; in-memory cache for cross-request data (should be Redis)

FLAG: Critical / Warning / Suggestion
```

### Step 4.5: Linting Check

```
RUN automated linting before the manual review:
  TypeScript/PCF (ESLint):  cd src/client/pcf && npm run lint
    - Catches unused vars, type issues, React hooks rules
    - Config: src/client/pcf/eslint.config.mjs (includes @microsoft/eslint-plugin-power-apps)
    - Other packages: run that package's own lint script, if it has one
  C# (Roslyn analyzers):    dotnet build --warnaserror
    - Catches null refs, async issues, naming conventions
    - Config: Directory.Build.props (TreatWarningsAsErrors=true, Nullable enabled)

  Fix common issues: TypeScript -> npx eslint --fix {files};  C# -> dotnet format

FLAG: Critical (lint errors block merge) / Warning (lint warnings)
```

### Step 5: Style and Maintainability Review

```
CHECK code quality:
  Naming conventions (from CLAUDE.md)
  - PascalCase for C# types/methods; camelCase for TypeScript variables
  - Descriptive names (no single letters except loop variables)

  Code organization
  - Method length (~30 lines is a signal to check the method does one thing, not a finding by size)
  - Class responsibility (single purpose); circular dependencies
  - Commented-out code; magic numbers without constants
  - Nested conditionals > 3 levels (a signal: would guard clauses or a lookup read better?)

  Component complexity (docs/standards/COMPONENT-COMPLEXITY.md — evaluate COMPLEXITY, not LOC)
  - Is this change adding a SECOND (or Nth) responsibility to a component? (SRP / cohesion is the real
    smell, not line count.) Direction matters: a component doing MORE jobs is declining; the same file
    getting simpler is improving.
  - Signals to weigh: multiple reasons to change, low-cohesion method clusters, many ctor deps (>~8-12),
    high cyclomatic complexity, mixed abstraction levels, merge-conflict-magnet churn.
  - Do NOT flag a large file on size alone: a large, single-responsibility, cohesive file (state machine,
    exhaustive mapping, generated code) is legitimate — accept it, and expect the PR to say why.
  - When responsibilities have genuinely diverged, flag a DECOMPOSITION opportunity (extract the cluster
    with its own reason to change) — a suggestion toward deliberate refactoring, not a blocker.

  Documentation
  - Public API has XML docs (.cs); complex logic has comments; TODO/HACK comments addressed

  Error handling
  - Catch blocks that swallow exceptions; missing try/catch for I/O; error messages useful for debugging

FLAG: Warning / Suggestion
```

### Step 5.5: AI Code Smell Detection

Five anti-patterns that come from how LLMs generate code — over-abstracting, over-guarding, and verbose patterns a human developer would not write. They are distinct from the generic smells in Step 5. Check every file in scope for all five. If you are unsure whether a pattern matches a smell, or whether it is an allowed exception, read `examples/ai-code-smells.md` (the reasoning, full detection steps and BAD/GOOD code for each smell) before you flag or clear it.

**Smell 1 — Interface with a single implementation** (ADR-010: register concretes by default; only two seams allowed).
- Detect: `grep -rn "interface I[A-Z]" {file}`; then for each interface, C#: `grep -rn "class .* : .*I{InterfaceName}" --include="*.cs"`; TS: is it used for DI, or only as a type?
- Flag when only one implementation exists (TS: the interface wraps a single concrete class or service).
- Not a smell: the allowed seams `IAccessDataSource` and `IAuthorizationRule`; a TS interface used only as a type.
- Severity: Warning. Action: remove the interface, register the concrete.

**Smell 2 — Try/catch log-rethrow.**
- Detect: C# `grep -A 3 "catch.*Exception" {file} | grep -B 1 "throw;"`; TS: `catch (error) { console.error(error); throw error; }` (or `logger.error`).
- Flag a catch that only logs and rethrows the same exception: the caller or global middleware logs it anyway, so it duplicates log entries and pollutes stack traces.
- Not a smell: a catch that wraps the exception in a domain exception to add context, or performs recovery beyond logging.
- Severity: Warning. Action: remove the try/catch.

**Smell 3 — Null check on a non-nullable type** (nullable reference types are enabled project-wide).
- Detect: `grep -n "if.*== *null"`, `grep -n "if.*is null"`, `grep -n "??"`, `grep -n "?\."` on {file}, then check each variable's declared type. Includes `ArgumentNullException.ThrowIfNull(param)` on a non-nullable parameter. TS: `param != null` or `param !== null && param !== undefined` on a parameter whose type has no `| null` / `| undefined`.
- Flag when the type is non-nullable (`string`, `Document`, `int`). Skip nullable types (`string?`, `Document?`).
- Severity: Suggestion. Action: remove the check; if null really can arrive, make the type nullable instead.

**Smell 4 — Code-restating comment.**
- Detect: read each comment and ask "does this tell me something I cannot already read from the code?" Indicators: it names the method or variable on the next line; it uses "get", "set", "create", "initialize", "return" matching the next operation; it describes one obvious operation.
- Not a smell: comments that explain why — business rules, edge cases, workarounds, non-obvious decisions.
- Severity: Suggestion. Action: remove the comment.

**Smell 5 — Method with more than three responsibilities** (SRP).
- Detect: a name containing And / Or / Also / Then (`ValidateAndSave`) or a generic name (`ProcessRequest`, `HandleData`, `DoWork`) — `grep -n "And\|Or\|Also\|Then" {file} | grep "public\|private\|async"`; count distinct concerns in the body: input validation, data retrieval / API calls, business logic / transformation, persistence, notification / eventing, logging / telemetry, error handling beyond a simple try/catch; structural hints: blank-line "sections", more than ~30 lines, comments acting as section headers (`// Step 1: Validate`).
- Flag when the method holds more than three of those concerns.
- Severity: Suggestion by default; Warning when the responsibilities have different reasons to change (they will diverge). Never Critical on a count — an orchestration method that sequences several steps is legitimate when each step is a call, not inline logic.
- Action: extract responsibilities into separate methods or services; use background jobs for async processing chains.

Record the results in the report's AI Code Smell Detection table, with the AI Smell Score and a verdict (Clean / Minor issues / Needs refactoring).

### Step 6: ADR Compliance Check

```
RUN a subset of the adr-check skill.

CRITICAL ADRs to always check:
  - ADR-001 / ADR-052: BFF endpoints in Minimal API; no Functions or Durable Task inside the BFF; any background
    work's host chosen per workload under ADR-052 (inside the BFF: queue -> ADR-004 IJobHandler,
    schedule -> ADR-036 IScheduledJob)
  - ADR-002: No plugins; record invariants server-side (WP-1...WP-8)
  - ADR-007: Graph types isolated
  - ADR-008: Endpoint filters for auth
  - ADR-013 (refined 2026-05-20): AI architecture — CRUD code MUST consume AI via Services/Ai/PublicContracts/
    facades (no direct injection of IOpenAiClient, IPlaybookService or other AI-internal types into CRUD code)
  - ADR-028: Spaarke Auth v2 contract — client: useAuth() + authenticatedFetch (no raw fetch with Authorization
    headers, no accessToken: string props, no tokenBridge/__SPAARKE_BFF_TOKEN__, no PublicClientApplication
    outside @spaarke/auth); server: DefaultAzureCredential (MI) for Graph/Dataverse when MI is enabled; HMAC
    webhook signing; tenant-specific MSAL authority (INV-3/INV-6)

IF violations found:
  // Per root CLAUDE.md §6.5 (ADR conflict resolution): a violation is NOT automatically Critical.
  // Check whether the PR description or the design.md/spec.md "ADR Tensions" section already documents
  // the deviation as Path A (project-scoped exception) or Path B (ADR amendment in flight).

  IF documented in the PR / design.md "ADR Tensions" as Path A:
    -> Verify the rationale is concrete (not boilerplate); cite the documented exception
    -> FLAG as Warning (reviewer judgment — confirm the exception's scope is bounded)
    -> Do not block on this alone
  ELSE IF Path B (ADR amendment) is referenced and a linked PR exists:
    -> FLAG as Warning; verify the amendment lands before or with this PR
  ELSE:
    -> FLAG as Critical (violation without a documented exception)
    -> Point to the full adr-check skill (.claude/skills/adr-check/) for detail
    -> REQUEST that the implementer choose Path A / B / C per root CLAUDE.md §6.5

  Silent violations (no documented exception, no amendment) are ALWAYS Critical.
  Reasoned exceptions documented at the point of decision are acceptable.
```

### Step 6.5: BFF Hygiene Check (Conditional — CLAUDE.md §10)

**Fires when** any file in scope is under `src/server/api/Sprk.Bff.Api/**`, `src/server/shared/Spaarke.Core/**` or `src/server/shared/Spaarke.Dataverse/**`.

If it fires, read `references/bff-hygiene-check.md` before continuing and apply its Sections A–D (the binding §10 pre-merge checklist from `.claude/constraints/bff-extensions.md`). Its Critical findings, which hold even before you open the file: missing Placement Justification; a new direct CRUD→AI dependency (ADR-013); a new HIGH-severity CVE. If it does not fire, record "Step 6.5: N/A — no BFF files".

### Step 6.55: Enforcement Check (Universal — CLAUDE.md §16 enforcement ladder)

```
WHEN the change adds or changes a RULE (an ADR MUST/MUST NOT, a constraint, a FAILURE-MODES entry,
a project standing directive) OR fixes a defect caused by a repeatable pattern:
  - Is the rule enforced by the strongest mechanism that works (type -> lint -> ArchTest/guard ->
    hook/permission -> prose)? Prose-only needs a one-line reason it cannot be mechanised.
  - Does a new guard carry must-fire and must-not-fire controls, and a ratchet baseline if it found
    existing violations?
  - For a pattern fix: did the author search for other instances and fix, guard or list them
    (task-execute Step 9.5 rule 6)?
FLAG: missing mechanism or missing class search -> Warning (Suggestion when the rule is genuinely
  unmechanisable and says why). A guard with no must-fire control -> Warning: it may never fire.
```

### Step 6.6: Component Justification Check (Universal — CLAUDE.md §11)

Extends Step 6.5 from the BFF to every new component: it enforces root CLAUDE.md §11 "Component justification — default to reuse" at review time (`task-create` Step 3.5.6 is the authoring-time gate). §10 catches scope creep into the BFF; §11 catches it everywhere — shared libs, PCFs, code pages, Dataverse columns. `chat-routing-redesign-r1` shipped three failures this gate now catches at review (the LegalWorkspace dead-code misreading, the `sprk_playbookcode` field choice, the 8-tool surface).

**Fires for any new surface, including new surface added inside an existing file** (root §11):
- a new `.cs` / `.ts` / `.tsx` / `.ps1` file under `src/`
- a new endpoint route or handler
- a new DI registration (`services.Add*`)
- a new package reference (`<PackageReference>` or a `dependencies` entry)
- a new Dataverse column, entity or alternate key

**Does not fire for** pure modification of existing surface: edit, refactor, bug fix, tests for existing surface, rename, format.

```
1. LOCATE the task POML that authorized the change — by branch-name pattern, or by the PR description's
   TASK-INDEX reference. If several tasks contributed, check each task's POML in turn.
   New file with no task POML findable -> WARNING

2. READ the POML's <justification>.
   Missing while new surface is present -> WARNING, weighted by surface:
     - new file in a shared lib, or a new endpoint              -> HIGH
     - new DI registration, or a new package                    -> HIGH
     - new internal helper / private class                      -> LOW
   Cite root CLAUDE.md §11 + task-create Step 3.5.6; recommend adding <justification> before merge.

3. VERIFY each of the three answers is concrete:
   a. <existing> cites a file:line found with Grep, OR shows the grep command and its "no matches" output.
      It starts from .claude/patterns/CAPABILITY-INDEX.md: "CAPABILITY-INDEX row N" + file:line, or "CAPABILITY-INDEX: no row" + the Grep run.
      - "None" with no grep evidence -> WARNING (lazy answer)
      - "Similar to FooService but different in X" with no file:line -> SUGGESTION (sharpen)
   b. <extension> is a yes/no with a reason. Acceptable reasons include:
      - "Existing X looks up by name; this needs lookup by id; signatures incompatible"
      - "Existing X is in the CRUD namespace; the new component must live in the AI facade per ADR-013"
      - "Existing X is sync; this requires an async stream signature"
      Hollow ("cleaner separation", "for testability", "to avoid coupling") -> WARNING
   c. <cost-of-doing-nothing> names a concrete behaviour, contract or failure mode:
      - "Without this, callers cannot resolve playbooks by stable ID; name resolution breaks on rename"
      - "Without this, the new endpoint returns 200 with raw exception text instead of RFC 7807 ProblemDetails"
      Abstract ("scalability", "abstraction layer", "future flexibility", "best practice"), or naming no
      concrete behaviour or contract -> WARNING

4. ANY hollow, boilerplate or abstract answer -> also recommend revisiting the scope before merge.
5. 8+ new components in one PR justified collectively instead of per component -> WARNING
```

### Step 6.7: Task POML Completeness Check (Conditional)

**Fires only when the diff adds or modifies `projects/*/tasks/*.poml` files.** It is the PR-time half of the `task-create` Completeness Lint: a hand-edited POML that skipped the canonical metadata otherwise fails silently at dispatch.

If it fires, read `references/poml-completeness-check.md` before continuing; the fastest path is `pwsh scripts/Validate-TaskPoml.ps1 <tasks-dir>`, with its findings folded into the report. If it does not fire, record "Step 6.7: N/A".

### Step 7: Technology-Specific Checks (Conditional — by file type)

If the scope contains any of the following, read `references/technology-checks.md` before continuing and apply the section for each type present:
- `.cs` files → ".NET" (Minimal API, nullable reference types, modern C#)
- `.ts` / `.tsx` files → "TypeScript / PCF" (React, TypeScript strictness, Fluent UI v9 per ADR-021)
- record create/update paths (wizard services, BFF writers) or any plugin code → "Dataverse write path" (ADR-002 WP-1…WP-8)

This gate holds even before you open the file: any plugin code, plugin-backed Custom API or low-code plugin is **Critical** — Spaarke has no Dataverse plugins (ADR-002).

### Step 8: Generate Review Report

Read `references/report-template.md` and fill it in. Then:
- Order findings Critical → Warning → Suggestion, then "What is good" (acknowledge positive patterns).
- Give every finding a file:line location, severity, confidence and suggested triage class (Step 0); mark "found in passing" where it applies.
- With 50+ findings, give the top 5 in full detail and list the rest one line each (file:line + description), grouped by category with counts. Clustering shortens the report; it never drops a finding.
- Where a CI check (`gh pr checks`) already reports an issue, cite the check in one line instead of re-deriving it, and spend the review on the judgment layer.
- Make the recommended actions concrete and ordered.

## Conventions

### Severity Levels

| Level | Meaning | Action required |
|-------|---------|-----------------|
| Critical | Security or correctness issue | Must fix before merge |
| Warning | Quality concern | Should fix; discuss if not |
| Suggestion | Enhancement | Optional, at the author's discretion |

Severity ranks the report. What the caller does about a finding follows its triage class (F1–F4 / K1–K4, `task-execute` Step 9.5), not its severity label.

### Review Depth

Depth sets how far you look. It never lets you drop a finding you made (Step 0).

| Depth | Focus | Use when |
|-------|-------|----------|
| quick | Look for Critical issues; style suggestions out of scope | Small changes and hotfixes, when the user asks for it |
| standard | Every step at every severity | Default; what `task-execute` Step 9.5 runs |
| thorough | Every step, plus a full `/adr-check` and the callers of changed code | Major features, refactors, pre-PR |

## Acceptance Criteria

- [ ] All files in scope were reviewed; every Workflow step ran or has an N/A line with a reason
- [ ] Quantitative metrics collected for each file, with a cohesion line for each file over a look-closer value
- [ ] Quality direction (before/after against BASE) assessed for changed files
- [ ] All five AI code smells checked
- [ ] ADR compliance checked (Step 6), and Steps 6.5 / 6.6 / 6.7 / 7 applied wherever they fire
- [ ] Every finding has a file:line location, severity, confidence and suggested triage class
- [ ] Critical issues have clear descriptions; F1 findings state a failure scenario
- [ ] Positive patterns acknowledged
- [ ] Next steps are actionable

## Gotchas

- **A "clean" review that missed a dimension.** The review looked at security and performance and skipped design quality. Run every step; a one-line confirmation is enough where a step finds nothing.
- **Restating CI.** Re-reporting a Trivy vulnerability or a lint error adds nothing. Check `gh pr checks` first and spend the review on what the CI Router (Tier 1 / Tier 2), `ci-standalone-checks.yml` and the nightly Trivy scan cannot judge.
- **Style nits, missed architecture.** A surface scan without the ADRs finds formatting and misses violations. Step 6 always runs; style is mostly CI's job, ADR violations are the judgment layer's.
- **An unread wall of findings.** Order and cluster as Step 8 says; Critical first.
- **A review that stopped part-way.** Pick the model and effort for review work from `.claude/constraints/agent-cost.md`. A full review is judgment work: never run it on the mechanical tier (Haiku).
- **"Review" read as "run the tests".** The user may map "review code" to CI. When this skill triggers, perform the review; do not just report CI status.
- **Wrong baseline.** Comparing against `HEAD~1` when the change is uncommitted, or spans several commits, gives a meaningless direction. Use BASE from Step 1.

## References

- Root `CLAUDE.md` — §6.5 (ADR conflicts), §8.5 (review classes), §10 (BFF hygiene), §11 / §11.5 (component justification and complexity), §16 (enforcement ladder)
- `docs/standards/COMPONENT-COMPLEXITY.md` — cohesion, not line count
- `.claude/skills/adr-check/` and `.claude/skills/adr-check/references/` — full ADR validation rules
- Related skills: **adr-check** (deep architecture compliance), **spaarke-conventions** (coding standards), **task-create** (review as a task deliverable; Step 3.5.6 justification gate), **task-execute** (calls this skill at Step 9.5), **push-to-github** (runs it as a pre-flight check), **repo-cleanup** (repository hygiene)
- Supporting files in this skill: `references/review-checklist.md` (grep patterns, Step 2), `examples/ai-code-smells.md` (Step 5.5), `references/bff-hygiene-check.md` (Step 6.5), `references/poml-completeness-check.md` (Step 6.7), `references/technology-checks.md` (Step 7), `references/report-template.md` (Step 8), `examples/invocation-examples.md` (Inputs), `references/integration-points.md`

If you were invoked by another skill (`task-execute` Step 9.5, `push-to-github` Step 1, a `090` wrap-up task) and need to know what it does with your report, read `references/integration-points.md`.
