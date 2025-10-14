# Phase 2 - Task 7: Fix Test Dependencies (Security & Compatibility)

**Phase**: 2 (Code Quality & Security)
**Duration**: 5-10 minutes
**Risk**: Low (tests only, not production)
**Pattern**: Dependency maintenance
**Priority**: HIGH - Blocks test execution, has CVE

---

## Current State (Before Starting)

**Current Test Dependency Problem**:
- Test packages from 2010-2012 (ancient versions)
- Not compatible with .NET 8.0 (NU1202 errors)
- Known high severity CVE in Newtonsoft.Json 3.5.8
- Tests cannot run (`dotnet test` fails with errors)
- Missing modern testing features

**Package Versions**:
```xml
<PackageReference Include="FluentAssertions" />      <!-- 1.3.0.1 (2011) -->
<PackageReference Include="xunit" />                  <!-- 1.7.0.1540 (2012) -->
<PackageReference Include="Moq" />                    <!-- 3.1.416.3 (2010) -->
<PackageReference Include="Newtonsoft.Json" />        <!-- 3.5.8 (2010) -->
<PackageReference Include="WireMock.Net" />           <!-- 1.0.0 (old) -->
```

**Impact**:
- ❌ Cannot validate Phase 2 refactoring work
- ❌ Security vulnerability (Newtonsoft.Json CVE)
- ❌ Build fails: `dotnet test` errors
- ❌ Missing async/await test support

**Quick Verification**:
```bash
# Try to run tests
cd tests/unit/Spe.Bff.Api.Tests
dotnet test

# Expected: NU1202 errors (not compatible with net8.0)
# If tests pass - task already complete!
```

---

## Background: Why Test Dependencies Are Outdated

**Historical Context**:
- Test project created in early .NET days
- Package versions not specified (installed whatever was available)
- NuGet resolved to ancient versions (1.x, 3.x)
- No version constraints in `.csproj` file
- Normal maintenance forgotten over time

**How This Happened**:
```xml
<!-- PROBLEM: No version specified -->
<PackageReference Include="FluentAssertions" />

<!-- NuGet behavior: -->
<!-- 1. No version = install "latest" at time of first restore -->
<!-- 2. Once restored, version locked in packages.lock.json -->
<!-- 3. Never updated unless explicitly upgraded -->
<!-- 4. Result: Ancient versions from 2010-2012 -->
```

**Why It Wasn't Noticed Earlier**:
- Tests may not have been run regularly
- Project may have been migrated from older .NET version
- Build warnings ignored
- Test project less visible than production code

**What Changed**:
- Now running .NET 8.0 (requires compatible test packages)
- Security scanning flagged Newtonsoft.Json CVE
- Phase 2 refactoring requires test validation
- Need working tests to verify refactoring correctness

**Why This Is Safe to Fix**:
- **Test code only**: Not deployed to production
- **Backward compatible**: Modern packages support old test syntax
- **Quick rollback**: Simple `git revert` if issues
- **Low risk**: Worst case = fix a few assertion method calls

**Real Impact Example**:
```bash
# BEFORE: Cannot test
$ dotnet test
error NU1202: Package FluentAssertions 1.3.0.1 is not compatible with net8.0
Build FAILED.

# AFTER: Tests run
$ dotnet test
Test run for Spe.Bff.Api.Tests.dll (.NETCoreApp,Version=v8.0)
Passed! - Failed: 0, Passed: 45, Skipped: 0, Total: 45
```

---

## 🤖 AI PROMPT

```
CONTEXT: You are working on Phase 2 cleanup, fixing outdated test dependencies that block test execution and have security vulnerabilities.

TASK: Update test package versions in Spe.Bff.Api.Tests.csproj to modern, .NET 8.0-compatible versions.

CONSTRAINTS:
- Must update to latest stable versions (not beta/preview)
- Must maintain backward compatibility with existing test code
- Must fix Newtonsoft.Json CVE (high severity)
- Must enable .NET 8.0 compatibility

VERIFICATION BEFORE STARTING:
1. Verify tests currently fail: `dotnet test` in test project
2. Verify ancient package versions in `.csproj` (no version specified)
3. Verify security warning: NU1903 (Newtonsoft.Json vulnerability)
4. If tests already pass, STOP - task already complete

FOCUS: Stay focused on test dependencies only. Do NOT modify production code or production dependencies.
```

---

## Goal

Update test project dependencies from ancient versions (2010-2012) to modern, .NET 8.0-compatible versions to:
- ✅ Enable test execution
- ✅ Fix security vulnerabilities
- ✅ Enable modern testing features

**Problem**:
- Cannot run tests (NU1202 compatibility errors)
- Known CVE in Newtonsoft.Json 3.5.8
- Missing modern async test support

**Target**:
- Tests run successfully
- No security vulnerabilities
- Modern package versions

---

## Pre-Flight Verification

### Step 0: Verify Context and Prerequisites

```bash
# 1. Verify tests currently fail
cd /c/code_files/spaarke/tests/unit/Spe.Bff.Api.Tests
dotnet test --nologo 2>&1 | grep -E "error NU1202|error NU1903"
# Expected: See NU1202 errors (incompatible packages)

# 2. Check current package versions
cat Spe.Bff.Api.Tests.csproj | grep PackageReference
# Expected: No version attributes (empty)

# 3. Verify this is test project (not production)
pwd
# Expected: Path contains "tests/unit/"

# 4. Check for security warnings
dotnet list package --vulnerable
# Expected: Newtonsoft.Json vulnerability warning
```

**If tests already pass**: STOP - task already complete!

---

## Files to Edit

```bash
- [ ] tests/unit/Spe.Bff.Api.Tests/Spe.Bff.Api.Tests.csproj
```

---

## Implementation

### Step 1: Update Package Versions

**File**: `tests/unit/Spe.Bff.Api.Tests/Spe.Bff.Api.Tests.csproj`

**BEFORE**:
```xml
<ItemGroup>
  <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
  <PackageReference Include="Microsoft.NET.Test.Sdk" />
  <PackageReference Include="xunit" />
  <PackageReference Include="xunit.runner.visualstudio">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  </PackageReference>
  <PackageReference Include="FluentAssertions" />
  <PackageReference Include="coverlet.collector">
    <PrivateAssets>all</PrivateAssets>
  </PackageReference>
  <PackageReference Include="Newtonsoft.Json" />
  <PackageReference Include="Moq" />
  <PackageReference Include="WireMock.Net" />
</ItemGroup>
```

**AFTER**:
```xml
<ItemGroup>
  <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.0.0" />
  <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
  <PackageReference Include="xunit" Version="2.6.2" />
  <PackageReference Include="xunit.runner.visualstudio" Version="2.5.4">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  </PackageReference>
  <PackageReference Include="FluentAssertions" Version="6.12.0" />
  <PackageReference Include="coverlet.collector" Version="6.0.0">
    <PrivateAssets>all</PrivateAssets>
  </PackageReference>
  <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  <PackageReference Include="Moq" Version="4.20.70" />
  <PackageReference Include="WireMock.Net" Version="1.5.45" />
</ItemGroup>
```

**Package Update Rationale**:

| Package | Old → New | Reason |
|---------|-----------|--------|
| FluentAssertions | 1.3.0.1 → 6.12.0 | .NET 8.0 support, modern assertions |
| xunit | 1.7.0 → 2.6.2 | Async support, .NET 8.0 compatibility |
| Moq | 3.1.416.3 → 4.20.70 | Modern mocking, not actually used but updated |
| Newtonsoft.Json | 3.5.8 → 13.0.3 | **Fixes CVE**, .NET 8.0 support |
| WireMock.Net | 1.0.0 → 1.5.45 | Modern HTTP mocking, stability |
| Others | (none) → latest | Add explicit versions, .NET 8.0 support |

---

## Validation

### Build Check
```bash
cd /c/code_files/spaarke/tests/unit/Spe.Bff.Api.Tests
dotnet restore --nologo
dotnet build --nologo
# Expected: Build succeeded, 0 errors
```

### Run Tests
```bash
dotnet test --nologo --verbosity minimal
# Expected: Tests pass (or show specific test failures, not package errors)
# Success criteria: No NU1202, NU1903 errors
```

### Check for Breaking Changes
```bash
# If tests fail, check for FluentAssertions syntax changes
# Common issue: Should().Be() vs Should().Equal()
# Fix: Update assertion syntax (usually 1-2 lines)

# Example fix if needed:
# OLD: result.Should().Equal(expected);
# NEW: result.Should().Be(expected);
```

### Security Check
```bash
dotnet list package --vulnerable
# Expected: No vulnerabilities reported
```

---

## Checklist

- [ ] **Pre-flight**: Verified tests currently fail (NU1202 errors)
- [ ] **Pre-flight**: Verified ancient package versions (1.x, 3.x)
- [ ] **Pre-flight**: Verified security warning (Newtonsoft.Json CVE)
- [ ] Updated `FluentAssertions` to 6.12.0
- [ ] Updated `xunit` to 2.6.2
- [ ] Updated `xunit.runner.visualstudio` to 2.5.4
- [ ] Updated `Moq` to 4.20.70
- [ ] Updated `Newtonsoft.Json` to 13.0.3 (fixes CVE)
- [ ] Updated `WireMock.Net` to 1.5.45
- [ ] Updated `Microsoft.AspNetCore.Mvc.Testing` to 8.0.0
- [ ] Updated `Microsoft.NET.Test.Sdk` to 17.8.0
- [ ] Updated `coverlet.collector` to 6.0.0
- [ ] Restore succeeded: `dotnet restore`
- [ ] Build succeeded: `dotnet build`
- [ ] Tests run: `dotnet test` (no package errors)
- [ ] No security vulnerabilities: `dotnet list package --vulnerable`
- [ ] Fixed any FluentAssertions syntax issues (if needed)

---

## Expected Results

**Before**:
- ❌ Tests cannot run (NU1202 errors)
- ❌ Newtonsoft.Json CVE (high severity)
- ❌ Package versions: 1.x, 3.x from 2010-2012
- ❌ Not compatible with .NET 8.0

**After**:
- ✅ Tests run successfully
- ✅ No security vulnerabilities
- ✅ Package versions: 6.x, 13.x (modern)
- ✅ Compatible with .NET 8.0
- ✅ Can validate Phase 2 refactoring work

---

## Risk Assessment

### What Could Break?

**Risk Level**: 🟢 **LOW** (tests only, not production)

**Potential Issues**:
1. ⚠️ **FluentAssertions syntax** - Some methods renamed in v6
   - **Impact**: 1-2 test assertions may need updates
   - **Fix Time**: 5 minutes
   - **Example**: `.Equal()` → `.Be()`

2. ⚠️ **xunit Theory data** - Data attribute syntax changes
   - **Impact**: Rare, usually backward compatible
   - **Fix Time**: 5 minutes per test

3. ✅ **Moq** - Not used in tests (zero impact)

4. ✅ **Newtonsoft.Json** - Core API unchanged (zero impact)

**Worst Case Scenario**:
- Need to update 1-2 FluentAssertions method calls
- Total fix time: ~5 minutes
- Does NOT affect production code

**Rollback Plan**:
```bash
git revert <commit-hash>
# Instant rollback if needed
```

---

## Troubleshooting

### Issue: Tests still fail with NU1202

**Cause**: Package cache not cleared

**Fix**:
```bash
dotnet nuget locals all --clear
dotnet restore --force
dotnet build
```

### Issue: FluentAssertions method not found

**Cause**: Syntax changed in v6 (rare)

**Fix**:
```csharp
// OLD (v1.3)
result.Should().Equal(expected);

// NEW (v6.12)
result.Should().Be(expected);
```

### Issue: Some tests fail (not package errors)

**Cause**: Existing test logic issues (unrelated to package update)

**Fix**: This is expected - package update revealed pre-existing test issues. These are separate from the package update task.

---

## Context Verification

Before marking complete, verify:
- [ ] ✅ Build succeeds (`dotnet build`)
- [ ] ✅ Tests execute (`dotnet test` runs, no package errors)
- [ ] ✅ No security vulnerabilities
- [ ] ✅ Task stayed focused (only test packages, no production code)

**If any item unchecked**: Review and fix before proceeding

---

## Commit Message

```bash
git add tests/unit/Spe.Bff.Api.Tests/Spe.Bff.Api.Tests.csproj
git commit -m "$(cat <<'EOF'
fix(tests): update test dependencies to .NET 8.0-compatible versions

- Update FluentAssertions: 1.3.0.1 → 6.12.0 (.NET 8.0 support)
- Update xunit: 1.7.0 → 2.6.2 (async support, modern features)
- Update Moq: 3.1.416.3 → 4.20.70 (modern mocking)
- Update Newtonsoft.Json: 3.5.8 → 13.0.3 (FIXES CVE-XXXX high severity)
- Update WireMock.Net: 1.0.0 → 1.5.45 (stability improvements)
- Add explicit versions to all test packages

Fixes: NU1202 (package compatibility), NU1903 (security vulnerability)
Impact: Enables test execution, removes security warnings
Risk: Low (test project only, not production code)
Task: Phase 2, Task 7 (Test Dependencies)

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude <noreply@anthropic.com>
EOF
)"
```

---

## Next Task

➡️ [Phase 2 - Task 8: Fix Azure.Identity CVE](phase-2-task-8-fix-azure-identity-cve.md)

**What's next**: Update Azure.Identity to fix security vulnerability

---

## Related Resources

- **Security**: [GitHub Advisory GHSA-5crp-9r3c-p9vr](https://github.com/advisories/GHSA-5crp-9r3c-p9vr) (Newtonsoft.Json)
- **Package Updates**:
  - [FluentAssertions Changelog](https://github.com/fluentassertions/fluentassertions/releases)
  - [xunit Releases](https://github.com/xunit/xunit/releases)
  - [Newtonsoft.Json Security](https://github.com/JamesNK/Newtonsoft.Json/security/advisories)
