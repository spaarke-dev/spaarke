# Nightly Code Quality Review — Claude Code Headless Prompt

You are performing an automated nightly code quality review of the Spaarke repository. This is a .NET 8 + TypeScript/React codebase with PCF controls, React Code Pages, and a Minimal API backend.

Your job is to execute five review sections and produce a single JSON object as output. Follow each section's instructions precisely.

---

## Output Schema

Your output MUST be a single valid JSON object matching this TypeScript interface. Produce ONLY the JSON — no prose, no markdown fences, no explanation before or after.

```typescript
interface NightlyReviewResult {
  /** ISO 8601 timestamp of when the review was generated */
  generated_at: string;

  /** One-line overall quality signal */
  summary: string;

  /** Aggregated counts by severity */
  metrics: {
    critical_count: number;
    high_count: number;
    medium_count: number;
    low_count: number;
    total_count: number;
  };

  /** All findings from all five sections */
  findings: Finding[];
}

interface Finding {
  /** Which review section produced this finding */
  section: "recent-changes" | "pattern-consistency" | "adr-compliance" | "todo-aging" | "dead-code";

  /** Severity: critical = must fix immediately, high = fix soon, medium = should fix, low = consider fixing */
  severity: "critical" | "high" | "medium" | "low";

  /** Relative file path from repository root */
  file: string;

  /** Line number (0 if not applicable) */
  line: number;

  /** Concise description of the issue */
  description: string;

  /** Actionable remediation recommendation */
  recommendation: string;
}
```

### Example Finding

```json
{
  "section": "adr-compliance",
  "severity": "high",
  "file": "src/server/api/Sprk.Bff.Api/Endpoints/DocumentEndpoints.cs",
  "line": 42,
  "description": "GraphServiceClient injected directly into endpoint method, bypassing SpeFileStore facade",
  "recommendation": "Replace GraphServiceClient parameter with SpeFileStore. Route all SPE/Graph operations through the facade per ADR-007."
}
```

---

## Section 1: Recent Changes Review

**Goal**: Review files changed in the last 24 hours for adherence to established patterns.

**Steps**:

1. Run: `git log --since="24 hours ago" --name-only --pretty=format:"%H %s" --diff-filter=ACMR`
2. If no commits in the last 24 hours, report zero findings for this section and move on.
3. For each changed file:
   - Identify the file type (.cs, .ts, .tsx, .csproj, .json, etc.)
   - Find 2-3 similar files in the same directory or module (siblings)
   - Compare the changed file against its siblings for:
     - Consistent method ordering and structure
     - Consistent error handling approach
     - Consistent import/using organization
     - Consistent naming conventions
   - If the changed file deviates from the pattern of its siblings, create a finding.

**Severity guide**:
- `high`: Security-relevant deviation (missing auth filter, exposed secret pattern)
- `medium`: Structural inconsistency (different error handling, different method ordering)
- `low`: Style inconsistency (import ordering, comment style)

**Scope**: Only review files under `src/`. Ignore files in `tests/`, `projects/`, `docs/`, `infrastructure/`, `config/`, `.github/`.

---

## Section 2: Pattern Consistency

**Goal**: Sample files of the same type and verify they follow consistent structural patterns.

**Steps**:

1. Sample up to 3 files of each significant type:
   - `*Endpoints.cs` files in `src/server/api/`
   - `*Service.cs` files in `src/server/`
   - `*.tsx` component files in `src/client/pcf/`
   - `*.tsx` component files in `src/client/code-pages/`

2. For each group, compare:
   - Method ordering (constructor, public methods, private methods)
   - Error handling (ProblemDetails usage, try/catch patterns)
   - Import/using organization (system first, then project)
   - Comment style and documentation
   - Naming conventions

3. Only report findings where the inconsistency is actionable — two files doing the same thing differently, where one should conform to the other. Do NOT report findings that are merely stylistic preferences with no established pattern.

**Severity guide**:
- `medium`: Structural inconsistency across files of the same type
- `low`: Minor style inconsistency

---

## Section 3: ADR Compliance Sweep

**Goal**: Check the codebase for violations of the seven highest-signal Architecture Decision Records.

For each ADR below, run the specified detection checks. Only report actual violations — do NOT report informational observations.

### ADR-002: Thin Plugins (No HTTP/Graph in plugins)

Search in `src/dataverse/` and any directory containing plugin code:
- `HttpClient` usage → violation (plugins must not make HTTP calls)
- `System.Net.Http` imports → violation
- `GraphServiceClient` usage → violation
- Any plugin file exceeding 200 lines of code → violation
- Run: `grep -rn "HttpClient\|System\.Net\.Http\|GraphServiceClient" src/dataverse/ --include="*.cs"` (if directory exists)

### ADR-006: PCF Over Webresources (No legacy JS)

Search for legacy JavaScript webresource patterns:
- `.js` files in webresource directories that contain business logic (not just ribbon invocation stubs)
- jQuery usage: `grep -rn "\\$(\|jQuery" src/ --include="*.js" --include="*.ts"`
- Non-PCF form scripts with business logic (more than simple `Xrm.Navigation` or ribbon command invocations)

### ADR-007: SpeFileStore Facade (No Graph SDK leaks)

Search for Graph SDK types used outside the SpeFileStore facade:
- `GraphServiceClient` injected or used outside of `SpeFileStore` class: `grep -rn "GraphServiceClient" src/ --include="*.cs"` — flag any match NOT in SpeFileStore.cs or its direct infrastructure
- Graph SDK types (`DriveItem`, `UploadSession`, `ItemReference`) exposed in endpoint DTOs or service interfaces
- Run: `grep -rn "DriveItem\|UploadSession\|ItemReference" src/ --include="*.cs"` and flag any match outside `SpeFileStore.cs` or `Infrastructure/` directories

### ADR-008: Endpoint Filters for Auth (No global auth middleware)

Search for global authorization middleware patterns:
- `app.UseAuthorization()` in Program.cs or startup (this is for identity auth only — flag if used for resource authorization)
- `app.UseMiddleware<.*Auth` patterns: `grep -rn "UseMiddleware.*Auth\|UseMiddleware.*Security" src/server/ --include="*.cs"`
- Custom middleware classes that perform resource-level authorization checks

### ADR-010: DI Minimalism (<=15 non-framework registrations, concretes by default)

Check for DI anti-patterns:
- Count interfaces with single implementations: `grep -rn "services\.Add.*<I[A-Z]" src/server/ --include="*.cs"` — for each interface found, check if there is more than one implementation. Flag single-implementation interfaces (except `IAccessDataSource` and `IAuthorizationRule` which are allowed seams).
- Count total non-framework DI registrations in Program.cs or module registration files. Flag if > 15.

### ADR-021: Fluent UI v9 Design System (No Fluent v8, no hard-coded colors)

Search for Fluent v8 and hard-coded color violations:
- Fluent v8 imports: `grep -rn "from ['\"]@fluentui/react['\"]" src/client/ --include="*.ts" --include="*.tsx"`
- Hard-coded hex colors in styles: `grep -rn "#[0-9a-fA-F]\{3,8\}" src/client/ --include="*.ts" --include="*.tsx" --include="*.css"` — flag any that are not in theme/token files
- Alternative UI library imports (MUI, Ant Design): `grep -rn "@mui/\|antd\|@ant-design" src/client/ --include="*.ts" --include="*.tsx"`
- Custom icon font imports: `grep -rn "font-awesome\|material-icons" src/client/ --include="*.ts" --include="*.tsx" --include="*.css"`

### ADR-022: PCF Platform Libraries (No React 18 in PCF controls)

Search specifically in PCF control directories (`src/client/pcf/`):
- React 18 imports: `grep -rn "react-dom/client\|createRoot\|hydrateRoot" src/client/pcf/ --include="*.ts" --include="*.tsx"`
- React 18 in PCF package.json dependencies (not devDependencies): check that `react` and `react-dom` are in `devDependencies`, not `dependencies`
- Missing platform-library declarations in ControlManifest.Input.xml files

**Severity guide for all ADR checks**:
- `critical`: Security-related ADR violation (auth bypass, exposed secrets)
- `high`: Architectural ADR violation (wrong technology choice, facade bypass)
- `medium`: Pattern ADR violation (wrong import style, minor structural deviation)

---

## Section 4: TODO/FIXME Aging

**Goal**: Find TODO and FIXME comments older than 30 days and flag them for resolution.

**Steps**:

1. Find all TODO/FIXME comments in `src/` only:
   - Run: `grep -rn "TODO\|FIXME\|HACK\|WORKAROUND\|XXX" src/ --include="*.cs" --include="*.ts" --include="*.tsx"`

2. For each match, determine when it was introduced using git:
   - Run: `git log -1 --format="%aI" -S "EXACT_TODO_TEXT" -- "FILE_PATH"` to find the commit that introduced it
   - If the introduction date is more than 30 days ago, create a finding

3. Do NOT report TODO/FIXME items less than 30 days old — they are still within the acceptable resolution window.

4. Do NOT report TODO/FIXME items in test files, documentation, or project task files.

**Severity guide**:
- `medium`: TODO/FIXME item 30-90 days old
- `high`: TODO/FIXME item older than 90 days

---

## Section 5: Dead Code Detection

**Goal**: Identify unused code that should be cleaned up.

**Scope**: Only search within `src/` directory. Do NOT flag code in `tests/`, `projects/`, `infrastructure/`, or `docs/`.

**Checks**:

### 5a. Unused Using Directives (C#)

Search for `using` directives that are likely unused:
- Run: `grep -rn "^using " src/ --include="*.cs"` to find all using directives
- For each `using Namespace;`, check if any type from that namespace is referenced in the file
- Focus on obvious cases: namespaces that appear only in the using block and nowhere else in the file
- Only flag files with 3+ unused usings to reduce noise

### 5b. Private Methods with No Callers

Search for private methods that appear to have no callers within their class:
- Run: `grep -rn "private.*\(.*\)" src/ --include="*.cs"` to find private methods
- For each private method name, check if it is called elsewhere in the same file
- Flag methods where the name does not appear as a call site in the file
- Exclude: event handlers (matching `On*` pattern), methods with attributes like `[Fact]`, `[Theory]`, serialization callbacks

### 5c. Commented-Out Code Blocks

Search for large blocks of commented-out code:
- Find consecutive lines starting with `//` that contain code patterns (semicolons, braces, keywords like `var`, `return`, `if`, `await`)
- Only flag blocks of 10+ consecutive commented lines
- Run: `grep -n "^\s*//" src/ --include="*.cs" --include="*.ts" --include="*.tsx"` and analyze for consecutive blocks

### 5d. Unreachable Code

Search for code after `return` or `throw` statements within the same block:
- Statements after unconditional `return` or `throw` that are not part of a different branch
- This check has a high false-positive risk — only report obvious cases

**Severity guide**:
- `low`: Unused usings, small commented blocks
- `medium`: Private methods with no callers, large commented blocks (20+ lines)
- `high`: Significant dead code indicating abandoned feature work

---

## Final Instructions

1. Execute all five sections in order.
2. Collect all findings into the `findings` array.
3. Calculate `metrics` by counting findings per severity level.
4. Write a one-line `summary` describing the overall quality signal (e.g., "3 high-severity ADR violations and 5 aging TODOs require attention" or "No critical issues found; 2 minor pattern inconsistencies detected").
5. Set `generated_at` to the current ISO 8601 timestamp.
6. Output ONLY the JSON object. No text before it. No text after it. No markdown code fences.

If a section produces zero findings, that is fine — include zero findings from that section.

Suppress informational observations that do not have actionable remediation. Every finding MUST have a concrete `recommendation` field that tells the developer exactly what to do.
