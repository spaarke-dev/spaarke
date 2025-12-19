# ADR Validation Process

**Last Updated:** December 12, 2025
**Status:** Active

## Overview

This document defines the process for validating code changes against SDAP's Architecture Decision Records (ADRs). The validation system uses a hybrid approach combining automated CI/CD testing (NetArchTest) and interactive pre-commit guidance (Claude Code skill).

## Components and Tools

### 1. NetArchTest Suite

**Location:** `tests/Spaarke.ArchTests/`
**Purpose:** Automated structural validation of architectural constraints
**Technology:** NetArchTest.Rules library with xUnit

**Test Coverage:**
- **ADR-001:** No Azure Functions packages or attributes (2 tests)
- **ADR-002:** No plugin orchestration in BFF (2 tests)
- **ADR-007:** Graph SDK isolation to Infrastructure layer (3 tests)
- **ADR-008:** Authorization via endpoint filters (4 tests)
- **ADR-009:** Redis-first caching policy (3 tests)
- **ADR-010:** DI minimalism patterns (4 tests)

**Total:** 18 automated tests across 6 test classes

**Run Locally:**
```bash
dotnet test tests/Spaarke.ArchTests/
```

### 2. Claude Code Skill

**Location:** `.claude/skills/adr-check.md`
**Purpose:** Interactive pre-commit validation with contextual guidance
**Coverage:** ADR guidance beyond what NetArchTest enforces

**Capabilities:**
- Validates specific files or recent git changes
- Provides violation explanations with context
- Suggests concrete fixes with code examples
- Links to relevant ADR documents

**Run Locally:**
```bash
/adr-check
```

### 3. Legacy PowerShell Script

**Location:** `scripts/adr_policy_check.ps1`
**Status:** Deprecated (maintained for reference only)
**Note:** NetArchTest is now the primary validation tool

## How Validation is Triggered

### Automatic Triggers (CI/CD)

**GitHub Actions Workflow:** `.github/workflows/sdap-ci.yml`

Validation runs automatically on:
- Every pull request
- Every push to `master` branch

**Execution Flow:**
1. Code Quality job restores dependencies
2. Runs `dotnet format` verification
3. Runs legacy PowerShell script (continue-on-error: true)
4. **Runs NetArchTest** (continue-on-error: true)
   - ⚠️ Violations do NOT block PR merge
   - Results uploaded as artifacts
   - Violations reported via PR comment (see below)
5. **Posts ADR Violations Report as PR comment**
   - Summarizes pass/fail status
   - Lists violations with file locations
   - Links to relevant ADR documents
   - Updates on each push to PR

**CI/CD Configuration:**
```yaml
- name: ADR architecture tests (NetArchTest)
  id: adr-tests
  run: dotnet test tests/Spaarke.ArchTests/Spaarke.ArchTests.csproj --no-restore --logger "trx;LogFileName=adr-results.trx" --results-directory ./TestResults
  continue-on-error: true  # Don't block PR - violations reported via comment
```

**PR Comment Example:**
```markdown
## 🏛️ ADR Architecture Validation Report

**Summary:** 13/18 tests passed ⚠️ (5 violations)

**Status:** ADR violations detected. These are **non-blocking warnings** but should be reviewed.

### ⚠️ Violations Found

#### 1. ADR-007: Endpoints must not reference Graph SDK
...
```

### Manual Triggers (Local Development)

**Pre-Commit Validation:**
```bash
# Interactive validation with guidance
/adr-check

# Automated validation
dotnet test tests/Spaarke.ArchTests/

# Specific ADR validation
dotnet test tests/Spaarke.ArchTests/ --filter "FullyQualifiedName~ADR007"
```

**During Code Review:**
- Reviewers should verify ADR checklist in PR template
- Run `/adr-check` on specific files if concerns arise

## How Results Are Tracked, Managed, and Resolved

### Result Reporting

**CI/CD Results:**
- Test results logged in GitHub Actions "Code Quality" job
- Failed tests show violation details with file paths
- Test artifacts uploaded for download (`.trx` files)
- Build status displayed on PR

**Local Results:**
- Terminal output shows pass/fail status per test
- Violation messages include:
  - ADR number and title
  - Specific file path and location
  - Description of violation
  - Suggested fix

**Example Output:**
```
Failed ADR-007: Endpoints must not reference Graph SDK [5 ms]
Error Message:
  ADR-007 violation: Endpoint classes must not reference Microsoft.Graph directly.
  Use SpeFileStore facade to isolate Graph SDK types.
  Failing types: Spe.Bff.Api.Api.FileAccessEndpoints
Stack Trace:
  at Spaarke.ArchTests.ADR007_GraphIsolationTests.EndpointsShouldNotReferenceGraphSdk()
  in C:\code_files\spaarke\tests\Spaarke.ArchTests\ADR007_GraphIsolationTests.cs:line 111
```

### Issue Tracking

**Automated Tracking Workflows:**

#### 1. PR Comment Reporting (Automatic)

**Trigger:** Every PR push
**Location:** PR conversation thread
**Status:** ✅ Implemented

**How it works:**
- NetArchTest runs on every PR
- Results posted as comment on the PR
- Comment updates on each push
- ⚠️ **Non-blocking:** Violations do not prevent PR merge
- Provides immediate visibility to developers

**What's included:**
- Pass/fail summary with test counts
- List of all violations with details
- Links to relevant ADR documents
- Instructions for local validation and fixes

**Use case:** Real-time feedback during development without blocking progress

#### 2. Architecture Audit Tracking Issue (On-Demand/Weekly)

**Trigger:** Manual or weekly schedule (Mondays 9 AM UTC)
**Workflow:** `.github/workflows/adr-audit.yml`
**Status:** ✅ Implemented

**How it works:**
- Runs NetArchTest on entire codebase
- Creates/updates single tracking issue
- Groups violations by ADR
- Auto-closes when all violations resolved
- Labels: `architecture`, `technical-debt`, `adr-audit`

**What's included:**
- Compliance percentage and trends
- Violations grouped by ADR
- Affected files for each violation
- Priority ratings (High/Medium/Low)
- Remediation instructions

**Manual trigger:**
```bash
# Via GitHub UI
Actions → ADR Architecture Audit → Run workflow

# Or via GitHub CLI
gh workflow run adr-audit.yml
```

**Use case:** Periodic technical debt tracking and architecture governance

#### 3. Individual Issue Creation (Manual - When Needed)

**When to use:**
- Specific violation requires dedicated tracking
- Needs assignment to specific developer
- Requires milestone/project board association
- Complex violation needing discussion

**Process:**
1. Create GitHub Issue manually
2. **Title:** `[ADR-XXX] Violation: <brief description>`
3. **Labels:** `architecture`, `technical-debt`, `adr-<number>`
4. **Body:** Include test output, file locations, suggested fixes
5. **Reference:** Link to relevant ADR document and tracking issue

**Use case:** Complex or high-priority violations requiring dedicated attention

### Resolution Process

**Step 1: Identify Violation**
```bash
# Run validation locally
dotnet test tests/Spaarke.ArchTests/

# Or use interactive skill
/adr-check
```

**Step 2: Understand Context**
- Read the violation message carefully
- Review referenced ADR document in `docs/reference/adr/ADR-XXX-*.md` (this repo)
- Use Claude skill for additional guidance: `/adr-check`

**Step 3: Fix Violation**
- Apply suggested fix from test output
- Follow ADR operationalization guidance
- Test fix locally before committing

**Step 4: Verify Fix**
```bash
# Re-run tests
dotnet test tests/Spaarke.ArchTests/

# Ensure all tests pass
```

**Step 5: Document (if applicable)**
- Update comments explaining architectural decision
- Reference ADR number in code comments if pattern is non-obvious

**Step 6: Commit and Push**
- Tests will run again in CI/CD
- PR will be unblocked when all tests pass

### Exception Process

**If ADR No Longer Applies:**
1. DO NOT ignore or skip the test
2. Create new ADR to supersede the old one
3. Update or remove the corresponding test
4. Update documentation (see "Updating the Process" section)

**If Legitimate Exception Needed:**
1. Document exception in ADR document
2. Update test to exclude specific case
3. Require explicit marker (e.g., `[AllowGraphTypes]` attribute)
4. Get architectural review approval

## How to Update the Process and Tools

### When Creating a New ADR

**Step 1: Create ADR Document**
```bash
# Create new ADR file
touch docs/reference/adr/ADR-013-new-decision.md

# Use standard ADR template with sections:
# - Context, Decision, Consequences, Alternatives, Operationalization
```

**Step 2: Determine if Enforceable via NetArchTest**

**Enforceable ADRs** (structural constraints):
- Package/namespace restrictions
- Type dependencies
- Naming conventions
- Inheritance/interface patterns

**Non-Enforceable ADRs** (runtime/process patterns):
- Async job contracts
- Storage access patterns
- Feature module conventions

**Step 3: Add NetArchTest Validation (if enforceable)**

Create new test class:
```bash
tests/Spaarke.ArchTests/ADR013_NewDecisionTests.cs
```

Template:
```csharp
using NetArchTest.Rules;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ADR-013: Brief description of decision
/// Validates that [specific constraint]
/// </summary>
public class ADR013_NewDecisionTests
{
    [Fact(DisplayName = "ADR-013: Description of what is validated")]
    public void TestMethodName()
    {
        // Arrange
        var assembly = typeof(Program).Assembly;

        // Act - Use NetArchTest to validate structure
        var result = Types.InAssembly(assembly)
            .That().[condition]
            .ShouldNot().[constraint]
            .GetResult();

        // Assert
        Assert.True(
            result.IsSuccessful,
            $"ADR-013 violation: [description]. " +
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}");
    }
}
```

**Step 4: Update Claude Code Skill**

Edit `.claude/skills/adr-check.md`:
```markdown
### ADR-013: New Decision Title

**Check for:**
- ❌ [Anti-pattern to avoid]
- ✅ [Pattern to enforce]

**How to check:**
```bash
# Commands to validate manually
grep -r "pattern" --include="*.cs"
```

**If violated:**
> **Fix:** [Concrete steps to resolve]
```

**Step 5: Update Documentation**

Update `docs/reference/adr/README-ADRs.md`:
```markdown
## Index of ADRs (001–020)

- [ADR-013: New decision title](./ADR-013-new-decision.md)
  Brief description of the decision
```

Update PR template `.github/pull_request_template.md`:
```markdown
## ADRs referenced
- [ ] ADR-013: New decision title
```

**Step 6: Update Changelog**

Add entry to `docs/reference/adr/README-ADRs.md`:
```markdown
## Change log
- 2025-XX-XX: Added ADR-013 (New decision title) with NetArchTest validation
```

**Step 7: Test Locally**
```bash
# Build and run tests
dotnet test tests/Spaarke.ArchTests/

# Verify new tests pass
```

### When Updating an Existing ADR

**Step 1: Update ADR Document**
- Change status if superseding: `Status: Superseded by ADR-XXX`
- Update "Last Revised" date
- Document what changed and why

**Step 2: Update Tests (if applicable)**

If constraints changed:
```bash
# Edit corresponding test file
tests/Spaarke.ArchTests/ADRXXX_*Tests.cs
```

If ADR superseded:
```csharp
[Fact(Skip = "ADR-XXX superseded by ADR-YYY")]
public void OldTest() { ... }
```

**Step 3: Update Claude Code Skill**
- Modify validation rules in `.claude/skills/adr-check.md`
- Update suggested fixes to reflect new guidance

**Step 4: Update Documentation**
```markdown
## Change log
- 2025-XX-XX: Updated ADR-XXX to [description]. Tests updated accordingly.
```

**Step 5: Test Changes**
```bash
# Ensure updated tests pass
dotnet test tests/Spaarke.ArchTests/

# Verify skill provides correct guidance
/adr-check
```

**Step 6: Communicate Change**
- Update PR template if checklist items changed
- Announce in team channel if significant change
- Update any affected design documents

### When Removing Validation

**Only remove validation if:**
- ADR is superseded by newer ADR
- Decision is no longer relevant

**Process:**
1. Mark test with `[Fact(Skip = "Reason")]` attribute (keep for historical reference)
2. Update ADR status to "Superseded"
3. Document removal in changelog
4. Update Claude skill to reference new guidance

## Validation Coverage Matrix

| ADR | Title | NetArchTest | Claude Skill | Notes |
|-----|-------|-------------|--------------|-------|
| 001 | Minimal API + BackgroundService | ✅ (2 tests) | ✅ | Package & attribute checks |
| 002 | Keep plugins thin | ✅ (2 tests) | ✅ | BFF-side validation |
| 003 | Lean authorization seams | ⚠️ Partial | ✅ | Runtime patterns |
| 004 | Async job contract | ❌ | ✅ | Runtime behavior |
| 005 | Flat storage in SPE | ❌ | ✅ | Usage patterns |
| 006 | Prefer PCF over web resources | ❌ | ✅ | Project structure |
| 007 | Graph isolation | ✅ (3 tests) | ✅ | Namespace checks |
| 008 | Authorization filters | ✅ (4 tests) | ✅ | Structural validation |
| 009 | Caching Redis-first | ✅ (3 tests) | ✅ | Dependency checks |
| 010 | DI minimalism | ✅ (4 tests) | ✅ | Pattern validation |
| 011 | Dataset PCF over subgrids | ❌ | ✅ | Usage patterns |
| 012 | Shared component library | ❌ | ✅ | Project structure |
| 013 | AI architecture | ❌ | ✅ | Runtime patterns |
| 014 | AI caching and reuse policy | ❌ | ❌ | New; add validation as patterns stabilize |
| 015 | AI data governance | ❌ | ❌ | New; primarily policy/process + code review |
| 016 | AI cost/rate-limit & backpressure | ❌ | ❌ | New; may be partially enforced via rate limiter + tests |
| 017 | Async job status & persistence | ❌ | ❌ | New; contract + runtime behavior |
| 018 | Feature flags and kill switches | ❌ | ❌ | New; add validation for required options gating |
| 019 | API errors and ProblemDetails standard | ❌ | ❌ | New; add helpers/tests incrementally |
| 020 | Versioning strategy | ❌ | ❌ | New; enforced via review + tooling as needed |

**Legend:**
- ✅ Fully validated
- ⚠️ Partially validated
- ❌ Not automated (manual/skill only)

## Troubleshooting

### Tests Fail Locally But Pass in CI

**Cause:** Different project references or stale build artifacts

**Fix:**
```bash
# Clean and rebuild
dotnet clean
dotnet restore
dotnet build
dotnet test tests/Spaarke.ArchTests/
```

### False Positive Violations

**Cause:** Test is too strict or doesn't account for legitimate exception

**Fix:**
1. Review ADR document for exceptions
2. Update test to exclude legitimate case
3. Document exception in test comments

### NetArchTest Package Updates

**Process:**
1. Update package version in `tests/Spaarke.ArchTests/Spaarke.ArchTests.csproj`
2. Test all validations still work correctly
3. Update documentation if API changed

## References

- **ADR Documents:** `docs/reference/adr/ADR-*.md`
- **Test Project:** `tests/Spaarke.ArchTests/`
- **Claude Skill:** `.claude/skills/adr-check.md`
- **CI/CD Workflow:** `.github/workflows/sdap-ci.yml`
- **PR Template:** `.github/pull_request_template.md`
- **NetArchTest Docs:** https://github.com/BenMorris/NetArchTest
