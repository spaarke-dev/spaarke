---
description: Comprehensive code review covering security, performance, style, and ADR compliance — the judgment-layer quality gate that complements CI/CD's mechanical checks
tags: [code-review, quality, security, performance, adr-compliance, judgment-layer]
techStack: [all]
appliesTo: ["**/*.cs", "**/*.ts", "**/*.tsx", "post-task", "pre-merge", "PR review"]
alwaysApply: false
exemplar: none-too-volatile
last-reviewed: 2026-05-17
---

# code-review

> **Last Reviewed**: 2026-05-17
> **Reviewed By**: ai-procedure-quality-r1 (Phase 2b Wave 2c — `leave-alone-justified` on body length; **normalized minimal frontmatter** [inventory anomaly #6: description+alwaysApply only → full block]; **no procedural split** per dereference-reliability concern)
> **Exemplar rationale**: Code reviews are per-PR outputs; no canonical reference holds. The 600-line Workflow body IS the canonical procedure.
> **Justified length** (846 lines): operationally dense judgment-layer procedure invoked by every `090-project-wrap-up.poml` task (~70 auto-invocations). Splitting risks the dereferencing-reliability problem identified in Phase 2b Wave 2c — agents executing the review need procedural detail inline.
> **CI/CD coverage note**: `sdap-ci.yml` covers ~40% of this skill's surface (security-scan via Trivy, build-test, code-quality formatting + ADR NetArchTest, adr-pr-comment). The other 60% (design quality, abstraction levels, cohesion/coupling judgment, "is this over-engineered?") is what THIS skill provides — automation can't replace the judgment layer.

## Purpose

Performs a structured, multi-dimension code review for Spaarke codebase changes. This skill ensures code quality across security, performance, maintainability, and architectural compliance dimensions. Unlike `adr-check` (architecture-only), this is a holistic quality gate.

## When to Use

- User says "review code", "review my changes", or "code review"
- Before creating a pull request
- After completing a task to validate implementation
- Explicitly invoked with `/code-review {file-or-path}`

## Inputs Required

| Input | Required | Source |
|-------|----------|--------|
| Files to review | Yes | Explicit paths, git diff, or current selection |
| Review depth | No | Default: "standard" (can be "quick" or "thorough") |
| Focus areas | No | Default: all (can narrow to security, performance, etc.) |

## Workflow

### Step 1: Determine Scope
```
IF files explicitly specified:
  SCOPE = specified files
ELIF git has uncommitted changes:
  SCOPE = git diff --name-only (staged + unstaged)
ELIF user mentions "recent" or "last commit":
  SCOPE = git diff HEAD~1 --name-only
ELSE:
  ASK user what to review

CATEGORIZE files by type:
  - .cs -> .NET review checklist
  - .ts/.tsx -> TypeScript/PCF review checklist
  - Plugin code -> Plugin review checklist
```

### Step 2: Load Context
```
LOAD relevant CLAUDE.md files for code area:
  - Root CLAUDE.md (always)
  - Module-specific CLAUDE.md if exists

FOR each file in scope:
  READ file content
  IDENTIFY: purpose, dependencies, public API
```

### Step 2.5: Quantitative Metrics Collection

**Purpose**: Collect measurable metrics for each file under review. These numbers enable tracking quality trends over time and provide objective data alongside qualitative observations.

```
FOR each file in SCOPE:
  COLLECT the following metrics:

  Metric                        How to Measure
  ----------------------------  --------------------------------------------
  Total Lines                   Count all lines in the file (wc -l)
  Public Method Count (.cs)     Count lines matching: public.*(
  Public Method Count (.ts)     Count exported functions/methods
  Private Method Count          Count private/non-exported functions
  Constructor Parameter Count   Count parameters in constructor signature
                                (DI injection count -- flag if > 5)
  Cyclomatic Complexity Est.    Count branches: if + else if + switch case
                                + for + foreach + while + catch + ??
                                + ternary (?:) + && + || in conditions
                                Then add 1 (baseline path)
  Interface Count               Count interface declarations in file
                                Flag if interface has single implementation

  DETECTION COMMANDS (C#):
    # Total lines
    wc -l {file}

    # Public methods
    grep -c "public.*(" {file}

    # Private methods
    grep -c "private.*(" {file}

    # Constructor parameters (count commas + 1 in ctor signature)
    grep "public {ClassName}(" {file}  -> count parameters

    # Cyclomatic complexity estimate
    grep -c -E "(^\s*if\b|else if|switch\b|case\b|for\b|foreach\b|while\b|catch\b)" {file}
    # Add 1 for baseline path. Also count ?? and ternary operators.

    # Interface declarations
    grep -c "interface I[A-Z]" {file}

  DETECTION COMMANDS (TypeScript):
    # Public/exported functions
    grep -c -E "(export (function|const|class)|public )" {file}

    # Cyclomatic complexity estimate
    grep -c -E "(^\s*if\b|else if|switch\b|case\b|for\b|\.forEach|\.map|while\b|catch\b)" {file}
    # Add 1 for baseline path. Also count ?? and ternary operators.

OUTPUT FORMAT (include in review report):

  ### Quantitative Metrics

  | File | Lines | Public Methods | Private Methods | Ctor Params | Complexity Est. | Interfaces |
  |------|-------|----------------|-----------------|-------------|-----------------|------------|
  | AuthService.cs | 245 | 8 | 5 | 3 | 18 | 0 |
  | DataGrid.tsx | 380 | 4 | 12 | -- | 24 | 1 |

  **Thresholds** (flag when exceeded):
  | Metric | Warning Threshold | Critical Threshold |
  |--------|-------------------|--------------------|
  | Total Lines | > 300 | > 500 |
  | Public Methods | > 10 | > 20 |
  | Constructor Parameters | > 4 | > 7 (ADR-010) |
  | Cyclomatic Complexity | > 15 | > 30 |
  | Interfaces per file | > 1 | > 3 |
```

### Step 2.6: Quality Direction Analysis (Before/After Comparison)

**Purpose**: Determine whether a change improved or worsened code quality relative to the prior version. Direction matters more than absolute state -- a file with complexity 20 that was 25 is improving; one that was 15 is degrading.

```
FOR each file in SCOPE:

  GET previous version:
    git show HEAD~1:{file} > /tmp/prev_{basename}

    IF file is new (git show fails):
      REPORT: "New file -- no baseline for comparison"
      SKIP comparison for this file
      CONTINUE

  COMPUTE metrics for BOTH versions:
    CURRENT = metrics from Step 2.5
    PREVIOUS = same metrics computed on /tmp/prev_{basename}

  GENERATE delta table:

  ### Quality Direction: {filename}

  | Metric | Before | After | Delta | Signal |
  |--------|--------|-------|-------|--------|
  | Total Lines | 180 | 245 | +65 | Warning: Grew |
  | Public Methods | 6 | 8 | +2 | -- |
  | Private Methods | 3 | 5 | +2 | -- |
  | Ctor Params | 3 | 3 | 0 | Stable |
  | Complexity Est. | 12 | 18 | +6 | Warning |

  SIGNAL RULES:
    Improved   -- metric decreased (fewer lines, lower complexity)
    Stable     -- metric unchanged (delta = 0)
    Neutral    -- metric changed within normal range
    Warning    -- metric increased past threshold:
                  - File grew > 20% in lines
                  - Cyclomatic complexity increased by > 3
                  - Constructor parameters increased
                  - New interface added without multiple implementations
    Degraded   -- metric crossed from below threshold to above threshold
                  (e.g., complexity went from 14 to 32, crossing the 30 critical line)

  SUMMARY SIGNAL (per file):
    IF any metric is Degraded: overall = Quality Degraded
    ELIF 2+ metrics are Warning: overall = Quality Declining
    ELIF any metric is Improved and none are Warning: overall = Quality Improved
    ELSE: overall = Neutral

OUTPUT (append to review report after Quantitative Metrics):

  ### Quality Direction Summary

  | File | Overall Signal | Key Changes |
  |------|----------------|-------------|
  | AuthService.cs | Declining | +36% lines, +6 complexity |
  | DataGrid.tsx | Improved | -15% lines, -4 complexity |
  | NewHelper.cs | New file | No baseline |

  **Actionable Insight**: {1-2 sentences summarizing whether this changeset
   moves quality in a positive or negative direction overall}
```

### Step 3: Security Review
```
CHECK for common vulnerabilities:

  Secrets/credentials
  - Hardcoded strings that look like tokens/passwords
  - Connection strings in code
  - API keys

  Input validation
  - User input used without validation
  - SQL/XSS injection vectors
  - Path traversal vulnerabilities

  Authorization
  - Missing auth checks on endpoints
  - Inconsistent permission models
  - Elevation of privilege risks

  Data exposure
  - Sensitive data in logs
  - PII in error messages
  - Overly permissive CORS

FLAG: Critical / Warning / Info
```

### Step 4: Performance Review
```
CHECK for performance issues:

  N+1 queries
  - Loops with individual database calls
  - Graph API calls in loops

  Missing async/await
  - Blocking calls (.Result, .Wait())
  - Sync-over-async patterns

  Resource management
  - Missing disposal (IDisposable)
  - Large object allocations in loops
  - Unbounded collections

  Caching patterns (per ADR-009)
  - Missing caching for repeated lookups
  - In-memory cache for cross-request data (should be Redis)

FLAG: Critical / Warning / Info
```

### Step 4.5: Linting Check
```
RUN automated linting before manual review:

  TypeScript/PCF (ESLint)
  cd src/client/pcf && npm run lint
  - Catches: unused vars, type issues, React hooks rules
  - Config: src/client/pcf/eslint.config.mjs
  - Includes: @microsoft/eslint-plugin-power-apps

  C# (Roslyn Analyzers)
  dotnet build --warnaserror
  - Catches: null refs, async issues, naming conventions
  - Config: Directory.Build.props (TreatWarningsAsErrors=true)
  - Nullable reference types enabled

  Fix common issues:
  - TypeScript: npx eslint --fix {files}
  - C#: dotnet format

FLAG: Critical (lint errors block merge) / Warning (lint warnings)
```

### Step 5: Style and Maintainability Review
```
CHECK code quality:

  Naming conventions (from CLAUDE.md)
  - PascalCase for C# types/methods
  - camelCase for TypeScript variables
  - Descriptive names (not single letters except loops)

  Code organization
  - Method length (recommend <30 lines)
  - Class responsibility (single purpose)
  - Circular dependencies

  Documentation
  - Public API has XML docs (.cs)
  - Complex logic has comments
  - TODO/HACK comments addressed

  Error handling
  - Catch blocks that swallow exceptions
  - Missing try/catch for I/O operations
  - Error messages helpful for debugging

FLAG: Warning / Info / Suggestion
```

### Step 5.5: AI Code Smell Detection

**Purpose**: Detect five anti-patterns commonly introduced by AI-generated code. These smells are distinct from generic code smells (Step 5) because they arise specifically from how LLMs generate code -- over-abstracting, over-guarding, and producing verbose patterns that a human developer would not write.

```
FOR each file in SCOPE, CHECK for these five AI code smells:

================================================================================
SMELL 1: Interfaces with Single Implementations
================================================================================
Relevant ADR: ADR-010 (DI Minimalism -- register concretes by default)

WHY: AI models default to "best practice" patterns like interface-per-class.
  In Spaarke, ADR-010 explicitly forbids this unless a genuine seam exists
  (only 2 allowed seams).

DETECTION (C#):
  1. Find interface declarations:
     grep -rn "interface I[A-Z]" {file}
  2. For each interface found, search codebase for implementations:
     grep -rn "class .* : .*I{InterfaceName}" --include="*.cs"
  3. FLAG if only ONE implementation exists

DETECTION (TypeScript):
  1. Find interface declarations:
     grep -rn "interface I[A-Z]" {file}
  2. Check if interface is used for DI or just type safety
  3. FLAG if interface wraps a single concrete class/service

EXAMPLE:
  // BAD - AI SMELL: Interface with single implementation
  public interface IDocumentProcessor { Task ProcessAsync(Document doc); }
  public class DocumentProcessor : IDocumentProcessor { ... }
  services.AddSingleton<IDocumentProcessor, DocumentProcessor>();

  // GOOD: Register concrete (ADR-010)
  public class DocumentProcessor { ... }
  services.AddSingleton<DocumentProcessor>();

SEVERITY: Warning
ACTION: Remove interface, register concrete. Exception: if interface is
  in Allowed Seams list (IAccessDataSource, IAuthorizationRule).

================================================================================
SMELL 2: Try/Catch Log-Rethrow
================================================================================
Relevant: C# best practices -- redundant exception handling

WHY: AI models add try/catch blocks defensively. Catching an exception only
  to log it and rethrow adds no value -- the caller or global exception
  middleware will log it. It also pollutes stack traces and duplicates log
  entries.

DETECTION (C#):
  Look for this pattern (multiline):
    catch (Exception ex)
    {
        _logger.Log*(... ex ...);   // any log call mentioning ex
        throw;                      // rethrow same exception
    }

  grep -A 3 "catch.*Exception" {file} | grep -B 1 "throw;"

DETECTION (TypeScript):
  catch (error) {
    console.error(error);  // or logger.error(error)
    throw error;           // rethrow same error
  }

EXAMPLE:
  // BAD - AI SMELL: Catch-log-rethrow (redundant)
  try { await _store.GetDocumentAsync(id); }
  catch (Exception ex)
  {
      _logger.LogError(ex, "Failed to get document {Id}", id);
      throw;  // Caller or middleware will log this anyway
  }

  // GOOD: Let it propagate (middleware handles logging)
  await _store.GetDocumentAsync(id);

  // ALSO GOOD: Catch to add context, wrap in domain exception
  try { await _store.GetDocumentAsync(id); }
  catch (Exception ex)
  {
      throw new DocumentAccessException("Document not found", ex);
  }

SEVERITY: Warning
ACTION: Remove try/catch unless it adds context (wraps in domain
  exception) or performs recovery logic beyond just logging.

================================================================================
SMELL 3: Null Checks on Non-Nullable Types
================================================================================
Relevant: C# nullable reference types (NRT) -- project-wide enabled

WHY: AI models add defensive null checks even when the type system guarantees
  non-null. With NRT enabled in Spaarke, a parameter of type string (not
  string?) is guaranteed non-null by the compiler. Checking it adds noise
  and implies the type annotation is wrong.

DETECTION (C#):
  1. Find null checks:
     grep -n "if.*== *null" {file}
     grep -n "if.*is null" {file}
     grep -n "??" {file}
     grep -n "?\." {file}   (null-conditional)

  2. For each null check, verify the variable declared type:
     - If type is non-nullable (e.g., string, Document, int):
       FLAG as AI smell
     - If type is nullable (e.g., string?, Document?, int?):
       SKIP -- null check is appropriate

  3. Special case -- constructor parameter guards:
     ArgumentNullException.ThrowIfNull(param)
     If param type is non-nullable -> FLAG (compiler already prevents null)

DETECTION (TypeScript):
  1. Find null/undefined checks on typed parameters:
     if (param !== null && param !== undefined)
     if (param != null)
  2. Check if parameter has non-nullable type annotation
  3. FLAG if type does not include | null or | undefined

EXAMPLE:
  // BAD - AI SMELL: Null check on non-nullable type
  public async Task<Document> GetAsync(string id)  // string, not string?
  {
      if (id == null) throw new ArgumentNullException(nameof(id));
      // With NRT enabled, compiler prevents null from reaching here
  }

  // GOOD: Trust the type system
  public async Task<Document> GetAsync(string id)
  {
      var result = await _store.GetAsync(id);
      return result;
  }

  // GOOD: Null check on nullable type
  public async Task<Document?> FindAsync(string? id)
  {
      if (id is null) return null;  // Appropriate -- type is nullable
  }

SEVERITY: Suggestion
ACTION: Remove null check if type is non-nullable. If null IS possible,
  update the type annotation to nullable instead.

================================================================================
SMELL 4: Code-Restating Comments
================================================================================
Relevant: Clean Code principles -- comments should explain "why", not "what"

WHY: AI models produce comments that restate what the code already says.
  These comments add visual noise without adding information. Good comments
  explain WHY something is done, not WHAT is being done.

DETECTION (C# and TypeScript):
  Look for comments where the comment text mirrors the next line of code:

  Pattern indicators:
  - Comment contains the method/variable name being called
  - Comment uses words like "get", "set", "create", "initialize", "return"
    that match the operation on the next line
  - Comment describes a single obvious operation

  Manual review -- read each comment and ask:
    "Does this tell me something I cannot already read from the code?"
    If no -> FLAG

EXAMPLE:
  // BAD - AI SMELL: Code-restating comments
  // Get the document by ID
  var document = await _store.GetDocumentAsync(id);

  // Initialize the list of results
  var results = new List<SearchResult>();

  // Return the response
  return Ok(response);

  // Set the status to active
  entity.Status = StatusCode.Active;

  // GOOD: Comments that add value
  // Graph API returns 404 for soft-deleted items; treat as "not found"
  var document = await _store.GetDocumentAsync(id);

  // Pre-allocate based on typical result set size to avoid resizing
  var results = new List<SearchResult>(capacity: 50);

  // Must return 200 even on empty results -- client polling depends on it
  return Ok(response);

SEVERITY: Suggestion
ACTION: Remove the comment if it restates the code. Keep comments that
  explain business rules, edge cases, workarounds, or non-obvious decisions.

================================================================================
SMELL 5: Methods with More Than Three Responsibilities
================================================================================
Relevant: Single Responsibility Principle (SRP)

WHY: AI models generate "god methods" that handle multiple unrelated
  concerns in sequence. These methods are hard to test, hard to name,
  and tend to grow over time. A method should do ONE thing.

DETECTION:
  1. Method name analysis:
     - Name contains "And", "Or", "Also", "Then" (e.g., ValidateAndSave)
     - Name is very generic (e.g., ProcessRequest, HandleData, DoWork)
     FLAG if method name suggests multiple operations

  2. Responsibility counting (manual review):
     Read the method body and identify distinct concerns:
     - Input validation
     - Data retrieval / API calls
     - Business logic / transformation
     - Persistence / saving
     - Notification / eventing
     - Logging / telemetry
     - Error handling (beyond simple try/catch)
     FLAG if method contains > 3 of these concerns

  3. Structural indicators:
     - Method has multiple "sections" separated by blank lines
     - Method exceeds 30 lines (from Step 5 threshold)
     - Method has comments acting as section headers
       (e.g., "// Step 1: Validate", "// Step 2: Transform")

  grep -n "And\|Or\|Also\|Then" {file} | grep "public\|private\|async"

EXAMPLE:
  // BAD - AI SMELL: Method with 5 responsibilities
  public async Task<ActionResult> CreateAndProcessDocument(CreateRequest req)
  {
      // Validate input
      if (!ModelState.IsValid) return BadRequest();

      // Create document in SharePoint
      var doc = await _store.CreateAsync(req.ToDocument());

      // Process with AI pipeline
      var summary = await _aiService.SummarizeAsync(doc.Content);

      // Save metadata to Dataverse
      await _dataverse.UpdateAsync(doc.Id, new { Summary = summary });

      // Send notification
      await _notifier.NotifyAsync(req.UserId, "Document ready");

      return Ok(doc);
  }

  // GOOD: Single responsibility per method
  public async Task<ActionResult> CreateDocument(CreateRequest request)
  {
      var doc = await _store.CreateAsync(request.ToDocument());
      await _pipeline.EnqueueProcessingAsync(doc.Id);  // Background job handles rest
      return Ok(doc);
  }

SEVERITY: Warning (if > 3 responsibilities), Critical (if > 5)
ACTION: Extract responsibilities into separate methods or services.
  Use background jobs for async processing chains.

================================================================================

OUTPUT FORMAT (include in review report after Style and Maintainability):

  ### AI Code Smell Detection

  | # | Smell | Files Affected | Count | Severity |
  |---|-------|----------------|-------|----------|
  | 1 | Interface w/ single impl | AuthService.cs | 1 | Warning |
  | 2 | Try/catch log-rethrow | -- | 0 | -- |
  | 3 | Null check on non-nullable | DataGrid.tsx | 3 | Suggestion |
  | 4 | Code-restating comment | AuthService.cs, DataGrid.tsx | 5 | Suggestion |
  | 5 | Method > 3 responsibilities | DocumentEndpoints.cs | 1 | Warning |

  **AI Smell Score**: {count of warnings + suggestions} findings across {file count} files
  **Verdict**: {Clean / Minor issues / Needs refactoring}

See: .claude/skills/code-review/references/review-checklist.md -> "AI Code Smells" section
```

### Step 6: ADR Compliance Check
```
RUN subset of adr-check skill:

CRITICAL ADRs to always check:
  - ADR-001: BFF endpoints in Minimal API (no Functions hosting BFF endpoints; Functions OK for out-of-band integration)
  - ADR-002: Thin plugins (<50ms, no HTTP)
  - ADR-007: Graph types isolated
  - ADR-008: Endpoint filters for auth
  - ADR-013 (refined 2026-05-20): AI architecture — CRUD code MUST consume AI via Services/Ai/PublicContracts/ facades (no direct injection of IOpenAiClient, IPlaybookService, or other AI-internal types into CRUD code)
  - ADR-028: Spaarke Auth v2 contract — client: useAuth() + authenticatedFetch (no raw fetch with Authorization headers, no accessToken: string props, no tokenBridge/__SPAARKE_BFF_TOKEN__, no PublicClientApplication outside @spaarke/auth); server: DefaultAzureCredential (MI) for Graph/Dataverse when MI enabled; HMAC webhook signing; tenant-specific MSAL authority (INV-3/INV-6)

IF violations found:
  LINK to full adr-check skill for details
  FLAG: Critical

See: .claude/skills/adr-check/ for detailed ADR validation rules
```

### Step 6.5: BFF Hygiene Check (Conditional — CLAUDE.md §10)

**Trigger condition**: any file in scope is under:
- `src/server/api/Sprk.Bff.Api/**`
- `src/server/shared/Spaarke.Core/**`
- `src/server/shared/Spaarke.Dataverse/**`

If trigger condition is met, run the binding §10 pre-merge checklist from [`.claude/constraints/bff-extensions.md`](../../constraints/bff-extensions.md):

```
LOAD .claude/constraints/bff-extensions.md (full file — it is the source of truth)

APPLY Section A (Pre-Merge Checklist — Binding):
  1. Placement Justification stated in PR description or design.md?
     (Even "obviously in BFF" requires a one-sentence justification per §10.)
  2. Relevant ADRs cited in PR/design? (ADR-001, ADR-007, ADR-008, ADR-010, ADR-013 most common)
  3. Publish-size impact verified? (only if NuGet packages added)
     - Baseline: ~60 MB compressed per .claude/constraints/azure-deployment.md
     - Run: dotnet publish --runtime linux-x64; inspect output size
  4. NO new direct CRUD→AI dependency?
     - CHECK: grep diff for new injections of IOpenAiClient, IPlaybookService, or other Services/Ai/ internal types into code OUTSIDE Services/Ai/
     - If found in CRUD code (Finance/Workspace/Jobs handlers outside Services/Ai/) → Violation; must use Services/Ai/PublicContracts/ facade
  5. Feature-module DI used? (per ADR-010 — Add{Feature}Module() extension, not flat Program.cs registrations)

APPLY Section B (New Package References) — only if .csproj changed:
  - dotnet list package --vulnerable --include-transitive — flag any new HIGH-severity CVE
  - Pre-release packages (-beta, -rc, -preview) require inline csproj comment justifying

APPLY Section C (New Endpoints) — only if MapPost/MapPut/MapGet added:
  - Minimal API (not MVC controllers)?
  - Endpoint-filter authorization (.AddEndpointFilter<...>())?
  - Results.Problem(...) for errors (RFC 7807)?
  - Registered via Map{Feature}Endpoints extension, NOT directly in Program.cs?

APPLY Section D (New Background Work) — only if IHostedService/IJobHandler added:
  - Uses IJobHandler<T> per ADR-004 (not free-form IHostedService)?
  - AI-coupled jobs in Services/Ai/Jobs/ (not Services/Jobs/Handlers/)?

FLAG SEVERITIES:
  - Missing Placement Justification → Critical (binding §10 imperative)
  - New direct CRUD→AI dep → Critical (per refined ADR-013)
  - New HIGH-severity CVE → Critical
  - Endpoint added directly in Program.cs → Warning (ADR-001/008)
  - Feature-module pattern not followed → Warning (ADR-010)
  - Publish-size verification skipped when packages added → Warning
```

See: [`.claude/constraints/bff-extensions.md`](../../constraints/bff-extensions.md) for full rules; [`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](../../../docs/assessments/bff-ai-extraction-assessment-2026-05-20.md) for the evidence base.

### Step 6.6: Component Justification Check (Universal — CLAUDE.md §11)

**This step extends Step 6.5 (BFF Hygiene) to ANY new component, not just BFF.** It enforces root CLAUDE.md §11 "Component Justification — Default to Reuse" at code-review time.

**Fires for ANY of**:
- New `.cs` / `.ts` / `.tsx` / `.ps1` file added under `src/`
- New endpoint route or handler
- New DI registration (`services.Add*`)
- New package reference (`<PackageReference>` or `dependencies` entry)
- New Dataverse column / entity / alternate key added by the change

**Does NOT fire for**: pure modifications to existing files (edit, refactor, fix bug, add tests for existing surface, rename, format).

**Workflow**:

```
1. LOCATE the task POML that authorized the change:
   - Look up by branch name pattern or by PR description reference to TASK-INDEX
   - If multiple tasks contributed, check each task's POML in turn

2. READ the <justification> element from the task POML:
   IF <justification> missing AND new-component scope present:
     → flag as code-review WARNING (severity proportional to surface):
       - New file in shared lib OR new endpoint → HIGH
       - New DI registration OR new package → HIGH
       - New internal helper / private class → LOW
     → cite root CLAUDE.md §11 + task-create Step 3.5.6 as the rule source
     → recommend: add <justification> retroactively before merge

3. VERIFY the three answers are concrete:

   a. <existing>: must cite a file:line found via Grep, OR show the grep command + "no matches" output.
      - "None" with no grep evidence → WARNING (lazy answer)
      - "Similar to FooService but different in X" without file:line → SUGGESTION (sharpen)

   b. <extension>: must be a yes/no with reason. Acceptable reasons include:
      - "Existing X uses lookup-by-name; this needs lookup-by-id; signatures incompatible"
      - "Existing X is in CRUD namespace; new component must live in AI facade per ADR-013"
      - "Existing X is sync; this requires async stream signature"
      NOT acceptable: "cleaner separation", "for testability", "to avoid coupling"
      → if extension reason is hollow → WARNING

   c. <cost-of-doing-nothing>: must name a concrete behavior, contract, or failure mode:
      - "Without this, callers cannot resolve playbooks by stable ID; name-resolution breaks on rename"
      - "Without this, the new endpoint returns 200 with raw exception text instead of RFC 7807 ProblemDetails"
      NOT acceptable: "scalability", "abstraction layer", "future flexibility", "best practice"
      → if cost-of-doing-nothing is abstract → WARNING

4. IF any of (a), (b), (c) is hollow / boilerplate / abstract:
   → flag as code-review SUGGESTION to revisit scope before merge
   → if all three are hollow → escalate to WARNING

5. PASS through to Step 7 (technology-specific checks) once justification quality verified.
```

**Anti-patterns to flag**:

| Pattern | Severity |
|---|---|
| New file with no task POML reference findable | WARNING |
| Justification missing entirely | WARNING |
| `<existing>` says "none" without grep evidence | WARNING |
| `<extension>` reason is "cleaner separation" or similar abstract framing | WARNING |
| `<cost-of-doing-nothing>` is "scalability" / "future flexibility" / "abstraction layer" | WARNING |
| `<cost-of-doing-nothing>` doesn't name a concrete behavior or contract | WARNING |
| 8+ new components in one PR with collective justification (instead of per-component) | WARNING |

**Why this gate matters**: BFF Hygiene §10 catches scope creep into the BFF specifically. §11 catches it everywhere — shared libs, PCFs, code pages, plugins, Dataverse columns. The chat-routing-redesign-r1 project produced three real-world failures of this gate (LegalWorkspace dead-code misreading, sprk_playbookcode field choice, 8-tool surface) — all caught only after they shipped. This step catches them at PR review.

See: root [`CLAUDE.md` §11](../../../CLAUDE.md) for the principle; [`.claude/skills/task-create/SKILL.md` Step 3.5.6](../task-create/SKILL.md) for the authoring-time gate.

### Step 7: Technology-Specific Checks

#### For .NET Code (.cs)
```
  Minimal API patterns
  - Endpoint groups properly organized
  - Result pattern for error handling
  - Dependency injection registration minimal (ADR-010)

  Nullable reference types
  - Proper null checks
  - No null-forgiving (!) without justification

  Modern C# patterns
  - Using file-scoped namespaces
  - Using records for DTOs where appropriate
```

#### For TypeScript/PCF (.ts, .tsx)
```
  React patterns
  - Proper hook usage (rules of hooks)
  - Memoization where appropriate
  - Key prop in lists

  TypeScript strictness
  - No "any" types without justification
  - Proper interface definitions
  - Null/undefined handling

  Fluent UI v9 Design System (ADR-021)
  - Using @fluentui/react-components (v9), NOT @fluentui/react (v8)
  - Icons from @fluentui/react-icons with currentColor
  - Semantic tokens (tokens.colorNeutralBackground1), no hard-coded colors
  - FluentProvider wrapper with theme
  - Dark mode compatibility (no hard-coded hex colors)
  - Accessibility: aria-labels on icon-only buttons
```

#### For Plugin Code
```
  Plugin constraints (ADR-002)
  - No HttpClient usage
  - No external service calls
  - Execution time estimation <50ms
  - Code size <200 LoC
```

### Step 8: Generate Review Report
````markdown
## Code Review Report

**Files Reviewed:** {count} files
**Review Depth:** {quick|standard|thorough}
**Date:** {timestamp}

### Quantitative Metrics

| File | Lines | Public Methods | Private Methods | Ctor Params | Complexity Est. | Interfaces |
|------|-------|----------------|-----------------|-------------|-----------------|------------|
| {file} | {n} | {n} | {n} | {n} | {n} | {n} |

{Flag any metrics exceeding warning/critical thresholds}

### Quality Direction Summary

| File | Overall Signal | Key Changes |
|------|----------------|-------------|
| {file} | {signal} | {description} |

**Direction**: {Overall assessment -- is this changeset improving or degrading quality?}

### AI Code Smell Detection

| # | Smell | Files Affected | Count | Severity |
|---|-------|----------------|-------|----------|
| 1 | Interface w/ single impl | {files or --} | {n} | {severity} |
| 2 | Try/catch log-rethrow | {files or --} | {n} | {severity} |
| 3 | Null check on non-nullable | {files or --} | {n} | {severity} |
| 4 | Code-restating comment | {files or --} | {n} | {severity} |
| 5 | Method > 3 responsibilities | {files or --} | {n} | {severity} |

**AI Smell Score**: {total findings} across {file count} files

### Critical Issues (Block Merge)

{List critical security, performance, or ADR violations}

### Warnings (Should Address)

{List warnings that should be fixed but are not blockers}

### Suggestions (Consider)

{List style improvements and optional enhancements}

### What is Good

{Highlight positive patterns observed}

### Summary by Category

| Category | Critical | Warning | Suggestion |
|----------|----------|---------|------------|
| Security | 0 | 1 | 0 |
| Performance | 0 | 0 | 2 |
| Style | 0 | 3 | 5 |
| ADR Compliance | 0 | 0 | 0 |
| AI Code Smells | 0 | 2 | 3 |

### Recommended Actions

1. {First priority fix}
2. {Second priority fix}
...

### Auto-Fixable Issues

Run these commands to fix some issues automatically:
- `dotnet format` - Fix C# formatting
- ADR check: `/adr-check` for full compliance scan
````

## Conventions

### Severity Levels
| Level | Meaning | Action Required |
|-------|---------|-----------------|
| Critical | Security/correctness issue | Must fix before merge |
| Warning | Quality concern | Should fix, discuss if not |
| Suggestion | Enhancement | Optional, at author discretion |

### Review Depth
| Depth | Focus | Use When |
|-------|-------|----------|
| quick | Critical issues only | Small changes, hotfixes |
| standard | Critical + warnings | Normal development |
| thorough | All levels + suggestions | Major features, refactors |

### Code Smells to Flag
- Methods >50 lines
- Classes >500 lines
- Files with >10 imports
- Nested conditionals >3 levels
- Commented-out code
- Magic numbers without constants

## Integration Points

This skill is called at these points in the development workflow:

### 1. In task-execute (Step 9.5 - Quality Gates)

After code implementation and before task completion:
```
AFTER all implementation steps complete:
  RUN /code-review on files modified in this task
  IF critical issues found:
    -> Fix issues before marking task complete
  RUN /adr-check on modified files
  THEN proceed to task completion
```

### 2. In push-to-github (Step 1 - Pre-flight)

Before committing changes:
```
RUN quality checks (ask user first):
  -> Execute linting on changed files
  -> Execute /code-review on changed files
  -> Execute /adr-check on changed files
  -> Report any issues found
```

### 3. In project-wrap-up (Task 090)

Final quality validation before project completion:
```
RUN /code-review on entire project scope
RUN /repo-cleanup to audit and clean ephemeral files
VERIFY all issues addressed before marking project complete
```

---

## Resources

### Reference Files
- Root `CLAUDE.md` - Coding standards and conventions
- `docs/architecture/` - Architecture decisions and constraints
- `.claude/skills/adr-check/references/` - ADR validation rules
- `.claude/skills/code-review/references/review-checklist.md` - Review checklist with AI smell detection

### Related Skills
- **adr-check**: Deep-dive on architecture compliance
- **spaarke-conventions**: Detailed coding standards enforcement
- **task-create**: Include code review as task deliverable
- **push-to-github**: Linting runs as pre-flight check before commits
- **task-execute**: Calls code-review in Step 9.5 Quality Gates
- **repo-cleanup**: Complementary skill for repository hygiene

## Examples

### Example 1: Review Git Changes
**Trigger**: "Review my changes before I commit"

**Process**:
1. Run `git diff --name-only` to get changed files
2. Read each file, apply review checklist
3. Output categorized findings

### Example 2: Focused Security Review
**Trigger**: "Do a security review of the auth endpoints"

**Process**:
1. Find files related to authentication
2. Focus on security checklist items
3. Report security-specific findings only

### Example 3: Thorough Pre-PR Review
**Trigger**: "Thorough code review for PR"

**Process**:
1. Get all changed files vs main branch
2. Run all checklist items at thorough depth
3. Include suggestions for improvement
4. Run full adr-check

### Example 4: Quick Hotfix Review
**Trigger**: "/code-review quick src/api/fix.cs"

**Process**:
1. Review single file
2. Focus on critical issues only
3. Skip style suggestions

## Validation Checklist

Before completing code review, verify:
- [ ] All files in scope were reviewed
- [ ] Quantitative metrics collected for each file
- [ ] Quality direction (before/after) assessed for changed files
- [ ] AI code smell checklist applied
- [ ] Critical issues have clear descriptions
- [ ] Warnings include specific locations (file:line)
- [ ] ADR compliance was checked
- [ ] Positive patterns were acknowledged
- [ ] Next steps are actionable

---

## Failure Modes & Recovery

| Failure | Cause | Prevention / Recovery |
|---|---|---|
| Review marks PR "clean" but reviewers find issues later | The skill missed a dimension (e.g., looked at security + performance but skipped design quality) | The 10-dimension checklist in Workflow MUST be applied to every review. Skipping a dimension because "it didn't seem to apply" is the failure mode. Use the dimension as a prompt; even a one-line confirmation is OK. |
| Review duplicates CI/CD findings (e.g., re-reports a Trivy vulnerability) | The skill wasn't aware of which CI/CD checks already ran | Before review, check `gh pr checks` to see what CI/CD covered. Add value beyond mechanical checks — focus on the judgment layer (design, abstraction, cohesion). Don't restate what `sdap-ci.yml` already flagged. |
| Review nits trivial style issues but misses an architecture violation | Surface-level scan only; didn't load ADRs | ALWAYS invoke `adr-check` as part of the review (or have it run separately). Style issues are CI's job; ADR violations are the judgment layer. |
| Review report is so long the user doesn't read it | Skill produced a wall of findings without prioritization | Sort findings: Critical → Warning → Suggestion → Acknowledged Positives. Critical items first. If 50+ findings, cluster them — present the top 5 with detail, the rest as a summary count. |
| Review invoked but skill auto-stopped mid-way | Context budget pressure; the 846-line procedure didn't fit cleanly | The skill is long but procedural detail matters. Invoke on Opus with effort `high` for full reviews; `medium` for spot checks. Don't run on Haiku for full reviews — the judgment layer needs the deeper model. |
| Trigger phrase ambiguity — "review code" interpreted as just running CI/CD | User mentally maps "review" → "run tests" instead of judgment-layer | Skill description explicitly calls out "judgment-layer quality gate that complements CI/CD's mechanical checks" (added 2026-05-17). When triggered, the agent should NOT just check CI status — it should perform the review. |
