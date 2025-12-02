# Task 4.3: Code Quality and Consistency - Cleanup and Standards

**Priority:** LOW-MEDIUM (Sprint 3, Phase 4)
**Estimated Effort:** 2 days
**Status:** POLISH & MAINTAINABILITY
**Dependencies:** All other Sprint 3 tasks (cleanup after implementation)

---

## Context & Problem Statement

After Sprint 2, the codebase has accumulated **technical debt and inconsistencies** that hurt maintainability:

**Problems**:
1. **Namespace Inconsistencies**:
   - `OboSpeService.cs` line 7: `namespace Services;` instead of `Spe.Bff.Api.Services`
   - Other classes may have similar issues

2. **TODO Comments**: Approximately **26 TODOs** scattered across codebase
   - Some are actionable, others are outdated
   - No tracking or prioritization

3. **Code Style Drift**:
   - No `.editorconfig` to enforce consistent formatting
   - Mixed use of `Results.Ok()` vs `TypedResults.Ok()`
   - Inconsistent null handling patterns

4. **Missing XML Documentation**:
   - Public APIs lack XML doc comments
   - No API documentation generation

5. **Unused Code**:
   - Commented-out code blocks
   - Unused using statements
   - Dead code paths

---

## Goals & Outcomes

### Primary Goals
1. Fix all namespace inconsistencies
2. Review and resolve all TODO comments (fix, track, or remove)
3. Add `.editorconfig` for consistent code style
4. Standardize on `TypedResults` for all minimal API endpoints
5. Add XML documentation for public APIs
6. Remove dead code, commented blocks, unused usings
7. Run code analysis and fix warnings

### Success Criteria
- [ ] All namespaces follow project structure convention
- [ ] Zero TODOs in codebase (resolved, tracked in backlog, or removed)
- [ ] `.editorconfig` in place and enforced
- [ ] All endpoints use `TypedResults` instead of `Results`
- [ ] Public APIs have XML doc comments
- [ ] Zero code analysis warnings
- [ ] Zero unused using statements
- [ ] Code formatted consistently (via `dotnet format`)

### Non-Goals
- Major refactoring (covered in other tasks)
- Performance optimization (Sprint 4+)
- Adding new features (Sprint 4+)

---

## Architecture & Design

### Current State (Sprint 2) - Inconsistent
```
Codebase Issues:
- Namespace: Services; (wrong)
- 26 TODO comments
- No .editorconfig
- Mixed Results vs TypedResults
- No XML docs
- Commented code blocks
- Unused usings
```

### Target State (Sprint 3) - Clean & Consistent
```
Codebase Standards:
- All namespaces follow convention
- Zero active TODOs (tracked elsewhere)
- .editorconfig enforced
- Standardized on TypedResults
- XML docs on public APIs
- No dead code
- Zero analysis warnings
- Formatted via dotnet format
```

---

## Implementation Steps

### Step 1: Fix Namespace Inconsistencies

**Search for Non-Standard Namespaces**:
```bash
# Find files with namespace declarations
rg "^namespace " --type cs -n

# Look for short namespaces (likely wrong)
rg "^namespace [A-Z][a-z]+;" --type cs
```

**Fix OboSpeService.cs** (Line 7):
```csharp
// Before
namespace Services;

// After
namespace Spe.Bff.Api.Services;
```

**Convention**:
- `src/api/Spe.Bff.Api/**/*.cs` → `namespace Spe.Bff.Api.<FolderPath>;`
- `src/shared/Spaarke.Core/**/*.cs` → `namespace Spaarke.Core.<FolderPath>;`
- `src/shared/Spaarke.Dataverse/**/*.cs` → `namespace Spaarke.Dataverse.<FolderPath>;`

---

### Step 2: Audit and Resolve TODO Comments

**Find All TODOs**:
```bash
rg "TODO|FIXME|HACK" --type cs -n src/
```

**Resolution Strategy**:
1. **Actionable Now**: Fix immediately if < 30 minutes
2. **Actionable Later**: Create backlog item, remove TODO
3. **Outdated**: Remove if no longer relevant
4. **Blocked**: Document blocker, create backlog item

**Example Resolutions**:
```csharp
// BEFORE: TODO without tracking
// TODO: Implement proper error handling

// AFTER: Either fix immediately or create backlog item and remove
// See backlog item SDAP-234 for error handling improvements
```

**Create Tracking Document** (optional):
- File: `dev/projects/sdap_project/Sprint 3/TODO-Audit.md`
- List all TODOs with resolution (fixed, tracked, removed)

---

### Step 3: Add .editorconfig for Consistent Formatting

**New File**: `.editorconfig` (root of repository)

```ini
# EditorConfig is awesome: https://EditorConfig.org

# top-most EditorConfig file
root = true

# All files
[*]
charset = utf-8
insert_final_newline = true
trim_trailing_whitespace = true

# Code files
[*.{cs,csx,vb,vbx}]
indent_size = 4
indent_style = space
end_of_line = crlf

# XML project files
[*.{csproj,vbproj,vcxproj,vcxproj.filters,proj,projitems,shproj}]
indent_size = 2

# XML config files
[*.{props,targets,ruleset,config,nuspec,resx,vsixmanifest,vsct}]
indent_size = 2

# JSON files
[*.json]
indent_size = 2

# YAML files
[*.{yml,yaml}]
indent_size = 2

# Markdown files
[*.md]
trim_trailing_whitespace = false

# C# files
[*.cs]

# New line preferences
csharp_new_line_before_open_brace = all
csharp_new_line_before_else = true
csharp_new_line_before_catch = true
csharp_new_line_before_finally = true

# Indentation preferences
csharp_indent_case_contents = true
csharp_indent_switch_labels = true
csharp_indent_labels = flush_left

# Space preferences
csharp_space_after_cast = false
csharp_space_after_keywords_in_control_flow_statements = true
csharp_space_between_method_declaration_parameter_list_parentheses = false
csharp_space_between_method_call_parameter_list_parentheses = false

# Organize usings
dotnet_sort_system_directives_first = true
dotnet_separate_import_directive_groups = false

# Code quality rules
dotnet_code_quality_unused_parameters = all:warning

# Null-checking preferences
csharp_style_throw_expression = true:suggestion
csharp_style_conditional_delegate_call = true:suggestion

# var preferences
csharp_style_var_for_built_in_types = false:suggestion
csharp_style_var_when_type_is_apparent = true:suggestion
csharp_style_var_elsewhere = false:suggestion

# Expression-bodied members
csharp_style_expression_bodied_methods = when_on_single_line:suggestion
csharp_style_expression_bodied_constructors = false:suggestion
csharp_style_expression_bodied_operators = when_on_single_line:suggestion
csharp_style_expression_bodied_properties = when_on_single_line:suggestion

# Pattern matching preferences
csharp_style_pattern_matching_over_is_with_cast_check = true:suggestion
csharp_style_pattern_matching_over_as_with_null_check = true:suggestion

# Null-checking preferences
csharp_style_throw_expression = true:suggestion
csharp_style_conditional_delegate_call = true:suggestion

# Modifier preferences
csharp_preferred_modifier_order = public,private,protected,internal,static,extern,new,virtual,abstract,sealed,override,readonly,unsafe,volatile,async:suggestion

# Expression-level preferences
dotnet_style_object_initializer = true:suggestion
dotnet_style_collection_initializer = true:suggestion
dotnet_style_explicit_tuple_names = true:suggestion
dotnet_style_null_propagation = true:suggestion
dotnet_style_coalesce_expression = true:suggestion
dotnet_style_prefer_is_null_check_over_reference_equality_method = true:suggestion
dotnet_style_prefer_inferred_tuple_names = true:suggestion
dotnet_style_prefer_inferred_anonymous_type_member_names = true:suggestion
dotnet_style_prefer_auto_properties = true:silent
dotnet_style_prefer_conditional_expression_over_assignment = true:silent
dotnet_style_prefer_conditional_expression_over_return = true:silent

# Naming conventions
dotnet_naming_rule.interface_should_be_begins_with_i.severity = warning
dotnet_naming_rule.interface_should_be_begins_with_i.symbols = interface
dotnet_naming_rule.interface_should_be_begins_with_i.style = begins_with_i

dotnet_naming_rule.types_should_be_pascal_case.severity = warning
dotnet_naming_rule.types_should_be_pascal_case.symbols = types
dotnet_naming_rule.types_should_be_pascal_case.style = pascal_case

dotnet_naming_rule.non_field_members_should_be_pascal_case.severity = warning
dotnet_naming_rule.non_field_members_should_be_pascal_case.symbols = non_field_members
dotnet_naming_rule.non_field_members_should_be_pascal_case.style = pascal_case

# Symbol specifications
dotnet_naming_symbols.interface.applicable_kinds = interface
dotnet_naming_symbols.interface.applicable_accessibilities = public, internal, private, protected, protected_internal, private_protected
dotnet_naming_symbols.interface.required_modifiers =

dotnet_naming_symbols.types.applicable_kinds = class, struct, interface, enum
dotnet_naming_symbols.types.applicable_accessibilities = public, internal, private, protected, protected_internal, private_protected
dotnet_naming_symbols.types.required_modifiers =

dotnet_naming_symbols.non_field_members.applicable_kinds = property, event, method
dotnet_naming_symbols.non_field_members.applicable_accessibilities = public, internal, private, protected, protected_internal, private_protected
dotnet_naming_symbols.non_field_members.required_modifiers =

# Naming styles
dotnet_naming_style.begins_with_i.required_prefix = I
dotnet_naming_style.begins_with_i.required_suffix =
dotnet_naming_style.begins_with_i.word_separator =
dotnet_naming_style.begins_with_i.capitalization = pascal_case

dotnet_naming_style.pascal_case.required_prefix =
dotnet_naming_style.pascal_case.required_suffix =
dotnet_naming_style.pascal_case.word_separator =
dotnet_naming_style.pascal_case.capitalization = pascal_case
```

**Apply Formatting**:
```bash
# Format entire solution
dotnet format Spaarke.sln

# Verify formatting
dotnet format Spaarke.sln --verify-no-changes
```

---

### Step 4: Standardize on TypedResults

**Find Non-TypedResults Usage**:
```bash
rg "Results\.(Ok|NotFound|BadRequest|Forbid|Created)" --type cs src/api/
```

**Replace**:
```csharp
// BEFORE
app.MapGet("/api/containers/{id}", (string id) =>
{
    return Results.Ok(container);
});

// AFTER
app.MapGet("/api/containers/{id}", (string id) =>
{
    return TypedResults.Ok(container);
});
```

**Benefits**:
- Type-safe return types
- Better OpenAPI/Swagger documentation
- Compile-time checking of response types

---

### Step 5: Add XML Documentation

**Enable XML Documentation Generation**:

**File**: `src/api/Spe.Bff.Api/Spe.Bff.Api.csproj`

```xml
<PropertyGroup>
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
  <NoWarn>$(NoWarn);1591</NoWarn> <!-- Suppress missing XML doc warnings for now -->
</PropertyGroup>
```

**Add XML Docs to Public APIs**:
```csharp
/// <summary>
/// Creates a new SharePoint Embedded container.
/// </summary>
/// <param name="containerTypeId">The container type GUID from SPE admin center.</param>
/// <param name="displayName">Human-readable container name.</param>
/// <param name="description">Optional container description.</param>
/// <param name="ct">Cancellation token.</param>
/// <returns>Created container DTO or null if creation failed.</returns>
public async Task<ContainerDto?> CreateContainerAsync(
    Guid containerTypeId,
    string displayName,
    string? description = null,
    CancellationToken ct = default)
{
    // Implementation
}
```

**Focus Areas**:
1. Public services (OboSpeService, SpeFileStore, DataverseWebApiService)
2. DTOs and models
3. Extension methods
4. Middleware and filters

---

### Step 6: Remove Dead Code

**Search for Commented Code**:
```bash
rg "^\\s*//.*\{|^\\s*//.*\(" --type cs src/
```

**Search for Unused Usings**:
```bash
# Visual Studio: Right-click solution → Remove Unused Usings
# Or use dotnet tool
dotnet tool install -g dotnet-unused-usings
dotnet unused-usings
```

**Remove**:
- Commented-out code blocks (commit history preserves deleted code)
- Unused using statements
- Unreachable code (dead branches)

---

### Step 7: Run Code Analysis and Fix Warnings

**Enable Analyzers**:

**File**: `Directory.Build.props` (create at solution root)

```xml
<Project>
  <PropertyGroup>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <WarningLevel>5</WarningLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <EnableNETAnalyzers>true</EnableNETAnalyzers>
    <AnalysisLevel>latest</AnalysisLevel>
  </PropertyGroup>
</Project>
```

**Run Analysis**:
```bash
# Build with analysis
dotnet build /p:TreatWarningsAsErrors=true

# Fix common issues
dotnet format analyzers
```

**Common Warnings to Fix**:
- CA1806: Do not ignore method results
- CA2007: Consider calling ConfigureAwait
- CA1031: Do not catch general exception types
- IDE0005: Remove unnecessary using directives

---

## AI Coding Prompts

### Prompt 1: Fix All Namespace Inconsistencies
```
Fix namespace inconsistencies across codebase:

Context:
- OboSpeService.cs line 7: "namespace Services;" (wrong)
- Need consistent namespace structure

Requirements:
1. Search for all namespace declarations: rg "^namespace " --type cs
2. Verify each namespace matches file path
3. Fix OboSpeService.cs: namespace Services; → namespace Spe.Bff.Api.Services;
4. Fix any other mismatched namespaces
5. Follow convention: <ProjectName>.<FolderPath>

Convention:
- src/api/Spe.Bff.Api/ → Spe.Bff.Api.*
- src/shared/Spaarke.Core/ → Spaarke.Core.*
- src/shared/Spaarke.Dataverse/ → Spaarke.Dataverse.*

Search: rg "^namespace [A-Z][a-z]+;" --type cs (find short/wrong namespaces)
```

### Prompt 2: Audit and Resolve All TODOs
```
Audit and resolve all TODO comments:

Context:
- ~26 TODO comments in codebase
- Need to fix, track, or remove each one

Requirements:
1. Find all TODOs: rg "TODO|FIXME|HACK" --type cs -n src/
2. For each TODO:
   - Fix immediately if < 30 minutes
   - Create backlog item and remove TODO
   - Remove if outdated
3. Create TODO-Audit.md documenting resolutions
4. Goal: Zero active TODOs in codebase

Resolution Strategy:
- Actionable now → Fix
- Actionable later → Create SDAP-XXX backlog item, remove TODO
- Outdated → Remove
- Blocked → Document blocker, create backlog item

Output: List of all TODOs with resolution action taken
```

### Prompt 3: Standardize on TypedResults
```
Replace Results.* with TypedResults.* in minimal API endpoints:

Context:
- Current code uses mix of Results and TypedResults
- TypedResults provides type safety and better Swagger docs

Requirements:
1. Find all Results.* usage: rg "Results\\.(Ok|NotFound|BadRequest)" --type cs src/api/
2. Replace with TypedResults.*
3. Verify endpoints still work
4. Update return types if needed (e.g., IResult → Results<Ok<Dto>>)

Examples:
- Results.Ok(data) → TypedResults.Ok(data)
- Results.NotFound() → TypedResults.NotFound()
- Results.BadRequest(error) → TypedResults.BadRequest(error)

Files: All endpoint definitions in src/api/Spe.Bff.Api/
```

### Prompt 4: Add XML Documentation to Public APIs
```
Add XML documentation comments to public APIs:

Context:
- Public services, DTOs, and methods lack documentation
- Need XML docs for API documentation generation

Requirements:
1. Add <summary>, <param>, <returns> tags
2. Focus on public classes and methods
3. Be concise but descriptive
4. Document exceptions if applicable

Priority Files:
- OboSpeService.cs
- SpeFileStore.cs (or refactored classes)
- DataverseWebApiService.cs
- All DTOs in Models/
- Public extension methods

Template:
/// <summary>
/// Brief description of what this does.
/// </summary>
/// <param name="paramName">Description of parameter.</param>
/// <returns>Description of return value.</returns>
```

---

## Testing Strategy

### Validation
1. **Build**: Ensure `dotnet build` succeeds with zero warnings
2. **Format**: Run `dotnet format --verify-no-changes`
3. **Tests**: All tests still pass after changes
4. **Analysis**: Zero code analysis warnings

---

## Validation Checklist

Before marking this task complete, verify:

- [ ] All namespaces fixed and consistent
- [ ] Zero active TODO comments (all resolved/tracked)
- [ ] `.editorconfig` added and applied
- [ ] All endpoints use TypedResults
- [ ] Public APIs have XML documentation
- [ ] Dead code and commented blocks removed
- [ ] Unused usings removed
- [ ] `dotnet format` applied to entire solution
- [ ] Zero build warnings
- [ ] Zero code analysis warnings
- [ ] All tests pass

---

## Completion Criteria

Task is complete when:
1. Codebase consistently formatted
2. All namespaces correct
3. Zero TODOs
4. TypedResults standardized
5. XML docs added
6. Zero warnings
7. Code review approved

**Estimated Completion: 2 days**

---

## Benefits

1. **Maintainability**: Consistent code easier to read and modify
2. **Quality**: Fewer bugs from enforced standards
3. **Onboarding**: New developers follow existing patterns
4. **Documentation**: XML docs generate API documentation
5. **CI/CD**: Automated checks prevent style drift
