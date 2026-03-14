# Code Review Quick Reference

## Security Checks

### Secrets Detection Patterns
```
# Hardcoded credentials
grep -rn "password\s*=" --include="*.cs" --include="*.ts"
grep -rn "apikey\s*=" --include="*.cs" --include="*.ts"
grep -rn "secret\s*=" --include="*.cs" --include="*.ts"

# Connection strings in code
grep -rn "Server=.*Password=" --include="*.cs"
grep -rn "AccountKey=" --include="*.cs"

# Bearer tokens
grep -rn "Bearer [A-Za-z0-9\-_]" --include="*.cs" --include="*.ts"
```

### Input Validation Red Flags
```
# SQL injection vectors
grep -rn "ExecuteSql.*\$" --include="*.cs"
grep -rn "FromSqlRaw.*\+" --include="*.cs"

# Path traversal
grep -rn "Path\.Combine.*Request\." --include="*.cs"
grep -rn "\.\./\.\." --include="*.cs"
```

## Performance Checks

### Blocking Async Patterns
```
# .Result and .Wait() - sync over async
grep -rn "\.Result[^s]" --include="*.cs"
grep -rn "\.Wait(" --include="*.cs"
grep -rn "\.GetAwaiter().GetResult()" --include="*.cs"
```

### Resource Leaks
```
# Missing disposal
grep -rn "new HttpClient(" --include="*.cs"
grep -rn "new SqlConnection(" --include="*.cs"

# Should check for 'using' or IDisposable implementation
```

### Caching Issues (ADR-009)
```
# In-memory cache (should be Redis for cross-request)
grep -rn "IMemoryCache" --include="*.cs"
grep -rn "MemoryCache" --include="*.cs"
```

## Style Checks

### Naming Conventions

**C# (.cs files)**
- Types: `PascalCase` → `AuthorizationService`
- Methods: `PascalCase` → `GetDocuments`
- Private fields: `_camelCase` → `_graphClient`
- Parameters: `camelCase` → `documentId`
- Constants: `PascalCase` → `MaxRetryCount`

**TypeScript (.ts/.tsx files)**
- Components: `PascalCase` → `DataGrid.tsx`
- Functions: `camelCase` → `formatDate`
- Interfaces: `IPascalCase` → `IDataItem`
- Types: `PascalCase` → `ButtonProps`

### Code Smell Patterns
```
# Long methods (>50 lines) - manual review
# Look for methods with many lines between { and }

# Deep nesting (>3 levels)
grep -rn "if.*if.*if.*if" --include="*.cs" --include="*.ts"

# Magic numbers
grep -rn "[^0-9a-zA-Z_][0-9]{3,}[^0-9a-zA-Z_]" --include="*.cs"

# Commented code
grep -rn "^\s*//.*;" --include="*.cs"
grep -rn "^\s*//.*{" --include="*.cs"
```

## ADR Quick Checks

Note: For full ADR validation (including ADR-013+), use `/adr-check` which loads `.claude/skills/adr-check/references/adr-validation-rules.md` and the ADR index in `docs/reference/adr/README-ADRs.md`.

### ADR-001: No Azure Functions
```
grep -rn "Microsoft.Azure.Functions" --include="*.cs" --include="*.csproj"
grep -rn "\[FunctionName" --include="*.cs"
```

### ADR-002: Thin Plugins
```
# Check plugin folder for HttpClient
grep -rn "HttpClient" src/dataverse/plugins/
grep -rn "System.Net.Http" src/dataverse/plugins/
```

### ADR-007: Graph Isolation
```
# Graph types should only be in Infrastructure
grep -rn "Microsoft.Graph" --include="*.cs" | grep -v "Infrastructure"
grep -rn "DriveItem" --include="*.cs" | grep -v "Infrastructure"
```

### ADR-008: Endpoint Filters
```
# Should NOT have global auth middleware
grep -rn "UseAuthorization" src/server/api/Sprk.Bff.Api/ --include="*.cs"
grep -rn "app.Use.*Auth" src/server/api/Sprk.Bff.Api/ --include="*.cs"
```

### ADR-010: DI Minimalism
```
# Count interface registrations
grep -c "services.Add.*<I[A-Z]" src/api/ --include="*.cs"
# Should be ≤15 non-framework interfaces
```

## TypeScript/PCF Specific

### React Anti-Patterns
```
# Missing key in lists
grep -rn "\.map(" --include="*.tsx" -A 2 | grep -v "key="

# Direct state mutation
grep -rn "this.state\." --include="*.tsx"
```

### Fluent UI Version Mixing
```
# v8 imports (should use v9)
grep -rn "@fluentui/react\"" --include="*.ts" --include="*.tsx"

# v9 imports (correct)
grep -rn "@fluentui/react-components" --include="*.ts" --include="*.tsx"
```

### Type Safety
```
# Any types (flag for review)
grep -rn ": any" --include="*.ts" --include="*.tsx"
grep -rn "as any" --include="*.ts" --include="*.tsx"
```

## AI Code Smells

AI-generated code exhibits five distinctive anti-patterns. Check for these during every review.

### Smell 1: Interface with Single Implementation
- [ ] **Check**: Are there interfaces with only one implementing class?
- **ADR**: ADR-010 (DI Minimalism -- register concretes by default)
- **Detection**: `grep -rn "interface I[A-Z]" {file}` then check implementation count
- **Exception**: IAccessDataSource and IAuthorizationRule are the only allowed seams
- **Severity**: Warning

**Example (BAD)**:
```csharp
public interface IDocumentProcessor { Task ProcessAsync(Document doc); }
public class DocumentProcessor : IDocumentProcessor { /* only impl */ }
services.AddSingleton<IDocumentProcessor, DocumentProcessor>();
```
**Fix**: Remove interface, register concrete: `services.AddSingleton<DocumentProcessor>();`

### Smell 2: Try/Catch Log-Rethrow
- [ ] **Check**: Are there catch blocks that only log and rethrow?
- **Detection**: `grep -A 3 "catch.*Exception" {file} | grep -B 1 "throw;"`
- **Severity**: Warning

**Example (BAD)**:
```csharp
try { await _store.GetDocumentAsync(id); }
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to get document {Id}", id);
    throw;  // Redundant -- middleware already logs unhandled exceptions
}
```
**Fix**: Remove try/catch entirely, OR wrap in a domain exception if adding context.

### Smell 3: Null Check on Non-Nullable Type
- [ ] **Check**: Are there null checks on parameters/variables with non-nullable types?
- **Relevant**: C# nullable reference types (NRT) are project-wide enabled
- **Detection**: `grep -n "if.*== *null" {file}` then verify parameter type
- **Severity**: Suggestion

**Example (BAD)**:
```csharp
public async Task<Document> GetAsync(string id)  // string, not string?
{
    if (id == null) throw new ArgumentNullException(nameof(id));
    // Compiler already prevents null here with NRT enabled
}
```
**Fix**: Remove null check. If null IS possible, change type to `string?`.

### Smell 4: Code-Restating Comment
- [ ] **Check**: Do comments restate what the code already says?
- **Detection**: Manual review -- does the comment add information beyond the next line?
- **Severity**: Suggestion

**Example (BAD)**:
```csharp
// Get the document by ID
var document = await _store.GetDocumentAsync(id);

// Return the response
return Ok(response);
```
**Example (GOOD)**:
```csharp
// Graph API returns 404 for soft-deleted items; treat as "not found"
var document = await _store.GetDocumentAsync(id);

// Must return 200 even on empty results -- client polling depends on it
return Ok(response);
```
**Fix**: Remove comment if it restates code. Keep comments that explain WHY, not WHAT.

### Smell 5: Method with More Than Three Responsibilities
- [ ] **Check**: Does any method handle > 3 distinct concerns?
- **Detection**: Method name contains "And"/"Or"/"Also"/"Then", or method body has
  distinct sections (validate, fetch, transform, save, notify, log, error-handle)
- **Severity**: Warning (>3 responsibilities), Critical (>5)

**Example (BAD)**:
```csharp
public async Task<ActionResult> CreateAndProcessDocument(CreateRequest req)
{
    if (!ModelState.IsValid) return BadRequest();           // 1. Validation
    var doc = await _store.CreateAsync(req.ToDocument());   // 2. Create
    var summary = await _aiService.SummarizeAsync(doc);     // 3. AI process
    await _dataverse.UpdateAsync(doc.Id, summary);          // 4. Persist
    await _notifier.NotifyAsync(req.UserId, "Ready");       // 5. Notify
    return Ok(doc);
}
```
**Fix**: Extract into single-responsibility methods. Use background jobs for async chains.

## Review Severity Guide

| Finding | Severity | Example |
|---------|----------|---------|
| Hardcoded secret | 🔴 Critical | `apiKey = "sk-..."` |
| SQL injection vector | 🔴 Critical | `FromSqlRaw($"...{input}")` |
| Missing auth check | 🔴 Critical | Unprotected endpoint |
| Sync-over-async | 🟡 Warning | `.Result` on async call |
| Missing disposal | 🟡 Warning | `new HttpClient()` without using |
| ADR violation | 🟡 Warning | Graph types in API layer |
| Magic number | 🔵 Suggestion | `if (count > 100)` |
| Long method | 🔵 Suggestion | Method >50 lines |
| Missing docs | 🔵 Suggestion | Public API without XML doc |
